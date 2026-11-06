using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.28 test 7: how much of a melee enemy's round reaches a spot (Tactics/ValueModel.Reach.cs).</summary>
    public class StrikeShareTests
    {
        [Fact]
        public void InReach_FullAttack_WhateverHoldsIt() =>
            Assert.Equal(1f, ValueModel.StrikeShare(inReach: true, inStep: false, heldChance: 0.9f, singleShare: 0.3f), 4);

        [Fact]
        public void OneStepAway_Free_StillAFullAttack() =>
            Assert.Equal(1f, ValueModel.StrikeShare(false, inStep: true, heldChance: 0f, singleShare: 0.4f), 4);

        [Fact]
        public void OneStepAway_HeldHalfTheTime_HalfFullHalfSingle() =>
            Assert.Equal(0.5f + 0.5f * 0.4f, ValueModel.StrikeShare(false, true, 0.5f, 0.4f), 4);

        [Fact]
        public void MustWalk_OneAttack() =>
            Assert.Equal(0.4f, ValueModel.StrikeShare(false, false, 0f, 0.4f), 4);

        [Fact]
        public void MustWalk_OutOfGrease_OnlyIfItStaysUp() =>
            // The Schir in Camellia's Grease: one bardiche swing, and only when its Reflex save holds it up.
            Assert.Equal((1f - 0.4f) * 0.6f, ValueModel.StrikeShare(false, false, 0.4f, 0.6f), 4);

        [Fact]
        public void MustWalk_Held_Nothing() =>
            Assert.Equal(0f, ValueModel.StrikeShare(false, false, 1f, 1f), 4);

        [Fact]
        public void UnknownInputs_AsNoHoldAndAFullAttack()
        {
            Assert.Equal(1f, ValueModel.StrikeShare(false, true, float.NaN, float.NaN), 4);
            Assert.Equal(1f, ValueModel.StrikeShare(false, false, float.NaN, float.NaN), 4);
            Assert.Equal(1f, ValueModel.StrikeShare(false, false, -1f, 2f), 4);
        }
    }
}
