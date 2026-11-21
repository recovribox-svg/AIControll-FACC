// ★ Fusion coverage C1 (17 November 2026; docs/FUSION_MAIN_TARGET_DOT.md, docs/FUSION_COVERAGE_PROGRAMME.md): the damage
// over time a fused cast leaves on each save outcome; ordinary spells too (docs/PERIODIC_DAMAGE.md). Native contracts (supplied assembly):
// - a buff's round ticks run in a context whose root is the fused cast (MechanicsContext.SourceAbilityContext): the fused
//   spellbook's dice cap, the cast's DC and metamagic apply (ContextActionDealDamage.GetDamageInfo, Params);
// - a tick runs outside any saving-throw scope, so a save in it is its own roll (ContextActionSavingThrow), against the
//   context's DC (UseDCFromContextSavingThrow finds no scope);
// - the first tick comes one interval after the buff is attached (Buff.OnAttach: game time, or the turn's start in
//   turn-based combat); the buff ticks before it is removed at an equal end time.
// Metadata and scalar reads only: no buff, context or rule is created or run.
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>The rounds over which damage over time is counted: the fight's expected rounds, at most three (as for
        /// damage already attached to a unit, CombatMath.AttachedDamageOf).</summary>
        internal static float PeriodicHorizon(TacticalContext ctx) =>
            Math.Min(CombatMath.AfflictionRounds, Math.Max(1f, ctx?.ExpectedRounds ?? CombatMath.AfflictionRounds));

        /// <summary>
        /// The duration in rounds of one application of a fused cast on <paramref name="recipient"/>: its expected rounds
        /// (doubled by Extend where the game extends it), else the native duration read in the cast's context (memoised for
        /// the decision). False when it cannot be read.
        /// </summary>
        private static bool TryFusionApplicationRounds(AbilityData ability, AbilityProfile.BuffApplication occurrence,
            UnitEntityData recipient, ref MechanicsContext durationContext, out float duration)
        {
            var action = occurrence.Action;
            duration = occurrence.Rounds;
            if (duration > 0f)
            {
                if (occurrence.Extendable && ability.HasMetamagic(Metamagic.Extend)) duration *= 2f;
                return true;
            }
            if (action == null || action.SameDuration) return false;
            if (!CombatMath.TryFusionDuration(ability, action, recipient, out duration))
            {
                if (durationContext == null) durationContext = CombatMath.CreateContext(ability, new TargetWrapper(recipient));
                if (durationContext == null) return false;
                duration = action.Permanent ? 100f : action.UseDurationSeconds ? Math.Max(1f, action.DurationSeconds / 6f)
                                                                             : DurationRounds(action.DurationValue, ability, durationContext);
                CombatMath.StoreFusionDuration(ability, action, recipient, duration);
            }
            return duration > 0f;
        }

        /// <summary>
        /// Expected damage over time on <paramref name="recipient"/> from the cast's periodic damage carriers, on a failed
        /// and on a passed root save, counted over <paramref name="horizonRounds"/>. A carrier on the failed branch counts on
        /// a failure, one on the passed branch on a pass, one outside both on either; without a root save the cast has one
        /// outcome, carried as "failed". The recipient's resistance and immunity apply to each tick, and a save in a tick ends
        /// the later ticks with its chance. No credit for a carrier the recipient already has (no speculative refresh), and
        /// none where the native guards or immunity exclude it. False when a guard or a duration cannot be read; with
        /// <paramref name="assumeUnknown"/> (an ally's harm) an unreadable guard counts as applying instead.
        /// ★ Ordinary spells (not fused): only a carrier applied whatever the save (outside a saving-throw branch) is priced,
        /// on both outcomes - Acid Arrow's acid, Molten Orb's and Incendiary Runes' burning cast on their own.
        /// </summary>
        internal static bool TryPeriodicDamage(AbilityData ability, AbilityProfile profile, UnitEntityData recipient,
            float horizonRounds, bool assumeUnknown, out float failed, out float passed) =>
            TryPeriodicDamage(ability, profile, recipient, horizonRounds, assumeUnknown, out failed, out passed, out _, out _);

        /// <summary>
        /// ★ Fusion coverage C6: the same, and for an ordinary spell the carriers behind a branch of its root save
        /// (<paramref name="savedFailed"/>, <paramref name="savedPassed"/>): Serenity's, Excruciating Deformation's and Acidic
        /// Spray's buffs on a failed save. They were left unpriced; the caller weights them by the save, not by the damage's.
        /// </summary>
        internal static bool TryPeriodicDamage(AbilityData ability, AbilityProfile profile, UnitEntityData recipient,
            float horizonRounds, bool assumeUnknown, out float failed, out float passed, out float savedFailed, out float savedPassed)
        {
            failed = passed = savedFailed = savedPassed = 0f;
            if (profile == null || recipient == null || !HasPeriodicCarrier(profile)) return true;
            MechanicsContext durationContext = null;
            bool fused = profile.Fusion != null;
            bool rootSave = profile.Save != SavingThrowType.Unknown;
            foreach (var occurrence in profile.BuffApplications)
            {
                if (!occurrence.PeriodicDamageOnly) continue;
                bool onFailure, onPass, saved = false;
                if (fused)
                {
                    var path = profile.FusionBuffPath(occurrence.Action);
                    if (!rootSave && path != AbilityProfile.FusionSavePath.Both) continue;
                    onFailure = path != AbilityProfile.FusionSavePath.Passed;
                    onPass = rootSave && path != AbilityProfile.FusionSavePath.Failed;
                }
                else if (occurrence.UnderSave)
                {
                    // ★ C6: on a branch of the root save now; a carrier on either branch, or under a nested save, stays unpriced.
                    var branch = rootSave ? profile.OrdinaryRootPath(occurrence.Action) : AbilityProfile.FusionSavePath.Both;
                    if (branch == AbilityProfile.FusionSavePath.Both || profile.TryNestedSave(occurrence.Action, out _, out _)) continue;
                    onFailure = branch == AbilityProfile.FusionSavePath.Failed;
                    onPass = !onFailure;
                    saved = true;
                }
                else onFailure = onPass = true;
                var buff = occurrence.Buff;
                if (recipient.Buffs.HasFact(buff)) continue;
                var applies = occurrence.Applies(ability, recipient);
                if (applies == EffectApplies.No || CombatMath.BuffImmune(recipient, buff, ability)) continue;
                if (applies == EffectApplies.Unknown && !assumeUnknown) return false;
                if (!TryFusionApplicationRounds(ability, occurrence, recipient, ref durationContext, out float rounds))
                {
                    if (!assumeUnknown) return false;
                    rounds = horizonRounds;   // an ally's harm: the whole horizon when the duration is unread
                }
                float harm = PeriodicCarrierDamage(ability, buff, recipient, rounds, horizonRounds);
                if (harm <= 0f) continue;
                if (saved)
                {
                    if (onFailure) savedFailed += harm;
                    if (onPass) savedPassed += harm;
                    continue;
                }
                if (onFailure) failed += harm;
                if (onPass) passed += harm;
            }
            return true;
        }

        /// <summary>One carrier's expected damage over the horizon: its ticks (first one interval after it lands), each
        /// weighted by the chance the earlier ticks' saves left it on.</summary>
        private static float PeriodicCarrierDamage(AbilityData ability, Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff buff,
            UnitEntityData recipient, float durationRounds, float horizonRounds)
        {
            var plan = CombatMath.PeriodicPlanOf(buff);
            // ★ Fusion coverage C6: a damage carrier the round-tick reader does not take (CombatMath.Carriers.cs).
            if (plan?.Round == null || !plan.Supported) return CarrierDamage(ability, buff, recipient, durationRounds, horizonRounds);
            double interval = buff.TickTime.TotalSeconds;
            int ticks = ValueModel.PeriodicTicks(interval, interval, durationRounds * ValueModel.RealTimeRoundSeconds,
                horizonRounds * ValueModel.RealTimeRoundSeconds);
            if (ticks <= 0) return 0f;
            int baseDC = CombatMath.ParamsOf(ability)?.DC ?? 10;
            bool maximize = false, empower = false;
            try
            {
                maximize = ability.HasMetamagic(Metamagic.Maximize);
                empower = ability.HasMetamagic(Metamagic.Empower);
            }
            catch { }
            var tick = CombatMath.ReadPeriodicTick(plan.Round,
                save => CombatMath.SaveFailChance(ability, save.Type, recipient, save.HasCustomDC ? save.CustomDC.Value - baseDC : 0),
                deal =>
                {
                    var harm = new CombatMath.Affliction();
                    // ★ coverage4 (C6a): a ranked number from the buff's own caster-level config (CombatMath.PeriodicRank).
                    float mean = ValueModel.PeriodicDiceMean(
                        CombatMath.FusedDiceCount(ability, CombatMath.PeriodicRank(ability, buff, recipient, deal.Value.DiceCountValue)),
                        deal.Value.DiceType.Sides(), CombatMath.PeriodicRank(ability, buff, recipient, deal.Value.BonusValue), maximize, empower);
                    harm.HitPoints = CombatMath.AfterTargetReduction(recipient, deal, mean, ability);
                    return harm;
                },
                apply => new CombatMath.Affliction());
            return Math.Max(0f, tick.Harm.HitPoints) * ValueModel.PeriodicWeight(ticks, tick.Survives);
        }

        /// <summary>
        /// ★ Fusion coverage C6: a damage carrier's expected damage over the horizon (CombatMath.CarrierPlan): its damage when it
        /// lands, its round ticks (first one interval after it lands, each weighted by the chance the earlier ones left it on), its
        /// damage when it ends (when that is within the horizon: its duration runs out there, or a tick's save removes it - a
        /// removal deactivates it too), and the damage its bearer takes when it attacks, once a round, by the chance it attacks.
        /// </summary>
        private static float CarrierDamage(AbilityData ability, Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff buff,
            UnitEntityData recipient, float durationRounds, float horizonRounds)
        {
            var plan = CombatMath.CarrierPlanOf(buff);
            if (plan.Kind != CombatMath.CarrierKind.Damage) return 0f;
            float total = 0f, survives = 1f;
            int ticks = 0;
            if (plan.Activated != null) total += Math.Max(0f, CombatMath.CarrierListHarm(ability, buff, recipient, plan.Activated).Harm.HitPoints);
            if (plan.Round != null)
            {
                double interval = buff.TickTime.TotalSeconds;
                if (interval > 0d && interval < double.MaxValue)
                    ticks = ValueModel.PeriodicTicks(interval, interval, durationRounds * ValueModel.RealTimeRoundSeconds,
                        horizonRounds * ValueModel.RealTimeRoundSeconds);
                var tick = CombatMath.CarrierListHarm(ability, buff, recipient, plan.Round);
                survives = tick.Survives;
                total += Math.Max(0f, tick.Harm.HitPoints) * ValueModel.PeriodicWeight(ticks, survives);
            }
            if (plan.Deactivated != null)
            {
                float end = ValueModel.CarrierEndChance(ticks, survives, durationRounds <= horizonRounds);
                total += end * Math.Max(0f, CombatMath.CarrierListHarm(ability, buff, recipient, plan.Deactivated).Harm.HitPoints);
            }
            if (plan.OnAttack != null)
            {
                float rounds = Math.Min(durationRounds, horizonRounds);
                float attacks = recipient.IsPlayerFaction ? 1f : EnemyThreat.AttackChance(recipient);
                total += rounds * attacks * Math.Max(0f, CombatMath.CarrierListHarm(ability, buff, recipient, plan.OnAttack).Harm.HitPoints);
            }
            return total;
        }

        internal static bool HasPeriodicCarrier(AbilityProfile profile)
        {
            var applications = profile.BuffApplications;
            for (int i = 0; i < applications.Count; i++)
                if (applications[i].PeriodicDamageOnly) return true;
            return false;
        }

        /// <summary>
        /// An ally's expected damage over time from the cast, both outcomes weighted by its chance to fail the root save.
        /// For the friendly-fire cost and the burst re-check; an unreadable guard or duration counts as applying.
        /// </summary>
        /// <summary>
        /// ★ Fusion coverage C6: an ordinary spell's damage over time on an enemy, for its damage section: the carriers applied
        /// whatever the save by <paramref name="damageChance"/> (as before), those behind a branch of its root save by the
        /// chance it is delivered and that branch is the outcome. A fused cast's goes through its joint value instead.
        /// </summary>
        internal static float OrdinaryPeriodicLater(UnitEntityData caster, AbilityData ability, AbilityProfile profile, UnitEntityData enemy,
                                                    float horizonRounds, float damageChance)
        {
            if (profile == null || enemy == null || !HasPeriodicCarrier(profile)) return 0f;
            try
            {
                if (profile.Fusion != null) return damageChance * PeriodicExpected(ability, profile, enemy, horizonRounds, false);
                if (!TryPeriodicDamage(ability, profile, enemy, horizonRounds, false, out float failed, out _, out float savedFailed, out float savedPassed))
                    return 0f;
                float later = damageChance * failed;
                if (savedFailed > 0f || savedPassed > 0f)
                {
                    float fail = profile.Save != SavingThrowType.Unknown ? CombatMath.SaveFailChance(ability, profile.Save, enemy) : 1f;
                    later += CombatMath.DeliveryChance(caster, enemy, ability, profile) * (fail * savedFailed + (1f - fail) * savedPassed);
                }
                return later;
            }
            catch { return 0f; }
        }

        internal static float PeriodicAllyDamage(AbilityData ability, AbilityProfile profile, UnitEntityData ally, float horizonRounds) =>
            PeriodicExpected(ability, profile, ally, horizonRounds, true);

        /// <summary>
        /// The cast's expected damage over time on <paramref name="unit"/>: a fused cast's outcomes weighted by the chance to
        /// fail its root save; an ordinary spell's, applied whatever the save. <paramref name="assumeUnknown"/>: an
        /// unreadable guard or duration counts as applying (an ally's harm), else it gives nothing.
        /// </summary>
        internal static float PeriodicExpected(AbilityData ability, AbilityProfile profile, UnitEntityData unit, float horizonRounds,
                                               bool assumeUnknown)
        {
            if (profile == null || unit == null || !HasPeriodicCarrier(profile)) return 0f;
            try
            {
                if (!TryPeriodicDamage(ability, profile, unit, horizonRounds, assumeUnknown, out float failed, out float passed,
                        out float savedFailed, out float savedPassed)) return 0f;
                if (failed <= 0f && passed <= 0f && savedFailed <= 0f && savedPassed <= 0f) return 0f;
                float fail = profile.Fusion != null && profile.Save != SavingThrowType.Unknown ? CombatMath.SaveFailChance(ability, profile.Save, unit) : 1f;
                float saveFail = profile.Save != SavingThrowType.Unknown ? CombatMath.SaveFailChance(ability, profile.Save, unit) : 1f;
                return fail * failed + (1f - fail) * passed + saveFail * savedFailed + (1f - saveFail) * savedPassed;
            }
            catch { return 0f; }
        }

        /// <summary>
        /// Every buff the cast applies to its targets is a periodic damage carrier priced as damage (TryPeriodicDamage): the
        /// control section has nothing left to value, and must not guess a debuff for it.
        /// </summary>
        internal static bool CarriersOnly(AbilityProfile profile)
        {
            if (profile == null || profile.Pit != null || profile.TargetBuffs.Count == 0) return false;
            bool fused = profile.Fusion != null;
            foreach (var buff in profile.TargetBuffs)
            {
                bool seen = false;
                foreach (var a in profile.BuffApplications)
                {
                    if (a.Buff != buff) continue;
                    if (!a.PeriodicDamageOnly || !fused && a.UnderSave &&
                        (profile.OrdinaryRootPath(a.Action) == AbilityProfile.FusionSavePath.Both || profile.TryNestedSave(a.Action, out _, out _)))
                        return false;
                    seen = true;
                }
                if (!seen) return false;
            }
            return true;
        }
    }
}
