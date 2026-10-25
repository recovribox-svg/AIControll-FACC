using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.14: which options a unit's statuses let the game start (Tactics/ActionRules.cs).</summary>
    public class ActionRulesTests
    {
        private static string Check(ActionPermissions p, bool plainMove, bool walks, bool mainAction) =>
            ActionRules.Refusal(p, plainMove, walks, mainAction);

        [Theory]
        [InlineData(true, true, false)]   // walk
        [InlineData(false, false, true)]  // attack / spell from here
        [InlineData(false, true, true)]   // walk then attack
        [InlineData(false, false, false)] // swift buff
        public void HealthyUnit_EverythingStarts(bool plainMove, bool walks, bool mainAction)
        {
            Assert.Null(Check(new ActionPermissions(), plainMove, walks, mainAction));
            Assert.Null(Check(null, plainMove, walks, mainAction));
        }

        [Fact]
        public void Nauseated_OnlyPlainMovement()
        {
            var p = new ActionPermissions { MovementOnly = true };
            Assert.Null(Check(p, plainMove: true, walks: true, mainAction: false));
            Assert.NotNull(Check(p, plainMove: false, walks: false, mainAction: false));  // swift ability, toggle
            Assert.NotNull(Check(p, plainMove: false, walks: false, mainAction: true));   // attack from here
            Assert.NotNull(Check(p, plainMove: false, walks: true, mainAction: true));    // walk then attack
        }

        [Fact]
        public void NauseatedAndHeld_Nothing()
        {
            var p = new ActionPermissions { MovementOnly = true, CanWalk = false };
            Assert.NotNull(Check(p, plainMove: true, walks: true, mainAction: false));
            Assert.NotNull(Check(p, plainMove: false, walks: false, mainAction: true));
        }

        [Fact]
        public void Staggered_EitherWalkOrAct()
        {
            var p = new ActionPermissions { OneActionOnly = true };
            Assert.Null(Check(p, plainMove: true, walks: true, mainAction: false));
            Assert.Null(Check(p, plainMove: false, walks: false, mainAction: true));
            Assert.Null(Check(p, plainMove: false, walks: false, mainAction: false));
            Assert.NotNull(Check(p, plainMove: false, walks: true, mainAction: true));
        }

        [Fact]
        public void CannotWalk_ActsInPlace()
        {
            var p = new ActionPermissions { CanWalk = false };
            Assert.NotNull(Check(p, plainMove: true, walks: true, mainAction: false));
            Assert.NotNull(Check(p, plainMove: false, walks: true, mainAction: true));
            Assert.Null(Check(p, plainMove: false, walks: false, mainAction: true));
            Assert.Null(Check(p, plainMove: false, walks: false, mainAction: false));
        }
    }
}
