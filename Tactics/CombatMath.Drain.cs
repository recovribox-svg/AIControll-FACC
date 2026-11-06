// ★ v0.3.28 test 13 (author's request): ability damage and negative levels read by the game's rules (ValueModel.Drain.cs
// says what they are worth). A ContextActionDealDamage is one of three kinds (its private m_Type): hit-point damage, ability
// damage (RuleDealStatDamage: AbilityType, Drain) or energy drain (RuleDrainEnergy: negative levels). Until test 12 every one
// was estimated as hit-point damage of its dice, through the target's damage reduction and energy resistance.
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Mechanics.Actions;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        internal enum DealKind { HitPoints, AbilityDamage, EnergyDrain }

        private static readonly Dictionary<ContextActionDealDamage, DealKind> s_DealKinds = new Dictionary<ContextActionDealDamage, DealKind>();

        /// <summary>The kind of <paramref name="deal"/> (its m_Type), once per action; hit points when it cannot be read.</summary>
        internal static DealKind KindOf(ContextActionDealDamage deal)
        {
            if (deal == null || DealDamageType == null) return DealKind.HitPoints;
            if (s_DealKinds.TryGetValue(deal, out var kind)) return kind;
            string type = DealDamageType.GetValue(deal)?.ToString();
            kind = type == "AbilityDamage" ? DealKind.AbilityDamage : type == "EnergyDrain" ? DealKind.EnergyDrain : DealKind.HitPoints;
            if (s_DealKinds.Count > 4096) s_DealKinds.Clear();
            s_DealKinds[deal] = kind;
            return kind;
        }

        /// <summary>The target's current score of <paramref name="stat"/>; 0 when it has none (a disabled score).</summary>
        internal static int AbilityScore(UnitEntityData unit, StatType stat)
        {
            try
            {
                // RuleDealStatDamage needs an enabled attribute (ModifiableValueAttributeStat.Enabled).
                var value = unit?.Stats?.GetStat(stat) as ModifiableValueAttributeStat;
                return value == null || !value.Enabled ? 0 : Math.Max(0, value.ModifiedValue);
            }
            catch { return 0; }
        }

        /// <summary>
        /// The game would refuse <paramref name="unit"/> this ability damage (AddImmunityToAbilityScoreDamage on its facts: the
        /// score listed, or every score when none is; damage, or drain too when it says so). A component with an ignore rule
        /// (some casters or descriptors get through) is read as not immune - the estimate keeps the damage rather than miss it.
        /// </summary>
        internal static bool AbilityDamageImmune(UnitEntityData unit, StatType stat, bool drain)
        {
            if (unit == null) return false;
            return FrameCached($"stat-immune|{unit.UniqueId}|{stat}|{drain}", () =>
            {
                bool immune = false;
                try
                {
                    Kingmaker.Utility.UnitDescription.UnitDescriptionHelper.VisitComponents<Kingmaker.UnitLogic.FactLogic.AddImmunityToAbilityScoreDamage>(
                        unit, (c, f) =>
                        {
                            if (c.UseIgnoreRule || drain && !c.Drain) return;
                            if (c.StatTypes == null || c.StatTypes.Length == 0 || Array.IndexOf(c.StatTypes, stat) >= 0) immune = true;
                        });
                }
                catch { }
                return immune ? 1 : 0;
            }) != 0;
        }

        /// <summary>The game would refuse <paramref name="unit"/> negative levels (AddImmunityToEnergyDrain on its facts: Death Ward,
        /// undead and construct traits).</summary>
        internal static bool EnergyDrainImmune(UnitEntityData unit)
        {
            if (unit == null) return false;
            return FrameCached($"drain-immune|{unit.UniqueId}", () =>
            {
                bool immune = false;
                try
                {
                    Kingmaker.Utility.UnitDescription.UnitDescriptionHelper.VisitComponents<Kingmaker.UnitLogic.FactLogic.AddImmunityToEnergyDrain>(
                        unit, (c, f) => immune = true);
                }
                catch { }
                return immune ? 1 : 0;
            }) != 0;
        }

        /// <summary>
        /// What <paramref name="amount"/> (the action's dice average: points of ability damage, or negative levels) of a
        /// non-hit-point <paramref name="deal"/> is worth in hit points on <paramref name="target"/> (ValueModel.Drain.cs);
        /// 0 when the target is immune or has no such score.
        /// </summary>
        internal static float DrainEquivalent(ContextActionDealDamage deal, DealKind kind, float amount, UnitEntityData target)
        {
            if (target == null || !(amount > 0f)) return 0f;
            try
            {
                if (kind == DealKind.AbilityDamage)
                {
                    if (AbilityDamageImmune(target, deal.AbilityType, deal.Drain)) return 0f;
                    return ValueModel.AbilityDamageEquivalent(amount, AbilityScore(target, deal.AbilityType), target.HPLeft);
                }
                if (kind == DealKind.EnergyDrain)
                {
                    if (EnergyDrainImmune(target)) return 0f;
                    int level = Math.Max(1, target.Descriptor?.Progression?.CharacterLevel ?? 1);
                    return ValueModel.EnergyDrainEquivalent(amount, level, target.HPLeft);
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] drain estimate on {target.CharacterName}: {ex.Message}");
            }
            return 0f;
        }
    }
}
