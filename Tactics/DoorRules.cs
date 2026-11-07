// ★ v0.3.29 test 7 (v0.3.29 test 6 turn-based log, the author's note): a closed door between a unit and an enemy it cannot
// see (TacticalEngine.Doors.cs). Pure geometry, unit-tested.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class DoorRules
    {
        /// <summary>Metres to either side of the straight line to the enemy a door may stand and still be in the way.</summary>
        public const float Corridor = 3f;

        /// <summary>Metres of height between a door and the line beyond which they are on different floors.</summary>
        public const float SameFloor = 3f;

        /// <summary>
        /// ★ v0.3.29 test 7: what opening a door to an enemy is worth beyond an advance on it: the advance cannot get past
        /// the closed door, the opening can - and lets the rest of the party through.
        /// </summary>
        public const float OverAdvance = 0.5f;

        /// <summary>
        /// The door at (<paramref name="doorX"/>, <paramref name="doorZ"/>) stands on the way from the unit
        /// (<paramref name="fromX"/>, <paramref name="fromZ"/>) to the enemy (<paramref name="toX"/>, <paramref name="toZ"/>):
        /// along the line between them (not behind the unit, at most 1.5 m beyond the enemy) and within
        /// <see cref="Corridor"/> of it. Unusable input is not on the way.
        /// </summary>
        public static bool OnTheWay(float fromX, float fromZ, float toX, float toZ, float doorX, float doorZ)
        {
            float lx = toX - fromX, lz = toZ - fromZ;
            float length = (float)Math.Sqrt(lx * lx + lz * lz);
            if (!(length > 0.5f) || float.IsInfinity(length)) return false;
            float ux = lx / length, uz = lz / length;
            float dx = doorX - fromX, dz = doorZ - fromZ;
            float along = dx * ux + dz * uz;
            float aside = Math.Abs(dx * uz - dz * ux);
            if (float.IsNaN(along) || float.IsNaN(aside)) return false;
            return along > 0f && along <= length + 1.5f && aside <= Corridor;
        }
    }
}
