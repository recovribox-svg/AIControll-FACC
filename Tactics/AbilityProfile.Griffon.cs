// ★ v0.3.50 review2: The native griffon leap owns its teleport and queued charge attack. It is not a walking Charge or a spell payload.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.TurnBasedModifiers;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.Designers.Mechanics.Facts.Restrictions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.CasterCheckers;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed partial class AbilityProfile
    {
        internal AbilityGriffinAttack GriffonAttack { get; private set; }

        private static AbilityGriffinAttack ReadGriffonAttack(BlueprintAbility blueprint)
        {
            if (blueprint == null || blueprint.Type != AbilityType.Physical || blueprint.Range != AbilityRange.DoubleMove ||
                blueprint.ActionType != Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Standard ||
                !blueprint.CanTargetEnemies || blueprint.CanTargetFriends || blueprint.CanTargetSelf || blueprint.CanTargetPoint ||
                blueprint.SpellResistance || blueprint.ComponentsArray?.Length != 7) return null;
            var leap = blueprint.GetComponent<AbilityGriffinAttack>();
            // These exact requirement types remain native authority. Modded condition/fact lists still go through
            // IsAvailable/CanTarget; extra providers, effects and custom subclasses do not inherit this forecast.
            var types = new HashSet<Type>(blueprint.ComponentsArray.Select(c => c?.GetType()));
            if (types.Count != 7 || !types.SetEquals(new[] { typeof(AbilityGriffinAttack), typeof(AbilityRequirementHasItemInHands),
                    typeof(AbilityRequirementHasCondition), typeof(AbilityCasterHasNoFacts), typeof(AbilityIsFullRoundInTurnBased),
                    typeof(AbilityRequirementCanMove), typeof(AbilityCasterInCombat) }) || leap == null || leap.IgnoreMountedRestriction ||
                !blueprint.GetComponent<AbilityIsFullRoundInTurnBased>().FullRoundIfTurnBased ||
                blueprint.GetComponent<AbilityCasterInCombat>().Not ||
                leap.TakeoffTime != 1.2f || leap.LandingTime != 1.5f || leap.DisappearStartTime != 1.1f || leap.AppearTime != 0.1f)
                return null;
            return leap;
        }

        internal sealed class GriffonChargeEffect
        {
            internal BlueprintBuff Buff;
            internal int Attack, Armor;
        }

        internal sealed class GriffonChargeProfile
        {
            internal BlueprintBuff Root;
            internal BlueprintUnitFact CasterFact, RecipientFact;
            internal bool CasterNot, RecipientNot;
            internal GriffonChargeEffect Yes, No;

            internal GriffonChargeEffect Select(Func<BlueprintUnitFact, bool> hasFact) =>
                hasFact(CasterFact) != CasterNot && hasFact(RecipientFact) != RecipientNot ? Yes : No;
        }

        /// <summary>
        /// Read the actual runtime BlueprintRoot charge carrier. The inspected native root applies one of two
        /// child effects using two simple fact checks on the caster/recipient (the same unit here), then removes
        /// those children. Unknown providers or lifecycle actions withhold the leap instead of inventing bonuses.
        /// </summary>
        internal static GriffonChargeProfile ReadGriffonCharge(BlueprintBuff root)
        {
            if (root?.ComponentsArray?.Length != 1 || root.ComponentsArray[0]?.GetType() != typeof(AddBuffActions)) return null;
            var actions = (AddBuffActions)root.ComponentsArray[0];
            if (actions.Activated?.Actions?.Length != 1 || actions.Activated.Actions[0]?.GetType() != typeof(Conditional) ||
                !GriffonEmpty(actions.NewRound) || !GriffonEmpty(actions.Dispose)) return null;
            var choice = (Conditional)actions.Activated.Actions[0];
            if (choice.ConditionsChecker?.Operation != Operation.And || choice.ConditionsChecker.Conditions?.Length != 2) return null;
            ContextConditionCasterHasFact caster = null; ContextConditionHasFact recipient = null;
            foreach (var condition in choice.ConditionsChecker.Conditions)
            {
                if (condition?.GetType() == typeof(ContextConditionCasterHasFact)) caster = (ContextConditionCasterHasFact)condition;
                else if (condition?.GetType() == typeof(ContextConditionHasFact)) recipient = (ContextConditionHasFact)condition;
                else return null;
            }
            if (caster?.Fact == null || recipient?.Fact == null) return null;
            var yes = ReadGriffonChargeApplication(choice.IfTrue);
            var no = ReadGriffonChargeApplication(choice.IfFalse);
            if (yes == null || no == null || yes.Buff == no.Buff || actions.Deactivated?.Actions?.Length != 2) return null;
            var removed = new HashSet<BlueprintBuff>();
            foreach (var action in actions.Deactivated.Actions)
            {
                if (action?.GetType() != typeof(ContextActionRemoveBuff)) return null;
                var remove = (ContextActionRemoveBuff)action;
                if (remove.ToCaster || remove.RemoveRank || remove.Buff == null) return null;
                removed.Add(remove.Buff);
            }
            if (removed.Count != 2 || !removed.Contains(yes.Buff) || !removed.Contains(no.Buff)) return null;
            return new GriffonChargeProfile { Root = root, CasterFact = caster.Fact, RecipientFact = recipient.Fact,
                CasterNot = caster.Not, RecipientNot = recipient.Not, Yes = yes, No = no };
        }

        private static bool GriffonEmpty(ActionList actions) => actions?.Actions == null || actions.Actions.Length == 0;

        private static GriffonChargeEffect ReadGriffonChargeApplication(ActionList actions)
        {
            if (actions?.Actions?.Length != 1 || actions.Actions[0]?.GetType() != typeof(ContextActionApplyBuff)) return null;
            var apply = (ContextActionApplyBuff)actions.Actions[0];
            var duration = apply.DurationValue;
            if (apply.Buff == null || apply.Permanent || apply.UseDurationSeconds || apply.ToCaster || !apply.AsChild ||
                apply.SameDuration || apply.IgnoreParentContext || apply.IsFromSpell || duration == null ||
                duration.Rate != DurationRate.Rounds || duration.DiceType != Kingmaker.RuleSystem.DiceType.Zero ||
                duration.BonusValue?.ValueType != ContextValueType.Simple || duration.BonusValue.Value != 1) return null;
            return ReadGriffonChargeEffect(apply.Buff);
        }

        internal static GriffonChargeEffect ReadGriffonChargeEffect(BlueprintBuff buff)
        {
            if (buff?.ComponentsArray == null || buff.ComponentsArray.Length == 0 || buff.ComponentsArray.Length > 4) return null;
            int attack = 0, armor = 0;
            foreach (var component in buff.ComponentsArray)
            {
                if (component?.GetType() == typeof(AddCondition))
                {
                    var condition = (AddCondition)component;
                    if (condition.Condition != UnitCondition.StealthForbidden || condition.Exceptions != null) return null;
                }
                else if (component?.GetType() == typeof(AddBuffActions))
                {
                    var lifecycle = (AddBuffActions)component;
                    if (!GriffonEmpty(lifecycle.Activated) || !GriffonEmpty(lifecycle.Deactivated) ||
                        !GriffonEmpty(lifecycle.NewRound) || !GriffonEmpty(lifecycle.Dispose)) return null;
                }
                else if (component?.GetType() == typeof(AttackOfOpportunityAttackBonus))
                {
                    var bonus = (AttackOfOpportunityAttackBonus)component;
                    if (!bonus.NotAttackOfOpportunity || bonus.Descriptor != ModifierDescriptor.None ||
                        bonus.Bonus?.ValueType != ContextValueType.Simple || bonus.Bonus.Value <= 0 || attack != 0) return null;
                    attack = bonus.Bonus.Value;
                }
                else if (component?.GetType() == typeof(AddContextStatBonus))
                {
                    var bonus = (AddContextStatBonus)component;
                    if (bonus.HasMinimal || bonus.Value?.ValueType != ContextValueType.Simple || bonus.Multiplier != 1 ||
                        ShotRestrictions == null || !(ShotRestrictions.GetValue(bonus) is RestrictionCalculator restrictions) ||
                        restrictions.Property != null && !restrictions.Property.Empty) return null;
                    if (bonus.Stat == StatType.AdditionalAttackBonus && bonus.Descriptor == ModifierDescriptor.None && bonus.Value.Value > 0 && attack == 0)
                        attack = bonus.Value.Value;
                    else if (bonus.Stat == StatType.AC && bonus.Descriptor == ModifierDescriptor.Penalty && bonus.Value.Value < 0 && armor == 0)
                        armor = bonus.Value.Value;
                    else return null;
                }
                else return null;
            }
            if (attack <= 0 || !ModifierDescriptorHelper.IsStackable(ModifierDescriptor.None)) return null;
            return new GriffonChargeEffect { Buff = buff, Attack = attack, Armor = armor };
        }
    }
}
