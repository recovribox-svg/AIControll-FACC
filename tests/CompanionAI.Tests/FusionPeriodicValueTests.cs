using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ Fusion coverage C1 (docs/FUSION_MAIN_TARGET_DOT.md): damage over time a fused cast leaves on each save outcome.
    public class FusionPeriodicValueTests
    {
        [Theory]
        [InlineData(0.3f, 1f, 12f, 6f, 40f, 20f, 15f, 2f, 1f)]
        [InlineData(1f, 0.5f, 0f, 0f, 10f, 0f, 30f, 0f, 0f)]
        [InlineData(0f, 1f, 100f, 50f, 15f, 40f, 40f, 3f, 1f)]
        public void WithoutDamageOverTimeTheValueIsUnchangedToTheLastBit(float fail, float delivery, float failed, float passed,
            float hp, float worth, float controlWorth, float rounds, float passRounds)
        {
            ValueModel.FusionValue(fail, delivery, failed, passed, hp, worth, controlWorth, rounds, passRounds, false, false,
                out float before, out float beforeControl);
            ValueModel.FusionValue(fail, delivery, failed, passed, 0f, 0f, hp, worth, controlWorth, rounds, passRounds, false, false,
                out float after, out float afterControl);
            Assert.Equal(before, after);
            Assert.Equal(beforeControl, afterControl);
        }

        [Fact]
        public void LaterDamageCountsItsHpAndProgressButNotAKillNow()
        {
            // 7 now, 10.5 over the next rounds (3 ticks of 3.5), against 40 HP and a worth of 20, no save.
            ValueModel.FusionValue(1f, 1f, 7f, 7f, 10.5f, 10.5f, 40f, 20f, 0f, 0f, 0f, false, false, out float damage, out _);
            float kill = ValueModel.KillChance(1f, 7f, 40f);
            Assert.Equal(ValueModel.DamageValue(17.5f, 40f, 20f, kill), damage, 5);
            ValueModel.FusionValue(1f, 1f, 7f, 7f, 40f, 20f, 0f, 0f, false, false, out float immediateOnly, out _);
            Assert.True(damage > immediateOnly);
            // The ticks never take more than the HP the immediate damage leaves.
            ValueModel.FusionValue(1f, 1f, 7f, 7f, 1000f, 1000f, 40f, 0f, 0f, 0f, 0f, false, false, out float capped, out _);
            Assert.Equal(40f, capped, 5);
        }

        [Fact]
        public void DamageOverTimeOnlyOnAFailedSaveIsWeightedByTheFailChance()
        {
            // Acid on a failed save only: 9 later on a failure, nothing on a pass, 25 % fail chance.
            ValueModel.FusionValue(0.25f, 1f, 10f, 5f, 9f, 0f, 1000f, 0f, 0f, 0f, 0f, false, false, out float damage, out _);
            Assert.Equal(0.25f * 19f + 0.75f * 5f, damage, 4);
        }

        [Fact]
        public void TheControlAKillCancelsIsStillReadFromTheImmediateDamage()
        {
            ValueModel.FusionValue(1f, 1f, 5f, 5f, 0f, 0f, 30f, 10f, 20f, 2f, 0f, false, false, out _, out float without);
            ValueModel.FusionValue(1f, 1f, 5f, 5f, 50f, 50f, 30f, 10f, 20f, 2f, 0f, false, false, out _, out float with);
            Assert.Equal(without, with);
        }

        [Fact]
        public void ABuffLandingNowTicksOneIntervalLaterWithinTheHorizon()
        {
            // Native Buff.OnAttach: first tick one interval (6 s) after the buff lands; it ticks at an equal end time.
            Assert.Equal(2, ValueModel.PeriodicTicks(6, 6, 2 * 6, 3 * 6));   // Molten Orb's burning, 1d3 = 2 rounds
            Assert.Equal(3, ValueModel.PeriodicTicks(6, 6, 7 * 6, 3 * 6));   // Acid Arrow at CL 18, horizon 3
            Assert.Equal(1, ValueModel.PeriodicTicks(6, 6, 7 * 6, 1 * 6));   // a fight about to end
            Assert.Equal(0, ValueModel.PeriodicTicks(6, 6, 0.5 * 6, 3 * 6)); // gone before its first tick
        }

        [Fact]
        public void ASaveInEachTickEndsTheLaterTicks()
        {
            // Incendiary Runes' burning: a Reflex save each tick removes it; 50 % stays: 1 + 0.5 + 0.25 of three ticks.
            Assert.Equal(1.75f, ValueModel.PeriodicWeight(3, 0.5f), 5);
        }
    }
}
