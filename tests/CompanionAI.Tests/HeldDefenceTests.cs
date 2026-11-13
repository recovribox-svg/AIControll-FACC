using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class HeldDefenceTests
    {
        [Fact]
        public void RemovingArmourAtTheNaturalTwentyBoundary_CanLoseProtection()
        {
            Assert.Equal(0f, ValueModel.ArmorGain(10f, 0.05f, 1f));
            Assert.Equal(10f, ValueModel.ArmorProtectionFromRolls(10f, 0.05f, 0.1f, held: true), 4);
        }

        [Fact]
        public void ArmourBeyondTheRollLimit_CorrectlyAddsOrLosesNothing()
        {
            Assert.Equal(0f, ValueModel.ArmorProtectionFromRolls(10f, 0.05f, 0.05f, held: true));
            Assert.Equal(0f, ValueModel.ArmorProtectionFromRolls(10f, 0.95f, 0.95f, held: false));
        }

        [Fact]
        public void UncappedIncomingAndHeldBenefits_UseTheirOwnCurrentBaseline()
        {
            Assert.Equal(1f, ValueModel.ArmorProtectionFromRolls(10f, 0.5f, 0.45f, held: false), 4);
            Assert.Equal(1f, ValueModel.ArmorProtectionFromRolls(10f, 0.5f, 0.55f, held: true), 4);
        }

        [Fact]
        public void RemovingAnArmourPenalty_IsABenefitRatherThanALoss()
            => Assert.Equal(-1f, ValueModel.ArmorProtectionFromRolls(10f, 0.5f, 0.45f, held: true), 4);

        [Fact]
        public void AddingAndRemovingProtection_AgreeAtTheSameTwoEndpoints()
        {
            float gain = ValueModel.ArmorProtectionFromRolls(10f, 0.5f, 0.45f, held: false);
            float protectedIncoming = 10f - gain;
            float loss = ValueModel.ArmorProtectionFromRolls(protectedIncoming, 0.45f, 0.5f, held: true);
            Assert.Equal(gain, loss, 4);
            Assert.Equal(ValueModel.PreventionValue(gain, 3f, 10f, 10f, 20f),
                         ValueModel.HeldPreventionLoss(loss, 3f, 10f, protectedIncoming, 20f), 4);
        }

        [Fact]
        public void RemovingProtection_RaisesDangerFromTheAlreadyProtectedBaseline()
        {
            float held = ValueModel.HeldPreventionLoss(10f, 1f, 10f, 10f, 20f);
            Assert.Equal(-ValueModel.PreventionValue(-10f, 1f, 10f, 10f, 20f), held, 5);
            Assert.True(held > 3f); // HP reserve plus the newly increased chance to drop
            Assert.NotEqual(ValueModel.PreventionValue(10f, 1f, 10f, 10f, 20f), held);
        }

        [Fact]
        public void ZeroCurrentDanger_DoesNotEraseTheCostOfRemovingRealProtection()
            => Assert.True(ValueModel.HeldPreventionLoss(10f, 1f, 10f, 0f, 20f) > 3f);

        [Fact]
        public void ZeroProtectionOrDuration_HasNoHeldValue()
        {
            Assert.Equal(0f, ValueModel.HeldPreventionLoss(0f, 3f, 10f, 10f, 20f));
            Assert.Equal(0f, ValueModel.HeldPreventionLoss(10f, 0f, 10f, 10f, 20f));
        }

        [Theory]
        [InlineData(0f, 0.5f, 0.4f)]
        [InlineData(float.NaN, 0.5f, 0.4f)]
        [InlineData(float.PositiveInfinity, 0.5f, 0.4f)]
        [InlineData(10f, 0f, 0.4f)]
        [InlineData(10f, float.NaN, 0.4f)]
        [InlineData(10f, 0.5f, float.NaN)]
        [InlineData(10f, 1.1f, 0.4f)]
        [InlineData(10f, 0.5f, 1.1f)]
        public void InvalidRollInputs_CreateNoProtection(float incoming, float current, float alternative)
            => Assert.Equal(0f, ValueModel.ArmorProtectionFromRolls(incoming, current, alternative, held: true));
    }
}
