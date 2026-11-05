// ★ v0.3.28 test 6: the shipped rank-increment reader's bindings and the blueprint wiring it relies on (Enveloping Winds,
// Shroud of Water), without a unit: the reflected game members exist, the winds' miss chance reads the effect feature's
// rank, the rank progression evaluates, and each shroud component names the upgrade feature.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class RankTalentCheck
    {
        private const string WindsBuff = "b803fcd9da7b1564fb52978f08372767";
        private const string WindsRank = "bbba1600582cf8446bb515a33bd89af8";
        private const string ShroudUpgrade = "fc083e19a8c961c4890de1a36e2b5c20";
        private static readonly string[] Shrouds = { "1ff803cb49f63ea4185490fae2c43ca7", "4d8feca11d6e29a499ae761b90eacdba" };

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            int bad = 0, checks = 0;
            var engine = typeof(TacticalEngine);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            foreach (var name in new[] { "RankConfigBaseType", "RankConfigFeature", "RankApplyProgression", "RankApplyMinMax", "MissType" })
            {
                checks++;
                if (engine.GetField(name, flags)?.GetValue(null) == null) { bad++; lines.Add($"RANK-INCREMENT FAILED: {name} not bound"); }
            }
            var reads = engine.GetMethod("ReadsRankOf", flags);
            var progression = engine.GetField("RankApplyProgression", flags)?.GetValue(null) as MethodInfo;
            var byGuid = loaded.GroupBy(b => b.AssetGuid.ToString()).ToDictionary(g => g.Key, g => g.First());
            if (byGuid.TryGetValue(WindsBuff, out var winds) && byGuid.TryGetValue(WindsRank, out var rankBp) && reads != null)
            {
                var feature = rankBp as BlueprintFeature;
                var configs = (winds as BlueprintScriptableObject)?.GetComponents<ContextRankConfig>()
                    .Where(c => (bool)reads.Invoke(null, new object[] { c, feature })).ToList() ?? new List<ContextRankConfig>();
                checks++;
                if (configs.Count != 1) { bad++; lines.Add($"RANK-INCREMENT FAILED: Enveloping Winds reads its rank {configs.Count} times"); }
                checks++;
                if ((winds as BlueprintScriptableObject)?.GetComponent<SetAttackerMissChance>() == null)
                { bad++; lines.Add("RANK-INCREMENT FAILED: Enveloping Winds has no SetAttackerMissChance"); }
                if (configs.Count == 1 && progression != null)
                {
                    checks++;
                    int three = (int)progression.Invoke(configs[0], new object[] { 3 });
                    if (three != 3) { bad++; lines.Add($"RANK-INCREMENT FAILED: AsIs progression of 3 gave {three}"); }
                }
            }
            else lines.Add("RANK-INCREMENT SKIPPED: Enveloping Winds blueprints not loaded");
            foreach (var guid in Shrouds)
            {
                if (!byGuid.TryGetValue(guid, out var bp)) { lines.Add($"RANK-INCREMENT SKIPPED: shroud {guid} not loaded"); continue; }
                checks++;
                var shroud = (bp as BlueprintScriptableObject)?.GetComponent<ShroudOfWater>();
                if (shroud?.UpgradeFeature?.AssetGuid.ToString() != ShroudUpgrade)
                { bad++; lines.Add($"RANK-INCREMENT FAILED: {bp.name} shroud upgrade feature not found"); }
            }
            lines.Insert(0, $"RANK-INCREMENT {checks} checks, failures {bad}");
            failures = bad;
            return lines;
        }
    }
}
