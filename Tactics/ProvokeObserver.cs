// ★ v0.3.18 (docs/AI_TUNING_AUDIT.md B10): whether a party member's ranged attack rolls provoke attacks of opportunity.
//
// The game provokes for every ranged and ranged-touch attack roll that does not set DoNotProvokeAttacksOfOpportunity
// (UnitCombatEngagementController.OnEventDidTrigger(RuleAttackRoll)): every enemy engaging the attacker may then make
// an attack of opportunity. The flag is set by the rule's own handlers before the roll - Point Blank Master for its
// weapon category, IgnoreAttacksOfOpportunityForSpellList for its spells, and whatever a mod adds. A fake attack roll
// cannot be used to ask (its fortification branch rolls dice, CombatMath), so the answer is read from the rolls the
// game really makes: for each attacker and weapon category (weapons) or ability (rays), the flag of its last roll. The
// party shoots and casts rays outside melee too, so this is known early. Before any roll, Point Blank Master in the
// attacker's facts answers for weapons; otherwise the roll is taken to provoke, which is the rule.
using System;
using System.Collections.Generic;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class ProvokeObserver : IGlobalRulebookHandler<RuleAttackRoll>
    {
        private static ProvokeObserver _instance;
        /// <summary>"unit|w:category" or "unit|a:ability guid" → the last roll did not provoke.</summary>
        private static readonly Dictionary<string, bool> Exempt = new Dictionary<string, bool>();

        public static void Subscribe()
        {
            if (_instance != null) return;
            _instance = new ProvokeObserver();
            EventBus.Subscribe(_instance);
        }

        public static void Unsubscribe()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
        }

        private static string WeaponKey(UnitEntityData unit, WeaponCategory category) => $"{unit.UniqueId}|w:{category}";
        private static string AbilityKey(UnitEntityData unit, AbilityData ability) => $"{unit.UniqueId}|a:{ability.Blueprint.AssetGuid}";

        /// <summary>A ranged attack with this weapon by <paramref name="unit"/> provokes (the last roll seen, else Point Blank Master).</summary>
        public static bool WeaponProvokes(UnitEntityData unit, ItemEntityWeapon weapon)
        {
            if (unit == null || weapon == null) return true;
            var category = weapon.Blueprint.Category;
            if (Exempt.TryGetValue(WeaponKey(unit, category), out bool exempt)) return !exempt;
            return !HasPointBlankMaster(unit, category);
        }

        /// <summary>A ranged attack roll of this ability (a ray) by <paramref name="unit"/> provokes (the last roll seen, else yes).</summary>
        public static bool AbilityProvokes(UnitEntityData unit, AbilityData ability)
        {
            if (unit == null || ability?.Blueprint == null) return true;
            return !(Exempt.TryGetValue(AbilityKey(unit, ability), out bool exempt) && exempt);
        }

        private static bool HasPointBlankMaster(UnitEntityData unit, WeaponCategory category)
        {
            try
            {
                foreach (var fact in unit.Facts.List)
                {
                    var components = fact?.Blueprint?.ComponentsArray;
                    if (components == null) continue;
                    foreach (var component in components)
                    {
                        if (component is PointBlankMaster master && master.Category == category) return true;
                        if (component is PointBlankMasterParametrized &&
                            (fact as Kingmaker.UnitLogic.Feature)?.Param?.WeaponCategory == category) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Provoke] {unit.CharacterName}: {ex.Message}");
            }
            return false;
        }

        private static void Note(UnitEntityData unit, string key, bool exempt, string what)
        {
            if (Exempt.TryGetValue(key, out bool before) && before == exempt) return;
            Exempt[key] = exempt;
            if (exempt)
                Main.Log($"[Provoke] {unit.CharacterName}: ranged attack rolls with {what} do not provoke attacks of opportunity (seen on a roll)");
        }

        public void OnEventAboutToTrigger(RuleAttackRoll evt)
        {
        }

        public void OnEventDidTrigger(RuleAttackRoll evt)
        {
            try
            {
                if (evt == null || evt.IsFake) return;
                if (evt.AttackType != Kingmaker.RuleSystem.AttackType.Ranged && evt.AttackType != Kingmaker.RuleSystem.AttackType.RangedTouch) return;
                var unit = evt.Initiator;
                if (unit == null || !unit.IsPlayerFaction) return;
                // An attack of opportunity never provokes (RuleAttackWithWeapon sets the flag for it): it says nothing.
                if (evt.RuleAttackWithWeapon?.IsAttackOfOpportunity ?? false) return;
                // A ray is an ability's roll with the ray "weapon"; a shot is a weapon's roll, sometimes made by an ability
                // (Vital Strike): both keys are kept, each answers its own question.
                bool exempt = evt.DoNotProvokeAttacksOfOpportunity;
                var ability = evt.Reason?.Ability;
                if (ability?.Blueprint != null) Note(unit, AbilityKey(unit, ability), exempt, ability.Name);
                var weapon = evt.Weapon?.Blueprint;
                if (weapon != null) Note(unit, WeaponKey(unit, weapon.Category), exempt, weapon.Category.ToString());
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Provoke] observer: {ex.Message}");
            }
        }
    }
}
