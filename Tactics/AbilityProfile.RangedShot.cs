// ★ v0.3.30: a single ranged weapon attack with a before-cast, next-attack damage buff (Focused Shot).
// Deliberately structural and read-only: uninspected extra actions, selectors and trigger filters stay unsupported.
using System;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts.Restrictions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.CasterCheckers;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Properties;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed partial class AbilityProfile
    {
        internal sealed class RangedShotProfile
        {
            internal BlueprintBuff Buff;
            internal AddContextStatBonus Bonus;
        }

        internal RangedShotProfile RangedShot { get; private set; }

        private static readonly FieldInfo ShotRestrictions = HarmonyLib.AccessTools.Field(typeof(AddContextStatBonus), "Restrictions");
        private static readonly FieldInfo[] ShotAttackFlags = typeof(ContextActionRangedAttack).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(bool)).ToArray();
        private static readonly FieldInfo[] ShotRemovalFlags = typeof(AddInitiatorAttackWithWeaponTrigger).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(bool)).ToArray();

        private static bool ShotFlagsClear(object value, FieldInfo[] flags) => flags.All(f => !(bool)f.GetValue(value));

        private static RangedShotProfile ReadRangedShot(BlueprintAbility blueprint)
        {
            if (blueprint == null || blueprint.Type != AbilityType.Physical || !blueprint.NeedEquipWeapons ||
                !blueprint.CanTargetEnemies || blueprint.CanTargetFriends || blueprint.CanTargetSelf || blueprint.CanTargetPoint ||
                blueprint.SpellResistance || blueprint.ComponentsArray?.Length != 3) return null;
            var cast = blueprint.GetComponent<AbilityExecuteActionOnCast>();
            var effect = blueprint.GetComponent<AbilityEffectRunAction>();
            var weapon = blueprint.GetComponent<AbilityCasterHasWeaponWithRangeType>();
            if (cast?.GetType() != typeof(AbilityExecuteActionOnCast) || effect?.GetType() != typeof(AbilityEffectRunAction) ||
                weapon?.GetType() != typeof(AbilityCasterHasWeaponWithRangeType) || weapon.RangeType != WeaponRangeType.Ranged ||
                cast.Conditions?.Conditions == null || cast.Conditions.Conditions.Length != 0 ||
                cast.Conditions.Operation != Operation.And || effect.SavingThrowType != Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown ||
                cast.Actions?.Actions?.Length != 1 || effect.Actions?.Actions?.Length != 1) return null;
            var attack = effect.Actions.Actions[0] as ContextActionRangedAttack;
            var apply = cast.Actions.Actions[0] as ContextActionApplyBuff;
            if (attack?.GetType() != typeof(ContextActionRangedAttack) || !ShotFlagsClear(attack, ShotAttackFlags) ||
                apply?.GetType() != typeof(ContextActionApplyBuff) || !apply.ToCaster || !apply.AsChild ||
                apply.Permanent || !apply.UseDurationSeconds || apply.DurationSeconds != 1f || apply.IsFromSpell ||
                apply.SameDuration || apply.IgnoreParentContext) return null;
            var buff = apply.Buff;
            if (buff == null || buff.Stacking != StackingType.Replace || buff.ComponentsArray?.Length != 2) return null;
            var bonus = buff.GetComponent<AddContextStatBonus>();
            var remove = buff.GetComponent<AddInitiatorAttackWithWeaponTrigger>();
            if (bonus?.GetType() != typeof(AddContextStatBonus) || bonus.Stat != StatType.AdditionalDamage ||
                bonus.Descriptor != ModifierDescriptor.None || bonus.Value == null ||
                (bonus.Value.ValueType != ContextValueType.Simple &&
                 (bonus.Value.ValueType != ContextValueType.CasterProperty || bonus.Value.Property != UnitProperty.StatBonusIntelligence)) ||
                ShotRestrictions == null || !(ShotRestrictions.GetValue(bonus) is RestrictionCalculator restrictions) ||
                restrictions.Property != null && !restrictions.Property.Empty ||
                remove?.GetType() != typeof(AddInitiatorAttackWithWeaponTrigger) || !ShotFlagsClear(remove, ShotRemovalFlags) ||
                remove.WeaponType != null || remove.Action?.Actions?.Length != 1 ||
                remove.Action.Actions[0]?.GetType() != typeof(ContextActionRemoveSelf)) return null;
            return new RangedShotProfile { Buff = buff, Bonus = bonus };
        }
    }
}
