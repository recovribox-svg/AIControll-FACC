using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// Share of incoming offence with matching energy evidence. Uses the existing observed-offence prior;
        /// unknown physical/custom damage does not become an invented generic energy percentage.
        /// </summary>
        public static float EnergyExposureShare(float observedEnergy, float observedTotal, float predictedEnergy, float predictedTotal)
        {
            float total = ObservedOffence(observedTotal, 0, predictedTotal);
            if (!(total > 0f)) return 0f;
            float energy = ObservedOffence(observedEnergy, 0, predictedEnergy);
            return Math.Max(0f, Math.Min(1f, energy / total));
        }

        /// <summary>
        /// Conservative same-energy packet size: native per-victim damage of a known cast (before hit/save chance),
        /// or the mean from completed same-energy damage events, whichever is larger. A cast/event may contain
        /// several real packets; aggregating them underprices resistance rather than inventing extra small hits.
        /// </summary>
        public static float EnergyPacketSize(float predictedPerCast, float observedDamage, int observedEvents) =>
            Math.Max(Math.Max(0f, predictedPerCast), observedEvents > 0 ? Math.Max(0f, observedDamage) / observedEvents : 0f);

        /// <summary>Typed packet evidence supplies the hit rate. Unknown size cannot borrow weapon hit frequency.</summary>
        public static float EnergyHitsPerRound(float incoming, float packetSize) =>
            incoming > 0f && packetSize > 0f ? incoming / packetSize : 0f;

        /// <summary>
        /// Expected extra prevention against matching incoming energy only. The resistance and existing resistance
        /// come from native component values. A finite pool is spent on all damage that protection absorbs, not just
        /// the improvement over existing resistance. This is a constant-stream appraisal, not a native damage rule.
        /// Overlap with an already active absorption pool is conservatively unpriced until that pool is exhausted.
        /// </summary>
        public static float EnergyProtectionPerRound(float incoming, float hitsPerRound, float rounds,
            float resistance, float? pool, bool immunity, float existingResistance, bool overlapsActivePool)
        {
            if (!(incoming > 0f) || !(rounds > 0f) || overlapsActivePool) return 0f;
            if (immunity) return incoming;
            if (!(hitsPerRound > 0f) || !(resistance > 0f)) return 0f;
            existingResistance = Math.Max(0f, existingResistance);
            float residualPerHit = incoming / hitsPerRound;
            float improvement = Math.Min(residualPerHit, Math.Max(0f, resistance - existingResistance));
            float rate = improvement * hitsPerRound;
            if (pool.HasValue)
            {
                if (!(pool.Value > 0f)) return 0f;
                float consumed = Math.Min(resistance, residualPerHit + existingResistance);
                float usefulPool = consumed > 0f ? pool.Value * improvement / consumed : 0f;
                rate = Math.Min(rate, usefulPool / rounds);
            }
            return Math.Max(0f, Math.Min(incoming, rate));
        }
    }
}
