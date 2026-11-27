using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public sealed class PotionValueTests
    {
        [Fact]
        public void PriceDoesNotDiscountExistingReliefOrAlreadyPricedHealing()
        {
            float heal = ValueModel.SpentHealValue(8f, 4f, 0f);
            float relief = 18f;
            Assert.Equal(heal + relief, ValueModel.ConsumableWorth(heal + relief, heal + relief, 0f), 3);
        }

        [Fact]
        public void MixedConsumableKeepsAllyCostsAndPricesTheDiscretionaryBenefit()
        {
            const float offence = 20f, presentRelief = 12f, allyCost = 10f;
            Assert.Equal(13f, ValueModel.ConsumableWorth(offence + presentRelief - allyCost,
                presentRelief - allyCost, 0f), 3);
            Assert.Equal(-5f, ValueModel.ConsumableWorth(-5f, -8f, 1f));
        }

        [Theory]
        [InlineData(0f, 5.5f)]
        [InlineData(0.5f, 7.75f)]
        [InlineData(0.79f, 9.055f)]
        [InlineData(1f, 10f)]
        public void ReadBuffUsesContinuousSlotRestraint(float difficulty, float expected)
        {
            Assert.Equal(expected, ValueModel.ConsumableWorth(10f, 0f, difficulty), 3);
        }
    }
}
