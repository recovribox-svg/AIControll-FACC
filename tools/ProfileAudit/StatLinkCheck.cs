// ★ v0.3.30 test 2: the fact reader of ability-score links (TacticalEngine.FactStatLinksOf) on the loaded blueprints: every
// feature or buff whose bonus follows an ability score (DerivativeStatBonus; AddContextStatBonus ranked by the score's bonus,
// or recalculated when the score changes). Known cases must read as the game builds them: a paladin's Divine Grace is
// Charisma to all three saves, a monk's AC buff Wisdom to AC.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;

namespace ProfileAudit
{
    internal static class StatLinkCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            failures = 0;
            int wrong = 0;
            var reader = typeof(TacticalEngine).GetMethod("FactStatLinksOf", BindingFlags.NonPublic | BindingFlags.Static);
            if (reader == null) { lines.Add("STAT-LINKS FAILED: reader not bound"); failures = 1; return lines; }
            var byLink = new Dictionary<string, List<string>>();
            var read = new Dictionary<string, List<string>>();
            int facts = 0;
            foreach (var fact in loaded.OfType<BlueprintUnitFact>())
            {
                facts++;
                Array links;
                try { links = (Array)reader.Invoke(null, new object[] { fact }); }
                catch (Exception ex) { wrong++; lines.Add($"STAT-LINKS FAILED: {fact.name}: {ex.InnerException?.Message ?? ex.Message}"); continue; }
                if (links == null || links.Length == 0) continue;
                var mine = new List<string>();
                foreach (var link in links)
                {
                    var t = link.GetType();
                    string text = $"{t.GetField("From").GetValue(link)}->{t.GetField("To").GetValue(link)} x{t.GetField("Factor").GetValue(link)}";
                    mine.Add(text);
                    if (!byLink.TryGetValue(text, out var names)) byLink[text] = names = new List<string>();
                    names.Add(fact.name);
                }
                read[fact.name] = mine;
            }
            void Expect(string name, params string[] wanted)
            {
                if (!loaded.Any(b => b.name == name)) return;   // not in this dump
                read.TryGetValue(name, out var got);
                got = got ?? new List<string>();
                var missing = wanted.Where(w => !got.Contains(w)).ToList();
                if (missing.Count == 0) { lines.Add($"STAT-LINKS ok: {name} reads {string.Join(", ", got)}"); return; }
                wrong++;
                lines.Add($"STAT-LINKS FAILED: {name} reads [{string.Join(", ", got)}], missing {string.Join(", ", missing)}");
            }
            Expect("DivineGrace", "Charisma->SaveFortitude x1", "Charisma->SaveReflex x1", "Charisma->SaveWill x1");
            Expect("MonkACBonusBuff", "Wisdom->AC x1");
            Expect("ScaledFistACBonusBuff", "Charisma->AC x1");          // a custom property, recalculated on Charisma
            Expect("ImitationMonkACBonus", "Intelligence->AC x1");
            lines.Add($"STAT-LINKS: {facts} facts read, {read.Count} with links: " +
                      string.Join("; ", byLink.OrderByDescending(k => k.Value.Count).Take(24)
                          .Select(k => $"{k.Key} {k.Value.Count} (e.g. {string.Join(", ", k.Value.Take(3))})")));
            failures = wrong;
            return lines;
        }
    }
}
