// ★ v0.3.30 competence3 (author's play-test note): when the only enemies left in the fight are hidden by invisibility, an ally
// who can aim a revealing area (an area whose buff makes the creatures in it immune to the Invisible condition: Faerie Fire,
// Glitterdust - AddConditionImmunity Invisible) casts it where the party saw them vanish (HiddenEnemies). The game then ends
// their stealth on its next stealth tick (UnitStealthController.ShouldBeInStealth needs the Invisible condition) and the
// party can target them again. Otherwise the turn ended "no reachable enemies" for every ally, round after round.
// Rules from the game: AddConditionImmunity -> UnitState.HasCondition is false while immune; the area's point is checked
// by AbilityData.CanTarget and the game's reach test (UnitCommand.IsUnitCloseEnough, with sight) from where the caster stands,
// so the reveal never walks. Spot choice and source order: HiddenRules (unit-tested).
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.Utility;
using CompanionAI_Pathfinder.Analysis;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>Buff → it makes its bearer immune to the Invisible condition (read once per blueprint).</summary>
        private static readonly Dictionary<BlueprintBuff, bool> s_RevealingBuffs = new Dictionary<BlueprintBuff, bool>();

        private static bool RevealingBuff(BlueprintBuff buff)
        {
            if (buff == null) return false;
            if (s_RevealingBuffs.TryGetValue(buff, out bool known)) return known;
            bool reveals = false;
            try
            {
                foreach (var component in buff.ComponentsArray ?? Array.Empty<Kingmaker.Blueprints.BlueprintComponent>())
                    if (component is AddConditionImmunity immune && immune.Condition == UnitCondition.Invisible) { reveals = true; break; }
            }
            catch (Exception ex) { Main.Verbose($"[Hidden] {buff.name}: {ex.Message}"); }
            return s_RevealingBuffs[buff] = reveals;
        }

        /// <summary>An area aimed at a point that puts a revealing buff on the enemies in it.</summary>
        private static bool RevealsInArea(AbilityData ability, AbilityProfile profile) =>
            ability.CanTargetPoint && profile != null && profile.IsAoE && !profile.IsProjectileCone &&
            profile.AoETargets != Kingmaker.UnitLogic.Abilities.Components.TargetType.Ally &&
            profile.TargetBuffs.Any(RevealingBuff);

        /// <summary>
        /// The revealing cast for this decision, or null. Only while no enemy can be seen (the only thing left to do is to find
        /// the hidden ones), from where the unit stands, at the spot where the party saw hidden enemies vanish that no
        /// revealing area was aimed at yet; the spot that holds the most of them, by the cheapest source.
        /// </summary>
        private static Candidate RevealHidden(TacticalContext ctx)
        {
            var spots = new List<KeyValuePair<UnitEntityData, Vector3>>();
            double newest = double.MaxValue;
            foreach (var enemy in ctx.HiddenEnemies)
            {
                if (!HiddenEnemies.UntriedSpot(enemy, out var at, out double ago)) continue;
                spots.Add(new KeyValuePair<UnitEntityData, Vector3>(enemy, at));
                newest = Math.Min(newest, ago);
            }
            if (spots.Count == 0) return null;

            Candidate best = null;
            float bestCost = float.MaxValue;
            int bestCovered = 0;
            var abilities = UserAbilityPolicy.Allowed(ctx.Blocks, ctx.Unit, AbilityDiscovery.Gather(ctx.Unit));
            foreach (var ability in abilities)
            {
                try
                {
                    if (ability == null || !ability.IsAvailable) continue;
                    if (AbilityClassifier.IsBlacklistedForCombat(ability, valuedByEffect: true)) continue;
                    var slot = SlotOf(ability);
                    bool affordable = slot == ActionSlot.Free || slot == ActionSlot.Swift && ctx.HasSwift ||
                        slot == ActionSlot.Move && (ctx.HasMove || ctx.HasStandard) ||
                        slot == ActionSlot.Standard && ctx.HasStandard || slot == ActionSlot.FullRound && ctx.HasFullRound;
                    if (!affordable) continue;
                    var profile = AbilityProfile.Get(ability);
                    if (!RevealsInArea(ability, profile)) continue;
                    if (ability.SourceItem != null && !ConsumablePolicy.IsAllowed(ability)) continue;

                    int index = HiddenRules.CoveringSpot(spots.Select(s => s.Value.x).ToList(), spots.Select(s => s.Value.z).ToList(),
                                                         profile.AoERadius, out int covered);
                    if (index < 0) continue;
                    var point = spots[index].Value;
                    if (!ability.CanTarget(new TargetWrapper(point))) continue;
                    if (!InRangeNow(ctx.Unit, point, ability.GetApproachDistance(), needLos: true)) continue;
                    int level;
                    try { level = ability.Spellbook?.GetSpellLevel(ability) ?? ability.SpellLevel; } catch { level = ability.SpellLevel; }
                    float cost = HiddenRules.SourceCost(CostsNothing(ability), ability.SourceItem != null, IsSpentItem(ability), level);
                    if (covered < bestCovered || covered == bestCovered && cost >= bestCost) continue;
                    float radius = profile.AoERadius;
                    var revealed = spots.Where(s => Horizontal(s.Value, point) <= radius).Select(s => s.Key).ToList();
                    bestCovered = covered;
                    bestCost = cost;
                    best = new Candidate
                    {
                        Kind = CandidateKind.Ability, Slot = slot, Ability = ability, Point = point, Value = covered,
                        Offensive = true, Key = $"reveal|{CombatMemory.AbilityKey(ability)}",
                        Reveals = revealed,
                        Label = $"{(ability.SourceItem != null ? "Item:" : "")}{ability.Name}->reveal " +
                                $"{string.Join(", ", revealed.Select(e => e.CharacterName))} (vanished there {newest:F0} s ago)",
                    };
                }
                catch (Exception ex)
                {
                    ReportOnce($"reveal|{ability?.Blueprint?.AssetGuid}", $"[Hidden] {ability?.Name}: not considered - {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (best == null)
                ReportOnce($"no-reveal|{CombatMemory.Fight}|{ctx.Unit.UniqueId}|{string.Join(",", spots.Select(s => s.Key.UniqueId))}",
                           () => $"[Hidden] {ctx.Unit.CharacterName}: {string.Join(", ", spots.Select(s => s.Key.CharacterName))} " +
                                 "hidden by invisibility - no revealing area (Faerie Fire, Glitterdust) usable from here now");
            return best;
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
