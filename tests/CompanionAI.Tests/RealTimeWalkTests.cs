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
    }
}
