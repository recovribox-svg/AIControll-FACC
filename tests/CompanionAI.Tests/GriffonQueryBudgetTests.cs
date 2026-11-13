using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class GriffonQueryBudgetTests
    {
        [Fact]
        public void FailedLandingsCannotCauseAnUnboundedNativeNodeSearch()
        {
            var budget = new GriffonQueryBudget();
            for (int i = 0; i < GriffonQueryBudget.MaxLandingAttempts; i++) Assert.True(budget.TryLanding());
            for (int i = 0; i < 20; i++) Assert.False(budget.TryLanding());
            Assert.Equal(8, budget.LandingAttempts);
            Assert.Equal(0, budget.WeaponQuotes);
            Assert.True(budget.Exhausted);
        }

        [Fact]
        public void FourWeaponQuotesStopBothKindsOfFurtherNativeWork()
        {
            var budget = new GriffonQueryBudget();
            for (int i = 0; i < GriffonQueryBudget.MaxWeaponQuotes; i++)
            {
                Assert.True(budget.TryLanding()); Assert.True(budget.TryWeaponQuote());
            }
            Assert.False(budget.TryLanding()); Assert.False(budget.TryWeaponQuote());
            Assert.Equal(4, budget.LandingAttempts); Assert.Equal(4, budget.WeaponQuotes);
        }

        [Fact]
        public void SomeBlockedLandingsLeaveTheRemainingQuoteBudgetAvailable()
        {
            var budget = new GriffonQueryBudget();
            for (int i = 0; i < 4; i++) Assert.True(budget.TryLanding());
            for (int i = 0; i < 4; i++)
            {
                Assert.True(budget.TryLanding()); Assert.True(budget.TryWeaponQuote());
            }
            Assert.Equal(8, budget.LandingAttempts); Assert.Equal(4, budget.WeaponQuotes);
            Assert.False(budget.TryLanding()); Assert.False(budget.TryWeaponQuote());
        }

        [Fact]
        public void AWeaponQuoteNeedsAnAttemptedNativeLanding()
        {
            var budget = new GriffonQueryBudget();
            Assert.False(budget.TryWeaponQuote()); Assert.Equal(0, budget.WeaponQuotes);
            Assert.True(budget.TryLanding()); Assert.True(budget.TryWeaponQuote());
            Assert.False(budget.TryWeaponQuote());
        }

        [Fact]
        public void ANewEvaluationGetsAFreshBudgetWithoutRetainingAnyActor()
        {
            var used = new GriffonQueryBudget();
            for (int i = 0; i < 8; i++) used.TryLanding();
            var next = new GriffonQueryBudget();
            Assert.True(next.TryLanding()); Assert.True(next.TryWeaponQuote());
            Assert.Equal(1, next.LandingAttempts); Assert.Equal(1, next.WeaponQuotes);
        }
    }
}
