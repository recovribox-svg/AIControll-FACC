using System;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.23 (bombs task): the easy-fight restraint shared by spell slots and daily bombs
    /// (Tactics/ValueModel.Resources.cs).</summary>
    public class LimitedUseDiscountTests
    {
        /// <summary>The spell path's inline formula before the move (TacticalEngine.ScoreAbility, v0.3.17).</summary>
        private static float Old(float difficulty) => 1f - Math.Max(0.55f, Math.Min(1f, 0.55f + 0.45f * difficulty));

        [Theory]
        [InlineData(0.1f)]
        [InlineData(0.3f)]
        [InlineData(0.5f)]
        [InlineData(0.8f)]
        [InlineData(1f)]
        [InlineData(2f)]
        public void SpellsKeepTheirDiscount(float difficulty) =>
            Assert.Equal(Old(difficulty), ValueModel.LimitedUseDiscount(difficulty), 5);

        [Fact]
        public void EasiestFight_TakesAboutTwoFifths() => Assert.Equal(0.405f, ValueModel.LimitedUseDiscount(0.1f), 3);

        [Fact]
        public void HardFight_TakesNothing()
        {
            Assert.Equal(0f, ValueModel.LimitedUseDiscount(1f), 5);
            Assert.Equal(0f, ValueModel.LimitedUseDiscount(2f), 5);
        }

        [Fact]
        public void NeverMoreThanTheFloorAllows() =>
            Assert.Equal(1f - ValueModel.MinLimitedUseShare, ValueModel.LimitedUseDiscount(-5f), 5);

        [Fact]
        public void EasierFights_DiscountMore() =>
            Assert.True(ValueModel.LimitedUseDiscount(0.2f) > ValueModel.LimitedUseDiscount(0.6f));

        [Fact]
        public void UnreadableDifficulty_DiscountsNothing() => Assert.Equal(0f, ValueModel.LimitedUseDiscount(float.NaN));

        /// <summary>The acceptance case: in an easy fight (difficulty 0.4) a bomb 15 % better than the reusable crossbow
        /// shot loses to it; one twice as good still wins.</summary>
        [Fact]
        public void ModestGainLoses_ClearGainWins()
        {
            float crossbow = 8f, d = 0.4f;
            Assert.True(crossbow * 1.15f * (1f - ValueModel.LimitedUseDiscount(d)) < crossbow);
            Assert.True(crossbow * 2f * (1f - ValueModel.LimitedUseDiscount(d)) > crossbow);
        }
    }
}
