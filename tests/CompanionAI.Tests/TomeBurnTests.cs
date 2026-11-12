using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class TomeBurnTests
    {
        [Fact]
        public void TheInspectedCompositeReducesTwoCharacterLevelsOnlyWhenItsFeatureTermIsEnabled()
        {
            Assert.Equal(33,ValueModel.TomeBurnValue(3,11,0));
            Assert.Equal(27,ValueModel.TomeBurnValue(3,11,-2));
            Assert.Equal(9,ValueModel.TomeBurnValue(4,11,-2)-ValueModel.TomeBurnValue(3,11,-2));
        }

        [Fact]
        public void EachCompositeTermPreservesTheProvidersFloatConversionAndFloor()
        {
            Assert.Equal(16777216,ValueModel.TomeBurnValue(16777217,1,0));
        }
    }
}
