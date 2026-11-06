// ★ v0.3.28 test 15: the swarm-space reader (CombatMath.IsSwarmTargetArea) on the loaded blueprints: every area that adds the
// units it covers to a swarm's targets (an AbilityAreaEffectBuff whose buff runs ContextActionSwarmTarget), and whether the
// buff that carries each such area also strikes each round (AddFactContextActions.NewRound with ContextActionSwarmAttack) -
// what CombatMath.SwarmAreaOf needs at run time to price the space. The Mandragora swarm's area must be read as one.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class SwarmAreaCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            failures = 0;
            var isSwarm = typeof(CombatMath).GetMethod("IsSwarmTargetArea", BindingFlags.NonPublic | BindingFlags.Static);
            var hasAttack = typeof(CombatMath).GetMethod("HasSwarmAttack", BindingFlags.NonPublic | BindingFlags.Static);
            var waits = typeof(CombatMath).GetMethod("StrikeWaitsForInnerCircle", BindingFlags.NonPublic | BindingFlags.Static);
            if (isSwarm == null || hasAttack == null || waits == null) { lines.Add("SWARM-AREA FAILED: reader not bound"); failures = 1; return lines; }
            var all = loaded.ToList();
            var areas = all.OfType<BlueprintAbilityAreaEffect>().Where(a => (bool)isSwarm.Invoke(null, new object[] { a })).ToList();
            if (areas.Count == 0)
            {
                lines.Add("SWARM-AREA SKIPPED: no swarm area in the loaded blueprints");
                return lines;
            }
            var set = new HashSet<BlueprintAbilityAreaEffect>(areas);
            int carriers = 0, striking = 0;
            var silent = new List<string>();
            var waiting = new List<string>();
            var always = new List<string>();
            foreach (var buff in all.OfType<BlueprintBuff>())
            {
                var carried = buff.GetComponents<Kingmaker.UnitLogic.Buffs.Components.AddAreaEffect>()
                                  .Select(c => c.AreaEffect).Where(a => a != null && set.Contains(a)).ToList();
                if (carried.Count == 0) continue;
                carriers++;
                bool strikes = buff.GetComponents<AddFactContextActions>()
                                   .Any(c => (bool)hasAttack.Invoke(null, new object[] { c.NewRound, 0 }));
                if (strikes)
                {
                    striking++;
                    bool? gate = null;
                    foreach (var c in buff.GetComponents<AddFactContextActions>())
                    {
                        var g = (bool?)waits.Invoke(null, new object[] { c.NewRound, false, 0 });
                        gate = gate == null ? g : g == null ? gate : gate.Value && g.Value;
                    }
                    (gate == false ? always : waiting).Add(buff.name);
                }
                else silent.Add(buff.name);
            }
            bool mandragora = areas.Any(a => a.name == "MandragoraSwarmDamageArea");
            if (waiting.Contains("MandragoraSwarmDamageBuff") == false && always.Contains("MandragoraSwarmDamageBuff"))
            {
                failures++;
                lines.Add("SWARM-AREA FAILED: MandragoraSwarmDamageBuff read as striking without a target at its centre");
            }
            if (waiting.Contains("RavenSwarmDamageBuff"))
            {
                failures++;
                lines.Add("SWARM-AREA FAILED: RavenSwarmDamageBuff read as waiting for a target at its centre");
            }
            if (all.OfType<BlueprintAbilityAreaEffect>().Any(a => a.name == "MandragoraSwarmDamageArea") && !mandragora)
            {
                failures++;
                lines.Add("SWARM-AREA FAILED: MandragoraSwarmDamageArea not read as a swarm's space");
            }
            if (failures == 0)
                lines.Add($"SWARM-AREA ok: {areas.Count} swarm spaces ({string.Join(", ", areas.Select(a => a.name).OrderBy(n => n).Take(6))}" +
                          $"{(areas.Count > 6 ? ", ..." : "")}); {striking} of their {carriers} carrier buffs strike each round " +
                          $"({waiting.Count} once a target is at the centre, {always.Count} everyone inside: {string.Join(", ", always.Take(4))})" +
                          (silent.Count > 0 ? $" (not: {string.Join(", ", silent.Take(4))})" : ""));
            return lines;
        }
    }
}
