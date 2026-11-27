using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public sealed class TypedProtectionTests
    {
        [Fact]
        public void SaveBonusDoesNotRemoveTheGuaranteedHalfDamage()
        {
            float before = ValueModel.SaveEffectFactor(0.6f, halfOnSave: true);
            float after = ValueModel.SaveEffectFactor(0.2f, halfOnSave: true);
            Assert.Equal(0.25f, ValueModel.PreventedEffectShare(before, after), 4);
        }

        [Fact]
        public void SpellResistanceValuesOnlyTheMarginalProtection()
        {
            float before = ResistanceRule.PassChance(false, false, true, false, 15, 10);
            float after = ResistanceRule.PassChance(false, false, true, false, 19, 10);
            Assert.Equal(0.25f, ValueModel.PreventedEffectShare(before, after), 4);
            Assert.Equal(0f, ValueModel.PreventedEffectShare(before, before));
            Assert.Equal(0f, ValueModel.PreventedEffectShare(0f, 0f));
        }
    }
}
