using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>coverage7 follow-ups: the hits of a routine that fire an on-hit rider (Tactics/OnHitRules.cs).</summary>
    public class OnHitRulesTests
    {
        [Fact]
        public void OnlySuitableWeaponsFireTheRider()
        {
            // Schir: a bardiche disease does not fire on the gore.
            float fired = OnHitRules.FiringHits(new[] { true, false }, new[] { 0.6f, 0.8f }, false, false, 1f);
            Assert.Equal(0.6f, fired, 4);
        }

        [Fact]
        public void EveryAttackOfASingleWeaponRoutineFires()
        {
            // A Shadow's three touches.
            Assert.Equal(1.5f, OnHitRules.FiringHits(new[] { true, true, true }, new[] { 0.7f, 0.5f, 0.3f }, false, false, 1f), 4);
        }

        [Fact]
        public void FirstAttackOnlyCountsTheFirstLine()
        {
            Assert.Equal(0.7f, OnHitRules.FiringHits(new[] { true, true }, new[] { 0.7f, 0.5f }, true, false, 1f), 4);
            Assert.Equal(0f, OnHitRules.FiringHits(new[] { false, true }, new[] { 0.7f, 0.5f }, true, false, 1f), 4);
        }

        [Fact]
        public void FirstHitOnlyFiresOnceAtMost()
        {
            Assert.Equal(1f - 0.3f * 0.5f, OnHitRules.FiringHits(new[] { true, true }, new[] { 0.7f, 0.5f }, false, true, 1f), 4);
        }

        [Fact]
        public void AGrappleThatEndsTheRoutineScalesTheFirings()
        {
            Assert.Equal(0.6f, OnHitRules.FiringHits(new[] { true, true }, new[] { 0.6f, 0.6f }, false, false, 0.5f), 4);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void NothingRunsNothingFires(float reached) =>
            Assert.Equal(0f, OnHitRules.FiringHits(new[] { true }, new[] { 0.9f }, false, false, reached));

        [Fact]
        public void MismatchedOrMissingLinesFireNothingBeyondTheShorter()
        {
            Assert.Equal(0f, OnHitRules.FiringHits(null, new[] { 0.5f }, false, false, 1f));
            Assert.Equal(0.5f, OnHitRules.FiringHits(new[] { true }, new[] { 0.5f, 0.9f }, false, false, 1f), 4);
        }
    }
}
