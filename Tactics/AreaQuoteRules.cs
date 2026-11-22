using System;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>Work and uncertainty bounds for a point dispel or a newly permitted hazardous advance.</summary>
    public static class AreaQuoteRules
    {
        public const int MaxAreas = 64, MaxRecipients = 128, MaxPathPoints = 64;
        public const float MaxPathMetres = 60f, MaxPathSeconds = 6f;

        public static bool PathWithinBudget(int points, float length, float speed) =>
            points > 0 && points <= MaxPathPoints && Finite(length) && length >= 0f && length <= MaxPathMetres &&
            Finite(speed) && speed > 0f && length / speed <= MaxPathSeconds;

        /// <summary>The entry segment supplies a provisional price, never permission for arbitrary later paths.</summary>
        public static bool CoversActualRisk(float quotedCost, float quotedStop, float actualCost, float actualStop) =>
            Finite(quotedCost) && quotedCost >= 0f && Finite(actualCost) && actualCost >= 0f &&
            Finite(quotedStop) && quotedStop >= 0f && quotedStop <= 1f &&
            Finite(actualStop) && actualStop >= 0f && actualStop <= 1f &&
            actualCost <= quotedCost + 0.001f && actualStop <= quotedStop + 0.001f;

        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    }
}
