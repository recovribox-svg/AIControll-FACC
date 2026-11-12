using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        /// <summary>Passive C06 playtest evidence. Never refresh a dynamic tactical cache, calculate a rule, select a move or
        /// create a part here. Run on the existing once-per-round enemy assessment; log only changed signatures.</summary>
        internal static void NoteSwarmCoverage(UnitEntityData unit)
        {
            if (unit == null) return;
            try
            {
                bool marker = false;
                Kingmaker.Utility.UnitDescription.UnitDescriptionHelper.VisitComponents<SwarmDamageResistance>(unit,(c,f)=>marker=true);
                if (!marker) return;
                var facts = unit.Facts.List;
                var raw = new List<string>();
                foreach (var fact in facts)
                {
                    if (fact?.Active != true || fact.Blueprint?.ComponentsArray == null) continue;
                    foreach (var component in fact.Blueprint.ComponentsArray)
                        if (component is AddFactContextActions round && HasSwarmAttack(round.NewRound,0)) raw.Add(fact.Blueprint.name);
                }
                int cached = s_SwarmStates.TryGetValue(unit,out var state) ? state.Facts.Rounds.Count : -1;
                int owned = 0, recognized = 0, viewless = 0;
                var areas = Game.Instance?.State?.AreaEffects;
                if (areas != null)
                    foreach (var area in areas)
                    {
                        if (area == null || !area.IsInGame || area.IsEnded || area.Context?.MaybeCaster != unit) continue;
                        owned++;
                        if (IsSwarmTargetArea(area.Blueprint)) recognized++;
                        if (area.View is null) viewless++;
                    }
                bool selected = s_SwarmAreas.ContainsKey(unit), current = s_SwarmFrame == Time.frameCount;
                string delivery = selected && s_SwarmAreas[unit].Cast != null ? "target-centred cast" : selected ? "registered aura" : "unread";
                string signature = $"swarm-audit|{unit.UniqueId}|{raw.Count}|{cached}|{owned}|{recognized}|{viewless}|{selected}|{current}|{delivery}";
                if (!CombatMemory.FirstThisFight(signature)) return;
                // Blueprint-only delivery hints, without parameter calculation or availability checks.
                var casts = new List<string>();
                foreach (var ability in unit.Abilities.Enumerable)
                {
                    var bp = ability?.Blueprint;
                    var around = bp?.GetComponent<AbilityTargetsAround>();
                    if (around != null) casts.Add(bp.name + ":AbilityTargetsAround");
                    if (casts.Count == 3) break; // Diagnostic display bound only; no tactical search is changed.
                }
                Main.Log($"[SwarmAudit] {unit.CharacterName}: active round facts={raw.Count} ({string.Join(",",raw.Take(3))}); " +
                    $"cached round sources={cached}; own live areas={owned}, registered-target areas={recognized}, viewless={viewless}; " +
                    $"selected cached space={selected} ({delivery}), area cache current={current}; nearby-cast hints={string.Join(",",casts)}. " +
                    "Passive coverage evidence.");
            }
            catch (Exception ex)
            {
                if (CombatMemory.FirstThisFight("swarm-audit-error|"+unit.UniqueId))
                    Main.Verbose($"[SwarmAudit] {unit.CharacterName}: diagnostic unavailable ({ex.GetType().Name})");
            }
        }
    }
}
