using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class SwarmEscapeTests
    {
        [Fact]
        public void HealthyMoveOnlyActor_CanPreventNonlethalHpInsteadOfEndingItsTurn()
            => Assert.Equal(0.9f, ValueModel.SwarmEscapeGain(0f, 0f, 20f, 3.5f, 0.5f, 1f), 4);

        [Fact]
        public void NoKnownDirectHp_PreservesExistingDropOnlyValue()
            => Assert.Equal(4.8f, ValueModel.SwarmEscapeGain(0.3f, 0.1f, 40f, 0f, 0f, 0.6f), 4);

        [Fact]
        public void HpReserveAndProductivity_AreSeparate_DropDifferenceCountedOnce()
            => Assert.Equal(9f, ValueModel.SwarmEscapeGain(0.4f, 0.1f, 20f, 10f, 0f, 1f), 4);

        [Fact]
        public void MoreDangerousDestination_KeepsItsNegativeSurvivalValue()
            => Assert.Equal(-3f, ValueModel.SwarmEscapeGain(0.1f, 0.4f, 20f, 10f, 0f, 1f), 4);

        [Fact]
        public void EnteringAnotherSwarm_CannotCreateAnHpSaving()
            => Assert.Equal(0f, ValueModel.SwarmEscapeGain(0f, 0f, 20f, 8f, 10f, 1f));

        [Fact]
        public void SameExposureOrImmuneRecipient_GetsNoHpReserve()
        {
            Assert.Equal(0f, ValueModel.SwarmEscapeGain(0f, 0f, 20f, 8f, 8f, 1f));
            Assert.Equal(0f, ValueModel.SwarmEscapeGain(0f, 0f, 20f, 0f, 0f, 1f));
        }

        [Fact]
        public void ExistingRoleTolerance_AppliesToBothBenefits()
            => Assert.Equal(5.4f, ValueModel.SwarmEscapeGain(0.4f, 0.1f, 20f, 10f, 0f, 0.6f), 4);

        [Fact]
        public void UnknownHpQuote_RetainsOnlyExistingSurvivalValue()
        {
            Assert.Equal(6f, ValueModel.SwarmEscapeGain(0.4f, 0.1f, 20f, float.NaN, 0f, 1f), 4);
            Assert.Equal(6f, ValueModel.SwarmEscapeGain(0.4f, 0.1f, 20f, 5f, float.PositiveInfinity, 1f), 4);
        }

        [Fact]
        public void ReserveUsesTheExistingPreventionPolicy()
            => Assert.Equal(ValueModel.PreventionValue(3f, 1f, 1000f, 3f, 0f),
                ValueModel.SwarmEscapeGain(0f, 0f, 0f, 3f, 0f, 1f), 4);

    }
}
