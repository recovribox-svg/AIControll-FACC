using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        // v0.3.22 fixes review (PR #15) / Recommendation A: the chance that a chosen spell is really delivered. The native
        // arcane failure chance discounts the spell's expected benefit exactly once, after the walk and before the costs that
        // do not depend on the spell working (provoking, concentration, the walk's risks). The same single factor goes into
        // an infusion's delivery and into Progressive viability, so none of them discounts twice or forgets it.

        /// <summary>A spell cast at the end of a walk keeps this share of its value (the walk can be cut short).</summary>
        public const float WalkThenCastFactor = 0.9f;

        /// <summary>An archer walking up to touch an enemy gives up its ranged attacks: this share of the value is kept.</summary>
        public const float ArcherTouchFactor = 0.5f;

        /// <summary>The native RuleCalculateArcaneSpellFailureChance result (percent) as a chance that the cast goes off.</summary>
        public static float ArcaneCastingChance(float failurePercent) => 1f - Clamp01(failurePercent / 100f);

        /// <summary>The spell's expected benefit after the walk and the delivery chance, before any risk or provoke cost.</summary>
        public static float DeliveredBenefit(float scored, bool needsMove, float deliveryChance) =>
            scored * (needsMove ? WalkThenCastFactor : 1f) * Clamp01(deliveryChance);

        /// <summary>Every multiplicative discount of the spell's benefit, for the infusion added to it (each applied once).</summary>
        public static float InfusionDeliveryFactor(float consumable, bool needsMove, float deliveryChance, float provokeStopChance,
            float concentrationChance, bool archerTouch, float walkStopChance) =>
            Clamp01(consumable) * (needsMove ? WalkThenCastFactor : 1f) * Clamp01(deliveryChance) *
            (1f - Clamp01(provokeStopChance)) * Clamp01(concentrationChance) * (archerTouch ? ArcherTouchFactor : 1f) *
            (1f - Clamp01(walkStopChance));

        /// <summary>Progressive viability: the chance the spell affects its target, times the chance it is delivered at all.</summary>
        public static float DeliveredSuccessChance(float effectChance, float deliveryChance) =>
            Clamp01(effectChance) * Clamp01(deliveryChance);

        /// <summary>
        /// ★ Fusion coverage C4: a cast's value once its walk is paid for - the same discounts the decision applies after scoring
        /// (DeliveredBenefit's walk share, the real-time walk factor, the walk's stop chance and cost), without the delivery
        /// chance both aims share. For comparing two aims of one cast, never added to a value.
        /// </summary>
        public static float WalkedNet(float scored, bool walks, float walkFactor, float stopChance, float cost) =>
            !(scored > 0f) ? scored
            : scored * (walks ? WalkThenCastFactor : 1f) * Clamp01(walkFactor) * (1f - Clamp01(stopChance)) - Math.Max(0f, cost);

        /// <summary>★ C4: a walk to line enemies up must beat casting from here by this share and margin (it may not be worth a
        /// walk for a sliver, and enemies move).</summary>
        public const float LineUpGainShare = 1.15f;
        public const float LineUpGainMargin = 0.5f;

        /// <summary>★ C4: whether walking to the lined-up spot first beats casting from here (both as <see cref="WalkedNet"/>).</summary>
        public static bool LineUpWorthIt(float hereNet, float lineUpNet) =>
            lineUpNet > 0f && lineUpNet > Math.Max(0f, hereNet) * LineUpGainShare + LineUpGainMargin;

        // NaN counts as no chance: a failed native query must not raise a value.
        private static float Clamp01(float x) => x > 0f ? Math.Min(1f, x) : 0f;
    }
}
