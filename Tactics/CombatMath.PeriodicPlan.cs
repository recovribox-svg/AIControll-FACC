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
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using UnityEngine;

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

        private static readonly Dictionary<BlueprintBuff, bool> s_DamageCarriers = new Dictionary<BlueprintBuff, bool>();

        /// <summary>
        /// ★ Fusion coverage C1 (docs/FUSION_MAIN_TARGET_DOT.md): a buff whose whole effect is hit-point damage each
        /// round, possibly ended by a save (Incendiary Runes' and Molten Orb's burning, Acid Arrow's acid). Only its round
        /// actions and spell descriptors; fixed dice; no child buff, poison or activation. Metadata only, once per buff.
        /// </summary>
        internal static bool IsPeriodicDamageCarrier(BlueprintBuff buff)
        {
            if (buff == null) return false;
            if (s_DamageCarriers.TryGetValue(buff, out bool known)) return known;
            bool carrier = false;
            try
            {
                var plan = PeriodicPlanOf(buff);
                // ★ coverage4 (Fusion coverage C6a): or a rank config of the buff's own read from the caster level (Caustic
                // Eruption's acid, 1d6 per two caster levels, at most 10): fixed by the cast's parameters, read in a buff
                // context under the cast's (PeriodicRank).
                carrier = plan != null && plan.Supported && plan.Round != null && plan.Poison == null &&
                          plan.EnergyRemovals.Count == 0 && plan.HitPoints &&
                          buff.ComponentsArray.All(c => c is AddFactContextActions || c is SpellDescriptorComponent || CasterLevelConfig(c)) &&
                          buff.TickTime.TotalSeconds >= 1d && buff.TickTime < TimeSpan.MaxValue &&
                          DamageOnlyTick(buff, plan.Round, 0);
            }
            catch { carrier = false; }
            s_DamageCarriers[buff] = carrier;
            return carrier;
        }

        private static bool DamageOnlyTick(BlueprintBuff buff, ActionList list, int depth)
        {
            if (depth > 8) return false;
            foreach (var action in list?.Actions ?? Array.Empty<GameAction>())
            {
                if (action is ContextActionRemoveSelf) continue;
                if (action is ContextActionDealDamage deal)
                {
                    if (KindOf(deal) != DealKind.HitPoints || !TickValue(buff, deal.Value.DiceCountValue) ||
                        !TickValue(buff, deal.Value.BonusValue)) return false;
                    continue;
                }
                if (action is ContextActionSavingThrow save)
                {
                    if (save.HasCustomDC && save.CustomDC.ValueType != ContextValueType.Simple) return false;
                    var saved = (ContextActionConditionalSaved)save.Actions.Actions[0];
                    if (!DamageOnlyTick(buff, saved.Failed, depth + 1) || !DamageOnlyTick(buff, saved.Succeed, depth + 1)) return false;
                    continue;
                }
                return false; // a child buff or anything else is more than damage
            }
            return true;
        }

        /// <summary>A tick's dice number: a fixed non-negative value, or a rank the buff's own caster-level config gives.</summary>
        private static bool TickValue(BlueprintBuff buff, ContextValue value)
        {
            if (value == null) return false;
            if (value.ValueType == ContextValueType.Simple) return value.Value >= 0;
            if (value.ValueType != ContextValueType.Rank) return false;
            foreach (var config in buff.GetComponents<Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig>())
                if (config.Type == value.ValueRank) return CasterLevelConfig(config);
            return false;
        }

        /// <summary>A rank config read from the caster level only (no stat, class, buff rank or property of anyone).</summary>
        private static bool CasterLevelConfig(BlueprintComponent component)
        {
            if (!(component is Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig config)) return false;
            try
            {
                return RankBaseType != null && (Kingmaker.UnitLogic.Mechanics.Components.ContextRankBaseValueType)RankBaseType.GetValue(config) ==
                       Kingmaker.UnitLogic.Mechanics.Components.ContextRankBaseValueType.CasterLevel;
            }
            catch { return false; }
        }

        private static readonly Dictionary<KeyValuePair<AbilityData, BlueprintBuff>, MechanicsContext> s_PeriodicRankContexts =
            new Dictionary<KeyValuePair<AbilityData, BlueprintBuff>, MechanicsContext>();
        private static int s_PeriodicRankFrame = -1;

        /// <summary>
        /// ★ coverage4 (C6a): a tick's dice number for <paramref name="ability"/>'s application of <paramref name="buff"/>: a fixed
        /// value, or the buff's caster-level rank, read as the game does in a buff context whose parent is the cast's (its
        /// caster level, fused spellbook and metamagic). The rank reads nobody's state, so one context per cast and buff
        /// serves the decision's frame.
        /// </summary>
        internal static int PeriodicRank(AbilityData ability, BlueprintBuff buff, UnitEntityData recipient, ContextValue value)
        {
            if (value == null) return 0;
            if (value.ValueType == ContextValueType.Simple) return value.Value;
            int frame = Time.frameCount;
            if (frame != s_PeriodicRankFrame) { s_PeriodicRankContexts.Clear(); s_PeriodicRankFrame = frame; }
            var key = new KeyValuePair<AbilityData, BlueprintBuff>(ability, buff);
            if (!s_PeriodicRankContexts.TryGetValue(key, out var context))
            {
                try
                {
                    var parent = CreateContext(ability, new TargetWrapper(recipient));
                    context = new MechanicsContext(ability.Caster?.Unit, recipient.Descriptor, buff, parent);
                    context.RecalculateRanks();
                }
                catch { context = null; }
                if (s_PeriodicRankContexts.Count > 128) s_PeriodicRankContexts.Clear();
                s_PeriodicRankContexts[key] = context;
            }
            return context != null ? Math.Max(0, EvaluateValue(value, context, 0)) : 0;
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
                    // ★ Fusion coverage C1: IgnoreUnitModifiers only stops flat damage bonuses being added
                    // (BaseDamage.AddModifier); resistance and immunity still apply. No estimate here adds those bonuses.
                    if (!PeriodicDice(deal.Value) || deal.Half || deal.HalfIfSaved || deal.ReadPreRolledFromSharedValue ||
                        deal.AddAdditionalDamage || deal.AddFavoredEnemyDamage ||
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
                    // ★ Fusion coverage C1: a round tick runs outside any saving-throw scope (MechanicsContext.SavingThrow
                    // reads ContextData<SavingThrowData>.Current), so UseDCFromContextSavingThrow falls back to the
                    // context's own DC there, and the tick rolls its own save, fused cast or not (ContextActionSavingThrow).
                    if (save.FromBuff || save.Type == SavingThrowType.Unknown ||
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
