// ★ v0.3.30 competence3 (author's play-test note: the party idled for about twenty turn-based rounds while the last enemy,
// an invisible Cultist Conjurer held in Orvath's Grease, stayed hidden): where to aim a revealing area and which source to
// spend on it. Pure, unit-tested; the game side is TacticalEngine.Hidden.cs and HiddenEnemies.cs.
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class HiddenRules
    {
        /// <summary>
        /// The spot (index into <paramref name="xs"/>/<paramref name="zs"/>) whose circle of <paramref name="radius"/> holds the
        /// most of the spots, itself included; the first such spot on a tie. -1 (and <paramref name="covered"/> 0) for no spots
        /// or unusable input. Horizontal distance only: an area spreads on the ground around its point.
        /// </summary>
        public static int CoveringSpot(IReadOnlyList<float> xs, IReadOnlyList<float> zs, float radius, out int covered)
        {
            covered = 0;
            if (xs == null || zs == null || xs.Count == 0 || xs.Count != zs.Count) return -1;
            float r = Math.Max(0f, radius), squared = r * r;
            int best = -1;
            for (int i = 0; i < xs.Count; i++)
            {
                if (float.IsNaN(xs[i]) || float.IsNaN(zs[i])) continue;
                int count = 0;
                for (int j = 0; j < xs.Count; j++)
                {
                    float dx = xs[i] - xs[j], dz = zs[i] - zs[j];
                    if (dx * dx + dz * dz <= squared) count++;   // a NaN spot compares false and is not counted
                }
                if (count > covered)
                {
                    covered = count;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// What using a revealing source costs, lowest first: one that spends nothing, then a spell slot (lower levels
        /// first), then an item charge, then an item that is used up (a scroll or potion cannot be had back by resting).
        /// </summary>
        public static float SourceCost(bool costsNothing, bool item, bool usedUp, int spellLevel)
        {
            if (costsNothing) return 0f;
            if (item) return usedUp ? 30f : 20f;
            return 1f + Math.Max(0, Math.Min(10, spellLevel));
        }
    }
}
