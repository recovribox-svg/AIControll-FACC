using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.Utility;
using Kingmaker.UnitLogic.Abilities;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static float SummonDebuffWorth(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
                                              UnitEntityData enemy, float rounds)
        {
            float threatened = EnemyThreat.WeaponThreatOnParty(enemy, out float rollPass);
            if (profile.WorstOfTwoAttacks != null)
                return threatened * ValueModel.WorstOfTwoLostShare(rollPass) * rounds;
            if (!(rounds > 1f) || !(threatened > 0f) || enemy.State.HasConditionImmunity(UnitCondition.AttackNearest) ||
                CombatMath.BuffImmune(enemy, profile.RedirectControl, ability))
                return 0f;

            // The native confusion controller considers the recipient's memory and its own group. Use those
            // collections only, abandon a crowded or unsettled forecast, and credit only the first possible tick.
            return ValueModel.RedirectedAttackWorth(profile.RedirectChance, threatened, CanRedirectToEnemy(ctx.Unit, enemy));
        }

        private static bool CanRedirectToEnemy(UnitEntityData caster, UnitEntityData enemy)
        {
            if (enemy.View?.MovementAgent?.IsReallyMoving != false) return false;
            var memory = enemy.Memory?.UnitsList;
            var group = enemy.Group;
            const int budget = 24;
            if (memory == null || group == null || memory.Count > budget) return false;
            UnitEntityData nearest = null;
            float closest = float.PositiveInfinity, rival = float.PositiveInfinity;

            void Consider(UnitEntityData other)
            {
                if (other == null || other == enemy || other == nearest || other.State.IsDead) return;
                float distance = enemy.SqrDistanceTo(other);
                if (distance < closest)
                {
                    rival = closest;
                    closest = distance;
                    nearest = other;
                }
                else if (distance < rival) rival = distance;
            }

            foreach (var info in memory) Consider(info.Unit);
            int remaining = budget - memory.Count;
            foreach (var member in group)
            {
                if (remaining-- <= 0) return false;
                Consider(member);
            }

            return nearest != null && caster.IsEnemy(nearest) && !CombatMath.IsDisabled(nearest) &&
                nearest.View?.MovementAgent?.IsReallyMoving == false &&
                Mathf.Sqrt(rival) - Mathf.Sqrt(closest) > nearest.Corpulence + enemy.Corpulence &&
                Mathf.Sqrt(closest) <= enemy.Descriptor.GetWeaponRange(enemy.Body.PrimaryHand.MaybeWeapon?.Blueprint).Meters + enemy.Corpulence + nearest.Corpulence;
        }
    }
}
