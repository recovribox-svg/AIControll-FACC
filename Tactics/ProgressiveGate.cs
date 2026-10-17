// ★ v0.3.7: Progressive spell policy - the game-facing side shared by the turn-based and real-time engines.
// Turns each engine's eligible options into TierOptions (effective slot level from the game, boss bypass), runs the
// pure SpellTierPolicy and logs the outcome when it changes. Design: docs/PROGRESSIVE_SPELLS.md.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using UnityEngine;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>One eligible option of either engine, as the gate sees it.</summary>
    public sealed class GateItem
    {
        public object Source;
        public string Label;
        /// <summary>The ability, or null for weapon attacks / movement.</summary>
        public AbilityData Ability;
        /// <summary>Damages, controls or debuffs an enemy.</summary>
        public bool Offensive;
        public float Value;
        public float Threshold;
        /// <summary>Chance to land on the main target; null = compute from the ability profile.</summary>
        public float? SuccessChance;
        public UnitEntityData Target;
        public Vector3? Point;
    }

    public static class ProgressiveGate
    {
        /// <summary>Last logged outcome per unit and mode, so a repeated identical decision is not logged again.</summary>
        private static readonly Dictionary<string, string> LastLog = new Dictionary<string, string>();

        public static bool IsEnabledFor(UnitEntityData unit)
        {
            var settings = ModSettings.Instance?.GetOrCreateSettings(unit.UniqueId, unit.CharacterName);
            return settings != null && settings.SpellPolicy == SpellResourcePolicy.Progressive;
        }

        /// <summary>Options to hold back (their <see cref="GateItem.Source"/>). Never throws: on error nothing is held back.</summary>
        public static HashSet<object> HeldBack(UnitEntityData unit, List<UnitEntityData> enemies, List<GateItem> items, string mode)
        {
            var held = new HashSet<object>();
            try
            {
                bool bypassOn = ModSettings.Instance?.ProgressiveBossBypass ?? true;
                var bosses = bypassOn ? enemies.Where(ThreatAssessment.IsBoss).ToList() : new List<UnitEntityData>();
                LogBossTransitions(enemies, bosses);

                var options = new List<TierOption>(items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    var book = it.Ability?.Spellbook;
                    bool inScope = book != null && it.Ability.SourceItem == null && it.Offensive;
                    int level = 0;
                    float chance = 1f;
                    bool bypass = false;
                    if (inScope)
                    {
                        // The game's own slot cost: base level + metamagic (Completely Normal Spell can make it 0).
                        try { level = book.GetSpellLevel(it.Ability); } catch { level = Math.Max(0, it.Ability.SpellLevel); }
                        var profile = AbilityProfile.Get(it.Ability);
                        chance = it.SuccessChance ??
                                 (it.Target != null && unit.IsEnemy(it.Target) && profile != null
                                     ? CombatMath.EffectChance(unit, it.Target, it.Ability, profile, profile.DealsDamage)
                                     : 1f);
                        bypass = ThreatAssessment.HitsBoss(it.Target, it.Point, profile, bosses);
                    }
                    options.Add(new TierOption
                    {
                        Key = i.ToString(),
                        Label = it.Label,
                        Book = book?.Blueprint?.name,
                        Level = level,
                        InScope = inScope,
                        Value = it.Value,
                        Threshold = it.Threshold,
                        SuccessChance = chance,
                        Bypass = bypass
                    });
                }

                var decision = SpellTierPolicy.Gate(options);
                foreach (var key in decision.HeldBack)
                    held.Add(items[int.Parse(key)].Source);

                string summary = decision.HeldBack.Count == 0 && decision.BypassedLabels.Count == 0 ? "" :
                    string.Join("; ", decision.Notes) +
                    (decision.HeldBack.Count > 0
                        ? " - held back: " + string.Join(", ", decision.HeldBack.Select(k => items[int.Parse(k)].Label))
                        : "") +
                    (decision.BypassedLabels.Count > 0 ? " - boss bypass: " + string.Join(", ", decision.BypassedLabels) : "");
                string logKey = unit.UniqueId + "|" + mode;
                if (summary.Length > 0 && (!LastLog.TryGetValue(logKey, out var last) || last != summary))
                    Main.Log($"[Progressive] {unit.CharacterName} ({mode}): {summary}");
                LastLog[logKey] = summary;
            }
            catch (Exception ex)
            {
                Main.Error($"[Progressive] {unit?.CharacterName}: {ex.Message} - policy skipped for this decision");
                held.Clear();
            }
            return held;
        }

        /// <summary>Boss bypass on/off, each logged once per combat.</summary>
        private static void LogBossTransitions(List<UnitEntityData> enemies, List<UnitEntityData> bosses)
        {
            foreach (var e in enemies)
                if (CombatMemory.ProgressiveLogged.Add("enemy:" + e.UniqueId))
                    Main.Verbose($"[Progressive] enemy {e.CharacterName}: Encounter={ThreatAssessment.EncounterTag(e)}, CR={e.Blueprint?.CR}");

            foreach (var b in bosses)
                if (CombatMemory.ActiveBosses.Add(b.UniqueId))
                    Main.Log($"[Progressive] boss bypass ON: {b.CharacterName} (the game marks it as a boss encounter)");
            foreach (var id in CombatMemory.ActiveBosses.ToList())
            {
                if (bosses.Any(b => b.UniqueId == id)) continue;
                CombatMemory.ActiveBosses.Remove(id);
                Main.Log("[Progressive] boss bypass OFF: the boss is no longer a valid target");
            }
        }

        /// <summary>Heuristic signals, logged once per combat as "would bypass" (turn-based context only).</summary>
        public static void LogShadowSignals(TacticalContext ctx)
        {
            foreach (var signal in ThreatAssessment.ShadowSignals(ctx))
                if (CombatMemory.ProgressiveLogged.Add("shadow:" + signal.Key))
                    Main.Log($"[Progressive] would bypass (not active): {signal.Value}");
        }

        public static void ResetCombat() => LastLog.Clear();
    }
}
