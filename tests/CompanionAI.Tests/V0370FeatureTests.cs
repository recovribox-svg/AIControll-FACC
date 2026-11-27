using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.70 F1-F3: the pure rules behind repositioning, weapon-set switching and summon choice.</summary>
    public sealed class V0370FeatureTests
    {
        [Fact]
        public void RepositionIsWorthTheDropChanceAndDamageKeptOff()
        {
            // 20 % less likely to drop for a unit worth 40, 10 HP less expected damage (0.3 a point).
            Assert.Equal(11f, ValueModel.RepositionGain(0.3f, 0.1f, 40f, 15f, 5f, 1f), 3);
            // A spot that takes more damage costs.
            Assert.True(ValueModel.RepositionGain(0.1f, 0.1f, 40f, 5f, 15f, 1f) < 0f);
            Assert.Equal(0f, ValueModel.RepositionGain(float.NaN, 0f, 40f, 5f, 5f, 1f));
        }

        [Fact]
        public void WeaponSetSwitchCountsOnlyAClearGain()
        {
            // The bow deals 20 a round against 5 for walking in with the blade: the shot now and two better rounds.
            Assert.Equal(8f + 2f * 15f, ValueModel.WeaponSetSwitchWorth(8f, 20f, 5f, 2f), 3);
            // Within the margin (x1.2 + 1): only the attack after the switch counts.
            Assert.Equal(8f, ValueModel.WeaponSetSwitchWorth(8f, 12.5f, 10f, 2f), 3);
            Assert.Equal(0f, ValueModel.WeaponSetSwitchWorth(0f, 5f, 10f, 2f));
        }

        [Fact]
        public void SummonPriorFollowsCrAgainstTheEnemies()
        {
            float even = ValueModel.SummonPrior(5f, 5f);
            Assert.Equal(ValueModel.PerRound(5f) * ValueModel.SummonFactor, even, 3);
            Assert.True(ValueModel.SummonPrior(9f, 5f) > even);
            Assert.True(ValueModel.SummonPrior(1f, 12f) < even);
            // Bounded within half and one and a half of an even match.
            Assert.Equal(ValueModel.PerRound(0.5f) * ValueModel.SummonFactor * 0.5f, ValueModel.SummonPrior(0.5f, 30f), 3);
        }

        [Fact]
        public void MeasuredSummonDamageWeighsInWithTheRoundsSeen()
        {
            Assert.Equal(10f, ValueModel.SummonPerRound(10f, float.NaN, 0), 3);
            // 6 rounds seen: half measured.
            Assert.Equal(15f, ValueModel.SummonPerRound(10f, 20f, 6), 3);
            // At most 60 % measured however long.
            Assert.Equal(10f * 0.4f + 30f * 0.6f, ValueModel.SummonPerRound(10f, 30f, 1000), 3);
        }

        [Fact]
        public void BodiesBeyondTwoAnEnemyCountLess()
        {
            Assert.Equal(1f, ValueModel.SummonBodyShare(3f, 0, 4), 3);
            Assert.Equal(0.5f, ValueModel.SummonBodyShare(4f, 0, 1), 3);
            Assert.Equal(0.25f, ValueModel.SummonBodyShare(4f, 6, 1), 3);
            Assert.Equal(0f, ValueModel.SummonBodyShare(0f, 0, 4));
        }

        [Fact]
        public void SummonMemoryAveragesDamageOverRoundsFought()
        {
            var memory = new SummonWorthMemory();
            Assert.False(memory.TryPerRound("wolf", out _, out _));
            memory.NoteDamage("wolf", 12f);   // damage before its first counted round still counts
            memory.NoteRound("wolf");
            memory.NoteRound("wolf");
            memory.NoteDamage("wolf", 8f);
            Assert.True(memory.TryPerRound("wolf", out float perRound, out int rounds));
            Assert.Equal(10f, perRound, 3);
            Assert.Equal(2, rounds);
            memory.NoteDamage("wolf", -5f);
            memory.NoteDamage(null, 5f);
            Assert.True(memory.TryPerRound("wolf", out perRound, out _));
            Assert.Equal(10f, perRound, 3);
        }
    }
}
