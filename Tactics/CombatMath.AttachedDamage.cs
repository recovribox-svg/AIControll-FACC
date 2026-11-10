// ★ v0.3.30 C06R4: author-approved recipient-owned periodic damage; native contracts, no action execution.
using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        internal sealed class AttachedEffect
        {
            internal Buff Buff;
            internal float NextRisk, FutureWorth;
        }
        internal sealed class AttachedSnapshot
        {
            internal readonly List<AttachedEffect> Effects = new List<AttachedEffect>();
            internal float NextRisk, RecentEnemy;
        }
        private static readonly PropertyInfo AttachedNextTick = HarmonyLib.AccessTools.Property(typeof(Buff), "NextTickTime");
        private static readonly PropertyInfo AttachedTickInterval = HarmonyLib.AccessTools.Property(typeof(Buff), "TickTime");
        private static readonly FieldInfo AttachedComponents = HarmonyLib.AccessTools.Field(typeof(EntityFact), "Components");

        // A decision owns these snapshots, including empty/failed reads. Neither positions nor the caster's continued
        // presence are part of the key: a target-owned buff remains until its own native removal/expiry.
        internal static AttachedSnapshot AttachedDamageOf(TacticalContext ctx, UnitEntityData victim)
        {
            var cache = ctx.AttachedDamage ?? (ctx.AttachedDamage = new Dictionary<UnitEntityData, AttachedSnapshot>());
            if (cache.TryGetValue(victim, out var known)) return known;
            var result = new AttachedSnapshot { RecentEnemy = FightStats.RecentWorstAttackTaken(victim, ctx.Round) }; cache[victim] = result;
            if (victim?.Buffs == null) return result;
            TimeSpan now;
            try { now = Game.Instance.TimeController.GameTime; }
            catch { return result; }
            double horizon = Math.Min(AfflictionRounds, Math.Max(1f, ctx.ExpectedRounds)) * ValueModel.RealTimeRoundSeconds;
            foreach (var buff in victim.Buffs.Enumerable)
            {
                if (buff == null || !buff.Active || !buff.IsEnabled || buff.IsSuppressed) continue;
                var plan = PeriodicPlanOf(buff.Blueprint);
                if (plan == null || !plan.Supported)
                {
                    if (plan?.Unsupported != null && CombatMemory.FirstThisFight("attached-contract|" + buff.Blueprint.AssetGuid))
                        Main.Verbose($"[Attached] {buff.Blueprint.name}: unsupported periodic contract ({plan.Unsupported})");
                    continue;
                }
                try
                {
                    var effect = ReadAttachedEffect(buff, plan, victim, now, horizon);
                    if (effect == null) continue;
                    result.Effects.Add(effect); result.NextRisk += effect.NextRisk;
                    if (CombatMemory.FirstThisFight("attached|" + victim.UniqueId + "|" + buff.Blueprint.AssetGuid))
                        Main.Verbose($"[Attached] {victim.CharacterName}: {buff.Blueprint.name}, next-round risk {effect.NextRisk:0.#}, " +
                                     $"removable future harm {effect.FutureWorth:0.#}; follows recipient, not caster position");
                }
                catch (Exception ex)
                {
                    if (CombatMemory.FirstThisFight("attached-unread|" + victim.UniqueId + "|" + buff.Blueprint.AssetGuid))
                        Main.Verbose($"[Attached] {victim.CharacterName}: {buff.Blueprint.name} unread ({ex.GetType().Name})");
                }
            }
            return result;
        }

        private static bool AttachedContext(MechanicsContext source)
        {
            var ability = source?.SourceAbilityContext?.Ability;
            return source?.Params != null && ability?.MagicHackData == null && ability?.ShadowSpellSettings == null &&
                   !source.HasMetamagic(Metamagic.Bolstered);
        }
        internal static bool IsAttachedDamagePacket(RuleDealDamage damage)
        {
            try
            {
                if (damage?.IsDot != true || !AttachedContext(damage.Reason?.Context)) return false;
                var source = damage.Reason.Context;
                var plan = PeriodicPlanOf(source.AssociatedBlueprint as BlueprintBuff);
                // These contracts have no activation/off/unknown damage trigger: HP packets belong to their round actions.
                if (plan?.Supported != true || !plan.HitPoints || damage.Target?.Buffs == null) return false;
                foreach (var dice in plan.DiceInputs)
                    if (AttachedValue(dice.DiceCountValue, source) < 0 || AttachedValue(dice.BonusValue, source) < 0) return false;
                foreach (var buff in damage.Target.Buffs.Enumerable)
                    if (ReferenceEquals(buff.Context, source))
                        return ReadAttachedTicks(buff, Game.Instance.TimeController.GameTime, ValueModel.RealTimeRoundSeconds, out _, out _);
            }
            catch { }
            return false; // An unread/removed instance does not silently disappear from observed offence calibration.
        }

        private static float AttachedValue(ContextValue value, MechanicsContext source) =>
            value.ValueType == ContextValueType.Simple ? value.Value : source[value.ValueRank];

        private static float AttachedDice(ContextDiceValue dice, MechanicsContext source, bool metamagic)
        {
            return ValueModel.PeriodicDiceMean((int)AttachedValue(dice.DiceCountValue, source), dice.DiceType.Sides(),
                (int)AttachedValue(dice.BonusValue, source), metamagic && source.HasMetamagic(Metamagic.Maximize),
                metamagic && source.HasMetamagic(Metamagic.Empower));
        }

        private static float AttachedSave(MechanicsContext source, UnitEntityData victim, Kingmaker.EntitySystem.Stats.SavingThrowType type, int dc, bool persistent)
        {
            float fail = SaveFailChance(source, type, victim, dc);
            return persistent && source.HasMetamagic(Metamagic.Persistent) ? 1f - (1f - fail) * (1f - fail) : fail;
        }

        private static Affliction AttachedDeal(ContextActionDealDamage deal, MechanicsContext source, UnitEntityData victim)
        {
            var harm = new Affliction();
            float amount = AttachedDice(deal.Value, source, true);
            var kind = KindOf(deal);
            if (kind == DealKind.HitPoints) harm.HitPoints = AfterTargetReduction(victim, deal, amount);
            else if (kind == DealKind.AbilityDamage && !AbilityDamageImmune(victim, deal.AbilityType, false)) harm.AddPoints(deal.AbilityType, amount);
            else if (kind == DealKind.EnergyDrain && !EnergyDrainImmune(victim)) harm.NegativeLevels = amount;
            return harm;
        }

        private static Affliction AttachedApplied(ContextActionApplyBuff apply, MechanicsContext source, UnitEntityData victim)
        {
            var harm = new Affliction();
            if (BuffBlockedFor(victim, apply.Buff)) return harm;
            if (apply.Buff.Stacking != StackingType.Stack && apply.Buff.Stacking != StackingType.Replace &&
                victim.Buffs.HasFact(apply.Buff)) return harm; // Native Ignore/Poison/etc. do not run a fresh activation dose.
            foreach (var component in apply.Buff.ComponentsArray)
            {
                if (component is BuffPoisonStatDamage poison && !AbilityDamageImmune(victim, poison.Stat, false))
                    harm.AddPoints(poison.Stat, ValueModel.PeriodicDiceMean(poison.Value.Rolls, poison.Value.Dice.Sides(), poison.Bonus,
                        source.HasMetamagic(Metamagic.Maximize), source.HasMetamagic(Metamagic.Empower))); // Native OnActivate: one dose, no save.
            }
            // The child buff's future ticks are not duplicated here. Only its active instance supplies those ticks.
            return harm;
        }

        internal static bool AttachedApplicationHeld(UnitEntityData victim, BlueprintBuff buff) => victim?.Buffs != null &&
            buff?.Stacking == StackingType.Ignore && PeriodicPlanOf(buff)?.Supported == true && victim.Buffs.HasFact(buff);

        // HP is never capped to current HP: a nearly down or unconscious companion still faces the full next packet.
        private static float AttachedRisk(Affliction harm, UnitEntityData victim)
        {
            float risk = Math.Max(0f, harm.HitPoints), hp = Math.Max(1f, HpBeforeDown(victim));
            foreach (var stat in harm.Points) risk += ValueModel.AbilityDamageEquivalent(stat.Value, AbilityScore(victim, stat.Key), hp);
            if (harm.NegativeLevels > 0f) risk += ValueModel.EnergyDrainEquivalent(harm.NegativeLevels, LevelsBeforeDrainDeath(victim), hp);
            return risk;
        }

        private static AttachedEffect ReadAttachedEffect(Buff buff, PeriodicPlan plan, UnitEntityData victim, TimeSpan now, double horizon)
        {
            var source = buff.Context;
            if (!AttachedContext(source) || AttachedNextTick == null || AttachedTickInterval == null ||
                plan.Round != null && source.MaybeCaster == null)
            {
                if (CombatMemory.FirstThisFight("attached-context|" + victim.UniqueId + "|" + buff.Blueprint.AssetGuid))
                    Main.Verbose($"[Attached] {buff.Blueprint.name}: unread context/caster or unsupported spell modifiers");
                return null;
            }
            if (!ReadAttachedTicks(buff, now, horizon, out int soon, out int later)) return null;
            if (later <= 0) return null;
            var effect = new AttachedEffect { Buff = buff };
            if (plan.Round != null)
            {
                var tick = ReadPeriodicTick(plan.Round,
                    save => AttachedSave(source, victim, save.Type, save.HasCustomDC ? (int)AttachedValue(save.CustomDC, source) : source.Params.DC, true),
                    deal => AttachedDeal(deal, source, victim), apply => AttachedApplied(apply, source, victim));
                float risk = AttachedRisk(tick.Harm, victim);
                effect.NextRisk = risk * ValueModel.PeriodicWeight(soon, tick.Survives);
                // New credit is damage actually avoided. Existing control/cleanse appraisal stays separate: do not
                // invent repeated control rounds for an already held condition or overlapping short ticks.
                effect.FutureWorth = risk * ValueModel.PeriodicWeight(later, tick.Survives);
            }
            else
            {
                var data = ReadAttachedPoisonState(buff, plan.Poison);
                if (data != null)
                {
                    var poison = plan.Poison;
                    if (poison.NoEffectOnFirstTick && data.TicksPassed == 0) return effect;
                    int left = Math.Max(0, poison.Ticks - data.TicksPassed), saves = Math.Max(0, poison.SuccesfullSaves - data.SavesSucceeded);
                    if (data.ContextValue != null && !PeriodicDice(data.ContextValue)) return null;
                    float amount = data.ContextValue == null ? ValueModel.PeriodicDiceMean(poison.Value.Rolls, poison.Value.Dice.Sides(), poison.Bonus,
                        source.HasMetamagic(Metamagic.Maximize), source.HasMetamagic(Metamagic.Empower)) : AttachedDice(data.ContextValue, source, true);
                    var harm = new Affliction();
                    if (!AbilityDamageImmune(victim, poison.Stat, false)) harm.AddPoints(poison.Stat, amount);
                    float fail = AttachedSave(source, victim, poison.SaveType, source.Params.DC + data.BonusDC, false);
                    var weakness = poison.WeaknessForPoison?.Get();
                    bool weak = weakness != null && victim.Buffs.HasFact(weakness);
                    float risk = AttachedRisk(harm, victim);
                    effect.NextRisk = risk * ValueModel.ActivePoisonDoses(fail, Math.Min(soon, left), saves, weak);
                    effect.FutureWorth = risk * ValueModel.ActivePoisonDoses(fail, Math.Min(later, left), saves, weak);
                    return effect;
                }
                return null; // Missing runtime counters must not turn into a fresh poison.
            }
            return effect;
        }

        internal static bool ReadAttachedTicks(Buff buff, TimeSpan now, double horizon, out int soon, out int later)
        {
            soon = later = 0;
            if (AttachedNextTick == null || AttachedTickInterval == null) return false;
            double next = ((TimeSpan)AttachedNextTick.GetValue(buff)).TotalSeconds - now.TotalSeconds;
            double interval = ((TimeSpan)AttachedTickInterval.GetValue(buff)).TotalSeconds;
            // Native BlueprintBuff intervals start at one second. A faster modded cadence is a different contract,
            // not permission for millions of preview iterations or for an invented slower tick rate.
            if (interval < 1d) return false;
            double remaining = buff.IsPermanent ? double.PositiveInfinity : (buff.EndTime - now).TotalSeconds;
            soon = ValueModel.PeriodicTicks(next, interval, remaining, ValueModel.RealTimeRoundSeconds);
            later = ValueModel.PeriodicTicks(next, interval, remaining, horizon);
            return true;
        }

        internal static BuffPoisonStatDamageData ReadAttachedPoisonState(Buff buff, BuffPoisonStatDamage poison)
        {
            var runtimes = AttachedComponents?.GetValue(buff) as List<EntityFactComponent>;
            if (runtimes == null) return null;
            foreach (var runtime in runtimes)
                if (runtime.SourceBlueprintComponent == poison && runtime.TryGetData<BuffPoisonStatDamageData>(out var data)) return data;
            return null; // Native TryGetData reads MaybeData; never create/reset poison counters.
        }
    }
}
