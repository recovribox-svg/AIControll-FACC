// ★ v0.3.0: What an ability actually does, read from its blueprint (cached per blueprint).
//
// The previous classifier walked only a hard-coded set of action containers and returned "no buff" for anything it
// did not recognise, which made unrecognised buffs look "never applied" (=> re-cast every turn) and let utility
// abilities such as Appraise Foe score as attacks. This walker recurses through ANY field of type ActionList /
// GameAction[] / GameAction and into referenced abilities (sticky-touch deliveries, ContextActionCastSpell, ...), and
// keeps "no detectable effect" as an explicit result instead of a silent default.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public enum ControlStrength { None = 0, Soft = 1, Hard = 2 }

    public sealed class AbilityProfile
    {
        public BlueprintAbility Blueprint { get; }

        public readonly List<ContextActionDealDamage> Damage = new List<ContextActionDealDamage>();
        public readonly List<ContextActionHealTarget> Heals = new List<ContextActionHealTarget>();
        /// <summary>Buffs applied to the ability's target(s).</summary>
        public readonly List<BlueprintBuff> TargetBuffs = new List<BlueprintBuff>();
        /// <summary>Buffs applied to the caster (ToCaster / ContextActionOnContextCaster).</summary>
        public readonly List<BlueprintBuff> CasterBuffs = new List<BlueprintBuff>();

        public bool Summons { get; private set; }
        public bool Dispels { get; private set; }
        public bool RemovesBuffs { get; private set; }
        public bool Enchants { get; private set; }

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
            Damage.Count > 0 || Heals.Count > 0 || TargetBuffs.Count > 0 || CasterBuffs.Count > 0 ||
            Summons || Dispels || RemovesBuffs || Enchants;

        public bool DealsDamage => Damage.Count > 0;

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

        public static void ClearCache() => Cache.Clear();

        #endregion

        #region Analysis

        private void Analyze()
        {
            var visited = new HashSet<BlueprintAbility>();
            AnalyzeAbility(Blueprint, visited, 0, toCaster: false);

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

        private void AnalyzeAbility(BlueprintAbility bp, HashSet<BlueprintAbility> visited, int depth, bool toCaster)
        {
            if (bp == null || depth > 4 || !visited.Add(bp)) return;

            var run = bp.GetComponent<AbilityEffectRunAction>();
            if (run != null)
            {
                if (run.SavingThrowType != SavingThrowType.Unknown && Save == SavingThrowType.Unknown)
                    Save = run.SavingThrowType;
                Walk(run.Actions?.Actions, visited, depth, toCaster, 0);
            }

            var projectile = bp.GetComponent<AbilityDeliverProjectile>();
            if (projectile != null && projectile.NeedAttackRoll) NeedsAttackRoll = true;
            if (bp.GetComponent<AbilityDeliverTouch>() != null) NeedsAttackRoll = true;

            var sticky = bp.StickyTouch;
            if (sticky?.TouchDeliveryAbility != null)
            {
                NeedsAttackRoll = true;
                AnalyzeAbility(sticky.TouchDeliveryAbility, visited, depth + 1, toCaster);
            }
        }

        private void Walk(GameAction[] actions, HashSet<BlueprintAbility> visited, int abilityDepth, bool toCaster, int depth)
        {
            if (actions == null || depth > 12) return;

            foreach (var action in actions)
            {
                if (action == null) continue;

                switch (action)
                {
                    case ContextActionDealDamage dmg:
                        Damage.Add(dmg);
                        if (dmg.HalfIfSaved) HalfOnSave = true;
                        break;
                    case ContextActionHealTarget heal:
                        Heals.Add(heal);
                        break;
                    case ContextActionApplyBuff apply when apply.Buff != null:
                        if (toCaster || apply.ToCaster) CasterBuffs.Add(apply.Buff);
                        else TargetBuffs.Add(apply.Buff);
                        if (apply.Permanent || (apply.DurationValue != null && (int)apply.DurationValue.Rate > 0))
                            HasLongBuff = true;
                        break;
                    case ContextActionSpawnMonster _:
                        Summons = true;
                        break;
                    case ContextActionDispelMagic _:
                        Dispels = true;
                        break;
                    case ContextActionRemoveBuff _:
                    case ContextActionRemoveBuffsByDescriptor _:
                        RemovesBuffs = true;
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

                bool childToCaster = toCaster || action is ContextActionOnContextCaster;
                foreach (var child in ChildActionLists(action))
                    Walk(child, visited, abilityDepth, childToCaster, depth + 1);

                foreach (var referenced in ReferencedAbilities(action))
                    AnalyzeAbility(referenced, visited, abilityDepth + 1, childToCaster);
            }
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
