// ★ infusion-selection: read actual native trigger graphs; never execute them or fabricate actor facts.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Utility;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        internal sealed class InfusionHit
        {
            internal UnitEntityData Target;
            internal float Chance, CurrentValue, ImmediateDamage;
        }

        internal sealed class InfusionAim
        {
            internal List<InfusionHit> Hits;
            internal float DeliveryFactor;
        }

        // Only a plain reduction result is memoized during one effect appraisal. Identity deliberately avoids
        // native equality; a different action, source context, recipient or calculated amount must be read again.
        private readonly struct InfusionDamageRead : IEquatable<InfusionDamageRead>
        {
            private readonly ContextActionDealDamage m_Action;
            private readonly MechanicsContext m_Source;
            private readonly UnitEntityData m_Target;
            private readonly float m_Amount;

            internal InfusionDamageRead(ContextActionDealDamage action, MechanicsContext source,
                UnitEntityData target, float amount)
            {
                m_Action = action;
                m_Source = source;
                m_Target = target;
                m_Amount = amount;
            }

            public bool Equals(InfusionDamageRead other) => ReferenceEquals(m_Action, other.m_Action) &&
                ReferenceEquals(m_Source, other.m_Source) && ReferenceEquals(m_Target, other.m_Target) && m_Amount.Equals(other.m_Amount);
            public override bool Equals(object other) => other is InfusionDamageRead read && Equals(read);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = IdentityHash(m_Action);
                    hash = hash * 397 ^ IdentityHash(m_Source);
                    hash = hash * 397 ^ IdentityHash(m_Target);
                    return hash * 397 ^ m_Amount.GetHashCode();
                }
            }

            private static int IdentityHash(object value) => ReferenceEquals(value, null) ? 0 : RuntimeHelpers.GetHashCode(value);
        }

        private static float ActiveInfusionValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
            UnitEntityData target, float triggerChance, float immediateDamage, out string note)
        {
            note = null;
            if (!IsOwnBlast(ctx.Unit.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>(), ability) ||
                ability.ShadowSpellSettings != null || profile.IsWeaponAttack || profile.Heals.Count > 0) return 0f;
            float sum = 0f;
            var seen = new HashSet<BlueprintBuff>();
            foreach (var toggle in ctx.Unit.ActivatableAbilities?.Enumerable ?? Enumerable.Empty<ActivatableAbility>())
            {
                var bp = toggle?.Blueprint;
                if (bp?.Group != ActivatableAbilityGroup.SubstanceInfusion || bp.Buff == null) continue;
                var installed = toggle.AppliedBuff;
                if (installed == null || !installed.Active || !installed.IsTurnedOn || installed.Context?.MaybeCaster != ctx.Unit) continue;
                if (!seen.Add(bp.Buff)) continue;
                float part = InfusionEffectValue(ctx, ability, bp.Buff, target, triggerChance, installed.Context, out string text,
                    immediateDamage, ctx.ScoredInfusionHits == null || ctx.ScoredInfusionHits.Count == 0);
                sum += part;
                if (text != null) note = note == null ? text : note + "; " + text;
            }
            ctx.ScoredInfusionHits?.Add(new InfusionHit { Target = target, Chance = triggerChance, CurrentValue = sum, ImmediateDamage = immediateDamage });
            return sum;
        }

        private sealed class InfusionDescription
        {
            internal BlueprintBuff Buff;
            internal AddKineticistInfusionDamageTrigger Trigger;
            internal IgnoreSpellResistanceForSpells Pierce;
            internal IgnoreDamageReductionOnTarget PiercePhysical;
            internal bool DamageGraph;
            internal bool CasterOnly;
            internal string Unsupported;

            internal bool Applies(AbilityData blast) => blast?.Blueprint != null &&
                // The installed component passes its buff context, so native ApplyInternal takes the reason-ability
                // branch (actual Blueprint or Parent), rather than the AbilityExecutionContext/ConvertedFrom branch.
                (Trigger != null && (!Trigger.CheckSpellParent || Trigger.AbilityList.Contains(blast.Blueprint) ||
                    Trigger.AbilityList.Contains(blast.Blueprint.Parent)) ||
                 Pierce != null && (Pierce.AllSpells || Pierce.AbilityList.Contains(blast.Blueprint) || Pierce.AbilityList.Contains(blast.Blueprint.Parent)) ||
                 PiercePhysical != null && Buff.GetComponents<Kingmaker.UnitLogic.Class.Kineticist.AddKineticistBurnModifier>().Any(m =>
                    !m.AppliableTo.Any() || m.AppliableTo.Contains(blast.Blueprint) || m.AppliableTo.Contains(blast.Blueprint.Parent)));
        }

        // Only immutable blueprint descriptions. No context, native result or actor state is retained here.
        private static readonly Dictionary<BlueprintBuff, InfusionDescription> InfusionDescriptions =
            new Dictionary<BlueprintBuff, InfusionDescription>();

        private static InfusionDescription DescribeInfusion(BlueprintBuff buff)
        {
            if (buff == null) return null;
            if (InfusionDescriptions.TryGetValue(buff, out var found)) return found;
            var description = new InfusionDescription { Buff = buff };
            try
            {
                var triggers = buff.GetComponents<AddKineticistInfusionDamageTrigger>().ToArray();
                description.Pierce = buff.GetComponent<IgnoreSpellResistanceForSpells>();
                description.PiercePhysical = buff.GetComponent<IgnoreDamageReductionOnTarget>();
                if (description.Pierce != null && description.Pierce.GetType() != typeof(IgnoreSpellResistanceForSpells) ||
                    description.PiercePhysical != null && description.PiercePhysical.GetType() != typeof(IgnoreDamageReductionOnTarget))
                    description.Unsupported = "custom bypass modifier";
                if (triggers.Length == 1)
                {
                    var t = triggers[0];
                    if (t.GetType() != typeof(AddKineticistInfusionDamageTrigger) || !t.TriggerOnDirectDamage ||
                        t.TriggerOnStatDamageOrEnergyDrain || t.CheckWeaponType && t.WeaponType != null || t.CheckSpellDescriptor)
                        description.Unsupported = "custom damage trigger/filter";
                    else if (AbilityProfile.ContainsAction(t.Actions?.Actions, a => a is ContextActionDispelMagic))
                        description.Unsupported = "dispel has no read-only native success query";
                    else
                    {
                        description.Trigger = t;
                        description.CasterOnly = t.Actions?.Actions?.Length == 1 &&
                            t.Actions.Actions[0] is ContextActionOnContextCaster caster && !caster.TargetAsCaster;
                        description.DamageGraph = HasInfusionDamageGraph(t.Actions, out bool mixed, out bool unknown);
                        if (mixed) description.Unsupported = "mixed periodic damage/control graph";
                        else if (unknown) description.Unsupported = "unsupported native action in infusion graph";
                    }
                }
                else if (description.Pierce == null && description.PiercePhysical == null)
                    description.Unsupported = "delivery-changing infusion has no supported read-only recipient query";
            }
            catch (Exception ex) { description.Unsupported = ex.GetType().Name; }
            InfusionDescriptions[buff] = description;
            return description;
        }

        private static readonly HashSet<Type> InfusionActionTypes = new HashSet<Type>
        {
            typeof(Conditional), typeof(ContextActionSavingThrow), typeof(ContextActionConditionalSaved),
            typeof(ContextActionApplyBuff), typeof(ContextActionCombatManeuver), typeof(ContextActionDealDamage),
            typeof(ContextActionRemoveSelf), typeof(ContextActionSpawnFx), typeof(ContextActionOnContextCaster),
            typeof(ContextActionRepeatedActions), typeof(ContextActionRemoveBuff)
        };

        private static bool HasInfusionDamageGraph(ActionList root, out bool mixed, out bool unknown)
        {
            var pending = new Queue<ActionList>();
            var seen = new HashSet<BlueprintBuff>();
            pending.Enqueue(root);
            bool damage = false, control = false, unsupported = false;
            while (pending.Count > 0)
            {
                if (seen.Count > 64) throw new InvalidOperationException("infusion blueprint graph limit");
                var list = pending.Dequeue();
                AbilityProfile.ContainsAction(list?.Actions, action =>
                {
                    if (!InfusionActionTypes.Contains(action.GetType()) &&
                        action.GetType().FullName != "Kingmaker.UnitLogic.Mechanics.Actions.ContextActionBreakFree") unsupported = true;
                    if (action is ContextActionDealDamage) damage = true;
                    if (action is ContextActionCombatManeuver) control = true;
                    if (action is ContextActionApplyBuff apply && apply.Buff != null && seen.Add(apply.Buff))
                    {
                        control |= AbilityProfile.BuffControl(apply.Buff) != ControlStrength.None || BuffPartsReader.Read(apply.Buff).Parts.Count > 0;
                        var component = apply.Buff.GetComponent<AddFactContextActions>();
                        if (component != null) { pending.Enqueue(component.Activated); pending.Enqueue(component.NewRound); }
                    }
                    return false;
                });
            }
            mixed = damage && control;
            unknown = unsupported;
            return damage;
        }

        private static MechanicsContext InfusionContext(UnitEntityData caster, BlueprintBuff blueprint,
            UnitEntityData recipient, MechanicsContext parent = null)
        {
            var context = new MechanicsContext(caster, recipient.Descriptor, blueprint, parent, new TargetWrapper(recipient));
            context.RecalculateAbilityParams();
            context.RecalculateRanks();
            // RecalculateSharedValues can roll dice. Shared values are not initialized by appraisal.
            return context;
        }

        private static float InfusionDamageChange(TacticalContext ctx, AbilityData blast, InfusionHit hit,
            BlueprintBuff added, BlueprintBuff[] removed, out float resultingChance, out float resultingDamage)
        {
            resultingChance = hit.Chance;
            resultingDamage = hit.ImmediateDamage;
            var profile = AbilityProfile.Get(blast);
            var add = DescribeInfusion(added);
            var removedPierce = removed.Where(b => DescribeInfusion(b)?.Pierce != null).ToArray();
            var removedPhysical = removed.Where(b => DescribeInfusion(b)?.PiercePhysical != null).ToArray();
            bool material = added != null && profile.HasInfusionDamageBranch(added) || removed.Any(profile.HasInfusionDamageBranch);
            if (add?.Pierce == null && removedPierce.Length == 0 && add?.PiercePhysical == null && removedPhysical.Length == 0 && !material) return 0f;
            // Native IgnoreSpellResistanceForSpells bypasses immunity too; preserve that game's order rather than
            // assuming this is only a bonus to a penetration roll. Other actually installed bypass sources remain.
            if (add?.Pierce != null)
                resultingChance = CombatMath.EffectChance(ctx.Unit, hit.Target, blast, profile, true, 1f);
            else if (removedPierce.Length > 0)
            {
                var sr = HitChanceCalculator.CalculateSpellResistance(ctx.Unit, hit.Target, blast, removedPierce);
                resultingChance = CombatMath.EffectChance(ctx.Unit, hit.Target, blast, profile, true, sr.PenetrationChance);
            }
            float before = CombatMath.EstimateAbilityDamage(blast, profile, hit.Target);
            float after = before;
            var branchBuff = added != null && profile.HasInfusionDamageBranch(added) ? added : removed.FirstOrDefault(profile.HasInfusionDamageBranch);
            bool? physicalBypass = null;
            if (add?.PiercePhysical is IgnoreDamageReductionOnTarget piercing &&
                (!piercing.CheckTargetAlignment || Kingmaker.Enums.AlignmentExtension.HasComponent(
                    hit.Target.Descriptor.Alignment.ValueRaw, piercing.Alignment))) physicalBypass = true;
            else if (removedPhysical.Length > 0)
                physicalBypass = CombatMath.IgnoresPhysicalReduction(ctx.Unit, hit.Target, removedPhysical);
            if (branchBuff != null || physicalBypass.HasValue)
            {
                bool on = added == branchBuff;
                after = CombatMath.EstimateAbilityDamageRaw(blast, profile, hit.Target, null,
                    branchBuff == null ? (Func<GameAction, EffectApplies>)null :
                        action => profile.AppliesWithInfusion(action, blast, hit.Target, branchBuff, on), physicalBypass) *
                    DamageObserver.BlastFactor(ctx.Unit, blast.Blueprint);
            }
            resultingDamage = after;
            bool endsRegeneration = CombatMath.AbilityEndsRegeneration(profile, hit.Target);
            return (DamageValue(hit.Target, after * resultingChance, resultingChance, endsRegeneration) * WakeFactor(hit.Target, after * resultingChance) -
                DamageValue(hit.Target, before * hit.Chance, hit.Chance, endsRegeneration) * WakeFactor(hit.Target, before * hit.Chance)) * RoleWeight(ctx, "damage");
        }

        private static float InfusionEffectValue(TacticalContext ctx, AbilityData blast, BlueprintBuff buff,
            UnitEntityData target, float deliveryChance, MechanicsContext installedContext, out string note,
            float immediateDamage = 0f, bool creditCasterEffect = true)
        {
            note = null;
            var description = DescribeInfusion(buff);
            if (description == null || description.Unsupported != null || !description.Applies(blast) ||
                target == null || !(deliveryChance > 0f) || description.CasterOnly && !creditCasterEffect) return 0f;
            try
            {
                var source = installedContext ?? InfusionContext(ctx.Unit, buff, ctx.Unit);
                int work = 0;
                Dictionary<InfusionDamageRead, float> damageReads = null; // Allocated only at a pure damage graph's first damage node.
                float conditional = InfusionActions(ctx, blast, description.Trigger?.Actions, source, target,
                    ValueModel.Horizon(ctx.ExpectedRounds, 3f), 1f, 0, ref work, out _, description.DamageGraph, ref damageReads);
                // Periodic damage is aggregated before HP capping. Never award a separate kill bonus on every tick,
                // or count damage to a target the immediate blast is expected to finish. Control uses the existing
                // shared kill-chance appraisal to discount a recipient likely to die on impact.
                float value = description.DamageGraph
                    ? ValueModel.InfusionPeriodicDamage(deliveryChance, conditional, immediateDamage, target.HPLeft)
                    : deliveryChance * conditional * (1f - ValueModel.KillChance(1f, immediateDamage, target.HPLeft));
                if (CombatMath.IsDisabled(target)) value *= 0.4f;
                if (value > 0f) note = $"{buff.name} +{value:F1} on {target.CharacterName}";
                return value;
            }
            catch (Exception ex)
            {
                ReportOnce($"infusion-read|{buff.AssetGuid}|{ex.GetType().Name}",
                    $"[Kinetic] {buff.name}: effect estimate withheld ({ex.GetType().Name})");
                return 0f;
            }
        }

        // This interpreter accepts only native effect nodes needed by the stock trigger graphs. Conditions, damage
        // reduction, durations, ranks and debuff/control math reuse existing readers. Unknown branches add no value.
        private static float InfusionActions(TacticalContext ctx, AbilityData blast, ActionList list,
            MechanicsContext source, UnitEntityData target, float horizon, float failedSave, int depth,
            ref int work, out float survives, bool memoizeDamage, ref Dictionary<InfusionDamageRead, float> damageReads)
        {
            survives = 1f;
            if (list?.Actions == null) return 0f;
            if (depth > 10) throw new InvalidOperationException("infusion graph depth limit");
            float value = 0f;
            foreach (var action in list.Actions)
            {
                if (++work > 128) throw new InvalidOperationException("infusion graph work limit");
                if (action == null) continue;
                float part = 0f, remains = 1f;
                if (action.GetType() == typeof(Conditional))
                {
                    var branch = (Conditional)action;
                    if (AbilityProfile.TryReadConditions(branch.ConditionsChecker, source, target, out bool yes))
                        part = InfusionActions(ctx, blast, yes ? branch.IfTrue : branch.IfFalse, source, target,
                            horizon, failedSave, depth + 1, ref work, out remains, memoizeDamage, ref damageReads);
                }
                else if (action.GetType() == typeof(ContextActionSavingThrow))
                {
                    var save = (ContextActionSavingThrow)action;
                    if (save.HasCustomDC || save.MakeSaveAlsoForMagicHack) continue;
                    // Native UseDCFromContextSavingThrow falls back to this buff's Params.DC when no save exists.
                    // Read a present save's DC only; never create/trigger a saving-throw rule during appraisal.
                    int? inheritedDC = save.UseDCFromContextSavingThrow ? source.SavingThrow?.DifficultyClass : null;
                    float fail = CombatMath.SaveFailChance(source, save.Type, target, inheritedDC);
                    part = InfusionActions(ctx, blast, save.Actions, source, target, horizon, fail, depth + 1, ref work, out remains,
                        memoizeDamage, ref damageReads);
                }
                else if (action.GetType() == typeof(ContextActionConditionalSaved))
                {
                    var branch = (ContextActionConditionalSaved)action;
                    float a = InfusionActions(ctx, blast, branch.Failed, source, target, horizon, failedSave,
                        depth + 1, ref work, out float aliveA, memoizeDamage, ref damageReads);
                    float b = InfusionActions(ctx, blast, branch.Succeed, source, target, horizon, failedSave,
                        depth + 1, ref work, out float aliveB, memoizeDamage, ref damageReads);
                    part = failedSave * a + (1f - failedSave) * b;
                    remains = failedSave * aliveA + (1f - failedSave) * aliveB;
                }
                else if (action.GetType() == typeof(ContextActionApplyBuff))
                {
                    var apply = (ContextActionApplyBuff)action;
                    if (!apply.ToCaster && apply.Buff != null && !target.Buffs.HasFact(apply.Buff) &&
                        !CombatMath.BuffImmune(target, apply.Buff, blast))
                        part = InfusionAppliedBuff(ctx, blast, apply, source, target, horizon, depth + 1, ref work,
                            memoizeDamage, ref damageReads);
                }
                else if (action.GetType() == typeof(ContextActionOnContextCaster))
                {
                    var casterActions = (ContextActionOnContextCaster)action;
                    if (!casterActions.TargetAsCaster)
                        part = InfusionCasterUtility(ctx, casterActions.Actions);
                }
                else if (action.GetType() == typeof(ContextActionRepeatedActions))
                {
                    var repeated = (ContextActionRepeatedActions)action;
                    var entries = repeated.Actions?.Actions;
                    if (entries?.Length != 1 || !(entries[0] is ContextActionApplyBuff ranked) || ranked.Buff == null ||
                        ranked.Buff.Stacking != StackingType.Rank || ranked.ToCaster || CombatMath.BuffImmune(target, ranked.Buff, blast)) continue;
                    int present = target.Buffs.GetBuff(ranked.Buff)?.Rank ?? 0;
                    int count = Math.Min(ranked.Buff.Ranks - present, (int)CombatMath.EvaluateDice(repeated.Value, source, 0));
                    if (count < 0 || count > 16) throw new InvalidOperationException("ranked infusion work limit");
                    for (int rank = 0; rank < count; rank++)
                        part += InfusionAppliedBuff(ctx, blast, ranked, source, target, horizon, depth + 1, ref work,
                            memoizeDamage, ref damageReads);
                }
                else if (action.GetType() == typeof(ContextActionCombatManeuver))
                {
                    var maneuver = (ContextActionCombatManeuver)action;
                    if (maneuver.Type == CombatManeuver.Trip &&
                        (target.Descriptor.State.Prone.Active || (target.View?.IsGetUp ?? false))) continue;
                    float strength = ManeuverStrength(target, blast, maneuver.Type, out _);
                    if (strength > 0f)
                    {
                        CombatMath.ManeuverOdds(ctx.Unit, target, blast,
                            new AbilityProfile.ManeuverEffect { Type = maneuver.Type, Action = maneuver }, out float chance, out _);
                        part = ValueModel.OnHitControlValue(1f, chance, CombatMath.ControlWorth(target), strength);
                    }
                }
                else if (action.GetType() == typeof(ContextActionDealDamage))
                {
                    var damage = (ContextActionDealDamage)action;
                    if (damage.ReadPreRolledFromSharedValue) continue;
                    float amount = CombatMath.EvaluateDice(damage.Value, source, 0);
                    if (damage.Half) amount *= 0.5f;
                    if (!memoizeDamage) part = CombatMath.AfterTargetReduction(target, damage, amount, blast, infusion: true);
                    else
                    {
                        if (damageReads == null) damageReads = new Dictionary<InfusionDamageRead, float>();
                        var read = new InfusionDamageRead(damage, source, target, amount);
                        if (!damageReads.TryGetValue(read, out part))
                            damageReads[read] = part = CombatMath.AfterTargetReduction(target, damage, amount, blast, infusion: true);
                    }
                }
                else if (action.GetType() == typeof(ContextActionRemoveSelf)) remains = 0f;
                // FX and caster-only utility are not target damage/control. Special modifiers are handled separately.
                value += survives * part;
                survives *= remains;
            }
            return value;
        }

        private static float InfusionAppliedBuff(TacticalContext ctx, AbilityData blast, ContextActionApplyBuff apply,
            MechanicsContext parent, UnitEntityData target, float horizon, int depth, ref int work,
            bool memoizeDamage, ref Dictionary<InfusionDamageRead, float> damageReads)
        {
            var buff = apply.Buff;
            var source = InfusionContext(ctx.Unit, buff, target, parent);
            var actions = buff.GetComponent<AddFactContextActions>();
            float value = InfusionActions(ctx, blast, actions?.Activated, source, target, horizon, 1f,
                depth + 1, ref work, out float alive, memoizeDamage, ref damageReads);
            if (!(alive > 0f)) return value; // A hidden carrier applies/saves/removes itself immediately.
            float rounds = apply.Permanent ? horizon : apply.UseDurationSeconds ? apply.DurationSeconds / 6f
                : DurationRounds(apply.DurationValue, null, parent);
            rounds = rounds > 0f ? Math.Min(horizon, rounds) : 1f;
            bool periodic = actions?.NewRound?.Actions?.Length > 0;
            // A periodic break-free/removal may shorten control. Credit its first interval rather than pretending
            // the minute-long nominal duration guarantees several turns. Periodic damage is evaluated below.
            float controlRounds = periodic ? Math.Min(rounds, 1f) : rounds;
            var conditions = AbilityProfile.ConditionsOf(buff).ToArray();
            bool conditionWorks = conditions.Length == 0 || conditions.Any(c =>
                !target.State.HasConditionImmunity(c) && !target.State.HasCondition(c));
            float strength = conditionWorks && AbilityProfile.BuffControl(buff) != ControlStrength.None
                ? AbilityProfile.ControlShareOf(buff) : 0f;
            var descriptor = buff.GetComponent<SpellDescriptorComponent>()?.Descriptor.Value ?? SpellDescriptor.None;
            var cc = AbilityClassifier.AnalyzeCCTypeFromDescriptor(descriptor);
            if (cc != CCType.None && AbilityClassifier.HasCCImmunity(target, cc)) strength = 0f;
            float byParts = ConditionalInfusionDebuff(ctx, blast, buff) ??
                DebuffShareIn(ctx, source, target, new List<BlueprintBuff> { buff }, out _);
            strength = Math.Max(strength, Math.Max(0f, byParts));
            if (buff.GetComponent<DoNotBenefitFromConcealment>() != null)
            {
                var party = ctx.Allies.Where(a => !CombatMath.IsDisabled(a)).ToList();
                float weight = party.Sum(a => ValueModel.PerRound(Level(a)));
                if (weight > 0f) strength = Math.Max(strength, party.Sum(a => ValueModel.PerRound(Level(a)) *
                    RemovableMiss(ctx, a, true, target)) / weight);
            }
            value += alive * CombatMath.ControlWorth(target) * strength * controlRounds;
            if (periodic)
            {
                float running = alive;
                for (int round = 0; round < (int)Math.Floor(rounds) && running > 0f; round++)
                {
                    float tick = InfusionActions(ctx, blast, actions.NewRound, source, target, 1f, 1f,
                        depth + 1, ref work, out float stays, memoizeDamage, ref damageReads);
                    value += running * tick;
                    running *= stays;
                }
            }
            return value;
        }

        private static float? ConditionalInfusionDebuff(TacticalContext ctx, AbilityData blast, BlueprintBuff buff)
        {
            var weapons = buff.GetComponents<ACBonusAgainstWeaponSubcategory>().ToArray();
            var spells = buff.GetComponents<ACBonusAgainstSpellsWithDescriptor>().ToArray();
            if (weapons.Length == 0 && spells.Length == 0) return null;
            var party = ctx.Allies.Where(a => a != null && !CombatMath.IsDisabled(a)).ToList();
            float weight = party.Sum(a => ValueModel.PerRound(Level(a)));
            if (!(weight > 0f)) return 0f;
            float armor = 0f, saves = 0f;
            foreach (var part in weapons)
            {
                float matching = party.Where(a => a.Body?.PrimaryHand?.MaybeWeapon?.Blueprint is var weapon && weapon != null &&
                    Kingmaker.Enums.WeaponCategoryExtension.HasSubCategory(weapon.Category, part.SubCategory))
                    .Sum(a => ValueModel.PerRound(Level(a))) / weight;
                armor += Math.Max(0, -part.ArmorClassBonus) * matching;
            }
            // Credit only the actually considered caster's matching spell follow-up; no new party ability scan.
            float ownShare = ValueModel.PerRound(Level(ctx.Unit)) / weight;
            foreach (var part in spells)
                if (AbilityProfile.Get(blast).NeedsAttackRoll && blast.SpellDescriptor.HasFlag(part.SpellDescriptor.Value))
                    armor += Math.Max(0, -part.ArmorClassBonus) * ownShare;
            foreach (var part in buff.GetComponents<SavingThrowBonusAgainstDescriptor>())
                if (AbilityProfile.Get(blast).Save != SavingThrowType.Unknown && blast.SpellDescriptor.HasFlag(part.SpellDescriptor.Value))
                    saves += Math.Max(0, -part.Value) * ownShare;
            return ValueModel.DebuffShare(0f, 0f, armor, saves, 0f, 0f);
        }

        private static float InfusionCasterUtility(TacticalContext ctx, ActionList actions)
        {
            float best = 0f;
            foreach (var apply in actions?.Actions?.OfType<ContextActionApplyBuff>() ?? Enumerable.Empty<ContextActionApplyBuff>())
            {
                if (apply.Buff == null || ctx.Unit.Buffs.HasFact(apply.Buff)) continue;
                foreach (var modifier in apply.Buff.GetComponents<Kingmaker.UnitLogic.Class.Kineticist.AddKineticistBurnModifier>())
                {
                    if (modifier.GetType() != typeof(Kingmaker.UnitLogic.Class.Kineticist.AddKineticistBurnModifier) ||
                        modifier.UseContextValue || modifier.Value >= 0 || modifier.BurnType != Kingmaker.UnitLogic.Class.Kineticist.KineticistBurnType.WildTalent)
                        continue;
                    foreach (var heal in ctx.DiscoveredAbilities ?? Enumerable.Empty<AbilityData>())
                    {
                        if (!modifier.AppliableTo.Contains(heal.Blueprint) && !modifier.AppliableTo.Contains(heal.Blueprint.Parent)) continue;
                        var profile = AbilityProfile.Get(heal);
                        if (profile?.Heals.Count == 0 || !ctx.Allies.Concat(ctx.DownedAllies).Any(a => a.HPLeft < a.MaxHP &&
                            profile.Heals.Any(h => profile.AppliesTo(h, heal, a) != EffectApplies.No))) continue;
                        var native = heal.Blueprint.GetComponent<Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist>();
                        if (native == null) continue;
                        var cost = native.CalculateBurnCost(heal);
                        int before = cost.Total;
                        cost.Decrease(-modifier.Value, modifier.BurnType);
                        best = Math.Max(best, BurnCost(ctx, before) - BurnCost(ctx, cost.Total));
                    }
                }
            }
            return best;
        }
    }
}
