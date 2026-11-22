using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class AreaQuoteRulesTests
    {
        [Fact]
        public void RememberedEntryDoesNotAuthorizeASecondHazardOrLongerExposure()
        {
            Assert.True(AreaQuoteRules.CoversActualRisk(10f, .25f, 10f, .25f));
            Assert.False(AreaQuoteRules.CoversActualRisk(10f, .25f, 12f, .25f));
            Assert.False(AreaQuoteRules.CoversActualRisk(10f, .25f, 10f, .5f));
            Assert.False(AreaQuoteRules.CoversActualRisk(10f, .25f, 20f, .25f));
            Assert.True(AreaQuoteRules.CoversActualRisk(10f, .25f, 3f, 0f));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(-1f)]
        public void UnreadOrInvalidRiskCannotAuthorizeACrossing(float risk) =>
            Assert.False(AreaQuoteRules.CoversActualRisk(10f, .25f, risk, 0f));

        [Fact]
        public void NativeRouteWorkIsBoundedBeforeSampling()
        {
            Assert.True(AreaQuoteRules.PathWithinBudget(64, 60f, 10f));
            Assert.False(AreaQuoteRules.PathWithinBudget(65, 60f, 10f));
            Assert.False(AreaQuoteRules.PathWithinBudget(64, 61f, 11f));
            Assert.False(AreaQuoteRules.PathWithinBudget(64, 60f, 5f));
            Assert.False(AreaQuoteRules.PathWithinBudget(0, 0f, 10f));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void UnknownSpeedCannotProveARouteWithinTheHorizon(float speed) =>
            Assert.False(AreaQuoteRules.PathWithinBudget(2, 3f, speed));
    }
}
