using System;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>
    /// Whether an ally's committed approach ends in, or walks into, a new circle: <see cref="FullyInside"/> (wherever it
    /// stops), or ★ v0.3.27 <see cref="WalkMeetsCircle"/> (its straight walk to where it stops). Unknown inputs never invent
    /// a conflict.
    /// </summary>
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

        /// <summary>
        /// ★ v0.3.27 (test3 log, real-time fight 10): the ally's straight walk from where it stands to where it stops - its
        /// approach radius short of the target, on its own side - comes within the circle with its body. Camellia put Grease
        /// on a Corrupted Mongrel Brute while Seelah walked to the Ranger beside it, inside the same circle; her stop was not
        /// wholly inside (<see cref="FullyInside"/>), so the cast went ahead, and she fell four times and lay prone ~15 s.
        /// Horizontal coordinates (x, z). False when the ally has already arrived or any input is unusable.
        /// </summary>
        public static bool WalkMeetsCircle(float allyX, float allyZ, float targetX, float targetZ, float approachRadius,
                                           float allyCorpulence, float centerX, float centerZ, float areaRadius)
        {
            if (!Finite(allyX) || !Finite(allyZ) || !Finite(targetX) || !Finite(targetZ) || !Finite(approachRadius) ||
                !Finite(allyCorpulence) || !Finite(centerX) || !Finite(centerZ) || !Finite(areaRadius) ||
                approachRadius < 0f || allyCorpulence < 0f || areaRadius <= 0f) return false;
            double dx = allyX - targetX, dz = allyZ - targetZ;
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (!(length > approachRadius)) return false;   // already within reach: no walk left
            double share = approachRadius / length;
            double stopX = targetX + dx * share, stopZ = targetZ + dz * share;
            // Distance from the circle's centre to the walk (ally -> stop).
            double wx = stopX - allyX, wz = stopZ - allyZ;
            double lengthSq = wx * wx + wz * wz;
            double t = lengthSq < 1e-8 ? 0d : ((centerX - allyX) * wx + (centerZ - allyZ) * wz) / lengthSq;
            t = Math.Max(0d, Math.Min(1d, t));
            double px = allyX + wx * t - centerX, pz = allyZ + wz * t - centerZ;
            return Math.Sqrt(px * px + pz * pz) < (double)areaRadius + allyCorpulence;
        }

        /// <summary>
        /// ★ v0.3.27: the game's cylinder areas are 100 m tall (AreaEffectView: ScriptZoneCylinder.Height = 100, compared
        /// with |y - centre| in ScriptZoneCylinder.Contains); a smaller height gap does not take a unit out of one.
        /// </summary>
        public const float CylinderHalfHeight = 100f;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
