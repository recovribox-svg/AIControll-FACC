using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.28 test 13: ability damage and negative levels in hit points (Tactics/ValueModel.Drain.cs).</summary>
    public class DrainTests
    {
        [Fact]
        public void AbilityDamage_IsTheScoreShareOfTheHitPointsLeft() =>
            // A Shadow's 1d6 Strength on a 16-Strength target with 30 hit points left.
            Assert.Equal(3.5f / 16f * 30f, ValueModel.AbilityDamageEquivalent(3.5f, 16, 30f), 3);

        [Fact]
        public void AbilityDamage_ThatTakesTheWholeScore_IsAKill() =>
            // A score taken below 1 marks the creature for death (RuleDealStatDamage).
            Assert.Equal(30f, ValueModel.AbilityDamageEquivalent(8f, 6, 30f), 3);

        [Theory]
        [InlineData(0f, 16, 30f)]   // no damage
        [InlineData(3.5f, 0, 30f)]  // a score it does not have (disabled stat)
        [InlineData(3.5f, 16, 0f)]  // already down
        public void AbilityDamage_NothingWithoutDamageScoreOrHitPoints(float points, int score, float hp) =>
            Assert.Equal(0f, ValueModel.AbilityDamageEquivalent(points, score, hp), 4);

        [Fact]
        public void NegativeLevels_TakeFiveHitPointsEach_ThenTheirShareOfTheWayToTheLevel()
        {
            // Enervation's 1d4 (2.5) on a level-6 creature with 40 hit points left: 12.5, then 2.5/6 of the other 27.5.
            Assert.Equal(12.5f + 2.5f / 6f * 27.5f, ValueModel.EnergyDrainEquivalent(2.5f, 6, 40f), 3);
        }

        [Fact]
        public void NegativeLevels_ReachingTheCharacterLevel_AreAKill() =>
            Assert.Equal(25f, ValueModel.EnergyDrainEquivalent(2.5f, 2, 25f), 3);

        [Fact]
        public void NegativeLevels_NeverWorthMoreThanTheHitPointsLeft() =>
            Assert.Equal(8f, ValueModel.EnergyDrainEquivalent(2f, 10, 8f), 3);

        [Theory]
        [InlineData(0f, 6, 40f)]
        [InlineData(2.5f, 6, 0f)]
        public void NegativeLevels_NothingWithoutLevelsOrHitPoints(float levels, int level, float hp) =>
            Assert.Equal(0f, ValueModel.EnergyDrainEquivalent(levels, level, hp), 4);
    }
}
