// ★ Fusion coverage C6 (docs/FUSION_COVERAGE_C6_C8.md): pricing the carrier buffs CombatMath.Carriers.cs reads, beyond damage
// (which joins the periodic damage pricing): the temporary hit points a spell gives its bearer (Enervation on an undead target),
// and a control a round tick puts on (Phantasmal Putrefaction's nausea). Both modes, ordinary and fused casts.
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>
        /// The temporary hit points the cast gives <paramref name="recipient"/> through a Benefit carrier, on a failed and on a
        /// passed root save (a fused cast by its paths; an ordinary spell by its root branch, or on either outcome when the
        /// application is outside a save). Temporary hit points absorb damage first (CombatMath.HpBeforeDown), so they count as
        /// damage the recipient will not take. Nothing where the native guards or immunity exclude it, or it already has it.
        /// </summary>
        internal static void CarrierBenefit(AbilityData ability, AbilityProfile profile, UnitEntityData recipient, out float failed, out float passed)
        {
            failed = passed = 0f;
            if (profile == null || recipient == null) return;
            bool fused = profile.Fusion != null, rootSave = profile.Save != SavingThrowType.Unknown;
            foreach (var occurrence in profile.BuffApplications)
            {
                var plan = CombatMath.CarrierPlanOf(occurrence.Buff);
                if (plan.Kind != CombatMath.CarrierKind.Benefit || recipient.Buffs.HasFact(occurrence.Buff)) continue;
                if (occurrence.Applies(ability, recipient) == EffectApplies.No || CombatMath.BuffImmune(recipient, occurrence.Buff, ability)) continue;
                var path = fused ? profile.FusionBuffPath(occurrence.Action) : rootSave ? profile.OrdinaryRootPath(occurrence.Action) : AbilityProfile.FusionSavePath.Both;
                float amount = CombatMath.CarrierTempHp(ability, plan);
                if (path != AbilityProfile.FusionSavePath.Passed) failed += amount;
                if (path != AbilityProfile.FusionSavePath.Failed) passed += amount;
            }
        }

        /// <summary>The same, weighted by the recipient's chance to fail the root save.</summary>
        internal static float CarrierBenefitExpected(AbilityData ability, AbilityProfile profile, UnitEntityData recipient)
        {
            CarrierBenefit(ability, profile, recipient, out float failed, out float passed);
            if (failed <= 0f && passed <= 0f) return 0f;
            float fail = profile.Save != SavingThrowType.Unknown ? CombatMath.SaveFailChance(ability, profile.Save, recipient) : 1f;
            return fail * failed + (1f - fail) * passed;
        }

        /// <summary>
        /// A round-tick control carrier (CombatMath.CarrierKind.PeriodicControl) on <paramref name="recipient"/>: the share of a
        /// round its control takes, and the rounds it is on within the horizon (ValueModel.TickControlRounds: the first tick one
        /// interval after it lands, its two saves at the cast's DC). False when it is no such carrier.
        /// </summary>
        internal static bool TickControl(AbilityData ability, BlueprintBuff buff, UnitEntityData recipient, float durationRounds,
                                         float horizonRounds, out float share, out float rounds)
        {
            share = rounds = 0f;
            var plan = CombatMath.CarrierPlanOf(buff);
            if (plan.Kind != CombatMath.CarrierKind.PeriodicControl || plan.Control == null) return false;
            if (recipient.Buffs.HasFact(plan.Control) || CombatMath.BuffImmune(recipient, plan.Control, ability)) return true;
            var strength = AbilityProfile.BuffControl(plan.Control);
            share = ControlShare(strength, false, false);
            double interval = buff.TickTime.TotalSeconds;
            if (!(interval > 0d) || interval >= double.MaxValue) { share = 0f; return true; }
            int ticks = ValueModel.PeriodicTicks(interval, interval, durationRounds * ValueModel.RealTimeRoundSeconds,
                                                 horizonRounds * ValueModel.RealTimeRoundSeconds);
            int baseDC = CombatMath.ParamsOf(ability)?.DC ?? 10;
            float first = CombatMath.SaveFailChance(ability, plan.ControlFirstSave.Type, recipient,
                plan.ControlFirstSave.HasCustomDC ? plan.ControlFirstSave.CustomDC.Value - baseDC : 0);
            float second = CombatMath.SaveFailChance(ability, plan.ControlSecondSave.Type, recipient,
                plan.ControlSecondSave.HasCustomDC ? plan.ControlSecondSave.CustomDC.Value - baseDC : 0);
            rounds = ValueModel.TickControlRounds(first, second, ticks);
            return true;
        }
    }
}
