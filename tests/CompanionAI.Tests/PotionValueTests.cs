using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public sealed class PotionValueTests
    {
        [Theory]
        [InlineData(0f, 5.5f)]
        [InlineData(0.5f, 7.75f)]
        [InlineData(0.79f, 9.055f)]
        [InlineData(1f, 10f)]
        public void ReadBuffUsesContinuousSlotRestraint(float difficulty, float expected)
        {
            Assert.Equal(expected, ValueModel.PotionBuffValue(10f, difficulty), 3);
        }
    }
}
