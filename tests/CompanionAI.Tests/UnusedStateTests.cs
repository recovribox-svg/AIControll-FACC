using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public sealed class UnusedStateTests
    {
        [Fact]
        public void ScoredOptionKeepsItsVerdictAgainstLaterTargetsInTheSameDecision()
        {
            var s = new UnusedState();
            s.Score(8f, decision: 1);
            s.Record(UnusedReason.NoApproach, 0f, decision: 1);   // another target out of range
            s.Record(UnusedReason.ItemOnSummon, 0f, decision: 1);
            Assert.Equal(UnusedReason.Scored, s.Reason);
            Assert.Equal(8f, s.Best);
        }

        [Fact]
        public void ExclusionBeforeTheScoringTargetIsReplaced()
        {
            var s = new UnusedState();
            s.Record(UnusedReason.FriendlyFire, -2f, decision: 1);
            s.Score(5f, decision: 1);
            Assert.Equal(UnusedReason.Scored, s.Reason);
        }

        [Fact]
        public void TheLastDecisionsVerdictWins()
        {
            var s = new UnusedState();
            s.Score(6f, decision: 1);
            s.Record(UnusedReason.Unavailable, 0f, decision: 2);
            Assert.Equal(UnusedReason.Unavailable, s.Reason);
            Assert.Equal(6f, s.Best);
        }

        [Fact]
        public void ANonPositiveScoreIsNotAScoredVerdict()
        {
            var s = new UnusedState();
            s.Record(UnusedReason.ItemBar, 3f, decision: 1);
            s.Score(0f, decision: 1);
            Assert.Equal(UnusedReason.ItemBar, s.Reason);
            Assert.Equal(3f, s.Best);
        }
    }
}
