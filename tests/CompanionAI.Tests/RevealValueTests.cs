using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class RevealValueTests
    {
        [Fact]
        public void RemovingConcealmentOnlyRecoversTheMissingDamage()
        {
            Assert.Equal(0f, ValueModel.ConcealmentRemovalGain(12, 1, 1));
            Assert.Equal(3f, ValueModel.ConcealmentRemovalGain(12, .8f, 1), 4);
            Assert.Equal(12f, ValueModel.ConcealmentRemovalGain(12, .5f, 1), 4);
            Assert.Equal(0f, ValueModel.ConcealmentRemovalGain(12, .8f, .8f));
            Assert.Equal(0f, ValueModel.ConcealmentRemovalGain(0, .5f, 1));
            Assert.Equal(0f, ValueModel.ConcealmentRemovalGain(12, 0, 1));
            // Expected damage already includes mirror images: scaling the concealment pass retains that reduction.
            Assert.Equal(2f, ValueModel.ConcealmentRemovalGain(2, .5f, 1), 4);
        }
    }
}
