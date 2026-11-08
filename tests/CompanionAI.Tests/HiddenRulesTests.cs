using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ v0.3.30 competence3: where a revealing area is aimed and which source pays for it.
    public class HiddenRulesTests
    {
        [Fact]
        public void OneSpot_IsTheSpot()
        {
            Assert.Equal(0, HiddenRules.CoveringSpot(new[] { 3f }, new[] { 4f }, 1.5f, out int covered));
            Assert.Equal(1, covered);
        }

        [Fact]
        public void TheSpotHoldingMostVanishings_Wins()
        {
            // Two enemies vanished 1 m apart, a third 10 m away: the first spot holds both of the pair.
            int best = HiddenRules.CoveringSpot(new[] { 10f, 0f, 1f }, new[] { 0f, 0f, 0f }, 1.5f, out int covered);
            Assert.Equal(1, best);
            Assert.Equal(2, covered);
        }

        [Fact]
        public void Ties_KeepTheFirst_AndARadiusOfZeroHoldsOnlyItself()
        {
            Assert.Equal(0, HiddenRules.CoveringSpot(new[] { 0f, 5f }, new[] { 0f, 0f }, 0f, out int covered));
            Assert.Equal(1, covered);
        }

        [Fact]
        public void NoOrBadInput_NoSpot()
        {
            Assert.Equal(-1, HiddenRules.CoveringSpot(new float[0], new float[0], 3f, out int none));
            Assert.Equal(0, none);
            Assert.Equal(-1, HiddenRules.CoveringSpot(new[] { 1f }, new[] { 1f, 2f }, 3f, out _));
            Assert.Equal(-1, HiddenRules.CoveringSpot(new[] { float.NaN }, new[] { 0f }, 3f, out _));
        }

        [Fact]
        public void Sources_FreeThenSpellsByLevelThenChargesThenUsedUpItems()
        {
            float free = HiddenRules.SourceCost(costsNothing: true, item: false, usedUp: false, spellLevel: 0);
            float first = HiddenRules.SourceCost(false, false, false, 1);
            float second = HiddenRules.SourceCost(false, false, false, 2);
            float wand = HiddenRules.SourceCost(false, item: true, usedUp: false, spellLevel: 2);
            float scroll = HiddenRules.SourceCost(false, item: true, usedUp: true, spellLevel: 1);
            Assert.True(free < first && first < second && second < wand && wand < scroll);
        }
    }
}
