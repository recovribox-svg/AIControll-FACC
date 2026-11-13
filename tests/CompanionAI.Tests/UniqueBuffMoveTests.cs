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
    
        // ★ v0.3.30 competence3: a unique buff settles where it was put.
        [Fact]
        public void PutThisFightLessThanThreeRoundsAgo_SmallGainStays()
        {
            Assert.False(ValueModel.UniqueMoveAllowed(0, 3.3f));
            Assert.False(ValueModel.UniqueMoveAllowed(2, 2.2f));
        }

        [Fact]
        public void SettledOrNotPutByThisCasterThisFight_Moves()
        {
            Assert.True(ValueModel.UniqueMoveAllowed(3, 1.2f));
            Assert.True(ValueModel.UniqueMoveAllowed(null, 1.2f));
        }

        /// <summary>The competence2 log: Constitution worth 21.1 on Tuvel, badly hurt, moves at once.</summary>
        [Fact]
        public void ALargeGain_MovesBeforeItSettles() => Assert.True(ValueModel.UniqueMoveAllowed(1, 21.1f));

        [Fact]
        public void NoGain_NeverMoves()
        {
            Assert.False(ValueModel.UniqueMoveAllowed(null, 0f));
            Assert.False(ValueModel.UniqueMoveAllowed(5, float.NaN));
        }

        [Theory]
        [InlineData(0, 20f, false)]
        [InlineData(1, 4.4f, false)]
        [InlineData(2, 1f, false)]
        [InlineData(1, 5f, true)]
        [InlineData(3, 1f, true)]
        [InlineData(3, 0f, false)]
        public void SiblingReplacement_SettlesTheRemovedBuff(int age, float gain, bool allowed) =>
            Assert.Equal(allowed, ValueModel.UniqueReplacementAllowed(age, gain));

        [Fact]
        public void IncomingVariantHasNoCast_StillUsesTheHeldSiblingsPlacement()
        {
            var casts = new CastLedger();
            casts.Record("Lann", "constitution-tier1", "Tuvel", 2);
            casts.Confirm("Lann");
            Assert.Null(casts.LastRound("Lann", "strength-tier1", "Tuvel"));
            int? placed = casts.LastRound("Lann", "constitution-tier1", "Tuvel");
            Assert.False(ValueModel.UniqueReplacementAllowed(3 - placed, 4.4f));
            Assert.True(ValueModel.UniqueReplacementAllowed(null, 4.4f)); // independent second tier, no removed buff
        }

        [Fact]
        public void UnperformedSiblingCast_DoesNotCreateASettleLock()
        {
            var casts = new CastLedger();
            casts.Record("Lann", "constitution-tier1", "Tuvel", 2);
            casts.TakeBack("Lann");
            int? placed = casts.LastRound("Lann", "constitution-tier1", "Tuvel");
            Assert.True(ValueModel.UniqueReplacementAllowed(3 - placed, 4.4f));
        }
    }
}
