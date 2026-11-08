// ★ v0.3.30 competence3: the shipped revealing-buff reader (TacticalEngine.RevealingBuff) on the loaded buffs: Faerie Fire's
// and Glitterdust's buffs make their bearer immune to the Invisible condition (AddConditionImmunity Invisible), which ends an
// invisible unit's stealth (UnitStealthController.ShouldBeInStealth). Lists every such buff found.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace ProfileAudit
{
    internal static class RevealCheck
    {
        private static readonly Dictionary<string, string> Expected = new Dictionary<string, string>
        {
            { "cc383a9eaae4d2b45a925d442b367b54", "FaerieFireBuff" },
            { "03457e519288aad4085eae91918a76bf", "GlitterdustBuff" },
        };

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            int bad = 0, checks = 0;
            var reader = typeof(TacticalEngine).GetMethod("RevealingBuff", BindingFlags.NonPublic | BindingFlags.Static);
            checks++;
            if (reader == null)
            {
                failures = 1;
                return new List<string> { "REVEAL FAILED: TacticalEngine.RevealingBuff not found" };
            }
            var buffs = loaded.OfType<BlueprintBuff>().ToList();
            var revealing = buffs.Where(b => (bool)reader.Invoke(null, new object[] { b })).Select(b => b.name).OrderBy(n => n).ToList();
            foreach (var pair in Expected)
            {
                var buff = buffs.FirstOrDefault(b => b.AssetGuid.ToString() == pair.Key);
                if (buff == null) { lines.Add($"REVEAL SKIPPED: {pair.Value} not loaded"); continue; }
                checks++;
                if (!(bool)reader.Invoke(null, new object[] { buff })) { bad++; lines.Add($"REVEAL FAILED: {pair.Value} not read as revealing"); }
            }
            lines.Insert(0, $"REVEAL {checks} checks, failures {bad}; revealing buffs among {buffs.Count}: {string.Join(", ", revealing)}");
            failures = bad;
            return lines;
        }
    }
}
