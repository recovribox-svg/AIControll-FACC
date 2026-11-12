using CompanionAI_Pathfinder.Analysis;
using Xunit;

namespace CompanionAI.Tests
{
    public class AreaExitGeometryTests
    {
        // Native vertical capsule: five-foot radius, long segment; body surface, not centre, must leave it.
        [Theory]
        [InlineData(0, 0, 0, true)]
        [InlineData(1.8, 0, 0, true)]
        [InlineData(1.924, 0, 0, true)]
        [InlineData(1.925, 0, 0, false)]
        [InlineData(0, 101.8, 0, true)]
        [InlineData(0, 101.925, 0, false)]
        [InlineData(1.5, 101.5, 0, false)]
        [InlineData(0, -101.8, 0, true)]
        [InlineData(0, 0, -1.925, false)]
        public void CapsuleUsesFullSphereAndRoundedEnds(float x, float y, float z, bool expected) =>
            Assert.Equal(expected, AreaExitGeometry.CapsuleOverlaps(x, y, z, 100f, 1.524f, 0.4f));

        [Theory]
        [InlineData(1.8, 0, 0, true)]
        [InlineData(1.81, 0, 0, false)]
        [InlineData(1.5, 2.5, 0, true)]
        [InlineData(1.6, 2.6, 0, false)]
        [InlineData(1.4, 2.4, 3.4, true)]
        [InlineData(1.5, 2.5, 3.5, false)]
        [InlineData(0, 2.81, 0, false)]
        [InlineData(-1.8, 0, 0, true)]
        public void BoxUsesNearestSurfaceIncludingEdgesAndCorners(float x, float y, float z, bool expected) =>
            Assert.Equal(expected, AreaExitGeometry.BoxOverlaps(x, y, z, 1f, 2f, 3f, 0.8f));

        [Fact]
        public void ArrivalClearanceAndActualBodySizeCannotBeIgnored()
        {
            Assert.False(AreaExitGeometry.CapsuleOverlaps(2f, 0, 0, 100, 1.524f, 0.4f));
            Assert.True(AreaExitGeometry.CapsuleOverlaps(2f, 0, 0, 100, 1.524f, 0.4f + 0.15f));
            Assert.True(AreaExitGeometry.CapsuleOverlaps(2f, 0, 0, 100, 1.524f, 0.6f));
            Assert.False(AreaExitGeometry.BoxOverlaps(2, 0, 0, 1, 2, 3, 0.8f));
            Assert.True(AreaExitGeometry.BoxOverlaps(2, 0, 0, 1, 2, 3, 1.1f));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void UnknownGeometryCannotProveClearance(float bad)
        {
            Assert.True(AreaExitGeometry.CapsuleOverlaps(bad, 0, 0, 100, 1, 0.5f));
            Assert.True(AreaExitGeometry.CapsuleOverlaps(20, 0, 0, 100, bad, 0.5f));
            Assert.True(AreaExitGeometry.BoxOverlaps(20, bad, 0, 1, 2, 3, 0.5f));
            Assert.True(AreaExitGeometry.BoxOverlaps(20, 0, 0, bad, 2, 3, 0.5f));
        }

        [Fact]
        public void InvalidSizesRefuseEscapeAndLargeFiniteValuesDoNotOverflowToInside()
        {
            Assert.True(AreaExitGeometry.CapsuleOverlaps(20, 0, 0, -1, 1, 0.5f));
            Assert.True(AreaExitGeometry.BoxOverlaps(20, 0, 0, -1, 2, 3, 0.5f));
            Assert.False(AreaExitGeometry.CapsuleOverlaps(float.MaxValue, 0, 0, 100, 1, 0.5f));
            Assert.False(AreaExitGeometry.BoxOverlaps(float.MaxValue, 0, 0, 1, 2, 3, 0.5f));
        }
    }
}
