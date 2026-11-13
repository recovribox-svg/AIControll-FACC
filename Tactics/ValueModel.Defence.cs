// ★ v0.3.50 playtest review: recognised automatic swarm harm is not an attack roll. Keep it in the full
// incoming stream, but do not credit armour, concealment or mirror images with preventing that component.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// The existing calibrated incoming estimate, apportioned by the same raw threat weights after known
        /// automatic swarm sources are excluded. The remainder retains the existing ordinary-enemy approximation;
        /// this is not a claim that all of its spells or other effects make attack rolls.
        /// </summary>
        public static float AttackMitigationIncoming(float incoming, float eligibleWeight, float totalWeight)
        {
            if (!(incoming > 0f) || float.IsInfinity(incoming) || !(totalWeight > 0f) ||
                float.IsInfinity(totalWeight) || !(eligibleWeight > 0f) || float.IsInfinity(eligibleWeight)) return 0f;
            return incoming * Math.Min(1f, eligibleWeight / totalWeight);
        }
    }
}
