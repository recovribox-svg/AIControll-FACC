using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ review follow-up A-R1 (docs/BURST_PATH_FORECAST.md): a walk along the game's own path, for the burst forecast.
    public class WalkRouteTests
    {
        private static WalkRoute Route(float speed, params float[] xz)
        {
            var route = new WalkRoute();
            route.Start(xz[0], 0f, xz[1], speed);
            for (int i = 2; i + 1 < xz.Length; i += 2) route.Add(xz[i], 0f, xz[i + 1]);
            return route;
        }

        [Fact]
        public void APathRoundACornerIsFollowed()
        {
            // Native path (0,0) -> (5,0) -> (5,10) at 2 m/s: after 4 s the unit is 8 m along it, at (5,3).
            var route = Route(2f, 0f, 0f, 5f, 0f, 5f, 10f);
            Assert.Equal(15f, route.Length, 4);
            route.At(4f, out float x, out _, out float z);
            Assert.Equal(5f, x, 4);
            Assert.Equal(3f, z, 4);
            // The straight walk to the same end would have it at 8 m along the chord: about (3.58, 7.16).
            route.At(1f, out x, out _, out z);
            Assert.Equal(2f, x, 4);
            Assert.Equal(0f, z, 4);
            route.At(30f, out x, out _, out z);   // it stops at the end
            Assert.Equal(5f, x, 4);
            Assert.Equal(10f, z, 4);
        }

        [Fact]
        public void TheWalkStopsWhereTheCommandIsCloseEnough()
        {
            // The command's point at the end of the path, radius 2 m: the walk ends 2 m short, on the path.
            var straight = Route(3f, 0f, 0f, 10f, 0f);
            straight.StopWithin(10f, 0f, 0f, 2f);
            Assert.Equal(8f, straight.EndX, 4);
            Assert.Equal(8f, straight.Length, 4);
            // Round a corner: (0,0) -> (0,10) -> (10,10), point (10,10), radius 3: it stops at (7,10).
            var bend = Route(3f, 0f, 0f, 0f, 10f, 10f, 10f);
            bend.StopWithin(10f, 0f, 10f, 3f);
            Assert.Equal(7f, bend.EndX, 4);
            Assert.Equal(10f, bend.EndZ, 4);
            Assert.Equal(17f, bend.Length, 4);
            // The first time the path comes close enough counts, even when it would go on: the command starts there.
            var past = Route(3f, 0f, 0f, 10f, 0f, 10f, 10f);
            past.StopWithin(6f, 0f, 1f, 2f);
            Assert.Equal(2, past.Count);
            Assert.True(past.EndX < 6f && past.EndX > 4f);
        }

        [Fact]
        public void AWalkThatStartsCloseEnoughIsNoWalk()
        {
            var route = Route(3f, 0f, 0f, 10f, 0f);
            route.StopWithin(1f, 0f, 0f, 2f);
            Assert.Equal(1, route.Count);
            route.At(5f, out float x, out _, out float z);
            Assert.Equal(0f, x);
            Assert.Equal(0f, z);
        }

        [Fact]
        public void AnAllyWalkingRoundAnObstacleIsCaughtWhereTheStraightLineMissesIt()
        {
            // The ally walks round a wall: (0,0) -> (0,8) -> (10,8) -> (10,0) at 2 m/s. The burst lands at (5,8) in 6.5 s.
            // On its path it is at (5,8) then (13 m along it); on the straight line to (10,0) it would be at (10,0).
            var path = Route(2f, 0f, 0f, 0f, 8f, 10f, 8f, 10f, 0f);
            Assert.True(path.MayBeInside(5f, 8f, 1.5f, 6.5f));
            var straight = Route(2f, 0f, 0f, 10f, 0f);
            Assert.False(straight.MayBeInside(5f, 8f, 1.5f, 6.5f));
        }

        [Fact]
        public void AnEnemyWalkingRoundAnObstacleIsLedAlongItsPath()
        {
            // An enemy whose path bends away from the straight line: the lead point follows the path.
            var path = Route(3f, 0f, 0f, 6f, 6f, 12f, 0f);
            path.At(2f, out float x, out _, out float z);   // 6 m along the first leg (length 8.49)
            Assert.Equal(6f / 8.4853f * 6f, x, 2);
            Assert.Equal(6f / 8.4853f * 6f, z, 2);
            Assert.True(z > 4f);                            // the straight line would keep it at z = 0
        }

        [Fact]
        public void HeightFollowsThePath()
        {
            var route = new WalkRoute();
            route.Start(0f, 0f, 0f, 2f);
            route.Add(4f, 2f, 0f);
            route.At(1f, out _, out float y, out _);
            Assert.Equal(1f, y, 4);
        }

        [Fact]
        public void ARouteKeepsAtMostItsCapAndSkipsRepeatedPoints()
        {
            var route = new WalkRoute();
            route.Start(0f, 0f, 0f, 2f);
            Assert.True(route.Add(0f, 0f, 0f));   // on top of the start: skipped, not refused
            Assert.Equal(1, route.Count);
            for (int i = 1; i < WalkRoute.MaxPoints; i++) Assert.True(route.Add(i, 0f, 0f));
            Assert.Equal(WalkRoute.MaxPoints, route.Count);
            Assert.False(route.Add(1000f, 0f, 0f));
            Assert.Equal(WalkRoute.MaxPoints - 1, route.Length, 3);
        }

        [Fact]
        public void HorizontalOverlapOnAnotherFloorIsNotAnApproachStop()
        {
            var route = new WalkRoute();
            route.Start(0f, 8f, 0f, 2f);
            route.Add(10f, 8f, 0f);
            route.StopWithin(10f, 0f, 0f, 2f);
            Assert.Equal(10f, route.EndX);
            Assert.Equal(8f, route.EndY);   // no distance-eligible point on this path
        }

        [Fact]
        public void ADescendingRouteStopsAtTheNativeHeightBoundary()
        {
            var route = new WalkRoute();
            route.Start(0f, 8f, 0f, 2f);
            route.Add(10f, 8f, 0f);
            route.Add(11f, 0f, 0f);
            route.StopWithin(11f, 0f, 0f, 2f);
            Assert.Equal(10.5f, route.EndX, 4);
            Assert.Equal(4f, route.EndY, 4); // mechanics distance uses Y² / 4 above 1.5 m
        }

        [Fact]
        public void SmallHeightDifferencesUseNativePlanarDistance()
        {
            var route = new WalkRoute();
            route.Start(0f, 1f, 0f, 2f);
            route.Add(10f, 1f, 0f);
            route.StopWithin(10f, 0f, 0f, 1f);
            Assert.Equal(9f, route.EndX, 4);
            Assert.Equal(1f, route.EndY, 4); // native threshold ignores this height, unlike Euclidean distance
        }

        [Fact]
        public void ASmallStopRadiusSurvivesALongPathSegment()
        {
            var route = Route(3f, 0f, 0f, 10000f, 0f);
            route.StopWithin(10000f, 0f, 0f, 0.1f);
            Assert.Equal(9999.9f, route.EndX, 3); // float-only discriminant cancellation used to lose the radius
        }

        [Fact]
        public void StartClearsTheRoute()
        {
            var route = Route(2f, 0f, 0f, 10f, 0f);
            route.Native = true;
            route.Start(1f, 0f, 1f, 3f);
            Assert.Equal(1, route.Count);
            Assert.False(route.Native);
            Assert.Equal(0f, route.Length);
            Assert.Equal(3f, route.Speed);
        }
    }
}
