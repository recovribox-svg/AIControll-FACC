using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ suppressed buffs: the native UnitPartBuffSuppress rule (docs/BURST_AIM_AND_TACTICS_PLAN.md §5, slice C).
    public class SuppressionRulesTests
    {
        [Theory]
        // excluded, listed, descriptor, school -> suppressed
        [InlineData(false, false, false, false, false)]   // nothing suppresses it
        [InlineData(false, true, false, false, true)]     // its blueprint is listed (a form's Enlarge Person)
        [InlineData(false, false, true, false, true)]     // one of its descriptor flags (an Ameliorating hex's Sickened)
        [InlineData(false, false, false, true, true)]     // its school
        [InlineData(true, true, true, true, false)]       // the exclusion list wins over all three
        [InlineData(true, false, false, false, false)]
        public void FollowsTheNativeOrder(bool excluded, bool listed, bool descriptor, bool school, bool expected)
        {
            Assert.Equal(expected, SuppressionRules.Suppressed(excluded, listed, descriptor, school));
        }

        [Fact]
        public void AnActiveBuffKeepsAllItsWorth()
        {
            Assert.Equal(1f, SuppressionRules.ActiveShare(false, 1f, 600f, 3f));
            Assert.Equal(1f, SuppressionRules.ActiveShare(false, float.NaN, float.NaN, 3f));
        }

        [Theory]
        // rounds left, suppressed rounds, horizon -> share of the cleanse's worth
        [InlineData(2f, 10f, 3f, 0f)]                 // ends while suppressed
        [InlineData(10f, 10f, 3f, 0f)]                // ends with it
        [InlineData(20f, 10f, 30f, 0.5f)]             // comes back for half its time (a long fight)
        [InlineData(20f, 10f, 3f, 0f)]                // ... but not within a three-round horizon
        [InlineData(10.1667f, 10f, 3f, 0f)]           // Codex's C-R1 example: 61 s left under 60 s, an 18 s horizon
        [InlineData(10f, 1f, 3f, 2f / 3f)]            // suppressed for the first round of three
        [InlineData(2f, 1f, 3f, 0.5f)]                // one of its two rounds left
        [InlineData(float.PositiveInfinity, 2f, 3f, 1f / 3f)]   // a permanent curse under a hex with two rounds left
        public void ASuppressedBuffCountsOnlyTheRoundsItActsWithinTheHorizon(float left, float suppressed, float horizon, float expected)
        {
            Assert.Equal(expected, SuppressionRules.ActiveShare(true, left, suppressed, horizon), 4);
        }

        [Fact]
        public void PermanentSuppressorsAndBuffs()
        {
            Assert.Equal(0f, SuppressionRules.ActiveShare(true, 100f, float.PositiveInfinity, 3f));                  // a feature's suppression
            Assert.Equal(0f, SuppressionRules.ActiveShare(true, float.PositiveInfinity, float.PositiveInfinity, 3f));
            Assert.Equal(0f, SuppressionRules.ActiveShare(true, 0f, 1f, 3f));                                        // nothing left
        }

        [Theory]
        // rounds left, suppressed rounds, horizon -> rounds it acts
        [InlineData(10f, 0f, 6f, 6f)]       // not suppressed: the horizon
        [InlineData(10f, 4f, 6f, 2f)]       // comes back after 4 rounds
        [InlineData(3f, 4f, 6f, 0f)]        // ends while suppressed
        [InlineData(10f, 8f, 6f, 0f)]       // suppressed past the horizon
        [InlineData(6f, float.PositiveInfinity, 6f, 0f)]   // a permanent suppressor
        [InlineData(6f, float.NaN, 6f, 6f)] // no suppressor found: as before
        [InlineData(0f, 0f, 6f, 0f)]
        public void ActiveRoundsSubtractTheSuppressedOnes(float left, float suppressed, float horizon, float expected)
        {
            Assert.Equal(expected, SuppressionRules.ActiveRounds(left, suppressed, horizon), 4);
        }

        [Fact]
        public void UnknownTimesKeepTheOldValuation()
        {
            Assert.Equal(1f, SuppressionRules.ActiveShare(true, float.NaN, 10f, 3f));
            Assert.Equal(1f, SuppressionRules.ActiveShare(true, 10f, float.NaN, 3f));   // no suppressor found
        }

        [Theory]
        [InlineData(0f, 3f)]
        [InlineData(-1f, 3f)]
        [InlineData(5f, 0f)]
        public void UnknownSuppressionCannotReviveAnEndedBuffOrHorizon(float left, float horizon)
        {
            Assert.Equal(0f, SuppressionRules.ActiveShare(true, left, float.NaN, horizon));
        }
    }
}
