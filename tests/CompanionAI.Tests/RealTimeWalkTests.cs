using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.23: real-time walk time (Tactics/ValueModel.Walk.cs).</summary>
    public class RealTimeWalkTests
    {
        [Fact]
        public void NoWalk_NothingLost() => Assert.Equal(1f, ValueModel.RealTimeWalkFactor(0f, 0f), 4);

        [Fact]
        public void WalkingDuringTheCooldown_IsFree() => Assert.Equal(1f, ValueModel.RealTimeWalkFactor(3f, 4f), 4);

        [Fact]
        public void OnlyTheWalkBeyondTheCooldown_Delays() =>
            Assert.Equal(6f / 8f, ValueModel.RealTimeWalkFactor(5f, 3f), 4);

        [Fact]
        public void AWholeRoundOfWalking_HalvesIt() => Assert.Equal(0.5f, ValueModel.RealTimeWalkFactor(6f, 0f), 4);

        [Fact]
        public void LongerWalks_AreWorthLess()
        {
            float near = ValueModel.RealTimeWalkFactor(1f, 0f), far = ValueModel.RealTimeWalkFactor(4.4f, 0f);
            Assert.True(near > far);
            Assert.InRange(far, 0.57f, 0.58f);
        }

        [Fact]
        public void BrokenInput_LosesNothing() => Assert.Equal(1f, ValueModel.RealTimeWalkFactor(float.NaN, 0f), 4);

        // ★ v0.3.27: who can step into a walk (the walk's price and the real-time re-decision share it).
        [Fact]
        public void EnemyBesideTheWay_Intercepts() => Assert.True(ValueModel.CanIntercept(1.5f, 2f, 9f, 3f));

        [Fact]
        public void EnemyThatCanCloseTheGapInHalfTheWalk_Intercepts() =>
            Assert.True(ValueModel.CanIntercept(2f + 9f * 1.5f, 2f, 9f, 3f));

        [Fact]
        public void EnemyTooFarForHalfTheWalk_DoesNot() =>
            Assert.False(ValueModel.CanIntercept(2f + 9f * 1.5f + 0.1f, 2f, 9f, 3f));

        [Fact]
        public void NoWalkLeft_NobodyIntercepts() => Assert.False(ValueModel.CanIntercept(0f, 2f, 9f, 0f));

        [Fact]
        public void BrokenDistance_DoesNotIntercept() => Assert.False(ValueModel.CanIntercept(float.NaN, 2f, 9f, 3f));

        // ★ v0.3.27: who an enemy goes for, how easily it hits, and the combined chance to go down.
        [Fact]
        public void MeleeEnemy_FightsWhoIsInItsReach() =>
            Assert.Equal(0.5f, ValueModel.TargetChance(true, 1.2f, true, 2.4f, 0.6f), 4);

        [Fact]
        public void MeleeEnemy_BusyWithOthers_LeavesTheRestAlone() =>
            Assert.Equal(0f, ValueModel.TargetChance(false, 0.6f, true, 2.4f, 0.6f), 4);

        [Fact]
        public void MeleeEnemy_WithNobodyInReach_GoesForWhoItCanReach() =>
            Assert.Equal(0.25f, ValueModel.TargetChance(false, 0f, true, 2.4f, 0.6f), 4);

        [Fact]
        public void EnemyOutOfRange_GoesForNobody() =>
            Assert.Equal(0f, ValueModel.TargetChance(false, 0f, false, 2.4f, 0.6f), 4);

        [Fact]
        public void TheEasierTarget_IsPickedMoreOften()
        {
            float squishy = ValueModel.HitWeight(8, 14), armoured = ValueModel.HitWeight(8, 22);
            Assert.Equal(0.75f, squishy, 4);
            Assert.Equal(0.35f, armoured, 4);
            Assert.True(ValueModel.TargetChance(true, squishy + armoured, true, 0f, squishy) > 0.68f);
        }

        [Fact]
        public void HitWeight_KeepsTheNaturalOneAndTwenty()
        {
            Assert.Equal(0.95f, ValueModel.HitWeight(30, 10), 4);
            Assert.Equal(0.05f, ValueModel.HitWeight(0, 40), 4);
        }

        [Fact]
        public void CombinedDrop_OfTwoEnemies() =>
            Assert.Equal(1f - (1f - 0.5f * 0.4f) * (1f - 1f * 0.2f), ValueModel.CombinedDrop(new[] { (0.5f, 0.4f), (1f, 0.2f) }), 4);

        [Fact]
        public void CombinedDrop_Nothing_IsZero() =>
            Assert.Equal(0f, ValueModel.CombinedDrop(new (float, float)[0]), 4);

        // ★ v0.3.28: an advance counts what it gives up; a move counts what it costs the rest of the party.
        [Fact]
        public void Advance_GivingUpAnAttack_IsWorthTheDifference() =>
            Assert.Equal((11f - 5.2f) * 0.35f, ValueModel.AdvanceWorth(11f, 5.2f, 0.35f), 4);

        [Fact]
        public void Advance_KeepingTheAttack_IsWorthItAll() =>
            Assert.Equal(11f * 0.35f, ValueModel.AdvanceWorth(11f, 0f, 0.35f), 4);

        [Fact]
        public void Advance_ToAWorseTarget_IsWorthNothing() =>
            Assert.Equal(0f, ValueModel.AdvanceWorth(4f, 5.2f, 0.35f), 4);

        [Fact]
        public void LeavingAlliesExposed_Costs() => Assert.Equal(3.9f, ValueModel.LeavesAlliesCost(-3.9f), 4);

        [Fact]
        public void ShieldingOrNoise_CostsNothing()
        {
            Assert.Equal(0f, ValueModel.LeavesAlliesCost(4f), 4);
            Assert.Equal(0f, ValueModel.LeavesAlliesCost(-0.4f), 4);
            Assert.Equal(0f, ValueModel.LeavesAlliesCost(float.NaN), 4);
        }

        [Fact]
        public void WalkIntoDanger_CostsTheRiseTimesWorth() =>
            Assert.Equal(0.3f * 30f, ValueModel.HealthyWalkExposure(0.05f, 0.35f, 30f, 1f, 0f), 3);

        [Fact]
        public void TankPaysItsTolerance() =>
            Assert.Equal(0.3f * 30f * 0.6f, ValueModel.HealthyWalkExposure(0.05f, 0.35f, 30f, 0.6f, 0f), 3);

        [Fact]
        public void ShieldingAnAlly_CancelsThePrice_NeverPays() =>
            Assert.Equal(0f, ValueModel.HealthyWalkExposure(0.05f, 0.35f, 30f, 1f, 12f), 4);

        [Fact]
        public void ShieldingPartly_LowersThePrice() =>
            Assert.Equal(9f - 4f, ValueModel.HealthyWalkExposure(0.05f, 0.35f, 30f, 1f, 4f), 3);

        [Fact]
        public void NoRealRise_IsFree()
        {
            Assert.Equal(0f, ValueModel.HealthyWalkExposure(0.10f, 0.115f, 30f, 1f, 0f), 4);
            Assert.Equal(0f, ValueModel.HealthyWalkExposure(0.30f, 0.10f, 30f, 1f, 0f), 4);
            Assert.Equal(0f, ValueModel.HealthyWalkExposure(float.NaN, 0.5f, 30f, 1f, 0f), 4);
        }
    }
}
