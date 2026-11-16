using System;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class FusionPoolMetamagicTests
    {
        [Theory]
        [InlineData(2, 0, 1.0, true, true, 3)]
        [InlineData(3, 0, 1.0, true, true, 4)]
        [InlineData(12, 0, 1.0, true, true, 18)]
        [InlineData(12, 1, 0.5, true, true, 9)] // Bonus before Empower; modifier last.
        [InlineData(2, 1, 0.5, true, true, 2)]
        [InlineData(0, 4, 1.0, true, true, 6)] // Zero d6 still empowers the bonus.
        [InlineData(4, 0, 1.0, false, true, 4)] // d1/zero die does not empower.
        [InlineData(0, 4, 1.0, false, true, 4)]
        [InlineData(12, 0, 1.0, true, false, 12)]
        [InlineData(3, -6, 1.0, true, true, -4)] // Native integer division truncates towards zero.
        public void PoolFollowsNativeMetamagicOrder(int roll, int bonus, double modifier, bool empowerable, bool empowered, int expected)
            => Assert.Equal(expected, SharedHdBudget.PoolTotal(roll, bonus, modifier, empowerable, empowered));

        [Fact]
        public void InvalidArithmeticCannotProduceAFalseBudget()
        {
            Assert.Throws<OverflowException>(() => SharedHdBudget.PoolTotal(int.MaxValue, 1, 1, true, false));
            Assert.Throws<OverflowException>(() => SharedHdBudget.PoolTotal(int.MaxValue, 0, 1, true, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => SharedHdBudget.PoolTotal(2, 0, double.NaN, true, false));
        }

        [Fact]
        public void EmpoweredTwoDiceCanReachThirteenHitDiceButOrdinaryCannot()
        {
            float ordinary = 0, empowered = 0;
            for (int first = 1; first <= 6; first++)
                for (int second = 1; second <= 6; second++)
                {
                    var target = new SharedHdBudget.Recipient(1f, remaining => new SharedHdBudget.Visit(remaining >= 13, remaining >= 13, 13));
                    Assert.True(SharedHdBudget.TryForecast(first + second, new[] { target }, out var normal));
                    Assert.True(SharedHdBudget.TryForecast(SharedHdBudget.PoolTotal(first + second, 0, 1, true, true), new[] { target }, out var boosted));
                    ordinary += normal[0] / 36f; empowered += boosted[0] / 36f;
                }
            Assert.Equal(0f, ordinary); Assert.Equal(10f / 36f, empowered, 5);
        }

    }
}
