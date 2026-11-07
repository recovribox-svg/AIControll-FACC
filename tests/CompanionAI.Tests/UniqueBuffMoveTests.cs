using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.30 test 1: moving a buff its caster keeps on one recipient at a time (Tactics/ValueModel.UniqueBuff.cs).</summary>
    public class UniqueBuffMoveTests
    {
        [Fact]
        public void AHolderThatGainsNothing_GivesItUpFreely() => Assert.Equal(6f, ValueModel.BuffSwitchGain(6f, 0f), 3);

        [Fact]
        public void AClearlyBetterRecipient_GainsTheDifference() => Assert.Equal(4f, ValueModel.BuffSwitchGain(10f, 6f), 3);

        /// <summary>The v0.3.29 log: Profane Invocation (Dexterity) hopped between Aeiluvetar (6.2-6.6) and Seelah (7.5).</summary>
        [Fact]
        public void ASlightlyBetterRecipient_LeavesItWhereItIs()
        {
            Assert.Equal(0f, ValueModel.BuffSwitchGain(7.5f, 6.6f));
            Assert.Equal(0f, ValueModel.BuffSwitchGain(6.6f, 7.5f));
        }

        [Fact]
        public void EqualRecipients_NeverSwap() => Assert.Equal(0f, ValueModel.BuffSwitchGain(8.8f, 8.8f));

        [Fact]
        public void JustPastTheMargin_Moves() => Assert.True(ValueModel.BuffSwitchGain(12.6f, 10f) > 0f);

        /// <summary>The v0.3.29 log: Strength (0.8) put in place of a mental-stat invocation (0.7) on Orvath, whose blasts use Intelligence.</summary>
        [Fact]
        public void ASwitchToSomethingWorthLess_IsNothing() => Assert.Equal(0f, ValueModel.BuffSwitchGain(0.4f, 9f));

        [Fact]
        public void NothingToGainOrBrokenInput_IsNothing()
        {
            Assert.Equal(0f, ValueModel.BuffSwitchGain(0f, 0f));
            Assert.Equal(0f, ValueModel.BuffSwitchGain(-1f, 0f));
            Assert.Equal(0f, ValueModel.BuffSwitchGain(float.NaN, 1f));
            Assert.Equal(0f, ValueModel.BuffSwitchGain(5f, float.NaN));
        }
    }
}
