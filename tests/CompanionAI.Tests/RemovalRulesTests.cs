using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class RemovalRulesTests
    {
        [Fact]
        public void PointDispelDoesNotStripAnAnchorsUnitBuffs()
        {
            Assert.False(RemovalRules.CanDispelUnit(true, false, false));
            Assert.True(RemovalRules.CanDispelUnit(true, true, false)); // Greater Area selects units around the point.
            Assert.True(RemovalRules.CanDispelUnit(false, false, false)); // Targeted Dispel Magic.
            Assert.True(RemovalRules.CanDispelUnit(true, false, true)); // The fusion has its own delivery.
        }

        [Fact]
        public void HelpfulArmorLossCountsItsActiveRoundsWithinTheFight()
        {
            Assert.Equal(9f, RemovalRules.HelpfulLoss(0.15f, 20f, 600f, 0f, 3f), 4);
            Assert.Equal(3f, RemovalRules.HelpfulLoss(0.15f, 20f, 1f, 0f, 3f), 4);
        }

        [Fact]
        public void RemovingAnAlreadySuppressedBuffCostsOnlyWhatWouldReturn()
        {
            Assert.Equal(0f, RemovalRules.HelpfulLoss(0.15f, 20f, 2f, 3f, 3f));
            Assert.Equal(3f, RemovalRules.HelpfulLoss(0.15f, 20f, 8f, 2f, 3f), 4);
        }

        [Theory]
        [InlineData(0f, 20f, 3f)]
        [InlineData(0.2f, 0f, 3f)]
        [InlineData(0.2f, 20f, 0f)]
        public void NoHelpfulOutputOrNoRemainingLifetimeHasNoLoss(float share, float output, float rounds) =>
            Assert.Equal(0f, RemovalRules.HelpfulLoss(share, output, rounds, 0f, 3f));
    }
}
