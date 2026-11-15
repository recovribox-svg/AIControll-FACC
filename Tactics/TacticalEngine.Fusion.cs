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
            Main.Log($"[Fusion] {ability.Name}: joint outcome value not qualified for this recipient; existing forecast retained " +
                $"({profile.FusionOutcomeMetadataReason ?? (ability.ShadowSpellSettings != null ? "shadow copy" : "recipient guard/duration/outcome or damage-wake/removal dependency")})");
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
            else if (profile.Dispels || profile.Enchants || profile.Extensions.Count > 0) reason = "dispel/enchantment/extension dependency";
            else if (profile.RemovedDescriptors != SpellDescriptor.None ||
                profile.RemovesBuffs && profile.RemovedBuffs.Count == 0) reason = "dynamic removal dependency";
            else if (!profile.DealsDamage && profile.BuffApplications.Count == 0) reason = "no direct HP/control payload";
            else if (!profile.Damage.All(a => a is ContextActionDealDamage d && CombatMath.IsHitPointDamage(d))) reason = "composite or non-HP damage";
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

        private static bool TryFusionControlRounds(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
            UnitEntityData recipient, bool friendly, out float rounds)
        {
            rounds = 0f;
            if (profile.RemovedBuffs.Any(recipient.Buffs.HasFact)) return false;
            if (profile.BuffApplications.Count == 0) return true;
            var envelope = ctx.FusionControlScratch ?? (ctx.FusionControlScratch = new ValueModel.ControlEnvelope());
            envelope.Clear();
            MechanicsContext durationContext = null;
            var single = ctx.FusionBuffScratch ?? (ctx.FusionBuffScratch = new List<BlueprintBuff>(1));
            float repeat = CombatMemory.TimesUsed(ctx.Unit, ability) >= 2 ? 0.3f : 1f;
            foreach (var occurrence in profile.BuffApplications)
            {
                var savePath = profile.FusionBuffPath(occurrence.Action);
                if (savePath == AbilityProfile.FusionSavePath.Passed ||
                    profile.Save == SavingThrowType.Unknown && savePath != AbilityProfile.FusionSavePath.Both) continue;
                var buff = occurrence.Buff;
                if (occurrence.PeriodicDamageOnly) continue; // omitted DOT benefit must not become generic lost turns
                if (recipient.Buffs.HasFact(buff)) continue; // no new stacking or speculative refresh credit
                var applies = occurrence.Applies(ability, recipient);
                if (applies == EffectApplies.No || CombatMath.BuffImmune(recipient, buff, ability)) continue;
                if (applies == EffectApplies.Unknown) return false;
                // A low-HD sleep branch may wake from a companion payload; an inapplicable sleep branch must not
                // suppress the independent high-HD stun branch of Color Spray.
                bool sleeping = occurrence.Conditions?.Contains(Kingmaker.UnitLogic.UnitCondition.Sleeping) == true;
                if (sleeping && profile.DealsDamage) return false;
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
                if (share <= 0f) continue;
                var action = occurrence.Action;
                float duration = occurrence.Rounds;
                if (duration > 0f)
                {
                    if (occurrence.Extendable && ability.HasMetamagic(Metamagic.Extend)) duration *= 2f;
                }
                else
                {
                    if (action == null || action.SameDuration) return false;
                    if (!CombatMath.TryFusionDuration(ability, action, recipient, out duration))
                    {
                        if (durationContext == null) durationContext = CombatMath.CreateContext(ability, new TargetWrapper(recipient));
                        if (durationContext == null) return false;
                        duration = action.Permanent ? 100f : action.UseDurationSeconds ? Math.Max(1f, action.DurationSeconds / 6f)
                                                                                     : DurationRounds(action.DurationValue, ability, durationContext);
                        CombatMath.StoreFusionDuration(ability, action, recipient, duration);
                    }
                    if (duration <= 0f) return false;
                }
                float horizon = Math.Min(Math.Min(ctx.ExpectedRounds, 3f), duration);
                if (sleeping && s_ActiveEnemies != null && s_ActiveEnemies.All(e => e == recipient) && ctx.Allies.Any(a => a != ctx.Unit))
                    horizon = Math.Min(horizon, 1f);
                if (!envelope.Add(share, horizon)) return false;
            }
            rounds = envelope.Rounds();
            if (!friendly && CombatMath.IsDisabled(recipient)) rounds *= 0.4f;
            return true;
        }

        private static bool FusionControlCatches(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData ally)
        {
            if (profile.RemovedBuffs.Any(ally.Buffs.HasFact)) return true;
            if (profile.BuffApplications.Any(a => a.PeriodicDamageOnly && !ally.Buffs.HasFact(a.Buff) &&
                a.Applies(ability, ally) != EffectApplies.No && !CombatMath.BuffImmune(ally, a.Buff, ability))) return true;
            if (TryFusionControlRounds(ctx, ability, profile, ally, true, out float rounds)) return rounds > 0f;
            // Unknown guard/duration/other harm cannot establish friendly-fire safety.
            return profile.TargetBuffs.Any(b => profile.BuffAppliesTo(b, ability, ally) != EffectApplies.No &&
                !ally.Buffs.HasFact(b) && !CombatMath.BuffImmune(ally, b, ability));
        }

        private static bool TryFusionValues(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
            UnitEntityData enemy, CombatMath.FusionDamageRead damageRead, float deliveryChance,
            out float damage, out float control)
        {
            damage = control = 0f;
            if (deliveryChance <= 0f) return true; // whole-cast immunity/no delivery cannot be revived by a surviving buff
            if (!damageRead.Known || !TryFusionControlRounds(ctx, ability, profile, enemy, false, out float controlRounds)) return false;
            if (controlRounds > 0f && profile.IsAoE && profile.InOwnSideArea(ctx.Unit, enemy)) controlRounds = 0f;
            ValueModel.FusionValue(damageRead.FailChance, deliveryChance, damageRead.Failed, damageRead.Passed,
                Math.Max(1f, enemy.HPLeft), CombatMath.TargetWorth(enemy), CombatMath.ControlWorth(enemy), controlRounds,
                RegenerationRuns(enemy), CombatMath.AbilityEndsRegeneration(profile, enemy), out damage, out control);
            if (!profile.FusionValueQualifiedReported)
            {
                profile.FusionValueQualifiedReported = true;
                Main.Log($"[FusionValue] {ability.Name} -> {enemy.CharacterName}: forecast shared-save fail {damageRead.FailChance:P0}, " +
                    $"conditional damage fail/pass {damageRead.Failed:F1}/{damageRead.Passed:F1}, control envelope {controlRounds:F2} round-equivalents; " +
                    "per-application eligibility and surviving failed-save outcomes (first qualified recipient per configuration)");
            }
            return true;
        }
    }
}
