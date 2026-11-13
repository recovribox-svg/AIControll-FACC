// ★ v0.3.50 review1: Profane Invocation exposed a general asymmetry: adding another +1 attack at the holder's current 95% hit chance
// is worthless, but removing its existing +1 can lose hits. Reprice known flat-stat losses from cached native lines.
using System;
using System.Collections.Generic;
using System.Reflection;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.Designers.Mechanics.Facts.Restrictions;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static readonly FieldInfo HeldBonusRestrictions = HarmonyLib.AccessTools.Field(typeof(AddContextStatBonus), "Restrictions");
        private static readonly Dictionary<BlueprintBuff, AddContextStatBonus> s_HeldWeaponBonuses =
            new Dictionary<BlueprintBuff, AddContextStatBonus>();

        /// <summary>
        /// Deliberately narrow native contract: one constant AddContextStatBonus, optionally marked UniqueBuff.
        /// No conditional provider, inherited component, child effect, equipment change or context evaluation.
        /// </summary>
        private static AddContextStatBonus ReadHeldWeaponBonus(BlueprintBuff buff)
        {
            if (buff?.ComponentsArray == null || buff.ComponentsArray.Length < 1 || buff.ComponentsArray.Length > 2) return null;
            AddContextStatBonus bonus = null;
            bool unique = false;
            foreach (var component in buff.ComponentsArray)
            {
                if (component?.GetType() == typeof(AddContextStatBonus) && bonus == null) bonus = (AddContextStatBonus)component;
                else if (component?.GetType() == typeof(UniqueBuff) && !unique) unique = true;
                else return null;
            }
            if (bonus == null || bonus.HasMinimal || bonus.Value?.ValueType != ContextValueType.Simple ||
                bonus.Value.Value <= 0 || bonus.Multiplier <= 0 || (long)bonus.Value.Value * bonus.Multiplier > int.MaxValue ||
                (AttributeIndex(bonus.Stat) < 0 && bonus.Stat != StatType.AdditionalAttackBonus && bonus.Stat != StatType.AdditionalDamage) ||
                HeldBonusRestrictions == null || !(HeldBonusRestrictions.GetValue(bonus) is RestrictionCalculator restrictions) ||
                restrictions.Property != null && !restrictions.Property.Empty) return null;
            return bonus;
        }

        private static AddContextStatBonus HeldWeaponBonus(BlueprintBuff buff)
        {
            if (buff == null) return null;
            if (s_HeldWeaponBonuses.TryGetValue(buff, out var known)) return known;
            if (s_HeldWeaponBonuses.Count >= 128) s_HeldWeaponBonuses.Clear();
            return s_HeldWeaponBonuses[buff] = ReadHeldWeaponBonus(buff);
        }

        private static bool TryAppraiseHeldWeaponLoss(UnitEntityData holder, Offence off, List<BlueprintBuff> buffs,
                                                       List<BuffPart> parts, out float loss)
        {
            loss = 0f;
            var held = s_AppraiseWithout;
            if (held == null || holder == null || off?.WeaponQuote == null || buffs.Count != 1 || parts.Count != 1 ||
                buffs[0] != held.Blueprint || parts[0].Owner != buffs[0]) return false;
            var bonus = HeldWeaponBonus(buffs[0]);
            if (bonus == null) return false;
            int amount = bonus.Value.Value * bonus.Multiplier;
            var stat = holder.Stats.GetStat(bonus.Stat);
            if (stat == null) return false;
            // Arcanist Powerful Change and other live changes can differ from the declared constant. Require the
            // actual source modifier rather than treating the blueprint amount as proof that it is active.
            int sources = 0;
            foreach (var modifier in stat.Modifiers)
            {
                if (modifier.Source != held) continue;
                if (modifier.ModDescriptor != bonus.Descriptor || modifier.ModValue != amount) return false;
                sources++;
            }
            if (sources != 1) return false;
            float increment = Increment(holder, bonus.Stat, parts[0], amount);
            if (!(increment >= 0f && increment <= int.MaxValue) || increment != (int)increment) return false;
            int attackLoss = 0;
            float damageLoss = 0f;
            if (AttributeIndex(bonus.Stat) >= 0)
            {
                // Native attack and damage stats were captured with this weapon. Extra fact-derived attack/damage
                // links retain the old approximation; their rules may have thresholds not described by this contract.
                var derived = UnitStatUse(holder, bonus.Stat);
                if (derived.Attack != 0f || derived.Damage != 0f ||
                    !HeldAttributeWeaponDelta(bonus.Stat, stat.ModifiedValue, (int)increment, off.AttackStat,
                                              off.DamageStat, off.DamageStatMultiplier, out attackLoss, out damageLoss)) return false;
            }
            else if (bonus.Stat == StatType.AdditionalAttackBonus) attackLoss = (int)increment;
            else if (bonus.Stat == StatType.AdditionalDamage) damageLoss = increment;
            return CombatMath.TryHeldWeaponLoss(holder, off.WeaponTarget, off.WeaponFull, off.WeaponQuote,
                                                attackLoss, damageLoss, out loss);
        }

        /// <summary>
        /// Native ModifiableValueAttributeStat.Bonus is score/2-5. RuleCalculateWeaponStats truncates the modifier
        /// times its captured damage multiplier, using x1 for a negative modifier. This includes odd scores and the
        /// two-handed 1.5 rounding boundary instead of treating every +2 score as a fractional per-hit approximation.
        /// </summary>
        private static bool HeldAttributeWeaponDelta(StatType stat, int score, int scoreLoss, StatType attackStat,
                                                      StatType? damageStat, float damageMultiplier,
                                                      out int attackLoss, out float damageLoss)
        {
            attackLoss = 0;
            damageLoss = 0f;
            if (AttributeIndex(stat) < 0 || scoreLoss < 0 || score <= scoreLoss ||
                !(damageMultiplier >= 0f) || float.IsInfinity(damageMultiplier)) return false;
            int before = score / 2 - 5, after = (score - scoreLoss) / 2 - 5;
            if (attackStat == stat) attackLoss = before - after;
            if (damageStat == stat)
            {
                float oldDamage = before * (before < 0 ? 1f : damageMultiplier);
                float newDamage = after * (after < 0 ? 1f : damageMultiplier);
                if (!(oldDamage >= int.MinValue && oldDamage < int.MaxValue && newDamage >= int.MinValue && newDamage < int.MaxValue)) return false;
                damageLoss = (int)oldDamage - (float)(int)newDamage;
            }
            return true;
        }
    }
}
