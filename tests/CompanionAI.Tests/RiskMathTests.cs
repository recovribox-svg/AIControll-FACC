using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.14 (test5): chance attacks drop a unit (Tactics/RiskMath.cs).</summary>
    public class RiskMathTests
    {
        [Fact]
        public void OneHit_DamageSpread()
        {
            Assert.Equal(1f, RiskMath.ChanceAtLeast(10f, 5f), 3);    // even a low roll (5) drops 5 HP
            Assert.Equal(0f, RiskMath.ChanceAtLeast(10f, 15f), 3);   // only the very top roll reaches 15 HP
            Assert.Equal(0.5f, RiskMath.ChanceAtLeast(10f, 10f), 3); // average damage against equal HP: half the time
            Assert.Equal(0f, RiskMath.ChanceAtLeast(0f, 1f), 3);
            Assert.Equal(1f, RiskMath.ChanceAtLeast(5f, 0f), 3);
        }

        [Fact]
        public void BigHitterAgainstMediumHp_IsARealRisk()
        {
            // Large Water Elemental-like slam: 80 % to hit, 16 per hit, x2 crits on 5 % of rolls; character with 18 HP.
            float drop = RiskMath.AttackDropChance(0.8f, 0.04f, 16f, 2f, 18f);
            Assert.InRange(drop, 0.25f, 0.45f);
            // The same blow against 40 HP only drops on a critical hit.
            float healthy = RiskMath.AttackDropChance(0.8f, 0.04f, 16f, 2f, 40f);
            Assert.InRange(healthy, 0.0f, 0.04f);
        }

        [Fact]
        public void CritChanceNeverExceedsHitChance()
        {
            float a = RiskMath.AttackDropChance(0.3f, 0.9f, 10f, 3f, 25f);
            Assert.InRange(a, 0f, 0.3f);
        }

        [Fact]
        public void SeveralAttacks_Combine()
        {
            Assert.Equal(0.75f, RiskMath.AnyDrops(0.5f, 0.5f), 3);
            Assert.Equal(0f, RiskMath.AnyDrops(), 3);
        }

        [Fact]
        public void ExpectedTakenIfStillUp_IsTheConditionalMean()
        {
            Assert.Equal(0f, RiskMath.ExpectedTakenIfStillUp(10f, 0f), 3);
            // danger 10, hp 10: 10 - 10·e^-1/(1-e^-1) = 4.180
            Assert.Equal(4.180f, RiskMath.ExpectedTakenIfStillUp(10f, 10f), 2);
            // Never more than the HP a survivor had, never less than nothing; small danger: close to the mean itself.
            Assert.InRange(RiskMath.ExpectedTakenIfStillUp(100f, 5f), 4.99f, 5.01f);
            Assert.InRange(RiskMath.ExpectedTakenIfStillUp(2f, 50f), 0f, 2f);
        }

        [Fact]
        public void RoundDropChance_IsTheEngineModel()
        {
            Assert.Equal(0f, RiskMath.RoundDropChance(10f, 0f), 3);
            Assert.Equal((float)System.Math.Exp(-2.0), RiskMath.RoundDropChance(20f, 10f), 3);
            Assert.True(RiskMath.RoundDropChance(5f, 10f) > RiskMath.RoundDropChance(20f, 10f));
        }
    
        // ★ v0.3.29 test 2 (R3): a full attack's chance to drop a unit from its real hits.
        [Fact]
        public void FullAttackDropChance_OneAttackIsTheSingleAttackRule()
        {
            float one = RiskMath.FullAttackDropChance(1, 0.7f, 0.05f, 14f, 2f, 17f);
            Assert.Equal(RiskMath.AttackDropChance(0.7f, 0.05f, 14f, 2f, 17f), one, 4);
        }

        [Fact]
        public void FullAttackDropChance_TheEarthElementalsSlamOnSeelah()
        {
            // 14 a slam at 70 %, crit x2 at 5 %, 17 HP: about 0.65 x 0.29 + 0.05 = 0.24.
            Assert.InRange(RiskMath.FullAttackDropChance(1, 0.7f, 0.05f, 14f, 2f, 17f), 0.22f, 0.26f);
            // The same slam once a mongrel has taken 5: about 0.65 x 0.64 + 0.05 = 0.47.
            Assert.InRange(RiskMath.FullAttackDropChance(1, 0.7f, 0.05f, 14f, 2f, 12f), 0.44f, 0.50f);
        }

        [Fact]
        public void FullAttackDropChance_MoreAttacksMoreChance()
        {
            float two = RiskMath.FullAttackDropChance(2, 0.6f, 0.03f, 9f, 2f, 15f);
            float three = RiskMath.FullAttackDropChance(3, 0.6f, 0.03f, 9f, 2f, 15f);
            Assert.True(three > two);
            Assert.True(two > RiskMath.FullAttackDropChance(1, 0.6f, 0.03f, 9f, 2f, 15f));
        }

        [Fact]
        public void FullAttackDropChance_ScytheCritOnAFullHpFighter()
        {
            // x4 scythe, 16 a hit, 5 % crits, 25 HP: only the crit drops her.
            float p = RiskMath.FullAttackDropChance(1, 0.6f, 0.03f, 16f, 4f, 25f);
            Assert.InRange(p, 0.03f, 0.04f);
        }

        [Fact]
        public void FullAttackDropChance_Bounds()
        {
            Assert.Equal(1f, RiskMath.FullAttackDropChance(1, 0.5f, 0f, 5f, 2f, 0f), 4);
            Assert.Equal(0f, RiskMath.FullAttackDropChance(0, 0.5f, 0f, 5f, 2f, 10f), 4);
            Assert.Equal(0f, RiskMath.FullAttackDropChance(3, 0f, 0f, 5f, 2f, 10f), 4);
            Assert.Equal(0f, RiskMath.FullAttackDropChance(2, 0.9f, 0.1f, 2f, 2f, 100f), 4);   // never enough
            Assert.InRange(RiskMath.FullAttackDropChance(40, 0.9f, 0.1f, 9f, 2f, 10f), 0.9f, 1f);
        }
}
}
