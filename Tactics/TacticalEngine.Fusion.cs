// F2/F3: existing supported Fusion payloads, occurrence-local control and shared-save outcome utility.
using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static void FusionValueFallback(AbilityData ability, AbilityProfile profile)
        {
            if (profile.Fusion == null || profile.FusionValueFallbackReported) return;
            profile.FusionValueFallbackReported = true;
            Main.Log($"[Fusion] {ability.Name}: joint outcome value not qualified for this recipient; " +
                (profile.FusionOutcomeEffectLocation != null || profile.FusionSharedHd ? "its control is not credited " : "existing forecast retained ") +
                $"({profile.FusionOutcomeMetadataReason ?? (ability.ShadowSpellSettings != null ? "shadow copy" : "recipient guard/duration/outcome or damage-wake/removal dependency")})");
        }

        internal const string FusionOutcomeGate = "effect on a passed or either save outcome; joint value not qualified";
        internal const string FusionSharedHdGate = "shared Hit Dice selection";

        /// <summary>
        /// An ordinary Hit Dice pool keeps its native current-position selection. A fused cast also carries an
        /// independent payload: its existing walking-in ally safety must remain, without adding those allies to the
        /// pool forecast or changing its native order. The caller is already in the ordinary area branch.
        /// </summary>
        internal static bool ForecastWalkingAreaAllies(AbilityProfile profile, bool supportedPool) =>
            profile != null && profile.AoETargets != Kingmaker.UnitLogic.Abilities.Components.TargetType.Enemy &&
            (!supportedPool || profile.Fusion != null);

        /// <summary>
        /// ★ Fusion F4: the one admission rule, shared by EvaluateAbility and the coverage audit. Null when the fused
        /// spell is offered. A fusion with an effect outside the failed-save branch needs the joint outcome value: the
        /// older forecast multiplies every control by the fail chance and cannot represent it.
        /// </summary>
        internal static string FusionWithheld(AbilityData ability, AbilityProfile profile)
        {
            if (profile?.Fusion == null) return null;
            if (profile.FusionUnsupported != null) return profile.FusionUnsupported;
            // ★ Fusion F7: a shared Hit Dice pool is offered only when the pool forecast reads the fused cast's native
            // pool and delivery, and the joint outcome value prices the result.
            if (profile.FusionSharedHd &&
                (SharedHdControl.FusedRefusal(ability, profile) != null || !FusionOutcomeModel(ability, profile))) return FusionSharedHdGate;
            if (profile.FusionOutcomeEffectLocation != null && !FusionOutcomeModel(ability, profile)) return FusionOutcomeGate;
            return null;
        }

        /// <summary>Why a gate withheld the fused spell: the pool reader's reason, else the joint value's.</summary>
        internal static string FusionWithheldDetail(AbilityData ability, AbilityProfile profile)
        {
            if (profile?.Fusion == null || profile.FusionUnsupported != null) return null;
            string pool = profile.FusionSharedHd ? SharedHdControl.FusedRefusal(ability, profile) : null;
            if (pool != null) return pool;
            if (!FusionOutcomeModel(ability, profile))
                return profile.FusionOutcomeMetadataReason ?? (ability.ShadowSpellSettings != null ? "shadow copy" : "unknown");
            return null;
        }

        private static bool FusionOutcomeModel(AbilityData ability, AbilityProfile profile)
        {
            if (profile.Fusion == null || ability.ShadowSpellSettings != null) return false;
            if (profile.FusionOutcomeMetadataQualified.HasValue) return profile.FusionOutcomeMetadataQualified.Value;
            string reason = null;
            if (profile.Pit != null) reason = "pit selection";
            else if (!profile.FusionBuffPathsConsistent) reason = "shared apply action on different save branches";
            else if (!profile.FusionIndependentGuards) reason = profile.FusionGuardRefusal ?? "unread guard";
            else if (profile.Intimidation != null || profile.MarksForOwnAttacks || profile.Maneuvers.Count > 0) reason = "mark/skill/maneuver dependency";
            else if (profile.IsWeaponAttack || profile.MeleeAttack != null) reason = "weapon/queued melee dependency";
            else if (profile.Enchants || profile.Extensions.Count > 0) reason = "dispel/enchantment/extension dependency";
            // ★ Fusion coverage C7: a dispel is priced by what it takes off every unit it reaches (TryDispelValue); it joins
            // the joint value when that reading is exact and it cannot take off a buff the same cast applies.
            else if (profile.Dispels && FusionDispelRefusal(profile) is string dispelRefusal) reason = "dispel/enchantment/extension dependency: " + dispelRefusal;
            else if (profile.RemovedDescriptors != SpellDescriptor.None ||
                profile.RemovesBuffs && profile.RemovedBuffs.Count == 0) reason = "dynamic removal dependency";
            else if (!profile.DealsDamage && profile.BuffApplications.Count == 0) reason = "no direct HP/control payload";
            // ★ Fusion coverage C5: ability damage and negative levels are read by their HP equivalent (CombatMath.DrainEquivalent).
            else if (!profile.Damage.All(a => a is ContextActionDealDamage)) reason = "composite damage (a provider or a kill)";
            else if (profile.BuffApplications.Count > ValueModel.ControlEnvelope.Capacity) reason = "control occurrence bound";
            else if (!profile.TargetBuffs.All(b => profile.BuffApplications.Any(a => a.Buff == b))) reason = "control without a direct apply occurrence";
            else if (profile.TargetBuffs.Any(b => (!AbilityProfile.IsHarmfulBuff(b) &&
                         !profile.BuffApplications.Any(a => a.Buff == b && a.PeriodicDamageOnly)) || OnlyRevealUtility(b) ||
                         b.GetComponent<Kingmaker.UnitLogic.FactLogic.ChangeFaction>() != null ||
                         b.GetComponent<Kingmaker.UnitLogic.FactLogic.AddFacts>() != null ||
                         PolymorphFacts.Form(b) != null ||
                         b.GetComponent<Kingmaker.Designers.Mechanics.Buffs.BuffPoisonStatDamage>() != null)) reason = "support/reveal/fact-grant/form/faction/poison carrier";
            profile.FusionOutcomeMetadataReason = reason;
            profile.FusionOutcomeMetadataQualified = reason == null;
            return reason == null;
        }

        /// <summary>
        /// ★ Fusion coverage C7: why a fused cast's dispel cannot join the joint outcome value, or null. Native contract
        /// (ContextActionDispelMagic, RuleDispelMagic): a dispel tries the recipient's buffs by caster level, highest first, up
        /// to its count; with OnlyTargetEnemyBuffs it skips a buff whose caster is not the dispeller's enemy, so it never takes
        /// what the same cast put on an enemy. Without it, a dispel that runs after one of the cast's own buff applications
        /// (Spell 1's actions, then Spell 2's) could take that buff off again, and the joint value would credit a control the
        /// cast removes. Each dispel must also be read by the dispel order (DispelUnsupported, or one with no check).
        /// </summary>
        internal static string FusionDispelRefusal(AbilityProfile profile)
        {
            if (profile?.Fusion == null) return "not a fusion";
            foreach (var dispel in profile.DispelActions)
            {
                if (DispelUnsupported(dispel) is string unread && !IsAutomaticDispel(dispel)) return unread;
                if (dispel.OnlyTargetEnemyBuffs) continue;
                if (!profile.FusionDispelOrdinals.TryGetValue(dispel, out int at)) return "dispel outside the fused payloads";
                foreach (var application in profile.BuffApplications)
                    if (!application.Action.ToCaster && application.ActionOrdinal < at) return "dispel after the cast's own buff";
            }
            return null;
        }

        /// <summary>
        /// Control round-equivalents on a failed and on a passed root save. ★ Fusion F4: an application on the passed
        /// branch enters the passed envelope, one outside any saved branch enters both. Without a root save the cast
        /// has one outcome, carried as "failed" with a fail chance of 1.
        /// </summary>
        private static bool TryFusionControlRounds(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
            UnitEntityData recipient, bool friendly, out float rounds, out float passRounds)
        {
            rounds = passRounds = 0f;
            if (profile.RemovedBuffs.Any(recipient.Buffs.HasFact)) return false;
            if (profile.BuffApplications.Count == 0) return true;
            var envelope = ctx.FusionControlScratch ?? (ctx.FusionControlScratch = new ValueModel.ControlEnvelope());
            envelope.Clear();
            ValueModel.ControlEnvelope passEnvelope = null;
            // ★ Fusion F7: the one application a shared Hit Dice pool gates (Sleep). It joins the envelopes with the
            // forecast chance that the pool still covers this recipient (SharedHdControl), blended after the loop.
            var pool = profile.FusionSharedHd ? ctx.FusionSharedHd : null;
            bool pooledSeen = false, pooledOnFailure = false, pooledOnPass = false;
            float pooledChance = 0f, pooledShare = 0f, pooledHorizon = 0f;
            MechanicsContext durationContext = null;
            var single = ctx.FusionBuffScratch ?? (ctx.FusionBuffScratch = new List<BlueprintBuff>(1));
            float repeat = CombatMemory.TimesUsed(ctx.Unit, ability) >= 2 ? 0.3f : 1f;
            foreach (var occurrence in profile.BuffApplications)
            {
                var savePath = profile.FusionBuffPath(occurrence.Action);
                bool rootSave = profile.Save != SavingThrowType.Unknown;
                if (!rootSave && savePath != AbilityProfile.FusionSavePath.Both) continue;
                bool onFailure = savePath != AbilityProfile.FusionSavePath.Passed;
                // Only a profile whose graph was seen to apply something outside the failed branch has a passed outcome
                // to price; every other fusion keeps exactly its earlier single envelope.
                bool onPass = rootSave && profile.FusionOutcomeEffectLocation != null &&
                              savePath != AbilityProfile.FusionSavePath.Failed;
                var buff = occurrence.Buff;
                if (occurrence.PeriodicDamageOnly) continue; // omitted DOT benefit must not become generic lost turns
                if (recipient.Buffs.HasFact(buff)) continue; // no new stacking or speculative refresh credit
                bool pooled = profile.FusionSharedHd && profile.SharedHdBuffs.Contains(buff);
                float reach = 1f;
                if (pooled)
                {
                    // The pool spell's exclusive branches apply one buff with one duration (SharedHdControl's plan):
                    // the first stands for the application, and the forecast has read all of its native guards.
                    if (pooledSeen) continue;
                    pooledSeen = true;
                    if (pool == null || !pool.Supported)
                    {
                        if (friendly) return false; // an unread pool cannot establish an ally's safety
                        continue;
                    }
                    reach = pool.Eligibility(recipient, buff);
                    if (reach <= 0f || CombatMath.BuffImmune(recipient, buff, ability)) continue;
                }
                else
                {
                    var applies = occurrence.Applies(ability, recipient);
                    if (applies == EffectApplies.No || CombatMath.BuffImmune(recipient, buff, ability)) continue;
                    if (applies == EffectApplies.Unknown) return false;
                }
                // A low-HD sleep branch may wake from a companion payload; an inapplicable sleep branch must not
                // suppress the independent high-HD stun branch of Color Spray.
                bool sleeping = occurrence.Conditions?.Contains(Kingmaker.UnitLogic.UnitCondition.Sleeping) == true;
                // ★ Fusion F7: a pooled sleep that the game applies after all of the cast's own damage is not woken by it.
                if (sleeping && profile.DealsDamage && !(pooled && profile.FusionDamageBefore(occurrence))) return false;
                var strength = AbilityProfile.BuffControlOn(occurrence, recipient, out bool immuneConditions);
                float share = immuneConditions ? 0f : ControlShare(strength, false, false);
                if (strength <= ControlStrength.Minor)
                {
                    single.Clear(); single.Add(buff);
                    float byParts = DebuffShareOf(ctx, ability, recipient, single, out bool partial);
                    if (byParts >= 0f) share = partial ? Math.Max(share, byParts) : byParts;
                    share *= repeat;
                }
                if (friendly && immuneConditions && share <= 0f && BuffPartsReader.Read(buff).Unread.Count > 0) return false;
                // ★ Fusion coverage C9: an application behind a nested save lands with that save's outcome (its own fail chance).
                share *= CombatMath.NestedSaveFactor(ability, profile, occurrence.Action, recipient);
                if (share <= 0f) continue;
                if (!TryFusionApplicationRounds(ability, occurrence, recipient, ref durationContext, out float duration)) return false;
                float horizon = Math.Min(Math.Min(ctx.ExpectedRounds, 3f), duration);
                if (sleeping && s_ActiveEnemies != null && s_ActiveEnemies.All(e => e == recipient) && ctx.Allies.Any(a => a != ctx.Unit))
                    horizon = Math.Min(horizon, 1f);
                if (pooled)
                {
                    // A periodic damage carrier from the same cast (burning) wakes the sleeper at its next tick.
                    if (sleeping && profile.BuffApplications.Any(a => a.PeriodicDamageOnly)) horizon = Math.Min(horizon, 1f);
                    pooledChance = reach; pooledShare = share; pooledHorizon = horizon;
                    pooledOnFailure = onFailure; pooledOnPass = onPass;
                    continue;
                }
                if (onFailure && !envelope.Add(share, horizon)) return false;
                if (onPass)
                {
                    if (passEnvelope == null)
                    {
                        passEnvelope = ctx.FusionControlPassScratch ?? (ctx.FusionControlPassScratch = new ValueModel.ControlEnvelope());
                        passEnvelope.Clear();
                    }
                    if (!passEnvelope.Add(share, horizon)) return false;
                }
            }
            rounds = envelope.Rounds();
            if (passEnvelope != null) passRounds = passEnvelope.Rounds();
            if (pooledChance > 0f && pooledShare > 0f)
            {
                // Expected envelope: with the forecast chance the pooled control overlaps the others, else it is absent.
                if (pooledOnFailure)
                {
                    if (!envelope.Add(pooledShare, pooledHorizon)) return false;
                    rounds = ValueModel.PooledRounds(rounds, envelope.Rounds(), pooledChance);
                }
                if (pooledOnPass)
                {
                    if (passEnvelope == null)
                    {
                        passEnvelope = ctx.FusionControlPassScratch ?? (ctx.FusionControlPassScratch = new ValueModel.ControlEnvelope());
                        passEnvelope.Clear();
                    }
                    if (!passEnvelope.Add(pooledShare, pooledHorizon)) return false;
                    passRounds = ValueModel.PooledRounds(passRounds, passEnvelope.Rounds(), pooledChance);
                }
            }
            if (!friendly && CombatMath.IsDisabled(recipient)) { rounds *= 0.4f; passRounds *= 0.4f; }
            return true;
        }

        // ★ Fusion F7: diagnostic only (first qualified recipient per configuration).
        private static float FusionPoolChance(TacticalContext ctx, AbilityProfile profile, UnitEntityData recipient)
        {
            var pool = ctx.FusionSharedHd;
            if (pool == null || !pool.Supported) return 0f;
            float chance = 0f;
            foreach (var buff in profile.SharedHdBuffs) chance = Math.Max(chance, pool.Eligibility(recipient, buff));
            return chance;
        }

        private static bool FusionControlCatches(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData ally)
        {
            if (SelectedBuffAllyRounds(ctx, ability, profile, ally) > 0f) return true;
            if (profile.RemovedBuffs.Any(ally.Buffs.HasFact)) return true;
            if (profile.BuffApplications.Any(a => a.PeriodicDamageOnly && !ally.Buffs.HasFact(a.Buff) &&
                a.Applies(ability, ally) != EffectApplies.No && !CombatMath.BuffImmune(ally, a.Buff, ability))) return true;
            if (TryFusionControlRounds(ctx, ability, profile, ally, true, out float rounds, out float passRounds))
                return rounds > 0f || passRounds > 0f; // ★ Fusion F4: the ally is harmed on either save outcome
            // Unknown guard/duration/other harm cannot establish friendly-fire safety.
            return profile.TargetBuffs.Any(b => profile.BuffAppliesTo(b, ability, ally) != EffectApplies.No &&
                !ally.Buffs.HasFact(b) && !CombatMath.BuffImmune(ally, b, ability));
        }

        private static bool TryFusionValues(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
            UnitEntityData enemy, CombatMath.FusionDamageRead damageRead, float deliveryChance,
            out float damage, out float control, out float controlShare)
        {
            damage = control = controlShare = 0f;
            if (deliveryChance <= 0f) return true; // whole-cast immunity/no delivery cannot be revived by a surviving buff
            if (!damageRead.Known ||
                !TryFusionControlRounds(ctx, ability, profile, enemy, false, out float controlRounds, out float passRounds)) return false;
            // ★ Fusion coverage C1: damage over time the cast leaves on each outcome (burning, acid).
            if (!TryPeriodicDamage(ability, profile, enemy, PeriodicHorizon(ctx), false,
                    out float laterFailed, out float laterPassed)) return false;
            if ((controlRounds > 0f || passRounds > 0f) && profile.IsAoE && profile.InOwnSideArea(ctx.Unit, enemy))
                controlRounds = passRounds = 0f;
            ValueModel.FusionValue(damageRead.FailChance, deliveryChance, damageRead.Failed, damageRead.Passed, laterFailed, laterPassed,
                Math.Max(1f, enemy.HPLeft), CombatMath.TargetWorth(enemy), CombatMath.ControlWorth(enemy), controlRounds, passRounds,
                RegenerationRuns(enemy), CombatMath.AbilityEndsRegeneration(profile, enemy), out damage, out control);
            // The share of delivered casts that leave some control behind: the failed outcome, the passed one or both.
            controlShare = ValueModel.FusionOutcomeShare(damageRead.FailChance, controlRounds > 0f, passRounds > 0f);
            if (!profile.FusionValueQualifiedReported)
            {
                profile.FusionValueQualifiedReported = true;
                Main.Log($"[FusionValue] {ability.Name} -> {enemy.CharacterName}: forecast shared-save fail {damageRead.FailChance:P0}, " +
                    $"conditional damage fail/pass {damageRead.Failed:F1}/{damageRead.Passed:F1}" +
                    (laterFailed > 0f || laterPassed > 0f ? $", damage over time fail/pass {laterFailed:F1}/{laterPassed:F1}" : "") +
                    $", control envelope {controlRounds:F2} round-equivalents" +
                    (profile.FusionOutcomeEffectLocation != null ? $" on a failed save, {passRounds:F2} on a passed save" : "") +
                    (profile.FusionSharedHd ? $", rolled Hit Dice pool covers this recipient {FusionPoolChance(ctx, profile, enemy):P0}" : "") + "; " +
                    "per-application eligibility and surviving failed-save outcomes (first qualified recipient per configuration)");
            }
            return true;
        }
    }
}
