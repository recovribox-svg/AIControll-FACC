// ★ v0.3.50 review2: Read the native landing helper and quote the actual current weapons; never activate a buff or move an actor.
using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker.Items;
using Kingmaker.Blueprints.Root;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        private static readonly Func<TargetWrapper, Vector3, UnitEntityData, Vector3> GriffonEndpoint = BindGriffonEndpoint();
        private static BlueprintBuff s_GriffonChargeRoot;
        private static AbilityProfile.GriffonChargeProfile s_GriffonCharge;

        private static Func<TargetWrapper, Vector3, UnitEntityData, Vector3> BindGriffonEndpoint()
        {
            try
            {
                var method = typeof(AbilityGriffinAttack).GetMethod("GetEndPoint", BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(TargetWrapper), typeof(Vector3), typeof(UnitEntityData) }, null);
                return method == null ? null : (Func<TargetWrapper, Vector3, UnitEntityData, Vector3>)
                    Delegate.CreateDelegate(typeof(Func<TargetWrapper, Vector3, UnitEntityData, Vector3>), method);
            }
            catch { return null; }
        }

        internal static bool TryGriffonLanding(UnitEntityData unit, UnitEntityData target, out Vector3 point)
        {
            point = default;
            if (GriffonEndpoint == null || unit?.View == null || target?.View == null) return false;
            try
            {
                point = GriffonEndpoint(new TargetWrapper(target), unit.Position, unit);
                return GriffonFinite(point);
            }
            catch { return false; }
        }

        private static bool GriffonFinite(Vector3 point) =>
            !float.IsNaN(point.x) && !float.IsInfinity(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
            !float.IsNaN(point.z) && !float.IsInfinity(point.z);

        internal static bool GriffonLandingReaches(UnitEntityData unit, UnitEntityData target, Vector3 point, float reach)
        {
            if (unit?.View == null || target?.View == null || !(reach > 0f)) return false;
            return UnitCommand.IsUnitCloseEnough(target.Position, point, point + unit.EyePosition - unit.Position, reach, true, 0f);
        }

        internal static bool TryGriffonCharge(AbilityData ability, out AbilityProfile.GriffonChargeEffect effect)
        {
            effect = null;
            var unit = ability?.Caster?.Unit;
            if (unit == null || unit.State.IsInStealth || unit.State.IsCharging || unit.RiderPart != null || unit.SaddledPart != null) return false;
            var root = BlueprintRoot.Instance?.SystemMechanics?.ChargeBuff;
            if (root == null) return false;
            if (root != s_GriffonChargeRoot)
            {
                s_GriffonChargeRoot = root;
                s_GriffonCharge = AbilityProfile.ReadGriffonCharge(root);
            }
            var charge = s_GriffonCharge;
            if (charge == null || unit.Buffs.HasFact(root) || unit.Buffs.HasFact(charge.Yes.Buff) || unit.Buffs.HasFact(charge.No.Buff)) return false;
            effect = charge.Select(fact => unit.Descriptor.HasFact(fact));
            if (BuffImmune(unit, root, ability) || BuffImmune(unit, effect.Buff, ability)) { effect = null; return false; }
            return true;
        }

        // ★ v0.3.51: the native post-flight attack's restrictions differ from ordinary full-round affordability.
        internal static bool GriffonCanPounce(UnitEntityData unit)
        {
            if (unit?.CombatState == null || !unit.State.Features.Pounce) return false;
            bool turnBased = !CombatMemory.IsRealTime;
            // The ability's own full-round/standard availability is checked separately. Read the native attack's
            // restrictions here: ordinary HasFullRoundAction/UsedOneMoveAction do not describe its ignored cooldown.
            return GriffonAttackRules.FullAttack(true, turnBased, turnBased && unit.IsSurprising(),
                unit.State.HasCondition(UnitCondition.Staggered), turnBased && unit.IsMoveActionRestricted(),
                !turnBased && unit.CombatState.IsFullAttackRestrictedBecauseOfMoveAction);
        }

        internal static WeaponAttackEstimate EstimateGriffonAttack(UnitEntityData unit, UnitEntityData target, bool full,
                                                                    AbilityProfile.GriffonChargeEffect charge)
        {
            if (unit == null || target == null || charge == null) return null;
            // The custom delivery queues an ordinary IsCharge attack. Its current hands/limbs and native Pounce
            // decide the routine. The native charge buff lasts one round, starting before the flight; its positive
            // roll bonus is deliberately uncredited because a long full attack can outlast the remaining duration.
            // No guessed animation schedule, charge proc, new form, target relocation or attack event is executed.
            var result = EstimateWeaponAttack(unit, target, full);
            return result?.IsRanged == false ? result : null;
        }

        // ★ v0.3.52: mode alternatives reuse the exact already-priced routine. A stale/evicted quote is unavailable,
        // never an excuse for another native attack query outside the leap's four-quote budget.
        internal static WeaponAttackEstimate RepriceGriffonModes(UnitEntityData unit, UnitEntityData target, bool full,
            WeaponAttackEstimate current, Func<ItemEntityWeapon, int, KeyValuePair<int, float>> delta)
        {
            if (unit == null || target == null || current == null || current.Attacker != unit || current.IsRanged) return null;
            string key = $"{unit.UniqueId}|{target.UniqueId}|{full}";
            if (!Routines.TryGetValue(key, out var routine) || routine.Frame != Time.frameCount ||
                !ReferenceEquals(current.RoutineIdentity, routine)) return null;
            return GriffonModeQuote(routine, current, delta);
        }

        private static WeaponAttackEstimate GriffonModeQuote(AttackRoutine routine, WeaponAttackEstimate current,
            Func<ItemEntityWeapon, int, KeyValuePair<int, float>> delta)
        {
            if (routine == null || current == null || routine.Lines.Count == 0) return null;
            // Only DamageValue's fields are needed: a leap cannot carry a held spell/Magus payload. Preserve raw
            // native per-weapon lines, criticals, mitigation, separate blade damage and concealment from the quote.
            var result = new WeaponAttackEstimate { Attacker = current.Attacker, Attacks = routine.Attacks,
                Weapon = current.Weapon, Reach = current.Reach, EndsRegeneration = current.EndsRegeneration };
            float noHit = 1f;
            foreach (var line in routine.Lines)
            {
                var change = delta?.Invoke(line.Weapon, line.Index) ?? new KeyValuePair<int, float>(0, 0f);
                result.ExpectedDamage += ExpectedWeaponLine(line, change.Key, change.Value, current.RoutineLands, out float hit, out _);
                if (line.Index == 0) result.FirstHitChance = hit;
                noHit *= 1f - hit;
            }
            result.AnyHitChance = 1f - noHit;
            return result;
        }
    }
}
