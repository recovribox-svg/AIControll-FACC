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
using Kingmaker.UnitLogic.Abilities;

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
