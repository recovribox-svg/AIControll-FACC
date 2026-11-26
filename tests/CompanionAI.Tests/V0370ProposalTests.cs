using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.70 P1/P2: the pure rules behind escape teleports and a summon declining an ally's area effect.</summary>
    public sealed class V0370ProposalTests
    {
        [Fact]
        public void MainActionEscapeGivesUpTheBestMainAction()
        {
            // A Dimension Door (standard) to a withdraw spot worth 30, while the best main action is worth 12.
            Assert.Equal(18f, ValueModel.TeleportEscapeWorth(30f, 0f, 12f, spendsMain: true, reserve: 1f), 4);
            // A swift teleport keeps the main action: only the walk's own losses count.
            Assert.Equal(26f, ValueModel.TeleportEscapeWorth(30f, 4f, 12f, spendsMain: false, reserve: 1f), 4);
        }

        [Fact]
        public void EscapeWorthIsRestrainedAndNeverNegative()
        {
            Assert.Equal(9f, ValueModel.TeleportEscapeWorth(30f, 0f, 12f, true, 0.5f), 4);
            Assert.Equal(0f, ValueModel.TeleportEscapeWorth(10f, 0f, 12f, true, 1f));
            Assert.Equal(0f, ValueModel.TeleportEscapeWorth(10f, 0f, 0f, false, -1f));
        }

        [Fact]
        public void SummonDeclinesOnlyWhenItsSpellsClearlyBeatTheEffect()
        {
            // Nabasu: Enervation's edge 40 a round against a rage worth 3 a round.
            Assert.True(ValueModel.DeclinesGrant(40f, 3f));
            // Equal or within the margin: keep it.
            Assert.False(ValueModel.DeclinesGrant(3f, 3f));
            Assert.False(ValueModel.DeclinesGrant(3.4f, 3f));
            // Spells worth nothing now: keep it.
            Assert.False(ValueModel.DeclinesGrant(0f, 0f));
        }

        [Fact]
        public void DeclinedEffectComesBackOnlyWhenItClearlyGivesMore()
        {
            Assert.True(ValueModel.ReacceptsGrant(1f, 3f));
            // Between the two thresholds the switch stays as it is (no flip-flop).
            Assert.False(ValueModel.DeclinesGrant(3.2f, 3f));
            Assert.False(ValueModel.ReacceptsGrant(3.2f, 3f));
            Assert.False(ValueModel.ReacceptsGrant(2f, 3f));
        }
    }
}
