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

        public static float EvaluateDice(ContextDiceValue value, MechanicsContext ctx, int casterLevel)
        {
            if (value == null) return 0f;
            int count = EvaluateValue(value.DiceCountValue, ctx, casterLevel);
            int bonus = EvaluateValue(value.BonusValue, ctx, 0);
            int sides = value.DiceType.Sides();
            return count * (sides + 1) / 2f + bonus;
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
            float total = 0f;
            foreach (var dmg in profile.Damage)
            {
                // ★ v0.3.1: only the branch that runs for this target (Inflict Wounds heals undead, Cure harms them).
                if (target != null && profile.AppliesTo(dmg, ability, target) == EffectApplies.No) continue;
                float avg = EvaluateDice(dmg.Value, ctx, Math.Min(cl, 10));
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
            float total = 0f;
            foreach (var heal in profile.Heals)
            {
                if (target != null && profile.AppliesTo(heal, ability, target) == EffectApplies.No) continue;
                total += EvaluateDice(heal.Value, ctx, Math.Min(cl, 5));
            }
            return total;
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
        public static float Threat(UnitEntityData enemy)
        {
            if (enemy == null) return 0f;
            float cr = 1f;
            try { cr = Math.Max(1, enemy.Blueprint?.CR ?? 1); } catch { }
            float threat = 4f + 3f * cr;

            var state = enemy.Descriptor?.State;
            if (state != null && (state.HasCondition(UnitCondition.Sleeping) || state.HasCondition(UnitCondition.Paralyzed) ||
                                  state.HasCondition(UnitCondition.Stunned) || state.HasCondition(UnitCondition.Helpless) ||
                                  state.HasCondition(UnitCondition.CantAct) || state.IsUnconscious))
                threat *= 0.25f;

            try { if (enemy.CombatState.EngagedUnits.Count > 0) threat *= 1.2f; } catch { }
            return threat;
        }

        #endregion
    }
}
