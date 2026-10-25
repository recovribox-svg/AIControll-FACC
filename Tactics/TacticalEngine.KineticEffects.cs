// ★ v0.3.22: substance effects contribute to the blast that delivers them, on the shared scoring scale.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static float KineticEffectValue(TacticalContext ctx, AbilityData blast, AbilityProfile delivery,
                                                ActionSlot slot, UnitEntityData target, out string note, out bool unsafeArea,
                                                IReadOnlyList<float> bladeHits = null)
        {
            note = null;
            unsafeArea = false;
            if (!IsOwnBlast(ctx.Unit.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>(), blast)) return 0f;
            float value = 0f, bestControl = 0f;
            var parts = new List<string>();
            var seen = new HashSet<AddKineticistInfusionDamageTrigger>();
            var sources = ctx.Unit.Facts.List.Where(f => f?.Active == true && f.Blueprint != null && f.MaybeContext != null)
                .Select(f => (Blueprint: f.Blueprint, Context: f.MaybeContext));
            var scope = KineticPreview.Current;
            if (scope?.Unit == ctx.Unit)
                sources = sources.Where(f => !scope.Controlled.Contains(f.Blueprint as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff))
                    .Concat(scope.Selected.Select(b => (Blueprint: (Kingmaker.Blueprints.Facts.BlueprintFact)b, Context: scope.Contexts[b])));
            foreach (var fact in sources)
            {
                foreach (var trigger in fact.Blueprint.GetComponents<AddKineticistInfusionDamageTrigger>())
                {
                    if (!seen.Add(trigger) || !TriggerMatches(trigger, blast, fact.Context)) continue;
                    var effect = AbilityProfile.ForKineticInfusion(blast.Blueprint, trigger);
                    if (effect == null) continue;
                    if (effect.Dispels && !NativeDispelPreview.CanQuery)
                        ReportOnce($"kinetic-unpriced-dispel|{fact.Blueprint.AssetGuid}",
                            $"[Kinetic] {fact.Blueprint.name}: dispel contribution 0; native preview unavailable or patched by another mod");
                    if (!effect.HasTargetEffects && effect.CasterBuffs.Count == 0) continue;
                    float added = 0f;
                    bool aim = ctx.AimCounted;
                    using (new KineticEffectContext(blast, fact.Context, fact.Blueprint.AssetGuid.ToString(), delivery, hitChances: bladeHits,
                        repeatCount: e => KineticAreaTicks(ctx, blast, delivery, e)))
                    {
                        // ScoreAbility owns target selection, saves, control, maneuvers and friendly fire.
                        // The effect scope supplies the separate delivery chance and source fact context.
                        float discount = KineticFollowUpSavings(ctx, blast, effect, target);
                        added = discount;
                        // Keep the shared area's friendly-fire veto even when its numerical value was zero.
                        if (effect.HasTargetEffects)
                        {
                            added = ScoreAbility(ctx, blast, effect, slot, target, out var why, out float control) + discount;
                            // Taking the strongest control package is conservative for disjoint recipients; it avoids
                            // awarding the same prevented actions separately to every simultaneously active infusion.
                            added -= Math.Max(0f, control);
                            bestControl = Math.Max(bestControl, control);
                            if (why == "friendly fire") unsafeArea = true;
                        }
                    }
                    ctx.AimCounted = aim || ctx.AimCounted;
                    value += added;
                    parts.Add($"{fact.Blueprint.name} non-control {added:F1}");
                }
            }
            if (parts.Count > 0) note = string.Join(", ", parts) + $"; combined control {bestControl:F1}";
            return value + bestControl;
        }

        private static float KineticDispelValue(TacticalContext ctx, AbilityData blast, AbilityProfile effect, UnitEntityData target)
        {
            if (!NativeDispelPreview.CanQueryFor(ctx.Unit, target)) return 0f;
            var source = KineticEffectContext.For(blast);
            var ordered = NativeDispelPreview.OrderedBuffs(target);
            if (source == null || ordered == null) return 0f;
            float total = 0f;
            foreach (var dispel in effect.DispelActions)
            {
                if (dispel.OneRollForAll) continue; // correlated rolls need a different distribution
                var probabilities = new List<float>();
                var values = new List<float>();
                foreach (var buff in ordered)
                {
                    if (!NativeDispelPreview.TryChance(dispel, source, target, buff, out float chance)) return 0f;
                    float share = DebuffShareIn(ctx, buff.Context, target, new[] { buff.Blueprint }.ToList(), out _, removingBenefits: true);
                    if (AbilityProfile.IsHarmfulBuff(buff.Blueprint))
                        share = -Math.Max(AbilityProfile.ControlShareOf(buff.Blueprint),
                            DebuffShareIn(ctx, buff.Context, target, new[] { buff.Blueprint }.ToList(), out _));
                    float rounds = Math.Min(Math.Min(ctx.ExpectedRounds, 3f), Math.Max(0f, (float)buff.TimeLeft.TotalSeconds / 6f));
                    probabilities.Add(chance);
                    values.Add((share < 0f && !AbilityProfile.IsHarmfulBuff(buff.Blueprint) ? 0f : share) * CombatMath.ControlWorth(target) * rounds);
                }
                bool limited = HarmonyLib.Traverse.Create(dispel).Field("m_StopAfterCountRemoved").GetValue<bool>();
                var count = HarmonyLib.Traverse.Create(dispel).Field("m_CountToRemove").GetValue<ContextValue>();
                int limit = limited ? Math.Max(0, count.Calculate(source)) : ordered.Count;
                total += ValueModel.LimitedSuccessValue(probabilities, values, limit);
            }
            return total * CombatMath.EffectChance(ctx.Unit, target, blast, effect, damage: false);
        }

        // ★ v0.3.22: a caster-side burn discount is valuable only for a learned follow-up useful now.
        // Query both native costs; neither the discount's declared Value nor a guessed future use is the value.
        private static float KineticFollowUpSavings(TacticalContext ctx, AbilityData blast, AbilityProfile effect, UnitEntityData target)
        {
            if (!KineticPreview.CostHookInstalled) return 0f;
            float best = 0f;
            foreach (var buff in effect.CasterBuffs.Where(b => !ctx.Unit.Buffs.HasFact(b) && b.GetComponent<AddKineticistBurnModifier>() != null))
            {
                if (buff.ComponentsArray.Any(c => c != null && !(c is AddKineticistBurnModifier) &&
                    !(c is Kingmaker.UnitLogic.FactLogic.AddAbilityUseTrigger) &&
                    !(c is ContextRankConfig) && !(c is ContextCalculateAbilityParamsBasedOnClass))) continue;
                // Limit this to heal/buff talents; a follow-up blast would recursively appraise its infusion.
                foreach (var follow in AbilityDiscovery.Gather(ctx.Unit).Where(a => a != blast && !IsOwnBlast(ctx.Unit.Get<UnitPartKineticist>(), a)))
                {
                    var profile = AbilityProfile.Get(follow);
                    if (profile == null || profile.Heals.Count == 0) continue;
                    int before = BurnOf(follow);
                    if (before <= 0) continue;
                    using (KineticPreview.WithAdditionalBuff(ctx.Unit, buff))
                    {
                        int after = BurnOf(follow);
                        if (after >= before || !follow.IsAvailable) continue;
                        float usefulness = EvaluateAbility(ctx, follow).Where(c => !c.NeedsMove).Select(c => c.Value - BurnCost(ctx, after))
                            .DefaultIfEmpty(0f).Max();
                        best = Math.Max(best, ValueModel.FutureUseValue(BurnCost(ctx, before) - BurnCost(ctx, after), usefulness, ctx.ExpectedRounds));
                    }
                }
            }
            return best * CombatMath.EffectChance(ctx.Unit, target, blast, effect, damage: false);
        }

        private static IEnumerable<UnitEntityData> RicochetTargets(AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            foreach (var ricochet in profile.Ricochets)
            {
                var context = CombatMath.CreateContext(ability, new Kingmaker.Utility.TargetWrapper(target));
                if (context == null) continue;
                var before = HarmonyLib.Traverse.Create(ricochet).Field("m_BeforeCondition").GetValue<Kingmaker.ElementsSystem.ConditionsChecker>();
                var filter = HarmonyLib.Traverse.Create(ricochet).Field("m_TargetCondition").GetValue<Kingmaker.ElementsSystem.ConditionsChecker>();
                if (AbilityProfile.CheckSafe(before, context, ability.Caster.Unit) != true) continue;
                // Unknown target conditions or repeat-victim delivery are not predicted as guaranteed extra damage.
                if (AbilityProfile.CheckSafe(filter, context, target) == null ||
                    HarmonyLib.Traverse.Create(ricochet).Field("m_UsedTargetsAgain").GetValue<bool>()) continue;
                var choose = HarmonyLib.AccessTools.Method(typeof(Kingmaker.UnitLogic.Abilities.Components.AbilityDeliverRicochet),
                                                          "SelectNextTargetByMinDistance");
                if (choose == null) continue;
                int count = Math.Max(0, ricochet.TargetsCount.Calculate(context));
                var used = new HashSet<UnitEntityData> { target };
                UnitEntityData previous = target;
                for (int i = 0; i < count; i++)
                {
                    var next = choose.Invoke(ricochet, new object[] { context, new Kingmaker.Utility.TargetWrapper(previous), used }) as UnitEntityData;
                    if (next == null || !used.Add(next)) break;
                    yield return next;
                    previous = next;
                }
            }
        }

        private static float BladeEffectValue(TacticalContext ctx, UnitEntityData target, WeaponAttackEstimate estimate)
        {
            float value = 0f;
            foreach (var blade in estimate.BladeDeliveries)
                value += KineticEffectValue(ctx, blade.Blast, AbilityProfile.Get(blade.Blast), ActionSlot.Standard, target,
                                            out _, out _, blade.Hits);
            return value;
        }

        private static float KineticPeriodicDamage(TacticalContext ctx, AbilityData blast, AbilityProfile effect,
                                                   UnitEntityData target)
        {
            if (!effect.IsKineticEffect || effect.PeriodicEffects.Count == 0) return 0f;
            var source = KineticEffectContext.For(blast);
            if (source == null) return 0f;
            float value = 0f;
            float delivered = CombatMath.EffectChance(ctx.Unit, target, blast, effect, damage: false);
            foreach (var periodic in effect.PeriodicEffects)
            {
                if (target.Buffs.HasFact(periodic.Buff) || CombatMath.BuffImmune(target, periodic.Buff, blast) ||
                    effect.BuffAppliesTo(periodic.Buff, blast, target) == EffectApplies.No) continue;
                float duration = EffectiveRounds(blast, effect, periodic.Buff, source);
                float rounds = ValueModel.UsefulRounds(duration, ctx.ExpectedRounds, ctx.ExpectedRounds);
                // Application/save is priced once; each later round uses the round action's own save and damage.
                using (new KineticEffectContext(blast, source, periodic.Buff.AssetGuid.ToString(), effect, 1f))
                {
                    float tick = CombatMath.EstimateAbilityDamage(blast, periodic.Tick, target) *
                                 CombatMath.EffectChance(ctx.Unit, target, blast, periodic.Tick, damage: true);
                    float remains = periodic.SaveEnds ? CombatMath.SaveFailChance(blast, periodic.Tick, target) : 1f;
                    value += delivered * tick * ValueModel.ExpectedTicks(rounds, remains);
                }
            }
            return value;
        }

        /// <summary>The trigger's structural delivery filters, matching ApplyInternal in the game.
        /// Unsupported weapon delivery is left unvalued rather than assuming a match.</summary>
        private static bool TriggerMatches(AddKineticistInfusionDamageTrigger trigger, AbilityData blast,
                                           MechanicsContext source)
        {
            if (!trigger.TriggerOnDirectDamage && !(source.AssociatedBlueprint is BlueprintAbilityAreaEffect)) return false;
            if (trigger.CheckSpellDescriptor && !source.SpellDescriptor.HasFlag(trigger.SpellDescriptorsList.Value)) return false;
            if (trigger.CheckWeaponType)
            {
                if (!CombatMath.DeliveryWeaponForEffect(blast, out var weapon) || weapon?.Blueprint?.Type != trigger.WeaponType)
                    return false;
            }
            if (!trigger.CheckSpellParent) return true;
            if (source is AbilityExecutionContext execution)
                return trigger.AbilityList.Contains(execution.Ability.Blueprint) ||
                       trigger.AbilityList.Contains(execution.Ability.ConvertedFrom?.Blueprint);
            // For a fact context ApplyInternal first takes the event's Reason.Ability, then its parent blueprint.
            return trigger.AbilityList.Contains(blast.Blueprint) || trigger.AbilityList.Contains(blast.Blueprint.Parent);
        }
    }
}
