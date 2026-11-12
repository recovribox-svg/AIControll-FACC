// ★ v0.3.30 C06R4: author-approved recipient-owned periodic damage; native contracts, no action execution.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums.Damage;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        internal sealed class PeriodicPlan
        {
            internal BlueprintBuff Blueprint;
            internal ActionList Round;
            internal BuffPoisonStatDamage Poison;
            internal readonly List<(DamageEnergyType energy, bool spell)> EnergyRemovals = new List<(DamageEnergyType, bool)>();
            internal readonly List<ContextDiceValue> DiceInputs = new List<ContextDiceValue>();
            internal bool HitPoints;
            internal string Unsupported;
            internal bool Supported => Unsupported == null && (Round != null || Poison != null);
        }

        private static readonly Dictionary<BlueprintBuff, PeriodicPlan> s_PeriodicPlans = new Dictionary<BlueprintBuff, PeriodicPlan>();
        private static readonly FieldInfo PeriodicSaveIncreases = HarmonyLib.AccessTools.Field(typeof(ContextActionSavingThrow), "m_ConditionalDCIncrease");

        // Metadata only, once per blueprint. Spatial/unknown actions are rejected, never evaluated at a guessed location.
        internal static PeriodicPlan PeriodicPlanOf(BlueprintBuff buff)
        {
            if (buff == null) return null;
            if (s_PeriodicPlans.TryGetValue(buff, out var known)) return known;
            var plan = new PeriodicPlan { Blueprint = buff };
            s_PeriodicPlans[buff] = plan;
            try
            {
                if (!(buff.ComponentsArray?.Any(c => c is Kingmaker.Controllers.Units.ITickEachRound) ?? false)) return plan;
                int visited = 0;
                foreach (var component in buff.ComponentsArray ?? Array.Empty<BlueprintComponent>())
                {
                    if (component is AddFactContextActions actions)
                    {
                        if (!Empty(actions.Activated) || !Empty(actions.Deactivated) || !Empty(actions.Dispose) || plan.Round != null ||
                            !ValidatePeriodicActions(actions.NewRound, false, plan, ref visited, 0))
                        { plan.Unsupported = "unread or non-local fact actions"; break; }
                        if (!Empty(actions.NewRound)) plan.Round = actions.NewRound;
                    }
                    else if (component is BuffPoisonStatDamage poison && plan.Poison == null && PeriodicPoison(poison)) plan.Poison = poison;
                    else if (component is AddEnergyDamageTrigger energy && RemovesSelfOnly(energy.Actions))
                        plan.EnergyRemovals.Add((energy.DamageType, energy.SpellsOnly));
                    else if (component is BuffStatusCondition status && !status.SaveEachRound) { }
                    else if (!(component is SpellDescriptorComponent) && !(component is ContextRankConfig))
                    { plan.Unsupported = "unread component " + component?.GetType().Name; break; }
                }
                if (plan.Round != null && plan.Poison != null) plan.Unsupported = "multiple tick/removal components";
            }
            catch { plan.Unsupported = "unread native layout"; }
            return plan;
        }

        private static bool Empty(ActionList actions) => actions?.Actions == null || actions.Actions.Length == 0;
        private static bool RemovesSelfOnly(ActionList actions) => actions?.Actions?.Length == 1 && actions.Actions[0]?.GetType() == typeof(ContextActionRemoveSelf);
        private static bool PeriodicValue(ContextValue value) => value != null &&
            (value.ValueType == ContextValueType.Simple || value.ValueType == ContextValueType.Rank);
        private static bool PeriodicDice(ContextDiceValue value) => value != null && PeriodicValue(value.DiceCountValue) && PeriodicValue(value.BonusValue);
        private static bool PeriodicPoison(BuffPoisonStatDamage poison) => poison.Ticks > 0 && poison.SuccesfullSaves > 0 &&
            (poison.SaveType == SavingThrowType.Fortitude || poison.SaveType == SavingThrowType.Reflex || poison.SaveType == SavingThrowType.Will) &&
            poison.Value.Rolls >= 0 && poison.Bonus >= 0;

        private static bool ValidatePeriodicActions(ActionList list, bool savedScope, PeriodicPlan plan, ref int visited, int depth)
        {
            if (depth > 12) return false;
            foreach (var action in list?.Actions ?? Array.Empty<GameAction>())
            {
                if (++visited > 128 || action == null) return false;
                if (action.GetType() == typeof(ContextActionRemoveSelf)) continue;
                if (action.GetType() == typeof(ContextActionDealDamage))
                {
                    var deal = (ContextActionDealDamage)action;
                    if (!PeriodicDice(deal.Value) || deal.Half || deal.HalfIfSaved || deal.ReadPreRolledFromSharedValue ||
                        deal.AddAdditionalDamage || deal.AddFavoredEnemyDamage || deal.IgnoreUnitModifiers ||
                        deal.UseWeaponDamageModifiers || deal.UseMinHPAfterDamage || deal.WriteResultToSharedValue ||
                        deal.WriteRawResultToSharedValue || deal.WriteCriticalToSharedValue || deal.Drain || deal.IsAoE) return false;
                    if (KindOf(deal) == DealKind.HitPoints)
                    {
                        if (deal.DamageType == null) return false;
                        plan.HitPoints = true;
                    }
                    plan.DiceInputs.Add(deal.Value);
                    continue;
                }
                if (action.GetType() == typeof(ContextActionSavingThrow))
                {
                    var save = (ContextActionSavingThrow)action;
                    if (save.FromBuff || save.UseDCFromContextSavingThrow || save.Type == SavingThrowType.Unknown ||
                        save.HasCustomDC && !PeriodicValue(save.CustomDC) || PeriodicSaveIncreases == null ||
                        (PeriodicSaveIncreases.GetValue(save) is Array increases && increases.Length > 0) ||
                        save.Actions?.Actions?.Length != 1 || !(save.Actions.Actions[0] is ContextActionConditionalSaved) ||
                        !ValidatePeriodicActions(save.Actions, true, plan, ref visited, depth + 1)) return false;
                    continue;
                }
                if (action.GetType() == typeof(ContextActionConditionalSaved) && savedScope)
                {
                    var saved = (ContextActionConditionalSaved)action;
                    if (!ValidatePeriodicActions(saved.Failed, false, plan, ref visited, depth + 1) ||
                        !ValidatePeriodicActions(saved.Succeed, false, plan, ref visited, depth + 1)) return false;
                    continue;
                }
                if (action.GetType() == typeof(ContextActionApplyBuff))
                {
                    var apply = (ContextActionApplyBuff)action;
                    if (apply.ToCaster || apply.AsChild || apply.SameDuration || apply.Buff?.ComponentsArray == null ||
                        !apply.Permanent && !apply.UseDurationSeconds && (apply.DurationValue == null ||
                            !PeriodicValue(apply.DurationValue.DiceCountValue) || !PeriodicValue(apply.DurationValue.BonusValue) ||
                            apply.DurationValue.Rate != DurationRate.Rounds)) return false;
                    // Only immediate, understood child consequences. Their later rounds are read from their own active
                    // instances, not invented as additional damage of the originating enemy's next action.
                    foreach (var component in apply.Buff.ComponentsArray)
                        if (!(component is AddCondition) && !(component is SpellDescriptorComponent) &&
                            !(component is BuffStatusCondition status && !status.SaveEachRound) &&
                            !(component is Kingmaker.Designers.Mechanics.Facts.CombatStateTrigger state && Empty(state.CombatStartActions) && RemovalLifecycle(state.CombatEndActions, 0)) &&
                            !(component is Kingmaker.UnitLogic.Buffs.Components.RemoveWhenCombatEnded) &&
                            !(component is BuffPoisonStatDamage poison && PeriodicPoison(poison))) return false;
                    continue;
                }
                return false;
            }
            return true;
        }

        private static bool RemovalLifecycle(ActionList list, int depth)
        {
            if (depth > 8) return false;
            foreach (var action in list?.Actions ?? Array.Empty<GameAction>())
            {
                if (action is ContextActionRemoveSelf) continue;
                if (action is Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional conditional &&
                    RemovalLifecycle(conditional.IfTrue, depth + 1) && RemovalLifecycle(conditional.IfFalse, depth + 1)) continue;
                return false;
            }
            return true;
        }

        internal sealed class PeriodicTick
        {
            internal readonly Affliction Harm = new Affliction();
            internal float Survives = 1f;
        }

        // Injected numerical readers keep native graph/counter tests independent of actors and Rulebook execution.
        internal static PeriodicTick ReadPeriodicTick(ActionList actions, Func<ContextActionSavingThrow, float> fail,
            Func<ContextActionDealDamage, Affliction> damage, Func<ContextActionApplyBuff, Affliction> applied)
        {
            var result = new PeriodicTick();
            foreach (var action in actions?.Actions ?? Array.Empty<GameAction>())
            {
                if (action is ContextActionRemoveSelf) result.Survives = 0f;
                else if (action is ContextActionDealDamage deal) result.Harm.Add(damage(deal), 1f);
                else if (action is ContextActionApplyBuff apply) result.Harm.Add(applied(apply), 1f);
                else if (action is ContextActionSavingThrow save)
                {
                    float f = Math.Max(0f, Math.Min(1f, fail(save)));
                    var branch = (ContextActionConditionalSaved)save.Actions.Actions[0];
                    var no = ReadPeriodicTick(branch.Failed, fail, damage, applied);
                    var yes = ReadPeriodicTick(branch.Succeed, fail, damage, applied);
                    result.Harm.Add(no.Harm, f); result.Harm.Add(yes.Harm, 1f - f);
                    result.Survives *= f * no.Survives + (1f - f) * yes.Survives;
                }
            }
            return result;
        }
    }
}
