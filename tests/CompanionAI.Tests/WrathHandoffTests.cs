using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.21: Wrath Tactics goes first in real time (Tactics/WrathHandoff.cs).</summary>
    public class WrathHandoffTests
    {
        [Fact]
        public void FirstLookAtAnIdleUnit_AsksWrathTactics() =>
            Assert.Equal(WrathHandoff.Step.Ask, WrathHandoff.Next(new WrathHandoff.State(), 10f, 5));

        [Fact]
        public void Asked_WaitsUntilWrathTacticsEvaluated()
        {
            var s = new WrathHandoff.State();
            WrathHandoff.MarkAsked(s, 10f, 5);
            Assert.Equal(WrathHandoff.Step.Wait, WrathHandoff.Next(s, 10.1f, 5));
            Assert.Equal(WrathHandoff.Step.Decide, WrathHandoff.Next(s, 10.2f, 6));
        }

        [Fact]
        public void NoAnswerInTime_AIControlDecides()
        {
            var s = new WrathHandoff.State();
            WrathHandoff.MarkAsked(s, 10f, 5);
            Assert.Equal(WrathHandoff.Step.Wait, WrathHandoff.Next(s, 10.5f, 5));
            Assert.Equal(WrathHandoff.Step.Decide, WrathHandoff.Next(s, 10f + WrathHandoff.TimeoutSeconds, 5));
        }

        [Fact]
        public void AnsweredOnce_DecidesWithoutAskingAgain_InTheSameIdleStretch()
        {
            var s = new WrathHandoff.State();
            WrathHandoff.MarkAsked(s, 10f, 5);
            WrathHandoff.Next(s, 10.1f, 6);
            Assert.Equal(WrathHandoff.Step.Decide, WrathHandoff.Next(s, 15f, 6));
        }

        [Fact]
        public void AfterACommand_TheNextIdleStretchAsksAgain()
        {
            var s = new WrathHandoff.State();
            WrathHandoff.MarkAsked(s, 10f, 5);
            WrathHandoff.Next(s, 10.1f, 6);
            WrathHandoff.Reset(s);
            Assert.Equal(WrathHandoff.Step.Ask, WrathHandoff.Next(s, 20f, 9));
        }
    }
}
