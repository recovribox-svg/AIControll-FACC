using System;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Commands;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        /// <summary>
        /// ★ v0.3.70 F2 (author request): a weapon attack with a weapon of another weapon set, without switching to it (a
        /// switch would run the game's equipment handlers and swap the model). The game's own rules take any weapon:
        /// RuleCalculateAttackBonus, RuleCalculateAC, RuleCalculateWeaponStats and the fake damage on the target. Two things
        /// the inactive set does not give: its enchantments are switched off with its slot (ItemSlot.OnActiveChanged), so
        /// their enhancement bonus (WeaponEnhancementBonus) is added back to attack and damage; and the attack count, which
        /// RuleCalculateAttacksCount reads from the active hands, is the main hand's by the same rule (one attack, the
        /// iteratives of the base attack bonus, the extra haste attack the unit has now). The off hand, special weapon
        /// abilities (flaming...) and attack modes are left out: the estimate errs low.
        /// </summary>
        internal static WeaponAttackEstimate EstimateWithWeapon(UnitEntityData unit, UnitEntityData target, ItemEntityWeapon weapon, bool full)
        {
            if (unit?.View is null || target?.View is null || weapon?.Blueprint == null) return null;
            try
            {
                int enhancement = InactiveEnhancement(weapon);
                var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(unit, weapon));
                float perHit = enhancement;
                foreach (var d in stats.DamageDescription) perHit += Average(d.Dice) + d.Bonus;
                var hit = ReadHitOnTarget(unit, target, weapon, stats, cache: false);
                float gameHit = hit.Read ? hit.Damage + enhancement * hit.PhysicalFactor : perHit;
                int ac = Rulebook.Trigger(new RuleCalculateAC(unit, target, weapon.Blueprint.AttackType)).Result;
                float lands = LandsPastDefences(unit, target);
                float threat = hit.CritImmune ? 0f : Mathf.Clamp01(stats.CriticalRange / 20f);
                int multiplier = Math.Max(1, stats.CriticalMultiplier);

                int extra = 0, iteratives = 0;
                if (full)
                {
                    iteratives = IterativeAttacks(unit);
                    var count = Rulebook.Trigger(new RuleCalculateAttacksCount(unit));
                    extra = Math.Max(0, count.Result.PrimaryHand.HasteAttacks);
                }
                var est = new WeaponAttackEstimate
                {
                    Attacker = unit, Weapon = weapon, IsRanged = weapon.Blueprint.IsRanged,
                    Reach = UnitAttack.GetApproachRadius(weapon, unit, target), Attacks = 1 + extra + iteratives,
                    CritMultiplier = multiplier, FirstPerHit = gameHit, FirstArmorClass = ac
                };
                float noHit = 1f;
                for (int i = 0; i < est.Attacks; i++)
                {
                    int penalty = i <= extra ? 0 : 5 * (i - extra);
                    int ab = Rulebook.Trigger(new RuleCalculateAttackBonus(unit, target, weapon, penalty)).Result + enhancement;
                    float p = Mathf.Clamp((21f + ab - ac) / 20f, 0.05f, 0.95f) * lands;
                    if (i == 0) { est.FirstHitChance = p; est.FirstAttackBonus = ab; est.FirstCritChance = Math.Min(threat, p) * p; }
                    est.ExpectedDamage += p * gameHit + Math.Min(threat, p) * p * (multiplier - 1) * gameHit;
                    noHit *= 1f - p;
                }
                est.AnyHitChance = 1f - noHit;
                return est;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] other-set estimate {unit.CharacterName}->{target.CharacterName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>The enhancement bonus an inactive weapon's switched-off enchantments would add (0 for an active one).</summary>
        private static int InactiveEnhancement(ItemEntityWeapon weapon)
        {
            try
            {
                if (weapon.HoldingSlot?.Active ?? false) return 0;
                int bonus = 0;
                foreach (var enchantment in weapon.Enchantments)
                    foreach (var c in enchantment.Blueprint.ComponentsArray)
                        if (c is Kingmaker.Designers.Mechanics.Facts.WeaponEnhancementBonus e) bonus = Math.Max(bonus, e.EnhancementBonus);
                return bonus;
            }
            catch { return 0; }
        }

        /// <summary>The iterative attacks of the base attack bonus, as RuleCalculateAttacksCount counts them.</summary>
        internal static int IterativeAttacks(UnitEntityData unit)
        {
            int bab = unit.Stats.BaseAttackBonus;
            int n = Math.Max(0, bab / 5 - (bab % 5 == 0 ? 1 : 0));
            if (n > 3 && unit.Get<Kingmaker.UnitLogic.Parts.UnitPartCompanion>() == null) n = 3;
            return n;
        }
    }
}
