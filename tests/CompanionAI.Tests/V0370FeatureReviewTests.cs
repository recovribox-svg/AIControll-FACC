using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.70 features review: a spell prohibition prices a caster its own decisions have not measured yet.</summary>
    public sealed class V0370FeatureReviewTests
    {
        [Fact]
        public void UnmeasuredFullCasterLosesARoundOfItsLevel()
        {
            // A 9th-level wizard with 5th-level spells: full casting, a whole round of a 9th-level unit.
            Assert.Equal(ValueModel.PerRound(9), ValueModel.UnmeasuredSpellEdge(9, 5, usesWeapon: false), 4);
        }

        [Fact]
        public void PartialCastersAndWeaponUsersLoseLess()
        {
            // A 20th-level magus (6th-level spells, Pure Mage: no weapon attack) loses 6/10.5 of a round.
            Assert.Equal(ValueModel.PerRound(20) * 6f / 10.5f, ValueModel.UnmeasuredSpellEdge(20, 6, false), 3);
            // A paladin of 8 (2nd-level spells) who fights with a weapon: a fraction of that, halved.
            float paladin = ValueModel.UnmeasuredSpellEdge(8, 2, usesWeapon: true);
            Assert.True(paladin > 0f && paladin < ValueModel.UnmeasuredSpellEdge(8, 2, false));
            Assert.Equal(ValueModel.PerRound(8) * 2f / 4.5f * 0.5f, paladin, 3);
        }

        [Fact]
        public void NoSpellbookNoLoss()
        {
            Assert.Equal(0f, ValueModel.UnmeasuredSpellEdge(0, 0, false));
            Assert.Equal(0f, ValueModel.UnmeasuredSpellEdge(5, 0, false));
        }

        [Fact]
        public void TheEstimateBlocksARageThatWouldCostTheCasterItsSpells()
        {
            // Inspired Rage in the turn-based log: 33.7 over 4.4 rounds for the others; the Knight Commander counted 0.
            float edge = ValueModel.UnmeasuredSpellEdge(20, 6, false);
            Assert.True(SpellEdgeMemory.Loss(edge, 4.4f) > 33.7f);
        }
    }
}
