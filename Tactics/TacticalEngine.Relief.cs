// ★ coverage5 (author request; docs/RELIEF_SPELLS.md): relief for an ally an effect hinders - confused, frightened, held,
// shaken, dominated - and the spells that end it (a dispel, a removal) or switch it off (a suppression).
// Evidence: a user of the stable 0.3.52 reports "none of the magic-using characters ever cast Dispel Magic". The cause: a
// dispel aimed at anyone was worth the flat 2-point "dispel" fallback, once a fight, whatever it took off; Break
// Enchantment only counted what was on its caster; Remove Fear and Unbreakable Heart were valued only by their save bonus
// (their SuppressBuffs unread).
// Native contracts (supplied assembly and exports):
//  - ContextActionDispelMagic tries the target's buffs by caster level, highest first, up to its count; RuleDispelMagic has
//    no protection for a friendly buff, so a dispel on an ally can take off the party's own buffs (Haste, Heroism) first;
//  - Break Enchantment: every Enchantment and Transmutation effect up to 5th level on each ally within 30 ft, no count;
//  - Remove Fear's buff suppresses Shaken and Frightened; Unbreakable Heart's suppresses Fear, Shaken, Frightened,
//    Confusion, Compulsion and negative emotion (and so Dominate). A new suppressor re-checks the buffs already on its
//    bearer (SuppressBuffs.OnActivate -> UnitPartBuffSuppress.ApplyChanges), by the buff context's descriptor and school;
//  - Confusion: a status condition with no round save, for its rank in rounds.
// The worth of ending an effect is what it takes from the ally (its control or penalty share, OwnDebuffShare) times the
// ally's output per round, for the rounds it would still last (its time left, its round re-save, its suppression), within
// three rounds; at most the ally's whole output. Valuation only: no buff, context or rule is run.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using UnityEngine;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>ContextActionDispelMagic branches on its actual recipient: a point without a unit selector
        /// removes area entities, never buffs on the unit used to score that point. Greater Area retains its
        /// AbilityTargetsAround unit branch (and separately has a clicked-point action).</summary>
        internal static bool DispelTargetsUnits(AbilityProfile profile)
        {
            var blueprint = profile?.Blueprint;
            if (blueprint == null) return false;
            bool pointOnly = blueprint.CanTargetPoint && !blueprint.CanTargetSelf &&
                             !blueprint.CanTargetFriends && !blueprint.CanTargetEnemies;
            return RemovalRules.CanDispelUnit(pointOnly, profile.SelectsTargets, profile.Fusion != null);
        }

        /// <summary>Helpful caster buffs explicitly spent by a removal, apart from the held states already charged
        /// by SpentStateCost. This is lost output, not relief; Deactivated callbacks are not speculatively credited.</summary>
        private static float CasterRemovalCost(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (profile.CasterRemovedBuffs.Count == 0 || ctx.Unit?.Buffs == null) return 0f;
            float horizon = ReliefHorizon(ctx), cost = 0f;
            foreach (var blueprint in profile.CasterRemovedBuffs)
            {
                var buff = ctx.Unit.Buffs.GetBuff(blueprint);
                if (buff == null || IsHarmfulOn(ctx.Unit, buff) || profile.CasterBuffs.Contains(blueprint) ||
                    (target == ctx.Unit && profile.TargetBuffs.Contains(blueprint)) || IsHeldState(ctx, ability, profile, buff)) continue;
                float rounds = buff.IsPermanent ? horizon : (float)buff.TimeLeft.TotalSeconds / 6f;
                float suppressed = buff.IsSuppressed ? BuffSuppression.SuppressedSeconds(buff) / 6f : 0f;
                cost += RemovalRules.HelpfulLoss(EnemyBuffShare(buff, ctx.Unit), AllyPerRound(ctx.Unit), rounds, suppressed, horizon);
            }
            return cost;
        }

        /// <summary>
        /// ★ coverage7 follow-ups: the heal a caster-buff removal gives its caster: each removed buff's Deactivated actions
        /// (AddFactContextActions, run when the buff goes), plain ContextActionHealTarget with simple dice only, valued as
        /// any heal (HealGain). Vermlek's Abandon Flesh ends its Flesh Armor for 2d6+3; the game's own AI uses it only when
        /// seriously hurt (Vermlek_AiAction_AbandonFlesh: HealthConsideration 20/10).
        /// </summary>
        private static float CasterRemovalHeal(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                               out float healed)
        {
            healed = 0f;
            var caster = ctx.Unit;
            if (profile.CasterRemovedBuffs.Count == 0 || caster?.Buffs == null) return 0f;
            if (!caster.Descriptor.State.IsConscious) return 0f;
            float total = 0f;
            foreach (var blueprint in profile.CasterRemovedBuffs)
            {
                var active = caster.Buffs.GetBuff(blueprint);
                if (active == null || !active.Active || !active.IsTurnedOn || active.IsSuppressed ||
                    !profile.CertainlyRemovesCasterBuff(blueprint, ability, target)) continue;
                total += DeactivationHeal(blueprint);
            }
            float missing = caster.MaxHP - caster.HPLeft;
            healed = Math.Min(total, missing);
            if (!(healed > 0f)) return 0f;
            float gain = HealGain(ctx, ability, caster, healed, out _);
            return gain > 0f ? gain : 0f;
        }

        /// <summary>The average heal of a buff's Deactivated actions (simple dice only; anything else reads as no heal).</summary>
        internal static float DeactivationHeal(BlueprintBuff buff)
        {
            float total = 0f;
            if (buff == null) return 0f;
            foreach (var component in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions>(buff))
            {
                if (component.GetType() != typeof(Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions) ||
                    component.Dispose?.Actions?.Length > 0) return 0f;
                var actions = component?.Deactivated?.Actions;
                if (actions == null) continue;
                foreach (var action in actions)
                {
                    if (action == null || action.GetType() != typeof(ContextActionHealTarget)) return 0f;
                    var heal = (ContextActionHealTarget)action;
                    if (heal.Value == null) return 0f;
                    var dice = heal.Value;
                    if (dice.DiceCountValue == null || dice.DiceCountValue.ValueType != ContextValueType.Simple ||
                        dice.BonusValue == null || dice.BonusValue.ValueType != ContextValueType.Simple) return 0f;
                    total += CombatMath.DiceAverage(dice);
                }
            }
            return total;
        }

        /// <summary>The rounds relief is counted for: as a cleanse's, the fight's expected rounds, at most three.</summary>
        private static float ReliefHorizon(TacticalContext ctx) => Math.Min(Math.Max(1f, ctx.ExpectedRounds), 3f);

        /// <summary>An ally's output for a round (its worth's base), half for a summon.</summary>
        private static float AllyPerRound(UnitEntityData ally) =>
            ValueModel.PerRound(Level(ally)) * (IsSummon(ally) ? ValueModel.SummonFactor : 1f);

        /// <summary>An effect working against its bearer: given by one of its enemies, or flagged harmful.</summary>
        internal static bool IsHarmfulOn(UnitEntityData bearer, Buff buff)
        {
            try
            {
                var giver = buff?.MaybeContext?.MaybeCaster;
                return giver != null && giver.IsEnemy(bearer) || (buff?.Blueprint?.Harmful ?? false);
            }
            catch { return false; }
        }

        /// <summary>
        /// What <paramref name="buff"/> costs <paramref name="ally"/>: its control or penalty share of the ally's output a round,
        /// for the rounds it would still act within the horizon (and at most <paramref name="capRounds"/>, a suppressor's time).
        /// </summary>
        internal static float HarmWorth(TacticalContext ctx, UnitEntityData ally, Buff buff, float horizon, float capRounds = float.PositiveInfinity)
        {
            try
            {
                if (ally?.Descriptor?.State == null || !ally.Descriptor.State.IsConscious || !IsHarmfulOn(ally, buff)) return 0f;
                float share = OwnDebuffShare(ctx, buff, ally);
                if (!(share > 0f)) return 0f;
                float rounds = buff.IsPermanent ? horizon : Math.Min(horizon, (float)buff.TimeLeft.TotalSeconds / 6f);
                if (buff.IsSuppressed) rounds = SuppressionRules.ActiveRounds(rounds, BuffSuppression.SuppressedSeconds(buff) / 6f, horizon);
                rounds = Math.Min(rounds, capRounds);
                if (!(rounds > 0f)) return 0f;
                return Math.Min(1f, share) * AllyPerRound(ally) * ValueModel.HeldRounds(rounds, RoundSavePass(buff, ally));
            }
            catch { return 0f; }
        }

        /// <summary>Removing harmful effects by name or descriptor (Remove Paralysis, Optimistic Smile...): what they cost the ally.</summary>
        private static float RemovalRelief(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData ally, float horizon)
        {
            if (!profile.RemovesAnything || ally?.Buffs == null || !DispelTargetsUnits(profile)) return 0f;
            float total = 0f;
            foreach (var buff in profile.BuffsRemovedFrom(ability, ally, certainOnly: true))
                total += HarmWorth(ctx, ally, buff, horizon);
            return Math.Min(total, AllyPerRound(ally) * horizon);
        }

        private static readonly Dictionary<BlueprintBuff, bool> s_Suppressors = new Dictionary<BlueprintBuff, bool>();

        /// <summary>The ability puts a buff on its recipient that suppresses other effects (cached per buff).</summary>
        internal static bool SuppressesOnRecipient(AbilityProfile profile)
        {
            if (profile?.TargetBuffs == null || profile.TargetBuffs.Count == 0) return false;
            foreach (var buff in profile.TargetBuffs)
            {
                if (buff == null) continue;
                if (!s_Suppressors.TryGetValue(buff, out bool suppresses))
                {
                    try { suppresses = buff.GetComponent<SuppressBuffs>() != null; } catch { suppresses = false; }
                    if (s_Suppressors.Count > 2048) s_Suppressors.Clear();
                    s_Suppressors[buff] = suppresses;
                }
                if (suppresses) return true;
            }
            return false;
        }

        /// <summary>Whether a SuppressBuffs component switches off <paramref name="buff"/> (UnitPartBuffSuppress.IsSuppressed).</summary>
        private static bool SuppressorHits(SuppressBuffs component, Buff buff)
        {
            if (component == null || buff?.Blueprint == null) return false;
            if (component.ExcludedBuffs.Contains(buff.Blueprint)) return false;
            if (component.Buffs.Contains(buff.Blueprint)) return true;
            var context = buff.MaybeContext;
            if (context == null) return false;
            if (component.Descriptor.Value != SpellDescriptor.None && (component.Descriptor.Value & context.SpellDescriptor) != SpellDescriptor.None)
                return true;
            return component.Schools != null && Array.IndexOf(component.Schools, context.SpellSchool) >= 0;
        }

        /// <summary>
        /// The buffs <paramref name="ability"/> puts on <paramref name="recipient"/> that suppress effects already on it (Remove
        /// Fear, Unbreakable Heart): the harm they switch off, for as long as the suppressor lasts; on an ally under an enemy's
        /// control, a suppressed hold frees it for that time (FreedWorth).
        /// </summary>
        private static float SuppressionRelief(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData recipient,
                                               float horizon, out bool frees)
        {
            frees = false;
            if (recipient?.Buffs == null || profile.TargetBuffs.Count == 0) return 0f;
            bool turned = ctx.TurnedAllies.Contains(recipient);
            float total = 0f;
            MechanicsContext durationContext = null;
            foreach (var occurrence in profile.BuffApplications)
            {
                var applied = occurrence.Buff;
                if (applied == null || !profile.TargetBuffs.Contains(applied)) continue;
                var suppressors = applied.GetComponents<SuppressBuffs>();
                bool any = false;
                foreach (var _ in suppressors) { any = true; break; }
                if (!any) continue;
                var applies = occurrence.Applies(ability, recipient);
                if (applies == EffectApplies.No || applies == EffectApplies.Unknown || CombatMath.BuffImmune(recipient, applied, ability)) continue;
                float lasts = horizon;
                try
                {
                    if (!TryFusionApplicationRounds(ability, occurrence, recipient, ref durationContext, out lasts)) continue;
                }
                catch { continue; }
                float part = 0f;
                foreach (var buff in recipient.Buffs.Enumerable)
                {
                    if (buff == null || buff.IsSuppressed) continue;   // already off: nothing to gain
                    bool hit = false;
                    foreach (var component in suppressors) if (SuppressorHits(component, buff)) { hit = true; break; }
                    if (!hit) continue;
                    if (turned && HoldsUnderControl(buff)) { part += FreedWorth(ctx, recipient, buff, lasts); frees = true; }
                    else if (!turned) part += HarmWorth(ctx, recipient, buff, horizon, lasts);
                }
                total = Math.Max(total, part);   // one occurrence of the same buff stands for the cast
            }
            return turned ? total : Math.Min(total, AllyPerRound(recipient) * horizon);
        }

        /// <summary>
        /// ★ coverage5: an ordinary dispel (Dispel Magic, Greater Dispel Magic, Break Enchantment) on every unit it reaches, in the
        /// game's own order and count (DispelValue): an enemy's buffs taken off are its loss; on an ally, a harmful effect taken
        /// off is relief and a buff the party gave it is a loss. Allies under an enemy's control are FreeingValue's. False when
        /// no dispel action of the ability can be read (the old fallback then stays).
        /// </summary>
        private static bool TryDispelValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, TargetWrapper actualAim,
                                           List<UnitEntityData> affectedEnemies, List<UnitEntityData> affectedAllies,
                                           out float value, out string note, out float outsideItemRestraint)
        {
            value = 0f;
            note = null;
            outsideItemRestraint = 0f;
            if (profile.DispelActions.Count == 0) return false;
            // Its native area branch is not a unit-buff fallback. Treat the absent unit benefit as known zero,
            // rather than allowing a scoring anchor's buffs to create a fictitious 2-point dispel option.
            if (!DispelTargetsUnits(profile))
            {
                // ★ coverage7 follow-ups: the native point branch ends the area effects at the point (PointDispelValue).
                value = PointDispelValue(ctx, ability, profile, actualAim, out note);
                outsideItemRestraint = value;
                return true;
            }
            if (profile.Fusion == null && !profile.SelectsTargets && actualAim?.Unit == null) return true;
            bool readable = false;
            foreach (var dispel in profile.DispelActions)
                if (DispelUnsupported(dispel) == null || IsAutomaticDispel(dispel)) { readable = true; break; }
            if (!readable) return false;
            float horizon = ReliefHorizon(ctx);
            var units = new List<UnitEntityData>();
            foreach (var u in affectedEnemies) if (!units.Contains(u)) units.Add(u);
            foreach (var u in affectedAllies) if (!units.Contains(u)) units.Add(u);
            float gain = 0f, loss = 0f;
            int helped = 0, hurt = 0;
            foreach (var u in units)
            {
                if (u == null || ctx.TurnedAllies.Contains(u)) continue;
                if (u.Buffs == null || !u.Buffs.Enumerable.Any()) continue; // no context/rank construction for an empty kit
                MechanicsContext source = null;
                try { source = CombatMath.CreateContext(ability, new TargetWrapper(u)); } catch { }
                float bestGain = 0f, ownLoss = 0f;
                bool tried = false;
                CombatMath.FusionDamageRead damageRead = null;
                foreach (var dispel in profile.DispelActions)
                {
                    var applies = profile.AppliesTo(dispel, ability, u);
                    if (applies == EffectApplies.No) continue;
                    float worth = DispelValue(ctx, dispel, source, u, horizon, true, true, out _, out float actionLoss);
                    float actionGain = applies == EffectApplies.Unknown ? 0f : worth + actionLoss;
                    if (actionGain > 0f)
                        bestGain = Math.Max(bestGain, actionGain * DispelEffectChance(ctx, ability, profile, dispel, u, true, ref damageRead));
                    if (actionLoss > 0f)
                        ownLoss += actionLoss * DispelEffectChance(ctx, ability, profile, dispel, u, false, ref damageRead);
                    tried = true;
                }
                if (!tried) continue;
                // Sequential dispels may overlap. Do not invent independent full benefits or discard the losses of
                // an earlier action by selecting a later positive net result. One action is exact; several use the
                // best individual benefit minus all priced losses (a deliberately conservative overlap estimate).
                // Keep present harm relief and lost helpful buffs outside the consumable restraint.
                outsideItemRestraint += (ctx.Unit.IsEnemy(u) ? 0f : bestGain) - ownLoss;
                float best = bestGain - ownLoss;
                if (best == 0f) continue;
                if (best > 0f) { gain += best; helped++; } else { loss -= best; hurt++; }
            }
            value = gain - loss;
            note = loss > 0f ? $"dispel +{gain:0.#} ({helped}) -{loss:0.#} ({hurt} lose own buffs)" : $"dispel +{gain:0.#} ({helped})";
            return true;
        }

        /// <summary>
        /// ★ coverage7 follow-ups (Codex's review of the coverage7 playtest, finding 1): a point dispel
        /// (ContextActionDispelMagic with no target unit) ends area effects, not buffs. Native contract
        /// (ContextActionDispelMagic.RunAction / TryDispelAreaEffect): every area in the game's list, in its order, whose view
        /// contains the point (not AllArea), with a caster and a source ability, passing the dispel's schools and descriptor
        /// (and only an enemy's with OnlyEnemyAreaEffects), rolls RuleDispelMagic against 11 + the area's caster level (its DC
        /// for CheckType.DC); it stops after its count (one for Dispel Magic). Each area is worth what it does to the units
        /// inside over the rounds it still lasts within the relief horizon: relief for the party's (AllyPerRound), a loss for
        /// the enemies' (ControlWorth), whoever cast it (AreaRemovalWorth). Ordered and counted as unit dispels are
        /// (ValueModel.ExpectedDispelWorth). Babau cast it three times at an enemy's feet for the old 2-point fallback.
        /// </summary>
        private static float PointDispelValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, TargetWrapper aim,
                                              out string note)
        {
            note = null;
            Vector3 point;
            try { if (aim == null) return 0f; point = aim.Point; } catch { return 0f; }
            var areas = Kingmaker.Game.Instance?.State?.AreaEffects;
            if (areas == null || ctx.Unit == null) return 0f;
            float horizon = ReliefHorizon(ctx);
            int casterLevel = CombatMath.ParamsOf(ability)?.CasterLevel ?? Level(ctx.Unit);
            float bestGain = 0f, lost = 0f;
            int reached = 0;
            var tried = new List<KeyValuePair<float, float>>();
            foreach (var dispel in profile.DispelActions)
            {
                bool automatic = IsAutomaticDispel(dispel) && !(dispel.OnSuccess?.Actions?.Length > 0) && !(dispel.OnFail?.Actions?.Length > 0);
                if (!automatic && DispelUnsupported(dispel) != null) continue;
                if (DispelStopField == null || DispelCountField == null || DispelCheckField == null ||
                    DispelUseMaxCasterField == null || DispelMaxCasterField == null) continue;
                int level = casterLevel;
                var maxCaster = (ContextValue)DispelMaxCasterField.GetValue(dispel);
                if ((bool)DispelUseMaxCasterField.GetValue(dispel))
                {
                    if (maxCaster == null || !maxCaster.IsValueSimple) continue;   // a context-ranked cap is not read here
                    level = Math.Min(level, maxCaster.Value);
                }
                // Native TryDispelAreaEffect adds CheckBonus only; ContextBonus is a buff-dispel branch input.
                int bonus = dispel.CheckBonus;
                var check = (RuleDispelMagic.CheckType)DispelCheckField.GetValue(dispel);
                var count = (ContextValue)DispelCountField.GetValue(dispel);
                if ((bool)DispelStopField.GetValue(dispel) && (count == null || !count.IsValueSimple)) continue;
                int stopAfter = (bool)DispelStopField.GetValue(dispel) ? Math.Max(1, count.Value) : 0;
                tried.Clear();
                int scanned = 0;
                foreach (var aoe in areas)
                {
                    if (++scanned > AreaQuoteRules.MaxAreas) { note = "point dispel: area budget exceeded"; return 0f; }
                    if (aoe?.Blueprint == null || aoe.Blueprint.Shape == AreaEffectShape.AllArea) continue;
                    if (!AoeDangerAnalyzer.DispelCoversPoint(aoe, point)) continue;
                    var context = aoe.Context;
                    var areaCaster = context?.MaybeCaster;
                    if (areaCaster == null || !(bool)context.SourceAbility || !DispelSchoolsMatch(dispel, context)) continue;
                    if (dispel.OnlyEnemyAreaEffects && !areaCaster.IsEnemy(ctx.Unit)) continue;
                    int dc = check == RuleDispelMagic.CheckType.DC ? context.Params.DC : 11 + context.Params.CasterLevel;
                    float chance = automatic ? 1f : ValueModel.DispelChance(dc - level - bonus);
                    float worth = AreaRemovalWorth(ctx, aoe, horizon);
                    if (float.IsNaN(worth) || float.IsInfinity(worth)) { note = "point dispel: area worth unread"; return 0f; }
                    tried.Add(new KeyValuePair<float, float>(chance, worth));
                }
                if (tried.Count == 0) continue;
                reached += tried.Count;
                float net = ValueModel.ExpectedDispelWorth(tried, stopAfter, dispel.OneRollForAll, out float loss);
                // Several dispel actions can end the same area only once. Keep one gain, retain every priced loss.
                bestGain = Math.Max(bestGain, net + loss);
                lost += loss;
            }
            if (reached == 0) { note = "point dispel: no area to end there"; return 0f; }
            float total = bestGain - lost;
            note = lost > 0f ? $"ends areas {total + lost:+0.#;-0.#} -{lost:0.#} ({reached} there)" : $"ends areas {total:+0.#;-0.#} ({reached} there)";
            return total;
        }

        /// <summary>
        /// What ending <paramref name="aoe"/> is worth now: for each unit inside, its danger (AoeDangerAnalyzer.AreaDangerOn)
        /// for the rounds the area still lasts within <paramref name="horizon"/>, a relief for the party (AllyPerRound) and a
        /// loss for the enemies (ControlWorth). An ally under an enemy's control counts as neither.
        /// </summary>
        private static float AreaRemovalWorth(TacticalContext ctx, AreaEffectEntityData aoe, float horizon)
        {
            if (ctx.AreaRemovalValues == null) ctx.AreaRemovalValues = new Dictionary<AreaEffectEntityData, float>();
            if (ctx.AreaRemovalVersion != CombatMath.ForecastVersion)
            {
                ctx.AreaRemovalValues.Clear();
                ctx.AreaRemovalVersion = CombatMath.ForecastVersion;
            }
            if (!ctx.AreaRemovalValues.TryGetValue(aoe, out float worth))
                ctx.AreaRemovalValues[aoe] = worth = AreaRemovalWorthCore(ctx, aoe, horizon);
            return worth;
        }

        private static float AreaRemovalWorthCore(TacticalContext ctx, AreaEffectEntityData aoe, float horizon)
        {
            float rounds = Math.Min(horizon, AoeDangerAnalyzer.RoundsLeft(aoe));
            if (!(rounds > 0f)) return 0f;
            float worth = 0f;
            try
            {
                var inside = aoe.InGameUnitsInside;
                if (inside == null) return 0f;
                int scanned = 0;
                foreach (var unit in inside)
                {
                    if (++scanned > AreaQuoteRules.MaxRecipients) return float.NaN;
                    if (unit?.Descriptor?.State?.IsConscious != true || ctx.TurnedAllies.Contains(unit)) continue;
                    float danger = AoeDangerAnalyzer.AreaDangerOn(unit, aoe);
                    if (!(danger > 0f)) continue;
                    worth += ctx.Unit.IsEnemy(unit)
                        ? -danger * CombatMath.ControlWorth(unit) * rounds
                        : danger * AllyPerRound(unit) * rounds;
                }
            }
            catch { return float.NaN; }
            return worth;
        }

        /// <summary>ContextActionDispelMagic.CheckSchoolsAndDescriptor on an effect's context (a buff's or an area's).</summary>
        private static bool DispelSchoolsMatch(ContextActionDispelMagic dispel, MechanicsContext context)
        {
            if (context == null) return false;
            bool school = dispel.Schools.Length == 0 || dispel.Schools.Contains(context.SpellSchool);
            bool descriptor = dispel.Descriptor.Value == SpellDescriptor.None ||
                              (dispel.Descriptor.Value & context.SpellDescriptor) != SpellDescriptor.None;
            return dispel.CheckSchoolOrDescriptor ? school || descriptor : school && descriptor;
        }

        /// <summary>A dispel's delivery and its own save path, rather than a second failed-root-save discount on an
        /// unconditional fused dispel. Reuse one outcome read per recipient; only damaging dispel fusions need it.</summary>
        private static float DispelEffectChance(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
                                                Kingmaker.UnitLogic.Mechanics.Actions.ContextActionDispelMagic action,
                                                UnitEntityData recipient, bool benefit, ref CombatMath.FusionDamageRead damageRead)
        {
            try
            {
                if (profile.Fusion == null)
                    return ctx.Unit.IsEnemy(recipient) ? CombatMath.EffectChance(ctx.Unit, recipient, ability, profile, damage: false) : 1f;
                // For a fusion, damage:true means resistance/attack/shadow delivery, with no root-save multiplier.
                float delivery = CombatMath.EffectChance(ctx.Unit, recipient, ability, profile, damage: true);
                bool saved = profile.Save != Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown;
                float fail = saved ? CombatMath.SaveFailChance(ability, profile.Save, recipient) : 1f;
                var path = profile.FusionPath(action);
                if (!saved && path != AbilityProfile.FusionSavePath.Both) return 0f;
                float failedSurvival = 1f, passedSurvival = 1f;
                if (benefit && profile.DealsDamage)
                {
                    if (damageRead == null)
                    {
                        damageRead = new CombatMath.FusionDamageRead();
                        CombatMath.EstimateFusionDamage(ability, profile, recipient, damageRead);
                    }
                    if (!damageRead.Known) return 0f; // an unread combined outcome cannot earn dispel benefit
                    float hp = CombatMath.HpBeforeDown(recipient);
                    failedSurvival -= ValueModel.KillChance(1f, damageRead.FailedHitPoints, hp);
                    passedSurvival -= ValueModel.KillChance(1f, damageRead.PassedHitPoints, hp);
                }
                return delivery * ValueModel.DispelOutcomeShare(fail, path != AbilityProfile.FusionSavePath.Passed,
                    path != AbilityProfile.FusionSavePath.Failed, failedSurvival, passedSurvival) *
                    CombatMath.NestedSaveFactor(ability, profile, action, recipient);
            }
            catch { return benefit ? 0f : 1f; }
        }
    }
}
