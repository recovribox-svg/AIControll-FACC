using System;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ burst aim: the pure rules behind ground-aimed delayed bursts (docs/BURST_AIM_AND_TACTICS_PLAN.md §3).
    public class BurstRulesTests
    {
        [Fact]
        public void FullRealTimeTrackingCannotDiscardATurnBasedLandingWait()
        {
            Assert.False(BurstRules.TrackLanding(true, 256, 256));
            Assert.True(BurstRules.TrackLanding(true, 255, 256));
            Assert.True(BurstRules.TrackLanding(false, 256, 256));
            Assert.True(BurstRules.TrackLanding(false, 257, 256));
        }
        [Theory]
        // realTime, fullRound, fromItem, wand, immediate -> seconds (UnitUseAbility.m_CastTime)
        [InlineData(true, false, false, false, false, 2f)]
        [InlineData(true, true, false, false, false, 6f)]
        [InlineData(true, true, true, false, false, 2f)]   // a full-round item keeps the standard time
        [InlineData(true, false, true, true, false, 0f)]   // wand
        [InlineData(true, false, false, false, true, 0f)]  // Immediate animation
        [InlineData(false, true, false, false, false, 0f)] // turn-based: nobody else moves during the turn
        public void CastSecondsFollowTheNativeRule(bool realTime, bool fullRound, bool fromItem, bool wand, bool immediate, float expected)
        {
            Assert.Equal(expected, BurstRules.CastSeconds(realTime, fullRound, fromItem, wand, immediate));
        }

        [Fact]
        public void FlightUsesSpeedAndMinimumTime()
        {
            Assert.Equal(0f, BurstRules.FlightSeconds(20f, 0f, 0f));            // no projectile
            Assert.Equal(20f / 13.5f, BurstRules.FlightSeconds(20f, 13.5f, 0f), 4);   // stock Fireball00
            Assert.Equal(0.5f, BurstRules.FlightSeconds(1f, 13.5f, 0.5f));      // a short flight still takes MinTime
            Assert.Equal(0f, BurstRules.FlightSeconds(float.PositiveInfinity, 13.5f, 0f));
        }

        [Fact]
        public void LandingAddsWalkCastDelayAndFlight()
        {
            // Explosive Runes: in range, 2 s cast, 2 s delay, no projectile.
            Assert.Equal(4f, BurstRules.LandingSeconds(0f, 2f, 2f, 0f));
            Assert.Equal(5.5f, BurstRules.LandingSeconds(1f, 2f, 2f, 0.5f));
            Assert.Equal(2f, BurstRules.LandingSeconds(-1f, 2f, -3f, -0.5f));   // negatives never shorten it
            // ★ coverage3 review: a burst cast starts after its walk and the native wait for its action (the shared start rule).
            Assert.Equal(3f, DisruptRules.OrderedStartSeconds(1f, 3f));
            Assert.Equal(2f, DisruptRules.OrderedStartSeconds(2f, 0.5f));
            Assert.Equal(0f, DisruptRules.OrderedStartSeconds(-1f, -2f));
            Assert.Equal(7f, BurstRules.LandingSeconds(DisruptRules.OrderedStartSeconds(1f, 3f), 2f, 2f, 0f));
        }

        [Fact]
        public void WalkSecondsCountOnlyTheDistanceBeyondRange()
        {
            Assert.Equal(0f, BurstRules.WalkSeconds(8f, 10f, 3f));
            Assert.Equal(1f, BurstRules.WalkSeconds(13f, 10f, 3f));
            Assert.Equal(2f, BurstRules.WalkSeconds(13f, 10f, 0f));   // a speed of 0 reads as the slowest walk
        }

        [Fact]
        public void TravelStopsAtTheEndOfTheWalk()
        {
            Assert.Equal(6f, BurstRules.Travel(10f, 3f, 2f));
            Assert.Equal(4f, BurstRules.Travel(4f, 3f, 2f));
            Assert.Equal(0f, BurstRules.Travel(0f, 3f, 2f));
            Assert.Equal(0f, BurstRules.Travel(10f, 3f, 0f));
            Assert.Equal(3f, BurstRules.Travel(10f, 0f, 2f));   // slowest walk 1.5 m/s
        }

        private static WalkRoute Straight(float fx, float fz, float tx, float tz, float speed)
        {
            var route = new WalkRoute();
            route.Start(fx, 0f, fz, speed);
            route.Add(tx, 0f, tz);
            return route;
        }

        [Fact]
        public void AtLeadsAlongTheStraightWalk()
        {
            Straight(0f, 0f, 10f, 0f, 2f).At(3f, out float x, out _, out float z);
            Assert.Equal(6f, x, 4);
            Assert.Equal(0f, z, 4);
            Straight(0f, 0f, 3f, 4f, 10f).At(3f, out x, out _, out z);   // stops at the end (5 m)
            Assert.Equal(3f, x, 4);
            Assert.Equal(4f, z, 4);
            Straight(1f, 1f, 1f, 1f, 3f).At(3f, out x, out _, out z);    // no walk
            Assert.Equal(1f, x);
            Assert.Equal(1f, z);
        }

        [Fact]
        public void UncertaintyGrowsWithTimeOnlyForWalkers()
        {
            Assert.Equal(0f, BurstRules.Uncertainty(false, 3f, 4f));
            Assert.Equal(0f, BurstRules.Uncertainty(true, 3f, 0f));
            Assert.Equal(3f, BurstRules.Uncertainty(true, 3f, 4f), 4);   // 0.25 x 3 m/s x 4 s
        }

        [Fact]
        public void EnemiesMustBeSurelyInsideAlliesMayBeInside()
        {
            // reach 3.5 m, uncertainty 1 m, distance 3 m: an enemy is not counted, an ally is.
            Assert.False(BurstRules.SurelyInside(3f, 3.5f, 1f));
            Assert.True(BurstRules.MaybeInside(3f, 3.5f, 1f));
            // without uncertainty both agree with the old static test
            Assert.True(BurstRules.SurelyInside(3.5f, 3.5f, 0f));
            Assert.True(BurstRules.MaybeInside(3.5f, 3.5f, 0f));
            Assert.False(BurstRules.SurelyInside(3.6f, 3.5f, 0f));
            Assert.False(BurstRules.MaybeInside(3.6f, 3.5f, 0f));
        }

        [Fact]
        public void WalkingAllyIsCaughtOnlyAroundTheLandingTime()
        {
            // Ally walks from (0,0) to (20,0) at 2 m/s; burst at (8,0), reach 1.5 m.
            // It passes the point at 4 s: caught for a landing at 4 s, not at 1 s or 9 s.
            var walk = Straight(0f, 0f, 20f, 0f, 2f);
            Assert.True(walk.MayBeInside(8f, 0f, 1.5f, 4f));
            Assert.False(walk.MayBeInside(8f, 0f, 1.5f, 1f));
            Assert.False(walk.MayBeInside(8f, 0f, 1.5f, 9f));
        }

        [Fact]
        public void WalkingAllyThatStopsInsideIsCaught()
        {
            // An ally that walks to an enemy at the landing point and stops there (its walk ends at 9 m).
            Assert.True(Straight(0f, 0f, 9f, 0f, 3f).MayBeInside(10f, 0f, 1.5f, 6f));
        }

        [Fact]
        public void JudgeCancelsForNewAlliesOrAnEmptyLanding()
        {
            Assert.Equal(BurstRules.Verdict.Keep, BurstRules.Judge(true, 0, 0f, 20f, 2));
            Assert.Equal(BurstRules.Verdict.AllyCaught, BurstRules.Judge(true, 1, 4f, 20f, 2));
            Assert.Equal(BurstRules.Verdict.Keep, BurstRules.Judge(false, 1, 4f, 20f, 2));          // worth it without the setting
            Assert.Equal(BurstRules.Verdict.AllyCostsMore, BurstRules.Judge(false, 1, 24f, 20f, 2));
            Assert.Equal(BurstRules.Verdict.NothingInside, BurstRules.Judge(false, 0, 0f, 20f, 0));
            Assert.Equal(BurstRules.Verdict.AllyCaught, BurstRules.Judge(true, 1, 4f, 20f, 0));   // the ally comes first
        }

        [Fact]
        public void CancelsAreLimited()
        {
            Assert.True(BurstRules.MayCancel(0));
            Assert.True(BurstRules.MayCancel(1));
            Assert.False(BurstRules.MayCancel(2));
        }

        [Fact]
        public void RealTimeCastTimeIsALagToo()
        {
            // coverage2 review, finding 1: a delay or a flight lands later in both modes.
            Assert.True(BurstRules.LandsLater(true, false, 0f));
            Assert.True(BurstRules.LandsLater(true, true, 0f));
            // Real time, a 2 s cast picks its victims 2 s later (Incendiary Runes and its fusions).
            float cast = BurstRules.CastSeconds(true, false, false, false, false);
            Assert.Equal(BurstRules.StandardCastSeconds, cast);
            Assert.True(BurstRules.LandsLater(false, true, cast));
            // A wand or an Immediate animation goes off at once; turn-based, nobody moves during the cast.
            Assert.False(BurstRules.LandsLater(false, true, BurstRules.CastSeconds(true, false, true, true, false)));
            Assert.False(BurstRules.LandsLater(false, true, BurstRules.CastSeconds(true, false, false, false, true)));
            Assert.False(BurstRules.LandsLater(false, false, BurstRules.CastSeconds(false, false, false, false, false)));
        }

        [Fact]
        public void AllyCostMatchesTheScoring()
        {
            Assert.Equal(20f, BurstRules.AllyCost(10f, 30f));
            Assert.Equal(12f, BurstRules.AllyCost(10f, 6f));   // at most its hit points, twice
            Assert.Equal(0f, BurstRules.AllyCost(0f, 30f));
        }
    }
}
