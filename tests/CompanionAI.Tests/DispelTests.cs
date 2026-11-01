using System.Collections.Generic;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.25 (Unraveling): the game's dispel check and its in-order removal (Tactics/ValueModel.Dispel.cs).</summary>
    public class DispelTests
    {
        private static KeyValuePair<float, float> E(float p, float w) => new KeyValuePair<float, float>(p, w);

        [Theory]
        [InlineData(1, 1f)]
        [InlineData(11, 0.5f)]
        [InlineData(20, 0.05f)]
        [InlineData(21, 0f)]
        [InlineData(-5, 1f)]
        public void ChanceIsTheD20Needed(int need, float expected) => Assert.Equal(expected, ValueModel.DispelChance(need), 3);

        [Fact]
        public void StopsAfterTheFirstSuccess()
        {
            // 0.5 x 10 + 0.5 x 0.5 x 4 = 6
            Assert.Equal(6f, ValueModel.ExpectedDispelWorth(new[] { E(0.5f, 10f), E(0.5f, 4f) }, 1), 3);
        }

        [Fact]
        public void NoLimitTriesEveryBuff()
        {
            Assert.Equal(7f, ValueModel.ExpectedDispelWorth(new[] { E(0.5f, 10f), E(0.5f, 4f) }, 0), 3);
        }

        [Fact]
        public void OwnDebuffFirstInLineCostsUs()
        {
            // our debuff (worth -8 to remove) tried first at 75 %, the enemy's buff (+10) only if that fails
            Assert.Equal(-6f + 0.25f * 0.75f * 10f, ValueModel.ExpectedDispelWorth(new[] { E(0.75f, -8f), E(0.75f, 10f) }, 1), 3);
        }

        [Fact]
        public void TwoRemovalsCountUpToTwo()
        {
            var list = new[] { E(1f, 1f), E(1f, 2f), E(1f, 4f) };
            Assert.Equal(3f, ValueModel.ExpectedDispelWorth(list, 2), 3);
        }
    }
}
