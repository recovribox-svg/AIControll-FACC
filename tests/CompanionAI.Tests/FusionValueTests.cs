using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class FusionValueTests
    {
        [Fact] public void AShortStunDoesNotInheritTheLongSlow()
        {
            var e = new ValueModel.ControlEnvelope();
            Assert.True(e.Add(1f, 1f)); Assert.True(e.Add(0.4f, 3f));
            Assert.Equal(1.8f, e.Rounds(), 5);
        }
        [Fact] public void TwoHardControlsDoNotDenyTheSameTurnTwice()
        {
            var e = new ValueModel.ControlEnvelope(); e.Add(1f, 2f); e.Add(1f, 3f);
            Assert.Equal(3f, e.Rounds()); e.Clear(); e.Add(0.15f, 1f);
            Assert.Equal(0.15f, e.Rounds());
        }
        [Fact] public void BoundOrInvalidMetadataCannotOverflowScratch()
        {
            var e = new ValueModel.ControlEnvelope();
            Assert.False(e.Add(float.NaN, 1)); Assert.False(e.Add(1, float.PositiveInfinity));
            for (int i = 0; i < ValueModel.ControlEnvelope.Capacity; i++) Assert.True(e.Add(0.4f, 1));
            Assert.False(e.Add(1, 1)); Assert.Equal(0.4f, e.Rounds());
        }
        [Fact] public void RareLethalDamageDoesNotBecomeALikelyKillFromItsMean()
        {
            ValueModel.FusionValue(0.2f, 1, 100, 0, 15, 40, 40, 3, false, false, out float damage, out float control);
            float conditional = ValueModel.DamageValue(100, 15, 40, 1);
            Assert.Equal(0.2f * conditional, damage, 5); Assert.Equal(0, control);
        }
        [Fact] public void ResistanceAndTheSharedSaveAreEachPaidOnce()
        {
            ValueModel.FusionValue(0.25f, 0.4f, 10, 0, 1000, 0, 30, 2, false, false, out float damage, out float control);
            Assert.Equal(1, damage, 5); Assert.Equal(6, control, 5);
        }
        [Fact] public void PassedHalfDamageStillCompetesWithoutFailedSaveControl()
        {
            ValueModel.FusionValue(0, 1, 20, 10, 1000, 0, 30, 3, false, false, out float damage, out float control);
            Assert.Equal(10, damage); Assert.Equal(0, control);
        }
        [Fact] public void EnergyImmuneDamageDoesNotEraseAUsefulControlPayload()
        {
            ValueModel.FusionValue(0.4f, 1, 0, 0, 100, 40, 30, 2, false, false, out float damage, out float control);
            Assert.Equal(0, damage); Assert.Equal(24, control, 5);
        }
        [Fact] public void RareDeliveryCannotEarnTheWholeHpCap()
        {
            ValueModel.FusionValue(1, 0.2f, 100, 100, 15, 0, 0, 0, false, false, out float damage, out float control);
            Assert.Equal(3, damage, 5); Assert.Equal(0, control);
        }
        [Fact] public void DamageThatNeverLandsCannotEarnHpOrControl()
        {
            ValueModel.FusionValue(1, 0, 100, 50, 20, 40, 40, 3, false, false, out float damage, out float control);
            Assert.Equal(0, damage); Assert.Equal(0, control);
        }
        // ★ Fusion F4 (docs/FUSION_OUTCOME_EFFECTS_PLAN.md): control on a passed root save, or on both outcomes.
        [Theory]
        [InlineData(0.2f, 1f, 100f, 0f, 15f, 40f, 40f, 3f)]
        [InlineData(0.25f, 0.4f, 10f, 0f, 1000f, 0f, 30f, 2f)]
        [InlineData(0.4f, 1f, 0f, 0f, 100f, 40f, 30f, 2f)]
        [InlineData(1f, 0.2f, 100f, 100f, 15f, 0f, 0f, 0f)]
        [InlineData(0.55f, 0.8f, 21f, 21f, 60f, 35f, 28f, 1.2f)]
        public void WithoutAPassedOutcomeEffectTheValueIsTheEarlierOne(float fail, float delivery, float failed, float passed,
            float hp, float worth, float controlWorth, float rounds)
        {
            ValueModel.FusionValue(fail, delivery, failed, passed, hp, worth, controlWorth, rounds, false, false, out float d0, out float c0);
            ValueModel.FusionValue(fail, delivery, failed, passed, hp, worth, controlWorth, rounds, 0f, false, false, out float d1, out float c1);
            Assert.Equal(d0, d1); Assert.Equal(c0, c1);   // exact: no rounding drift for admitted fusions
        }
        [Fact] public void PassOnlyControlIsWeightedByThePassedSave()
        {
            // Fear's shaken round against a target that fails 30% of the time; no damage.
            ValueModel.FusionValue(0.3f, 0.8f, 0, 0, 100, 0, 30, 0, 1, false, false, out float damage, out float control);
            Assert.Equal(0, damage); Assert.Equal(0.8f * 0.7f * 30f, control, 4);
        }
        [Fact] public void ControlOnBothOutcomesDoesNotDependOnTheSave()
        {
            // Frigid Touch's stagger: the same envelope whatever the save does.
            ValueModel.FusionValue(0.1f, 1, 0, 0, 100, 0, 30, 0.4f, 0.4f, false, false, out _, out float unlikely);
            ValueModel.FusionValue(0.9f, 1, 0, 0, 100, 0, 30, 0.4f, 0.4f, false, false, out _, out float likely);
            Assert.Equal(12f, unlikely, 4); Assert.Equal(12f, likely, 4);
        }
        [Fact] public void EachOutcomeKeepsItsOwnEnvelope()
        {
            // Fear: three frightened rounds on a failed save, one shaken round (a fifth of a turn) on a passed one.
            ValueModel.FusionValue(0.6f, 1, 0, 0, 100, 0, 30, 3f, 0.2f, false, false, out _, out float control);
            Assert.Equal(30f * (0.6f * 3f + 0.4f * 0.2f), control, 4);
        }
        [Fact] public void ATargetKilledOnThePassedOutcomeLosesNoFurtherTurns()
        {
            // Half damage on a pass still kills: its passed-outcome control is worth nothing more.
            ValueModel.FusionValue(0.5f, 1, 200, 100, 10, 40, 30, 0, 2, false, false, out _, out float dead);
            ValueModel.FusionValue(0.5f, 1, 20, 0, 1000, 40, 30, 0, 2, false, false, out _, out float alive);
            Assert.Equal(0f, dead, 4); Assert.Equal(0.5f * 30f * 2f, alive, 4);
        }
        [Fact] public void AnUndeliveredCastLeavesNoPassedOutcomeControl()
        {
            ValueModel.FusionValue(0.5f, 0, 0, 0, 100, 0, 30, 1, 1, false, false, out _, out float control);
            Assert.Equal(0, control);
        }
        [Theory]
        [InlineData(0.3f, true, false, 0.3f)]
        [InlineData(0.3f, false, true, 0.7f)]
        [InlineData(0.3f, true, true, 1f)]
        [InlineData(0.3f, false, false, 0f)]
        [InlineData(1.4f, false, true, 0f)]
        public void TheOutcomeShareCountsOnlyOutcomesThatCarryAnEffect(float fail, bool onFailure, bool onPass, float expected) =>
            Assert.Equal(expected, ValueModel.FusionOutcomeShare(fail, onFailure, onPass), 5);
        // ★ Fusion F7 (docs/FUSION_SLEEP_POOL.md): a control gated by a rolled shared Hit Dice pool.
        [Theory]
        [InlineData(0.4f, 3f, 0f, 1.2f)]      // nothing else: three rounds with the pool's chance
        [InlineData(0.4f, 3f, 1f, 1.8f)]      // beside a one-round control: the two envelopes blended, not summed
        [InlineData(1f, 3f, 1f, 3f)]
        [InlineData(0f, 3f, 1f, 1f)]
        [InlineData(1.7f, 3f, 1f, 3f)]
        public void APooledControlJoinsWithItsChance(float chance, float with, float without, float expected) =>
            Assert.Equal(expected, ValueModel.PooledRounds(without, with, chance), 5);
        [Fact] public void ARolledPoolAveragesTheOrderedForecastOverItsRolls()
        {
            // Two recipients of 4 and 5 Hit Dice, the first spends when it is covered. Pool 2d6.
            var recipients = new[] { Recipient(4), Recipient(5) };
            float first = 0f, second = 0f;
            for (int a = 1; a <= 6; a++)
                for (int b = 1; b <= 6; b++)
                {
                    Assert.True(SharedHdBudget.TryForecast(a + b, recipients, out var e));
                    first += e[0] / 36f; second += e[1] / 36f;
                }
            Assert.Equal(33f / 36f, first, 5);           // a 2d6 pool covers 4 Hit Dice unless it rolls 2 or 3
            Assert.Equal(10f / 36f, second, 5);          // 5 more are left only on a roll of 9 or more
            Assert.True(SharedHdBudget.TryForecast(4, recipients, out var fixedPool));
            Assert.Equal(1f, fixedPool[0]); Assert.Equal(0f, fixedPool[1]);   // Sleep's own fixed 4 would never reach the second
        }
        private static SharedHdBudget.Recipient Recipient(int hitDice) => new SharedHdBudget.Recipient(1f,
            remaining => new SharedHdBudget.Visit(hitDice <= remaining, hitDice <= remaining, hitDice));
        [Fact] public void RegenerationKeepsItsExistingRemovalDiscount()
        {
            ValueModel.FusionValue(1, 1, 100, 0, 15, 40, 40, 3, true, false, out float regenerating, out _);
            ValueModel.FusionValue(1, 1, 100, 0, 15, 40, 40, 3, true, true, out float stopped, out _);
            Assert.True(regenerating < stopped);
        }
    }
}
