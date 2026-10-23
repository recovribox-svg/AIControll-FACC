using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.20 (audit PERF-001): quiet waits for the standard action's cooldown (Tactics/RealTimePacing.cs).</summary>
    public class RealTimePacingTests
    {
        [Fact]
        public void StandardAvailable_NoQuietWait() =>
            Assert.Equal(0f, RealTimePacing.QuietSeconds(hasStandardAction: true, standardCooldownLeft: 0f, decisionInterval: 0.5f));

        [Fact]
        public void Cooldown_WaitsUntilItEnds_AtMostTheCap()
        {
            Assert.Equal(1.2f, RealTimePacing.QuietSeconds(false, 1.2f, 0.5f), 3);
            Assert.Equal(RealTimePacing.MaxQuietSeconds, RealTimePacing.QuietSeconds(false, 5.5f, 0.5f), 3);
        }

        [Fact]
        public void CooldownShorterThanTheInterval_NoQuietWait() =>
            Assert.Equal(0f, RealTimePacing.QuietSeconds(false, 0.3f, 0.5f));

        private static RealTimePacing.Snapshot S(int hp = 30, int engaged = 1, int enemies = 3, int areas = 0, int allies = 5) =>
            new RealTimePacing.Snapshot { Hp = hp, EngagedBy = engaged, Enemies = enemies, Areas = areas, Allies = allies };

        [Fact]
        public void NothingChanged_StaysQuietUntilTheTime()
        {
            Assert.False(RealTimePacing.ShouldWake(10.0, 11.0, S(), S()));
            Assert.True(RealTimePacing.ShouldWake(11.0, 11.0, S(), S()));
        }

        [Theory]
        [InlineData(25, 1, 3, 0)]   // lost HP
        [InlineData(30, 2, 3, 0)]   // an enemy walked up
        [InlineData(30, 0, 3, 0)]   // the enemy next to it left (or fell)
        [InlineData(30, 1, 2, 0)]   // an enemy fell
        [InlineData(30, 1, 4, 0)]   // an enemy joined
        [InlineData(30, 1, 3, 1)]   // a harmful area on it now
        public void AChange_EndsTheQuietWait(int hp, int engaged, int enemies, int areas) =>
            Assert.True(RealTimePacing.ShouldWake(10.0, 11.0, S(), S(hp, engaged, enemies, areas)));

        [Fact]
        public void AnAllyDown_EndsTheQuietWait()
        {
            Assert.True(RealTimePacing.ShouldWake(10.0, 11.0, S(), S(allies: 4)));
            Assert.False(RealTimePacing.ShouldWake(10.0, 11.0, S(), S(allies: 6)));   // a summon joined
        }

        [Fact]
        public void Healed_OrAnAreaGone_StaysQuiet()
        {
            Assert.False(RealTimePacing.ShouldWake(10.0, 11.0, S(), S(hp: 40)));
            Assert.False(RealTimePacing.ShouldWake(10.0, 11.0, S(areas: 1), S(areas: 0)));
        }
    }
}
