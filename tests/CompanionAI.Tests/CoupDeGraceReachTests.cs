using System;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class CoupDeGraceReachTests
    {
        [Theory]
        [InlineData(30f, 2f, 2f)] // Bow first, melee limb delivers.
        [InlineData(30f, 4.6f, 4.6f)] // Reach weapon retains its own reach and body sizes.
        [InlineData(1.5f, 4.6f, 1.5f)] // Do not extend a stricter ability radius.
        [InlineData(2f, 2f, 2f)]
        [InlineData(0.1f, 0.1f, 0.1f)]
        public void DeliveryCannotBorrowAnotherWeaponsReach(float ability, float weapon, float expected)
        {
            Assert.True(CoupDeGraceRules.TryRadius(ability, weapon, out float radius));
            Assert.Equal(expected, radius);
        }

        [Theory]
        [InlineData(0f, 2f)]
        [InlineData(2f, 0f)]
        [InlineData(-1f, 2f)]
        [InlineData(2f, -1f)]
        [InlineData(float.NaN, 2f)]
        [InlineData(2f, float.NaN)]
        [InlineData(float.PositiveInfinity, 2f)]
        [InlineData(2f, float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity, 2f)]
        public void UnreadReachCannotAuthorizeACommand(float ability, float weapon)
        {
            Assert.False(CoupDeGraceRules.TryRadius(ability, weapon, out float radius));
            Assert.Equal(0f, radius);
        }

        [Fact]
        public void RepeatedPreparationRetainsStricterCommandRadius()
        {
            Assert.True(CoupDeGraceRules.TryRadius(30f, 3f, out float radius));
            Assert.True(CoupDeGraceRules.TryRadius(1f, radius, out radius));
            Assert.True(CoupDeGraceRules.TryRadius(radius, 3f, out radius));
            Assert.Equal(1f, radius);
        }

        [Theory]
        [InlineData("TabletopTweaks.Core.NewComponents.OwlcatReplacements.CoupDeGraceTTT", "TabletopTweaks-Core", true)]
        [InlineData("SomeMod.CoupDeGraceTTT", "TabletopTweaks-Core", false)]
        [InlineData("TabletopTweaks.Core.NewComponents.OwlcatReplacements.CoupDeGraceTTT", "SomeMod", false)]
        [InlineData(null, null, false)]
        public void ReplacementMustBeTheVerifiedProvider(string type, string assembly, bool expected)
            => Assert.Equal(expected, CoupDeGraceRules.IsTabletopTweaks(type, assembly));
    }
}
