// Value-selected effects: a verified common buff and fixed minimum duration, never all mutually exclusive branches.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.ElementsSystem;
using Kingmaker.RuleSystem;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed partial class AbilityProfile
    {
        internal sealed class SelectedBuffFloor
        {
            internal ContextActionApplyBuff Application;
            internal GameAction[] Actions;
            internal float MaximumRounds;
            internal UnitCondition[] Conditions;
            internal bool OnlyConditions;
            internal string Refusal;
        }
        private Dictionary<ContextActionSelectByValue, SelectedBuffFloor> valueSelections;
        private List<SelectedBuffFloor> selectedBuffFloors;
        internal IReadOnlyList<SelectedBuffFloor> SelectedBuffFloors =>
            (IReadOnlyList<SelectedBuffFloor>)selectedBuffFloors ?? Array.Empty<SelectedBuffFloor>();

        private static readonly FieldInfo SelectionType = typeof(ContextActionSelectByValue).GetField("m_Type", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SelectionVariants = typeof(ContextActionSelectByValue).GetField("m_Variants", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Type SelectionVariant = SelectionVariants?.FieldType.GetElementType();
        private static readonly FieldInfo SelectionValue = SelectionVariant?.GetField("Value");
        private static readonly FieldInfo SelectionAction = SelectionVariant?.GetField("Action");

        // RunAction selects exactly one Greatest variant (last on a tie). No Calculate, Run or synthetic roll here.
        // Only an effect common to EVERY variant is credited. The longer/critical benefit is deliberately omitted.
        private SelectedBuffFloor ReadValueSelection(ContextActionSelectByValue selection)
        {
            if (valueSelections == null) valueSelections = new Dictionary<ContextActionSelectByValue, SelectedBuffFloor>();
            if (valueSelections.TryGetValue(selection, out var cached)) return cached;
            var read = ReadValueSelectionCore(selection);
            valueSelections.Add(selection, read);
            return read;
        }

        private static SelectedBuffFloor ReadValueSelectionCore(ContextActionSelectByValue selection)
        {
            SelectedBuffFloor Refuse(string why) => new SelectedBuffFloor { Refusal = why };
            try
            {
                if (selection.GetType() != typeof(ContextActionSelectByValue) || SelectionValue == null || SelectionAction == null ||
                    SelectionType?.GetValue(selection)?.ToString() != "Greatest" ||
                    !(SelectionVariants.GetValue(selection) is Array variants) || variants.Length < 2 || variants.Length > 8)
                    return Refuse("uninspected selector layout/type or variant bound");
                ContextActionApplyBuff minimum = null;
                float smallest = float.MaxValue, largest = 0f;
                foreach (var entry in variants)
                {
                    var value = SelectionValue.GetValue(entry) as ContextValue;
                    var actions = (SelectionAction.GetValue(entry) as ActionList)?.Actions;
                    if (value == null || value.ValueType != ContextValueType.Simple && value.ValueType != ContextValueType.Shared ||
                        actions?.Length != 1 || !(actions[0] is ContextActionApplyBuff apply) ||
                        apply.GetType() != typeof(ContextActionApplyBuff) || apply.Buff == null || !IsHarmfulBuff(apply.Buff))
                        return Refuse("variants do not each directly apply one common harmful buff");
                    var duration = apply.DurationValue;
                    if (apply.Permanent || apply.SameDuration || apply.UseDurationSeconds || duration == null ||
                        duration.DiceType != DiceType.Zero || duration.DiceCountValue?.IsValueSimple != true ||
                        duration.DiceCountValue.Value != 0 || duration.BonusValue?.IsValueSimple != true ||
                        duration.Rate != DurationRate.Rounds && duration.Rate != DurationRate.Minutes)
                        return Refuse("non-fixed or inherited duration");
                    float rounds = ExpectedRounds(apply);
                    if (rounds <= 0f || float.IsNaN(rounds) || float.IsInfinity(rounds)) return Refuse("non-positive duration");
                    if (minimum != null && (apply.Buff != minimum.Buff || apply.ToCaster != minimum.ToCaster ||
                        apply.IsFromSpell != minimum.IsFromSpell || apply.IsNotDispelable != minimum.IsNotDispelable ||
                        apply.AsChild != minimum.AsChild || apply.NotLinkToAreaEffect != minimum.NotLinkToAreaEffect ||
                        apply.IgnoreParentContext != minimum.IgnoreParentContext ||
                        duration.IsExtendable != minimum.DurationValue.IsExtendable))
                        return Refuse("different buff, subject, application flags or Extend behavior");
                    largest = Math.Max(largest, rounds);
                    if (rounds < smallest) { minimum = apply; smallest = rounds; }
                }
                return new SelectedBuffFloor { Application = minimum, Actions = new GameAction[] { minimum }, MaximumRounds = largest,
                    Conditions = ConditionsOf(minimum.Buff).ToArray(),
                    OnlyConditions = minimum.Buff.ComponentsArray != null && minimum.Buff.ComponentsArray.All(c => c == null ||
                        c.GetType() == typeof(AddCondition) || c.GetType() == typeof(SpellDescriptorComponent) ||
                        c.GetType() == typeof(Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition) ||
                        c.GetType() == typeof(Kingmaker.UnitLogic.Buffs.Components.RemoveWhenCombatEnded)) };
            }
            catch (Exception ex) { return Refuse("selector metadata " + ex.GetType().Name); }
        }
    }
}
