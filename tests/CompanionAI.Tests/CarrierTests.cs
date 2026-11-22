using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ Fusion coverage C6: a buff's end within the horizon, and round-tick control (Tactics/ValueModel.Periodic.cs).</summary>
    public class CarrierTests
    {
        [Fact]
        public void EndComesWhenTheDurationRunsOutOrASaveRemovesIt()
        {
            Assert.Equal(1f, ValueModel.CarrierEndChance(0, 1f, true), 4);     // Acidic Spray's acid: one round, within the horizon
            Assert.Equal(0f, ValueModel.CarrierEndChance(0, 1f, false), 4);    // no tick, longer than the horizon
            Assert.Equal(0f, ValueModel.CarrierEndChance(3, 1f, false), 4);    // ticks that never remove it
            Assert.Equal(0.75f, ValueModel.CarrierEndChance(2, 0.5f, false), 4);
            Assert.Equal(1f, ValueModel.CarrierEndChance(1, 0f, false), 4);
        }

        [Fact]
        public void TickControlNeedsBothSavesFailedAndEndsOnAPass()
        {
            Assert.Equal(0f, ValueModel.TickControlRounds(1f, 1f, 0), 4);
            Assert.Equal(3f, ValueModel.TickControlRounds(1f, 1f, 3), 4);      // on from the first tick, never removed
            Assert.Equal(0f, ValueModel.TickControlRounds(0f, 1f, 3), 4);      // the first save always passes: removed
            Assert.Equal(0f, ValueModel.TickControlRounds(1f, 0f, 3), 4);      // the second save always passes: never on
            // first 0.5, second 0.5: tick 1 on with 0.25 (free 0.25 left); tick 2: newly on 0.25*0.5*0.5, kept 0.25*0.5
            Assert.Equal(0.25f + 0.0625f + 0.125f, ValueModel.TickControlRounds(0.5f, 0.5f, 2), 4);
            Assert.True(ValueModel.TickControlRounds(0.6f, 0.6f, 3) < ValueModel.TickControlRounds(0.8f, 0.6f, 3));
        }
    }
}
