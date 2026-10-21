// ★ v0.3.18 (docs/AI_TUNING_AUDIT.md C2): the game's order for "does a spell get through to this target"
// (RuleSpellResistanceCheck.IsSpellResisted), with no game types so it is unit-tested
// (tests/CompanionAI.Tests/ResistanceRuleTests.cs). The game side, which reads these inputs, is
// HitChanceCalculator.CalculateSpellResistance.
namespace CompanionAI_Pathfinder.Tactics
{
    public static class ResistanceRule
    {
        /// <summary>
        /// Chance the spell is not resisted.
        /// 1. <paramref name="bypass"/> (Primal Magic Essence, IgnoreSpellResistanceForSpells): it gets through, immunity
        ///    included.
        /// 2. <paramref name="immune"/> (spell or descriptor immunity): stopped, whether or not the spell allows SR.
        /// 3. No roll when the spell does not allow SR, when it ignores SR against allies and the target is one, or when
        ///    the target has none.
        /// 4. Otherwise it passes on a d20 of (SR - penetration) or more, with no natural 1 or 20.
        /// </summary>
        public static float PassChance(bool bypass, bool immune, bool allowsSR, bool allyIgnores, int spellResistance, int penetration)
        {
            if (bypass) return 1f;
            if (immune) return 0f;
            if (!allowsSR || allyIgnores || spellResistance <= 0) return 1f;
            int need = spellResistance - penetration;
            float chance = (21f - need) / 20f;
            return chance < 0f ? 0f : chance > 1f ? 1f : chance;
        }
    }
}
