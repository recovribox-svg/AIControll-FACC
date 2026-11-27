using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        // With one independent extra die and the lower kept, both dice must pass.
        internal static float WorstOfTwoLostShare(float pass) => pass > 0f ? 1f - Clamp(pass, 0f, 1f) : 0f;

        internal static float RedirectedAttackWorth(float chance, float threatened, bool beneficiaryCertain) =>
            beneficiaryCertain ? Clamp(chance, 0f, 1f) * Math.Max(0f, threatened) : 0f;

        /// <summary>
        /// ★ v0.3.23 (build5 review): the share of a new summon's worth left while <paramref name="ownSummons"/> creatures
        /// the same caster summoned still fight: 1 / (1 + n) - a second creature is worth half, a fourth a quarter. An AI
        /// policy, not a game rule: each further creature has fewer enemies left to itself, and a day's summons are not
        /// spent at once on a fight already covered. A negative count is treated as none.
        /// </summary>
        public static float SummonCrowdShare(int ownSummons) => 1f / (1f + Math.Max(0, ownSummons));
    }
}
