using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    // Geometry hints only. The caller still checks the actual native affected space and movement legality.
    internal static class SwarmGeometry
    {
        internal readonly struct Circle
        {
            internal readonly float X, Z, Radius;
            internal Circle(float x, float z, float radius) { X = x; Z = z; Radius = radius; }
        }

        /// <summary>First exit from the connected union containing the start along a unit ray. No sampling rings,
        /// temporary arrays or sorting. Disjoint circles beyond the exit do not make a short escape unnecessarily long.</summary>
        internal static float ExitDistance(IReadOnlyList<Circle> circles, float x, float z, float dx, float dz,
            float maximum, float clearance)
        {
            float length = dx * dx + dz * dz;
            if (!(length > 0f) || !(maximum > 0f)) return -1f;
            float exit = 0f;
            bool inside = false;
            for (int pass = 0; pass <= circles.Count; pass++)
            {
                float before = exit;
                for (int i = 0; i < circles.Count; i++)
                {
                    var c = circles[i];
                    float ox = x - c.X, oz = z - c.Z, radius = c.Radius + clearance;
                    float dot = ox * dx + oz * dz;
                    float discriminant = dot * dot - length * (ox * ox + oz * oz - radius * radius);
                    if (!(radius >= 0f) || discriminant < 0f) continue;
                    float root = (float)Math.Sqrt(discriminant);
                    float near = (-dot - root) / length, far = (-dot + root) / length;
                    if (near <= exit && far >= exit && far > 0f)
                    { inside = true; if (far > exit) exit = far; }
                }
                if (exit > maximum) return -1f;
                if (exit == before) return inside ? exit : -1f;
            }
            return -1f; // Invalid/non-finite input cannot manufacture an exit.
        }
    }
}
