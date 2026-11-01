using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class SummonCrowdShareTests
    {
        [Theory]
        [InlineData(0, 1f)]
        [InlineData(1, 0.5f)]
        [InlineData(3, 0.25f)]
        [InlineData(-2, 1f)]
        public void ShareFallsWithOwnSummonsFighting(int own, float expected) =>
            Assert.Equal(expected, ValueModel.SummonCrowdShare(own), 3);

        [Fact]
        public void EachFurtherSummonIsWorthLess()
        {
            for (int n = 0; n < 6; n++)
                Assert.True(ValueModel.SummonCrowdShare(n + 1) < ValueModel.SummonCrowdShare(n));
        }
    }
}
