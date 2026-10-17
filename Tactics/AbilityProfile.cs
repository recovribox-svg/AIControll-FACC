// ★ v0.3.0: What an ability actually does, read from its blueprint (cached per blueprint).
//
// The previous classifier walked only a hard-coded set of action containers and returned "no buff" for anything it
// did not recognise, which made unrecognised buffs look "never applied" (=> re-cast every turn) and let utility
// abilities such as Appraise Foe score as attacks. This walker recurses through ANY field of type ActionList /
// GameAction[] / GameAction and into referenced abilities (sticky-touch deliveries, ContextActionCastSpell, ...), and
// keeps "no detectable effect" as an explicit result instead of a silent default.
//
// ★ v0.3.1: effects behind a Conditional (Cure/Inflict Wounds: "undead → heal / else damage", Lay On Hands) carry the
// branch they sit in, and AppliesTo() evaluates that branch against a concrete target. v0.3.0 counted both branches
// for everyone, so Inflict Light Wounds was valued as a heal on living Camellia and as damage on an undead Skeletal
// Champion (where it heals). Also records what an ability removes (cleanses) and buffs that only last for the next
// attack (RemoveBuffOnAttack / ≤ 1 round, e.g. Perfect Strike).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.Utility;
using UnityEngine;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public enum ControlStrength { None = 0, Soft = 1, Hard = 2 }

    /// <summary>Result of evaluating an effect's Conditional branch for one target.</summary>
    public enum EffectApplies { Unconditional, Yes, No, Unknown }

    /// <summary>Whom a nested action runs on: the ability's target, its caster, or someone else (party, pet, random units).</summary>
    public enum EffectSubject { Target, Caster, Other }

    /// <summary>One enclosing Conditional: the effect runs when the checker evaluates to <see cref="Branch"/>.</summary>
    public sealed class EffectGuard
    {
        public ConditionsChecker Checker;
        public bool Branch;
        /// <summary>The game evaluates this checker against the caster (Conditional inside ContextActionOnContextCaster).</summary>
        public bool OnCaster;
    }

    public sealed class AbilityProfile
    {
        public BlueprintAbility Blueprint { get; }

        public readonly List<ContextActionDealDamage> Damage = new List<ContextActionDealDamage>();
        /// <summary>Heals of the ability's target(s).</summary>
        public readonly List<ContextActionHealTarget> Heals = new List<ContextActionHealTarget>();
        /// <summary>★ v0.3.1: heals run on the caster (ContextActionOnContextCaster, life drain) or on other units
        /// (party members, pet, random units around) - not on the ability's target.</summary>
        public readonly List<ContextActionHealTarget> CasterHeals = new List<ContextActionHealTarget>();
        /// <summary>Buffs applied to the ability's target(s).</summary>
        public readonly List<BlueprintBuff> TargetBuffs = new List<BlueprintBuff>();
        /// <summary>Buffs applied to the caster (ToCaster / ContextActionOnContextCaster).</summary>
        public readonly List<BlueprintBuff> CasterBuffs = new List<BlueprintBuff>();

        public bool Summons { get; private set; }
        public bool Dispels { get; private set; }
        public bool RemovesBuffs { get; private set; }
        public bool Enchants { get; private set; }

        /// <summary>Descriptors of buffs the ability removes from its target (ContextActionRemoveBuffsByDescriptor).</summary>
        public SpellDescriptor RemovedDescriptors { get; private set; }
        /// <summary>Specific buffs the ability removes from its target (ContextActionRemoveBuff without ToCaster).</summary>
        public readonly List<BlueprintBuff> RemovedBuffs = new List<BlueprintBuff>();

        /// <summary>Removes buffs/conditions from its target and neither damages nor heals: its worth is what it removes.</summary>
        public bool RemovesFromTarget => RemovesAnything && Damage.Count == 0 && Heals.Count == 0;

        /// <summary>Removes buffs/conditions from its target (possibly alongside a heal, e.g. Lay On Hands mercies).</summary>
        public bool RemovesAnything => RemovedDescriptors != SpellDescriptor.None || RemovedBuffs.Count > 0;

        /// <summary>
        /// Removes whole classes of conditions by descriptor (fear, poison, disease...). The buffs such an ability applies
        /// (protection, cooldown markers) only matter together with the cleanse. Removing one specific buff, by contrast,
        /// is often a side effect of a real buff (e.g. a haste effect removing slow) and does not make it a cleanse.
        /// </summary>
        public bool IsCleanse => RemovedDescriptors != SpellDescriptor.None && Damage.Count == 0 && Heals.Count == 0;

        /// <summary>Every buff it applies lasts at most one round: it only helps what happens this turn (and this round).</summary>
        public bool BuffsLastOneRound { get; private set; }
        /// <summary>Every buff it applies is spent by its owner's next attack roll (RemoveBuffOnAttack, e.g. Perfect Strike).</summary>
        public bool BuffsSpentByAttack { get; private set; }
        /// <summary>One round or one attack's worth of effect.</summary>
        public bool IsOneShot => BuffsLastOneRound || BuffsSpentByAttack;

        public SavingThrowType Save { get; private set; } = SavingThrowType.Unknown;
        public bool HalfOnSave { get; private set; }
        public bool NeedsAttackRoll { get; private set; }
        public bool IsCharge { get; private set; }

        public float AoERadius { get; private set; }
        public Kingmaker.UnitLogic.Abilities.Components.TargetType AoETargets { get; private set; } =
            Kingmaker.UnitLogic.Abilities.Components.TargetType.Any;
        public bool IsAoE => AoERadius > 0.01f;

        public CCType Control { get; private set; }
        public ControlStrength ControlStrength { get; private set; }

        public bool HarmfulToEnemies => Blueprint.EffectOnEnemy == AbilityEffectOnUnit.Harmful;
        public bool HelpfulToAllies => Blueprint.EffectOnAlly == AbilityEffectOnUnit.Helpful;

        /// <summary>Longest buff duration category we saw (rounds = short, minutes or longer = long).</summary>
        public bool HasLongBuff { get; private set; }

        public bool HasDetectableEffect =>
            Damage.Count > 0 || Heals.Count > 0 || CasterHeals.Count > 0 || TargetBuffs.Count > 0 || CasterBuffs.Count > 0 ||
            Summons || Dispels || RemovesBuffs || Enchants;

        public bool DealsDamage => Damage.Count > 0;

        private readonly Dictionary<object, EffectGuard[]> m_Guards = new Dictionary<object, EffectGuard[]>();
        private readonly Dictionary<BlueprintBuff, EffectGuard[]> m_BuffGuards = new Dictionary<BlueprintBuff, EffectGuard[]>();
        private static readonly EffectGuard[] NoGuards = new EffectGuard[0];
        private int m_ShortBuffs, m_SpentByAttackBuffs, m_AllBuffs;
        /// <summary>Removal actions (ContextActionRemoveBuff / ...ByDescriptor) on the target, with their branch.</summary>
        private readonly List<GameAction> m_Removals = new List<GameAction>();

        private AbilityProfile(BlueprintAbility blueprint)
        {
            Blueprint = blueprint;
        }

        #region Cache

        private static readonly Dictionary<BlueprintAbility, AbilityProfile> Cache = new Dictionary<BlueprintAbility, AbilityProfile>();

        public static AbilityProfile Get(AbilityData ability)
        {
            return ability?.Blueprint == null ? null : Get(ability.Blueprint);
        }

        public static AbilityProfile Get(BlueprintAbility blueprint)
        {
            if (blueprint == null) return null;
            if (Cache.TryGetValue(blueprint, out var cached)) return cached;

            var profile = new AbilityProfile(blueprint);
            try
            {
                profile.Analyze();
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Profile] {blueprint.name}: analysis error {ex.Message}");
            }
            Cache[blueprint] = profile;
            return profile;
        }

        public static void ClearCache()
        {
            Cache.Clear();
            ClearConditionCache();
        }

        /// <summary>Per-target condition results are keyed by unit id; drop them when a combat ends.</summary>
        public static void ClearConditionCache() => CheckerCache.Clear();

        #endregion

        #region Analysis

        private void Analyze()
        {
            var visited = new HashSet<BlueprintAbility>();
            AnalyzeAbility(Blueprint, visited, 0, EffectSubject.Target);
            BuffsLastOneRound = m_AllBuffs > 0 && m_ShortBuffs == m_AllBuffs;
            BuffsSpentByAttack = m_AllBuffs > 0 && m_SpentByAttackBuffs == m_AllBuffs;

            // A personal ability's caster IS its target: its caster heals are heals of the target.
            if (Blueprint.Range == AbilityRange.Personal && CasterHeals.Count > 0)
            {
                Heals.AddRange(CasterHeals);
                CasterHeals.Clear();
            }

            IsCharge = Blueprint.GetComponent<AbilityCustomCharge>() != null;

            var around = Blueprint.GetComponent<AbilityTargetsAround>();
            if (around != null)
            {
                AoERadius = around.AoERadius.Meters;
                AoETargets = around.Targets;
            }

            // Control: the spell's own descriptor plus the descriptors / conditions of what it applies.
            SpellDescriptor descriptor = Blueprint.SpellDescriptor;
            foreach (var buff in TargetBuffs)
            {
                var comp = buff.GetComponent<SpellDescriptorComponent>();
                if (comp != null) descriptor |= comp.Descriptor.Value;
            }
            Control = AbilityClassifier.AnalyzeCCTypeFromDescriptor(descriptor);
            ControlStrength = ClassifyControl(Control);
            foreach (var buff in TargetBuffs)
            {
                foreach (var cond in buff.GetComponents<AddCondition>())
                {
                    var s = ClassifyCondition(cond.Condition);
                    if (s > ControlStrength) ControlStrength = s;
                }
            }
        }

        private void AnalyzeAbility(BlueprintAbility bp, HashSet<BlueprintAbility> visited, int depth, EffectSubject subject)
        {
            if (bp == null || depth > 4 || !visited.Add(bp)) return;

            var run = bp.GetComponent<AbilityEffectRunAction>();
            if (run != null)
            {
                if (run.SavingThrowType != SavingThrowType.Unknown && Save == SavingThrowType.Unknown)
                    Save = run.SavingThrowType;
                Walk(run.Actions?.Actions, visited, depth, subject, 0, NoGuards);
            }

            var projectile = bp.GetComponent<AbilityDeliverProjectile>();
            if (projectile != null && projectile.NeedAttackRoll) NeedsAttackRoll = true;
            if (bp.GetComponent<AbilityDeliverTouch>() != null) NeedsAttackRoll = true;

            var sticky = bp.StickyTouch;
            if (sticky?.TouchDeliveryAbility != null)
            {
                NeedsAttackRoll = true;
                AnalyzeAbility(sticky.TouchDeliveryAbility, visited, depth + 1, subject);
            }
        }

        private void Walk(GameAction[] actions, HashSet<BlueprintAbility> visited, int abilityDepth, EffectSubject subject, int depth,
                          EffectGuard[] guards)
        {
            if (actions == null || depth > 12) return;
            bool toCaster = subject == EffectSubject.Caster;

            foreach (var action in actions)
            {
                if (action == null) continue;

                // Conditional: each branch's effects only happen when the condition has that outcome for the target.
                if (action is Conditional conditional)
                {
                    Walk(conditional.IfTrue?.Actions, visited, abilityDepth, subject, depth + 1, With(guards, conditional.ConditionsChecker, true, toCaster));
                    Walk(conditional.IfFalse?.Actions, visited, abilityDepth, subject, depth + 1, With(guards, conditional.ConditionsChecker, false, toCaster));
                    continue;
                }

                switch (action)
                {
                    case ContextActionDealDamage dmg:
                        Damage.Add(dmg);
                        m_Guards[dmg] = guards;
                        if (dmg.HalfIfSaved) HalfOnSave = true;
                        break;
                    case ContextActionHealTarget heal:
                        // A heal run on the caster (life drain) or on other units is not a heal of the target.
                        if (subject != EffectSubject.Target) CasterHeals.Add(heal);
                        else Heals.Add(heal);
                        m_Guards[heal] = guards;
                        break;
                    case ContextActionApplyBuff apply when apply.Buff != null:
                        if (toCaster || apply.ToCaster) CasterBuffs.Add(apply.Buff);
                        else TargetBuffs.Add(apply.Buff);
                        // The same buff in two branches: treat as unconditional.
                        m_BuffGuards[apply.Buff] = m_BuffGuards.ContainsKey(apply.Buff) ? NoGuards : guards;
                        if (apply.Permanent || (apply.DurationValue != null && (int)apply.DurationValue.Rate > 0))
                            HasLongBuff = true;
                        m_AllBuffs++;
                        if (LastsOneRound(apply)) m_ShortBuffs++;
                        if (apply.Buff.GetComponent<RemoveBuffOnAttack>() != null) m_SpentByAttackBuffs++;
                        break;
                    case ContextActionSpawnMonster _:
                        Summons = true;
                        break;
                    case ContextActionDispelMagic _:
                        Dispels = true;
                        break;
                    case ContextActionRemoveBuff remove:
                        RemovesBuffs = true;
                        if (!remove.ToCaster && subject == EffectSubject.Target && remove.Buff != null)
                        {
                            RemovedBuffs.Add(remove.Buff);
                            m_Removals.Add(remove);
                            m_Guards[remove] = guards;
                        }
                        break;
                    case ContextActionRemoveBuffsByDescriptor removeByDescriptor:
                        RemovesBuffs = true;
                        if (subject == EffectSubject.Target)
                        {
                            RemovedDescriptors |= removeByDescriptor.SpellDescriptor.Value;
                            m_Removals.Add(removeByDescriptor);
                            m_Guards[removeByDescriptor] = guards;
                        }
                        break;
                    case ContextActionSavingThrow save:
                        if (save.Type != SavingThrowType.Unknown && Save == SavingThrowType.Unknown) Save = save.Type;
                        break;
                    case ContextActionConditionalSaved _:
                        HalfOnSave = HalfOnSave || DealsDamage;
                        break;
                }

                string typeName = action.GetType().Name;
                if (typeName.Contains("Enchant") || typeName.Contains("EnhanceWeapon"))
                    Enchants = true;

                var childSubject = ChildSubject(subject, action);
                foreach (var child in ChildActionLists(action))
                    Walk(child, visited, abilityDepth, childSubject, depth + 1, guards);

                foreach (var referenced in ReferencedAbilities(action))
                    AnalyzeAbility(referenced, visited, abilityDepth + 1, childSubject);
            }
        }

        /// <summary>Actions that run their children on someone other than the current subject.</summary>
        private static EffectSubject ChildSubject(EffectSubject subject, GameAction action)
        {
            switch (action)
            {
                case ContextActionOnContextCaster _:
                    return EffectSubject.Caster;
                case ContextActionOnOwner _:
                case ContextActionPartyMembers _:
                case ContextActionsOnPet _:
                case ContextActionOnRandomTargetsAround _:
                case ContextActionOnSwarmTargets _:
                case ContextActionOnRandomAreaTarget _:
                    return EffectSubject.Other;
                default:
                    return subject;
            }
        }

        private static EffectGuard[] With(EffectGuard[] guards, ConditionsChecker checker, bool branch, bool onCaster)
        {
            if (checker == null) return guards;
            var result = new EffectGuard[guards.Length + 1];
            guards.CopyTo(result, 0);
            result[guards.Length] = new EffectGuard { Checker = checker, Branch = branch, OnCaster = onCaster };
            return result;
        }

        /// <summary>Lasts at most one round (SameDuration copies a parent duration we cannot see: not short).</summary>
        private static bool LastsOneRound(ContextActionApplyBuff apply)
        {
            try
            {
                if (apply.Permanent || apply.SameDuration) return false;
                if (apply.UseDurationSeconds) return apply.DurationSeconds <= 6.01f;
                var d = apply.DurationValue;
                if (d == null || (int)d.Rate > 0) return false;
                bool noDice = d.DiceType == Kingmaker.RuleSystem.DiceType.Zero || (d.DiceCountValue?.IsValueSimple == true && d.DiceCountValue.Value == 0);
                return noDice && d.BonusValue != null && d.BonusValue.IsValueSimple && d.BonusValue.Value <= 1;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Per-target evaluation

        /// <summary>
        /// Conditions that only read facts of the target/caster (no dice, no area or encounter state), so evaluating
        /// them outside the real cast is safe. Anything else makes the branch "Unknown" (counted, as in v0.3.0).
        /// </summary>
        private static readonly HashSet<Type> SafeConditions = new HashSet<Type>
        {
            typeof(ContextConditionHasFact), typeof(ContextConditionHasBuff), typeof(ContextConditionHasBuffFromCaster),
            typeof(ContextConditionHasBuffWithDescriptor), typeof(ContextConditionAlignment), typeof(ContextConditionCasterHasFact),
            typeof(ContextConditionIsAlly), typeof(ContextConditionIsEnemy), typeof(ContextConditionIsCaster),
            typeof(ContextConditionTargetIsYourself), typeof(ContextConditionAlive), typeof(ContextConditionIsUnconscious),
            typeof(ContextConditionIsHelpless), typeof(ContextConditionIsPartyMember), typeof(ContextConditionCharacterClass),
            typeof(ContextConditionSize), typeof(ContextConditionIsAnimalCompanion)
        };

        private sealed class CheckerResult
        {
            public int Frame;
            public bool? Result;
        }

        /// <summary>Checker outcome per (checker, target) within one frame; a decision evaluates many candidates.</summary>
        private static readonly Dictionary<ConditionsChecker, Dictionary<string, CheckerResult>> CheckerCache =
            new Dictionary<ConditionsChecker, Dictionary<string, CheckerResult>>();

        private static readonly HashSet<string> ReportedUnknown = new HashSet<string>();

        public EffectApplies AppliesTo(GameAction effect, AbilityData ability, UnitEntityData target)
        {
            return Evaluate(effect != null && m_Guards.TryGetValue(effect, out var g) ? g : NoGuards, ability, target);
        }

        public EffectApplies BuffAppliesTo(BlueprintBuff buff, AbilityData ability, UnitEntityData target)
        {
            return Evaluate(buff != null && m_BuffGuards.TryGetValue(buff, out var g) ? g : NoGuards, ability, target);
        }

        public bool HasGuardedEffects => m_Guards.Values.Any(g => g.Length > 0) || m_BuffGuards.Values.Any(g => g.Length > 0);

        private static EffectApplies Evaluate(EffectGuard[] guards, AbilityData ability, UnitEntityData target)
        {
            if (guards.Length == 0) return EffectApplies.Unconditional;
            if (ability == null || target == null) return EffectApplies.Unknown;
            var caster = ability.Caster?.Unit;

            bool unknown = false;
            foreach (var guard in guards)
            {
                var subject = guard.OnCaster ? caster : target;
                if (subject == null) { unknown = true; continue; }
                bool? result = CheckFor(guard.Checker, ability, subject, caster);
                if (!result.HasValue) { unknown = true; continue; }
                if (result.Value != guard.Branch) return EffectApplies.No;
            }
            return unknown ? EffectApplies.Unknown : EffectApplies.Yes;
        }

        private static bool? CheckFor(ConditionsChecker checker, AbilityData ability, UnitEntityData target, UnitEntityData caster)
        {
            int frame = Time.frameCount;
            // Caster-dependent conditions (IsAlly, CasterHasFact, HasBuffFromCaster...): key by caster and target.
            string key = $"{caster?.UniqueId}|{target.UniqueId}";
            if (!CheckerCache.TryGetValue(checker, out var perUnit))
                CheckerCache[checker] = perUnit = new Dictionary<string, CheckerResult>();
            if (perUnit.TryGetValue(key, out var cached) && cached.Frame == frame) return cached.Result;

            bool? result = null;
            try
            {
                if (checker.Conditions == null || checker.Conditions.Length == 0)
                {
                    result = true;
                }
                else if (!checker.Conditions.All(c => c == null || SafeConditions.Contains(c.GetType())))
                {
                    string types = string.Join(",", checker.Conditions.Where(c => c != null && !SafeConditions.Contains(c.GetType()))
                                                                     .Select(c => c.GetType().Name).Distinct());
                    if (ReportedUnknown.Add($"{ability.Blueprint?.AssetGuid}|{types}"))
                        Main.Log($"[Profile] {ability.Name}: branch condition {types} not evaluated - both branches counted");
                }
                else
                {
                    var context = CombatMath.CreateContext(ability, new TargetWrapper(target));
                    if (context != null)
                    {
                        using (context.GetDataScope(new TargetWrapper(target)))
                        {
                            bool and = checker.Operation == Operation.And;
                            bool acc = and;
                            foreach (var condition in checker.Conditions)
                            {
                                if (condition == null) continue;
                                bool r = condition.Check();
                                if (and && !r) { acc = false; break; }
                                if (!and && r) { acc = true; break; }
                            }
                            result = acc;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Profile] condition check for {ability.Name} on {target.CharacterName}: {ex.Message}");
                result = null;
            }

            perUnit[key] = new CheckerResult { Frame = frame, Result = result };
            return result;
        }

        /// <summary>
        /// ★ v0.3.2: the target currently has a HARMFUL buff (debuff, poison, fear...) this ability would remove. Removing a
        /// good buff is not a cleanse: Orvath's "Shift Form: Mortal Guise" removes the Overlord form and was valued as a
        /// cleanse, so he switched forms back and forth every turn.
        /// </summary>
        public bool WouldRemoveSomething(AbilityData ability, UnitEntityData target)
        {
            return BuffsRemovedFrom(ability, target).Any(b => IsHarmfulFor(b, target));
        }

        /// <summary>Good buffs on the target this ability would remove (a mode switch such as Overlord ↔ Mortal Guise).</summary>
        public List<Kingmaker.UnitLogic.Buffs.Buff> BeneficialBuffsRemoved(AbilityData ability, UnitEntityData target)
        {
            return BuffsRemovedFrom(ability, target).Where(b => !IsHarmfulFor(b, target)).ToList();
        }

        private IEnumerable<Kingmaker.UnitLogic.Buffs.Buff> BuffsRemovedFrom(AbilityData ability, UnitEntityData target)
        {
            var result = new List<Kingmaker.UnitLogic.Buffs.Buff>();
            if (target?.Buffs == null || m_Removals.Count == 0) return result;
            try
            {
                var buffs = target.Buffs.Enumerable.ToList();
                foreach (var removal in m_Removals)
                {
                    if (AppliesTo(removal, ability, target) == EffectApplies.No) continue;
                    foreach (var buff in buffs)
                    {
                        bool removed = removal is ContextActionRemoveBuff rb && rb.Buff == buff.Blueprint ||
                                       removal is ContextActionRemoveBuffsByDescriptor rd && buff.MaybeContext != null &&
                                       (buff.MaybeContext.SpellDescriptor & rd.SpellDescriptor.Value) != SpellDescriptor.None;
                        if (removed && !result.Contains(buff)) result.Add(buff);
                    }
                }
            }
            catch { }
            return result;
        }

        /// <summary>Flagged harmful by its blueprint, or put on the unit by one of its enemies.</summary>
        private static bool IsHarmfulFor(Kingmaker.UnitLogic.Buffs.Buff buff, UnitEntityData unit)
        {
            try
            {
                if (buff.Blueprint != null && buff.Blueprint.Harmful) return true;
                var caster = buff.MaybeContext?.MaybeCaster;
                return caster != null && caster.IsEnemy(unit);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ★ v0.3.2: single-target effects (damage, heal, target buffs, removals), as opposed to caster-only side effects.
        /// When none of them applies to a target, aiming the ability at it does nothing useful there (e.g. Lay On Hands -
        /// Others on a living enemy, whose only remaining effect is a buff on the paladin).
        /// </summary>
        public bool HasTargetEffects => Damage.Count > 0 || Heals.Count > 0 || TargetBuffs.Count > 0 || m_Removals.Count > 0;

        public bool AnyTargetEffectApplies(AbilityData ability, UnitEntityData target)
        {
            return Damage.Any(d => AppliesTo(d, ability, target) != EffectApplies.No) ||
                   Heals.Any(h => AppliesTo(h, ability, target) != EffectApplies.No) ||
                   TargetBuffs.Any(b => BuffAppliesTo(b, ability, target) != EffectApplies.No) ||
                   m_Removals.Any(r => AppliesTo(r, ability, target) != EffectApplies.No);
        }

        private static readonly HashSet<BlueprintAbility> Described = new HashSet<BlueprintAbility>();

        /// <summary>★ v0.3.2: once per ability, what its buffs are made of (evidence for tuning from play logs).</summary>
        public void DescribeOnce()
        {
            if (!Described.Add(Blueprint)) return;
            try
            {
                var parts = TargetBuffs.Concat(CasterBuffs).Distinct()
                    .Select(b => BuffEffectAnalyzer.Analyze(b).ToString()).ToList();
                if (RemovesAnything)
                    parts.Add($"removes {RemovedDescriptors}{(RemovedBuffs.Count > 0 ? " + " + string.Join(",", RemovedBuffs.Select(b => b.name)) : "")}");
                if (IsOneShot) parts.Add(BuffsLastOneRound ? "lasts 1 round" : "spent by next attack");
                if (parts.Count > 0)
                    Main.Log($"[Profile] {Blueprint.Name}: {string.Join(" ; ", parts)}");
            }
            catch { }
        }

        #endregion

        #region Reflection helpers

        private static readonly Dictionary<Type, FieldInfo[]> ActionFields = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, FieldInfo[]> AbilityRefFields = new Dictionary<Type, FieldInfo[]>();
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static IEnumerable<GameAction[]> ChildActionLists(GameAction action)
        {
            var type = action.GetType();
            if (!ActionFields.TryGetValue(type, out var fields))
            {
                fields = AllFields(type).Where(f =>
                    f.FieldType == typeof(ActionList) ||
                    f.FieldType == typeof(GameAction[]) ||
                    typeof(GameAction).IsAssignableFrom(f.FieldType)).ToArray();
                ActionFields[type] = fields;
            }

            foreach (var f in fields)
            {
                object value;
                try { value = f.GetValue(action); } catch { continue; }
                switch (value)
                {
                    case ActionList list when list.Actions != null:
                        yield return list.Actions;
                        break;
                    case GameAction[] array:
                        yield return array;
                        break;
                    case GameAction single:
                        yield return new[] { single };
                        break;
                }
            }
        }

        private static IEnumerable<BlueprintAbility> ReferencedAbilities(GameAction action)
        {
            var type = action.GetType();
            if (!AbilityRefFields.TryGetValue(type, out var fields))
            {
                fields = AllFields(type).Where(f => f.FieldType == typeof(BlueprintAbilityReference)).ToArray();
                AbilityRefFields[type] = fields;
            }

            foreach (var f in fields)
            {
                BlueprintAbility bp = null;
                try { bp = (f.GetValue(action) as BlueprintAbilityReference)?.Get(); } catch { }
                if (bp != null) yield return bp;
            }
        }

        private static IEnumerable<FieldInfo> AllFields(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(Fields | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        #endregion

        #region Control classification

        private static ControlStrength ClassifyControl(CCType cc)
        {
            if ((cc & CCType.HardCC) != 0) return ControlStrength.Hard;
            if ((cc & (CCType.Confusion | CCType.Daze | CCType.Charm | CCType.Nauseate)) != 0) return ControlStrength.Hard;
            if ((cc & (CCType.Fear | CCType.Blind | CCType.Slow | CCType.Entangle | CCType.MovementImpair)) != 0) return ControlStrength.Soft;
            return ControlStrength.None;
        }

        private static ControlStrength ClassifyCondition(UnitCondition condition)
        {
            switch (condition)
            {
                case UnitCondition.Sleeping:
                case UnitCondition.Paralyzed:
                case UnitCondition.Stunned:
                case UnitCondition.Helpless:
                case UnitCondition.CantAct:
                case UnitCondition.Confusion:
                case UnitCondition.Dazed:
                case UnitCondition.Petrified:
                case UnitCondition.Nauseated:
                case UnitCondition.Cowering:
                case UnitCondition.Frightened:
                    return ControlStrength.Hard;
                case UnitCondition.Shaken:
                case UnitCondition.Sickened:
                case UnitCondition.Entangled:
                case UnitCondition.Slowed:
                case UnitCondition.Staggered:
                case UnitCondition.Blindness:
                case UnitCondition.Fatigued:
                case UnitCondition.Exhausted:
                case UnitCondition.Prone:
                case UnitCondition.CantMove:
                    return ControlStrength.Soft;
                default:
                    return ControlStrength.None;
            }
        }

        #endregion
    }
}
