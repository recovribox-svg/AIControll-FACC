// ★ v0.3.50 playtest review: recognised automatic swarm harm is not an attack roll. Keep it in the full
// incoming stream, but do not credit armour, concealment or mirror images with preventing that component.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// Damage prevented by an AC change against the same native roll. For an incoming bonus the alternative has
        /// more AC; for a held bonus it has less. The unchanged concealment factor cancels in this relative change.
        /// </summary>
        public static float ArmorProtectionFromRolls(float incoming, float currentHit, float alternativeHit, bool held)
        {
            if (!(incoming > 0f) || float.IsInfinity(incoming) || !(currentHit > 0f && currentHit <= 1f) ||
                !(alternativeHit >= 0f && alternativeHit <= 1f)) return 0f;
            return incoming * (held ? alternativeHit - currentHit : currentHit - alternativeHit) / currentHit;
        }

        /// <summary>
        /// The holder's current danger already includes this buff. Its loss is the existing model's penalty for
        /// removing that protection, reversed in sign, rather than a further improvement from current danger.
        /// </summary>
        public static float HeldPreventionLoss(float perRound, float rounds, float hp, float currentIncoming, float allyWorth)
            => -PreventionValue(-perRound, rounds, hp, currentIncoming, allyWorth);

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
