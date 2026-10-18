// ★ v0.3.0: Expected-value math from the game's own rules.
//
// Weapon attacks: RuleCalculateAttackBonus + RuleCalculateAC + RuleCalculateWeaponStats, triggered exactly the way the
// game itself does for read-only purposes (PowerAttackWatcher, UnitDescriptionHelper, UIUtilityItem). These are pure
// calculation rules; a fake RuleAttackRoll is deliberately NOT used because its fortification branch rolls Dice.D100.
// The list of attacks in a full attack comes from UnitAttack { ForceFullAttack, OnlyForInspect }.Init, which is how the
// game's unit description UI enumerates them (hands, iterative penalties, natural limbs, haste...).
//
// Spells: dice are evaluated with the ability's own execution context after RecalculateRanks() (ContextRankConfig is
// deterministic); Recalculate() is avoided because shared values can roll dice. Save DC comes from
// AbilityData.CalculateParams().DC; spell resistance from the existing UnitPartSpellResistance-based helper.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using UnityEngine;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class WeaponAttackEstimate
    {
        public float ExpectedDamage;
        public int Attacks;
        public float Reach;
        public bool IsRanged;
        public ItemEntityWeapon Weapon;
        public float FirstHitChance;
    }

    public static class CombatMath
    {
        #region Primitives

        /// <summary>d20 hit probability with natural 1 / natural 20.</summary>
        public static float HitProbability(int attackBonus, int armorClass)
        {
            return Mathf.Clamp((21f + attackBonus - armorClass) / 20f, 0.05f, 0.95f);
        }

        public static float Average(DiceFormula dice)
        {
            int sides = dice.Dice.Sides();
            return sides <= 0 ? 0f : dice.Rolls * (sides + 1) / 2f;
        }

        public static float HpPercent(UnitEntityData unit)
        {
            int max = Math.Max(1, unit.MaxHP);
            return Mathf.Clamp01(unit.HPLeft / (float)max);
        }

        #endregion

        #region Weapon attacks

        /// <summary>
        /// Attack list the game would execute (full = iterative + off-hand + natural limbs, else the single attack).
        /// Empty when the unit cannot make weapon attacks at all.
        /// </summary>
        public static IList<AttackHandInfo> EnumerateAttacks(UnitEntityData unit, UnitEntityData target, bool full)
        {
            try
            {
                var probe = new UnitAttack(target) { ForceFullAttack = full, IsSingleAttack = !full, OnlyForInspect = true };
                probe.Init(unit);
                return probe.AllAttacks;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] EnumerateAttacks {unit.CharacterName}: {ex.Message}");
                return Array.Empty<AttackHandInfo>();
            }
        }

        public static WeaponAttackEstimate EstimateWeaponAttack(UnitEntityData unit, UnitEntityData target, bool full)
        {
            var attacks = EnumerateAttacks(unit, target, full);
            if (attacks.Count == 0) return null;

            var result = new WeaponAttackEstimate { Attacks = attacks.Count };
            var firstWeapon = attacks[0].Weapon;
            result.Weapon = firstWeapon;
            result.IsRanged = firstWeapon?.Blueprint?.IsRanged ?? false;
            result.Reach = UnitAttack.GetApproachRadius(firstWeapon, unit, target);

            bool flanked = target.CombatState?.IsFlanked ?? false;
            int sneakDice = SafeStat(unit.Stats.SneakAttack);

            for (int i = 0; i < attacks.Count; i++)
            {
                var weapon = attacks[i].Weapon;
                if (weapon == null) continue;
                try
                {
                    int ab = Rulebook.Trigger(new RuleCalculateAttackBonus(unit, target, weapon, attacks[i].AttackBonusPenalty)
                    {
                        TargetIsFlanked = flanked
                    }).Result;
                    var acRule = Rulebook.Trigger(new RuleCalculateAC(unit, target, weapon.Blueprint.AttackType));
                    float p = HitProbability(ab, acRule.Result);
                    if (i == 0) result.FirstHitChance = p;

                    var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(unit, weapon));
                    float perHit = 0f;
                    foreach (var d in stats.DamageDescription)
                        perHit += Average(d.Dice) + d.Bonus;

                    var dr = HitChanceCalculator.CalculateDamageReduction(unit, target, weapon, perHit);
                    if (dr != null && !dr.CanBypass && dr.DRValue > 0)
                        perHit = Math.Max(0f, perHit - dr.DRValue);

                    float threatChance = Mathf.Clamp01(stats.CriticalRange / 20f);
                    float critExtra = threatChance * p * (stats.CriticalMultiplier - 1) * perHit;

                    float sneak = 0f;
                    if (sneakDice > 0 && (flanked || acRule.IsTargetFlatFooted))
                        sneak = 3.5f * sneakDice;

                    result.ExpectedDamage += p * (perHit + sneak) + critExtra;
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Math] attack estimate {unit.CharacterName}->{target.CharacterName}: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// ★ v0.3.9: expected effect of a coup de grace (Pathfinder rules): the attack hits automatically and is a critical
        /// hit with the wielded weapon; a target that survives must make a Fortitude save (DC 10 + damage dealt) or die.
        /// Creatures immune to critical hits and death effects (undead, constructs) only take the normal damage.
        /// Returns (damage, chance the target dies). Derived from the rules, not from a mod's component, so it works with
        /// and without Tabletop Tweaks (which replaces the vanilla implementation).
        /// </summary>
        public static bool EstimateCoupDeGrace(UnitEntityData unit, UnitEntityData target, out float damage, out float killChance)
        {
            damage = 0f;
            killChance = 0f;
            var weapon = unit?.Body?.PrimaryHand?.MaybeWeapon;
            if (weapon == null || target == null) return false;
            try
            {
                var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(unit, weapon));
                float perHit = 0f;
                foreach (var d in stats.DamageDescription)
                    perHit += Average(d.Dice) + d.Bonus;
                var dr = HitChanceCalculator.CalculateDamageReduction(unit, target, weapon, perHit);
                if (dr != null && !dr.CanBypass && dr.DRValue > 0)
                    perHit = Math.Max(0f, perHit - dr.DRValue);

                bool immune = AbilityClassifier.IsUndeadOrConstruct(target);
                damage = perHit * (immune ? 1 : Math.Max(2, stats.CriticalMultiplier));
                int sneakDice = SafeStat(unit.Stats.SneakAttack);       // a helpless target is denied its Dex bonus
                if (sneakDice > 0 && !immune) damage += 3.5f * sneakDice;

                if (damage >= target.HPLeft) killChance = 1f;
                else if (!immune)
                {
                    int fort = SafeStat(target.Stats.SaveFortitude);
                    float dc = 10f + damage;
                    killChance = Mathf.Clamp((dc - fort - 1f) / 20f, 0.05f, 0.95f);
                }
                return true;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] coup de grace {unit.CharacterName}->{target.CharacterName}: {ex.Message}");
                return false;
            }
        }

        private static int SafeStat(ModifiableValue value)
        {
            try { return value?.ModifiedValue ?? 0; } catch { return 0; }
        }

        #endregion

        #region Spells and abilities

        public static AbilityExecutionContext CreateContext(AbilityData ability, TargetWrapper target)
        {
            try
            {
                var ctx = ability.CreateExecutionContext(target);
                ctx.RecalculateRanks();
                return ctx;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] context for {ability.Name}: {ex.Message}");
                return null;
            }
        }

        public static float EvaluateDice(ContextDiceValue value, MechanicsContext ctx, int casterLevel,
                                         bool maximize = false, bool empower = false)
        {
            if (value == null) return 0f;
            int count = EvaluateValue(value.DiceCountValue, ctx, casterLevel);
            int bonus = EvaluateValue(value.BonusValue, ctx, 0);
            int sides = value.DiceType.Sides();
            // ★ v0.3.13: the game's metamagic rule (RuleCalculateDamage, RuleHealDamage, ContextValueHelper): Maximize
            // takes the highest roll of every die, Empower multiplies dice + bonus by 1.5.
            float total = (maximize ? count * sides : count * (sides + 1) / 2f) + bonus;
            return empower ? total * 1.5f : total;
        }

        /// <summary>
        /// ★ v0.3.13: Maximize / Empower on this cast, including the caster's automatic metamagic
        /// (AbilityData.HasMetamagic reads UnitPartSpecialMetamagic: AutoMetamagic toggles such as IsekaiMod's
        /// "Overpowered Ability - Auto Empower/Maximize", metamagic rods).
        /// </summary>
        private static void DamageMetamagic(AbilityData ability, out bool maximize, out bool empower)
        {
            maximize = empower = false;
            try
            {
                maximize = ability.HasMetamagic(Metamagic.Maximize);
                empower = ability.HasMetamagic(Metamagic.Empower);
            }
            catch { }
        }

        private static int EvaluateValue(ContextValue value, MechanicsContext ctx, int fallback)
        {
            if (value == null) return 0;
            if (value.ValueType == ContextValueType.Simple) return value.Value;
            try
            {
                if (ctx != null)
                {
                    int v = value.Calculate(ctx);
                    if (v > 0) return v;
                }
            }
            catch { }
            return fallback;
        }

        public static float EstimateAbilityDamage(AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (profile.Damage.Count == 0) return 0f;
            if (target != null && profile.Damage.All(d => profile.AppliesTo(d, ability, target) == EffectApplies.No)) return 0f;
            var ctx = CreateContext(ability, target != null ? new TargetWrapper(target) : null);
            int cl = Math.Max(1, ability.CalculateParams()?.CasterLevel ?? 1);
            DamageMetamagic(ability, out bool maximize, out bool empower);
            float total = 0f;
            foreach (var dmg in profile.Damage)
            {
                // ★ v0.3.1: only the branch that runs for this target (Inflict Wounds heals undead, Cure harms them).
                if (target != null && profile.AppliesTo(dmg, ability, target) == EffectApplies.No) continue;
                float avg = EvaluateDice(dmg.Value, ctx, Math.Min(cl, 10), maximize, empower);
                if (dmg.Half) avg *= 0.5f;
                total += avg;
            }
            return total;
        }

        public static float EstimateAbilityHeal(AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (profile.Heals.Count == 0) return 0f;
            if (target != null && profile.Heals.All(h => profile.AppliesTo(h, ability, target) == EffectApplies.No)) return 0f;
            var ctx = CreateContext(ability, target != null ? new TargetWrapper(target) : null);
            int cl = Math.Max(1, ability.CalculateParams()?.CasterLevel ?? 1);
            DamageMetamagic(ability, out bool maximize, out bool empower);
            float total = 0f;
            foreach (var heal in profile.Heals)
            {
                if (target != null && profile.AppliesTo(heal, ability, target) == EffectApplies.No) continue;
                total += EvaluateDice(heal.Value, ctx, Math.Min(cl, 5), maximize, empower);
            }
            return total;
        }

        /// <summary>
        /// ★ v0.3.14: chance <paramref name="caster"/> gets the spell off at all while entangled or under a "casting is
        /// difficult" condition. The game then makes a concentration check before every spell, potions and scrolls
        /// included, wands excepted (UnitUseAbility.MakeConcentrationCheckIfCastingIsDifficult, RuleCheckConcentration:
        /// d20 + concentration against 15 + spell level, twice the level when very difficult). Camellia's Daze (twice) and
        /// Cure Light Wounds and Seelah's potion failed inside Orvath's Entangle. 1 when no check applies.
        /// </summary>
        public static float CastingConcentrationChance(UnitEntityData caster, AbilityData ability)
        {
            try
            {
                if (caster == null || ability?.Blueprint == null || !ability.Blueprint.IsSpell) return 1f;
                if (ability.SourceItem != null && ability.SourceItemUsableBlueprint == null) return 1f;
                if (ability.SourceItemUsableBlueprint != null &&
                    ability.SourceItemUsableBlueprint.Type == Kingmaker.Blueprints.Items.Equipment.UsableItemType.Wand) return 1f;
                var state = caster.Descriptor.State;
                bool veryDifficult = state.HasCondition(UnitCondition.SpellCastingIsVeryDifficult);
                if (!veryDifficult && !state.HasCondition(UnitCondition.Entangled) && !state.HasCondition(UnitCondition.SpellCastingIsDifficult))
                    return 1f;
                var p = ability.CalculateParams();
                int dc = 15 + p.SpellLevel + (veryDifficult ? p.SpellLevel : 0);
                int need = dc - p.Concentration;                       // d20 result needed
                return Mathf.Clamp01((21f - need) / 20f);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>
        /// ★ v0.3.7: chance an ability's effect lands on <paramref name="enemy"/>: spell-resistance pass × attack-roll hit
        /// chance × failed save (for damage, a successful save still counts half when the spell does half on a save).
        /// One formula for damage/control scoring and Progressive's viability test, in both combat modes.
        /// </summary>
        public static float EffectChance(UnitEntityData caster, UnitEntityData enemy, AbilityData ability, AbilityProfile profile, bool damage)
        {
            float p = SpellResistancePass(caster, enemy, ability);
            if (profile.NeedsAttackRoll) p *= SpellAttackHitChance(caster, enemy, ability);
            if (profile.Save != SavingThrowType.Unknown)
            {
                float fail = SaveFailChance(ability, profile, enemy);
                p *= damage ? fail + (1f - fail) * (profile.HalfOnSave ? 0.5f : 0f) : fail;
            }
            return p;
        }

        /// <summary>Probability the target fails its save (1 when the ability has no save).</summary>
        public static float SaveFailChance(AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (profile.Save == SavingThrowType.Unknown || target == null) return 1f;
            try
            {
                int dc = ability.CalculateParams()?.DC ?? 10;
                int bonus;
                switch (profile.Save)
                {
                    case SavingThrowType.Fortitude: bonus = target.Stats.SaveFortitude.ModifiedValue; break;
                    case SavingThrowType.Reflex: bonus = target.Stats.SaveReflex.ModifiedValue; break;
                    case SavingThrowType.Will: bonus = target.Stats.SaveWill.ModifiedValue; break;
                    default: return 1f;
                }
                // Fails when d20 + bonus < DC.
                return Mathf.Clamp((dc - bonus - 1) / 20f, 0.05f, 0.95f);
            }
            catch
            {
                return 0.5f;
            }
        }

        public static float SpellResistancePass(UnitEntityData caster, UnitEntityData target, AbilityData ability)
        {
            if (target == null) return 1f;
            try
            {
                var sr = HitChanceCalculator.CalculateSpellResistance(caster, target, ability);
                return sr.IsImmune ? 0f : Mathf.Clamp01(sr.PenetrationChance);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>Chance an attack-roll spell (touch / ranged touch / weapon-delivered) hits.</summary>
        public static float SpellAttackHitChance(UnitEntityData caster, UnitEntityData target, AbilityData ability)
        {
            try
            {
                return Mathf.Clamp(HitChanceCalculator.CalculateAbilityHitChance(caster, target, ability).HitChance, 0.05f, 0.95f);
            }
            catch
            {
                return 0.6f;
            }
        }

        /// <summary>Use Magic Device success chance for items that need it (1 otherwise).</summary>
        public static float UseMagicDeviceChance(AbilityData ability, UnitEntityData unit)
        {
            try
            {
                int? dc = ability.UseMagicDeviceDC;
                if (!dc.HasValue) return 1f;
                if (!unit.Descriptor.HasUMDSkill) return 0f;
                int skill = unit.Stats.ActualSkillUseMagicDevice.ModifiedValue;
                return Mathf.Clamp((21f + skill - dc.Value) / 20f, 0.05f, 1f);
            }
            catch
            {
                return 1f;
            }
        }

        #endregion

        #region Threat

        /// <summary>
        /// Rough damage-per-round an enemy is worth removing. CR-based so it is stable and cheap; adjusted for enemies
        /// that are already disabled and for enemies currently attacking the party.
        /// </summary>
        /// <summary>★ v0.3.9: cannot act right now (asleep, paralysed, stunned, helpless...).</summary>
        public static bool IsDisabled(UnitEntityData unit)
        {
            var state = unit?.Descriptor?.State;
            if (state == null) return false;
            return state.HasCondition(UnitCondition.Sleeping) || state.HasCondition(UnitCondition.Paralyzed) ||
                   state.HasCondition(UnitCondition.Stunned) || state.HasCondition(UnitCondition.Helpless) ||
                   state.HasCondition(UnitCondition.CantAct) || state.IsUnconscious;
        }

        public static float Threat(UnitEntityData enemy)
        {
            if (enemy == null) return 0f;
            float cr = 1f;
            try { cr = Math.Max(1, enemy.Blueprint?.CR ?? 1); } catch { }
            float threat = 4f + 3f * cr;

            if (IsDisabled(enemy)) threat *= 0.25f;

            try { if (enemy.CombatState.EngagedUnits.Count > 0) threat *= 1.2f; } catch { }
            return threat;
        }

        #endregion
    }
}
