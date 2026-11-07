// ★ v0.3.29 test 7 (v0.3.29 test 6 turn-based log, the author's note): a closed door between a unit and an enemy it cannot
// see (TacticalEngine.Doors.cs). Pure geometry, unit-tested.
// ★ v0.3.29 test 8: "on the way" is how much longer the way through the door is than the straight line (the detour), not
// a 3 m corridor along that line - a door in the wall of the next room stands well off the line to an enemy in its corner.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class DoorRules
    {
        /// <summary>Metres the way through a door may be longer than the straight line and the door still be on the way...</summary>
        public const float MinDetour = 4f;

        /// <summary>...or this share of the straight line, when that is more (a far enemy, a room further on).</summary>
        public const float DetourShare = 0.5f;

        /// <summary>Metres of height between a door and the line beyond which they are on different floors.</summary>
        public const float SameFloor = 3f;

        /// <summary>
        /// ★ v0.3.29 test 7: what opening a door to an enemy is worth beyond an advance on it: the advance cannot get past
        /// the closed door, the opening can - and lets the rest of the party through.
        /// </summary>
        public const float OverAdvance = 0.5f;

        /// <summary>
        /// ★ v0.3.29 test 8: how many metres longer the way from the unit (<paramref name="fromX"/>, <paramref name="fromZ"/>)
        /// through the door (<paramref name="doorX"/>, <paramref name="doorZ"/>) to the enemy (<paramref name="toX"/>,
        /// <paramref name="toZ"/>) is than the straight line between them; NaN for unusable input or an enemy where the
        /// unit stands.
        /// </summary>
        public static float Detour(float fromX, float fromZ, float toX, float toZ, float doorX, float doorZ)
        {
            float direct = Distance(fromX, fromZ, toX, toZ);
            if (!(direct > 0.5f) || float.IsInfinity(direct)) return float.NaN;
            float through = Distance(fromX, fromZ, doorX, doorZ) + Distance(doorX, doorZ, toX, toZ);
            if (float.IsNaN(through) || float.IsInfinity(through)) return float.NaN;
            return Math.Max(0f, through - direct);
        }

        /// <summary>The longest detour that still counts as on the way to an enemy <paramref name="direct"/> metres off.</summary>
        public static float MaxDetour(float direct) => Math.Max(MinDetour, DetourShare * Math.Max(0f, direct));

        /// <summary>
        /// The door stands on the way from the unit to the enemy: walking through it is at most <see cref="MaxDetour"/>
        /// longer than the straight line. Unusable input is not on the way.
        /// </summary>
        public static bool OnTheWay(float fromX, float fromZ, float toX, float toZ, float doorX, float doorZ, out float detour)
        {
            detour = Detour(fromX, fromZ, toX, toZ, doorX, doorZ);
            if (float.IsNaN(detour)) return false;
            return detour <= MaxDetour(Distance(fromX, fromZ, toX, toZ));
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
