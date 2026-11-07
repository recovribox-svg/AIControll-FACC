using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.29 test 7: a closed door on the way to an enemy (Tactics/DoorRules.cs).</summary>
    public class DoorRulesTests
    {
        [Fact]
        public void ADoorBetween_IsOnTheWay() => Assert.True(DoorRules.OnTheWay(0f, 0f, 10f, 0f, 6f, 1f));

        [Fact]
        public void ADoorJustBeyondTheEnemy_IsOnTheWay() => Assert.True(DoorRules.OnTheWay(0f, 0f, 10f, 0f, 11f, 0f));

        [Fact]
        public void ADoorBehindTheUnit_IsNot() => Assert.False(DoorRules.OnTheWay(0f, 0f, 10f, 0f, -2f, 0f));

        [Fact]
        public void ADoorFarToTheSide_IsNot() => Assert.False(DoorRules.OnTheWay(0f, 0f, 10f, 0f, 5f, 4f));

        [Fact]
        public void ADoorFarBeyondTheEnemy_IsNot() => Assert.False(DoorRules.OnTheWay(0f, 0f, 10f, 0f, 14f, 0f));

        [Fact]
        public void NoDistanceOrBrokenInput_IsNot()
        {
            Assert.False(DoorRules.OnTheWay(0f, 0f, 0.1f, 0f, 0.05f, 0f));
            Assert.False(DoorRules.OnTheWay(0f, 0f, float.NaN, 0f, 5f, 0f));
        }
    }
}
