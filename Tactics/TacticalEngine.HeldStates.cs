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
using Kingmaker.Blueprints.Facts;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.UnitLogic;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Settings;

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

        internal static void ResetHeldStates()
        {
            s_HeldStates.Clear();
            s_HeldDependence.Clear();
        }

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

        #region Getting a held state back (★ v0.3.25)

        /// <summary>What depends on a marker state for one unit (see the file header).</summary>
        private sealed class HeldDependence
        {
            public int FactCount;
            public readonly List<BlueprintBuff> Comes = new List<BlueprintBuff>();
            public readonly List<string> Dependents = new List<string>();
            public readonly List<ActivatableAbility> Toggles = new List<ActivatableAbility>();
            public bool RequiredByAbility;
            /// <summary>Something of the unit depends on it. What only comes with it (a wrapper buff's child) is not enough:
            /// such a buff stays with the usual buff appraisal, which reads its children.</summary>
            public bool Any => Dependents.Count > 0 || Toggles.Count > 0 || RequiredByAbility;
        }

        /// <summary>Per unit and state; rebuilt when the unit's facts change (a level-up between fights).</summary>
        private static readonly Dictionary<string, HeldDependence> s_HeldDependence = new Dictionary<string, HeldDependence>();
        private static readonly Dictionary<BlueprintBuff, bool> s_MarkerStates = new Dictionary<BlueprintBuff, bool>();
        private static readonly Dictionary<Type, System.Reflection.FieldInfo[]> s_ElementFields = new Dictionary<Type, System.Reflection.FieldInfo[]>();

        /// <summary>"unit id" → toggles (blueprint GUIDs) the AI's spending ended, on before the AI first switched them.</summary>
        private static readonly Dictionary<string, HashSet<string>> s_EndedByAi = new Dictionary<string, HashSet<string>>();

        /// <summary>A buff whose only components are its start and end actions: it does nothing but carry what depends on it.</summary>
        private static bool IsMarkerState(BlueprintBuff buff)
        {
            if (buff == null) return false;
            if (s_MarkerStates.TryGetValue(buff, out bool known)) return known;
            bool marker = false;
            try { marker = buff.ComponentsArray.Length > 0 && buff.ComponentsArray.All(c => c is AddFactContextActions); } catch { }
            s_MarkerStates[buff] = marker;
            return marker;
        }

        private static HeldDependence Dependence(UnitEntityData unit, BlueprintBuff state)
        {
            int facts = 0;
            try { facts = unit.Facts.List.Count; } catch { }
            string key = unit.UniqueId + "|" + state.AssetGuid;
            if (s_HeldDependence.TryGetValue(key, out var known) && known.FactCount == facts) return known;
            var d = new HeldDependence { FactCount = facts };
            try
            {
                // What comes with it: its start actions' buffs, the conditions read on the unit itself.
                foreach (var actions in state.GetComponents<AddFactContextActions>())
                    CollectComing(unit, actions.Activated, d.Comes, 0);
                d.Comes.Remove(state);
                // What works only while it is held: a feature asking for it before it acts. A feature's own start actions
                // are left out: what they give comes with the state's start actions (Psionic Dodge has both).
                foreach (var feature in unit.Progression.Features.Enumerable)
                {
                    var bp = feature?.Blueprint;
                    if (bp == null) continue;
                    if (bp.ComponentsArray.Any(c => c != null && !(c is AddFactContextActions) && AsksFor(c, state, 0)))
                        d.Dependents.Add(bp.Name);
                }
                foreach (var toggle in unit.ActivatableAbilities.Enumerable)
                    if (toggle?.Blueprint != null && toggle.Blueprint.GetComponents<RestrictionHasFact>().Any(r => !r.Not && r.Feature == state))
                        d.Toggles.Add(toggle);
                d.RequiredByAbility = unit.Abilities.Enumerable.Any(a => a?.Blueprint != null &&
                    a.Blueprint.GetComponents<AbilityCasterHasFacts>().Any(c => c.Facts.Contains(state)));
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Held] {unit.CharacterName} {state.name} dependence: {ex.Message}");
            }
            if (s_HeldDependence.Count > 512) s_HeldDependence.Clear();
            s_HeldDependence[key] = d;
            return d;
        }

        private static void CollectComing(UnitEntityData unit, ActionList list, List<BlueprintBuff> into, int depth)
        {
            if (list?.Actions == null || depth > 3) return;
            foreach (var action in list.Actions)
            {
                switch (action)
                {
                    case ContextActionApplyBuff apply when apply.Buff != null && !apply.ToCaster:
                        if (!into.Contains(apply.Buff)) into.Add(apply.Buff);
                        break;
                    case Conditional conditional:
                        bool? holds = HoldsOnUnit(unit, conditional.ConditionsChecker);
                        if (holds == true) CollectComing(unit, conditional.IfTrue, into, depth + 1);
                        else if (holds == false) CollectComing(unit, conditional.IfFalse, into, depth + 1);
                        break;
                }
            }
        }

        /// <summary>
        /// Whether "has fact" conditions hold for the unit - a state's own context is the unit's (caster and target alike);
        /// null when another kind of condition decides.
        /// </summary>
        private static bool? HoldsOnUnit(UnitEntityData unit, ConditionsChecker checker)
        {
            var conditions = checker?.Conditions;
            if (conditions == null || conditions.Length == 0) return true;
            bool and = checker.Operation == Operation.And;
            bool result = and;
            foreach (var c in conditions)
            {
                BlueprintUnitFact fact = c is ContextConditionCasterHasFact caster ? caster.Fact
                                       : c is ContextConditionHasFact has ? has.Fact : null;
                if (fact == null) return null;
                bool holds = unit.Descriptor.HasFact(fact) != c.Not;
                result = and ? result && holds : result || holds;
            }
            return result;
        }

        /// <summary>Whether an element tree (a component, its actions and conditions) asks "has <paramref name="state"/>".</summary>
        private static bool AsksFor(object node, BlueprintBuff state, int depth)
        {
            if (node == null || depth > 6) return false;
            switch (node)
            {
                case ContextConditionCasterHasFact caster: return !caster.Not && caster.Fact == state;
                case ContextConditionHasFact has: return !has.Not && has.Fact == state;
                case ActionList list: return list.Actions != null && list.Actions.Any(a => AsksFor(a, state, depth + 1));
                case ConditionsChecker checker: return checker.Conditions != null && checker.Conditions.Any(c => AsksFor(c, state, depth + 1));
                case Array array:
                    foreach (var element in array)
                        if (AsksFor(element, state, depth + 1)) return true;
                    return false;
            }
            foreach (var field in ElementFields(node.GetType()))
                if (AsksFor(field.GetValue(node), state, depth + 1)) return true;
            return false;
        }

        /// <summary>A type's fields that hold actions or conditions (cached per type).</summary>
        private static System.Reflection.FieldInfo[] ElementFields(Type type)
        {
            if (s_ElementFields.TryGetValue(type, out var known)) return known;
            var fields = new List<System.Reflection.FieldInfo>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var field in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                                  System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    var ft = field.FieldType;
                    var element = ft.IsArray ? ft.GetElementType() : ft;
                    if (element == typeof(ActionList) || element == typeof(ConditionsChecker) ||
                        typeof(GameAction).IsAssignableFrom(element) || typeof(Condition).IsAssignableFrom(element))
                        fields.Add(field);
                }
            var result = fields.ToArray();
            s_ElementFields[type] = result;
            return result;
        }

        /// <summary>Whether any of <paramref name="buffs"/> is a lasting marker state (the cheap first check: no allocation).</summary>
        private static bool AppliesMarkerState(AbilityProfile profile, List<BlueprintBuff> buffs)
        {
            foreach (var b in buffs)
                if (profile.IsPersistent(b) && IsMarkerState(b)) return true;
            return false;
        }

        /// <summary>A lasting marker state the ability puts on the unit, that it lacks and something of the unit depends on.</summary>
        private static bool RegainsHeldState(UnitEntityData unit, AbilityProfile profile, BlueprintBuff buff) =>
            buff != null && !profile.IsToggle && profile.IsPersistent(buff) && IsMarkerState(buff) && !unit.Buffs.HasFact(buff) &&
            Dependence(unit, buff).Any;

        /// <summary>Out of combat: whether the after-fight routine should put this state back on the unit.</summary>
        internal static bool RestoresHeldState(UnitEntityData unit, AbilityProfile profile, BlueprintBuff buff)
        {
            try { return unit != null && profile != null && RegainsHeldState(unit, profile, buff); }
            catch { return false; }
        }

        /// <summary>
        /// What an ability that puts a lost held state back on its caster is worth now (Gain Psionic Focus), with its
        /// decision-log reason. The state counts from the next action on: a standard action leaves this round without it.
        /// </summary>
        private static float RegainValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                         ActionSlot slot, out string note)
        {
            note = null;
            var unit = ctx.Unit;
            if (ability.SourceItem != null || !AppliesMarkerState(profile, profile.CasterBuffs) &&
                (target != unit || !AppliesMarkerState(profile, profile.TargetBuffs)) || profile.IsToggle) return 0f;
            float total = 0f;
            var names = new List<string>();
            try
            {
                var states = profile.CasterBuffs.Concat(target == unit ? profile.TargetBuffs : Enumerable.Empty<BlueprintBuff>()).Distinct()
                    .Where(b => profile.BuffAppliesTo(b, ability, unit) != EffectApplies.No && RegainsHeldState(unit, profile, b)).ToList();
                if (states.Count == 0) return 0f;
                float rounds = Math.Max(0f, ctx.ExpectedRounds - (slot == ActionSlot.Standard || slot == ActionSlot.FullRound ? 1f : 0f));
                float unitRound = ValueModel.PerRound(Level(unit));
                float scale = ValueModel.BuffPerRound(Math.Max(1, Level(unit) / 2));
                foreach (var state in states)
                {
                    var d = Dependence(unit, state);
                    float readable = d.Comes.Where(b => !unit.Buffs.HasFact(b)).Sum(b => BuffEffectAnalyzer.GetCombatValue(b)) * scale;
                    float togglePerRound = 0f, toggleRounds = 0f;
                    ActivatableAbility best = null;
                    foreach (var toggle in d.Toggles)
                    {
                        if (toggle.IsOn || toggle.Blueprint.Buff == null || ToggleExclusion(unit, toggle) != null ||
                            CombatMemory.PlayerOwnsToggle(unit, toggle)) continue;
                        float worth = BuffEffectAnalyzer.GetCombatValue(toggle.Blueprint.Buff) * scale;
                        if (worth <= togglePerRound) continue;
                        togglePerRound = worth;
                        toggleRounds = ToggleRestoreActions(unit, toggle.Blueprint.Buff);
                        best = toggle;
                    }
                    float part = ValueModel.HeldStateWorth(rounds, readable, d.Dependents.Count, unitRound, togglePerRound, toggleRounds);
                    if (part <= 0f) continue;
                    total += part;
                    var what = new List<string>();
                    if (d.Comes.Count > 0) what.Add(string.Join(", ", d.Comes.Select(b => b.Name)));
                    if (d.Dependents.Count > 0) what.Add($"{d.Dependents.Count} feat{(d.Dependents.Count > 1 ? "s" : "")}");
                    if (best != null) what.Add(best.Name);
                    names.Add($"{state.Name} {part:0.#}" + (what.Count > 0 ? $" ({string.Join("; ", what)})" : ""));
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Held] {unit.CharacterName} {ability.Name} regain: {ex.Message}");
                return 0f;
            }
            if (total > 0f) note = $"regains {string.Join(", ", names)}";
            return total;
        }

        /// <summary>
        /// When the AI issues an ability that ends a toggle with a state it spends (a trance with the focus): that is the
        /// AI's switch (no second switch this round; the state before the fight is "on"), and, when the toggle was on before
        /// the AI first switched it, it goes back on after the fight once its state is back.
        /// </summary>
        internal static void NoteEndedByAi(UnitEntityData unit, AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (unit == null || profile == null || profile.HasLongBuff) return;
            if (profile.CasterRemovedBuffs.Count == 0 && (target != unit || profile.RemovedBuffs.Count == 0)) return;
            try
            {
                var removed = profile.CasterRemovedBuffs.Concat(target == unit ? profile.RemovedBuffs : Enumerable.Empty<BlueprintBuff>()).Distinct();
                foreach (var blueprint in removed)
                {
                    if (unit.Buffs.GetBuff(blueprint) == null) continue;
                    var info = HeldState(unit, blueprint, ability);
                    if (info.Cascade.Count == 0) continue;
                    foreach (var toggle in unit.ActivatableAbilities.Enumerable)
                    {
                        if (toggle?.Blueprint?.Buff == null || !toggle.IsOn || !info.Cascade.Contains(toggle.Blueprint.Buff)) continue;
                        CombatMemory.RecordToggle(unit, toggle, false, true);
                        if (!CombatMemory.WasOnBeforeAi(unit, toggle)) continue;
                        if (!s_EndedByAi.TryGetValue(unit.UniqueId, out var guids)) s_EndedByAi[unit.UniqueId] = guids = new HashSet<string>();
                        guids.Add(toggle.Blueprint.AssetGuid.ToString());
                        Main.Log($"[Held] {unit.CharacterName}: {ability.Name} ends {toggle.Name} with {blueprint.Name} - " +
                                 $"switched on again once {blueprint.Name} is back");
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Held] {unit.CharacterName} {ability.Name} ended toggles: {ex.Message}");
            }
        }

        internal static bool HasEndedToggles(UnitEntityData unit) =>
            unit != null && s_EndedByAi.TryGetValue(unit.UniqueId, out var guids) && guids.Count > 0;

        /// <summary>
        /// Out of combat: the first toggle the AI's spending ended (NoteEndedByAi) whose state is back goes on again, as the
        /// action bar does it; true when one was switched on. One the player or the fight switched on already is forgotten.
        /// </summary>
        internal static bool RestoreEndedToggles(UnitEntityData unit)
        {
            if (!(ModSettings.Instance?.AiManagesToggles ?? true) || !HasEndedToggles(unit)) return false;
            var guids = s_EndedByAi[unit.UniqueId];
            try
            {
                foreach (var toggle in unit.ActivatableAbilities.Enumerable.ToList())
                {
                    string guid = toggle?.Blueprint?.AssetGuid.ToString();
                    if (guid == null || !guids.Contains(guid)) continue;
                    if (toggle.IsOn) { guids.Remove(guid); continue; }
                    if (!toggle.IsAvailable) continue;   // its state is not back yet
                    guids.Remove(guid);
                    toggle.IsOn = true;
                    if (!toggle.IsOn) continue;
                    Main.Log($"[Held] {unit.CharacterName}: {toggle.Name} on again - its state is back after the fight (it was on before the AI spent it)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Held] {unit.CharacterName} restore toggles: {ex.Message}");
            }
            return false;
        }

        #endregion
    }
}
