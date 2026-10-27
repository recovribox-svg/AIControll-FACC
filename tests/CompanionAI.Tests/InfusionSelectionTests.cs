using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class InfusionSelectionTests
    {
        [Fact]
        public void PeriodicDamageCannotRepeatedlyClaimHealthAlreadyLostToTheBlast()
        {
            Assert.Equal(2f, ValueModel.InfusionPeriodicDamage(0.5f, 12f, 6f, 10f), 4);
            Assert.Equal(0f, ValueModel.InfusionPeriodicDamage(1f, 12f, 10f, 10f));
            Assert.Equal(0f, ValueModel.InfusionPeriodicDamage(0f, 12f, 0f, 10f));
        }
        [Fact]
        public void PositiveEffectDoesNotJustifyWorseWholePlan()
        {
            // Blast 10, extra 15 discounted by delivery risk to 7.5, competing spell 20: keep the spell.
            Assert.Equal(-2.5f, ValueModel.InfusionPlanGain(10f, 15f, 0f, 0.5f, 0f, 0f, 20f), 4);
        }

        [Fact]
        public void BurnPriceAndReplacedEffectArePaidOnce()
        {
            // Actual 14-point blast already paid 2 for burn; replace its 6-point effect with 10, then pay 5 burn.
            Assert.Equal(1f, ValueModel.InfusionPlanGain(14f, 10f, 6f, 1f, 2f, 5f, 14f), 4);
            Assert.Equal(-3f, ValueModel.InfusionPlanGain(14f, 10f, 6f, 0f, 2f, 5f, 14f), 4);
        }

        [Fact]
        public void TurningOffCanWinWhenCostOutweighsLostEffect()
        {
            Assert.Equal(2f, ValueModel.InfusionPlanGain(14f, 0f, 1f, 1f, 3f, 0f, 14f), 4);
            Assert.Equal(-3f, ValueModel.InfusionPlanGain(14f, 0f, 6f, 1f, 3f, 0f, 14f), 4);
        }

        [Fact]
        public void BetterWeaponStillBeatsAUsefulInfusedBlast()
        {
            Assert.Equal(-1f, ValueModel.InfusionPlanGain(8f, 4f, 0f, 1f, 0f, 0f, 13f), 4);
        }

        [Fact]
        public void WorkCapPreventsAnUnboundedOptionProduct()
        {
            var budget = new InfusionSearchBudget(() => 0);
            int started = 0;
            while (budget.TryBegin()) started++;
            Assert.Equal(InfusionSearchBudget.MaxComparisons, started);
            Assert.True(budget.Exhausted);
        }

        [Fact]
        public void SlowNativeCallCannotStartAnotherComparisonAfterDeadline()
        {
            long elapsed = 0;
            var budget = new InfusionSearchBudget(() => elapsed);
            Assert.True(budget.TryBegin());
            elapsed = 250; // One completed native call exceeded the cooperative budget.
            Assert.False(budget.TryBegin());
            Assert.Equal(1, budget.Comparisons);
        }

        [Theory]
        [InlineData(true, false, false, false, true)]  // available ordinary action
        [InlineData(false, true, false, false, true)]  // RT future offence/positioning
        [InlineData(false, false, true, false, true)]  // held touch charge's opportunity cost
        [InlineData(false, false, false, true, true)]  // spell-forbidding preparation's opportunity cost
        [InlineData(false, false, false, false, false)] // unavailable unrelated healing/buffs
        public void CooldownPruningRetainsDeferredDependencies(bool available, bool offence, bool heldTouch,
            bool spellOpportunity, bool expected) =>
            Assert.Equal(expected, AbilityScoringPolicy.NeedsScore(available, offence, heldTouch, spellOpportunity));
    }
}
