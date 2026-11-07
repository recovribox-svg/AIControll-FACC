using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.29 test 4: spot claims in real time (Tactics/SpotRules.cs).</summary>
    public class SpotRulesTests
    {
        [Fact]
        public void TwoCompanionsFromNearlyOneSpot_ClaimTheSameEndSpot()
        {
            // v0.3.29 test 2 real-time fight 1: Seelah and Camellia 0.8 m apart, both walking to one Corrupted Mongrel
            // (approach radius 1.6 m). Their straight walks end 0.1 m apart: one body's spot.
            SpotRules.WalkEnd(57.85f, 124.12f, 53.35f, 113.76f, 1.6f, out float sx, out float sz);
            SpotRules.WalkEnd(58.64f, 123.94f, 53.35f, 113.76f, 1.6f, out float cx, out float cz);
            Assert.True(SpotRules.Takes(sx, sz, 0.5f, cx, cz, 0.5f));
        }

        [Fact]
        public void WalkEnd_StopsNinetyPercentOfTheRadiusShort()
        {
            SpotRules.WalkEnd(10f, 0f, 0f, 0f, 2f, out float x, out float z);
            Assert.Equal(1.8f, x, 3);
            Assert.Equal(0f, z, 3);
        }

        [Fact]
        public void WalkEnd_AlreadyCloserThanThat_StaysPut()
        {
            SpotRules.WalkEnd(1f, 0f, 0f, 0f, 2f, out float x, out float z);
            Assert.Equal(1f, x, 3);
            Assert.Equal(0f, z, 3);
        }

        [Fact]
        public void WalkEnd_OnThePoint_IsThePoint()
        {
            SpotRules.WalkEnd(3f, 4f, 3f, 4f, 1.5f, out float x, out float z);
            Assert.Equal(3f, x, 3);
            Assert.Equal(4f, z, 3);
        }

        [Fact]
        public void Takes_ByNinetyPercentOfTheSummedRadii()
        {
            // Two Medium bodies (0.5 m each): closer than 0.9 m take one spot, 0.9 m apart do not.
            Assert.True(SpotRules.Takes(0f, 0f, 0.5f, 0.89f, 0f, 0.5f));
            Assert.False(SpotRules.Takes(0f, 0f, 0.5f, 0.9f, 0f, 0.5f));
        }

        [Fact]
        public void Takes_ALargeBodyTakesMore() => Assert.True(SpotRules.Takes(0f, 0f, 1.2f, 1.4f, 0f, 0.5f));

        [Fact]
        public void Takes_BrokenInput_TakesNothing() => Assert.False(SpotRules.Takes(float.NaN, 0f, 0.5f, 0f, 0f, 0.5f));

        [Fact]
        public void Applies_OnlyInRealTime_ToContactApproaches()
        {
            Assert.True(SpotRules.Applies(true, 1.6f, false));      // a melee attack
            Assert.False(SpotRules.Applies(false, 1.6f, false));    // turn-based: the game's path does not stop on allies
            Assert.False(SpotRules.Applies(true, 9f, false));       // a cast from range stands clear of the crowd
            Assert.False(SpotRules.Applies(true, 1.6f, true));      // nothing left to move with
        }
        [Fact]
        public void WalkSteps_CountsTheStretchWalkedDuringTheCast()
        {
            // Seelah, 3 m/s, 9 m from where she stops: 6 m of it in 2 s, tested every 0.5 m.
            Assert.Equal(12, SpotRules.WalkSteps(9f, 3f, SpotRules.WalkInSeconds, SpotRules.WalkInStep, out float covered));
            Assert.Equal(6f, covered, 3);
        }

        [Fact]
        public void WalkSteps_AShortWalkIsTestedToItsEnd()
        {
            Assert.Equal(3, SpotRules.WalkSteps(1.2f, 3f, 2f, 0.5f, out float covered));
            Assert.Equal(1.2f, covered, 3);
        }

        [Fact]
        public void WalkSteps_NoWalkOrBrokenInput_TestsNothing()
        {
            Assert.Equal(0, SpotRules.WalkSteps(0f, 3f, 2f, 0.5f, out _));
            Assert.Equal(0, SpotRules.WalkSteps(5f, float.NaN, 2f, 0.5f, out _));
            Assert.Equal(0, SpotRules.WalkSteps(float.PositiveInfinity, 3f, 2f, 0.5f, out _));
        }

        [Fact]
        public void StuckInARow_FromOneSpot_Counts()
        {
            // Orvath at the start of the second test-4 fight: walks 0.2-0.8 m apart, 10 s or so between them.
            int count = 1;
            count = SpotRules.StuckInARow(count, 0.6f, 10d);
            Assert.Equal(2, count);
            Assert.True(count >= SpotRules.BoxedInWalks);
            Assert.Equal(3, SpotRules.StuckInARow(count, 0.1f, 4d));
        }

        [Fact]
        public void StuckInARow_ElsewhereOrLater_StartsAfresh()
        {
            Assert.Equal(1, SpotRules.StuckInARow(1, 3f, 5d));     // the unit got somewhere in between
            Assert.Equal(1, SpotRules.StuckInARow(1, 0.2f, 30d));  // a long time since the last one
            Assert.Equal(1, SpotRules.StuckInARow(0, 0.2f, 5d));   // no walk before
            Assert.Equal(1, SpotRules.StuckInARow(2, float.NaN, 5d));
        }
    }
}
