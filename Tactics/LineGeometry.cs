// ★ Fusion coverage C4 (author-approved, docs/FUSION_COVERAGE_REST.md): flat line geometry for choosing where to aim a line
// and where to stand to line enemies up. It is only a cheap pre-filter over a few directions and spots; every aim it picks is
// valued again through the native line query (ProjectileLine: WouldTargetUnitLine), which decides who is really caught.
// The formula is the one native WouldTargetPointLine applies (AbilityDeliverProjectile, supplied assembly): from the launch
// point (the caster's eye moved forward by its body), a unit is caught when it is in front (along > 0), before the line's
// travel plus its body, and no further to the side than half the line's width plus its body. Pure: no game types (unit-tested).
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    internal static class LineGeometry
    {
        /// <summary>
        /// Native WouldTargetPointLine on the ground plane: the point (<paramref name="x"/>, <paramref name="z"/>) with
        /// <paramref name="radius"/> is caught by the line launched at (<paramref name="launchX"/>, <paramref name="launchZ"/>) along
        /// the unit direction (<paramref name="dirX"/>, <paramref name="dirZ"/>).
        /// </summary>
        public static bool Catches(float launchX, float launchZ, float dirX, float dirZ, float travel, float halfWidth,
                                   float x, float z, float radius)
        {
            float rx = x - launchX, rz = z - launchZ;
            float along = rx * dirX + rz * dirZ;
            if (along <= 0f || along >= travel + radius) return false;
            float sx = rx - dirX * along, sz = rz - dirZ * along;
            float width = halfWidth + radius;
            return sx * sx + sz * sz <= width * width;
        }

        /// <summary>The unit direction from (<paramref name="fromX"/>, <paramref name="fromZ"/>) to the point; false when they coincide.</summary>
        public static bool Direction(float fromX, float fromZ, float toX, float toZ, out float dirX, out float dirZ)
        {
            dirX = toX - fromX;
            dirZ = toZ - fromZ;
            float length = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (!(length > 1e-3f)) { dirX = dirZ = 0f; return false; }
            dirX /= length;
            dirZ /= length;
            return true;
        }

        /// <summary>The direction turned by <paramref name="degrees"/> about the vertical (as the cone's turned aims).</summary>
        public static void Turn(float dirX, float dirZ, float degrees, out float turnedX, out float turnedZ)
        {
            double rad = degrees * Math.PI / 180d;
            float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
            turnedX = dirX * cos + dirZ * sin;
            turnedZ = dirZ * cos - dirX * sin;
        }

        /// <summary>The signed angle in degrees that turns direction a onto direction b (the sense of <see cref="Turn"/>).</summary>
        public static float AngleBetween(float ax, float az, float bx, float bz)
        {
            // Turn(a, +d) = (ax cos + az sin, az cos - ax sin): positive turns go from a towards (az, -ax).
            double cross = ax * bz - az * bx;
            double dot = ax * bx + az * bz;
            return (float)(-Math.Atan2(cross, dot) * 180d / Math.PI);
        }

        /// <summary>
        /// The spot to stand on to line up <paramref name="near"/> and <paramref name="far"/>: on the line through them, behind the
        /// near one (the far one beyond it), at least <paramref name="minBack"/> and at most <paramref name="maxBack"/> from the near
        /// one, and nearest to the caster at (<paramref name="casterX"/>, <paramref name="casterZ"/>). False when no such spot exists.
        /// </summary>
        public static bool BehindSpot(float casterX, float casterZ, float nearX, float nearZ, float farX, float farZ,
                                      float minBack, float maxBack, out float spotX, out float spotZ)
        {
            spotX = spotZ = 0f;
            if (!(maxBack >= minBack) || !(minBack >= 0f)) return false;
            if (!Direction(farX, farZ, nearX, nearZ, out float backX, out float backZ)) return false;
            float t = (casterX - nearX) * backX + (casterZ - nearZ) * backZ;
            t = Math.Max(minBack, Math.Min(maxBack, t));
            spotX = nearX + backX * t;
            spotZ = nearZ + backZ * t;
            return true;
        }

        /// <summary>
        /// How far behind the near unit a caster may stand for the far unit, <paramref name="gap"/> beyond it, to be caught still:
        /// the far one's distance from the launch point (the caster's position plus its body) must stay under the travel plus its
        /// body, less <paramref name="margin"/>.
        /// </summary>
        public static float MaxBack(float travel, float farRadius, float gap, float casterBody, float margin) =>
            travel + farRadius + casterBody - gap - margin;
    }
}
