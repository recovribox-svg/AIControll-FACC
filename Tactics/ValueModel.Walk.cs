using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>Real time: the seconds of one round (the game's action cooldowns and the engine's horizon).</summary>
        public const float RealTimeRoundSeconds = 6f;

        /// <summary>
        /// ★ v0.3.23 (build3 logs): real time, an action that walks first comes that much later. Walking during the action's
        /// own cooldown is free; the rest of the walk delays it. The same action later is worth it at the rate it is made:
        /// round / (round + delay) - nothing lost with no delay, half after a whole round of walking. Seelah walked 13.6 m
        /// (about 4.4 s) past the enemy front line to a Cultist Archer and was shot and clawed on the way.
        /// </summary>
        public static float RealTimeWalkFactor(float walkSeconds, float cooldownLeftSeconds)
        {
            float delay = Math.Max(0f, walkSeconds - Math.Max(0f, cooldownLeftSeconds));
            if (float.IsNaN(delay) || float.IsInfinity(delay)) return 1f;
            return RealTimeRoundSeconds / (RealTimeRoundSeconds + delay);
        }

        /// <summary>
        /// ★ v0.3.23 (build3 logs), shared since v0.3.27: a melee enemy <paramref name="distanceToWay"/> metres from the walk
        /// steps into it before the unit has passed when it can close the gap beyond its reach in half the walk's time.
        /// </summary>
        /// <summary>
        /// ★ v0.3.27 (the author's request after the test 3 log): the chance an enemy goes for one party member before its
        /// next turn. A melee enemy with party members in its reach fights one of those (moving away provokes); with none in
        /// reach - or a ranged enemy or caster - one of those it can reach this round. Each candidate is picked by how easily
        /// the enemy hits it (<paramref name="weight"/> of <paramref name="weightSum"/>, see <see cref="HitWeight"/>).
        /// </summary>
        public static float TargetChance(bool whoInReach, float reachWeightSum, bool whoInZone, float zoneWeightSum, float weight)
        {
            if (!(weight > 0f)) return 0f;
            if (reachWeightSum > 0f) return whoInReach ? Math.Min(1f, weight / reachWeightSum) : 0f;
            return whoInZone && zoneWeightSum > 0f ? Math.Min(1f, weight / zoneWeightSum) : 0f;
        }

        /// <summary>
        /// ★ v0.3.27: how easily an attack bonus hits an Armor Class - d20 + attack ≥ AC, a natural 1 always misses and a
        /// natural 20 always hits. Used relatively (a front-liner in heavy armour is attacked less often and hit less
        /// hard than a squishy companion), not as the exact roll.
        /// </summary>
        public static float HitWeight(int attackBonus, int armorClass) =>
            Math.Min(0.95f, Math.Max(0.05f, (21 + attackBonus - armorClass) / 20f));

        /// <summary>
        /// ★ v0.3.27: the chance a party member goes down before its next turn when each enemy comes for it with chance
        /// <c>Item1</c> and then drops it with chance <c>Item2</c>: 1 - Π(1 - chance × drop).
        /// </summary>
        public static float CombinedDrop(System.Collections.Generic.IEnumerable<(float chance, float drop)> threats)
        {
            double stays = 1d;
            foreach (var t in threats)
            {
                if (!(t.chance > 0f) || !(t.drop > 0f)) continue;
                stays *= 1d - Math.Min(1f, t.chance) * Math.Min(1f, t.drop);
            }
            return (float)(1d - stays);
        }

        /// <summary>
        /// ★ v0.3.27 (author-approved): what a healthy unit's walk costs for where it ends - its higher chance to drop before
        /// its next turn there (above 2 %, as ExposureCost), times its worth and its role's tolerance - less what the allies
        /// it takes enemies off gain (<paramref name="shielding"/>). Never below 0: shielding cancels the price, it does not
        /// pay the unit to walk in. Camellia (Skald 2, full HP) walked up to a Large Water Elemental (25 a slam) and went down
        /// to it and a wolf; a front-liner stepping in front of a heavy hitter that is on a squishier ally pays little or nothing.
        /// </summary>
        public static float HealthyWalkExposure(float dropHere, float dropThere, float worth, float tolerance, float shielding)
        {
            float rise = dropThere - dropHere;
            if (!(rise > 0.02f) || !(worth > 0f) || float.IsInfinity(rise)) return 0f;
            float cost = rise * worth * Math.Max(0f, tolerance) - (shielding > 0f && !float.IsInfinity(shielding) ? shielding : 0f);
            return cost > 0f ? cost : 0f;
        }

        public static bool CanIntercept(float distanceToWay, float reach, float enemySpeedMps, float walkSeconds)
        {
            if (float.IsNaN(distanceToWay) || float.IsNaN(walkSeconds) || walkSeconds <= 0f) return false;
            return distanceToWay - Math.Max(0f, reach) <= Math.Max(0f, enemySpeedMps) * walkSeconds * 0.5f;
        }
    }
}
