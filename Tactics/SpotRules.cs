// ★ v0.3.29 test 4 (author-approved, body blocking): where a real-time walk to an enemy or an ally ends, and whether another
// body or another companion's walk already takes that spot (TacticalEngine.Spots.cs). Pure geometry, unit-tested.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class SpotRules
    {
        /// <summary>
        /// Two bodies closer than this share of their summed radii (the game's corpulence) take the same spot. The share the
        /// approach search has used for spots around harmful areas since v0.3.17.
        /// </summary>
        public const float Overlap = 0.9f;

        /// <summary>
        /// Approaches up to this range (metres, centre to centre: weapon reach or touch plus both bodies) end in body contact
        /// with the target. A cast from further off stands well clear of the crowd round it.
        /// </summary>
        public const float ContactRange = 4f;

        /// <summary>Free spots fully priced (attacks of opportunity, areas, exposure) per crowded approach, nearest first.</summary>
        public const int PricedSpots = 4;

        /// <summary>Bodies this far apart in height stand on different floors (a bridge, a stair) and take no spot of each other.</summary>
        public const float SameFloor = 2f;

        /// <summary>Spot claims are read in real time, for approaches that end in body contact, while the unit can still move.</summary>
        public static bool Applies(bool realTime, float range, bool noMovementLeft) =>
            realTime && !noMovementLeft && range > 0f && range <= ContactRange;

        /// <summary>
        /// A body of <paramref name="bodyRadius"/> at (<paramref name="bodyX"/>, <paramref name="bodyZ"/>) takes the spot
        /// (<paramref name="spotX"/>, <paramref name="spotZ"/>) for a unit of <paramref name="ownRadius"/>. Unusable input
        /// takes nothing.
        /// </summary>
        public static bool Takes(float bodyX, float bodyZ, float bodyRadius, float spotX, float spotZ, float ownRadius)
        {
            float dx = bodyX - spotX, dz = bodyZ - spotZ;
            float room = (Math.Max(0f, bodyRadius) + Math.Max(0f, ownRadius)) * Overlap;
            float squared = dx * dx + dz * dz;
            if (float.IsNaN(squared) || float.IsNaN(room)) return false;
            return squared < room * room;
        }

        /// <summary>
        /// ★ v0.3.29 test 5: seconds an ally's walk counts on while a cast is under way (a cast takes 1-1.5 s and a cone or
        /// burst lands about then). Seelah walked 4.4 m in 1.8 s into Orvath's Fan of Flames (v0.3.29 test 4 real-time log).
        /// </summary>
        public const float WalkInSeconds = 2f;

        /// <summary>Metres between the points of a walk tested for walking into an area.</summary>
        public const float WalkInStep = 0.5f;

        /// <summary>
        /// ★ v0.3.29 test 5: the stretch of a walk of <paramref name="distance"/> metres covered in <paramref name="seconds"/>
        /// at <paramref name="speed"/> (<paramref name="covered"/>), and how many points <paramref name="step"/> apart test
        /// it, the last at its end. Nothing for unusable input.
        /// </summary>
        public static int WalkSteps(float distance, float speed, float seconds, float step, out float covered)
        {
            covered = 0f;
            if (!(distance > 0f) || !(speed > 0f) || !(seconds > 0f) || !(step > 0f) || float.IsInfinity(distance)) return 0;
            covered = Math.Min(distance, speed * seconds);
            return (int)Math.Ceiling(covered / step);
        }

        /// <summary>
        /// Where a straight walk from (<paramref name="fromX"/>, <paramref name="fromZ"/>) to within
        /// <paramref name="radius"/> of (<paramref name="toX"/>, <paramref name="toZ"/>) stops: on the line between them, 90 %
        /// of the radius short of the point (TacticalEngine.ApproachPoint), or where it stands when it is that close already.
        /// </summary>
        public static void WalkEnd(float fromX, float fromZ, float toX, float toZ, float radius, out float endX, out float endZ)
        {
            float dx = fromX - toX, dz = fromZ - toZ;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (!(distance > 0.001f) || float.IsNaN(radius))
            {
                endX = toX;
                endZ = toZ;
                return;
            }
            float keep = Math.Min(distance, Math.Max(0f, radius) * 0.9f) / distance;
            endX = toX + dx * keep;
            endZ = toZ + dz * keep;
        }
    }
}
