// ★ cast disruption (17 November 2026; docs/BURST_AIM_AND_TACTICS_PLAN.md §4, slice B): the pure rules for valuing a hit
// on an enemy whose spell has not gone off yet. No game types: the game side is TacticalEngine.Disrupt.cs.
// Native contracts (read from the supplied assembly):
// - every damage packet to a caster whose cast has started and not gone off queues a concentration check, if the spell
//   can be broken by damage (UnitConcentrationController, AbilityData.CanBeInterruptedWithDamage);
// - the check is d20 + concentration against 10 + spell level + damage / 2 (RuleCheckConcentration.OnTrigger);
// - a failed check ends the cast and spends the spell (UnitUseAbility.FailIfConcentrationCheckFailed); once one check has
//   failed, later ones change nothing (MakeConcentrationCheck).
// Real time only: in turn-based combat an enemy casts on its own turn, when the party cannot act.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class DisruptRules
    {
        /// <summary>From a decision to its command starting (the next ticks of the real-time controller).</summary>
        public const float CommandLatencySeconds = 0.1f;

        /// <summary>From a weapon attack starting to its first hit (an estimate; the replay measures it).</summary>
        public const float FirstSwingSeconds = 0.6f;

        /// <summary>Between two attacks that follow each other at once: a bow's, or an animation sequence's (an estimate).</summary>
        public const float NextSwingSeconds = 0.7f;

        /// <summary>
        /// ★ review follow-up F2: real time, UnitAttack spreads a routine's attacks over its 6 s animation window
        /// (m_AnimationsDuration): the k-th starts k × 6 / N seconds into the command (UnitAttack.OnTick). Bow-style attacks
        /// and an animation sequence start as soon as the previous one acts instead.
        /// </summary>
        public const float RoutineWindowSeconds = 6f;

        /// <summary>Seconds between two attacks of an <paramref name="attacks"/>-attack routine, by the native schedule.</summary>
        public static float SwingSpacing(int attacks, bool chained, float window = RoutineWindowSeconds) =>
            chained || attacks <= 1 ? NextSwingSeconds : window / attacks;

        /// <summary>The speed of an arrow or bolt in flight, in m/s (an estimate).</summary>
        public const float MissileSpeed = 30f;

        /// <summary>A hit must land this long before the cast would go off.</summary>
        public const float MarginSeconds = 0.25f;

        /// <summary>Attacks of one routine counted against one cast (the window is short).</summary>
        public const int MaxHits = 2;

        /// <summary>
        /// The chance one damage packet of <paramref name="damage"/> makes the caster fail its concentration check: it fails
        /// when d20 + concentration is under 10 + spell level + damage / 2 (integer division, as the rule does).
        /// </summary>
        public static float FailChance(int spellLevel, float damage, int concentration)
        {
            if (!(damage >= 0f) || float.IsInfinity(damage)) return 0f;
            int dc = 10 + Math.Max(0, spellLevel) + (int)Math.Floor(damage) / 2;
            float fail = (dc - concentration - 1) / 20f;   // d20 results 1 .. dc - concentration - 1 fail
            return fail < 0f ? 0f : fail > 1f ? 1f : fail;
        }

        /// <summary>
        /// Folds one more hit into the chance that no hit has broken the cast yet: it lands with <paramref name="hitChance"/>
        /// and then breaks it with <paramref name="failChance"/>.
        /// </summary>
        public static float StillCasting(float stillCasting, float hitChance, float failChance)
        {
            float p = Clamp01(hitChance) * Clamp01(failChance);
            return Clamp01(stillCasting) * (1f - p);
        }

        /// <summary>A hit that lands <paramref name="hitSeconds"/> from now counts when the cast still has that long left.</summary>
        public static bool InTime(float hitSeconds, float castSecondsLeft) =>
            hitSeconds >= 0f && hitSeconds + MarginSeconds <= castSecondsLeft;

        /// <summary>
        /// When the <paramref name="index"/>-th attack of a weapon routine lands, from the decision: the command starting, the
        /// walk, the attacks before it (<paramref name="spacing"/> apart, <see cref="SwingSpacing"/>) and, for a missile, its
        /// flight over <paramref name="distance"/>.
        /// </summary>
        public static float WeaponHitSeconds(int index, float walkSeconds, bool ranged, float distance, float spacing)
        {
            float t = CommandLatencySeconds + Math.Max(0f, walkSeconds) + FirstSwingSeconds + Math.Max(0, index) * Math.Max(0f, spacing);
            return t + Flight(ranged, distance);
        }

        /// <summary>
        /// ★ F2: when attack <paramref name="index"/> of an ally's routine that is already running lands, from now. On the
        /// native schedule it starts index × spacing into the command; a chained attack (bow, animation sequence) starts as
        /// the one before it acts, taken as now for the next one.
        /// </summary>
        public static float RunningHitSeconds(int index, int nextIndex, float spacing, float elapsed, bool chained, bool ranged, float distance)
        {
            float t = chained
                ? FirstSwingSeconds + Math.Max(0, index - nextIndex) * Math.Max(0f, spacing)
                : Math.Max(0f, index * Math.Max(0f, spacing) + FirstSwingSeconds - Math.Max(0f, elapsed));
            return t + Flight(ranged, distance);
        }

        /// <summary>A queued attack may approach while its action cooldown runs. It can start after both finish.</summary>
        public static float OrderedStartSeconds(float walkSeconds, float cooldownSeconds) =>
            Math.Max(Math.Max(0f, walkSeconds), Math.Max(0f, cooldownSeconds));

        private static float Flight(bool ranged, float distance) =>
            ranged && distance > 0f && !float.IsInfinity(distance) ? distance / MissileSpeed : 0f;

        /// <summary>When an ability lands: the command starting, the walk, its own cast time and its delivery lag.</summary>
        public static float AbilityHitSeconds(float walkSeconds, float castSeconds, float lagSeconds) =>
            CommandLatencySeconds + Math.Max(0f, walkSeconds) + Math.Max(0f, castSeconds) + Math.Max(0f, lagSeconds);

        /// <summary>
        /// What a cast that lands costs the party: the spell's own forecast when the engine reads it, else the caster's worth
        /// for a round (the main action the spell takes).
        /// </summary>
        public static float Stakes(float readHarm, float roundWorth) => readHarm > 0f ? readHarm : Math.Max(0f, roundWorth);

        /// <summary>The value of breaking the cast: the chance it is broken times what it would cost.</summary>
        public static float Value(float stillCasting, float stakes) => (1f - Clamp01(stillCasting)) * Math.Max(0f, stakes);

        /// <summary>
        /// ★ F2: what this action adds when allies' hits already ordered leave the cast going with <paramref name="othersStill"/>.
        /// Breaks are independent, so P(broken by anyone) − P(broken by the others alone) = othersStill × (1 − ourStill),
        /// whichever lands first. With no other hits pending it is <see cref="Value"/>.
        /// </summary>
        public static float MarginalValue(float ourStill, float othersStill, float stakes) =>
            (float.IsNaN(othersStill) ? 1f : Clamp01(othersStill)) *     // unknown others: as if none were ordered
            (1f - (float.IsNaN(ourStill) ? 1f : Clamp01(ourStill))) *    // unknown own chance: no break claimed
            Math.Max(0f, stakes);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : float.IsNaN(v) ? 0f : v;
    }
}
