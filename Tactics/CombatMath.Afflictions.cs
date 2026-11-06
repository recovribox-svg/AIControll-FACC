// ★ v0.3.28 test 14 (author's request: Mandragora swarm poison and weapon poisons): what an enemy's weapon hits and a swarm's
// rounds do beyond their hit-point damage, read from the actions the game runs:
//   - a weapon hit: AddInitiatorAttackWithWeaponTrigger (on hit, actions on the target) on the attacker's facts - a giant
//     spider's bite: a Fortitude save (the fact's DC: ContextCalculateAbilityParams, Constitution), on a failure
//     GiantSpiderPoisonBuff (BuffPoisonStatDamage: 1d2 Strength, 4 ticks, cured by 1 save); a Shadow's 1d6 Strength;
//   - a swarm's round: AddFactContextActions.NewRound with ContextActionSwarmAttack on the swarm's own facts - the Mandragora
//     swarm's 5d6, a Fortitude save or Nauseated for a round, and a Fortitude save or MandragoraSwarmPoisonBuff, whose every
//     round is a save, then Constitution damage and Confusion.
// The walk follows the game's structure: a saving throw (ContextActionSavingThrow, its fact's DC or a custom one) splits
// ContextActionConditionalSaved into its failed and passed branches by the victim's chance to fail; a Conditional counts the
// larger branch (its condition is not evaluated); an applied buff counts its poison doses (ValueModel.PoisonDoses), the
// control of its conditions (AbilityProfile.BuffControl) and its own NewRound actions for the rounds left; immune victims get
// nothing of a buff (BuffDescriptorImmunity, SpecificBuffImmunity), of ability damage or of negative levels. Until test 13
// only a weapon trigger's direct ability damage was read (CombatMath.OnHit.cs), and a swarm's round not at all.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        /// <summary>The expected effects of one application of an action list on one victim.</summary>
        internal sealed class Affliction
        {
            public float HitPoints;
            public readonly Dictionary<StatType, float> Points = new Dictionary<StatType, float>();
            public float NegativeLevels;
            /// <summary>Rounds the victim is controlled, weighted by the control's strength (hard 1, soft 0.4).</summary>
            public float Control;
            /// <summary>★ v0.3.29 test 1: the chance one application controls the victim at all (<see cref="Control"/> over
            /// it is how long), so repeated hits count it once over (<see cref="Repeated"/>).</summary>
            public float ControlChance;

            public bool IsEmpty => HitPoints <= 0f && NegativeLevels <= 0f && Control <= 0f && Points.Values.All(v => v <= 0f);

            /// <summary>Only to choose a Conditional's larger branch.</summary>
            public float Size => HitPoints + 3f * Points.Values.Sum() + 5f * NegativeLevels + 5f * Control;

            public void Add(Affliction other, float weight)
            {
                if (other == null || !(weight > 0f)) return;
                HitPoints += other.HitPoints * weight;
                NegativeLevels += other.NegativeLevels * weight;
                Control += other.Control * weight;
                ControlChance += other.ControlChance * weight;
                foreach (var entry in other.Points) AddPoints(entry.Key, entry.Value * weight);
            }

            /// <summary>
            /// ★ v0.3.29 test 1: <paramref name="applications"/> applications in one round (a full attack's expected hits):
            /// damage, ability damage and negative levels add up; control does not - a second paralysis in the same round
            /// costs no more turns (ValueModel.RepeatedControl). The Ghoul Creeper read 63.9 a round on Seelah, 2.4 hits
            /// each worth 1.2 rounds of paralysis, against 24-31 it dealt.
            /// </summary>
            public Affliction Repeated(float applications)
            {
                var many = new Affliction();
                if (!(applications > 0f)) return many;
                many.HitPoints = HitPoints * applications;
                many.NegativeLevels = NegativeLevels * applications;
                foreach (var entry in Points) many.AddPoints(entry.Key, entry.Value * applications);
                many.Control = ValueModel.RepeatedControl(Control, ControlChance, applications);
                many.ControlChance = Math.Min(1f, ControlChance) > 0f && applications > 1f
                    ? 1f - (float)Math.Pow(1f - Math.Min(1f, ControlChance), applications) : ControlChance * applications;
                return many;
            }

            public void AddPoints(StatType stat, float points)
            {
                if (points > 0f) Points[stat] = (Points.TryGetValue(stat, out float had) ? had : 0f) + points;
            }
        }

        /// <summary>Rounds an affliction's later effects are counted for (the threat model's horizon).</summary>
        internal const float AfflictionRounds = 3f;

        /// <summary>
        /// What one application of <paramref name="actions"/> (from a fact whose DC is <paramref name="dc"/>) is expected to do to
        /// <paramref name="victim"/>.
        /// </summary>
        internal static Affliction ReadAffliction(ActionList actions, UnitEntityData victim, int dc)
        {
            var result = new Affliction();
            if (actions?.Actions == null) return result;
            // No victim (tools/ProfileAudit on blueprint dumps): every save is an even chance, nobody is immune.
            try { WalkAffliction(actions, victim, dc, 1f, null, result, 0); }
            catch (Exception ex) { Main.Verbose($"[Math] affliction on {victim?.CharacterName}: {ex.Message}"); }
            return result;
        }

        private static void WalkAffliction(ActionList list, UnitEntityData victim, int dc, float weight, float? fail, Affliction into, int depth)
        {
            var actions = list?.Actions;
            if (actions == null || depth > 12 || !(weight > 0.001f)) return;   // a swarm round nests 7 deep before its poison ticks
            foreach (var action in actions)
            {
                switch (action)
                {
                    case ContextActionDealDamage deal:
                    {
                        float amount = DiceAverage(deal.Value) * weight;
                        var kind = KindOf(deal);
                        if (kind == DealKind.AbilityDamage) into.AddPoints(deal.AbilityType, amount);
                        else if (kind == DealKind.EnergyDrain) into.NegativeLevels += amount;
                        else into.HitPoints += amount;
                        break;
                    }
                    case ContextActionSavingThrow save:
                    {
                        int saveDc = save.HasCustomDC && save.CustomDC != null && save.CustomDC.ValueType == ContextValueType.Simple &&
                                     save.CustomDC.Value > 0 ? save.CustomDC.Value : dc;
                        WalkAffliction(save.Actions, victim, dc, weight, SaveFail(victim, save.Type, saveDc), into, depth + 1);
                        break;
                    }
                    case ContextActionConditionalSaved saved:
                    {
                        float f = fail ?? 1f;
                        WalkAffliction(saved.Failed, victim, dc, weight * f, fail, into, depth + 1);
                        WalkAffliction(saved.Succeed, victim, dc, weight * (1f - f), fail, into, depth + 1);
                        break;
                    }
                    case Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional conditional:
                    {
                        var yes = new Affliction();
                        var no = new Affliction();
                        WalkAffliction(conditional.IfTrue, victim, dc, 1f, fail, yes, depth + 1);
                        WalkAffliction(conditional.IfFalse, victim, dc, 1f, fail, no, depth + 1);
                        into.Add(yes.Size >= no.Size ? yes : no, weight);
                        break;
                    }
                    case Kingmaker.Assets.UnitLogic.Mechanics.Actions.ContextActionSwarmAttack swarm:
                        WalkAffliction(swarm.AttackActions, victim, dc, weight, fail, into, depth + 1);
                        break;
                    case ContextActionOnSwarmTargets targets:
                        WalkAffliction(targets.Actions, victim, dc, weight, fail, into, depth + 1);
                        break;
                    case ContextActionApplyBuff apply:
                        AddBuffAffliction(apply, victim, dc, weight, into, depth);
                        break;
                }
            }
        }

        private static void AddBuffAffliction(ContextActionApplyBuff apply, UnitEntityData victim, int dc, float weight, Affliction into, int depth)
        {
            var buff = apply.Buff;
            if (buff == null || apply.ToCaster || BuffBlockedFor(victim, buff)) return;
            float rounds = AfflictionRounds;
            if (!apply.Permanent && apply.DurationValue != null && apply.DurationValue.Rate == Kingmaker.UnitLogic.Mechanics.DurationRate.Rounds)
            {
                float lasting = DiceAverage(apply.DurationValue.DiceCountValue?.ValueType == ContextValueType.Simple
                                                ? new ContextDiceValue { DiceType = apply.DurationValue.DiceType, DiceCountValue = apply.DurationValue.DiceCountValue, BonusValue = apply.DurationValue.BonusValue }
                                                : null);
                if (lasting > 0f) rounds = Math.Min(rounds, lasting);
            }
            // Its poison doses: one when it lands, then a save each round (BuffPoisonStatDamage).
            foreach (var poison in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<Kingmaker.Designers.Mechanics.Buffs.BuffPoisonStatDamage>(buff))
            {
                float each = Average(poison.Value) + poison.Bonus;
                if (each <= 0f) continue;
                float doseFail = poison.SaveType == Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown ? 1f : SaveFail(victim, poison.SaveType, dc);
                int later = Math.Min(poison.Ticks - 1, Mathf.FloorToInt(AfflictionRounds - 1f));
                into.AddPoints(poison.Stat, weight * each * ValueModel.PoisonDoses(doseFail, Math.Max(0, later), poison.SuccesfullSaves));
            }
            // Its control (its conditions), for the rounds it lasts.
            var control = AbilityProfile.BuffControl(buff);
            if (control == ControlStrength.Hard) into.Control += weight * rounds;
            else if (control == ControlStrength.Soft) into.Control += weight * 0.4f * rounds;
            if (control == ControlStrength.Hard || control == ControlStrength.Soft) into.ControlChance += weight;
            // Its own round actions (a custom poison: MandragoraSwarmPoisonBuff), from the next round on.
            foreach (var round in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions>(buff))
            {
                if (round.NewRound?.Actions == null || round.NewRound.Actions.Length == 0) continue;
                var tick = new Affliction();
                WalkAffliction(round.NewRound, victim, dc, 1f, null, tick, depth + 1);
                into.Add(tick, weight * Math.Max(1f, rounds - 1f));
            }
        }

        /// <summary>The victim's chance to fail a save of <paramref name="type"/> against <paramref name="dc"/>.</summary>
        internal static float SaveFail(UnitEntityData victim, Kingmaker.EntitySystem.Stats.SavingThrowType type, int dc)
        {
            if (victim == null) return 0.5f;
            try
            {
                switch (type)
                {
                    case Kingmaker.EntitySystem.Stats.SavingThrowType.Fortitude: return ValueModel.SaveFailChance(dc, victim.Stats.SaveFortitude.ModifiedValue);
                    case Kingmaker.EntitySystem.Stats.SavingThrowType.Reflex: return ValueModel.SaveFailChance(dc, victim.Stats.SaveReflex.ModifiedValue);
                    case Kingmaker.EntitySystem.Stats.SavingThrowType.Will: return ValueModel.SaveFailChance(dc, victim.Stats.SaveWill.ModifiedValue);
                }
            }
            catch { }
            return 1f;
        }

        /// <summary>The game would refuse <paramref name="victim"/> this buff: BuffDescriptorImmunity for its descriptor (poison
        /// immunity) or SpecificBuffImmunity for it - read without the caster checks.</summary>
        private static bool BuffBlockedFor(UnitEntityData victim, BlueprintBuff buff)
        {
            if (victim == null) return false;
            try
            {
                var descriptor = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.Blueprints.Classes.Spells.SpellDescriptorComponent>(buff)?.Descriptor.Value
                                 ?? Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None;
                foreach (var component in BuffImmunities(victim))
                {
                    if (component is Kingmaker.UnitLogic.FactLogic.BuffDescriptorImmunity byDescriptor &&
                        descriptor != Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None && byDescriptor.Descriptor.HasAnyFlag(descriptor) &&
                        byDescriptor.IgnoreFeature == null && !byDescriptor.CheckFact) return true;
                    if (component is Kingmaker.UnitLogic.FactLogic.SpecificBuffImmunity specific && specific.Buff == buff) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// <paramref name="affliction"/> in hit points on <paramref name="victim"/> (ValueModel.AfflictionWorth): its hit-point
        /// damage, ability damage and negative levels (by score, immunities read), and its control at the victim's worth a round.
        /// </summary>
        internal static float AfflictionHp(Affliction affliction, UnitEntityData victim)
        {
            if (affliction == null || affliction.IsEmpty || victim == null) return 0f;
            float hpLeft = Math.Max(0f, victim.HPLeft);
            int level = Math.Max(1, victim.Descriptor?.Progression?.CharacterLevel ?? 1);
            float drain = 0f;
            foreach (var entry in affliction.Points)
                if (!AbilityDamageImmune(victim, entry.Key, false))
                    drain += ValueModel.AbilityDamageEquivalent(entry.Value, AbilityScore(victim, entry.Key), hpLeft);
            if (affliction.NegativeLevels > 0f && !EnergyDrainImmune(victim))
                drain += ValueModel.EnergyDrainEquivalent(affliction.NegativeLevels, level, hpLeft);
            return ValueModel.AfflictionWorth(affliction.HitPoints, drain, affliction.Control, hpLeft, ValueModel.PerRound(level));
        }

        /// <summary><paramref name="affliction"/> as the share of <paramref name="victim"/> it takes (ValueModel.AfflictionShare);
        /// its hit-point damage is left out (callers price the hit's damage themselves).</summary>
        internal static float AfflictionShare(Affliction affliction, UnitEntityData victim)
        {
            if (affliction == null || affliction.IsEmpty || victim == null) return 0f;
            float share = 0f;
            foreach (var entry in affliction.Points)
                if (!AbilityDamageImmune(victim, entry.Key, false))
                    share += ValueModel.AbilityDamageShare(entry.Value, AbilityScore(victim, entry.Key));
            float levels = affliction.NegativeLevels > 0f && !EnergyDrainImmune(victim) ? affliction.NegativeLevels : 0f;
            return ValueModel.AfflictionShare(share, levels, Math.Max(1, victim.Descriptor?.Progression?.CharacterLevel ?? 1), affliction.Control);
        }

        private sealed class AfflictionSource
        {
            public ActionList Actions;
            public int Dc;
        }

        private static readonly Dictionary<string, List<AfflictionSource>> s_HitSources = new Dictionary<string, List<AfflictionSource>>();
        private static readonly Dictionary<string, List<AfflictionSource>> s_RoundSources = new Dictionary<string, List<AfflictionSource>>();

        /// <summary>
        /// The DC an attacker's fact gives its saves: the fact's own context (ContextCalculateAbilityParams), else the rule for
        /// a creature's special attacks, 10 + half its Hit Dice + its Constitution modifier.
        /// </summary>
        private static int FactDc(UnitEntityData owner, Kingmaker.EntitySystem.EntityFact fact)
        {
            try
            {
                int dc = fact?.MaybeContext?.Params?.DC ?? 0;
                if (dc > 0) return dc;
            }
            catch { }
            int hd = Math.Max(1, owner.Descriptor?.Progression?.CharacterLevel ?? 1);
            int con = 0;
            try { con = owner.Stats.Constitution.Bonus; } catch { }
            return 10 + hd / 2 + con;
        }

        private static List<AfflictionSource> SourcesOf(UnitEntityData unit, bool hits)
        {
            var cache = hits ? s_HitSources : s_RoundSources;
            string key = unit.UniqueId + "|" + unit.Facts.List.Count + "|" + (unit.Body?.PrimaryHand?.MaybeWeapon?.UniqueId ?? "-");
            if (cache.TryGetValue(key, out var known)) return known;
            var list = new List<AfflictionSource>();
            var facts = new List<Kingmaker.EntitySystem.EntityFact>(unit.Facts.List);
            // A weapon's own enchantments carry hit triggers too (AnimatedWeaponPoisonEnchant: the same component).
            if (hits)
                foreach (var weapon in HandWeapons(unit))
                    foreach (var enchantment in weapon.Enchantments) facts.Add(enchantment);
            foreach (var fact in facts)
            {
                var components = fact?.Blueprint?.ComponentsArray;
                if (components == null || !fact.Active) continue;
                foreach (var component in components)
                {
                    if (hits && component is Kingmaker.UnitLogic.Mechanics.Components.AddInitiatorAttackWithWeaponTrigger trigger &&
                        trigger.OnlyHit && !trigger.OnMiss && !trigger.CriticalHit && !trigger.ActionsOnInitiator && trigger.Action?.Actions?.Length > 0)
                        list.Add(new AfflictionSource { Actions = trigger.Action, Dc = FactDc(unit, fact) });
                    if (!hits && component is Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions round &&
                        HasSwarmAttack(round.NewRound, 0))
                        list.Add(new AfflictionSource { Actions = round.NewRound, Dc = FactDc(unit, fact) });
                }
            }
            if (cache.Count > 256) cache.Clear();
            cache[key] = list;
            return list;
        }

        private static readonly Dictionary<UnitEntityData, KeyValuePair<int, bool>> s_HasHitSources =
            new Dictionary<UnitEntityData, KeyValuePair<int, bool>>();

        /// <summary>The unit's hits carry anything (cheap, once a frame per unit: most attackers have nothing).</summary>
        private static bool HasHitAfflictions(UnitEntityData unit)
        {
            int frame = Time.frameCount;
            if (s_HasHitSources.TryGetValue(unit, out var known) && known.Key == frame) return known.Value;
            bool any = false;
            try { any = SourcesOf(unit, true).Count > 0; } catch { }
            if (s_HasHitSources.Count > 256) s_HasHitSources.Clear();
            s_HasHitSources[unit] = new KeyValuePair<int, bool>(frame, any);
            return any;
        }

        private static IEnumerable<Kingmaker.Items.ItemEntityWeapon> HandWeapons(UnitEntityData unit)
        {
            var body = unit.Body;
            if (body == null) yield break;
            if (body.PrimaryHand?.MaybeWeapon != null) yield return body.PrimaryHand.MaybeWeapon;
            if (body.SecondaryHand?.MaybeWeapon != null) yield return body.SecondaryHand.MaybeWeapon;
            foreach (var limb in body.AdditionalLimbs)
                if (limb?.MaybeWeapon != null) yield return limb.MaybeWeapon;
        }

        /// <summary>The round actions strike the swarm's targets: ContextActionSwarmAttack (the Mandragora swarm), or
        /// ★ v0.3.28 test 15: ContextActionOnSwarmTargets run directly (the Raven swarm, Creeping Doom: no attack animation
        /// first). Through Conditionals, 3 deep.</summary>
        private static bool HasSwarmAttack(ActionList list, int depth)
        {
            if (list?.Actions == null || depth > 3) return false;
            foreach (var action in list.Actions)
            {
                if (action is Kingmaker.Assets.UnitLogic.Mechanics.Actions.ContextActionSwarmAttack ||
                    action is ContextActionOnSwarmTargets) return true;
                if (action is Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional c &&
                    (HasSwarmAttack(c.IfTrue, depth + 1) || HasSwarmAttack(c.IfFalse, depth + 1))) return true;
            }
            return false;
        }

        /// <summary>What one weapon hit of <paramref name="attacker"/> does to <paramref name="victim"/> beyond the hit itself.</summary>
        internal static Affliction OnHitAffliction(UnitEntityData attacker, UnitEntityData victim)
        {
            var result = new Affliction();
            if (attacker == null || victim == null) return result;
            try { foreach (var source in SourcesOf(attacker, true)) result.Add(ReadAffliction(source.Actions, victim, source.Dc), 1f); }
            catch (Exception ex) { Main.Verbose($"[Math] on-hit afflictions of {attacker.CharacterName}: {ex.Message}"); }
            return result;
        }

        /// <summary>What one round of <paramref name="swarm"/>'s swarm attack does to <paramref name="victim"/> in its space;
        /// empty for a unit without one.</summary>
        internal static Affliction SwarmRoundAffliction(UnitEntityData swarm, UnitEntityData victim)
        {
            var result = new Affliction();
            if (swarm == null || victim == null) return result;
            try { foreach (var source in SourcesOf(swarm, false)) result.Add(ReadAffliction(source.Actions, victim, source.Dc), 1f); }
            catch (Exception ex) { Main.Verbose($"[Math] swarm round of {swarm.CharacterName}: {ex.Message}"); }
            return result;
        }

        /// <summary>
        /// What <paramref name="hits"/> weapon hits of <paramref name="attacker"/> (an attack's expected hits) are worth beyond
        /// the hits' own damage on <paramref name="victim"/>, in hit points (AfflictionHp of Affliction.Repeated), once a frame
        /// per pair and hit count: the party's own poisoned or draining weapons are valued with it.
        /// </summary>
        internal static float OnHitHp(UnitEntityData attacker, UnitEntityData victim, float hits)
        {
            if (attacker == null || victim == null || !(hits > 0f) || !HasHitAfflictions(attacker)) return 0f;
            return FrameCached($"onhit-hp|{attacker.UniqueId}|{victim.UniqueId}|{(int)Math.Round(hits * 20f)}",
                () => (int)Math.Round(AfflictionHp(OnHitAffliction(attacker, victim).Repeated(hits), victim) * 100f)) / 100f;
        }

        /// <summary>
        /// The share of <paramref name="victim"/> one weapon hit of <paramref name="attacker"/> takes through its on-hit
        /// afflictions (TacticalEngine.PriceAttacks), once a frame per pair.
        /// </summary>
        internal static float OnHitShare(UnitEntityData attacker, UnitEntityData victim)
        {
            if (attacker == null || victim == null || !HasHitAfflictions(attacker)) return 0f;
            return FrameCached($"onhit-share|{attacker.UniqueId}|{victim.UniqueId}",
                () => (int)Math.Round(AfflictionShare(OnHitAffliction(attacker, victim), victim) * 10000f)) / 10000f;
        }
    }
}
