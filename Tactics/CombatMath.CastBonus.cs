// ★ v0.3.30 competence3 (competence2 logs, [DmgCheck] in four fights: "Bandit Alchemist's Alchemist Bomb: 5 hits dealt 10-12
// a hit, estimated 3.5"): damage bonuses the game adds to an ability's damage rolls that its own dice do not show.
//  - A cast-time buff: a fact's AddAbilityUseTrigger (before the cast: RuleCastSpell) puts a buff on the caster for the
//    ability, and the buff's DamageBonusConditional adds to the first damage of every RuleCalculateDamage the caster makes
//    (the Alchemist's Throw Anything: +Intelligence bonus on bombs, AlchemistThrowAnythingBuff, removed after the cast). It is
//    never on the caster when the engine estimates, so the combat log's "1d6 + 9" was estimated as 1d6.
//  - Point-Blank Shot (DistanceDamageBonus, RuleDealDamage): +1 within 30 feet for a ranged weapon or an ability whose
//    projectile needs an attack roll (bombs, rays), bodies included.
// Read only where it can be read exactly: a bonus without conditions, from a constant or a stat of the caster. Kinetic
// blasts keep their learned share (DamageObserver), which already holds what such bonuses add to them.
using System;
using System.Collections.Generic;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        private static readonly Dictionary<(UnitEntityData, Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility), float> s_CastBonus =
            new Dictionary<(UnitEntityData, Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility), float>();
        private static readonly Dictionary<UnitEntityData, List<KeyValuePair<float, int>>> s_PointBlank =
            new Dictionary<UnitEntityData, List<KeyValuePair<float, int>>>();
        private static int s_CastBonusFrame = -1;

        /// <summary>What the game adds to each hit-point damage roll of <paramref name="ability"/> on <paramref name="target"/>.</summary>
        internal static float CastDamageBonus(AbilityData ability, UnitEntityData target)
        {
            var caster = ability?.Caster?.Unit;
            if (caster == null || ability.Blueprint == null) return 0f;
            try
            {
                if (Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist>(ability.Blueprint) != null)
                    return 0f;
                if (Time.frameCount != s_CastBonusFrame)
                {
                    s_CastBonus.Clear();
                    s_PointBlank.Clear();
                    s_UseTriggers.Clear();
                    s_CastBonusFrame = Time.frameCount;
                }
                var key = (caster, ability.Blueprint);
                if (!s_CastBonus.TryGetValue(key, out float bonus)) s_CastBonus[key] = bonus = CastTriggerBonus(caster, ability);
                if (target != null) bonus += PointBlankBonus(caster, ability, target);
                return bonus;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] cast-time damage bonus of {ability.Name}: {ex.Message}");
                return 0f;
            }
        }

        private static readonly Dictionary<UnitEntityData, List<KeyValuePair<AddAbilityUseTrigger, MechanicsContext>>> s_UseTriggers =
            new Dictionary<UnitEntityData, List<KeyValuePair<AddAbilityUseTrigger, MechanicsContext>>>();

        /// <summary>The DamageBonusConditional of buffs the caster's facts put on it as <paramref name="ability"/> is cast.</summary>
        private static float CastTriggerBonus(UnitEntityData caster, AbilityData ability)
        {
            // The caster's before-cast triggers that put a damage-bonus buff on it, gathered once a frame.
            if (!s_UseTriggers.TryGetValue(caster, out var triggers))
            {
                triggers = new List<KeyValuePair<AddAbilityUseTrigger, MechanicsContext>>();
                foreach (var fact in caster.Facts.List)
                {
                    if (fact?.Blueprint?.ComponentsArray == null || !fact.Active || fact.MaybeContext == null) continue;
                    foreach (var component in fact.Blueprint.ComponentsArray)
                        if (component is AddAbilityUseTrigger use && !use.AfterCast && use.Action?.Actions != null && GivesDamageBonus(use))
                            triggers.Add(new KeyValuePair<AddAbilityUseTrigger, MechanicsContext>(use, fact.MaybeContext));
                }
                s_UseTriggers[caster] = triggers;
            }
            float bonus = 0f;
            foreach (var pair in triggers)
            {
                if (!TriggersFor(pair.Key, ability)) continue;
                foreach (var action in pair.Key.Action.Actions)
                {
                    if (!(action is ContextActionApplyBuff apply) || !apply.ToCaster || apply.Buff?.ComponentsArray == null) continue;
                    foreach (var part in apply.Buff.ComponentsArray)
                        if (ReadableDamageBonus(part, out var value)) bonus += value.Calculate(pair.Value);
                }
            }
            return bonus;
        }

        private static bool GivesDamageBonus(AddAbilityUseTrigger use)
        {
            foreach (var action in use.Action.Actions)
            {
                if (!(action is ContextActionApplyBuff apply) || !apply.ToCaster || apply.Buff?.ComponentsArray == null) continue;
                foreach (var part in apply.Buff.ComponentsArray)
                    if (ReadableDamageBonus(part, out _)) return true;
            }
            return false;
        }

        /// <summary>A DamageBonusConditional on any damage, without conditions, from a constant or a stat of the caster.</summary>
        private static bool ReadableDamageBonus(Kingmaker.Blueprints.BlueprintComponent part, out ContextValue value)
        {
            value = null;
            if (!(part is DamageBonusConditional damage) || damage.OnlyWeaponDamage || damage.Bonus == null) return false;
            if (damage.Conditions?.Conditions != null && damage.Conditions.Conditions.Length > 0) return false;
            var type = damage.Bonus.ValueType;
            if (type != ContextValueType.Simple && type != ContextValueType.CasterProperty && type != ContextValueType.CasterCustomProperty) return false;
            value = damage.Bonus;
            return true;
        }

        /// <summary>The game's filter for an AddAbilityUseTrigger (AddAbilityUseTrigger.RunAction), for <paramref name="spell"/>.</summary>
        private static bool TriggersFor(AddAbilityUseTrigger use, AbilityData spell)
        {
            var bp = spell.Blueprint;
            if (use.DisableForStickyTouchCasts &&
                Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectStickyTouch>(bp) != null)
                return false;
            if (use.FromSpellbook && (spell.Spellbook == null || !use.Spellbooks.HasReference(spell.Spellbook.Blueprint))) return false;
            if (use.ForOneSpell && bp != use.Ability) return false;
            if (use.ForMultipleSpells && (use.Abilities == null || !use.Abilities.Exists(r => r != null && r.Is(bp)))) return false;
            if (use.CheckAbilityType && bp.Type != use.Type) return false;
            if (use.MinSpellLevel && spell.SpellLevel < use.MinSpellLevelLimit) return false;
            if (use.ExactSpellLevel && spell.SpellLevel != use.ExactSpellLevelLimit) return false;
            if (use.CheckDescriptor && (spell.SpellDescriptor & use.SpellDescriptor.Value) == Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None) return false;
            if (use.CheckAoE && bp.IsAoEDamage != use.IsAoE) return false;
            if (use.CheckSpellSchool && spell.SpellSchool != use.IsSpellSchool) return false;
            if (use.CheckRange && bp.Range != use.Range) return false;
            if (use.CheckSourceItemType &&
                !Kingmaker.UnitLogic.Abilities.SpellSourceTypeHelper.CheckSourceItemType(spell, use.SourceItemType, use.SourceItemTypeExclude))
                return false;
            return true;
        }

        /// <summary>Point-Blank Shot's damage (DistanceDamageBonus.OnEventAboutToTrigger) on a projectile with an attack roll.</summary>
        private static float PointBlankBonus(UnitEntityData caster, AbilityData ability, UnitEntityData target)
        {
            if (!s_PointBlank.TryGetValue(caster, out var bonuses))
            {
                bonuses = new List<KeyValuePair<float, int>>();
                foreach (var fact in caster.Facts.List)
                {
                    if (fact?.Blueprint?.ComponentsArray == null || !fact.Active) continue;
                    foreach (var component in fact.Blueprint.ComponentsArray)
                        if (component is DistanceDamageBonus distance && distance.DamageBonus != 0)
                            bonuses.Add(new KeyValuePair<float, int>(distance.Range.Meters, distance.DamageBonus));
                }
                s_PointBlank[caster] = bonuses;
            }
            if (bonuses.Count == 0) return 0f;
            if (caster.Parts.Get<Kingmaker.UnitLogic.Parts.UnitPartDisableBonusForDamage>()?.DisableAdditionalDamage ?? false) return 0f;
            var projectile = ability.GetDeliverProjectileFromDeliverBlueprint(Kingmaker.Armies.TacticalCombat.TacticalCombatHelper.IsActive);
            if (!(projectile?.NeedAttackRoll ?? false)) return 0f;
            float gap = target.DistanceTo(caster) - target.Corpulence - caster.Corpulence;
            float bonus = 0f;
            foreach (var b in bonuses)
                if (gap <= b.Key) bonus += b.Value;
            return bonus;
        }
    }
}
