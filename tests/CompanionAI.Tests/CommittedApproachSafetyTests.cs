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
    }
}
