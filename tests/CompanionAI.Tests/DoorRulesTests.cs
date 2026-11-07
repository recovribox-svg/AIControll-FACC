using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.29 test 7/8: a closed door on the way to an enemy (Tactics/DoorRules.cs).</summary>
    public class DoorRulesTests
    {
        private static bool OnTheWay(float doorX, float doorZ) => DoorRules.OnTheWay(0f, 0f, 10f, 0f, doorX, doorZ, out _);

        [Fact]
        public void ADoorOnTheLine_IsNoDetour()
        {
            Assert.True(DoorRules.OnTheWay(0f, 0f, 10f, 0f, 6f, 0f, out float detour));
            Assert.Equal(0f, detour, 3);
        }

        [Fact]
        public void ADoorBetween_IsOnTheWay() => Assert.True(OnTheWay(6f, 1f));

        [Fact]
        public void ADoorJustBeyondTheEnemy_IsOnTheWay() => Assert.True(OnTheWay(11f, 0f));

        /// <summary>The test-7 corridor (3 m) refused this one: a door in the wall of the next room, 4 m off the line.</summary>
        [Fact]
        public void ADoorInTheNextRoomsWall_IsOnTheWay() => Assert.True(OnTheWay(5f, 4f));

        [Fact]
        public void ADoorBehindTheUnit_IsNot() => Assert.False(OnTheWay(-3f, 0f));

        [Fact]
        public void ADoorFarToTheSide_IsNot() => Assert.False(OnTheWay(5f, 8f));

        [Fact]
        public void ADoorFarBeyondTheEnemy_IsNot() => Assert.False(OnTheWay(14f, 0f));

        [Fact]
        public void AFarEnemy_AllowsALongerDetour()
        {
            Assert.Equal(DoorRules.MinDetour, DoorRules.MaxDetour(4f));
            Assert.Equal(15f, DoorRules.MaxDetour(30f), 3);
            // 30 m away, a door 10 m off the line half-way: 2 * sqrt(15^2 + 10^2) - 30 = 6.06 m longer.
            Assert.True(DoorRules.OnTheWay(0f, 0f, 30f, 0f, 15f, 10f, out _));
        }

        [Fact]
        public void TheNearerDoor_HasTheShorterDetour()
        {
            float near = DoorRules.Detour(0f, 0f, 10f, 0f, 5f, 1f);
            float far = DoorRules.Detour(0f, 0f, 10f, 0f, 5f, 3f);
            Assert.True(near < far);
        }

        [Fact]
        public void NoDistanceOrBrokenInput_IsNot()
        {
            Assert.False(DoorRules.OnTheWay(0f, 0f, 0.1f, 0f, 0.05f, 0f, out _));
            Assert.False(DoorRules.OnTheWay(0f, 0f, float.NaN, 0f, 5f, 0f, out _));
            Assert.False(DoorRules.OnTheWay(0f, 0f, 10f, 0f, float.PositiveInfinity, 0f, out _));
            Assert.True(float.IsNaN(DoorRules.Detour(0f, 0f, 0f, 0f, 1f, 1f)));
        }
    }
}
