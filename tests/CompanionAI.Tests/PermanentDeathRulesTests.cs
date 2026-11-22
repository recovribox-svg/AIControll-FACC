// ★ coverage3 review / Fusion C3: the permanent-death rules (author's policy: a cast that may kill an ally for good is not used).
using System.Collections.Generic;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class PermanentDeathRulesTests
    {
        private static PermanentDeathRules.DiceGroup D(int count, int sides, int bonus = 0, float factor = 1f, bool maximize = false) =>
            new PermanentDeathRules.DiceGroup { Count = count, Sides = sides, Bonus = bonus, Factor = factor, Maximize = maximize };

        [Fact]
        public void TheVetoIsAtOnePercent()
        {
            Assert.True(PermanentDeathRules.Vetoed(0.01f));
            Assert.True(PermanentDeathRules.Vetoed(0.34f));
            Assert.False(PermanentDeathRules.Vetoed(0.0099f));
            Assert.False(PermanentDeathRules.Vetoed(0f));
            Assert.True(PermanentDeathRules.Vetoed(float.NaN));
        }

        [Fact]
        public void BelowTheVetoTheCostIsHundredsOfTheAllysWorth()
        {
            Assert.Equal(0f, PermanentDeathRules.Cost(0f, 50f));
            Assert.Equal(25f, PermanentDeathRules.Cost(0.005f, 50f), 3);   // 0.5% x 100 x 50
            Assert.Equal(0.5f, PermanentDeathRules.Cost(0.005f, 0f), 3);   // a worth below 1 counts as 1
        }

        [Fact]
        public void DiceTailsAreExact()
        {
            // 1d6: at least 4 = 3/6.
            Assert.Equal(0.5f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup> { D(1, 6) }, 4), 4);
            // 2d6: at least 12 = 1/36; at least 7 = 21/36.
            Assert.Equal(1f / 36f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup> { D(2, 6) }, 12), 5);
            Assert.Equal(21f / 36f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup> { D(2, 6) }, 7), 5);
            // Out of reach: 5d6 never reaches 31.
            Assert.Equal(0f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup> { D(5, 6) }, 31), 6);
            // A threshold at or below 0 is always reached (a downed ally is turned to dust by any hit).
            Assert.Equal(1f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup> { D(5, 6) }, 0));
            Assert.Equal(1f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup>(), -3));
        }

        [Fact]
        public void GroupsConvolveAndModify()
        {
            // 1d6 + 1d6 = 2d6.
            var two = new List<PermanentDeathRules.DiceGroup> { D(1, 6), D(1, 6) };
            Assert.Equal(1f / 36f, PermanentDeathRules.ChanceAtLeast(two, 12), 5);
            // Maximize: 12d6 is always 72.
            Assert.Equal(1f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup> { D(12, 6, maximize: true) }, 72));
            Assert.Equal(0f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup> { D(12, 6, maximize: true) }, 73));
            // Empower x1.5: 1d6 reaches 9 only on a 6.
            Assert.Equal(1f / 6f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup> { D(1, 6, factor: 1.5f) }, 9), 5);
            // Bonus shifts the roll.
            Assert.Equal(1f, PermanentDeathRules.ChanceAtLeast(new List<PermanentDeathRules.DiceGroup> { D(1, 6, bonus: 5) }, 6));
        }

        [Fact]
        public void DisintegrateOnAnAllyByItsHp()
        {
            // Caster level 6, failed save: 12d6 (mean 42). The dust needs HP left below 0 afterwards.
            var failed = new List<PermanentDeathRules.DiceGroup> { D(12, 6) };
            float healthy = PermanentDeathRules.ChanceAtLeast(failed, PermanentDeathRules.DamageToTrigger(100f, 0));
            float wounded = PermanentDeathRules.ChanceAtLeast(failed, PermanentDeathRules.DamageToTrigger(40f, 0));
            float down = PermanentDeathRules.ChanceAtLeast(failed, PermanentDeathRules.DamageToTrigger(-3f, 0));
            Assert.Equal(0f, healthy);                      // 72 at most: never
            Assert.True(wounded > 0.5f && PermanentDeathRules.Vetoed(wounded));
            Assert.Equal(1f, down);
            // Passed save, 5d6: a 30-HP ally is safe, a 20-HP one is not.
            var passed = new List<PermanentDeathRules.DiceGroup> { D(5, 6) };
            Assert.Equal(0f, PermanentDeathRules.ChanceAtLeast(passed, PermanentDeathRules.DamageToTrigger(30f, 0)));
            Assert.True(PermanentDeathRules.Vetoed(PermanentDeathRules.ChanceAtLeast(passed, PermanentDeathRules.DamageToTrigger(20f, 0))));
        }

        [Fact]
        public void TheTriggerCountsTemporaryHp()
        {
            Assert.Equal(11, PermanentDeathRules.DamageToTrigger(10f, 0));    // 10 HP: 11 damage leaves -1
            Assert.Equal(1, PermanentDeathRules.DamageToTrigger(0f, 0));      // 0 HP: any point
            Assert.Equal(-2, PermanentDeathRules.DamageToTrigger(-3f, 0));    // already below: always
        }

        [Theory]
        [InlineData(-3, 30, 0, 0)] // temp HP do not undo an already satisfied lethal-HP gate
        [InlineData(0, 30, 0, 31)]
        [InlineData(10, 20, 0, 31)]
        [InlineData(30, -5, 0, 31)]
        [InlineData(10, 20, 15, 0)]
        public void DustUsesLethalHpAndTemporaryAbsorptionSeparately(int hp, int temporary, int below, int expected) =>
            Assert.Equal(expected, PermanentDeathRules.DamageToTrigger(hp, temporary, below));

        [Theory]
        [InlineData(int.MaxValue, 6, 0)]
        [InlineData(2001, 6, 0)]
        [InlineData(1, int.MaxValue, 0)]
        [InlineData(0, 0, int.MaxValue)]
        public void OversizedRollsCannotAllocateAnUnboundedDistributionOrProveSafety(int count, int sides, int bonus)
        {
            var groups = new[] { D(count, sides, bonus) };
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            float risk = PermanentDeathRules.ChanceAtLeast(groups, 200000);
            Assert.Equal(1f, risk);
            Assert.True(System.GC.GetAllocatedBytesForCurrentThread() - before < 65536);
        }

        [Fact]
        public void AConvolutionIsBoundedBeforeItRuns()
        {
            var groups = new[] { D(0, 0, 3000), D(0, 0, 3000) };
            Assert.Equal(1f, PermanentDeathRules.ChanceAtLeast(groups, 6000));
            Assert.Equal(1f, PermanentDeathRules.ChanceAtLeast(new[] { D(1, 6, factor: float.NaN) }, 100));
            Assert.Equal(int.MaxValue, PermanentDeathRules.DamageToTrigger(int.MaxValue, int.MaxValue, int.MinValue));
        }

        [Fact]
        public void ASmallScaleCannotOverflowABigBonusBeforeScaling()
        {
            var groups = new[] { D(1, 6, int.MaxValue, factor: 0.0000001f, maximize: true) };
            Assert.Equal(1f, PermanentDeathRules.ChanceAtLeast(groups, 214));
            Assert.Equal(0f, PermanentDeathRules.ChanceAtLeast(groups, 215));
        }
    }
}
