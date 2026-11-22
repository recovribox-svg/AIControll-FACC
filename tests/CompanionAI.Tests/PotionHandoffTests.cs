using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public sealed class PotionHandoffTests
    {
        [Theory]
        [InlineData(true, true, false, true)]
        [InlineData(false, true, false, false)]
        [InlineData(true, false, false, false)]
        [InlineData(true, true, true, false)]
        public void OnlyFriendTargetableQuickSlotPotionsQualify(bool quickSlot, bool friends, bool personal, bool offered)
        {
            Assert.Equal(offered, PotionHandoffRules.CanOffer(quickSlot, friends, personal));
        }

        [Theory]
        [InlineData(false, false, false, true)]
        [InlineData(true, false, false, false)]
        [InlineData(false, true, false, false)]
        [InlineData(false, false, true, false)]
        public void HandoffRequiresAnotherNonSummonedAlly(bool self, bool enemy, bool summoned, bool eligible)
        {
            Assert.Equal(eligible, PotionHandoffRules.CanReceive(self, enemy, summoned));
        }
    }
}
