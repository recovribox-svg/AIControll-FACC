using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// ★ v0.3.23 (Carnage task): what an effect fired by a kill is worth on the attack that may make it - Dreadful
        /// Carnage's free Dazzling Display (AddOutgoingDamageTrigger, TargetKilledByThisDamage): the chance the attack
        /// takes its target to 0 HP times what the effect does to each other enemy within <paramref name="radius"/> of
        /// where the attack is made. The killed target (<paramref name="killedIndex"/>) gets nothing, each recipient
        /// counts once whatever the number of swings, and a negative worth counts as nothing.
        /// </summary>
        public static float KillTriggerCredit(float killChance, IReadOnlyList<float> distances, IReadOnlyList<float> worths,
                                              int killedIndex, float radius)
        {
            if (distances == null || worths == null || float.IsNaN(killChance) || killChance <= 0f) return 0f;
            float sum = 0f;
            int n = Math.Min(distances.Count, worths.Count);
            for (int i = 0; i < n; i++)
            {
                if (i == killedIndex || float.IsNaN(distances[i]) || distances[i] > radius) continue;
                float w = worths[i];
                if (w > 0f && !float.IsInfinity(w)) sum += w;
            }
            return Math.Min(1f, killChance) * sum;
        }
    }
}
