using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.18: the game's spell-resistance order (Tactics/ResistanceRule.cs, docs/AI_TUNING_AUDIT.md C2).</summary>
    public class ResistanceRuleTests
    {
        [Fact]
        public void ImmunityStopsSpellsWithoutSpellResistance()
        {
            // Grease (no SR) on an airborne creature (immune to ground effects): 0.3.17 said it passes.
            Assert.Equal(0f, ResistanceRule.PassChance(bypass: false, immune: true, allowsSR: false, allyIgnores: false, 0, 5), 3);
            Assert.Equal(0f, ResistanceRule.PassChance(false, true, true, false, 15, 5), 3);
        }

        [Fact]
        public void BypassBeatsImmunity()
        {
            Assert.Equal(1f, ResistanceRule.PassChance(bypass: true, immune: true, allowsSR: true, allyIgnores: false, 30, 1), 3);
        }

        [Fact]
        public void NoRollWithoutSpellResistance()
        {
            Assert.Equal(1f, ResistanceRule.PassChance(false, false, allowsSR: false, allyIgnores: false, 20, 1), 3);
            Assert.Equal(1f, ResistanceRule.PassChance(false, false, true, allyIgnores: true, 20, 1), 3);
            Assert.Equal(1f, ResistanceRule.PassChance(false, false, true, false, 0, 1), 3);
        }

        [Fact]
        public void PenetrationCounts()
        {
            // SR 15 against caster level 5: passes on 10+ (55 %); Spell Penetration (+2): 8+ (65 %).
            Assert.Equal(0.55f, ResistanceRule.PassChance(false, false, true, false, 15, 5), 3);
            Assert.Equal(0.65f, ResistanceRule.PassChance(false, false, true, false, 15, 7), 3);
            Assert.Equal(0f, ResistanceRule.PassChance(false, false, true, false, 40, 5), 3);
            Assert.Equal(1f, ResistanceRule.PassChance(false, false, true, false, 5, 10), 3);
        }
    }
}
