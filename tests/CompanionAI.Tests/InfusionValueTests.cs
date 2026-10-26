using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class InfusionValueTests
    {
        [Fact]
        public void DeliveryAndManeuverAreSeparateChecks_CountEachOnce()
        {
            // 50% landed blasts, 25% successful trips, worth 40 at the existing 0.4 trip share: two points.
            Assert.Equal(2f, ValueModel.OnHitControlValue(0.5f, 0.25f, 40f, 0.4f), 4);
            Assert.Equal(4f, ValueModel.OnHitControlValue(1f, 0.25f, 40f, 0.4f), 4);
            Assert.Equal(8f, ValueModel.OnHitControlValue(0.5f, 1f, 40f, 0.4f), 4);
        }

        [Theory]
        [InlineData(0f, 1f, 40f, 0.4f)] // missed / resisted blast
        [InlineData(1f, 0f, 40f, 0.4f)] // maneuver immunity / no successful check
        [InlineData(1f, 1f, 40f, 0f)]   // prone, unpushable or no valued control
        [InlineData(1f, 1f, 0f, 0.4f)]
        public void NoDeliveryOrNoUsefulControl_ContributesNothing(float hit, float check, float worth, float share)
        {
            Assert.Equal(0f, ValueModel.OnHitControlValue(hit, check, worth, share));
        }

        [Fact]
        public void DoesNotExceedTheSuccessfulEffectsWorth()
        {
            Assert.Equal(16f, ValueModel.OnHitControlValue(2f, 2f, 40f, 0.4f), 4);
            Assert.Equal(0f, ValueModel.OnHitControlValue(float.NaN, 1f, 40f, 0.4f));
            Assert.Equal(0f, ValueModel.OnHitControlValue(1f, -0.1f, 40f, 0.4f));
        }
    }
}
