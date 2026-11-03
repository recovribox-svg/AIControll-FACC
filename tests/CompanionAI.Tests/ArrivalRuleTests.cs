using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.27: when a real-time walk counts as arrived (Tactics/ArrivalRule.cs).</summary>
    public class ArrivalRuleTests
    {
        [Fact]
        public void TheGamesOwnTest_Decides() => Assert.True(ArrivalRule.InRange(true, 5f, false));

        [Fact]
        public void WobblingAroundTheRadius_StaysInRange()
        {
            // Seelah against the Vermlek: -0.04 m, +0.04 m, -0.02 m - in range throughout.
            Assert.True(ArrivalRule.InRange(false, -0.04f, true));
            Assert.True(ArrivalRule.InRange(false, 0.04f, true));
            Assert.True(ArrivalRule.InRange(false, -0.02f, true));
        }

        [Fact]
        public void BeyondTheTolerance_IsNotInRange() => Assert.False(ArrivalRule.InRange(false, 0.31f, true));

        [Fact]
        public void NoSightWhereNeeded_IsNotInRange() => Assert.False(ArrivalRule.InRange(false, -1f, false));

        [Fact]
        public void BrokenDistance_IsNotInRange() => Assert.False(ArrivalRule.InRange(false, float.NaN, true));
    }
}
