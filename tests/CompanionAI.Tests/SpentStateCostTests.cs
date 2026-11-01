using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.24 (Psionics): spending a held state such as psionic focus (Tactics/ValueModel.HeldStates.cs).</summary>
    public class SpentStateCostTests
    {
        [Fact]
        public void NothingWhenTheFightEndsWithTheGainedEffect()
        {
            Assert.Equal(0f, ValueModel.SpentStateCost(0f, 2f, 1f, 19f));
            Assert.Equal(0f, ValueModel.SpentStateCost(-1f, 2f, 1f, 19f));
            Assert.Equal(0f, ValueModel.SpentStateCost(float.NaN, 2f, 1f, 19f));
        }

        [Fact]
        public void FocusAndTranceCostTheirActionsOnceARoundIsLeft()
        {
            // Gain Psionic Focus (standard) + the trance's toggle (standard), a level-5 unit (PerRound 19).
            Assert.Equal(38f, ValueModel.SpentStateCost(1f, 2f, 0f, 19f), 3);
            Assert.Equal(38f + 0.5f * 4f, ValueModel.SpentStateCost(4f, 2f, 0.5f, 19f), 3);
        }

        [Fact]
        public void LessThanARoundLeftChargesInProportion()
        {
            Assert.Equal(19f, ValueModel.SpentStateCost(0.5f, 2f, 0f, 19f), 3);
        }

        [Fact]
        public void FarAboveAOneRoundBuffInALongFight()
        {
            // A +2 dodge / +2 attack round is worth a few points; spending focus at level 5 with 4 rounds left is not.
            Assert.True(ValueModel.SpentStateCost(4f, 1.5f, 0.2f, ValueModel.PerRound(5)) > 25f);
        }
    }
}
