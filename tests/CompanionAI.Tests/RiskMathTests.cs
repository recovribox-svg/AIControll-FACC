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
        public void RoundDropChance_IsTheEngineModel()
        {
            Assert.Equal(0f, RiskMath.RoundDropChance(10f, 0f), 3);
            Assert.Equal((float)System.Math.Exp(-2.0), RiskMath.RoundDropChance(20f, 10f), 3);
            Assert.True(RiskMath.RoundDropChance(5f, 10f) > RiskMath.RoundDropChance(20f, 10f));
        }
    }
}
