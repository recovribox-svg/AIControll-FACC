// ★ test1-scoring-fixes: restricted, read-only appraisal of native shared-HD control graphs.
// No GameAction is executed; contexts and forecast state exist only for one actual scoring aim.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.Base;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    internal static class SharedHdControl
    {
        internal sealed class Forecast
        {
            internal readonly bool Supported;
            private readonly HashSet<BlueprintBuff> m_Controlled;
            private readonly HashSet<GameAction> m_Pooled;
            private readonly Dictionary<UnitEntityData, float> m_Eligibility;

            internal Forecast(bool supported, IEnumerable<BlueprintBuff> controlled,
                Dictionary<UnitEntityData, float> eligibility = null, HashSet<GameAction> pooled = null)
            {
                Supported = supported;
                m_Controlled = new HashSet<BlueprintBuff>(controlled ?? Enumerable.Empty<BlueprintBuff>());
                m_Eligibility = eligibility;
                m_Pooled = pooled != null && pooled.Count > 0 ? pooled : null;
            }

            /// <summary>★ Fusion coverage C8: the pool spends on damage or a kill (Circle of Death), behind the same save.</summary>
            internal bool PoolsDamage => m_Pooled != null;

            internal bool Pools(GameAction action) => action != null && m_Pooled != null && m_Pooled.Contains(action);

            /// <summary>The chance the pool still covers <paramref name="recipient"/> when the spell reaches it; 0 unread.</summary>
            internal float DamageEligibility(UnitEntityData recipient) =>
                m_Pooled == null ? 1f : Supported && recipient != null && m_Eligibility != null &&
                                        m_Eligibility.TryGetValue(recipient, out var p) ? p : 0f;

            /// <summary>The same for an ally: an unread pool, or one the forecast did not select (walking in), is assumed to reach it.</summary>
            internal float AllyReach(UnitEntityData ally) =>
                m_Pooled == null ? 1f : Supported && ally != null && m_Eligibility != null &&
                                        m_Eligibility.TryGetValue(ally, out var p) ? p : 1f;

            /// <summary>The forecast gives this recipient some chance of a pooled control or a pooled damage.</summary>
            internal bool Reaches(UnitEntityData recipient) => Supported && recipient != null && m_Eligibility != null &&
                m_Eligibility.TryGetValue(recipient, out var p) && p > 0f;

            internal bool Controls(BlueprintBuff buff) => buff != null && m_Controlled.Contains(buff);

            internal bool Selects(UnitEntityData recipient) => Supported && recipient != null &&
                m_Eligibility != null && m_Eligibility.ContainsKey(recipient);

            internal float Eligibility(UnitEntityData recipient, BlueprintBuff buff)
            {
                if (!Controls(buff)) return 1f;
                return Supported && recipient != null && m_Eligibility != null && m_Eligibility.TryGetValue(recipient, out var p)
                    ? p : 0f;
            }

        }

        private sealed class Plan
        {
            internal AbilityTargetsAround Selector;
            internal AbilitySharedValue SharedValue;
            internal ContextCalculateSharedValue Calculator;
            internal Node Root;
            internal bool IgnoreCaster;
            internal bool Spread;
            internal int[] Limits;
            internal string Failure;
            internal bool Reported;
            internal readonly HashSet<BlueprintBuff> Buffs = new HashSet<BlueprintBuff>();
            internal readonly HashSet<GameAction> Payload = new HashSet<GameAction>();
        }

        private sealed class ReadState
        {
            internal AbilityExecutionContext Context;
            internal AbilitySharedValue SharedValue;
            internal int Cost;
            internal int Debits;
            internal bool ReachesBuff;
        }

        private abstract class Node
        {
            internal abstract void Read(ReadState state);
        }

        private sealed class Empty : Node
        {
            internal override void Read(ReadState state) { }
        }

        private sealed class Branch : Node
        {
            internal ConditionsChecker Checker;
            internal Node True, False;
            internal override void Read(ReadState state) => (Checker.Check() ? True : False).Read(state);
        }

        // The recognized save's failed branch reaches this buff. Its probability is priced by EffectChance, not here.
        private sealed class Save : Node
        {
            internal override void Read(ReadState state) => state.ReachesBuff = true;
        }

        private sealed class Debit : Node
        {
            internal override void Read(ReadState state)
            {
                state.Debits++;
                state.Context[state.SharedValue] -= state.Cost;
            }
        }

        private sealed class SaveThenDebit : Node
        {
            internal override void Read(ReadState state)
            {
                state.ReachesBuff = true;
                state.Debits++;
                state.Context[state.SharedValue] -= state.Cost;
            }
        }

        private sealed class Builder
        {
            internal int Nodes;
            internal readonly HashSet<AbilitySharedValue> Conditions = new HashSet<AbilitySharedValue>();
            internal readonly HashSet<AbilitySharedValue> Debits = new HashSet<AbilitySharedValue>();
            internal readonly HashSet<BlueprintBuff> Buffs = new HashSet<BlueprintBuff>();
            internal SavingThrowType SaveType;
            internal ContextActionApplyBuff Application;
            internal bool Fused; // ★ Fusion F7: under a root save the nested save rolls nothing (ContextActionSavingThrow)
            // ★ Fusion coverage C8: what a save's outcome does instead of one buff (Circle of Death's kill or 100 damage), and the
            // limits of the pool's Hit Dice conditions ("level <= limit + pool").
            internal readonly HashSet<GameAction> Payload = new HashSet<GameAction>();
            internal readonly HashSet<int> Limits = new HashSet<int>();
        }

        private static readonly Dictionary<BlueprintAbility, Plan> Plans = new Dictionary<BlueprintAbility, Plan>();
        private static readonly HashSet<BlueprintAbility> ReportedFailures = new HashSet<BlueprintAbility>();
        private static readonly FieldInfo SelectorCondition = typeof(AbilityTargetsAround).GetField("m_Condition", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SaveAdjustments = typeof(ContextActionSavingThrow).GetField("m_ConditionalDCIncrease", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly HashSet<Type> SafeConditions = new HashSet<Type>
        {
            typeof(ContextConditionHasFact), typeof(ContextConditionCasterHasFact), typeof(ContextConditionHasBuff),
            typeof(ContextConditionHasBuffFromCaster), typeof(ContextConditionIsUnconscious), typeof(ContextConditionAlive),
            typeof(ContextConditionIsAlly), typeof(ContextConditionIsEnemy), typeof(ContextConditionIsCaster)
        };

        /// <summary>
        /// True means a shared-HD control dependency was recognized. Unsupported forecasts still return true and
        /// withhold the dependent buff's credit, rather than restoring independent full-budget-per-recipient scoring.
        /// </summary>
        internal static bool TryForAim(AbilityData ability, AbilityProfile profile, TargetWrapper aim, out Forecast forecast)
        {
            forecast = null;
            if (ability?.Blueprint == null || profile == null || !profile.UsesSharedHdBudget) return false;
            var controlled = profile.SharedHdBuffs;
            // ★ Fusion coverage C8: a pool that spends on a kill or damage (Circle of Death) has no dependent buff; its forecast
            // covers the pool's payload instead. Without a readable plan such a cast keeps the older range estimate.
            HashSet<GameAction> payload = null;
            if (controlled.Count == 0)
            {
                payload = ability.MagicHackData != null ? FusedPayload(ability, profile) : OrdinaryPayload(ability, profile);
                if (payload == null) return false;
            }
            forecast = new Forecast(false, controlled, null, payload);
            if (aim == null || ability.ShadowSpellSettings != null)
                return Fail(ability, "unsupported aim or substituted delivery");
            if (ability.MagicHackData != null) return TryForFusedAim(ability, profile, aim, controlled, ref forecast);
            if (!Plans.TryGetValue(ability.Blueprint, out var plan))
                Plans[ability.Blueprint] = plan = Build(ability.Blueprint, profile);
            if (plan.Failure != null) return Fail(ability, plan.Failure);
            try
            {
                // One actual-aim context; never activated, retained in a cache, or used to execute actions.
                var context = CombatMath.CreateContext(ability, aim);
                if (context == null) return Fail(ability, "no read-only cast context");
                // ★ Fusion coverage C8: the pool's exact distribution as this cast rolls it (a fixed pool is one value).
                if (!TryOrdinaryRoll(plan, context, out var budgets, out var chances, out string rollFailure))
                    return Fail(ability, "pool roll: " + rollFailure);
                int top = budgets[budgets.Length - 1];
                int previous = context[plan.SharedValue];
                try
                {
                    context[plan.SharedValue] = top;
                    List<UnitEntityData> selected;
                    using (context.GetDataScope(aim))
                        selected = plan.Selector.Select(context, aim).Select(t => t?.Unit)
                            .Take(SharedHdBudget.MaxRecipients + 1).ToList();
                    if (selected.Count > SharedHdBudget.MaxRecipients || selected.Any(u => u == null) ||
                        selected.Distinct().Count() != selected.Count)
                        return Fail(ability, "unsupported native recipient set");
                    if (plan.Spread) selected = SpreadOrder(selected, aim);
                    var recipients = new List<SharedHdBudget.Recipient>(selected.Count);
                    foreach (var unit in selected)
                        recipients.Add(RecipientOf(ability, context, plan.SharedValue, plan.Root, plan.IgnoreCaster, plan.Limits, unit, top));
                    if (!SharedHdBudget.TryForecast(budgets, chances, recipients, out var eligibility))
                        return Fail(ability, "unsupported shared-budget transition or work bound");
                    var values = new Dictionary<UnitEntityData, float>();
                    for (int i = 0; i < selected.Count; i++) values.Add(selected[i], Math.Min(1f, eligibility[i]));
                    forecast = new Forecast(true, controlled, values, payload);
                    return true;
                }
                finally { context[plan.SharedValue] = previous; }
            }
            catch (Exception ex) { return Fail(ability, "native budget query failed: " + ex.GetType().Name); }
        }

        // ★ Fusion coverage C8: the payload of an ordinary pool plan, or null when the cast has none or no readable plan.
        private static HashSet<GameAction> OrdinaryPayload(AbilityData ability, AbilityProfile profile)
        {
            if (!Plans.TryGetValue(ability.Blueprint, out var plan))
                Plans[ability.Blueprint] = plan = Build(ability.Blueprint, profile);
            if (plan.Failure != null)
            {
                if (!plan.Reported)
                {
                    plan.Reported = true;
                    Main.Log($"[HD] {ability.Name}: Hit Dice pool forecast unavailable ({plan.Failure}); its range estimate is kept");
                }
                return null;
            }
            return plan.Payload.Count > 0 ? plan.Payload : null;
        }

        private static HashSet<GameAction> FusedPayload(AbilityData ability, AbilityProfile profile)
        {
            if (profile.Fusion == null || !profile.FusionSharedHd) return null;
            var plan = FusedPlanOf(ability, profile);
            return plan.Failure == null && plan.Payload.Count > 0 ? plan.Payload : null;
        }

        /// <summary>★ Fusion coverage C8: <paramref name="action"/> is a kill or damage of a fused cast's readable pool.</summary>
        internal static bool FusedPools(AbilityData ability, AbilityProfile profile, GameAction action) =>
            action != null && ability?.Blueprint != null && FusedPayload(ability, profile)?.Contains(action) == true;

        /// <summary>
        /// ★ Fusion coverage C8. Native (AbilityExecutionProcess.ScheduleEffect, AbilityApproachingEffect.TickApproaching): a
        /// spreading area reaches each recipient once its front has travelled the 3D distance from the clicked target's point;
        /// those it reaches in the same frame follow the selector's order. So: distance order, the selector's for ties.
        /// </summary>
        private static List<UnitEntityData> SpreadOrder(List<UnitEntityData> selected, TargetWrapper aim)
        {
            var at = aim.Point;
            return selected.Select((u, i) => new KeyValuePair<int, UnitEntityData>(i, u))
                .OrderBy(p => (p.Value.Position - at).magnitude).ThenBy(p => p.Key).Select(p => p.Value).ToList();
        }

        /// <summary>
        /// One recipient's visit as a function of the pool left. The graph reads the pool only through "level &lt;= limit +
        /// pool" (SafeChecker), so a visit is the same for every pool between two values where one of those flips (pool =
        /// level - limit): it is read once per interval, by the native conditions, never once per pool value.
        /// </summary>
        private static SharedHdBudget.Recipient RecipientOf(AbilityData ability, AbilityExecutionContext context, AbilitySharedValue key,
            Node root, bool ignoreCaster, int[] limits, UnitEntityData recipient, int top)
        {
            int cost = recipient.Descriptor.Progression.CharacterLevel;
            float pass = recipient == ability.Caster.Unit ? 1f
                : CombatMath.SpellResistancePass(ability.Caster.Unit, recipient, ability);
            if (ignoreCaster && recipient == ability.Caster.Unit)
                return new SharedHdBudget.Recipient(pass, _ => new SharedHdBudget.Visit(false, false, cost));
            var flips = (limits ?? Array.Empty<int>()).Select(h => cost - h).Where(b => b > 0 && b <= top).Distinct().OrderBy(b => b).ToArray();
            var seen = new SharedHdBudget.Visit?[flips.Length + 1];
            return new SharedHdBudget.Recipient(pass, remaining =>
            {
                if (remaining < 0 || remaining > top) throw new InvalidOperationException("pool state out of range");
                int interval = 0;
                while (interval < flips.Length && remaining >= flips[interval]) interval++;
                if (seen[interval].HasValue) return seen[interval].Value;
                int saved = context[key];
                try
                {
                    context[key] = remaining;
                    var state = new ReadState { Context = context, SharedValue = key, Cost = cost };
                    using (context.GetDataScope(new TargetWrapper(recipient))) root.Read(state);
                    if (state.Debits > 1 || state.ReachesBuff != (state.Debits == 1))
                        throw new InvalidOperationException("shared debit is not paired with the control save");
                    var visit = new SharedHdBudget.Visit(state.ReachesBuff, state.Debits == 1, cost);
                    seen[interval] = visit;
                    return visit;
                }
                finally { context[key] = saved; }
            });
        }

        private static bool Fail(AbilityData ability, string reason)
        {
            if (ReportedFailures.Add(ability.Blueprint))
                Main.Log($"[HD] {ability.Name}: shared-HD pool credit withheld ({reason})");
            return true;
        }

        private static Plan Build(BlueprintAbility blueprint, AbilityProfile profile)
        {
            var plan = new Plan();
            try
            {
                var selectors = blueprint.GetComponents<AbilityTargetsAround>().ToArray();
                var actions = blueprint.GetComponents<AbilityEffectRunAction>().ToArray();
                var deliveries = blueprint.GetComponents<AbilityDeliverEffect>().ToArray();
                if (selectors.Length != 1 || selectors[0].GetType() != typeof(AbilityTargetsAround) ||
                    blueprint.GetComponents<AbilitySelectTarget>().Count() != 1 ||
                    actions.Length != 1 || actions[0].GetType() != typeof(AbilityEffectRunAction) ||
                    actions[0].SavingThrowType != SavingThrowType.Unknown || profile.NeedsAttackRoll ||
                    blueprint.StickyTouch != null || blueprint.GetComponents<AbilityApplyEffect>().Count() != 1)
                    throw new InvalidOperationException("requires one ordinary native around-selector/control action");
                // A chain/projectile/custom delivery may apply the same effect repeatedly at different anchors or
                // in another order. Native delay only yields MainTarget once, after waiting; it preserves this aim.
                if (deliveries.Length > 1 || deliveries.Any(d => d.GetType() != typeof(AbilityDeliverDelay)) ||
                    blueprint.GetComponents<AbilityCustomLogic>().Any())
                    throw new InvalidOperationException("custom or repeated delivery is outside this forecast");
                plan.Selector = selectors[0];
                plan.IgnoreCaster = actions[0].IgnoreCaster;
                var builder = new Builder { SaveType = profile.Save };
                if (SelectorCondition == null || !SafeChecker(SelectorCondition.GetValue(plan.Selector) as ConditionsChecker, builder))
                    throw new InvalidOperationException("unsupported native selector condition");
                // Select is lazy during actual delivery. A budget-dependent selector would observe earlier debits;
                // materializing its initial recipients would change those semantics. Stock Sleep has no such guard.
                if (builder.Conditions.Count != 0)
                    throw new InvalidOperationException("shared-HD selector conditions are outside this forecast");
                // ★ Fusion coverage C8: a spreading area (Circle of Death) reaches its recipients in distance order (SpreadOrder).
                plan.Spread = plan.Selector.GetSpreadSpeed().Meters != 0f;
                plan.Root = ReadSequence(actions[0].Actions, builder, 0);
                if (builder.Conditions.Count != 1 || builder.Debits.Count != 1 || !builder.Conditions.SetEquals(builder.Debits) ||
                    !(ControlPool(builder, profile) || DamagePool(builder, profile, fused: false)))
                    throw new InvalidOperationException("requires one shared key and one dependent target buff");
                plan.SharedValue = builder.Conditions.Single();
                var calculators = blueprint.GetComponents<ContextCalculateSharedValue>().Where(v => v.ValueType == plan.SharedValue).ToArray();
                if (calculators.Length != 1 || calculators[0].GetType() != typeof(ContextCalculateSharedValue) ||
                    calculators[0].Value == null || calculators[0].Value.GetType() != typeof(ContextDiceValue) ||
                    ReadsShared(calculators[0].Value.DiceCountValue) || ReadsShared(calculators[0].Value.BonusValue))
                    throw new InvalidOperationException("requires one native shared-value source");
                plan.Calculator = calculators[0];
                plan.Limits = builder.Limits.ToArray();
                plan.Buffs.UnionWith(builder.Buffs);
                plan.Payload.UnionWith(builder.Payload);
            }
            catch (Exception ex) { plan.Failure = ex.Message; }
            return plan;
        }

        // One dependent control buff (Sleep), or (★ Fusion coverage C8) kills and damage only (Circle of Death): every damage action
        // of an ordinary cast is in the pool's payload, so its forecast covers the whole of it.
        private static bool ControlPool(Builder builder, AbilityProfile profile) =>
            builder.Buffs.Count == 1 && builder.Payload.Count == 0 && builder.Buffs.SetEquals(profile.SharedHdBuffs);

        private static bool DamagePool(Builder builder, AbilityProfile profile, bool fused) =>
            builder.Buffs.Count == 0 && builder.Payload.Count > 0 && profile.SharedHdBuffs.Count == 0 &&
            builder.Payload.All(profile.Damage.Contains) && (fused || profile.Damage.All(builder.Payload.Contains) && profile.TargetBuffs.Count == 0);

        private static Node ReadSequence(ActionList list, Builder builder, int depth)
        {
            if (depth > 12) throw new InvalidOperationException("shared graph is too deep");
            var nodes = new List<Node>();
            foreach (var action in list?.Actions ?? Array.Empty<GameAction>())
            {
                if (++builder.Nodes > 128) throw new InvalidOperationException("shared graph is too large");
                if (action == null) throw new InvalidOperationException("null action in shared graph");
                Type type = action.GetType();
                if (type == typeof(ContextActionSpawnFx)) continue;
                if (type == typeof(Conditional))
                {
                    var branch = (Conditional)action;
                    if (!SafeChecker(branch.ConditionsChecker, builder)) throw new InvalidOperationException("unsupported condition");
                    nodes.Add(new Branch { Checker = branch.ConditionsChecker,
                        True = ReadSequence(branch.IfTrue, builder, depth + 1), False = ReadSequence(branch.IfFalse, builder, depth + 1) });
                }
                else if (type == typeof(ContextActionSavingThrow))
                {
                    var save = (ContextActionSavingThrow)action;
                    if ((!builder.Fused && (save.Type != builder.SaveType || save.Type == SavingThrowType.Unknown)) || save.FromBuff ||
                        save.HasCustomDC || save.UseDCFromContextSavingThrow || save.MakeSaveAlsoForMagicHack ||
                        SaveAdjustments == null || (SaveAdjustments.GetValue(save) as Array)?.Length > 0)
                        throw new InvalidOperationException("unsupported control save");
                    var saved = save.Actions?.Actions;
                    if (saved?.Length != 1 || saved[0]?.GetType() != typeof(ContextActionConditionalSaved))
                        throw new InvalidOperationException("save requires one native failed-buff branch");
                    var branches = (ContextActionConditionalSaved)saved[0];
                    // ★ Fusion coverage C8: an outcome that kills or deals damage (Circle of Death: a kill at 9 Hit Dice or less,
                    // else 100 damage) instead of applying one buff. Its own conditions do not read the pool; the damage
                    // appraisal prices them, and the forecast only says whether the pool still covers the recipient.
                    if (IsPayload(branches.Failed?.Actions, 0) && IsPayload(branches.Succeed?.Actions, 0))
                    {
                        int before = builder.Payload.Count;
                        ReadPayload(branches.Failed?.Actions, builder, depth + 1);
                        ReadPayload(branches.Succeed?.Actions, builder, depth + 1);
                        if (builder.Payload.Count == before) throw new InvalidOperationException("unsupported save outcomes");
                        nodes.Add(new Save());
                        continue;
                    }
                    if ((branches.Succeed?.Actions?.Length ?? 0) != 0 || branches.Failed?.Actions?.Length != 1 ||
                        branches.Failed.Actions[0]?.GetType() != typeof(ContextActionApplyBuff))
                        throw new InvalidOperationException("unsupported save outcomes");
                    var apply = (ContextActionApplyBuff)branches.Failed.Actions[0];
                    if (apply.ToCaster || apply.Buff == null) throw new InvalidOperationException("not a target control buff");
                    if (!EquivalentDuration(apply, builder.Application ?? apply))
                        throw new InvalidOperationException("shared control has unsupported or branch-dependent durations");
                    builder.Application = apply;
                    builder.Buffs.Add(apply.Buff);
                    nodes.Add(new Save());
                }
                else if (type == typeof(ContextActionChangeSharedValue))
                {
                    var change = (ContextActionChangeSharedValue)action;
                    if (change.Type != SharedValueChangeType.SubHD) throw new InvalidOperationException("unknown shared-value mutation");
                    builder.Debits.Add(change.SharedValue);
                    nodes.Add(new Debit());
                }
                else throw new InvalidOperationException("unsupported action " + type.Name);
            }
            if (nodes.Count == 0) return new Empty();
            if (nodes.Count == 1 && nodes[0] is Branch) return nodes[0];
            if (nodes.Count == 2 && nodes[0] is Save && nodes[1] is Debit) return new SaveThenDebit();
            throw new InvalidOperationException("requires a save followed by unconditional shared-HD debit");
        }

        // ★ Fusion coverage C8: a save outcome of kills and damage only, behind conditions that do not read a shared value.
        private static bool IsPayload(GameAction[] actions, int depth)
        {
            if (actions == null || actions.Length == 0) return true;
            if (depth > 6) return false;
            foreach (var action in actions)
            {
                if (action == null) return false;
                var type = action.GetType();
                if (type == typeof(ContextActionSpawnFx) || type == typeof(ContextActionKill)) continue;
                if (type == typeof(ContextActionDealDamage))
                {
                    var deal = (ContextActionDealDamage)action;
                    if (deal.ReadPreRolledFromSharedValue || deal.WriteResultToSharedValue || deal.WriteRawResultToSharedValue ||
                        deal.WriteCriticalToSharedValue || ReadsShared(deal.Value?.DiceCountValue) || ReadsShared(deal.Value?.BonusValue))
                        return false;
                    continue;
                }
                if (type == typeof(Conditional))
                {
                    var branch = (Conditional)action;
                    var conditions = branch.ConditionsChecker?.Conditions;
                    if (conditions == null || conditions.Length > 16) return false;
                    foreach (var c in conditions)
                    {
                        if (c == null) return false;
                        if (c.GetType() == typeof(ContextConditionHitDice)) { if (((ContextConditionHitDice)c).AddSharedValue) return false; }
                        else if (!SafeConditions.Contains(c.GetType())) return false;
                    }
                    if (!IsPayload(branch.IfTrue?.Actions, depth + 1) || !IsPayload(branch.IfFalse?.Actions, depth + 1)) return false;
                    continue;
                }
                return false;
            }
            return true;
        }

        private static bool ReadsShared(ContextValue value) => value != null && value.ValueType == ContextValueType.Shared;

        private static void ReadPayload(GameAction[] actions, Builder builder, int depth)
        {
            if (actions == null) return;
            if (depth > 12) throw new InvalidOperationException("shared graph is too deep");
            foreach (var action in actions)
            {
                if (++builder.Nodes > 128) throw new InvalidOperationException("shared graph is too large");
                if (action is ContextActionKill || action is ContextActionDealDamage) builder.Payload.Add(action);
                else if (action is Conditional branch)
                {
                    ReadPayload(branch.IfTrue?.Actions, builder, depth + 1);
                    ReadPayload(branch.IfFalse?.Actions, builder, depth + 1);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // ★ Fusion F7 (docs/FUSION_SLEEP_POOL.md): the same forecast for a fused cast.
        // Native facts (decompiled): AbilityExecutionContext is built on the HOLDER ability (ability.Blueprint), and
        // MechanicsContext.RecalculateSharedValues reads that blueprint's ContextCalculateSharedValue components. The
        // pool is therefore the holder's own roll (stock slots: 2d6), not the source spell's number (Sleep: 4). The
        // deliver blueprint supplies the recipients (AbilityExecutionProcess: its AbilityTargetsAround, else the one
        // delivery target), and both source payloads run for each recipient under one root save.
        private enum FusedDelivery { Around, Single }

        private sealed class FusedPlan
        {
            internal string Failure;
            internal bool Reported;
            internal FusedDelivery Delivery;
            internal AbilityTargetsAround Selector;
            internal AbilitySharedValue SharedValue;
            internal Node Root;
            internal bool IgnoreCaster;
            internal bool Rolled;          // the game maximizes only a die above d1 (ContextValueHelper.CalculateDiceValue)
            internal int MaximizedBudget;
            internal int[] Budgets;        // ascending
            internal float[] Chances;
            internal bool Empowerable; // Native tests die type, even with zero dice.
            internal int[] EmpoweredBudgets;
            internal float[] EmpoweredChances;
            internal int EmpoweredMaximizedBudget;
            internal string EmpoweredFailure;
            internal bool Spread;          // ★ Fusion coverage C8: a spreading deliver blueprint (Circle of Death)
            internal int[] Limits;
            internal readonly HashSet<GameAction> Payload = new HashSet<GameAction>();
        }

        /// <summary>Null when the fused pool can be forecast from metadata; else why not. Cached on the profile.</summary>
        internal static string FusedRefusal(AbilityData ability, AbilityProfile profile)
        {
            if (ability?.Blueprint == null || profile?.Fusion == null || !profile.FusionSharedHd) return null;
            if (ability.ShadowSpellSettings != null) return "shadow copy";
            return FusedPlanOf(ability, profile).Failure;
        }

        // The profile is specific to one holder and one configuration (AbilityProfile.FusionEntry.Matches).
        private static FusedPlan FusedPlanOf(AbilityData ability, AbilityProfile profile)
        {
            if (profile.FusionSharedHdPlan is FusedPlan plan) return plan;
            profile.FusionSharedHdPlan = plan = BuildFused(ability.Blueprint, profile);
            return plan;
        }

        private static AbilityEffectRunAction PayloadOf(BlueprintAbility spell) =>
            spell?.GetComponent<AbilityEffectRunAction>() ??
            spell?.GetComponent<AbilityEffectStickyTouch>()?.TouchDeliveryAbility?.GetComponent<AbilityEffectRunAction>();

        private static bool UsesPool(GameAction[] actions, int depth)
        {
            if (actions == null) return false;
            if (depth > 12) throw new InvalidOperationException("shared graph is too deep");
            foreach (var action in actions)
            {
                if (action == null) continue;
                if (action is ContextActionChangeSharedValue) return true;
                if (action is Conditional branch && branch.ConditionsChecker?.Conditions?.Any(c =>
                        c is ContextConditionHitDice hd && hd.AddSharedValue) == true) return true;
                // A damage action that stores its result would overwrite the pool between recipients.
                if (action is ContextActionDealDamage deal && (deal.WriteResultToSharedValue ||
                        deal.WriteRawResultToSharedValue || deal.WriteCriticalToSharedValue)) return true;
                foreach (var child in AbilityProfile.ChildActionLists(action))
                    if (UsesPool(child, depth + 1)) return true;
            }
            return false;
        }

        private static FusedPlan BuildFused(BlueprintAbility holder, AbilityProfile profile)
        {
            var plan = new FusedPlan();
            try
            {
                var hack = profile.Fusion;
                if (profile.Save == SavingThrowType.Unknown)
                    throw new InvalidOperationException("no root save: the pool spell's own save would be a second roll");
                if (hack.IsTouch || hack.AdditionalAoeBlueprint != null)
                    throw new InvalidOperationException("touch or additional-area delivery is outside this forecast");
                var payloads = new[] { PayloadOf(hack.Spell1), PayloadOf(hack.Spell2) };
                if (payloads.Any(p => p == null)) throw new InvalidOperationException("source spell without a native RunAction payload");
                var pooled = payloads.Where(p => UsesPool(p.Actions?.Actions, 0)).ToArray();
                if (pooled.Length != 1)
                    throw new InvalidOperationException("requires exactly one source spell that reads or writes a shared value");
                var builder = new Builder { SaveType = profile.Save, Fused = true };
                plan.Root = ReadSequence(pooled[0].Actions, builder, 0);
                if (builder.Conditions.Count != 1 || builder.Debits.Count != 1 || !builder.Conditions.SetEquals(builder.Debits) ||
                    !(ControlPool(builder, profile) || DamagePool(builder, profile, fused: true)))
                    throw new InvalidOperationException("requires one shared key and one dependent target buff");
                plan.SharedValue = builder.Conditions.Single();
                plan.Limits = builder.Limits.ToArray();
                plan.Payload.UnionWith(builder.Payload);

                var calculators = holder.GetComponents<ContextCalculateSharedValue>().Where(v => v.ValueType == plan.SharedValue).ToArray();
                if (calculators.Length != 1 || calculators[0].GetType() != typeof(ContextCalculateSharedValue))
                    throw new InvalidOperationException("the holder ability has no single native source for the pool");
                var value = calculators[0].Value;
                if (value?.DiceCountValue == null || value.BonusValue == null ||
                    value.GetType() != typeof(ContextDiceValue) ||
                    value.DiceCountValue.ValueType != ContextValueType.Simple || value.BonusValue.ValueType != ContextValueType.Simple)
                    throw new InvalidOperationException("the holder's pool is not a fixed dice expression");
                Distribution(plan, value.DiceCountValue.Value, (int)value.DiceType, value.BonusValue.Value, calculators[0].Modifier);

                var delivery = hack.DeliverBlueprint;
                // AbilityEffectRunAction.Apply: a fused cast takes IgnoreCaster from the deliver blueprint.
                plan.IgnoreCaster = delivery.GetComponent<AbilityEffectRunAction>()?.IgnoreCaster ?? false;
                if (delivery.GetComponents<AbilityCustomLogic>().Any())
                    throw new InvalidOperationException("custom delivery is outside this forecast");
                var selectors = delivery.GetComponents<AbilitySelectTarget>().ToArray();
                var deliveries = delivery.GetComponents<AbilityDeliverEffect>().ToArray();
                if (selectors.Length == 1 && selectors[0].GetType() == typeof(AbilityTargetsAround))
                {
                    if (profile.NeedsAttackRoll || deliveries.Length > 1 || deliveries.Any(d => d.GetType() != typeof(AbilityDeliverDelay)))
                        throw new InvalidOperationException("custom or repeated delivery is outside this forecast");
                    plan.Selector = (AbilityTargetsAround)selectors[0];
                    var selection = new Builder();
                    if (SelectorCondition == null || !SafeChecker(SelectorCondition.GetValue(plan.Selector) as ConditionsChecker, selection) ||
                        selection.Conditions.Count != 0)
                        throw new InvalidOperationException("unsupported native selector condition");
                    plan.Spread = plan.Selector.GetSpreadSpeed().Meters != 0f;
                    plan.Delivery = FusedDelivery.Around;
                }
                else if (selectors.Length == 0)
                {
                    // One clicked recipient, reached directly or by exactly one simple projectile.
                    if (deliveries.Length > 1 || deliveries.Length == 1 && !(deliveries[0] is AbilityDeliverProjectile shot &&
                            shot.GetType() == typeof(AbilityDeliverProjectile) && shot.Type == AbilityProjectileType.Simple &&
                            !shot.UseMaxProjectilesCount && shot.Projectiles.Length == 1))
                        throw new InvalidOperationException("cone, line, chain or repeated delivery is outside this forecast");
                    plan.Delivery = FusedDelivery.Single;
                }
                else throw new InvalidOperationException("unsupported native recipient selection");
            }
            catch (Exception ex) { plan.Failure = ex is InvalidOperationException ? ex.Message : "pool reader " + ex.GetType().Name; }
            return plan;
        }

        // Exact distribution of (count d sides + bonus) * modifier, truncated as the game does.
        private static void Distribution(FusedPlan plan, int count, int sides, int bonus, double modifier)
        {
            var sums = DiceSums(count, sides);
            plan.Rolled = sides > 1 && count > 0;
            plan.Empowerable = sides > 1;
            ReadRoll(sums, count, sides, bonus, modifier, false, out plan.Budgets, out plan.Chances, out plan.MaximizedBudget);
            // An unsupported Empower bound must not withhold an ordinary supported configuration.
            try { ReadRoll(sums, count, sides, bonus, modifier, true, out plan.EmpoweredBudgets, out plan.EmpoweredChances, out plan.EmpoweredMaximizedBudget); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is OverflowException || ex is ArgumentException)
            { plan.EmpoweredFailure = "empowered " + ex.Message; }
        }

        // ★ Fusion coverage C8: up to 25 dice (Circle of Death: d4 per caster level, 25 with Intensified).
        private static double[] DiceSums(int count, int sides)
        {
            if (count < 0 || count > 40 || sides < 0 || sides > 20) throw new InvalidOperationException("pool dice are outside the forecast bound");
            int faces = sides >= 1 ? sides : 0, dice = faces == 0 ? 0 : count;
            var sums = new double[dice * faces + 1];
            sums[0] = 1.0;
            for (int d = 0; d < dice; d++)
            {
                var next = new double[sums.Length];
                for (int s = 0; s <= d * faces; s++)
                {
                    if (sums[s] == 0.0) continue;
                    for (int f = 1; f <= faces; f++) next[s + f] += sums[s] / faces;
                }
                sums = next;
            }
            return sums;
        }

        private static void ReadRoll(double[] sums, int count, int sides, int bonus, double modifier, bool empowered,
                                     out int[] budgets, out float[] chances, out int maximized)
        {
            bool empowerable = sides > 1;
            var byBudget = new SortedDictionary<int, double>();
            for (int s = 0; s < sums.Length; s++)
            {
                if (sums[s] <= 0.0) continue;
                int budget = SharedHdBudget.PoolTotal(s, bonus, modifier, empowerable, empowered);
                if (budget < 0 || budget > SharedHdBudget.MaxBudget) throw new InvalidOperationException("pool is outside the forecast bound");
                byBudget.TryGetValue(budget, out double p);
                byBudget[budget] = p + sums[s];
            }
            budgets = byBudget.Keys.ToArray(); chances = byBudget.Values.Select(p => (float)p).ToArray();
            maximized = SharedHdBudget.PoolTotal(count * Math.Max(0, sides), bonus, modifier, empowerable, empowered);
            if (maximized < 0 || maximized > SharedHdBudget.MaxBudget) throw new InvalidOperationException("pool is outside the forecast bound");
        }

        // ★ Fusion coverage C8: an ordinary cast's pool as the game rolls it for this cast (ContextValueHelper.CalculateDiceValue:
        // Maximize takes every die at its top above d1, Empower adds half of roll plus bonus). Its dice count is read from this
        // cast's context (Circle of Death: its caster level rank). Small cache by the values that decide it.
        private struct RollKey : IEquatable<RollKey>
        {
            internal int Count, Sides, Bonus; internal double Modifier; internal bool Empowered, Maximized;
            public bool Equals(RollKey o) => Count == o.Count && Sides == o.Sides && Bonus == o.Bonus &&
                Modifier.Equals(o.Modifier) && Empowered == o.Empowered && Maximized == o.Maximized;
            public override bool Equals(object obj) => obj is RollKey k && Equals(k);
            public override int GetHashCode() => ((Count * 31 + Sides) * 31 + Bonus) * 31 + Modifier.GetHashCode() * 7 + (Empowered ? 2 : 0) + (Maximized ? 1 : 0);
        }
        private static readonly Dictionary<RollKey, KeyValuePair<int[], float[]>> Rolls = new Dictionary<RollKey, KeyValuePair<int[], float[]>>();

        private static bool TryOrdinaryRoll(Plan plan, MechanicsContext context, out int[] budgets, out float[] chances, out string failure)
        {
            budgets = null; chances = null; failure = null;
            var value = plan.Calculator.Value;
            int sides = (int)value.DiceType;
            var key = new RollKey
            {
                Count = value.DiceCountValue?.Calculate(context) ?? 0, Sides = sides, Bonus = value.BonusValue?.Calculate(context) ?? 0,
                Modifier = plan.Calculator.Modifier,
                Empowered = sides > 1 && context.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Empower)
            };
            key.Maximized = sides > 1 && key.Count > 0 && context.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Maximize);
            if (Rolls.TryGetValue(key, out var cached)) { budgets = cached.Key; chances = cached.Value; return true; }
            try
            {
                var sums = DiceSums(key.Count, sides);
                ReadRoll(sums, key.Count, sides, key.Bonus, key.Modifier, key.Empowered, out budgets, out chances, out int maximized);
                if (key.Maximized) { budgets = new[] { maximized }; chances = new[] { 1f }; }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is OverflowException || ex is ArgumentException)
            { failure = ex.Message; return false; }
            if (Rolls.Count > 256) Rolls.Clear();
            Rolls[key] = new KeyValuePair<int[], float[]>(budgets, chances);
            return true;
        }

        private static bool TryForFusedAim(AbilityData ability, AbilityProfile profile, TargetWrapper aim,
            IReadOnlyCollection<BlueprintBuff> controlled, ref Forecast forecast)
        {
            var plan = FusedPlanOf(ability, profile);
            if (plan.Failure != null) return FailFused(ability, plan, plan.Failure);
            try
            {
                // One actual-aim context on the holder ability, as the game builds it; never activated or cached.
                var context = CombatMath.CreateContext(ability, aim);
                if (context == null) return FailFused(ability, plan, "no read-only cast context");
                int previous = context[plan.SharedValue];
                try
                {
                    List<UnitEntityData> selected;
                    if (plan.Delivery == FusedDelivery.Around)
                    {
                        using (context.GetDataScope(aim))
                            selected = plan.Selector.Select(context, aim).Select(t => t?.Unit)
                                .Take(SharedHdBudget.MaxRecipients + 1).ToList();
                        if (selected.Count > SharedHdBudget.MaxRecipients || selected.Any(u => u == null) ||
                            selected.Distinct().Count() != selected.Count)
                            return FailFused(ability, plan, "unsupported native recipient set");
                        if (plan.Spread) selected = SpreadOrder(selected, aim);
                    }
                    else
                    {
                        if (aim.Unit == null) return FailFused(ability, plan, "no clicked recipient");
                        selected = new List<UnitEntityData> { aim.Unit };
                    }
                    // Use effective params, including automatic metamagic. This context already exists.
                    bool empowered = plan.Empowerable && context.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Empower);
                    bool maximized = plan.Rolled && context.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Maximize);
                    if (empowered && plan.EmpoweredFailure != null) return FailFused(ability, plan, plan.EmpoweredFailure);
                    var budgets = empowered ? plan.EmpoweredBudgets : plan.Budgets;
                    var chances = empowered ? plan.EmpoweredChances : plan.Chances;
                    int maximum = empowered ? plan.EmpoweredMaximizedBudget : plan.MaximizedBudget;
                    int top = maximized ? maximum : budgets[budgets.Length - 1];
                    var recipients = new List<SharedHdBudget.Recipient>(selected.Count);
                    foreach (var unit in selected)
                        recipients.Add(RecipientOf(ability, context, plan.SharedValue, plan.Root, plan.IgnoreCaster, plan.Limits, unit, top));
                    // ★ Fusion coverage C8: one pass over the pool's whole distribution (the chain is linear in it).
                    var start = maximized ? new[] { top } : budgets;
                    var weights = maximized ? new[] { 1f } : chances;
                    if (!SharedHdBudget.TryForecast(start, weights, recipients, out var total))
                        return FailFused(ability, plan, "unsupported shared-budget transition or work bound");
                    var values = new Dictionary<UnitEntityData, float>();
                    for (int i = 0; i < selected.Count; i++) values.Add(selected[i], Math.Min(1f, total[i]));
                    forecast = new Forecast(true, controlled, values, plan.Payload.Count > 0 ? plan.Payload : null);
                    return true;
                }
                finally { context[plan.SharedValue] = previous; }
            }
            catch (Exception ex) { return FailFused(ability, plan, "native budget query failed: " + ex.GetType().Name); }
        }

        private static bool FailFused(AbilityData ability, FusedPlan plan, string reason)
        {
            if (!plan.Reported)
            {
                plan.Reported = true;
                Main.Log($"[HD] {ability.Name}: fused shared-HD pool credit withheld ({reason})");
            }
            return true;
        }

        private static bool SafeChecker(ConditionsChecker checker, Builder builder)
        {
            if (checker?.Conditions == null || checker.Conditions.Length > 16) return false;
            foreach (var condition in checker.Conditions)
            {
                if (condition == null) return false;
                if (condition.GetType() == typeof(ContextConditionHitDice))
                {
                    var hd = (ContextConditionHitDice)condition;
                    if (!hd.AddSharedValue || hd.HitDice != 0) return false;
                    builder.Conditions.Add(hd.SharedValue);
                    builder.Limits.Add(hd.HitDice);
                }
                else if (!SafeConditions.Contains(condition.GetType())) return false;
            }
            return true;
        }

        // Duration reuse is keyed by buff. Recognize only equivalent duration expressions across its branches,
        // so eligibility cannot accidentally select the longest of several different-duration applications.
        private static bool EquivalentDuration(ContextActionApplyBuff a, ContextActionApplyBuff b)
        {
            if (a.SameDuration || b.SameDuration || a.Permanent != b.Permanent ||
                a.UseDurationSeconds != b.UseDurationSeconds) return false;
            if (a.Permanent) return true;
            if (a.UseDurationSeconds) return a.DurationSeconds == b.DurationSeconds;
            var x = a.DurationValue;
            var y = b.DurationValue;
            return x != null && y != null && x.GetType() == typeof(ContextDurationValue) &&
                y.GetType() == typeof(ContextDurationValue) && x.Rate == y.Rate && x.DiceType == y.DiceType &&
                x.IsExtendable == y.IsExtendable && SameValue(x.DiceCountValue, y.DiceCountValue) &&
                SameValue(x.BonusValue, y.BonusValue);
        }

        private static bool SameValue(ContextValue a, ContextValue b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.GetType() != typeof(ContextValue) || b.GetType() != typeof(ContextValue) || a.ValueType != b.ValueType)
                return false;
            return a.ValueType == ContextValueType.Simple ? a.Value == b.Value :
                a.ValueType == ContextValueType.Rank && a.ValueRank == b.ValueRank;
        }
    }
}
