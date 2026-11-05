using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class BuffFallbackQualityTests
    {
        // ★ v0.3.28 (codex review): exercise the shipped fallback shared with ProfileAudit, including the marker
        // branch reached by unread AddFacts and the recognized/enchantment paths that must retain their value.
        [Theory]
        [InlineData(true, 0.4f, false, false, true, false, 0.4f)]
        [InlineData(true, 0.4f, false, false, true, true, 0f)]
        [InlineData(true, 0.6f, false, false, true, true, 0.6f)]
        [InlineData(false, 0.05f, true, false, true, false, 0.8f)]
        [InlineData(false, 0.05f, false, true, false, true, 0f)]
        [InlineData(false, 0.05f, false, true, true, false, 0f)]
        [InlineData(false, 0.05f, false, true, false, false, 0.5f)]
        [InlineData(false, 0.05f, false, false, true, false, 0f)]
        [InlineData(false, 0.05f, false, false, false, false, 0.05f)]
        [InlineData(false, 0.05f, false, false, false, true, 0f)]
        public void PreservesRecognizedEffectsAndRejectsUnreadLimitedBuffs(bool recognized, float combatValue,
            bool enchants, bool onlyUnrecognized, bool limitedUse, bool item, float expected)
        {
            Assert.Equal(expected, ValueModel.BuffFallbackQuality(recognized, combatValue, enchants,
                onlyUnrecognized, limitedUse, item));
        }
    }
}
