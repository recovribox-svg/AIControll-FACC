using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ cast disruption: the concentration rule a hit forces on a caster (docs/BURST_AIM_AND_TACTICS_PLAN.md §4).
    public class DisruptRulesTests
    {
        [Theory]
        // spell level, damage, concentration -> chance the check fails (DC 10 + level + damage / 2; fail below it on a d20)
        [InlineData(3, 10f, 9, 0.40f)]    // DC 18: rolls 1-8 fail
        [InlineData(3, 11.9f, 9, 0.40f)]  // damage 11 -> 11 / 2 = 5, as the rule's integer division
        [InlineData(3, 0f, 9, 0.15f)]     // a hit for 0 still forces DC 13
        [InlineData(9, 60f, 5, 1f)]       // unpassable
        [InlineData(1, 2f, 30, 0f)]       // cannot fail
        public void FailChanceFollowsTheNativeDc(int level, float damage, int concentration, float expected)
        {
            Assert.Equal(expected, DisruptRules.FailChance(level, damage, concentration), 4);
        }

        [Fact]
        public void BadDamageNeverBreaksACast()
        {
            Assert.Equal(0f, DisruptRules.FailChance(3, float.NaN, 0));
            Assert.Equal(0f, DisruptRules.FailChance(3, -1f, 0));
            Assert.Equal(0f, DisruptRules.FailChance(3, float.PositiveInfinity, 0));
        }

        [Fact]
        public void HitsCombineAsIndependentChecks()
        {
            float still = DisruptRules.StillCasting(1f, 0.5f, 0.4f);
            Assert.Equal(0.8f, still, 4);
            still = DisruptRules.StillCasting(still, 0.5f, 0.4f);
            Assert.Equal(0.64f, still, 4);
            Assert.Equal(1f, DisruptRules.StillCasting(1f, 0f, 1f));   // a miss breaks nothing
            Assert.Equal(1f, DisruptRules.StillCasting(1f, 2f, -1f));  // clamped
        }

        [Fact]
        public void OnlyHitsThatLandInTimeCount()
        {
            Assert.True(DisruptRules.InTime(1.0f, 1.3f));
            Assert.False(DisruptRules.InTime(1.1f, 1.3f));   // inside the 0.25 s margin
            Assert.False(DisruptRules.InTime(-1f, 5f));
        }

        [Fact]
        public void WeaponHitsLandAfterLatencyWalkSwingsAndFlight()
        {
            Assert.Equal(0.7f, DisruptRules.WeaponHitSeconds(0, 0f, false, 2f), 4);
            Assert.Equal(1.4f, DisruptRules.WeaponHitSeconds(1, 0f, false, 2f), 4);
            Assert.Equal(1.2f, DisruptRules.WeaponHitSeconds(0, 0f, true, 15f), 4);   // + 15 m / 30 m/s
            Assert.Equal(2.7f, DisruptRules.WeaponHitSeconds(0, 2f, false, 2f), 4);
        }

        [Fact]
        public void AbilityHitsLandAfterTheirOwnCast()
        {
            Assert.Equal(2.1f, DisruptRules.AbilityHitSeconds(0f, 2f, 0f), 4);    // too slow for a 2 s cast
            Assert.Equal(4.6f, DisruptRules.AbilityHitSeconds(0.5f, 2f, 2f), 4);
            Assert.True(DisruptRules.InTime(DisruptRules.AbilityHitSeconds(0f, 2f, 0.3f), 5.5f));   // a full-round cast (6 s)
        }

        [Fact]
        public void StakesUseTheSpellElseTheRound()
        {
            Assert.Equal(18f, DisruptRules.Stakes(18f, 6f));
            Assert.Equal(6f, DisruptRules.Stakes(0f, 6f));
            Assert.Equal(0f, DisruptRules.Stakes(0f, -2f));
        }

        [Fact]
        public void ValueIsTheBreakChanceTimesTheStakes()
        {
            Assert.Equal(4f, DisruptRules.Value(0.8f, 20f), 4);
            Assert.Equal(0f, DisruptRules.Value(1f, 20f));
            Assert.Equal(0f, DisruptRules.Value(0.5f, -3f));
        }
    }
}
