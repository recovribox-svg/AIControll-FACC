using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class BurnConsequenceTests
    {
        [Fact]
        public void MentalPenaltyDoesNotInventNonlethalKnockout()
        {
            // The actual modeled graph has -2 Will per burn and no nonlethal modifier. Even at 1 HP, its cost must
            // come from the save penalty, not the old level-scaled phantom damage/automatic knockout.
            float mental = Cost(0, -2f / 3f, hp: 1, incoming: 1, worth: 100, rounds: 3);
            Assert.InRange(mental, 0.01f, 10f);
            Assert.Equal(200f, Cost(11, 0, hp: 1, incoming: 1, worth: 100, rounds: 3));
        }

        [Fact]
        public void OrdinaryNonlethalRetainsDayCostAndDropRisk()
        {
            Assert.Equal(2.2f, Cost(22, 0, hp: 60, incoming: 0, worth: 100, rounds: 4), 4);
            Assert.True(Cost(22, 0, hp: 60, incoming: 20, worth: 100, rounds: 4) > 2.2f);
            Assert.Equal(200f, Cost(22, 0, hp: 22, incoming: 20, worth: 100, rounds: 4));
        }

        [Fact]
        public void SavePenaltyUsesActualDangerAndRemainingHorizon()
        {
            float one = Cost(0, -2f / 3f, hp: 60, incoming: 20, worth: 100, rounds: 1);
            float longer = Cost(0, -2f / 3f, hp: 60, incoming: 20, worth: 100, rounds: 4);
            float moreBurn = Cost(0, -4f / 3f, hp: 60, incoming: 20, worth: 100, rounds: 4);
            Assert.True(one > 0);
            Assert.True(longer > one);
            Assert.True(moreBurn > longer);
            Assert.Equal(0, Cost(0, -2f / 3f, hp: 60, incoming: 0, worth: 100, rounds: 4));
        }

        [Fact]
        public void UnchangedRankAddsNoConsequence()
        {
            Assert.Equal(0f, Cost(0, 0, hp: 60, incoming: 20, worth: 100, rounds: 4));
        }

        private static float Cost(float hpLoss, float saves, float hp, float incoming, float worth, float rounds) =>
            ValueModel.KineticBurnConsequenceCost(hpLoss, saves, hp, incoming, worth, rounds, 0.1f);
    }
}
