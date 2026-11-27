using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public sealed class TeleportApproachTests
    {
        [Fact]
        public void SwiftLeapQuotesOnlyTheGainOverTheApproachAlreadyAvailable()
        {
            Assert.Equal(7f, ValueModel.TeleportApproachWorth(20f, 13f, true, 0.35f));
            Assert.Equal(0f, ValueModel.TeleportApproachWorth(20f, 20f, true, 0.35f));
        }

        [Fact]
        public void StandardTeleportPricesTheNextAttackAndTheOneLeftBehind()
        {
            Assert.Equal(4.2f, ValueModel.TeleportApproachWorth(20f, 8f, false, 0.35f), 4);
            Assert.Equal(0f, ValueModel.TeleportApproachWorth(8f, 20f, false, 0.35f));
        }
    }
}
