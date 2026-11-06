// ★ v0.3.28 test 13: the drain reader (CombatMath.KindOf) on the loaded blueprints: which damage actions of the abilities'
// profiles are ability damage or negative levels, so they are valued as such (ValueModel.Drain.cs) and not as hit points.
// Enervation must read as negative levels.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class DrainCheck
    {
        private const string Enervation = "f34fb78eaaec141469079af124bcfa0f";

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            failures = 0;
            var kindOf = typeof(CombatMath).GetMethod("KindOf", BindingFlags.NonPublic | BindingFlags.Static);
            var field = typeof(ContextActionDealDamage).GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance);
            if (kindOf == null || field == null) { lines.Add("DRAIN FAILED: KindOf or m_Type not bound"); failures = 1; return lines; }
            var byKind = new Dictionary<string, List<string>>();
            int wrong = 0;
            bool enervation = false, enervationSeen = false;
            foreach (var ability in loaded.OfType<BlueprintAbility>())
            {
                AbilityProfile profile;
                try { profile = AbilityProfile.Get(ability); } catch { continue; }
                if (profile == null) continue;
                foreach (var deal in profile.Damage.OfType<ContextActionDealDamage>())
                {
                    string kind = kindOf.Invoke(null, new object[] { deal }).ToString();
                    string raw = field.GetValue(deal)?.ToString();
                    string expected = raw == "AbilityDamage" ? "AbilityDamage" : raw == "EnergyDrain" ? "EnergyDrain" : "HitPoints";
                    if (kind != expected) { wrong++; lines.Add($"DRAIN FAILED: {ability.name} reads {kind}, its action is {raw}"); }
                    if (kind == "HitPoints") continue;
                    if (!byKind.TryGetValue(kind, out var names)) byKind[kind] = names = new List<string>();
                    if (!names.Contains(ability.name)) names.Add(ability.name);
                    if (ability.AssetGuid.ToString() == Enervation && kind == "EnergyDrain") enervation = true;
                }
                if (ability.AssetGuid.ToString() == Enervation) enervationSeen = true;
            }
            if (enervationSeen && !enervation) { wrong++; lines.Add("DRAIN FAILED: Enervation does not read as negative levels"); }
            failures = wrong;
            if (failures == 0)
                lines.Add("DRAIN ok: " + string.Join("; ", byKind.Select(k => $"{k.Key} {k.Value.Count} abilities (e.g. {string.Join(", ", k.Value.Take(6))})")) +
                          (enervationSeen ? "; Enervation reads as negative levels" : "; Enervation not loaded"));
            return lines;
        }
    }
}
