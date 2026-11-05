using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ v0.3.28 (codex review): policies called by the shipped quiver and burn-pricing paths.
    public class KineticRulesTests
    {
        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(0, 2, true)]
        [InlineData(1, 2, true)]
        [InlineData(2, 2, false)]
        [InlineData(9, 3, false)]
        public void PreparationOnlyRefillsBelowOneCurrentUse(int ranks, int abilityBurn, bool expected) =>
            Assert.Equal(expected, ValueModel.NeedsKineticQuiver(ranks, abilityBurn));

        [Fact]
        public void PreparationDoesNotPlanForAnInfusionThatIsCurrentlyOff()
        {
            Assert.False(ValueModel.NeedsKineticQuiver(0, 0));
            Assert.True(ValueModel.NeedsKineticQuiver(0, 1));
        }

        [Theory]
        [InlineData(60f, 100, 10f, false)] // Exactly half remains: allowed.
        [InlineData(59f, 100, 10f, true)]
        [InlineData(70f, 100, 10f, false)]
        [InlineData(0.5f, 0, 0f, false)] // The existing maximum-HP floor is one.
        public void PreparationKeepsTheExistingHalfHpFloor(float realHp, int maxHp, float loss, bool unsafeBurn) =>
            Assert.Equal(unsafeBurn, ValueModel.PreparationBurnBelowHpFloor(realHp, maxHp, loss));

        [Fact]
        public void PreparationUsesRealHpAfterExistingNonlethalDamage()
        {
            // 100 max HP, 20 lethal + 25 existing nonlethal: 55 real HP; 10 more burn HP leaves 45.
            Assert.True(ValueModel.PreparationBurnBelowHpFloor(55f, 100, 10f));
        }

        [Theory]
        [InlineData(4, 5f, 4f, 0f)]
        [InlineData(4, 4f, 4f, 12f)]
        [InlineData(4, 0f, 4f, 12f)]
        [InlineData(4, 0f, 1f, 0f)]
        [InlineData(4, 0f, 0.5f, 0f)]
        [InlineData(0, 0f, 4f, 0f)]
        public void OverflowKeepsTheExistingThresholdAndLaterRoundsOnly(int gain, float shortfall,
                                                                          float horizon, float expected) =>
            Assert.Equal(expected, ValueModel.KineticOverflowCredit(gain, shortfall, horizon));

        [Fact]
        public void NoFreeBlastRetainsTheExistingFutureBlastAssumption() =>
            Assert.Equal(12f, ValueModel.KineticOverflowCredit(4, null, 4f));

        [Fact]
        public void ExtractionPreservesExistingUnknownFloatBehavior()
        {
            Assert.False(ValueModel.PreparationBurnBelowHpFloor(float.NaN, 100, 10f));
            Assert.Equal(0f, ValueModel.KineticOverflowCredit(4, float.NaN, 4f));
            Assert.True(float.IsNaN(ValueModel.KineticOverflowCredit(4, null, float.NaN)));
        }
    }
}
