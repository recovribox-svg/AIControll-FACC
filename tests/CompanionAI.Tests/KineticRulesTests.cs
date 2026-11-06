using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ v0.3.28 (codex review): policies called by the shipped quiver and burn-pricing paths.
    public class KineticRulesTests
    {
        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(0, 2, true)]
        [InlineData(1, 2, true)]
        [InlineData(2, 2, false)]
        [InlineData(9, 3, false)]
        public void PreparationOnlyRefillsBelowOneCurrentUse(int ranks, int abilityBurn, bool expected) =>
            Assert.Equal(expected, ValueModel.NeedsKineticQuiver(ranks, abilityBurn));

        [Fact]
        public void PreparationDoesNotPlanForAnInfusionThatIsCurrentlyOff()
        {
            Assert.False(ValueModel.NeedsKineticQuiver(0, 0));
            Assert.True(ValueModel.NeedsKineticQuiver(0, 1));
        }

        [Theory]
        [InlineData(60f, 100, 10f, false)] // Exactly half remains: allowed.
        [InlineData(59f, 100, 10f, true)]
        [InlineData(70f, 100, 10f, false)]
        [InlineData(0.5f, 0, 0f, false)] // The existing maximum-HP floor is one.
        public void PreparationKeepsTheExistingHalfHpFloor(float realHp, int maxHp, float loss, bool unsafeBurn) =>
            Assert.Equal(unsafeBurn, ValueModel.PreparationBurnBelowHpFloor(realHp, maxHp, loss));

        [Fact]
        public void PreparationUsesRealHpAfterExistingNonlethalDamage()
        {
            // 100 max HP, 20 lethal + 25 existing nonlethal: 55 real HP; 10 more burn HP leaves 45.
            Assert.True(ValueModel.PreparationBurnBelowHpFloor(55f, 100, 10f));
        }

        // ★ v0.3.28 test 6: Overflow credited in expected damage over the best other action.
        [Theory]
        [InlineData(4, 1f, 0f, 4f, 12f)]      // a blast is already best: the whole gain, three later rounds
        [InlineData(4, 0.5f, 0f, 4f, 6f)]     // half the later blasts hit
        [InlineData(4, 1f, 3f, 4f, 3f)]       // only what it lifts the blast above the weapon
        [InlineData(4, 0.5f, 2f, 4f, 0f)]     // 2 expected damage does not close a gap of 2
        [InlineData(4, 1f, 5f, 4f, 0f)]       // the weapon stays better
        [InlineData(4, 1f, 0f, 1f, 0f)]       // no later round
        [InlineData(0, 1f, 0f, 4f, 0f)]
        public void OverflowCountsTheExpectedGainOverTheBestOtherAction(int gain, float land, float shortfall,
                                                                          float horizon, float expected) =>
            Assert.Equal(expected, ValueModel.KineticOverflowCredit(gain, land, shortfall, horizon), 3);

        [Fact]
        public void NoFreeBlastKeepsTheOneBlastARoundCredit() =>
            Assert.Equal(12f, ValueModel.KineticOverflowCredit(4, 0.5f, null, 4f));

        [Fact]
        public void UnknownNumbersCreditNothing()
        {
            Assert.False(ValueModel.PreparationBurnBelowHpFloor(float.NaN, 100, 10f));
            Assert.Equal(0f, ValueModel.KineticOverflowCredit(4, 1f, float.NaN, 4f));
            Assert.Equal(0f, ValueModel.KineticOverflowCredit(4, float.NaN, 0f, 4f));
            Assert.Equal(0f, ValueModel.KineticOverflowCredit(4, 1f, null, float.NaN));
        }

        [Theory]
        [InlineData(3, 2f, 9, true, 0.6667f)]     // level 6 + Con 3: one burn makes 9 arrows
        [InlineData(3, 2f, 25, true, 0.24f)]      // level 20 + Con 5: arrows get cheaper as the quiver grows
        [InlineData(3, 2f, 9, false, 6f)]         // no burn left to make another: a burn's price each
        [InlineData(0, 2f, 9, true, 0f)]
        [InlineData(3, 0f, 9, true, 0f)]
        [InlineData(3, 2f, 0, true, 6f)]          // an unreadable arrow count never divides by zero
        public void ArrowsCostTheirShareOfABurn(int arrows, float burnPrice, int perQuiver, bool canRemake, float expected) =>
            Assert.Equal(expected, ValueModel.ArrowPrice(arrows, burnPrice, perQuiver, canRemake), 3);

        [Theory]
        [InlineData(20f, 9, 3, 5f, 1f, 59f)]       // 3 shots, 4 rounds left: 3 x 20 - 1
        [InlineData(20f, 25, 3, 5f, 1f, 79f)]      // 8 shots, 4 rounds left: 4 x 20 - 1
        [InlineData(20f, 9, 3, 1f, 1f, -1f)]       // last round: nothing to gain, the burn still costs
        [InlineData(0f, 9, 3, 5f, 1f, 0f)]
        [InlineData(20f, 0, 3, 5f, 1f, 0f)]
        public void RefillIsWorthTheRoundsTheArrowsCover(float gain, int arrows, int perBlast, float horizon, float burn, float expected) =>
            Assert.Equal(expected, ValueModel.QuiverRefillWorth(gain, arrows, perBlast, horizon, burn), 3);

        // ★ v0.3.28 test 6: critical hits.
        [Theory]
        [InlineData(0.05f, 0.6f, 3f, 1.1f)]     // longbow 20/x3
        [InlineData(0.10f, 0.6f, 3f, 1.2f)]     // 19-20/x3
        [InlineData(0.30f, 0.2f, 2f, 1.2f)]     // 15-20 but a hit only on 17+: threats are hits
        [InlineData(0.05f, 0.6f, 1f, 1f)]       // no multiplier
        [InlineData(0f, 0.6f, 3f, 1f)]          // immune to criticals
        [InlineData(float.NaN, 0.6f, 3f, 1f)]
        public void CriticalsRaiseALandedHit(float threat, float hit, float mult, float expected) =>
            Assert.Equal(expected, ValueModel.CritFactor(threat, hit, mult), 3);

        // ★ v0.3.28 test 10: Dark Elementalist Soul Power (character-level pool, Overflow ranks capped at class level / 3).
        [Theory]
        [InlineData(10, 9, false)]
        [InlineData(10, 10, true)]
        [InlineData(10, 14, true)]
        [InlineData(0, 5, false)]
        public void SoulHarvest_NeedsTheCastersLevelInSouls(int pool, int souls, bool expected) =>
            Assert.Equal(expected, ValueModel.SoulHarvestSucceeds(pool, souls));

        [Theory]
        [InlineData(0, 6, 1)]    // level 6: cap 2
        [InlineData(1, 6, 1)]
        [InlineData(2, 6, 0)]    // capped: more successes add nothing
        [InlineData(5, 6, 0)]
        [InlineData(0, 2, 0)]    // below level 3 there is no Overflow at all
        [InlineData(4, 15, 1)]
        public void OverflowRankGain_StopsAtTheGamesCap(int rank, int classLevel, int expected) =>
            Assert.Equal(expected, ValueModel.OverflowRankGain(rank, classLevel));
    }
}
