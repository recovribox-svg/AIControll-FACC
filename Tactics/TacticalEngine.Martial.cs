// ★ v0.3.18 (docs/ACTIVE_ABILITY_PLAN.md AA-4): martial abilities valued by the game's own rules.
//
//   Combat maneuvers (ContextActionCombatManeuver and its custom version, ContextActionDisarm): the chance comes from
//   RuleCombatManeuver's roll (CombatMath.ManeuverOdds), the worth from what ApplyManeuver does on a success:
//     - trip, overrun, awesome blow: the target falls prone (if it can be knocked down), for one round: it spends its
//       move standing up, provoking, and fights at -4 meanwhile - soft control (0.4 x its worth per round);
//     - disarm: the weapon in its hand (not natural, not unarmed) for (margin / 5 + 1) rounds - 0.6;
//     - dirty trick blind / entangle: those conditions for the duration - soft control (0.4); sickened and sunder armor
//       (an AC penalty): a plain debuff (0.25);
//     - bull rush (Hydraulic Push): the target is pushed 5 feet + the margin away from the caster; an enemy fighting a
//       party member in melee must walk back and loses its full attack next turn (turn-based) - 0.2, one round; nothing
//       otherwise. Pull, grapple: a position change the engine does not value (0).
//
//   Vital Strike (AbilityCustomVitalStrike): one attack with the first weapon, plus (VitalStrikeMod - 1) more rolls of its
//   damage dice after the critical multiplier (CombatMath.VitalStrikeExtra) - a standard action that beats a single
//   attack and, after a move, a full attack the unit cannot make.
//   Cleave (AbilityCustomCleave): an attack on the target, then on the other enemies in the melee hand's reach by distance
//   to it, each made only while the attacks before it hit; two at most, all with Great Cleave.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Root;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>
        /// What a successful maneuver of this type is worth on <paramref name="enemy"/>, as a share of its worth per round
        /// (the control scale), and whether it lasts the maneuver's duration (<paramref name="lasting"/>) or one round.
        /// 0 when it would do nothing to this enemy.
        /// </summary>
        private static float ManeuverStrength(UnitEntityData enemy, AbilityData ability, CombatManeuver type, out bool lasting)
        {
            lasting = true;
            try
            {
                var mechanics = BlueprintRoot.Instance.SystemMechanics;
                switch (type)
                {
                    case CombatManeuver.Trip:
                    case CombatManeuver.Overrun:
                    case CombatManeuver.AwesomeBlow:
                        lasting = false;
                        return enemy.CanBeKnockedOff() ? 0.4f : 0f;
                    case CombatManeuver.Disarm:
                        if (enemy.Buffs.HasFact(mechanics.DisarmMainHandBuff) || enemy.Buffs.HasFact(mechanics.DisarmOffHandBuff)) return 0f;
                        return HoldsManufacturedWeapon(enemy) ? 0.6f : 0f;
                    case CombatManeuver.DirtyTrickBlind:
                        return BuffWorth(enemy, ability, mechanics.DirtyTrickBlindnessBuff, 0.4f);
                    case CombatManeuver.DirtyTrickEntangle:
                        return BuffWorth(enemy, ability, mechanics.DirtyTrickEntangledBuff, 0.4f);
                    case CombatManeuver.DirtyTrickSickened:
                        return BuffWorth(enemy, ability, mechanics.DirtyTrickSickenedBuff, 0.25f);
                    case CombatManeuver.BullRush:
                        lasting = false;
                        return EngagesParty(enemy) && !CombatMemory.IsRealTime ? 0.2f : 0f;
                    case CombatManeuver.SunderArmor:
                        return BuffWorth(enemy, ability, mechanics.SunderArmorBuff, 0.25f);
                    default:
                        return 0f;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] maneuver {type} on {enemy?.CharacterName}: {ex.Message}");
                return 0f;
            }
        }

        private static float BuffWorth(UnitEntityData enemy, AbilityData ability, BlueprintBuff buff, float strength)
        {
            if (buff == null || enemy.Buffs.HasFact(buff) || CombatMath.BuffImmune(enemy, buff, ability)) return 0f;
            return strength;
        }

        /// <summary>The enemy is in melee with a party member now.</summary>
        private static bool EngagesParty(UnitEntityData enemy)
        {
            try { return enemy.CombatState.EngagedUnits.Any(u => u != null && u.IsPlayerFaction && u.Descriptor.State.IsConscious); }
            catch { return false; }
        }

        /// <summary>Disarm takes a weapon that is neither natural nor unarmed (RuleCombatManeuver.ApplyManeuver).</summary>
        private static bool HoldsManufacturedWeapon(UnitEntityData unit)
        {
            foreach (var weapon in new[] { unit.Body.PrimaryHand.MaybeWeapon, unit.Body.SecondaryHand.MaybeWeapon })
                if (weapon != null && !weapon.Blueprint.IsUnarmed && !weapon.Blueprint.IsNatural) return true;
            return false;
        }

        /// <summary>Vital Strike or Cleave on <paramref name="target"/>, valued like the weapon attacks they are.</summary>
        private static float WeaponAbilityValue(TacticalContext ctx, AbilityProfile profile, UnitEntityData target, out string why)
        {
            why = "dmg";
            if (target == null || !ctx.Unit.IsEnemy(target)) { why = "not an enemy"; return 0f; }
            float value = profile.VitalStrike != null ? VitalStrikeValue(ctx, profile.VitalStrike, target)
                                                      : CleaveValue(ctx, profile.Cleave, target, out why);
            if (value > 0f) ctx.AimCounted = true;
            else why = "nothing to gain";
            return Math.Max(0f, value);
        }

        private static float VitalStrikeValue(TacticalContext ctx, AbilityCustomVitalStrike vitalStrike, UnitEntityData target)
        {
            var unit = ctx.Unit;
            var est = CombatMath.EstimateWeaponAttack(unit, target, full: false);
            if (est == null) return 0f;
            float expected = est.ExpectedDamage + CombatMath.VitalStrikeExtra(unit, est, vitalStrike);
            float value = DamageValue(target, expected, est.AnyHitChance) * AttackWeight(ctx, target, est.IsRanged) *
                          WakeFactor(target, expected);
            // A shot provokes like any ranged attack roll (where the unit stands now; after a move the ability's approach
            // prices the move).
            if (est.IsRanged && ProvokeObserver.WeaponProvokes(unit, est.Weapon))
            {
                var provoke = AttackRollProvokeRisk(ctx, unit.Position, 1);
                if (provoke != null) value = value * (1f - provoke.StopChance) - provoke.Cost;
            }
            return value;
        }

        private static float CleaveValue(TacticalContext ctx, AbilityCustomCleave cleave, UnitEntityData target, out string why)
        {
            why = "dmg";
            var unit = ctx.Unit;
            var hand = unit.GetThreatHandMelee();
            if (hand == null) return 0f;
            bool greater = cleave.GreaterFeature != null && unit.Descriptor.Progression.Features.HasFact(cleave.GreaterFeature);
            // Who else is in reach: from here when the target already is; otherwise (a move first) only the enemies next
            // to the target, at a discount for not knowing where the move ends.
            bool reachNow = unit.IsReach(target, hand);
            var others = ctx.Enemies
                .Where(e => e != target && e.Descriptor.State.IsConscious &&
                            (reachNow ? unit.IsReach(e, hand)
                                      : Vector3.Distance(e.Position, target.Position) <= 1.5f + e.Corpulence + target.Corpulence))
                .OrderBy(e => (e.Position - target.Position).sqrMagnitude)
                .ToList();
            var chain = new List<UnitEntityData> { target };
            chain.AddRange(others.Take(greater ? others.Count : 1));

            float value = 0f, reached = 1f;
            int counted = 0;
            foreach (var enemy in chain)
            {
                var est = CombatMath.EstimateWeaponAttack(unit, enemy, full: false);
                if (est == null) break;
                float part = reached * DamageValue(enemy, est) * WakeFactor(enemy, est.ExpectedDamage);
                value += enemy == target || reachNow ? part : part * 0.7f;
                reached *= est.FirstHitChance;
                counted++;
            }
            if (counted > 1) why = $"dmg:cleave {counted}";
            return value * AttackWeight(ctx, target, false);
        }
    }
}
