using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class ShifterRulesTests
    {
        [Theory]
        [InlineData(0, false, 0)]
        [InlineData(5, false, 0)]
        [InlineData(6, false, 1)]
        [InlineData(10, false, 1)]
        [InlineData(11, false, 2)]
        [InlineData(15, false, 2)]
        [InlineData(16, false, 3)]
        [InlineData(20, false, 3)]
        [InlineData(21, false, 3)]
        [InlineData(21, true, 4)]
        [InlineData(25, true, 4)]
        [InlineData(26, true, 5)]
        public void NaturalIterativesBeginAfterEachFiveBabAndRespectCompanionException(int bab, bool companion, int extra)
            => Assert.Equal(extra, ShifterRules.Iteratives(bab, companion));

        [Theory]
        [InlineData(3, 0.5f, 1)]
        [InlineData(3, 1.5f, 4)]
        [InlineData(4, 1.5f, 6)]
        [InlineData(-3, 0.5f, -3)]
        [InlineData(-3, 1.5f, -3)]
        [InlineData(0, 0.5f, 0)]
        public void NativeAbilityDamageRoundsPositiveFractionsButKeepsNegativeModifierWhole(int bonus, float multiplier, int damage)
            => Assert.Equal(damage, ShifterRules.AttributeDamage(bonus, multiplier));
    }
}
