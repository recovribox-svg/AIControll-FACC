// Ordered-attack disruption: reuse the cached routine's line for the native hand/iterative penalty.
// Animation configuration can reorder UnitAttack.AllAttacks, and GetAttackIndex may already be past the first two.
using System;
using Kingmaker.Items;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        /// <summary>
        /// Read one pending native attack from the routine just quoted for that command. Match weapon identity and its
        /// native iterative penalty, not its animation index. No new routine, rule, actor query or allocation. An absent
        /// line is unknown and must not be credited with another line's chance of breaking a cast.
        /// </summary>
        internal static bool TryOrderedHit(WeaponAttackEstimate estimate, ItemEntityWeapon weapon, int penalty,
                                            out float hit, out float damage)
        {
            hit = damage = 0f;
            if (weapon == null || !(estimate?.RoutineIdentity is AttackRoutine routine) || routine.Frame != Time.frameCount)
                return false;
            foreach (var line in routine.Lines)
            {
                if (!ReferenceEquals(line.Weapon, weapon) || line.AttackBonusPenalty != penalty) continue;
                ExpectedWeaponLine(line, 0, 0f, estimate.RoutineLands, out hit, out damage);
                return true;
            }
            return false;
        }
    }
}
