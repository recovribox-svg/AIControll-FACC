using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>
    /// ★ v0.3.70 features6 review: the weapon-set switch compares with the current weapon's price, not with 0 when this
    /// moment's actions do not allow an attack; half drained counts as badly hurt (author-approved).
    /// </summary>
    public sealed class V0370Features6ReviewTests
    {
        [Fact]
        public void CurrentWeaponKeepsItsEarlierPriceThisTurn()
        {
            // Priced now: that price.
            Assert.True(WeaponSetRules.CurrentPrice(true, 10.8f, 4f, out float now));
            Assert.Equal(10.8f, now);
            // Turn-based, Camellia after Inspired Rage: no weapon option now, the greataxe's 10.8 from earlier this turn.
            Assert.True(WeaponSetRules.CurrentPrice(false, 0f, 10.8f, out float earlier));
            Assert.Equal(10.8f, earlier);
            // Neither: no switch offered (it was compared with 0).
            Assert.False(WeaponSetRules.CurrentPrice(false, 0f, null, out _));
        }

        [Fact]
        public void AnEarlierPriceStandsForTheTurnOrARealTimeRound()
        {
            Assert.True(WeaponSetRules.Fresh(3, 3, realTime: false));
            Assert.False(WeaponSetRules.Fresh(3, 4, realTime: false));
            Assert.True(WeaponSetRules.Fresh(3, 4, realTime: true));
            Assert.False(WeaponSetRules.Fresh(3, 5, realTime: true));
            Assert.False(WeaponSetRules.Fresh(4, 3, realTime: true));
        }

        [Fact]
        public void HalfDrainedIsBadlyHurt()
        {
            // arcan, Arcanist 4: 2 of 4 drained (the Spectre's first touch), then dead at 4.
            Assert.False(ValueModel.HalfDrained(4, 1));
            Assert.True(ValueModel.HalfDrained(4, 2));
            // Aeiluvetar, level 5: 3 of 5.
            Assert.False(ValueModel.HalfDrained(5, 2));
            Assert.True(ValueModel.HalfDrained(5, 3));
            Assert.True(ValueModel.HalfDrained(20, 10));
            Assert.False(ValueModel.HalfDrained(20, 9));
            Assert.False(ValueModel.HalfDrained(4, 0));
            Assert.True(ValueModel.HalfDrained(1, 1));
        }
    }
}
