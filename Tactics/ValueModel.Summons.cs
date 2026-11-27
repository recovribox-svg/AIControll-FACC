using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        // With one independent extra die and the lower kept, both dice must pass.
        internal static float WorstOfTwoLostShare(float pass) => pass > 0f ? 1f - Clamp(pass, 0f, 1f) : 0f;

        internal static float RedirectedAttackWorth(float chance, float threatened, bool beneficiaryCertain) =>
            beneficiaryCertain ? Clamp(chance, 0f, 1f) * Math.Max(0f, threatened) : 0f;

        internal static float TeleportApproachWorth(float followUp, float alternative, bool retainsAttack, float advanceShare)
        {
            if (!(followUp > 0f)) return 0f;
            return retainsAttack ? Math.Max(0f, followUp - Math.Max(0f, alternative))
                : AdvanceWorth(followUp, Math.Max(0f, alternative), advanceShare);
        }

        /// <summary>
        /// ★ v0.3.70 P1 (author-approved movement): a teleport to an escape walk's spot, before its landing and the cast's own
        /// risks: what reaching the spot is worth (<paramref name="gross"/>) less what the teleport gives up - the walk's own
        /// losses, and for a main-action teleport the best main action this turn (the walk keeps it) - with the easy-fight
        /// restraint on what it spends (<paramref name="reserve"/>).
        /// </summary>
        internal static float TeleportEscapeWorth(float gross, float walkGivenUp, float bestMain, bool spendsMain, float reserve)
        {
            float givenUp = Math.Max(0f, walkGivenUp);
            if (spendsMain) givenUp = Math.Max(givenUp, Math.Max(0f, bestMain));
            return Math.Max(0f, gross - givenUp) * Clamp(reserve, 0f, 1f);
        }

        /// <summary>
        /// ★ v0.3.70 P2 (author-approved): a summon AI Control drives declines an ally's area effect it has accepted (Inspired
        /// Rage's acceptance toggle) when what the effect stops - its spells' measured edge per round - beats what the effect
        /// gives it per round, by at least <see cref="AcceptSwitchMargin"/> points. Unmeasured: nothing is declined.
        /// </summary>
        internal static bool DeclinesGrant(float stoppedPerRound, float givenPerRound) =>
            stoppedPerRound > Math.Max(0f, givenPerRound) + AcceptSwitchMargin;

        /// <summary>
        /// ★ v0.3.70 P2: a declined effect is accepted again when what it gives clearly beats what it stops (a quarter more and
        /// the margin), so the switch does not flip back and forth between two close values.
        /// </summary>
        internal static bool ReacceptsGrant(float stoppedPerRound, float givenPerRound) =>
            givenPerRound > Math.Max(0f, stoppedPerRound) * 1.25f + AcceptSwitchMargin;

        internal const float AcceptSwitchMargin = 0.5f;

        /// <summary>
        /// ★ v0.3.23 (build5 review): the share of a new summon's worth left while <paramref name="ownSummons"/> creatures
        /// the same caster summoned still fight: 1 / (1 + n) - a second creature is worth half, a fourth a quarter. An AI
        /// policy, not a game rule: each further creature has fewer enemies left to itself, and a day's summons are not
        /// spent at once on a fight already covered. A negative count is treated as none.
        /// </summary>
        public static float SummonCrowdShare(int ownSummons) => 1f / (1f + Math.Max(0, ownSummons));
    }
}
