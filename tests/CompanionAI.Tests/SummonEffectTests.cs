using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public sealed class SummonEffectTests
    {
        [Theory]
        [InlineData(0.25f, 0.75f)]
        [InlineData(0.75f, 0.25f)]
        [InlineData(0f, 0f)]
        public void KeepingTheWorseRollChangesAnExistingAttackForecast(float pass, float lost)
        {
            Assert.Equal(lost, ValueModel.WorstOfTwoLostShare(pass), 4);
        }

        [Theory]
        [InlineData(true, 12f)]
        [InlineData(false, 0f)]
        public void RedirectCreditsOnlyAnAttackTakenAwayFromTheParty(bool beneficial, float worth)
        {
            Assert.Equal(worth, ValueModel.RedirectedAttackWorth(0.5f, 24f, beneficial), 4);
        }

        [Fact]
        public void ACombatHorizonCannotIncludeADailyDiseaseTick()
        {
            int future = ValueModel.PeriodicTicks(86400, 86400, double.PositiveInfinity, 18);
            Assert.Equal(1f, ValueModel.PoisonDoses(0.5f, future, 2));
        }
    }
}
