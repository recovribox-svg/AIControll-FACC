using System;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class EnergyProtectionTests
    {
        [Fact]
        public void OneLargeFirePacket_DoesNotBorrowTenSmallWeaponHits()
        {
            float size = ValueModel.EnergyPacketSize(100f, 0f, 0);
            float hits = ValueModel.EnergyHitsPerRound(100f, size);
            Assert.Equal(1f, hits);
            Assert.Equal(10f, ValueModel.EnergyProtectionPerRound(100f, hits, 3f, 10f, null, false, 0f, false));
        }

        [Fact]
        public void PacketSizeUsesTypedEventEvidence_AndUnknownRateDoesNotBecomeOneHit()
        {
            Assert.Equal(30f, ValueModel.EnergyPacketSize(0f, 90f, 3));
            Assert.Equal(100f, ValueModel.EnergyPacketSize(100f, 90f, 3));
            Assert.Equal(30f, ValueModel.EnergyPacketSize(20f, 90f, 3));
            Assert.Equal(0f, ValueModel.EnergyHitsPerRound(100f, ValueModel.EnergyPacketSize(0f, 0f, 0)));
            Assert.Equal(0f, ValueModel.EnergyHitsPerRound(0f, 30f));
        }

        [Fact]
        public void PhysicalOrOtherEnergyEvidence_DoesNotGiveColdProtectionValue()
        {
            float cold = ValueModel.EnergyExposureShare(0f, 100f, 0f, 40f);
            float fire = ValueModel.EnergyExposureShare(30f, 100f, 20f, 40f);
            Assert.Equal(0f, cold);
            Assert.InRange(fire, 0.01f, 1f);
            Assert.Equal(0f, ValueModel.EnergyProtectionPerRound(50f * cold, 2f, 3f, 10f, null, false, 0f, false));
            Assert.True(ValueModel.EnergyProtectionPerRound(50f * fire, 2f, 3f, 10f, null, false, 0f, false) > 0f);
        }

        [Fact]
        public void UnknownEnergyIsNotAnInventedShare_AndExistingPriorDampsOneHit()
        {
            Assert.Equal(0f, ValueModel.EnergyExposureShare(0f, 0f, 0f, 0f));
            Assert.Equal(0f, ValueModel.EnergyExposureShare(0f, 40f, 0f, 20f));
            float oneHit = ValueModel.EnergyExposureShare(10f, 10f, 0f, 40f);
            Assert.InRange(oneHit, 0.001f, 0.2f); // a first energy hit does not reclassify all prior physical danger
        }

        [Fact]
        public void EnergyAttributionCannotExceedWholeDamage()
        {
            float fire = ValueModel.EnergyExposureShare(20f, 100f, 40f, 80f);
            float cold = ValueModel.EnergyExposureShare(30f, 100f, 20f, 80f);
            Assert.InRange(fire + cold, 0f, 1f);
            Assert.Equal(1f, ValueModel.EnergyExposureShare(200f, 100f, 40f, 80f));
        }

        [Theory]
        [InlineData(0f, 2f, 3f, 10f, 0f)]
        [InlineData(12f, 2f, 3f, 5f, 10f)]
        [InlineData(12f, 2f, 3f, 50f, 12f)]
        [InlineData(12f, 0f, 3f, 5f, 0f)]
        [InlineData(12f, 2f, 0f, 5f, 0f)]
        public void ResistanceIsCappedByMatchingDamageAndNativeRate(float incoming, float hits, float rounds,
            float resistance, float expected)
        {
            Assert.Equal(expected, ValueModel.EnergyProtectionPerRound(incoming, hits, rounds, resistance, null, false, 0f, false), 4);
        }

        [Fact]
        public void PoolIsAWholeWindowCap_NotARepeatedPerRoundGrant()
        {
            Assert.Equal(5f, ValueModel.EnergyProtectionPerRound(12f, 2f, 3f, 5f, 15f, false, 0f, false), 4);
            Assert.Equal(0f, ValueModel.EnergyProtectionPerRound(12f, 2f, 3f, 5f, 0f, false, 0f, false));
        }

        [Fact]
        public void PoolPaysForAllAbsorption_NotOnlyImprovementOverExistingResistance()
        {
            // Twenty cold damage per hit, ten already resisted: the sixty-point pool lasts three hits, not six.
            // It prevents ten EXTRA per hit: thirty extra HP over ten rounds, hence three per round.
            Assert.Equal(3f, ValueModel.EnergyProtectionPerRound(10f, 1f, 10f, 60f, 60f, false, 10f, false), 4);
        }

        [Fact]
        public void EqualOrWeakerResistanceAddsNothing_BetterCountsOnlyIncrement()
        {
            Assert.Equal(0f, ValueModel.EnergyProtectionPerRound(20f, 1f, 3f, 10f, null, false, 10f, false));
            Assert.Equal(0f, ValueModel.EnergyProtectionPerRound(20f, 1f, 3f, 5f, null, false, 10f, false));
            Assert.Equal(5f, ValueModel.EnergyProtectionPerRound(20f, 1f, 3f, 15f, null, false, 10f, false), 4);
        }

        [Fact]
        public void FireAndColdPoolsCannotPayForEachOthersDamage()
        {
            float cold = ValueModel.EnergyProtectionPerRound(8f, 1f, 4f, 20f, 20f, false, 0f, false);
            float fire = ValueModel.EnergyProtectionPerRound(12f, 1f, 4f, 20f, 2f, false, 0f, false);
            Assert.Equal(5f, cold, 4);
            Assert.Equal(0.5f, fire, 4);
            Assert.Equal(5.5f, cold + fire, 4);
        }

        [Fact]
        public void ImmunityHasNoInventedThirtyPointRateLimit_AndPoolOverlapIsConservative()
        {
            Assert.Equal(75f, ValueModel.EnergyProtectionPerRound(75f, 1f, 2f, 0f, null, true, 0f, false), 4);
            Assert.Equal(0f, ValueModel.EnergyProtectionPerRound(75f, 1f, 2f, 0f, null, true, 0f, true));
        }
    }
}
