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
        [Fact] public void RegenerationKeepsItsExistingRemovalDiscount()
        {
            ValueModel.FusionValue(1, 1, 100, 0, 15, 40, 40, 3, true, false, out float regenerating, out _);
            ValueModel.FusionValue(1, 1, 100, 0, 15, 40, 40, 3, true, true, out float stopped, out _);
            Assert.True(regenerating < stopped);
        }
    }
}
