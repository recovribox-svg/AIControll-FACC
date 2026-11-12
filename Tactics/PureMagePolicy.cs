// ★ pure-mage: admits AI Control's own actions; never patches manual orders, native procs or another mod.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Settings;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.Enums;
using Kingmaker.Utility;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class PureMagePolicy
    {
        public static bool IsActive(CharacterSettings settings) =>
            settings?.PureMage == true && settings.RangePreference == RangePreference.Ranged;

        public static CharacterSettings SettingsFor(UnitEntityData unit)
        {
            // ★ summon control: a summon's temporary view (Pure Mage is never on for it), not a row under its id.
            if (GameInterface.SummonControl.IsOrdinarySummon(unit)) return GameInterface.SummonControl.SettingsFor(unit);
            if (unit?.UniqueId != null && ModSettings.Instance?.CharacterSettings != null &&
                ModSettings.Instance.CharacterSettings.TryGetValue(unit.UniqueId, out var settings)) return settings;
            return null;
        }

        public static bool IsActive(UnitEntityData unit) => IsActive(SettingsFor(unit));

        /// <summary>Weapon maneuvers are physical even when represented by an AbilityData. Native kinetic archer blasts
        /// and Eldritch Archer spell commands remain spells; a Spellstrike toggle alone never authorizes a bare bow.</summary>
        public static bool AllowsAbility(CharacterSettings settings, AbilityData ability, AbilityProfile profile)
        {
            if (!IsActive(settings)) return true;
            if (profile == null || ability == null) return false;
            bool physical = profile.IsCharge || profile.IsWeaponAttack || profile.MeleeAttack != null || TacticalEngine.IsCoupDeGrace(ability) ||
                ability.Blueprint.Type == AbilityType.Extraordinary &&
                (profile.Maneuvers.Count > 0 || profile.Damage.Any(a => a is ContextActionDealWeaponDamage));
            if (!physical) return true;
            return ability.Caster?.Unit != null && Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist.IsArcherBlast(ability);
        }

        public static bool AllowsWeapon(UnitEntityData unit, CharacterSettings settings) =>
            !IsActive(settings) || HasEnergyShot(unit);

        // Only transparent native energy riders. Unknown/conditional/random shapes do not authorize a physical fallback.
        // Metadata is cached; live activation, context and weapon restrictions are checked once per decision and at issue.
        private static readonly Dictionary<BlueprintBuff, AdditionalDiceOnAttack[]> Riders =
            new Dictionary<BlueprintBuff, AdditionalDiceOnAttack[]>();
        private static readonly FieldInfo RandomDamage = typeof(AdditionalDiceOnAttack).GetField("m_RandomizeDamage", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo WeaponDice = typeof(AdditionalDiceOnAttack).GetField("m_UseWeaponDice", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static void ClearCache() => Riders.Clear();

        internal static bool IsEnergyRider(AdditionalDiceOnAttack part)
        {
            if (part == null || RandomDamage == null || WeaponDice == null ||
                (bool)RandomDamage.GetValue(part) || (bool)WeaponDice.GetValue(part)) return false;
            return part.AttackType != AdditionalDiceOnAttack.WeaponOptions.OnlyNonWeaponAttacks &&
                !part.OnlyOnFullAttack && !part.OnlyOnFirstHit && !part.CriticalHit && !part.NotCriticalHit && !part.OnAttackOfOpportunity &&
                !part.OnlySneakAttack && !part.NotSneakAttack && !part.ReduceHPToZero && !part.CheckDistance &&
                !part.AllNaturalAndUnarmed && !part.DuelistWeapon && !part.OnCharge &&
                SafeRiderValue(part.Value?.DiceCountValue) && SafeRiderValue(part.Value?.BonusValue) &&
                // Native Check returns true for an empty checker; a missing checker would throw in this component.
                part.InitiatorConditions?.HasConditions == false && part.TargetConditions?.HasConditions == false &&
                !part.MainDamageTypeUse && part.DamageType?.Type == Kingmaker.RuleSystem.Rules.Damage.DamageType.Energy;
        }

        private static bool SafeRiderValue(ContextValue value) => value == null ||
            value.ValueType == ContextValueType.Simple || value.ValueType == ContextValueType.Rank;

        public static bool HasEnergyShot(UnitEntityData unit)
        {
            var weapon = unit?.Body?.PrimaryHand?.MaybeWeapon;
            if (weapon?.Blueprint?.IsRanged != true || unit.Buffs == null) return false;
            if (unit.Get<Kingmaker.UnitLogic.Parts.UnitPartDisableBonusForDamage>()?.DisableAdditionalDice == true) return false;
            foreach (var buff in unit.Buffs.Enumerable)
            {
                if (buff == null || !buff.Active || !buff.IsEnabled || buff.IsSuppressed || buff.Context == null) continue;
                if (!Riders.TryGetValue(buff.Blueprint, out var parts))
                {
                    parts = buff.Blueprint.GetComponents<AdditionalDiceOnAttack>().Where(IsEnergyRider).ToArray();
                    Riders[buff.Blueprint] = parts;
                }
                foreach (var part in parts)
                {
                    if (!MatchesWeapon(part, weapon)) continue;
                    // Calculate the active buff's own dice, not its name, inventory ownership or nominal caster level.
                    try
                    {
                        if (part.Value == null) continue;
                        int count = part.Value.DiceCountValue.Calculate(buff.Context);
                        int sides = part.Value.DiceType.Sides();
                        int bonus = part.Value.BonusValue.Calculate(buff.Context);
                        if (count >= 0 && sides >= 0 && (sides > 0 ? count * (sides + 1f) / 2f : 0f) + bonus > 0f) return true;
                    }
                    catch (Exception ex)
                    {
                        AbilityDiscovery.ReportOnce("pure-mage-rider|" + buff.Blueprint.AssetGuid,
                            () => $"[PureMage] {buff.Blueprint.name}: energy rider unreadable ({ex.GetType().Name}); weapon fallback withheld");
                    }
                }
            }
            return false;
        }

        private static bool MatchesWeapon(AdditionalDiceOnAttack part, ItemEntityWeapon weapon) =>
            (part.WeaponType == null || part.WeaponType == weapon.Blueprint.Type) &&
            (!part.CheckWeaponCategory || part.Category == weapon.Blueprint.Category) &&
            (!part.CheckWeaponGroup || weapon.Blueprint.FighterGroup.Contains(part.Group)) &&
            (!part.CheckWeaponRangeType || part.RangeType.IsSuitableWeapon(weapon));
    }
}
