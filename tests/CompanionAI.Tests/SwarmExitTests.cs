using System;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class SwarmExitTests
    {
        [Fact]
        public void NativeBoundaryCanBeBetweenOldRingsOrBeyondSixMetres()
        {
            var small = new[] { new SwarmGeometry.Circle(0, 0, 1) };
            Assert.Equal(1.35f, SwarmGeometry.ExitDistance(small, 0, 0, 1, 0, 1.4f, .35f), 4);
            var large = new[] { new SwarmGeometry.Circle(0, 0, 7) };
            Assert.Equal(7.35f, SwarmGeometry.ExitDistance(large, 0, 0, 1, 0, 9, .35f), 4);
            Assert.Equal(-1f, SwarmGeometry.ExitDistance(large, 0, 0, 1, 0, 6, .35f));
        }

        [Fact]
        public void ConnectedOverlapMustBeClearedButDisjointSpaceDoesNotLengthenTheExit()
        {
            var circles = new[] { new SwarmGeometry.Circle(2, 0, 2), new SwarmGeometry.Circle(0, 0, 1), new SwarmGeometry.Circle(20, 0, 3) };
            Assert.Equal(4.35f, SwarmGeometry.ExitDistance(circles, 0, 0, 1, 0, 10, .35f), 4);
            Assert.Equal(1.35f, SwarmGeometry.ExitDistance(circles, 0, 0, -1, 0, 10, .35f), 4);
            Array.Reverse(circles);
            Assert.Equal(4.35f, SwarmGeometry.ExitDistance(circles, 0, 0, 1, 0, 10, .35f), 4);
        }

        [Fact]
        public void PrimaryTargetAndNearbyAlliesHaveSeparateBurstBoundaries()
        {
            // Caller supplies native approach distance for this body, plus radius/body circles at other eligible anchors.
            var circles = new[] { new SwarmGeometry.Circle(0, 0, 1), new SwarmGeometry.Circle(1, 0, 2) };
            Assert.Equal(3.35f, SwarmGeometry.ExitDistance(circles, 0, 0, 1, 0, 5, .35f), 4);
            Assert.Equal(1.35f, SwarmGeometry.ExitDistance(circles, 0, 0, -1, 0, 5, .35f), 4);
        }

        [Fact]
        public void ClearanceAccountsForTheMoveCommandsStopRadius()
        {
            var circles = new[] { new SwarmGeometry.Circle(0, 0, 2) };
            float exit = SwarmGeometry.ExitDistance(circles, 0, 0, 1, 0, 3, .35f);
            Assert.True(exit - .3f > 2f);
            Assert.Equal(-1f, SwarmGeometry.ExitDistance(circles, 4, 0, 1, 0, 3, .35f));
        }

        [Fact]
        public void EmptyZeroAndInvalidInputsDoNotCreateMoves()
        {
            var circles = new[] { new SwarmGeometry.Circle(0, 0, 2) };
            Assert.Equal(-1f, SwarmGeometry.ExitDistance(Array.Empty<SwarmGeometry.Circle>(), 0, 0, 1, 0, 3, .35f));
            Assert.Equal(-1f, SwarmGeometry.ExitDistance(circles, 0, 0, 0, 0, 3, .35f));
            Assert.Equal(-1f, SwarmGeometry.ExitDistance(circles, 0, 0, 1, 0, 0, .35f));
            Assert.Equal(-1f, SwarmGeometry.ExitDistance(circles, float.NaN, 0, 1, 0, 3, .35f));
        }

        [Fact]
        public void RepeatedBoundaryQueriesAllocateNoManagedMemory()
        {
            var circles = new[] { new SwarmGeometry.Circle(0, 0, 2), new SwarmGeometry.Circle(2, 0, 2) };
            for (int i = 0; i < 100; i++) SwarmGeometry.ExitDistance(circles, 0, 0, 1, 0, 8, .35f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            float sum = 0;
            for (int i = 0; i < 10000; i++) sum += SwarmGeometry.ExitDistance(circles, 0, 0, 1, 0, 8, .35f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(sum > 40000);
            Assert.Equal(0, allocated);
        }
    }
}
