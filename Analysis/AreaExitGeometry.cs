// Numerical intersection only. The caller supplies the native query's world-space geometry and actual sphere bound.
using System;

namespace CompanionAI_Pathfinder.Analysis
{
    internal static class AreaExitGeometry
    {
        internal static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        internal static bool CapsuleOverlaps(float x, float y, float z, float halfHeight, float radius, float body)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(halfHeight) || !Finite(radius) || !Finite(body) ||
                halfHeight < 0f || radius < 0f || body < 0f) return true; // Unknown geometry cannot buy escape credit.
            double dy = Math.Max(0d, Math.Abs((double)y) - halfHeight), r = (double)radius + body;
            return (double)x * x + dy * dy + (double)z * z <= r * r;
        }

        internal static bool BoxOverlaps(float x, float y, float z, float hx, float hy, float hz, float body)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(hx) || !Finite(hy) || !Finite(hz) || !Finite(body) ||
                hx < 0f || hy < 0f || hz < 0f || body < 0f) return true;
            double dx = Math.Max(0d, Math.Abs((double)x) - hx), dy = Math.Max(0d, Math.Abs((double)y) - hy),
                dz = Math.Max(0d, Math.Abs((double)z) - hz);
            return dx * dx + dy * dy + dz * dz <= (double)body * body;
        }
    }
}
