using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class MagusChargeChanceTests
    {
        [Fact]
        public void NoEligibleWeaponDoesNotDeliverACharge()
        {
            var chance = new MagusChargeChance();
            Assert.Equal(0f, chance.Hit);
            Assert.Equal(0f, chance.Critical);
        }

        [Theory]
        [InlineData(3, 3, 19)]
        [InlineData(11, 16, 18)]
        [InlineData(16, 20, 20)]
        [InlineData(20, 20, 20)]
        public void TwoWeaponAttemptsMatchIndependentD20Enumeration(int firstNeeds, int secondNeeds, int threat)
        {
            // Enumerate both actual attack/confirmation dice. The second critical cannot multiply a charge
            // already delivered by a first ordinary hit. Expected probability is independent of accumulator code.
            int landed = 0, critical = 0, total = 20 * 20 * 20 * 20;
            for (int a = 1; a <= 20; a++)
            for (int confirmA = 1; confirmA <= 20; confirmA++)
            for (int b = 1; b <= 20; b++)
            for (int confirmB = 1; confirmB <= 20; confirmB++)
            {
                if (a >= firstNeeds)
                {
                    landed++;
                    if (a >= threat && confirmA >= firstNeeds) critical++;
                }
                else if (b >= secondNeeds)
                {
                    landed++;
                    if (b >= threat && confirmB >= secondNeeds) critical++;
                }
            }
            var chance = new MagusChargeChance();
            float first = (21f - firstNeeds) / 20f, second = (21f - secondNeeds) / 20f;
            chance.Add(first, (21f - System.Math.Max(firstNeeds, threat)) / 20f * first);
            chance.Add(second, (21f - System.Math.Max(secondNeeds, threat)) / 20f * second);
            Assert.Equal((float)landed / total, chance.Hit, 5);
            Assert.Equal((float)critical / total, chance.Critical, 5);
        }

        [Fact]
        public void AGuaranteedFirstHitConsumesTheOnlyCharge()
        {
            var chance = new MagusChargeChance();
            chance.Add(1f, 0f);
            chance.Add(1f, 1f);
            Assert.Equal(1f, chance.Hit);
            Assert.Equal(0f, chance.Critical);
        }

        [Fact]
        public void MissedAttemptsRetainTheChargeForLaterCritical()
        {
            var chance = new MagusChargeChance();
            chance.Add(0f, 0f);
            chance.Add(1f, 1f);
            Assert.Equal(1f, chance.Hit);
            Assert.Equal(1f, chance.Critical);
        }

        [Fact]
        public void ThreeFiftyPercentHitsStillDeliverOneCharge()
        {
            var chance = new MagusChargeChance();
            chance.Add(0.5f, 0f); chance.Add(0.5f, 0f); chance.Add(0.5f, 0f);
            Assert.Equal(0.875f, chance.Hit);
            Assert.Equal(0f, chance.Critical);
        }
    }
}
