using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class CommittedApproachSafetyTests
    {
        [Fact]
        public void ClearCentralMeleeArrivalFitsWithoutSelectingAnApproachDirection()
        {
            Assert.True(CommittedApproachSafety.FullyInside(0f, 2.2f, 0.4f, 3.0f));
            Assert.True(CommittedApproachSafety.FullyInside(1f, 1f, 0.25f, 3f));
        }

        [Fact]
        public void OverlapOrAChosenNearSideDoesNotProveTheWholeArrivalSafe()
        {
            // The near side reaches the circle, but the attacker can legally arrive at its far side.
            Assert.False(CommittedApproachSafety.FullyInside(2f, 2f, 0.4f, 3f));
            Assert.False(CommittedApproachSafety.FullyInside(0f, 16f, 0.4f, 3f));
        }

        [Fact]
        public void BodyAndExactBoundaryRemainUncertain()
        {
            Assert.False(CommittedApproachSafety.FullyInside(1f, 1.5f, 0.5f, 3f));
            Assert.False(CommittedApproachSafety.FullyInside(1f, 1.5f, 0.6f, 3f));
            Assert.True(CommittedApproachSafety.FullyInside(1f, 1.5f, 0.4f, 3f));
        }

        [Theory]
        [InlineData(-1f, 1f, 0.5f, 3f)]
        [InlineData(0f, -1f, 0.5f, 3f)]
        [InlineData(0f, 0f, 0.5f, 3f)]
        [InlineData(0f, 1f, -0.5f, 3f)]
        [InlineData(0f, 1f, 0.5f, 0f)]
        [InlineData(0f, 1f, 0.5f, -3f)]
        public void InvalidNativeGeometryDoesNotInventAConflict(float distance, float approach, float body, float radius) =>
            Assert.False(CommittedApproachSafety.FullyInside(distance, approach, body, radius));

        [Fact]
        public void NonFiniteUnknownsCannotBecomeARejection()
        {
            foreach (float unknown in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.False(CommittedApproachSafety.FullyInside(unknown, 1f, 0.4f, 3f));
                Assert.False(CommittedApproachSafety.FullyInside(0f, unknown, 0.4f, 3f));
                Assert.False(CommittedApproachSafety.FullyInside(0f, 1f, unknown, 3f));
                Assert.False(CommittedApproachSafety.FullyInside(0f, 1f, 0.4f, unknown));
            }
            Assert.False(CommittedApproachSafety.FullyInside(float.MaxValue, float.MaxValue, 0f, float.MaxValue));
        }
    
        // ★ v0.3.27 (test3 log, fight 10): the walk to where the ally stops meets the circle.
        [Fact]
        public void WalkToATargetInsideTheCircle_MeetsIt() =>
            // Ally 10 m east of a target 1 m from the centre of a 3 m circle: it stops 1.6 m east of the target, inside.
            Assert.True(CommittedApproachSafety.WalkMeetsCircle(11f, 0f, 1f, 0f, 1.6f, 0.5f, 0f, 0f, 3f));

        [Fact]
        public void TargetAtTheFarEdge_TheWalkCrossesTheCircle() =>
            // Target on the far side (2.5 m west): the walk from the east passes through the circle on the way.
            Assert.True(CommittedApproachSafety.WalkMeetsCircle(10f, 0f, -2.5f, 0f, 1.6f, 0.5f, 0f, 0f, 3f));

        [Fact]
        public void TargetJustInsideTheNearEdge_StopsOutside() =>
            // Target 3 m east of the centre, ally further east: it stops 1.6 m short, 4.6 m out - outside with its body.
            Assert.False(CommittedApproachSafety.WalkMeetsCircle(10f, 0f, 3f, 0f, 1.6f, 0.5f, 0f, 0f, 3f));

        [Fact]
        public void WalkPassingWide_DoesNotMeetIt() =>
            Assert.False(CommittedApproachSafety.WalkMeetsCircle(10f, 6f, 1f, 6f, 1.6f, 0.5f, 0f, 0f, 3f));

        [Fact]
        public void BodyCountsAtTheEdge() =>
            // The walk passes 3.4 m from the centre: inside with a 0.5 m body, outside with 0.3 m.
            Assert.True(CommittedApproachSafety.WalkMeetsCircle(10f, 3.4f, -10f, 3.4f, 1.6f, 0.5f, 0f, 0f, 3f) &&
                        !CommittedApproachSafety.WalkMeetsCircle(10f, 3.4f, -10f, 3.4f, 1.6f, 0.3f, 0f, 0f, 3f));

        [Fact]
        public void AlreadyInReach_NoWalk() =>
            Assert.False(CommittedApproachSafety.WalkMeetsCircle(2f, 0f, 1f, 0f, 1.6f, 0.5f, 0f, 0f, 3f));

        [Fact]
        public void LongRangeApproach_StopsFarAway() =>
            // An archer's 16 m approach stops 16 m short of a target in the circle, east of it, far outside.
            Assert.False(CommittedApproachSafety.WalkMeetsCircle(30f, 0f, 0f, 0f, 16f, 0.5f, 0f, 0f, 3f));

        [Fact]
        public void UnusableInput_NoConflict()
        {
            Assert.False(CommittedApproachSafety.WalkMeetsCircle(float.NaN, 0f, 1f, 0f, 1.6f, 0.5f, 0f, 0f, 3f));
            Assert.False(CommittedApproachSafety.WalkMeetsCircle(11f, 0f, 1f, 0f, -1f, 0.5f, 0f, 0f, 3f));
            Assert.False(CommittedApproachSafety.WalkMeetsCircle(11f, 0f, 1f, 0f, 1.6f, 0.5f, 0f, 0f, 0f));
        }
    }
}
