using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public sealed class SpellEdgeMemoryTests
    {
        [Fact]
        public void EdgeIsTheSpellsLeadOverTheBestOtherMainAction()
        {
            Assert.Equal(26.1f, SpellEdgeMemory.EdgeOf(61.3f, 35.2f), 3);   // a breath weapon over a full attack
            Assert.Equal(0f, SpellEdgeMemory.EdgeOf(20f, 35f));              // the weapon is better: nothing lost
            Assert.Equal(0f, SpellEdgeMemory.EdgeOf(0f, 10f));               // no spell option
            Assert.Equal(12f, SpellEdgeMemory.EdgeOf(12f, 0f));              // only spells
        }

        [Fact]
        public void UnitMeanFirstThenItsKind()
        {
            var m = new SpellEdgeMemory();
            m.Note("demon#1", "kind:shadow", 20f);
            Assert.True(m.TryGet("demon#2", "kind:shadow", out float kind, out int n));   // a new summon of the same kind
            Assert.Equal(20f, kind);
            Assert.Equal(1, n);
            m.Note("demon#2", "kind:shadow", 0f);
            Assert.True(m.TryGet("demon#2", "kind:shadow", out float own, out _));
            Assert.Equal(0f, own);
            Assert.False(m.TryGet("other", "kind:other", out _, out _));
        }

        [Fact]
        public void RunningMeanFollowsRecharge()
        {
            var m = new SpellEdgeMemory();
            m.Note("u", null, 30f);   // breath ready
            m.Note("u", null, 0f);    // recharging
            Assert.True(m.TryGet("u", null, out float e, out int n));
            Assert.Equal(2, n);
            Assert.Equal(30f * (1f - SpellEdgeMemory.Alpha), e, 3);
        }

        [Fact]
        public void BoundedAndSafe()
        {
            var m = new SpellEdgeMemory();
            for (int i = 0; i < SpellEdgeMemory.MaxEntries; i++) m.Note("u" + i, null, 1f);
            Assert.True(m.Count <= SpellEdgeMemory.MaxEntries);
            m.Note("x", "y", float.NaN);
            Assert.False(m.TryGet("x", "y", out _, out _));
            m.Note("x", "y", -5f);
            Assert.True(m.TryGet("x", null, out float e, out _));
            Assert.Equal(0f, e);
        }

        [Fact]
        public void LossIsTheEdgeForEachRound()
        {
            Assert.Equal(52.2f, SpellEdgeMemory.Loss(26.1f, 2f), 3);
            Assert.Equal(0f, SpellEdgeMemory.Loss(-3f, 2f));
            Assert.Equal(0f, SpellEdgeMemory.Loss(5f, -1f));
        }
    }
}
