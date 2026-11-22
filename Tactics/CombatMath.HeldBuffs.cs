// ★ v0.3.50 review1: A held flat-stat buff is removed from the routine already read, without another native calculation or actor preview.
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        /// <summary>The shared scalar kernel for an ordinary quote and a held-buff removal quote.</summary>
        private static float ExpectedWeaponLine(AttackLine line, int attackChange, float damageChange, float lands,
                                                 out float hit, out float perHit) =>
            RepriceWeaponLine(line, attackChange, damageChange, lands, false, out hit, out perHit);

        private static float RepriceWeaponLine(AttackLine line, int attackChange, float damageChange, float lands,
                                                bool bypassReduction, out float hit, out float perHit)
        {
            hit = HitProbability(line.AttackBonus + attackChange, line.ArmorClass) * lands;
            // Preserve the existing native-derived damage, DR, physical factor and critical model, including a
            // kinetic blade's separate blast. Removing physical damage cannot remove that blast or the sneak dice.
            float modeChange = Math.Max(0f, line.RawPerHit + damageChange - (bypassReduction ? 0 : line.DamageReduction)) -
                               Math.Max(0f, line.RawPerHit - line.DamageReduction);
            perHit = Math.Max(0f, line.GameHit + modeChange * line.PhysicalFactor) + line.BladeBlast;
            float critExtra = hit * perHit * (ValueModel.CritFactor(line.ThreatChance, hit, line.CritMultiplier) - 1f);
            // ★ sneak attack (slice D1): the target's fortification negates sneak dice and critical extra together.
            return SneakRules.LineDamage(hit, perHit, line.Sneak, critExtra, line.Keep);
        }

        internal static float AttackMarkGain(UnitEntityData unit, UnitEntityData target, bool full, int attackBonus,
                                             float damageBonus, bool bypassReduction, Kingmaker.Enums.WeaponRangeType? onlyRange)
        {
            string key = $"{unit.UniqueId}|{target.UniqueId}|{full}";
            if (!Routines.TryGetValue(key, out var routine) || routine.Frame != Time.frameCount || routine.Lines.Count == 0)
            {
                if (full) return AttackMarkGain(unit, target, false, attackBonus, damageBonus, bypassReduction, onlyRange);
                return 0f;
            }
            // The native weapon routes already read this frame supply the baseline. Reuse their reach,
            // DR, concealment and crit math. A mark alone does not request another weapon or path preview.
            var first = routine.Lines[0].Weapon;
            if (first == null || Vector3.Distance(unit.Position, target.Position) >
                routine.Reach + unit.Corpulence + target.Corpulence) return 0f;
            float lands = LandsPastDefences(unit, target);
            float gain = 0f;
            foreach (var line in routine.Lines)
            {
                int lineBonus = !onlyRange.HasValue || Kingmaker.Enums.WeaponRangeTypeHelper.IsSuitableWeapon(onlyRange.Value, line.Weapon.Blueprint)
                    ? attackBonus : 0;
                float before = ExpectedWeaponLine(line, 0, 0f, lands, out _, out _);
                float after = RepriceWeaponLine(line, lineBonus, damageBonus, lands, bypassReduction, out _, out _);
                gain += Math.Max(0f, after - before);
            }
            return gain;
        }

        /// <summary>
        /// Reprice only the exact routine behind the supplied quote while it remains in this frame's cache. No
        /// ReadRoutine, Rulebook, defence, magus-state or actor query is performed. A mixed-weapon routine is outside
        /// this uniform primary-weapon contract; the caller retains its existing appraisal in that case.
        /// </summary>
        internal static bool TryHeldWeaponLoss(UnitEntityData unit, UnitEntityData target, bool full,
                                                WeaponAttackEstimate current, int attackLoss, float damageLoss,
                                                out float loss)
        {
            loss = 0f;
            if (unit == null || target == null || current == null || current.Attacker != unit) return false;
            string key = $"{unit.UniqueId}|{target.UniqueId}|{full}";
            if (!Routines.TryGetValue(key, out var routine) || routine.Frame != Time.frameCount ||
                !ReferenceEquals(current.RoutineIdentity, routine)) return false;
            return HeldWeaponRoutineLoss(routine, current.Weapon, current.ExpectedDamage, current.RoutineLands,
                                         attackLoss, damageLoss, out loss);
        }

        /// <summary>Pure cached-line calculation; also used by actor-free native audit fixtures.</summary>
        private static bool HeldWeaponRoutineLoss(AttackRoutine routine, ItemEntityWeapon weapon, float currentDamage,
                                                  float lands, int attackLoss, float damageLoss, out float loss)
        {
            loss = 0f;
            if (routine == null || weapon == null || routine.Lines.Count == 0 || attackLoss < 0 || damageLoss < 0f ||
                float.IsNaN(damageLoss) || float.IsInfinity(damageLoss) || !(currentDamage >= 0f) ||
                float.IsInfinity(currentDamage) || !(lands >= 0f && lands <= 1f)) return false;
            float after = 0f;
            foreach (var line in routine.Lines)
            {
                if (line.Weapon != weapon) return false;
                after += ExpectedWeaponLine(line, -attackLoss, -damageLoss, lands, out _, out _);
            }
            loss = Math.Max(0f, currentDamage - after);
            return true;
        }
    }
}
