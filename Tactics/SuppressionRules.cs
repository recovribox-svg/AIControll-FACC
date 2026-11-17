// ★ suppressed buffs (17 November 2026; docs/BURST_AIM_AND_TACTICS_PLAN.md §5, slice C): the pure rules. No game types:
// the game side is BuffSuppression.cs.
// Native contracts (UnitPartBuffSuppress, SuppressBuffs, Buff, read from the supplied assembly):
// - a buff is suppressed when it is not on the part's exclusion list and its blueprint is on the part's list, or one of
//   its context's descriptor flags or its school is suppressed;
// - every new buff is checked as it is attached, so a recast of a suppressed buff is suppressed too;
// - when the suppressing fact ends, the game turns the buff back on by itself.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class SuppressionRules
    {
        /// <summary>The native order: the exclusion list wins; then the blueprint list, a descriptor flag or the school.</summary>
        public static bool Suppressed(bool excluded, bool listed, bool descriptorSuppressed, bool schoolSuppressed) =>
            !excluded && (listed || descriptorSuppressed || schoolSuppressed);

        /// <summary>
        /// ★ review follow-up F3 (C-R1): the share of what removing a harmful buff is worth that falls within the horizon.
        /// A buff that is not suppressed keeps all of it (the cleanse's value as before). A suppressed one does nothing
        /// until its suppressor ends: its share is the rounds it acts after that, within the horizon and before it ends,
        /// over its rounds left within the horizon. Rounds; <see cref="float.PositiveInfinity"/> for a permanent buff or
        /// suppressor; an unknown time (NaN) keeps all of it, as before.
        /// </summary>
        public static float ActiveShare(bool suppressed, float roundsLeft, float suppressedRounds, float horizon)
        {
            if (!suppressed) return 1f;
            // Unknown suppression cannot create future active time after the buff (or scoring horizon) has ended.
            if (roundsLeft <= 0f || horizon <= 0f) return 0f;
            if (float.IsNaN(roundsLeft) || float.IsNaN(suppressedRounds)) return 1f;
            float rounds = Math.Min(horizon, roundsLeft);
            if (!(rounds > 0f)) return 0f;
            return ActiveRounds(roundsLeft, suppressedRounds, horizon) / rounds;
        }

        /// <summary>
        /// The rounds a buff acts within the horizon: its rounds left less those its suppressor still holds it
        /// (<paramref name="suppressedRounds"/>: 0 when it is not suppressed, NaN when unknown, which keeps all of them).
        /// </summary>
        public static float ActiveRounds(float roundsLeft, float suppressedRounds, float horizon)
        {
            float rounds = Math.Min(horizon, roundsLeft);
            if (!(rounds > 0f)) return 0f;
            if (float.IsNaN(suppressedRounds) || !(suppressedRounds > 0f)) return rounds;
            return Math.Max(0f, rounds - Math.Min(horizon, suppressedRounds));
        }
    }
}
