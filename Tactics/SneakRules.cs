// ★ sneak attack refinements (17 November 2026; docs/BURST_AIM_AND_TACTICS_PLAN.md §6, slice D): the pure rules. No game
// types: the game side is CombatMath.Flanking.cs.
// Native contracts (read from the supplied assembly):
// - RuleAttackRoll: on a rolled attack that is a sneak attack, a confirmed critical or a precise strike, one d100 against
//   UnitPartFortification.Value (the highest fortification) negates all three when it is at or under the value;
// - UnitCombatState.IsFlanked: turn-based, more than one unit engages the target; real time, more than one engaging unit
//   whose command targets it; never with CannotBeFlanked. Engagement also needs threat reach, sight and no total
//   concealment (UnitCombatEngagementController.TickUnit / UnitEngagementExtension.IsEngage). PredictsFlank consumes
//   the caller's prospective engagement assumption; it cannot prove those conditions;
// - RuleCalculateAttackBonus: +2 flanking on a melee weapon when the target is flanked or TargetIsFlanked is set.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class SneakRules
    {
        /// <summary>The share of sneak attack and critical extra damage that survives fortification (percent).</summary>
        public static float FortificationKeep(int chance) => 1f - Math.Max(0, Math.Min(100, chance)) / 100f;

        /// <summary>
        /// One attack line's expected damage: its hits, and its sneak dice and critical extra as far as fortification lets
        /// them through. A single fortification roll negates both, so the expected loss is linear in each.
        /// </summary>
        public static float LineDamage(float hit, float perHit, float sneak, float critExtra, float keep) =>
            hit * perHit + keep * (hit * sneak + critExtra);

        /// <summary>
        /// The target will be flanked when this attack lands: it is now, or the attacker engages it (a melee attack) and
        /// at least one other unit counts as engaging it by the mode's rule. Never when it cannot be flanked.
        /// </summary>
        public static bool PredictsFlank(bool nativeFlanked, bool cannotBeFlanked, bool attackerEngages, int otherEngagers) =>
            !cannotBeFlanked && (nativeFlanked || attackerEngages && otherEngagers >= 1);

        /// <summary>
        /// What one of a partner's attack lines gains once the target is flanked: the difference between native-derived
        /// expected line damage, plus newly enabled sneak dice (0 when they applied already). The two line quotes already
        /// include existing sneak dice, criticals and fortification; do not apply fortification to that difference again.
        /// </summary>
        public static float PartnerLineGain(float before, float after, float hitFlanked, float newSneak, float keep) =>
            Math.Max(0f, after - before) + Math.Max(0f, hitFlanked) * Math.Max(0f, newSneak) * keep;
    }
}
