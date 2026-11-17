// ★ sneak attack refinements (17 November 2026; docs/BURST_AIM_AND_TACTICS_PLAN.md §6, slice D): the game side of
// fortification and of the flanking a second attacker creates. Pure rules: SneakRules. Read only: engagement sets, the
// fortification part and the native threat hand. Partner counterfactuals trigger the native attack-bonus rule once per
// line of the cached routine; actors and engagement are never changed.
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Parts;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        /// <summary>D1: the target's fortification chance, as RuleAttackRoll reads it (the highest of its values).</summary>
        internal static int FortificationChance(UnitEntityData target)
        {
            try { return target?.Get<UnitPartFortification>()?.Value ?? 0; }
            catch { return 0; }
        }

        /// <summary>
        /// D1: a critical factor (1 + the critical share of the damage) after the target's fortification roll, which
        /// negates a confirmed critical of any attack roll (RuleAttackRoll).
        /// </summary>
        internal static float FortifiedCritFactor(float factor, UnitEntityData target) =>
            factor > 1f ? 1f + (factor - 1f) * SneakRules.FortificationKeep(FortificationChance(target)) : factor;

        /// <summary>
        /// D2: the units besides <paramref name="attacker"/> that count as engaging <paramref name="target"/> for
        /// UnitCombatState.IsFlanked - turn-based every engager, real time those whose command targets it - and the one of
        /// them when there is exactly one.
        /// </summary>
        internal static int OtherEngagers(UnitEntityData target, UnitEntityData attacker, out UnitEntityData sole)
        {
            sole = null;
            int count = 0;
            try
            {
                var state = target?.CombatState;
                if (state == null) return 0;
                // Native order: the turn-based count applies to a target in combat; otherwise the command rule.
                bool turnBased = TurnBased.Controllers.CombatController.IsInTurnBasedCombat() && target.IsInCombat;
                foreach (var engager in state.EngagedBy)
                {
                    if (engager == null || engager == attacker) continue;
                    if (!turnBased && !engager.Commands.AnyCommandTargets(target)) continue;
                    count++;
                    sole = count == 1 ? engager : null;
                }
            }
            catch
            {
                sole = null;
                return 0;
            }
            return count;
        }

        /// <summary>
        /// D2 / F1 (docs/REVIEW_FOLLOWUPS_PLAN.md, D-R1): <paramref name="unit"/>'s melee attack with <paramref name="weapon"/>
        /// makes it an engager of <paramref name="target"/> when it lands. The native engagement state when the unit engages
        /// the target already; otherwise its threat range must cover the weapon's and the target must not be totally
        /// concealed from it now (SneakRules.WillEngage). Line of sight and height at a future arrival point are taken from
        /// the attack being possible there (the dispatcher's path-end check), not re-proved. Ranged attacks use only the
        /// native state.
        /// </summary>
        internal static bool AttackerEngages(UnitEntityData unit, UnitEntityData target, ItemEntityWeapon weapon)
        {
            try
            {
                if (unit == null || target == null || weapon?.Blueprint == null || !weapon.Blueprint.IsMelee) return false;
                if (unit.CombatState?.IsEngage(target) == true) return true;   // the controller's own answer
                bool canAct = unit.Descriptor.State.CanAct;
                var hand = canAct ? unit.GetThreatHand() : null;
                float? threat = hand != null ? unit.GetThreatRange(hand) : null;
                if (!threat.HasValue) return false;
                bool total = UnitPartConcealment.Calculate(unit, target) == Kingmaker.Enums.Concealment.Total;
                return SneakRules.WillEngage(false, canAct, threat.Value, weapon.AttackRange.Meters, total);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// D2: whether <paramref name="target"/> will be flanked when <paramref name="unit"/>'s attack with
        /// <paramref name="weapon"/> lands, and whether that is new (this attack is the second engager).
        /// </summary>
        private static bool PredictFlank(UnitEntityData unit, UnitEntityData target, ItemEntityWeapon weapon,
                                         out bool bySecond, out UnitEntityData partner)
        {
            bySecond = false;
            partner = null;
            bool native = target.CombatState?.IsFlanked ?? false;
            if (native) return true;
            bool cannot;
            try { cannot = target.Descriptor.State.Features.CannotBeFlanked; }
            catch { cannot = true; }
            if (cannot || !AttackerEngages(unit, target, weapon)) return false;
            int others = OtherEngagers(target, unit, out partner);
            bySecond = SneakRules.PredictsFlank(false, cannot, true, others);
            if (!bySecond) partner = null;
            return bySecond;
        }

        /// <summary>
        /// D2: what <paramref name="partner"/> gains for a round once <paramref name="target"/> is flanked: the native attack
        /// bonus with TargetIsFlanked, and sneak dice where they did not apply yet. Reuse the ordinary line kernel, including
        /// its existing sneak and critical damage. Read at most once per cached routine; never change either actor's state.
        /// </summary>
        internal static float FlankPartnerGain(UnitEntityData partner, UnitEntityData target)
        {
            try
            {
                var routine = ReadRoutine(partner, target, full: true);
                if (routine == null || routine.Flanked || routine.Lines.Count == 0) return 0f;
                if (routine.PartnerFlankGain.HasValue) return routine.PartnerFlankGain.Value;
                int dice = SafeStat(partner.Stats.SneakAttack);
                bool sneaks = dice > 0 && !ImmuneToPrecision(target);
                float lands = LandsPastDefences(partner, target);
                float gain = 0f;
                foreach (var line in routine.Lines)
                {
                    int flankedBonus = Rulebook.Trigger(new RuleCalculateAttackBonus(partner, target, line.Weapon, line.AttackBonusPenalty)
                    {
                        TargetIsFlanked = true
                    }).Result;
                    float before = ExpectedWeaponLine(line, 0, 0f, lands, out _, out _);
                    float after = ExpectedWeaponLine(line, flankedBonus - line.AttackBonus, 0f, lands, out float hit, out _);
                    float newSneak = sneaks && line.Sneak <= 0f ? 3.5f * dice * line.PhysicalFactor : 0f;
                    gain += SneakRules.PartnerLineGain(before, after, hit, newSneak, line.Keep);
                }
                routine.PartnerFlankGain = gain;
                return gain;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] flank partner {partner?.CharacterName}->{target?.CharacterName}: {ex.Message}");
                return 0f;
            }
        }
    }
}
