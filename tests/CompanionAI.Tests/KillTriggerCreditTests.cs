using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.23 (Carnage task): what a kill-fired area effect (Dreadful Carnage's Dazzling Display) adds to the
    /// attack that may make the kill (Tactics/ValueModel.OnKill.cs).</summary>
    public class KillTriggerCreditTests
    {
        private const float Radius = 9.14f;   // 30 ft

        [Fact]
        public void LikelyKill_CreditsTheSurvivorsInRange()
        {
            // target (index 0) and two survivors within 30 ft
            float credit = ValueModel.KillTriggerCredit(0.8f, new[] { 1f, 3f, 6f }, new[] { 2f, 1.5f, 1f }, 0, Radius);
            Assert.Equal(0.8f * 2.5f, credit, 4);
        }

        [Fact]
        public void TheKilledTarget_GetsNothing() =>
            Assert.Equal(0f, ValueModel.KillTriggerCredit(1f, new[] { 1f }, new[] { 5f }, 0, Radius));

        [Fact]
        public void UnlikelyKill_SmallCredit() =>
            Assert.Equal(0.05f * 2f, ValueModel.KillTriggerCredit(0.05f, new[] { 1f, 2f }, new[] { 9f, 2f }, 0, Radius), 4);

        [Fact]
        public void NoSurvivorInRange_Nothing() =>
            Assert.Equal(0f, ValueModel.KillTriggerCredit(0.9f, new[] { 1f, 12f, 20f }, new[] { 3f, 3f, 3f }, 0, Radius));

        [Fact]
        public void NoKillChance_Nothing() =>
            Assert.Equal(0f, ValueModel.KillTriggerCredit(0f, new[] { 1f, 2f }, new[] { 3f, 3f }, 0, Radius));

        [Fact]
        public void EachRecipientCountsOnce_ChanceNeverAboveOne()
        {
            // a full attack's several swings do not multiply it: the chance is one kill's, capped at 1
            float credit = ValueModel.KillTriggerCredit(3f, new[] { 1f, 2f }, new[] { 4f, 2f }, 0, Radius);
            Assert.Equal(2f, credit, 4);
        }

        [Fact]
        public void AlreadyShakenOrImmune_CountNothing() =>
            Assert.Equal(0.5f * 1f, ValueModel.KillTriggerCredit(0.5f, new[] { 1f, 2f, 3f }, new[] { 4f, 0f, 1f }, 0, Radius), 4);

        [Fact]
        public void TargetNotAmongRecipients_AllCount() =>
            Assert.Equal(0.5f * 3f, ValueModel.KillTriggerCredit(0.5f, new[] { 2f, 3f }, new[] { 1f, 2f }, -1, Radius), 4);
    }
}
