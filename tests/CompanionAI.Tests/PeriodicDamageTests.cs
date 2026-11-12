using System;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class PeriodicDamageTests
    {
        [Theory]
        [InlineData(1, 4, 0, false, false, 2.5f)]
        [InlineData(1, 4, 0, false, true, 3.5f)]
        [InlineData(4, 6, 0, false, true, 20.75f)]
        [InlineData(1, 4, 0, true, true, 6f)]
        [InlineData(0, 0, 3, false, true, 4f)]
        public void UsesNativeIntegerEmpowerWithoutRolling(int count, int sides, int bonus, bool maximize, bool empower, float expected) =>
            Assert.Equal(expected, ValueModel.PeriodicDiceMean(count, sides, bonus, maximize, empower), 5);

        [Theory]
        [InlineData(5, 6, 30, 6, 1)]
        [InlineData(7, 6, 30, 6, 0)]
        [InlineData(6, 6, 5, 18, 0)]
        [InlineData(6, 6, 6, 18, 1)] // Native tick precedes expiry at an equal timestamp.
        [InlineData(6, 6, 18, 18, 3)]
        [InlineData(1, 1, 30, 6, 6)]
        [InlineData(0, 6, 30, 6, 2)]
        [InlineData(0, 6, -1, 18, 0)]
        [InlineData(1, 0, 30, 6, 0)]
        public void CountsNativeScheduleAndDeadline(double next, double every, double remaining, double horizon, int expected) =>
            Assert.Equal(expected, ValueModel.PeriodicTicks(next, every, remaining, horizon));

        [Fact]
        public void PermanentAndInvalidSchedulesStayDistinct()
        {
            Assert.Equal(3, ValueModel.PeriodicTicks(6, 6, double.PositiveInfinity, 18));
            Assert.Equal(0, ValueModel.PeriodicTicks(double.NaN, 6, 30, 18));
            Assert.Equal(0, ValueModel.PeriodicTicks(6, double.PositiveInfinity, 30, 18));
        }

        [Theory]
        [InlineData(0, .5f, 0)]
        [InlineData(3, 0, 1)]
        [InlineData(3, 1, 3)]
        [InlineData(3, .5f, 1.75f)]
        public void SaveThatEndsTheBuffReducesLaterTicks(int ticks, float stays, float expected) =>
            Assert.Equal(expected, ValueModel.PeriodicWeight(ticks, stays), 5);

        [Theory]
        [InlineData(.5f, 3, 1, false, .875f)]
        [InlineData(.5f, 3, 2, false, 1.375f)]
        [InlineData(.5f, 3, 1, true, 1.75f)]
        [InlineData(0, 5, 2, false, 0)]
        [InlineData(0, 5, 2, true, 2)]
        [InlineData(1, 3, 1, false, 3)]
        [InlineData(.5f, 4, 0, false, 0)]
        [InlineData(.5f, 0, 1, true, 0)]
        public void UsesPoisonCountersAndNativeWeakness(float fail, int ticks, int saves, bool weak, float expected) =>
            Assert.Equal(expected, ValueModel.ActivePoisonDoses(fail, ticks, saves, weak), 5);

        [Fact]
        public void CurrentAttachedHarmIsUnscaledAndPersistsWithoutAnEnemy()
        {
            Assert.Equal(15, ValueModel.IncomingWithAttached(20, .25f, 15, true, 0));
            Assert.Equal(5, ValueModel.IncomingWithAttached(20, .25f, 15, false, 0));
            Assert.Equal(22, ValueModel.IncomingWithAttached(20, .25f, 15, true, 7));
            Assert.Equal(12, ValueModel.IncomingWithAttached(20, .25f, 15, false, 7));
            Assert.Equal(7, ValueModel.IncomingWithAttached(0, .1f, 15, true, 7));
        }

        [Fact]
        public void LeavingAnEnemyKeepsTheSameBackgroundDropChance()
        {
            Assert.Equal(.58f, ValueModel.WithBackgroundDrop(.4f, .3f), 5);
            Assert.Equal(.3f, ValueModel.WithBackgroundDrop(0, .3f), 5);
            Assert.Equal(.4f, ValueModel.WithBackgroundDrop(.4f, 0), 5);
            Assert.Equal(0, ValueModel.WithBackgroundDrop(0, 0));
        }

        [Fact]
        public void EnemyArmourProtectionCannotPreventAttachedDamage()
        {
            Assert.Equal(12, ValueModel.AfterEnemyProtection(17, 7, .5f));
            Assert.Equal(7, ValueModel.AfterEnemyProtection(17, 7, 1));
            Assert.Equal(7, ValueModel.AfterEnemyProtection(7, 7, .5f));
            Assert.Equal(5, ValueModel.AfterEnemyProtection(10, 0, .5f));
        }
    }
}
