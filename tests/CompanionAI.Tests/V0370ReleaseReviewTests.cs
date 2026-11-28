using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.70 release review: the hold kept while the holder has no action to regain; a badly hurt holder kept
    /// where a withdrawal would help; helpless melee targets.</summary>
    public class V0370ReleaseReviewTests
    {
        [Fact]
        public void KeepingCostsNothingThisRoundWithoutAnActionToRegain()
        {
            // The old form charges the holder's turn now.
            Assert.Equal(ValueModel.GrappleKeepValue(0.46f, 3f, 29.9f, 28f, 14.3f, 0.5f, 1f),
                         ValueModel.GrappleKeepValue(0.46f, 3f, 29.9f, 28f, 14.3f, 0.5f, 1f, true), 4);
            // Wenduag's Bulette: let go at once with her turn charged...
            Assert.True(ValueModel.GrappleKeepValue(0.46f, 3f, 29.9f, 28f, 14.3f, 0.5f, 1f, true) < -0.5f);
            // ...kept while her attack has spent her actions: the victim's held turn still comes, at no cost to her.
            float kept = ValueModel.GrappleKeepValue(0.46f, 3f, 29.9f, 28f, 14.3f, 0.5f, 1f, false);
            Assert.True(kept > 0f);
            Assert.Equal(29.9f * 0.5f + System.Math.Max(0f, ValueModel.GrappleHeldRounds(0.46f, 3f) *
                         ValueModel.GrappleRoundNet(29.9f, 28f, 14.3f, 1f)), kept, 3);
            // No horizon, no value either way.
            Assert.Equal(0f, ValueModel.GrappleKeepValue(0.46f, 0f, 29.9f, 28f, 14.3f, 0.5f, 1f, false));
        }

        [Fact]
        public void KeepingCostsTheWithdrawalALetGoWouldAllow()
        {
            // Wenduag's Rustclaw hold read "keep +28.5". Badly hurt, with a withdrawal worth more than the hold, she lets go.
            float stuck = ValueModel.GrappleKeepWhileStuck(28.5f, 35f);
            Assert.Equal(-6.5f, stuck, 3);
            Assert.True(ValueModel.GrappleReleaseValue(stuck, out string why) > 0f);
            Assert.False(string.IsNullOrEmpty(why));
            // A smaller withdrawal leaves the hold kept, lower.
            Assert.Equal(18.5f, ValueModel.GrappleKeepWhileStuck(28.5f, 10f), 3);
            Assert.Equal(0f, ValueModel.GrappleReleaseValue(ValueModel.GrappleKeepWhileStuck(28.5f, 10f), out _));
            // Healthy, or nowhere safer to go: unchanged.
            Assert.Equal(28.5f, ValueModel.GrappleKeepWhileStuck(28.5f, 0f));
            Assert.Equal(28.5f, ValueModel.GrappleKeepWhileStuck(28.5f, -4f));
            Assert.Equal(28.5f, ValueModel.GrappleKeepWhileStuck(28.5f, float.NaN));
            Assert.Equal(28.5f, ValueModel.GrappleKeepWhileStuck(28.5f, float.PositiveInfinity));
        }

        [Fact]
        public void MeleeGainsAgainstProneAndHelplessTargets()
        {
            Assert.Equal(0, ValueModel.MeleeDefenceLoss(true, false, false));
            Assert.Equal(4, ValueModel.MeleeDefenceLoss(true, true, false));
            Assert.Equal(4, ValueModel.MeleeDefenceLoss(true, false, true));
            Assert.Equal(8, ValueModel.MeleeDefenceLoss(true, true, true));
            Assert.Equal(0, ValueModel.MeleeDefenceLoss(false, true, true));
        }
    }
}
