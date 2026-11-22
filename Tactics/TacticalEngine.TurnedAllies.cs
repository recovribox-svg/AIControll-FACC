// ★ coverage3 review (21 November 2026; docs/COVERAGE3_REVIEW.md): allies under an enemy's control, and casts that could kill an
// ally for good (Fusion coverage C3, the author's policy).
// Evidence (coverage3 log, fight 15): an enemy's Dominate put Seelah on its side (native ChangeFaction: a temporary faction
// switch, UnitDescriptor.FactionSwitcher). She became an enemy to every rule: Tripper, Wenduag, Bandit and Cambion Bard made
// weapon attacks on her, and Wenduag's and Bandit's last four hits (74 HP) killed her. Native contract: a temporary switch keeps
// UnitDescriptor.FactionOriginal (SwitchFactions permanently: false), so the unit's own side is known while it lasts.
// Policy: such an ally is to be stopped, not killed. It stays an enemy for its danger and for control (Sleep, a stun, a trip); no
// weapon attack, walk to attack it, leap or Coup de Grace is aimed at it; damage to it is priced as friendly fire.
using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Root;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>
        /// A unit of the party's side that an effect has put on another side for now (Dominate, charm: native ChangeFaction):
        /// its original faction is the player's, its current one is not.
        /// </summary>
        internal static bool IsTurnedAlly(UnitEntityData unit)
        {
            try
            {
                var descriptor = unit?.Descriptor;
                var player = BlueprintRoot.Instance?.PlayerFaction;
                return descriptor != null && player != null && descriptor.Faction != player && descriptor.FactionOriginal == player;
            }
            catch { return false; }
        }

        /// <summary>A summoned creature (temporary: it is not killed for good).</summary>
        internal static bool IsSummonUnit(UnitEntityData unit) => IsSummon(unit);

        private static void NoteTurnedAlly(TacticalContext ctx, UnitEntityData ally)
        {
            if (!CombatMemory.FirstThisFight($"turned|{ally.UniqueId}")) return;
            string by = null;
            try
            {
                foreach (var buff in ally.Buffs)
                    if (buff?.Blueprint?.GetComponent<Kingmaker.UnitLogic.FactLogic.ChangeFaction>() != null) { by = buff.Name; break; }
            }
            catch { }
            Main.Log($"[Tactics] {ally.CharacterName} is under an enemy's control{(by != null ? $" ({by})" : "")}: stopped, not killed - " +
                     "no weapon attack, Coup de Grace or killing damage on it; control still counts (first per fight)");
        }

        /// <summary>
        /// An option that only hurts an ally under an enemy's control: a weapon attack on it (its damage is all it does), a walk
        /// to attack it, a leap at it, or a Coup de Grace. Spells and maneuvers stay: their control counts, their damage is
        /// friendly fire (ScoreAbility).
        /// </summary>
        private static bool SparesTurnedAlly(TacticalContext ctx, Candidate c)
        {
            if (ctx.TurnedAllies.Count == 0 || c?.Target == null || !ctx.TurnedAllies.Contains(c.Target)) return false;
            if (c.Kind == CandidateKind.WeaponAttack || c.Griffon != null) return true;
            if (c.Kind == CandidateKind.Move && c.Ability == null) return true;
            if (c.Ability != null)
            {
                if (IsCoupDeGrace(c.Ability)) return true;
                var profile = AbilityProfile.Get(c.Ability);
                if (profile != null && profile.IsWeaponAttack) return true;
            }
            return false;
        }

        /// <summary>★ coverage4: a buff that holds its bearer on another side (an enabled ChangeFaction: Dominate, a charm).</summary>
        internal static bool HoldsUnderControl(Buff buff)
        {
            try { return buff?.Blueprint?.GetComponent<ChangeFaction>() != null && !buff.IsSuppressed; }
            catch { return false; }
        }

        /// <summary>A unit of the party's side by its own faction (under an enemy's control or not).</summary>
        internal static bool OfPartySide(UnitEntityData unit)
        {
            try
            {
                var player = BlueprintRoot.Instance?.PlayerFaction;
                return unit?.Descriptor != null && player != null && unit.Descriptor.FactionOriginal == player;
            }
            catch { return false; }
        }

        /// <summary>
        /// ★ coverage4 (author request): what taking <paramref name="hold"/> off <paramref name="ally"/> gives back. For each round
        /// it would still hold (ValueModel.HeldRounds: its NewRound re-save, its time left, at most six rounds of the fight),
        /// the enemy loses a fighter (its control worth, as a hard control would take) and the party has its ally back (its
        /// worth per round; less while it cannot act anyway, half for a summon).
        /// </summary>
        internal static float FreedWorth(TacticalContext ctx, UnitEntityData ally, Buff hold)
        {
            try
            {
                float horizon = ValueModel.Horizon(ctx.ExpectedRounds, 6f);
                float rounds = hold.IsPermanent ? horizon : Math.Min(horizon, (float)hold.TimeLeft.TotalSeconds / 6f);
                if (!(rounds > 0f)) return 0f;
                float pass = RoundSavePass(hold, ally);
                float own = ValueModel.PerRound(Level(ally)) * (CombatMath.IsDisabled(ally) ? 0.4f : 1f) *
                            (IsSummon(ally) ? ValueModel.SummonFactor : 1f);
                return (CombatMath.ControlWorth(ally) + own) * ValueModel.HeldRounds(rounds, pass);
            }
            catch { return 0f; }
        }

        private static readonly Dictionary<BlueprintBuff, ContextActionSavingThrow> RoundSaves = new Dictionary<BlueprintBuff, ContextActionSavingThrow>();

        /// <summary>
        /// The chance <paramref name="bearer"/> ends <paramref name="hold"/> itself at its next round: a NewRound saving throw whose
        /// pass removes the buff (Dominate Person: Will, ContextActionRemoveSelf). 0 without one.
        /// </summary>
        private static float RoundSavePass(Buff hold, UnitEntityData bearer)
        {
            var blueprint = hold.Blueprint;
            if (!RoundSaves.TryGetValue(blueprint, out var save))
            {
                save = null;
                foreach (var component in blueprint.GetComponents<Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions>())
                    foreach (var action in component.NewRound?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                        if (action is ContextActionSavingThrow throwing && throwing.Type != SavingThrowType.Unknown && EndsOnPass(throwing))
                            save = throwing;
                if (RoundSaves.Count > 256) RoundSaves.Clear();
                RoundSaves[blueprint] = save;
            }
            if (save == null) return 0f;
            int? dc = save.HasCustomDC ? save.CustomDC.Calculate(hold.Context) : (int?)null;
            return 1f - CombatMath.SaveFailChance(hold.Context, save.Type, bearer, dc);
        }

        private static bool EndsOnPass(ContextActionSavingThrow throwing)
        {
            foreach (var action in throwing.Actions?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                if (action is ContextActionConditionalSaved saved)
                    foreach (var passed in saved.Succeed?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                        if (passed is ContextActionRemoveSelf) return true;
            return false;
        }

        /// <summary>
        /// ★ coverage4 (author request): what <paramref name="ability"/> is worth for freeing the allies under an enemy's control it
        /// reaches. A dispel in the game's own order (DispelValue: the highest caster level first, its check, its count; a buff
        /// the party gave the ally is a loss), or a removal by name or descriptor of the hold itself, times the chance the cast
        /// reaches the ally (EffectChance). An area of allies only does not reach a unit on another side (native target filter).
        /// </summary>
        private static float FreeingValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                          List<UnitEntityData> affectedEnemies, out string note)
        {
            note = null;
            if (ctx.TurnedAllies.Count == 0 || profile.DispelActions.Count == 0 && !profile.RemovesAnything) return 0f;
            float total = 0f;
            List<string> freed = null;
            var reached = new List<UnitEntityData>();
            if (target != null && ctx.TurnedAllies.Contains(target)) reached.Add(target);
            foreach (var enemy in affectedEnemies)
                if (ctx.TurnedAllies.Contains(enemy) && !reached.Contains(enemy)) reached.Add(enemy);
            float horizon = Math.Min(ctx.ExpectedRounds, 3f);
            foreach (var ally in reached)
            {
                bool held = false;
                foreach (var buff in ally.Buffs.Enumerable) if (HoldsUnderControl(buff)) { held = true; break; }
                if (!held) continue;
                float reach;
                try { reach = CombatMath.EffectChance(ctx.Unit, ally, ability, profile, damage: false); } catch { reach = 0f; }
                if (!(reach > 0f)) continue;
                MechanicsContext source = null;
                try { source = CombatMath.CreateContext(ability, new TargetWrapper(ally)); } catch { }
                float best = 0f, chance = 0f;
                foreach (var dispel in profile.DispelActions)
                {
                    float worth = DispelValue(ctx, dispel, source, ally, horizon, true, out float frees);
                    if (frees > 0f && worth > best) { best = worth; chance = frees; }
                }
                foreach (var buff in ally.Buffs.Enumerable)
                {
                    if (!HoldsUnderControl(buff) || !RemovesByNameOrDescriptor(profile, buff)) continue;
                    float worth = FreedWorth(ctx, ally, buff);
                    if (worth > best) { best = worth; chance = 1f; }
                }
                if (!(best > 0f)) continue;
                total += reach * best;
                (freed ?? (freed = new List<string>())).Add($"{ally.CharacterName} {reach * chance:P0}");
            }
            if (freed != null)
            {
                string text = $"frees {string.Join(", ", freed)} from an enemy's control";
                note = text;
                ReportOnce($"freeing|{CombatMemory.Fight}|{ability.Blueprint.AssetGuid}|{ctx.Unit.UniqueId}",
                    () => $"[Tactics] {ctx.Unit.CharacterName}: {ability.Name} {text} = {total:F1} (first per fight, caster and ability)");
            }
            return total;
        }

        private static bool RemovesByNameOrDescriptor(AbilityProfile profile, Buff buff)
        {
            if (profile.RemovedBuffs.Contains(buff.Blueprint)) return true;
            var descriptor = buff.MaybeContext?.SpellDescriptor ?? Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None;
            return profile.RemovedDescriptors != Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None &&
                   (profile.RemovedDescriptors & descriptor) != Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None;
        }

        /// <summary>
        /// ★ Fusion coverage C3 (author's policy): a cast that may kill <paramref name="ally"/> for good. At the veto risk or
        /// above the cast is not used, whatever the settings and its value (<paramref name="why"/>); below it the chance costs a
        /// hundred times the ally's worth (<paramref name="cost"/>). A summon is not killed for good: it vanishes anyway, and
        /// keeps the ordinary friendly-fire price.
        /// </summary>
        private static bool PermanentDeathVetoes(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData ally,
                                                 out float cost, out string why)
        {
            cost = 0f;
            why = null;
            if (ally == null || IsSummon(ally) || !CombatMath.CanKillPermanently(ability)) return false;
            float risk = CombatMath.PermanentDeathRisk(ability, profile, ally);
            if (!(risk > 0f)) return false;
            if (PermanentDeathRules.Vetoed(risk))
            {
                why = $"could kill {ally.CharacterName} for good ({risk:P0})";
                ReportOnce($"permadeath|{CombatMemory.Fight}|{ability.Blueprint.AssetGuid}|{ally.UniqueId}",
                    () => $"[Tactics] {ctx.Unit.CharacterName}: {ability.Name} not used - it could kill {ally.CharacterName} for good " +
                          $"({risk:P0}: {ally.HPLeft} HP left) (first per fight, ability and ally)");
                return true;
            }
            cost = PermanentDeathRules.Cost(risk, AllyWorth(ctx, ally));
            return false;
        }
    }
}
