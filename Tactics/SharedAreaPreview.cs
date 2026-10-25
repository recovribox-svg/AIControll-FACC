// ★ v0.3.22: shared target budgets (Sleep) in native recipient order; no effects or dice are executed.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    internal sealed class SharedAreaPreview : IDisposable
    {
        [ThreadStatic] private static SharedAreaPreview current;
        private readonly SharedAreaPreview previous;
        private readonly AbilityData ability;
        private readonly TargetWrapper aim;
        private readonly BlueprintBuff buff;
        private readonly Dictionary<UnitEntityData, float> shares;

        private SharedAreaPreview(AbilityData ability, BlueprintBuff buff, Dictionary<UnitEntityData, float> shares)
        {
            this.ability = ability; this.buff = buff; this.shares = shares;
            aim = AbilityProfile.CurrentAim; previous = current; current = this;
        }
        internal static bool TryShare(AbilityData ability, BlueprintBuff buff, UnitEntityData target, out float share)
        {
            var active = current;
            share = 1f;
            if (active == null || !ReferenceEquals(active.ability, ability) || active.buff != buff ||
                !ReferenceEquals(active.aim, AbilityProfile.CurrentAim)) return false;
            share = target != null && active.shares.TryGetValue(target, out float found) ? found : 0f;
            return true;
        }
        internal static SharedAreaPreview Begin(AbilityData ability, AbilityProfile profile, TargetWrapper aim)
        {
            if (profile == null || ability == null || aim == null) return null;
            try
            {
                var plan = profile.SharedAreaPlan;
                if (plan == null || ability.MagicHackData != null) return null;
                var context = CombatMath.CreateContext(ability, aim);
                if (context == null) return null;
                AbilityProfile.SharedValueRange(ability, context, plan.Pool, out int lo, out int initial, out int hi);
                // Random budgets need a distribution of their own. Do not silently treat an average as exact.
                if (lo != hi) return null;
                context[plan.Pool] = initial;
                // The selector supplies radius, side, Selective handling, target filter AND recipient order.
                var recipients = plan.Selector.Select(context, aim).ToList();
                var shares = Forecast(plan, context, recipients, initial,
                    u => CombatMath.SpellResistancePass(ability.Caster.Unit, u, ability));
                AbilityDiscovery.ReportOnce($"shared-area-budget|{ability.Caster.Unit.UniqueId}|{ability.Blueprint.AssetGuid}|{initial}",
                    () => $"[AreaBudget] {ability.Caster.Unit.CharacterName}: {ability.Name} previews native recipient order and SubHD; initial {plan.Pool}={initial}");
                return new SharedAreaPreview(ability, plan.Buff, shares);
            }
            catch (Exception ex)
            {
                AbilityDiscovery.ReportOnce($"shared-area-preview|{ability.Blueprint.AssetGuid}",
                    () => $"[AreaBudget] {ability.Blueprint.name}: native preview unavailable; existing per-target estimate retained ({ex.Message})");
                return null;
            }
        }

        // Each state is a remaining native shared value, weighted by earlier recipients' SR outcomes. A save
        // still spends Sleep's budget; only an SR/immunity rejection before actions leaves that state untouched.
        internal static Dictionary<UnitEntityData, float> Forecast(Plan plan, MechanicsContext context,
            IEnumerable<TargetWrapper> recipients, int initial, Func<UnitEntityData, float> resistancePass)
        {
            int saved = context[plan.Pool];
            var shares = new Dictionary<UnitEntityData, float>();
            var states = new Dictionary<int, float> { [initial] = 1f };
            try
            {
                foreach (var recipient in recipients)
                {
                    var unit = recipient?.Unit;
                    if (unit == null) continue;
                    float pass = Mathf.Clamp01(resistancePass(unit));
                    float share = 0f;
                    var next = new Dictionary<int, float>();
                    foreach (var state in states)
                    {
                        context[plan.Pool] = state.Key;
                        bool applies;
                        using (context.GetDataScope(recipient)) applies = plan.Visit(context);
                        // The current recipient's SR/save probability is priced by EffectChance, once, later.
                        if (applies) share += state.Value;
                        Add(next, context[plan.Pool], state.Value * pass);
                        Add(next, state.Key, state.Value * (1f - pass));
                    }
                    shares[unit] = Mathf.Clamp01(share);
                    // Bound pathological modded state spaces; abandon the projection rather than truncate targets.
                    if (next.Count > 128) throw new InvalidOperationException("shared-budget state space exceeded");
                    states = next;
                }
                return shares;
            }
            finally { context[plan.Pool] = saved; }
        }
        private static void Add(Dictionary<int, float> states, int value, float weight)
        {
            if (weight <= 0f) return;
            states[value] = (states.TryGetValue(value, out float old) ? old : 0f) + weight;
        }
        public void Dispose() { current = previous; }

        // Deliberately narrow: one target buff, one deterministic SubHD pool, safe conditionals, and a saving
        // throw whose successful branch is empty and whose branches never change the pool. Unknown action
        // containers/writes fall back. The only native RunAction invoked is the detached shared-value write.
        internal sealed class Plan
        {
            internal AbilityTargetsAround Selector;
            internal AbilitySharedValue Pool;
            internal BlueprintBuff Buff;
            internal Func<MechanicsContext, bool> Visit;

            internal static Plan Create(AbilityProfile profile)
            {
                if (!profile.IsAoE || !profile.SelectsTargets || profile.IsKineticEffect || profile.TargetBuffs.Distinct().Count() != 1 ||
                    profile.DealsDamage || profile.Heals.Count != 0 || profile.CasterBuffs.Count != 0 || profile.SpawnedAreas.Any()) return null;
                var selector = profile.Blueprint.GetComponent<AbilityTargetsAround>();
                var run = profile.Blueprint.GetComponent<AbilityEffectRunAction>();
                if (selector?.GetType() != typeof(AbilityTargetsAround) || run?.GetType() != typeof(AbilityEffectRunAction) ||
                    run.Actions == null || run.IgnoreCaster ||
                    run.SavingThrowType != Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown) return null;
                var filter = HarmonyLib.AccessTools.Field(typeof(AbilityTargetsAround), "m_Condition")?.GetValue(selector) as ConditionsChecker;
                if (filter?.Conditions?.Any(c => !AbilityProfile.IsSafe(c)) == true) return null;
                AbilitySharedValue? pool = null;
                var buff = profile.TargetBuffs[0];
                try
                {
                    Func<MechanicsContext, bool> Compile(GameAction[] actions, bool inSave = false, int depth = 0)
                    {
                        if (depth > 24) throw new InvalidOperationException();
                        var steps = new List<Func<MechanicsContext, bool>>();
                        foreach (var action in actions ?? new GameAction[0])
                        {
                            if (action == null) continue;
                            if (action.GetType() == typeof(Conditional))
                            {
                                var conditional = (Conditional)action;
                                if (conditional.ConditionsChecker?.Conditions?.All(AbilityProfile.IsSafe) != true)
                                    throw new InvalidOperationException();
                                var yes = Compile(conditional.IfTrue?.Actions, inSave, depth + 1);
                                var no = Compile(conditional.IfFalse?.Actions, inSave, depth + 1);
                                steps.Add(ctx => AbilityProfile.Run(conditional.ConditionsChecker) ? yes(ctx) : no(ctx));
                            }
                            else if (action.GetType() == typeof(ContextActionChangeSharedValue))
                            {
                                var change = (ContextActionChangeSharedValue)action;
                                if (inSave || change.Type != SharedValueChangeType.SubHD || pool.HasValue && pool != change.SharedValue)
                                    throw new InvalidOperationException();
                                pool = change.SharedValue;
                                steps.Add(ctx => { change.RunAction(); return false; });
                            }
                            else if (action.GetType() == typeof(ContextActionApplyBuff))
                            {
                                var apply = (ContextActionApplyBuff)action;
                                if (apply.ToCaster || apply.Buff != buff) throw new InvalidOperationException();
                                steps.Add(ctx => true);
                            }
                            else if (action.GetType() == typeof(ContextActionSavingThrow))
                                steps.Add(Compile(((ContextActionSavingThrow)action).Actions?.Actions, true, depth + 1));
                            else if (action.GetType() == typeof(ContextActionConditionalSaved))
                            {
                                var saved = (ContextActionConditionalSaved)action;
                                if ((saved.Succeed?.Actions?.Length ?? 0) != 0) throw new InvalidOperationException();
                                steps.Add(Compile(saved.Failed?.Actions, true, depth + 1));
                            }
                            else if (action.GetType() != typeof(ContextActionSpawnFx)) throw new InvalidOperationException();
                        }
                        return ctx => { bool applies = false; foreach (var step in steps) applies |= step(ctx); return applies; };
                    }
                    var visit = Compile(run.Actions.Actions);
                    return pool.HasValue ? new Plan { Selector = selector, Pool = pool.Value, Buff = buff, Visit = visit } : null;
                }
                catch (InvalidOperationException) { return null; }
            }
        }
    }
}
