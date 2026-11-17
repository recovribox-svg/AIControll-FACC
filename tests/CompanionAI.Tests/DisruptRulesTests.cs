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
            float chained = DisruptRules.NextSwingSeconds;
            Assert.Equal(0.7f, DisruptRules.WeaponHitSeconds(0, 0f, false, 2f, chained), 4);
            Assert.Equal(1.4f, DisruptRules.WeaponHitSeconds(1, 0f, false, 2f, chained), 4);
            Assert.Equal(1.2f, DisruptRules.WeaponHitSeconds(0, 0f, true, 15f, chained), 4);   // + 15 m / 30 m/s
            Assert.Equal(2.7f, DisruptRules.WeaponHitSeconds(0, 2f, false, 2f, chained), 4);
        }

        [Theory]
        // attacks in the routine, chained (bow / animation sequence) -> seconds between two attacks (UnitAttack.OnTick)
        [InlineData(1, false, 0.7f)]
        [InlineData(2, false, 3f)]
        [InlineData(3, false, 2f)]
        [InlineData(4, false, 1.5f)]
        [InlineData(6, false, 1f)]
        [InlineData(3, true, 0.7f)]
        public void AttacksFollowTheNativeSchedule(int attacks, bool chained, float spacing)
        {
            Assert.Equal(spacing, DisruptRules.SwingSpacing(attacks, chained), 4);
        }

        [Fact]
        public void AFullAttacksSecondHitUsuallyMissesAShortCast()
        {
            // Three iterative attacks: the second starts 2 s in, too late for a standard 2 s cast. A fixed 0.7 s swing said in time.
            float spacing = DisruptRules.SwingSpacing(3, false);
            Assert.False(DisruptRules.InTime(DisruptRules.WeaponHitSeconds(1, 0f, false, 2f, spacing), 2f));
            Assert.True(DisruptRules.InTime(DisruptRules.WeaponHitSeconds(1, 0f, false, 2f, DisruptRules.NextSwingSeconds), 2f));
            Assert.True(DisruptRules.InTime(DisruptRules.WeaponHitSeconds(0, 0f, false, 2f, spacing), 2f));   // the first still counts
        }

        [Fact]
        public void AnIssuedIgnoreCooldownRoutineUsesItsConfiguredWindow()
        {
            // Native ConfigureAnimations may store a 3 s window. Treating it as 6 s misses an allied hit and overprices
            // this unit's response. The production reader is checked on a native command layout in ProfileAudit.
            float configured = DisruptRules.SwingSpacing(3, false, 3f);
            Assert.Equal(1f, configured);
            float timely = DisruptRules.RunningHitSeconds(1, 1, configured, 0.2f, false, false, 0f);
            float late = DisruptRules.RunningHitSeconds(1, 1, DisruptRules.SwingSpacing(3, false), 0.2f, false, false, 0f);
            Assert.True(DisruptRules.InTime(timely, 2f));
            Assert.False(DisruptRules.InTime(late, 2f));
        }

        [Fact]
        public void AnOrderedAttackWaitsForReadinessWhileApproaching()
        {
            // Standing beside the caster is not enough when this ally's standard action is still cooling down.
            float wait = DisruptRules.OrderedStartSeconds(0f, 3f);
            Assert.False(DisruptRules.InTime(DisruptRules.WeaponHitSeconds(0, wait, false, 0f, 2f), 2f));
            // Movement and cooldown overlap, rather than being added and incorrectly hiding a timely allied hit.
            Assert.Equal(2f, DisruptRules.OrderedStartSeconds(2f, 1f));
            Assert.Equal(2f, DisruptRules.OrderedStartSeconds(1f, 2f));
            Assert.True(DisruptRules.InTime(DisruptRules.WeaponHitSeconds(0, 2f, false, 0f, 2f), 3f));
        }

        [Fact]
        public void AnAllysRunningAttackLandsOnItsSchedule()
        {
            // 3-attack routine, 1 s into the command, next attack index 1: it starts at 2 s, lands 0.6 s later
            Assert.Equal(1.6f, DisruptRules.RunningHitSeconds(1, 1, 2f, 1f, false, false, 2f), 4);
            Assert.Equal(0f, DisruptRules.RunningHitSeconds(0, 0, 2f, 1f, false, false, 2f), 4);          // under way: lands now
            Assert.Equal(1.3f, DisruptRules.RunningHitSeconds(2, 1, 0.7f, 5f, true, false, 2f), 4);        // chained: one after the next
            Assert.Equal(1.1f, DisruptRules.RunningHitSeconds(0, 0, 0.7f, 0f, true, true, 15f), 4);        // + 15 m / 30 m/s flight
        }

        [Fact]
        public void APendingAlliedHitLeavesOnlyTheMarginalBreak()
        {
            // no other hits ordered: the full value
            Assert.Equal(DisruptRules.Value(0.5f, 100f), DisruptRules.MarginalValue(0.5f, 1f, 100f), 4);
            // an ally's hit already breaks it half the time: this one adds 25, so both together are worth 75, not 100
            Assert.Equal(25f, DisruptRules.MarginalValue(0.5f, 0.5f, 100f), 4);
            Assert.Equal(75f, DisruptRules.Value(0.5f, 100f) + DisruptRules.MarginalValue(0.5f, 0.5f, 100f), 4);
            // the others surely break it: nothing to add
            Assert.Equal(0f, DisruptRules.MarginalValue(0.2f, 0f, 100f), 4);
            Assert.Equal(0f, DisruptRules.MarginalValue(float.NaN, 0.5f, 100f), 4);
        }

        [Fact]
        public void ALateIterativesLowerChanceLeavesMoreWorkForTheNextAlly()
        {
            // A near-certain concentration failure on hit. Reusing an earlier 90% attack for a pending 40% iterative
            // would suppress this ally's useful response: 5 points instead of 30 on a stakes-100 cast.
            float late = DisruptRules.StillCasting(1f, 0.4f, 1f);
            float earlier = DisruptRules.StillCasting(1f, 0.9f, 1f);
            Assert.Equal(30f, DisruptRules.MarginalValue(0.5f, late, 100f), 4);
            Assert.Equal(5f, DisruptRules.MarginalValue(0.5f, earlier, 100f), 4);
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

        [Fact]
        public void ModeWithLessPhysicalDamageCanBeBetterAtBreakingTheCast()
        {
            // A level-2 cast with +14 concentration cannot fail against a 5-damage hit (DC 14).
            // An 11-damage hit forces DC 17: rolls 1-2 fail. The less accurate mode can therefore win.
            float ordinary = 0.9f * 5f;
            float power = 0.4f * 11f;
            Assert.True(ordinary > power);
            float before = DisruptRules.Value(DisruptRules.StillCasting(1f, 0.9f, DisruptRules.FailChance(2, 5f, 14)), 100f);
            float after = DisruptRules.Value(DisruptRules.StillCasting(1f, 0.4f, DisruptRules.FailChance(2, 11f, 14)), 100f);
            Assert.Equal(0f, before);
            Assert.Equal(4f, after, 4);
            Assert.True(power + after > ordinary + before);
        }
    }
}
