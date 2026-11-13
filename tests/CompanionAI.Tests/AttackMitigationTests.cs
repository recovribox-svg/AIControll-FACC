using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class AttackMitigationTests
    {
        [Fact]
        public void SwarmOnly_DoesNotBuyImagesOrArmour_ButStillBuysTemporaryHp()
        {
            float totalIncoming = 20f;
            float attacks = ValueModel.AttackMitigationIncoming(totalIncoming, 0f, 20f);
            Assert.Equal(0f, ValueModel.ArmorGain(attacks, 0.6f, 4f));
            Assert.Equal(0f, ValueModel.MirrorImagesAbsorbed(4f, attacks / 5f, 5f, 3f));
            Assert.True(ValueModel.TempHpAbsorbed(10f, totalIncoming, 3f) > 0f);
        }

        [Fact]
        public void OrdinaryEnemyOnly_PreservesExistingCalibratedEstimate()
            => Assert.Equal(17f, ValueModel.AttackMitigationIncoming(17f, 30f, 30f));

        [Fact]
        public void MixedFight_UsesOrdinaryShareOfCalibratedDanger()
            => Assert.Equal(10f, ValueModel.AttackMitigationIncoming(40f, 5f, 20f), 4);

        [Fact]
        public void MixedFight_KeepsImagesUsefulAgainstTheOrdinaryPart()
        {
            float attacks = ValueModel.AttackMitigationIncoming(40f, 5f, 20f);
            Assert.True(ValueModel.MirrorImagesAbsorbed(4f, attacks / 5f, 5f, 1f) > 0f);
            Assert.True(ValueModel.MirrorImagesAbsorbed(4f, attacks / 5f, 5f, 1f) <
                        ValueModel.MirrorImagesAbsorbed(4f, 40f / 5f, 5f, 1f));
        }

        [Fact]
        public void NoRawExposure_CannotInventAttackProtection()
            => Assert.Equal(0f, ValueModel.AttackMitigationIncoming(12f, 2f, 0f));

        [Fact]
        public void PredictiveSwarmOnlyShare_IsStillZero()
            => Assert.Equal(0f, ValueModel.AttackMitigationIncoming(7f, 0f, 25f));

        [Theory]
        [InlineData(0f, 5f, 20f)]
        [InlineData(-1f, 5f, 20f)]
        [InlineData(10f, -1f, 20f)]
        [InlineData(10f, 5f, -1f)]
        [InlineData(float.NaN, 5f, 20f)]
        [InlineData(10f, float.NaN, 20f)]
        [InlineData(10f, 5f, float.NaN)]
        [InlineData(float.PositiveInfinity, 5f, 20f)]
        [InlineData(10f, float.PositiveInfinity, 20f)]
        [InlineData(10f, 5f, float.PositiveInfinity)]
        public void InvalidOrNonpositiveInput_IsNotProtection(float incoming, float eligible, float total)
            => Assert.Equal(0f, ValueModel.AttackMitigationIncoming(incoming, eligible, total));

        [Fact]
        public void RoundingOrOversizedEligibleShare_CannotExceedTotalIncoming()
            => Assert.Equal(10f, ValueModel.AttackMitigationIncoming(10f, 21f, 20f));
    }
}
