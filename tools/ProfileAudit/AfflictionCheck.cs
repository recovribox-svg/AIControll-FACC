// ★ v0.3.28 test 14: the affliction reader (CombatMath.ReadAffliction) on the loaded blueprints, without a unit (every save an
// even chance): the giant spider's bite poisons Strength (a save, then BuffPoisonStatDamage), the Shadow's touch takes 1d6
// Strength, and the Mandragora swarm's round deals 5d6 and nauseates (its poison buff ticks; its Constitution damage is not in
// the dumps, which lose those actions).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class AfflictionCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            failures = 0;
            var read = typeof(CombatMath).GetMethod("ReadAffliction", BindingFlags.NonPublic | BindingFlags.Static);
            if (read == null) { lines.Add("AFFLICTION FAILED: ReadAffliction not bound"); failures = 1; return lines; }
            var byName = loaded.OfType<BlueprintScriptableObject>().GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
            int checks = 0, bad = 0;
            var notes = new List<string>();
            object Read(ActionList list) => read.Invoke(null, new object[] { list, null, 15 });
            float Field(object a, string name) => (float)a.GetType().GetField(name).GetValue(a);
            float Points(object a, StatType stat)
            {
                var points = (IDictionary)a.GetType().GetField("Points").GetValue(a);
                return points.Contains(stat) ? (float)points[stat] : 0f;
            }
            void Check(bool ok, string what) { checks++; if (!ok) { bad++; lines.Add("AFFLICTION FAILED: " + what); } }

            if (byName.TryGetValue("GiantSpiderPoisonFeature", out var spider))
            {
                var trigger = spider.GetComponents<AddInitiatorAttackWithWeaponTrigger>().FirstOrDefault();
                var a = Read(trigger?.Action);
                float str = Points(a, StatType.Strength);
                Check(str > 0.3f, $"giant spider bite reads Strength {str:0.##}");
                notes.Add($"giant spider bite: Strength {str:0.##}");
            }
            if (byName.TryGetValue("Shadow_Feature_StrengthDamage", out var shadow))
            {
                var trigger = shadow.GetComponents<AddInitiatorAttackWithWeaponTrigger>().FirstOrDefault();
                var a = Read(trigger?.Action);
                float str = Points(a, StatType.Strength);
                Check(Math.Abs(str - 3.5f) < 0.01f, $"Shadow touch reads Strength {str:0.##} (3.5 expected)");
                notes.Add($"Shadow touch: Strength {str:0.##}");
            }
            if (byName.TryGetValue("MandragoraSwarmDamageBuff", out var swarm))
            {
                var round = swarm.GetComponents<AddFactContextActions>().FirstOrDefault(c => c.NewRound?.Actions?.Length > 0);
                var a = Read(round?.NewRound);
                float hp = Field(a, "HitPoints"), control = Field(a, "Control");
                Check(Math.Abs(hp - 17.5f) < 0.01f, $"Mandragora swarm round reads {hp:0.##} hit points (5d6 = 17.5 expected)");
                Check(control > 0.9f, $"Mandragora swarm round reads control {control:0.##} (Nauseated, then its poison's Confusion: 1 expected)");
                notes.Add($"Mandragora swarm round: {hp:0.#} HP, control {control:0.##} rounds, Constitution {Points(a, StatType.Constitution):0.##}");
            }
            failures = bad;
            if (checks == 0) lines.Add("AFFLICTION SKIPPED: none of the creature blueprints loaded");
            else if (bad == 0) lines.Add($"AFFLICTION ok: {checks} checks ({string.Join("; ", notes)})");
            return lines;
        }
    }
}
