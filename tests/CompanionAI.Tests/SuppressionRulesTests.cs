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
        public void AnActiveBuffAlwaysMatters()
        {
            Assert.True(SuppressionRules.StillMatters(false, 6f, 600f));
            Assert.True(SuppressionRules.StillMatters(false, float.NaN, float.NaN));
        }

        [Fact]
        public void ASuppressedBuffMattersOnlyIfItOutlastsItsSuppressor()
        {
            Assert.False(SuppressionRules.StillMatters(true, 12f, 60f));    // ends while suppressed
            Assert.False(SuppressionRules.StillMatters(true, 60f, 60f));    // ends with it
            Assert.True(SuppressionRules.StillMatters(true, 120f, 60f));    // comes back for a minute
        }

        [Fact]
        public void PermanentSuppressorsAndBuffs()
        {
            Assert.False(SuppressionRules.StillMatters(true, 600f, float.PositiveInfinity));                   // a feature's suppression
            Assert.False(SuppressionRules.StillMatters(true, float.PositiveInfinity, float.PositiveInfinity));
            Assert.True(SuppressionRules.StillMatters(true, float.PositiveInfinity, 60f));                     // a permanent curse outlasts a hex
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
            Assert.True(SuppressionRules.StillMatters(true, float.NaN, 60f));
            Assert.True(SuppressionRules.StillMatters(true, 60f, float.NaN));   // no suppressor found
        }
    }
}
