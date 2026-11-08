// ★ v0.3.30 competence3: the shipped cast-time damage-bonus reader (CombatMath.GivesDamageBonus) on the Alchemist's Throw
// Anything: its before-cast trigger puts AlchemistThrowAnythingBuff (DamageBonusConditional, +Intelligence) on the caster.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.FactLogic;

namespace ProfileAudit
{
    internal static class CastBonusCheck
    {
        private const string ThrowAnything = "AlchemistThrowAnything";

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            int bad = 0, checks = 1;
            var reader = typeof(CombatMath).GetMethod("GivesDamageBonus", BindingFlags.NonPublic | BindingFlags.Static);
            if (reader == null)
            {
                failures = 1;
                return new List<string> { "CAST-BONUS FAILED: CombatMath.GivesDamageBonus not found" };
            }
            var facts = loaded.OfType<BlueprintScriptableObject>().ToList();
            var found = new List<string>();
            foreach (var fact in facts)
                foreach (var use in fact.ComponentsArray?.OfType<AddAbilityUseTrigger>() ?? Enumerable.Empty<AddAbilityUseTrigger>())
                    if (!use.AfterCast && use.Action?.Actions != null && (bool)reader.Invoke(null, new object[] { use })) found.Add(fact.name);
            var throwAnything = facts.FirstOrDefault(f => f.name == ThrowAnything);
            if (throwAnything == null) lines.Add($"CAST-BONUS SKIPPED: {ThrowAnything} not loaded");
            else
            {
                checks++;
                if (!found.Contains(ThrowAnything)) { bad++; lines.Add($"CAST-BONUS FAILED: {ThrowAnything} not read as a cast-time damage bonus"); }
            }
            lines.Insert(0, $"CAST-BONUS {checks} checks, failures {bad}; cast-time damage bonuses: {string.Join(", ", found.Distinct().OrderBy(n => n))}");
            failures = bad;
            return lines;
        }
    }
}
