using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class BurnConsequenceTests
    {
        // ★ v0.3.30 competence2: the per-rest price of one burn at character level 11 (burn x level x 0.1).
        private const float DayPrice = 1.1f;

        [Fact]
        public void MentalPenaltyDoesNotInventNonlethalKnockout()
        {
            // The actual modeled graph has -2 Will per burn and no nonlethal modifier. Even at 1 HP, its cost must
            // come from the save penalty and the burn's own day price, not the old level-scaled phantom damage/knockout.
            float mental = Cost(0, -2f / 3f, hp: 1, incoming: 1, worth: 100, rounds: 3);
            Assert.InRange(mental, DayPrice + 0.01f, 10f);
            Assert.Equal(200f, Cost(11, 0, hp: 1, incoming: 1, worth: 100, rounds: 3));
        }

        [Fact]
        public void OrdinaryNonlethalRetainsDayCostAndDropRisk()
        {
            Assert.Equal(2.2f, Cost(22, 0, hp: 60, incoming: 0, worth: 100, rounds: 4, dayPrice: 2.2f), 4);
            Assert.True(Cost(22, 0, hp: 60, incoming: 20, worth: 100, rounds: 4, dayPrice: 2.2f) > 2.2f);
            Assert.Equal(200f, Cost(22, 0, hp: 22, incoming: 20, worth: 100, rounds: 4, dayPrice: 2.2f));
        }

        [Fact]
        public void SavePenaltyUsesActualDangerAndRemainingHorizon()
        {
            float one = Cost(0, -2f / 3f, hp: 60, incoming: 20, worth: 100, rounds: 1);
            float longer = Cost(0, -2f / 3f, hp: 60, incoming: 20, worth: 100, rounds: 4);
            float moreBurn = Cost(0, -4f / 3f, hp: 60, incoming: 20, worth: 100, rounds: 4);
            Assert.True(one > DayPrice);
            Assert.True(longer > one);
            Assert.True(moreBurn > longer);
        }

        /// <summary>
        /// ★ v0.3.30 competence2 (PR #20 review): with no danger seen yet a Will-penalty burn still spends the day's
        /// pool and lasts until rest - it costs the same day price as nonlethal burn, never nothing.
        /// </summary>
        [Fact]
        public void MentalBurnWithNoDangerStillCostsTheDayPrice()
        {
            Assert.Equal(DayPrice, Cost(0, -2f / 3f, hp: 60, incoming: 0, worth: 100, rounds: 4), 4);
            Assert.Equal(Cost(11, 0, hp: 60, incoming: 0, worth: 100, rounds: 4),
                         Cost(0, -2f / 3f, hp: 60, incoming: 0, worth: 100, rounds: 4), 4);
        }

        [Fact]
        public void UnchangedRankAddsOnlyTheDayPrice()
        {
            Assert.Equal(0f, Cost(0, 0, hp: 60, incoming: 20, worth: 100, rounds: 4, dayPrice: 0f));
            Assert.Equal(DayPrice, Cost(0, 0, hp: 60, incoming: 20, worth: 100, rounds: 4), 4);
        }

        private static float Cost(float hpLoss, float saves, float hp, float incoming, float worth, float rounds,
                                  float dayPrice = DayPrice) =>
            ValueModel.KineticBurnConsequenceCost(hpLoss, saves, hp, incoming, worth, rounds, dayPrice);
    }
}
