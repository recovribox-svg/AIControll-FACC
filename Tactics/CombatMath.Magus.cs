using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        // A scalar forecast belongs to one synchronous payload evaluation, never the shared blueprint/roll cache.
        [ThreadStatic] private static MagusRollScope s_MagusRoll;
        internal sealed class MagusRollScope : IDisposable
        {
            private readonly MagusRollScope previous;
            private readonly AbilityData ability;
            private readonly UnitEntityData target;
            internal readonly float Hit, CriticalFactor;
            internal MagusRollScope(AbilityData ability, UnitEntityData target, WeaponAttackEstimate weapon, bool critical = true)
            {
                this.ability = ability; this.target = target;
                Hit = weapon?.TouchHitChance ?? 1f;
                // ContextActionDealDamage.GetDamageInfo caps a Magus spell's critical multiplier at x2.
                CriticalFactor = !critical || weapon == null || Hit <= 0f ? 1f : 1f + weapon.TouchCritChance / Hit;
                previous = s_MagusRoll; s_MagusRoll = this;
            }
            internal bool Matches(AbilityData a, UnitEntityData t) => ReferenceEquals(ability, a) && ReferenceEquals(target, t);
            public void Dispose() { s_MagusRoll = previous; }
        }

        internal static WeaponAttackEstimate MagusWeaponAttack(UnitEntityData unit, UnitEntityData target, bool full,
            Func<ItemEntityWeapon, int, KeyValuePair<int, float>> delta, int extra)
            => EstimateWeaponAttackCore(unit, target, full, delta, full ? extra : 0);

        private static IList<AttackHandInfo> MagusProjectedAttacks(UnitEntityData unit, int extra)
        {
            // Same native enumeration as UnitAttack.CreateFullAttack, with the inspected BuffExtraAttack operation
            // applied to this read-only calculation result. No fake attack roll, buff, animation or command runs.
            var count = Rulebook.Trigger(new RuleCalculateAttacksCount(unit));
            count.AddExtraAttacks(extra, false, false);
            var result = new List<AttackHandInfo>();
            foreach (var attack in UnitAttack.EnumerateAttacks(count))
                result.Add(new AttackHandInfo(attack.Hand, attack.AttackBonusPenalty));
            var limbs = Rulebook.Trigger(new RuleCalculateAttacksCountLimbs(unit));
            foreach (var attack in UnitAttack.EnumerateLimbsAttacks(limbs))
                result.Add(new AttackHandInfo(attack.Hand, attack.AttackBonusPenalty));
            return result;
        }
    }
}
