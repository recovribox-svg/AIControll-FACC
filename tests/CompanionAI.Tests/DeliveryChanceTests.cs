using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>Recommendation A: the arcane failure chance from PR #15, applied once (Tactics/ValueModel.Delivery.cs).</summary>
    public class DeliveryChanceTests
    {
        [Theory]
        [InlineData(0f, 1f)]
        [InlineData(20f, 0.8f)]
        [InlineData(35f, 0.65f)]
        [InlineData(100f, 0f)]
        [InlineData(150f, 0f)]
        [InlineData(-10f, 1f)]
        public void NativeFailurePercent_IsACastingChance(float percent, float expected) =>
            Assert.Equal(expected, ValueModel.ArcaneCastingChance(percent), 5);

        [Fact]
        public void NoFailure_LeavesTheValueAlone()
        {
            Assert.Equal(10f, ValueModel.DeliveredBenefit(10f, false, 1f), 5);
            Assert.Equal(10f * ValueModel.WalkThenCastFactor, ValueModel.DeliveredBenefit(10f, true, 1f), 5);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Benefit_IsDiscountedExactlyOnce(bool needsMove)
        {
            // Linear in the chance: applied twice, 0.8 would give 0.64 of the undiscounted value.
            float full = ValueModel.DeliveredBenefit(12f, needsMove, 1f);
            foreach (float d in new[] { 0f, 0.25f, 0.5f, 0.8f, 1f })
                Assert.Equal(full * d, ValueModel.DeliveredBenefit(12f, needsMove, d), 4);
        }

        [Fact]
        public void TwentyPercentFailure_GivesFactorPointEight() =>
            Assert.Equal(8f, ValueModel.DeliveredBenefit(10f, false, ValueModel.ArcaneCastingChance(20f)), 5);

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(-0.5f)]
        public void BrokenChance_NeverRaisesTheValue(float d) =>
            Assert.Equal(0f, ValueModel.DeliveredBenefit(10f, false, d), 5);

        [Fact]
        public void ChanceAboveOne_IsCapped() => Assert.Equal(10f, ValueModel.DeliveredBenefit(10f, false, 1.5f), 5);

        [Theory]
        [InlineData(1f, false, 0f, 1f, false, 0f)]
        [InlineData(0.85f, true, 0.2f, 0.9f, false, 0.1f)]
        [InlineData(0.7f, true, 0f, 0.6f, true, 0.3f)]
        public void InfusionDelivery_CarriesTheChanceExactlyOnce(float consumable, bool move, float provoke, float conc,
            bool archer, float walk)
        {
            float full = ValueModel.InfusionDeliveryFactor(consumable, move, 1f, provoke, conc, archer, walk);
            foreach (float d in new[] { 0f, 0.5f, 0.8f })
                Assert.Equal(full * d, ValueModel.InfusionDeliveryFactor(consumable, move, d, provoke, conc, archer, walk), 4);
        }

        [Fact]
        public void InfusionDelivery_IsTheProductOfItsDiscounts()
        {
            float expected = 0.85f * ValueModel.WalkThenCastFactor * 0.8f * (1f - 0.2f) * 0.9f * ValueModel.ArcherTouchFactor * (1f - 0.1f);
            Assert.Equal(expected, ValueModel.InfusionDeliveryFactor(0.85f, true, 0.8f, 0.2f, 0.9f, true, 0.1f), 5);
        }

        [Fact]
        public void InfusionDelivery_MatchesTheSpellsOwnMultiplicativeDiscounts()
        {
            // The spell value's multiplicative path: walk, delivery, concentration, archer touch, walk risk. Provoke costs
            // are subtracted from the spell instead, so with no provoke both discount the same way.
            float spell = ValueModel.DeliveredBenefit(1f, true, 0.8f) * 0.9f * ValueModel.ArcherTouchFactor * (1f - 0.1f);
            Assert.Equal(spell, ValueModel.InfusionDeliveryFactor(1f, true, 0.8f, 0f, 0.9f, true, 0.1f), 5);
        }

        [Fact]
        public void ProgressiveViability_CarriesTheChanceExactlyOnce()
        {
            foreach (float effect in new[] { 0.3f, 0.6f, 1f })
            foreach (float d in new[] { 0f, 0.5f, 0.8f, 1f })
                Assert.Equal(effect * d, ValueModel.DeliveredSuccessChance(effect, d), 5);
        }

        [Fact]
        public void HeavyArmorFailure_CanMakeAViableSpellUnviable()
        {
            // SpellTierPolicy.MinSuccessChance applies to the delivered chance, not the save chance alone.
            float effect = SpellTierPolicy.MinSuccessChance + 0.1f;
            Assert.True(ValueModel.DeliveredSuccessChance(effect, 1f) >= SpellTierPolicy.MinSuccessChance);
            Assert.True(ValueModel.DeliveredSuccessChance(effect, ValueModel.ArcaneCastingChance(50f)) < SpellTierPolicy.MinSuccessChance);
        }
    }
}
