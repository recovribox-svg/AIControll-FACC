// ★ v0.3.7: boss detection for the Progressive spell policy's emergency bypass.
//
// Only the game's own marker counts: BlueprintUnit's Experience component with Encounter == EncounterType.Boss (the game
// checks the same field in BlueprintUnit.ValidateBrainActions). Missing data means "not a boss". Other signals (CR far
// above the party, a dangerous fight) are reported as "would bypass" only, as evidence for later tuning.
// Everything is derived from the live battlefield each decision; the only stored data are per-combat "already logged"
// sets in CombatMemory and an immutable per-blueprint cache.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Experience;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class ThreatAssessment
    {
        private static readonly Dictionary<BlueprintUnit, bool> BossBlueprints = new Dictionary<BlueprintUnit, bool>();

        /// <summary>The game marks this unit's blueprint as a boss encounter.</summary>
        public static bool IsBoss(UnitEntityData unit)
        {
            var bp = unit?.Blueprint;
            if (bp == null) return false;
            if (BossBlueprints.TryGetValue(bp, out bool cached)) return cached;
            bool boss = false;
            try { boss = bp.GetComponent<Experience>()?.Encounter == EncounterType.Boss; } catch { }
            BossBlueprints[bp] = boss;
            return boss;
        }

        public static string EncounterTag(UnitEntityData unit)
        {
            try { return unit?.Blueprint?.GetComponent<Experience>()?.Encounter.ToString() ?? "none"; }
            catch { return "none"; }
        }


        /// <summary>Would this cast hit one of the bosses: aimed at it, or a boss inside its area.</summary>
        public static bool HitsBoss(UnitEntityData target, Vector3? point, AbilityProfile profile, List<UnitEntityData> bosses,
                                    IReadOnlyCollection<UnitEntityData> recipients = null)
        {
            if (bosses.Count == 0) return false;
            if (recipients != null) return bosses.Any(recipients.Contains);
            if (profile?.IsProjectileCone == true) return false;
            if (target != null && bosses.Contains(target)) return true;
            if (profile == null || !profile.IsAoE) return false;
            Vector3? center = target?.Position ?? point;
            return center.HasValue && bosses.Any(b => Vector3.Distance(b.Position, center.Value) <= profile.AoERadius + b.Corpulence);
        }

        /// <summary>Heuristic signals that are logged but never used to bypass (no validated threshold yet).</summary>
        /// <returns>(stable key for "log once", text)</returns>
        public static IEnumerable<KeyValuePair<string, string>> ShadowSignals(TacticalContext ctx)
        {
            float partyLevel = ctx.Allies.Count == 0 ? 1f
                : (float)ctx.Allies.Average(a => Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1));
            foreach (var e in ctx.Enemies)
            {
                int cr = 0;
                try { cr = e.Blueprint?.CR ?? 0; } catch { }
                if (cr >= partyLevel + 3f && !IsBoss(e))
                    yield return new KeyValuePair<string, string>("cr:" + e.UniqueId, $"{e.CharacterName} CR {cr} vs party level {partyLevel:F0}");
            }
            if (ctx.Difficulty >= 1.25f)
                yield return new KeyValuePair<string, string>("difficulty", $"encounter difficulty {ctx.Difficulty:F2} (>= 1.25)");
        }

        public static void ClearCache() => BossBlueprints.Clear();
    }
}
