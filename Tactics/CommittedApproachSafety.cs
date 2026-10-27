using System;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>Geometric proof only: unknown or boundary-overlapping arrival regions do not prove a future catch.</summary>
    public static class CommittedApproachSafety
    {
        public static bool FullyInside(float centerDistance, float approachRadius, float allyCorpulence, float areaRadius)
        {
            if (!Finite(centerDistance) || !Finite(approachRadius) || !Finite(allyCorpulence) || !Finite(areaRadius) ||
                centerDistance < 0f || approachRadius <= 0f || allyCorpulence < 0f || areaRadius <= 0f) return false;
            // The native approach radius already defines the possible actor-centre disk. Requiring its entire body
            // disk inside the actual circle is stricter than the ordinary radius+corpulence overlap test.
            return (double)centerDistance + approachRadius + allyCorpulence < areaRadius;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
