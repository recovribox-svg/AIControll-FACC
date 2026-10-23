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

        /// <summary>
        /// Slot level the cast will spend, from the game: Spellbook.GetSpellLevel (base level + metamagic; Completely Normal
        /// Spell can make it 0). A variant (Command, Bestow Curse...) is cast from its parent's slot
        /// (AbilityData.SpendOneSpellCharge spends ConvertedFrom), so its level is the parent's; GetSpellLevel on the
        /// variant itself returns -1. A held touch charge (StickyTouch) has already spent its slot: 0.
        /// </summary>
        public static int EffectiveSlotLevel(AbilityData ability)
        {
            if (ability == null) return 0;
            if (ability.StickyTouch != null) return 0;
            var book = ability.Spellbook;
            try
            {
                int level = book != null ? book.GetSpellLevel(ability.ConvertedFrom ?? ability) : ability.SpellLevel;
                return level >= 0 ? level : Math.Max(0, ability.SpellLevel);
            }
            catch
            {
                return Math.Max(0, ability.SpellLevel);
            }
        }

        /// <summary>
        /// ★ v0.3.8: once per combat and caster, what the policy covers: the in-scope spells with their slot level, and
        /// why the other abilities are left alone. Without it a log could not show that the policy was on at all.
        /// </summary>
        private static void LogLadderOnce(UnitEntityData unit, List<GateItem> items, List<TierOption> options, string mode)
        {
            if (!CombatMemory.ProgressiveLogged.Add("ladder:" + mode + ":" + unit.UniqueId)) return;
            var inScope = new SortedSet<string>();
            var inScopeNames = new HashSet<string>();
            var outOfScope = new SortedDictionary<string, string>();
            for (int i = 0; i < items.Count; i++)
            {
                var ability = items[i].Ability;
                if (ability == null) continue;
                string name = ability.Name;
                if (options[i].InScope && options[i].Level > 0)
                {
                    inScope.Add($"{name} L{options[i].Level} ({options[i].BookName ?? options[i].Book})");
                    inScopeNames.Add(name);
                }
                else
                {
                    string why = ability.SourceItem != null ? "item"
                               : ability.Spellbook == null ? "not a spellbook spell"
                               : !items[i].Offensive ? "not offensive"
                               : "level 0";
                    if (!outOfScope.ContainsKey(name)) outOfScope[name] = why;
                }
            }
            var leftAlone = outOfScope.Where(kv => !inScopeNames.Contains(kv.Key)).Select(kv => $"{kv.Key}: {kv.Value}").ToList();
            // ★ v0.3.19 (release): once per unit until its list changes (it was repeated at the start of every fight).
            string text = $"[Progressive] {unit.CharacterName} ({mode}): policy on - on the spell-level ladder (held back only while a lower level can do the job): " +
                          $"{(inScope.Count > 0 ? string.Join(", ", inScope) : "none")}; left alone: " +
                          $"{(leftAlone.Count > 0 ? string.Join(", ", leftAlone) : "none")}";
            AbilityDiscovery.ReportOnce($"progressive|{unit.UniqueId}|{text.GetHashCode()}", text);
        }

        /// <summary>
        /// Chance the spell works on the enemy it is aimed at; for an area, on the most exposed enemy inside it (0 when no
        /// enemy is inside). Spell resistance × attack roll × failed save (damage: half on a save counts half).
        /// </summary>
        public static float SuccessChance(UnitEntityData caster, List<UnitEntityData> enemies, AbilityData ability,
                                          AbilityProfile profile, UnitEntityData target, Vector3? point)
        {
            if (profile == null) return 1f;
            bool damage = profile.DealsDamage;
            if (profile.IsAoE)
            {
                if (profile.AoETargets == Kingmaker.UnitLogic.Abilities.Components.TargetType.Ally) return 0f;
                Vector3? center = target?.Position ?? point;
                if (!center.HasValue) return 1f;
                float best = 0f;
                foreach (var e in enemies)
                    if (Vector3.Distance(e.Position, center.Value) <= profile.AoERadius + e.Corpulence && profile.AreaReaches(ability, e))
                        best = Math.Max(best, CombatMath.EffectChance(caster, e, ability, profile, damage));
                return best;
            }
            return target != null && caster.IsEnemy(target) ? CombatMath.EffectChance(caster, target, ability, profile, damage) : 1f;
        }

        /// <summary>Options to hold back (their <see cref="GateItem.Source"/>). Never throws: on error nothing is held back.</summary>
        /// <param name="enemiesComplete">The enemy list is the whole fight (turn-based); real-time lists are distance-filtered,
        /// so boss on/off transitions are only logged from turn-based decisions.</param>
        public static HashSet<object> HeldBack(UnitEntityData unit, List<UnitEntityData> enemies, List<GateItem> items, string mode,
                                               bool enemiesComplete)
        {
            var held = new HashSet<object>();
            try
            {
                bool bypassOn = ModSettings.Instance?.ProgressiveBossBypass ?? true;
                var bosses = bypassOn ? enemies.Where(ThreatAssessment.IsBoss).ToList() : new List<UnitEntityData>();
                if (enemiesComplete) LogBossTransitions(enemies, bosses);
                if (LastLog.Count > 512) LastLog.Clear();                         // real-time-only play never resets
                if (CombatMemory.ProgressiveLogged.Count > 4096) CombatMemory.ProgressiveLogged.Clear();

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
                        level = EffectiveSlotLevel(it.Ability);
                        var profile = AbilityProfile.Get(it.Ability);
                        chance = it.SuccessChance ?? SuccessChance(unit, enemies, it.Ability, profile, it.Target, it.Point);
                        bypass = ThreatAssessment.HitsBoss(it.Target, it.Point, profile, bosses);
                    }
                    options.Add(new TierOption
                    {
                        Key = i.ToString(),
                        Label = it.Label,
                        Book = book?.Blueprint != null ? book.Blueprint.AssetGuid.ToString() : null,   // ★ v0.3.12
                        BookName = book?.Blueprint?.name,
                        Level = level,
                        InScope = inScope,
                        Value = it.Value,
                        Threshold = it.Threshold,
                        SuccessChance = chance,
                        Bypass = bypass
                    });
                }

                LogLadderOnce(unit, items, options, mode);

                var decision = SpellTierPolicy.Gate(options);
                foreach (var key in decision.HeldBack)
                    held.Add(items[int.Parse(key)].Source);

                // Logged whenever a tier was chosen over something (held back, escalated past lower levels, or bypassed),
                // and only when that outcome changes (the dedupe key leaves out values, which change every decision).
                bool escalated = decision.Notes.Any(n => n.Contains("lower not viable"));
                string summary = decision.HeldBack.Count == 0 && decision.BypassedLabels.Count == 0 && !escalated ? "" :
                    string.Join("; ", decision.Notes) +
                    (decision.HeldBack.Count > 0
                        ? " - held back: " + string.Join(", ", decision.HeldBack.Select(k => items[int.Parse(k)].Label))
                        : "") +
                    (decision.BypassedLabels.Count > 0 ? " - boss bypass: " + string.Join(", ", decision.BypassedLabels) : "");
                string logKey = unit.UniqueId + "|" + mode;
                string dedupe = string.Join("|", decision.Notes.Select(n => n.Split('(')[0])) + "#" +
                                string.Join("|", decision.HeldBack.Select(k => items[int.Parse(k)].Label).OrderBy(l => l)) + "#" +
                                string.Join("|", decision.BypassedLabels.OrderBy(l => l));
                if (summary.Length > 0 && (!LastLog.TryGetValue(logKey, out var last) || last != dedupe))
                    Main.Log($"[Progressive] {unit.CharacterName} ({mode}): {summary}");
                LastLog[logKey] = summary.Length > 0 ? dedupe : "";
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
                if (!CombatMemory.ActiveBosses.ContainsKey(b.UniqueId))
                {
                    CombatMemory.ActiveBosses[b.UniqueId] = b.CharacterName;
                    Main.Log($"[Progressive] boss bypass ON: {b.CharacterName} (the game marks it as a boss encounter)");
                }
            foreach (var entry in CombatMemory.ActiveBosses.ToList())
            {
                if (bosses.Any(b => b.UniqueId == entry.Key)) continue;
                CombatMemory.ActiveBosses.Remove(entry.Key);
                Main.Log($"[Progressive] boss bypass OFF: {entry.Value} is no longer a valid target");
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
