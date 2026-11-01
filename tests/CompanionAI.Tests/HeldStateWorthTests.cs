using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.25 (Psionics): getting a held state such as psionic focus back (Tactics/ValueModel.HeldStates.cs).</summary>
    public class HeldStateWorthTests
    {
        [Fact]
        public void NothingWhenNoRoundIsLeft()
        {
            Assert.Equal(0f, ValueModel.HeldStateWorth(0f, 2f, 2, 22f, 3f, 1f));
            Assert.Equal(0f, ValueModel.HeldStateWorth(-1f, 2f, 2, 22f, 3f, 1f));
            Assert.Equal(0f, ValueModel.HeldStateWorth(float.NaN, 2f, 2, 22f, 3f, 1f));
        }

        [Fact]
        public void NothingWhenNothingDependsOnIt()
        {
            Assert.Equal(0f, ValueModel.HeldStateWorth(4f, 0f, 0, 22f, 0f, 0f));
        }

        [Fact]
        public void ReadablePartsAndFeaturesForEveryRoundLeft()
        {
            // Psionic Dodge's buff (readable 1.5 a round) and Psionic Weapon (one feature: 0.15 x 22 = 3.3), 3 rounds.
            Assert.Equal((1.5f + 3.3f) * 3f, ValueModel.HeldStateWorth(3f, 1.5f, 1, 22f, 0f, 0f), 3);
        }

        [Fact]
        public void FeaturesCountUpToTheCap()
        {
            float capped = ValueModel.HeldStateWorth(2f, 0f, ValueModel.DependentCap, 22f, 0f, 0f);
            Assert.Equal(capped, ValueModel.HeldStateWorth(2f, 0f, ValueModel.DependentCap + 4, 22f, 0f, 0f), 3);
        }

        [Fact]
        public void TheToggleCountsAfterItsOwnCommand()
        {
            // A trance worth 4 a round, started with a standard action (a round) after the focus is back: 3 of 4 rounds.
            Assert.Equal(4f * 3f, ValueModel.HeldStateWorth(4f, 0f, 0, 22f, 4f, 1f), 3);
            Assert.Equal(0f, ValueModel.HeldStateWorth(1f, 0f, 0, 22f, 4f, 1f), 3);
        }

        [Fact]
        public void WorthMoreThanAnAttackEarlyAndLessLate()
        {
            // Level 6, two features (Psionic Weapon, Psionic Shot) and Psionic Dodge's +1 AC: against a ~12-point attack.
            float early = ValueModel.HeldStateWorth(4f, 1.5f, 2, ValueModel.PerRound(6), 0f, 0f);
            float late = ValueModel.HeldStateWorth(1f, 1.5f, 2, ValueModel.PerRound(6), 0f, 0f);
            Assert.True(early > 12f);
            Assert.True(late < 12f);
        }
    }
}
