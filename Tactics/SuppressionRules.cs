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
        /// A suppressed harmful buff does nothing while it is suppressed: removing it is worth something only when it would
        /// outlast what suppresses it (seconds; <see cref="float.PositiveInfinity"/> for a permanent fact).
        /// </summary>
        public static bool StillMatters(bool suppressed, float buffSecondsLeft, float suppressorSecondsLeft)
        {
            if (!suppressed) return true;
            if (float.IsNaN(buffSecondsLeft) || float.IsNaN(suppressorSecondsLeft)) return true;   // unknown: as before
            if (float.IsPositiveInfinity(suppressorSecondsLeft)) return false;
            return buffSecondsLeft > suppressorSecondsLeft;
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
