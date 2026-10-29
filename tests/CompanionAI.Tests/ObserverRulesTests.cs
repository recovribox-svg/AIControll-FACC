using CompanionAI_Pathfinder.Tactics;
using Xunit;
using Step = CompanionAI_Pathfinder.Tactics.ObserverRules.SettleStep;

namespace CompanionAI.Tests
{
    /// <summary>Recommendation A: the observers' learning gates from PR #14 (Tactics/ObserverRules.cs).</summary>
    public class ObserverRulesTests
    {
        // Settle(commandPresent, commandFinished, executed, commandActed, evidence)

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, true, true)]
        [InlineData(false, true, false)]
        public void RunningCommand_IsJudgedLater(bool executed, bool acted, bool evidence) =>
            Assert.Equal(Step.Wait, ObserverRules.Settle(true, false, executed, acted, evidence));

        [Fact]
        public void InterruptedBeforeCasting_IsNotCounted() =>
            Assert.Equal(Step.NotCast, ObserverRules.Settle(true, true, false, false, false));

        [Fact]
        public void OldGreaseEvidence_DoesNotTurnAnUncastOrderIntoACast() =>
            // v0.3.22 Log2: an approaching order interrupted while an old Grease ticked on its target.
            Assert.Equal(Step.NotCast, ObserverRules.Settle(true, true, false, false, true));

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void ExecutedOrActedCast_WithEvidence_Worked(bool executed, bool acted) =>
            Assert.Equal(Step.Worked, ObserverRules.Settle(true, true, executed, acted, true));

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void ExecutedOrActedCast_WithoutEvidence_IsInspected(bool executed, bool acted) =>
            // A genuine silent failure stays possible: the touch-charge and remaining checks still run.
            Assert.Equal(Step.Inspect, ObserverRules.Settle(true, true, executed, acted, false));

        [Theory]
        [InlineData(false, true, Step.Worked)]
        [InlineData(false, false, Step.Inspect)]
        [InlineData(true, true, Step.Worked)]
        [InlineData(true, false, Step.Inspect)]
        public void CastWithoutCommandObject_IsNeverDiscardedAsUncast(bool executed, bool evidence, Step expected)
        {
            // The game already cleared the command: "finished" and "acted" carry no information then.
            Assert.Equal(expected, ObserverRules.Settle(false, true, executed, false, evidence));
            Assert.Equal(expected, ObserverRules.Settle(false, false, executed, false, evidence));
        }

        [Fact]
        public void EveryFinishedUncastUnactedCommand_IsNotCast_WhateverTheEvidence()
        {
            foreach (bool evidence in new[] { false, true })
                Assert.Equal(Step.NotCast, ObserverRules.Settle(true, true, false, false, evidence));
        }

        [Fact]
        public void MissedAttackRoll_IsNoCalibrationSample() =>
            // Holy Water's splash keeps the missed roll.
            Assert.True(ObserverRules.SkipCalibrationSample(true, false));

        [Fact]
        public void HitAttackRoll_IsASample() => Assert.False(ObserverRules.SkipCalibrationSample(true, true));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PacketWithoutAttackRoll_IsASample(bool hitFlag) =>
            Assert.False(ObserverRules.SkipCalibrationSample(false, hitFlag));
    }
}
