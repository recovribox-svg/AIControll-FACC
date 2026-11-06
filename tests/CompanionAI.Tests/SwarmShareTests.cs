using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.28 test 15: how much of a swarm's round reaches a spot (Tactics/ValueModel.Swarm.cs).</summary>
    public class SwarmShareTests
    {
        [Fact]
        public void Struck_AllOfIt() => Assert.Equal(1f, ValueModel.SwarmShare(struck: true, reachable: true), 4);

        [Fact]
        public void Struck_EvenWhereItCouldNotWalk() => Assert.Equal(1f, ValueModel.SwarmShare(true, false), 4);

        [Fact]
        public void OutsideItsSpace_WithinItsWalk_Half() =>
            Assert.Equal(ValueModel.SwarmWalkShare, ValueModel.SwarmShare(false, true), 4);

        [Fact]
        public void OutOfItsWalk_Nothing() => Assert.Equal(0f, ValueModel.SwarmShare(false, false), 4);

        [Fact]
        public void StepOut_TakesHalfTheRoundOff() =>
            // Inside the Mandragora swarm against a step out of its space: the danger there is half of what it is inside.
            Assert.True(ValueModel.SwarmShare(false, true) < ValueModel.SwarmShare(true, true));

        [Fact]
        public void VictimFactor_ByTheRoundsWorthAgainstTheMean()
        {
            Assert.Equal(1f, ValueModel.SwarmVictimFactor(20f, 20f), 4);
            Assert.Equal(0.5f, ValueModel.SwarmVictimFactor(10f, 20f), 4);   // a good Fortitude save, immune to its poison
            Assert.Equal(1.5f, ValueModel.SwarmVictimFactor(30f, 20f), 4);
        }

        [Fact]
        public void VictimFactor_CappedAndSafe()
        {
            Assert.Equal(3f, ValueModel.SwarmVictimFactor(100f, 10f), 4);
            Assert.Equal(0f, ValueModel.SwarmVictimFactor(-5f, 10f), 4);
            Assert.Equal(1f, ValueModel.SwarmVictimFactor(10f, 0f), 4);
            Assert.Equal(1f, ValueModel.SwarmVictimFactor(float.NaN, 10f), 4);
            Assert.Equal(1f, ValueModel.SwarmVictimFactor(10f, float.PositiveInfinity), 4);
        }
    }
}
