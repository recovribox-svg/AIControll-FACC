// ★ v0.3.28: the mod's reader of on-hit ability damage (CombatMath.OnHitAbilityDamage) on the game's real blueprints.
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using CompanionAI_Pathfinder.Tactics;

namespace ProfileAudit
{
    internal static class OnHitCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            failures = 0;
            var lines = new List<string>();
            var found = new List<string>();
            foreach (var fact in loaded.OfType<BlueprintUnitFact>())
            {
                float points = CombatMath.OnHitAbilityDamage(fact.ComponentsArray, out var stat);
                if (points > 0f) found.Add($"{fact.name}: {points:0.##} {stat}");
            }
            lines.Add($"ONHIT ability damage read on {found.Count} facts: " + string.Join("; ", found.Take(40)));
            var shadow = loaded.OfType<BlueprintUnitFact>().FirstOrDefault(f => f.name == "Shadow_Feature_StrengthDamage");
            if (shadow != null)
            {
                float points = CombatMath.OnHitAbilityDamage(shadow.ComponentsArray, out var stat);
                bool ok = System.Math.Abs(points - 3.5f) < 0.01f && stat == Kingmaker.EntitySystem.Stats.StatType.Strength;
                if (!ok) failures++;
                lines.Add($"ONHIT Shadow_Feature_StrengthDamage: {points:0.##} {stat} - {(ok ? "held (1d6 Strength)" : "FAILED (expected 3.5 Strength)")}");
            }
            return lines;
        }
    }
}
