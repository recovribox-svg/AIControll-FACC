// ★ v0.3.28 (codex review): the existing kineticist preparation and Overflow policies, shared with pure tests.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>The current quiver cannot pay one use of the costliest current kinetic configuration.</summary>
        public static bool NeedsKineticQuiver(int ranks, int abilityBurn) => abilityBurn > 0 && ranks < abilityBurn;

        /// <summary>Preparation burn would leave less than half the real (not temporary) maximum hit points.</summary>
        public static bool PreparationBurnBelowHpFloor(float realHp, int maxHp, float burnHpLoss) =>
            realHp - burnHpLoss < 0.5f * Math.Max(1, maxHp);

        /// <summary>
        /// The existing one-blast-per-later-round Overflow credit. A known shortfall must be closed by the raw damage
        /// gain before any credit counts; null retains the old assumption when no free blast was offered. This is a
        /// policy approximation: raw Overflow damage and candidate values do not have the same hit/risk scaling.
        /// </summary>
        public static float KineticOverflowCredit(int gain, float? blastShortfall, float horizon) =>
            blastShortfall == null || gain >= blastShortfall.Value ? gain * Math.Max(0f, horizon - 1f) : 0f;
    }
}
