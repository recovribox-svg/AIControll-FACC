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

        // ★ coverage4: an effect its bearer re-saves against each round holds (1 - p)^k for round k.
        [Fact]
        public void HeldRoundsFollowTheRoundSave()
        {
            Assert.Equal(3f, ValueModel.HeldRounds(3f, 0f), 3);                       // no re-save: the horizon
            Assert.Equal(1f + 0.5f + 0.25f, ValueModel.HeldRounds(3f, 0.5f), 3);       // half pass each round
            Assert.Equal(1f, ValueModel.HeldRounds(6f, 1f), 3);                        // the first save ends it: this round only
            Assert.Equal(1f + 0.5f * 0.5f, ValueModel.HeldRounds(1.5f, 0.5f), 3);      // half of the second round in the horizon
            Assert.Equal(0f, ValueModel.HeldRounds(0f, 0.3f), 3);
            Assert.True(ValueModel.HeldRounds(6f, 0.3f) < ValueModel.HeldRounds(6f, 0.1f));
        }

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

        [Fact]
        public void ASharedRollCannotFailOneEqualDcThenPassTheNext()
        {
            var buffs = new[] { E(0.5f, 10f), E(0.5f, 4f) };
            Assert.Equal(5f, ValueModel.ExpectedDispelWorth(buffs, 1, true), 4);
            Assert.Equal(6f, ValueModel.ExpectedDispelWorth(buffs, 1, false), 4);
        }

        [Fact]
        public void AZeroValueBuffStillConsumesARemoval()
        {
            var buffs = new[] { E(0.5f, 0f), E(0.75f, 20f) };
            Assert.Equal(5f, ValueModel.ExpectedDispelWorth(buffs, 1, true), 4);
            Assert.Equal(7.5f, ValueModel.ExpectedDispelWorth(buffs, 1, false), 4);
            Assert.Equal(0f, ValueModel.ExpectedDispelWorth(new[] { E(1f, 0f), E(1f, 20f) }, 1, true));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ASpellKeepsItsEarlierDispelLossInsteadOfSelectingOnlyALaterGain(bool shared)
        {
            var buffs = new[] { E(1f, -12f), E(1f, 4f), E(1f, 20f) };
            float net = ValueModel.ExpectedDispelWorth(buffs, 2, shared, out float loss);
            Assert.Equal(-8f, net);
            Assert.Equal(12f, loss);
            Assert.Equal(4f, net + loss); // useful removal before accounting for its own-buff loss
        }

        [Theory]
        [InlineData(0f, true, true, 1f)]
        [InlineData(1f, true, true, 1f)]
        [InlineData(0.25f, true, false, 0.25f)]
        [InlineData(0.25f, false, true, 0.75f)]
        public void AnUnconditionalDispelIsNotDiscountedByTheOtherPayloadsSave(float fail, bool onFail, bool onPass, float expected) =>
            Assert.Equal(expected, ValueModel.DispelOutcomeShare(fail, onFail, onPass), 5);

        [Fact]
        public void RemovingBuffsFromAnEnemyDroppedByThatOutcomeEarnsNoBenefit() =>
            Assert.Equal(0.75f, ValueModel.DispelOutcomeShare(0.25f, true, true, 0f, 1f), 5);

        [Fact]
        public void AnEnormousRemovalLimitNeedsNoEnormousStateArray() =>
            Assert.Equal(3f, ValueModel.ExpectedDispelWorth(new[] { E(1f, 1f), E(1f, 2f) }, int.MaxValue), 5);
    }
}
