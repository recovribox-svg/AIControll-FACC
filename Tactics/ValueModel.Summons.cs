using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// ★ v0.3.23 (build5 review): the share of a new summon's worth left while <paramref name="ownSummons"/> creatures
        /// the same caster summoned still fight: 1 / (1 + n) - a second creature is worth half, a fourth a quarter. An AI
        /// policy, not a game rule: each further creature has fewer enemies left to itself, and a day's summons are not
        /// spent at once on a fight already covered. A negative count is treated as none.
        /// </summary>
        public static float SummonCrowdShare(int ownSummons) => 1f / (1f + Math.Max(0, ownSummons));
    }
}
