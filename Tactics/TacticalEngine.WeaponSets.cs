using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Settings;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.Items.Slots;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Commands.Base;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    // ★ v0.3.70 F2 (author request): a character set to "Mixed" range switches between its melee and its ranged weapon set
    // when the other one is clearly better here: the bow when nothing is in reach and walking in costs, the blade when an
    // enemy stands next to it and a shot would provoke. The game's own mob AI does it with BlueprintAiSwitchWeapon (the
    // Cambion's brain: "switch to melee when threatened", nothing back) and the move-action command UnitSwitchHandEquipmentSet;
    // here the engine prices both sets with the game's attack rules (CombatMath.EstimateWithWeapon for the inactive one) and
    // the switch competes with every other option. Both combat modes. Not for Melee/Ranged preferences, Pure Mages, riders,
    // polymorphed units, or within a round of the last switch.
    public static partial class TacticalEngine
    {
        private const int MaxSwitchTargets = 6;
        private static readonly Dictionary<string, int> s_LastSetSwitch = new Dictionary<string, int>(StringComparer.Ordinal);
        private static int s_SetSwitchFight = -1;

        private static void AddWeaponSetCandidate(TacticalContext ctx, List<Candidate> list)
        {
            var unit = ctx.Unit;
            if (ctx.Settings.RangePreference != RangePreference.Mixed || PureMagePolicy.IsActive(ctx.Settings) || !ctx.HasMove ||
                ctx.Permissions.MovementOnly || unit.RiderPart != null || ctx.Enemies.Count == 0) return;
            var body = unit.Body;
            if (body == null || body.IsPolymorphed || body.IsEquipmentSetChangeForbidden || !unit.CanChangeWeaponSet()) return;
            if (s_SetSwitchFight != CombatMemory.Fight) { s_SetSwitchFight = CombatMemory.Fight; s_LastSetSwitch.Clear(); }
            if (s_LastSetSwitch.TryGetValue(unit.UniqueId, out int last) && ctx.Round - last < 2) return;

            int current = body.CurrentHandEquipmentSetIndex;
            bool currentRanged = body.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false;
            int index = -1;
            ItemEntityWeapon weapon = null;
            var sets = body.HandsEquipmentSets;
            for (int i = 0; i < sets.Count; i++)
            {
                if (i == current || sets[i] == null || sets[i].IsEmpty()) continue;
                var w = sets[i].PrimaryHand?.MaybeWeapon ?? sets[i].SecondaryHand?.MaybeWeapon;
                if (w?.Blueprint == null || w.Blueprint.IsRanged == currentRanged || w.Blueprint.IsNatural) continue;
                index = i;
                weapon = w;
                break;
            }
            if (weapon == null)
            {
                // ★ v0.3.70 features review: neither playtest offered a switch; say once a session why it cannot apply.
                // ★ features3 review: not for a creature fighting with natural weapons (it has no weapon sets to speak of),
                // and once per summon kind (each Summoned Dog and Cambion printed its own line).
                if (body.PrimaryHand?.MaybeWeapon?.Blueprint?.IsNatural ?? true) return;
                string who = GameInterface.SummonControl.IsOrdinarySummon(unit) ? unit.Blueprint.AssetGuid.ToString() : unit.UniqueId;
                ReportOnce($"weaponset-none|{who}", () =>
                    $"[WeaponSet] {unit.CharacterName}: Mixed range, but no other weapon set holds a " +
                    $"{(currentRanged ? "melee" : "ranged")} weapon ({sets.Count} set(s), current {current + 1}: " +
                    $"{body.PrimaryHand?.MaybeWeapon?.Name ?? "empty"}) - no switch to offer");
                return;
            }

            bool realTime = CombatMemory.IsRealTime;
            // What the current weapon can do: from here, and at best (a walk to the enemy included).
            float currentBest = 0f, currentHere = 0f;
            foreach (var c in list)
            {
                if (c.Kind != CandidateKind.WeaponAttack || !Affordable(ctx, c)) continue;
                currentBest = Math.Max(currentBest, c.Value);
                if (!c.NeedsMove && !c.MoveFirst) currentHere = Math.Max(currentHere, c.Value);
            }

            Vector3 eye = unit.EyePosition - unit.Position;
            float altFull = 0f, altNow = 0f;
            UnitEntityData best = null;
            var enemies = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e))
                                     .OrderBy(e => HorizontalDistance(unit.Position, e.Position)).Take(MaxSwitchTargets);
            foreach (var enemy in enemies)
            {
                var full = CombatMath.EstimateWithWeapon(unit, enemy, weapon, full: true);
                if (full == null) continue;
                bool inReach = weapon.Blueprint.IsRanged
                    ? UnitCommand.IsUnitCloseEnough(enemy.Position, unit.Position, unit.Position + eye, full.Reach, true, 0f)
                    : InRangeNow(unit, enemy.Position, full.Reach, true);
                if (!inReach) continue;
                float roleWeight = RoleWeight(ctx, "damage");
                float wake = WakeFactor(enemy, full.ExpectedDamage);
                float fullValue = DamageValue(enemy, full.ExpectedDamage, full.AnyHitChance) * roleWeight * wake;
                float nowValue = 0f;
                if (!realTime)
                {
                    var single = CombatMath.EstimateWithWeapon(unit, enemy, weapon, full: false);
                    if (single != null) nowValue = DamageValue(enemy, single.ExpectedDamage, single.AnyHitChance) * roleWeight * wake;
                }
                // A shot from melee provokes (ranged attack rolls), the switch included: priced as for the bow already in hand.
                if (weapon.Blueprint.IsRanged)
                {
                    var provoke = AttackRollProvokeRisk(ctx, unit.Position, full.Attacks);
                    if (provoke != null)
                    {
                        fullValue = fullValue * (1f - provoke.StopChance) - provoke.Cost;
                        var once = AttackRollProvokeRisk(ctx, unit.Position, 1);
                        if (once != null) nowValue = nowValue * (1f - once.StopChance) - once.Cost;
                    }
                }
                if (fullValue > altFull) { altFull = fullValue; best = enemy; }
                altNow = Math.Max(altNow, nowValue);
            }
            if (best == null) return;
            float rounds = ValueModel.Horizon(ctx.ExpectedRounds - 1f, 2f);
            float value = ValueModel.WeaponSetSwitchWorth(altNow, altFull, currentBest, rounds);
            if (value < MinQuickValue || value <= currentHere) return;
            list.Add(new Candidate
            {
                // No target and not offensive: the switch itself touches nobody (no cast watch, no "attacks from here").
                Kind = CandidateKind.WeaponSet, Slot = ActionSlot.Move, WeaponSetIndex = index, Value = value,
                Label = $"Switch to {(weapon.Blueprint.IsRanged ? "ranged" : "melee")} weapon set {index + 1} ({weapon.Name}) for " +
                        $"{best.CharacterName} ({altFull:0.#}/rd against {currentBest:0.#} with {body.PrimaryHand?.MaybeWeapon?.Name ?? "the current set"})",
                Key = $"weaponset:{index}"
            });
        }

        /// <summary>The switch still applies: the same set to switch to, not empty, and the game allows a change now.</summary>
        private static bool ValidateWeaponSetIssue(UnitEntityData unit, Candidate c, out string failure)
        {
            failure = null;
            try
            {
                var body = unit.Body;
                if (c.WeaponSetIndex < 0 || c.WeaponSetIndex >= body.HandsEquipmentSets.Count || c.WeaponSetIndex == body.CurrentHandEquipmentSetIndex ||
                    body.HandsEquipmentSets[c.WeaponSetIndex].IsEmpty() || body.IsPolymorphed || body.IsEquipmentSetChangeForbidden ||
                    !unit.CanChangeWeaponSet())
                {
                    failure = "weapon set no longer available to switch to";
                    return false;
                }
                if (s_SetSwitchFight != CombatMemory.Fight) { s_SetSwitchFight = CombatMemory.Fight; s_LastSetSwitch.Clear(); }
                if (s_LastSetSwitch.Count < 256) s_LastSetSwitch[unit.UniqueId] = CombatMemory.Round;
                return true;
            }
            catch (Exception ex)
            {
                failure = "weapon set unreadable: " + ex.Message;
                return false;
            }
        }
    }
}
