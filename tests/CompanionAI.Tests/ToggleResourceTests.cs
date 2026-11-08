using CompanionAI_Pathfinder.Tactics;
using Xunit;
using static CompanionAI_Pathfinder.Tactics.ValueModel;

namespace CompanionAI.Tests
{
    public class ToggleResourceTests
    {
        [Theory]
        [InlineData(ToggleSpendCadence.Activation)]
        [InlineData(ToggleSpendCadence.ExternalActivation)]
        [InlineData(ToggleSpendCadence.Minute)]
        [InlineData(ToggleSpendCadence.TenMinutes)]
        [InlineData(ToggleSpendCadence.Hour)]
        public void OnePaidUseIsNotOneRound(ToggleSpendCadence cadence)
        {
            var w = ToggleResourceWindow(cadence, 1, 1, false, 6);
            Assert.Equal(6, w.Rounds); Assert.Equal(1, w.StartCost); Assert.Equal(0, w.ContinueCost);
            Assert.False(w.StopSavesResource);
        }

        [Theory]
        [InlineData(ToggleSpendCadence.Activation)]
        [InlineData(ToggleSpendCadence.ExternalActivation)]
        [InlineData(ToggleSpendCadence.Minute)]
        [InlineData(ToggleSpendCadence.Hour)]
        public void ContinueDoesNotRepayStart(ToggleSpendCadence cadence)
        {
            var w = ToggleResourceWindow(cadence, 0, 1, true, 4, 6, 8);
            Assert.Equal(4, w.Rounds); Assert.Equal(0, w.StartCost); Assert.False(w.Spends);
        }

        [Fact]
        public void RoundDrainUsesModifiedCostAndCannotValueAnUnaffordableFirstTick()
        {
            Assert.Equal(2, ToggleResourceWindow(ToggleSpendCadence.Round, 5, 2, false, 6).Rounds);
            Assert.Equal(0, ToggleResourceWindow(ToggleSpendCadence.Round, 1, 2, false, 6).Rounds);
            var active = ToggleResourceWindow(ToggleSpendCadence.Round, 0, 2, true, 6, 3);
            Assert.Equal(.5f, active.Rounds); Assert.False(active.StopSavesResource);
        }

        [Fact]
        public void TimedRenewalUsesExistingCounterAndPreservesPaidTime()
        {
            var paid = ToggleResourceWindow(ToggleSpendCadence.Minute, 3, 1, true, 6, 6, 8);
            Assert.False(paid.Spends); Assert.False(paid.StopSavesResource);
            var renew = ToggleResourceWindow(ToggleSpendCadence.Minute, 3, 2, true, 6, 3, 1);
            Assert.Equal(2, renew.ContinueCost); Assert.Equal(.5f, renew.NextPaymentRounds); Assert.True(renew.StopSavesResource);
            var empty = ToggleResourceWindow(ToggleSpendCadence.Minute, 0, 2, true, 6, 3, 1);
            Assert.Equal(.5f, empty.Rounds); Assert.False(empty.StopSavesResource);
        }

        [Fact]
        public void TttPostSpendDeactivationIsDifferentFromNativeStart()
        {
            Assert.Equal(6, ToggleResourceWindow(ToggleSpendCadence.Activation, 2, 2, false, 6).Rounds);
            Assert.Equal(0, ToggleResourceWindow(ToggleSpendCadence.Activation, 2, 2, false, 6, endsEmptyAfterStart:true).Rounds);
            Assert.Equal(6, ToggleResourceWindow(ToggleSpendCadence.Activation, 3, 2, false, 6, endsEmptyAfterStart:true).Rounds);
            Assert.Equal(6, ToggleResourceWindow(ToggleSpendCadence.Minute, 1, 1, false, 6).Rounds);
        }

        [Fact]
        public void ACostReductionToZeroHasNoDrainOrFalseStop()
        {
            var w = ToggleResourceWindow(ToggleSpendCadence.Round, 1, 0, true, 6, 0);
            Assert.Equal(6, w.Rounds); Assert.False(w.Spends); Assert.False(w.StopSavesResource);
        }

        [Fact]
        public void AlreadyPaidJudgmentUsesNoNewResourcePrice()
        {
            var paidJudgment = ToggleResourceWindow(ToggleSpendCadence.ExternalActivation, 0, 0, false, 6);
            Assert.Equal(6, paidJudgment.Rounds); Assert.False(paidJudgment.Spends);
        }

        [Fact]
        public void LastJudgmentUseRetainsPaidTimeWhenItsWatcherWaivesThePoolRequirement()
        {
            var first = ToggleResourceWindow(ToggleSpendCadence.ExternalActivation,1,1,false,6);
            Assert.Equal(1,first.StartCost); Assert.Equal(6,first.Rounds);
            Assert.False(ToggleEventLifetimeUnknown(true,1,1,first,false));
            Assert.True(ToggleEventLifetimeUnknown(true,1,1,first,true));
            var oneShot = ToggleResourceWindow(ToggleSpendCadence.Activation,1,1,false,6,endsEmptyAfterStart:true);
            Assert.Equal(0,oneShot.Rounds);
            Assert.False(ToggleEventLifetimeUnknown(true,1,1,oneShot,true));
        }

        [Fact]
        public void RecurringPricesAreNotDroppedInEasyFights()
        {
            var w = ToggleResourceWindow(ToggleSpendCadence.Round, 20, 1, false, 4);
            Assert.True(w.Spends); Assert.Equal(4, w.ContinueCost);
            Assert.True(ValueModel.LimitedUseDiscount(.2f) > 0);
        }
    }
}
