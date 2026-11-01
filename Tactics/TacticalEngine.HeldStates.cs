// ★ v0.3.24 (Psionics mod feedback, docs/TACTICAL_AI.md §81): spending a held state.
//
//   Psionics' Psychic Warrior keeps psionic focus (PsionicFocusBuff, permanent) and, while focused, a trance (a stance
//   toggle that requires the focus: RestrictionHasFact). The focus buff's AddBuffActions end the trance and the
//   focus-only feat buffs (Psionic Dodge) when it goes; the Psionic Weapon / Shot / Fist feats deal their extra dice only
//   while it is held. A maneuver (and Wounding Attack, Psychic Strike's recharge, ...) is a swift action that removes the
//   focus for a 1-round buff. The engine read only the 1-round buff: a removed own buff was compared by its per-round
//   quality, and the focus buff itself has none, so spending it looked free ("the AI loves to constantly waste the focus
//   for little gain").
//
//   A held state is a lasting good buff on the caster that the ability takes off without putting a lasting effect in its
//   place (not a mode switch: those keep the quality comparison), and that something else of the unit depends on - its
//   removal ends other buffs the unit has, or another ability or toggle of the unit requires it. Spending it costs what
//   getting it back costs: the unit's own ability that re-applies it, and the commands of the toggles that ended with it
//   (one round of the unit's worth per standard action, ValueModel.PerRound; a move action half, a swift a quarter) - or,
//   with no way back, one action's worth - plus the readable worth of what ends with it, for the rest of the fight after
//   the gained effect. Nothing is charged for the part of the fight the gained effect itself covers: as the last action of
//   a fight a burst costs little. Recognised by structure, never by name.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components.CasterCheckers;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.ActivatableAbilities.Restrictions;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>What a unit's held state is to it: what ends with it, whether it is required, what restores it.</summary>
        private sealed class HeldStateInfo
        {
            public bool Required;
            public List<BlueprintBuff> Cascade = new List<BlueprintBuff>();
            public float RestoreActions;     // in standard actions
            public string RestoredBy;
        }

        /// <summary>Per unit and buff, for the fight (the unit's abilities and toggles do not change during it).</summary>
        private static readonly Dictionary<string, HeldStateInfo> s_HeldStates = new Dictionary<string, HeldStateInfo>();

        internal static void ResetHeldStates() => s_HeldStates.Clear();

        /// <summary>
        /// The cost of the held states <paramref name="ability"/> spends (none for most abilities: one list check), with
        /// its decision-log reason.
        /// </summary>
        private static float SpentStateCost(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                            out string note)
        {
            note = null;
            if (profile.HasLongBuff || profile.RemovedBuffs.Count == 0 && profile.CasterRemovedBuffs.Count == 0) return 0f;
            var unit = ctx.Unit;
            float cost = 0f;
            var names = new List<string>();
            try
            {
                foreach (var buff in SpentStates(ctx, ability, profile, target))
                {
                    var info = HeldState(unit, buff.Blueprint, ability);
                    float gainRounds = BuffRounds(profile, ctx.ExpectedRounds);
                    float after = Math.Max(0f, ctx.ExpectedRounds - gainRounds);
                    var ending = new List<Buff> { buff };
                    ending.AddRange(info.Cascade.Select(b => unit.Buffs.GetBuff(b)).Where(b => b != null));
                    float perRound = ending.Sum(b => BuffEffectAnalyzer.GetCombatValue(b.Blueprint)) *
                                     ValueModel.BuffPerRound(Math.Max(1, Level(unit) / 2));
                    float restore = info.RestoreActions +
                                    ending.Skip(1).Sum(b => ToggleRestoreActions(unit, b.Blueprint));
                    float part = ValueModel.SpentStateCost(after, restore, perRound, ValueModel.PerRound(Level(unit)));
                    if (part <= 0f) continue;
                    cost += part;
                    names.Add(buff.Blueprint.Name + (ending.Count > 1 ? $" (ends {string.Join(", ", ending.Skip(1).Select(b => b.Blueprint.Name))})" : ""));
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Held] {unit.CharacterName} {ability.Name}: {ex.Message}");
                return 0f;
            }
            if (cost > 0f) note = $"spends {string.Join(", ", names)} -{cost:0.#}";
            return cost;
        }

        /// <summary>The held states among the caster's buffs this ability removes (see the file header).</summary>
        private static List<Buff> SpentStates(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            var unit = ctx.Unit;
            var removed = new List<Buff>();
            if (target == unit) removed.AddRange(profile.BeneficialBuffsRemoved(ability, unit));
            foreach (var blueprint in profile.CasterRemovedBuffs)
            {
                var buff = unit.Buffs.GetBuff(blueprint);
                if (buff != null && !removed.Contains(buff) && !(buff.Blueprint.Harmful)) removed.Add(buff);
            }
            return removed.Where(b => IsHeldState(ctx, ability, profile, b)).ToList();
        }

        private static bool IsHeldState(TacticalContext ctx, AbilityData ability, AbilityProfile profile, Buff buff)
        {
            if (profile.HasLongBuff || buff?.Blueprint == null) return false;
            // The ability puts the same buff back (a refresh), or its own effect is that buff.
            if (profile.TargetBuffs.Contains(buff.Blueprint) || profile.CasterBuffs.Contains(buff.Blueprint)) return false;
            bool lasting = buff.IsPermanent || buff.TimeLeft.TotalSeconds >= 6f * Math.Max(1f, ctx.ExpectedRounds);
            if (!lasting) return false;
            var info = HeldState(ctx.Unit, buff.Blueprint, ability);
            return info.Required || info.Cascade.Any(b => ctx.Unit.Buffs.GetBuff(b) != null);
        }

        private static HeldStateInfo HeldState(UnitEntityData unit, BlueprintBuff blueprint, AbilityData spender)
        {
            string key = unit.UniqueId + "|" + blueprint.AssetGuid;
            if (s_HeldStates.TryGetValue(key, out var known)) return known;
            var info = new HeldStateInfo();
            try
            {
                // What ends with it: the buff's own AddBuffActions, Deactivated (nested Conditionals included).
                foreach (var actions in blueprint.GetComponents<AddFactContextActions>())
                    CollectRemovals(actions.Deactivated, info.Cascade, 0);
                info.Cascade.Remove(blueprint);
                // Who requires it: another ability (AbilityCasterHasFacts) or a toggle (RestrictionHasFact).
                info.Required =
                    unit.Abilities.Enumerable.Any(a => a.Blueprint != spender.Blueprint &&
                        a.Blueprint.GetComponents<AbilityCasterHasFacts>().Any(c => c.Facts.Contains(blueprint))) ||
                    unit.ActivatableAbilities.Enumerable.Any(a =>
                        a.Blueprint.GetComponents<RestrictionHasFact>().Any(r => !r.Not && r.Feature == blueprint));
                // What restores it: the unit's own ability that puts it on the unit, the cheapest by its action.
                info.RestoreActions = 1f;   // no way back this fight: one action's worth
                foreach (var a in unit.Abilities.Enumerable)
                {
                    if (a.Blueprint == spender.Blueprint) continue;
                    var p = AbilityProfile.Get(a.Data);
                    if (p == null || !(p.TargetBuffs.Contains(blueprint) || p.CasterBuffs.Contains(blueprint))) continue;
                    float actions = ActionsOf(a.Data.RuntimeActionType, a.Data.RequireFullRoundAction);
                    if (info.RestoredBy == null || actions < info.RestoreActions)
                    {
                        info.RestoreActions = actions;
                        info.RestoredBy = a.Blueprint.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Held] {unit.CharacterName} {blueprint.name}: {ex.Message}");
            }
            if (s_HeldStates.Count > 512) s_HeldStates.Clear();
            s_HeldStates[key] = info;
            return info;
        }

        private static void CollectRemovals(ActionList list, List<BlueprintBuff> into, int depth)
        {
            if (list?.Actions == null || depth > 3) return;
            foreach (var action in list.Actions)
            {
                switch (action)
                {
                    case ContextActionRemoveBuff remove when remove.Buff != null && !remove.ToCaster:
                        if (!into.Contains(remove.Buff)) into.Add(remove.Buff);
                        break;
                    case Conditional conditional:
                        CollectRemovals(conditional.IfTrue, into, depth + 1);
                        CollectRemovals(conditional.IfFalse, into, depth + 1);
                        break;
                }
            }
        }

        /// <summary>A toggle whose buff ended with the state: the command that switches it on again, in standard actions.</summary>
        private static float ToggleRestoreActions(UnitEntityData unit, BlueprintBuff buff)
        {
            try
            {
                var toggle = unit.ActivatableAbilities.Enumerable.FirstOrDefault(a => a.Blueprint.Buff == buff);
                if (toggle == null) return 0f;   // comes back with the state (Psionic Dodge's buff)
                return toggle.Blueprint.ActivationType == AbilityActivationType.WithUnitCommand
                    ? ActionsOf(UnitActivateAbility.GetCommandType(toggle), false) : 0.1f;
            }
            catch { return 0f; }
        }

        private static float ActionsOf(UnitCommand.CommandType type, bool fullRound)
        {
            if (fullRound) return 2f;
            switch (type)
            {
                case UnitCommand.CommandType.Free: return 0.1f;
                case UnitCommand.CommandType.Swift: return 0.25f;
                case UnitCommand.CommandType.Move: return 0.5f;
                default: return 1f;
            }
        }
    }
}
