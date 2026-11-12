// Native ranged Spellstrike spends its payload on the first arrow, including a miss, rather than a melee hit.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        internal static bool NativeRangedStrike(UnitEntityData unit, AbilityData ability)
        {
            var part = unit?.Get<UnitPartMagus>();
            if (part?.EldritchArcher != true || !part.Spellstrike.Active || ability == null ||
                unit.GetFirstWeapon()?.Blueprint.IsRanged != true) return false;
            var action = ability.RuntimeActionType;
            return (action == UnitCommand.CommandType.Standard || action == UnitCommand.CommandType.Swift) && part.Spellbook != null &&
                part.IsSuitableForEldritchArcherSpellStrike(ability);
        }

        private static bool PlainRangedPayload(AbilityData ability, AbilityProfile profile) => ability?.MagicHackData == null &&
            profile?.SimpleProjectiles?.NeedAttackRoll == true && !profile.IsAoE && !profile.IsWeaponAttack &&
            profile.MeleeAttack == null && profile.ControlStrength != ControlStrength.Hard &&
            !profile.Summons && profile.CasterBuffs.Count == 0 && profile.CasterHeals.Count == 0 &&
            profile.SimpleProjectiles.ControlledProjectileHolderBuff == null;

        private static bool SafeRangedSource(UnitEntityData unit, AbilityData ability, UnitEntityData target, AbilityProfile profile)
        {
            var weapon = unit.GetFirstWeapon();
            return unit.View != null && target?.View != null && unit.IsEnemy(target) && weapon?.Blueprint.IsRanged == true &&
                unit.CanAttack(weapon) && PlainRangedPayload(ability, profile) &&
                InRangeNow(unit, target.Position, UnitAttack.GetApproachRadius(weapon, unit, target), true);
        }

        private static AbilityData ArmedRangedSpell(UnitEntityData unit)
        {
            var part = unit?.Get<UnitPartMagus>();
            // Same expiry/motion/equipment conditions as MagusController.Tick; never extend native retention.
            return part != null && part.Spellstrike.Active && part.EldritchArcherSpell != null &&
                !unit.HasMotionThisTick && unit.GetFirstWeapon()?.Blueprint.IsRanged == true &&
                Game.Instance.TimeController.GameTime - part.EldritchArcherSpellCastTime <= 1.Rounds().Seconds
                ? part.EldritchArcherSpell : null;
        }

        private static bool ValidateEldritchIssue(UnitEntityData unit, Candidate candidate, out string failure)
        {
            failure = null;
            var nativePending = unit.Get<UnitPartMagus>()?.EldritchArcherSpell;
            if (candidate.MagusPairedCast && candidate.MagusSourceStrike && unit.Get<UnitPartMagus>()?.EldritchArcher == true &&
                !NativeRangedStrike(unit, candidate.Ability))
                failure = "native ranged spell delivery state changed";
            if (nativePending != null && ArmedRangedSpell(unit) == null &&
                (candidate.Kind == CandidateKind.WeaponAttack || candidate.Kind == CandidateKind.Ability))
                failure = "native ranged spell awaits its motion/expiry/mode cleanup";
            if (candidate.MagusRangedCharge != null &&
                (!ReferenceEquals(ArmedRangedSpell(unit), candidate.MagusRangedCharge.Ability) || candidate.NeedsMove || candidate.MoveFirst))
                failure = "native armed ranged spell or in-place delivery changed";
            if (candidate.Ability != null && NativeRangedStrike(unit, candidate.Ability) &&
                (!SafeRangedSource(unit, candidate.Ability, candidate.Target, AbilityProfile.Get(candidate.Ability)) || ArmedRangedSpell(unit) != null))
                failure = "ranged Spellstrike requires a priced in-place bow delivery without replacing an armed spell";
            var armed = ArmedRangedSpell(unit);
            if (candidate.Kind == CandidateKind.WeaponAttack && armed != null && candidate.Target != null)
            {
                var scope = UserAbilityPolicy.ScopeFor(unit);
                var profile = AbilityProfile.Get(armed);
                if (UserAbilityPolicy.SourceOf(scope, armed) != BlockSource.None || profile.Heals.Count > 0)
                    failure = "native ranged weapon would discharge a blocked or helpful spell";
            }
            return failure == null;
        }

        internal static float RangedPayloadValue(Candidate candidate, WeaponAttackEstimate attack)
        {
            var payload = candidate.MagusRangedCharge;
            if (payload == null || attack == null) return 0f;
            float landed = payload.Damage * (attack.FirstHitChance + (payload.Critical ? attack.FirstCritChance : 0f));
            return Math.Max(0f, DamageValue(candidate.Target, attack.ExpectedDamage + landed, attack.AnyHitChance,
                attack.EndsRegeneration || payload.EndsRegeneration) - DamageValue(candidate.Target, attack)) * payload.DamageRole +
                payload.Other * attack.FirstHitChance;
        }

        private static Candidate PriceEldritchCast(TacticalContext ctx, UnitPartMagus part, Candidate source)
        {
            if (source.Ability == null || source.Target == null || source.NeedsMove || source.MoveFirst ||
                source.Ability.RuntimeActionType != UnitCommand.CommandType.Standard || !MagusSpell(part, source.Ability) ||
                ArmedRangedSpell(ctx.Unit) != null) return null;
            var profile = AbilityProfile.Get(source.Ability);
            bool strike = NativeRangedStrike(ctx.Unit, source.Ability);
            bool full = MagusFullCast(ctx, part);
            if ((!full && !strike) || source.MagusNeedsStrikeOff && part.Spellstrike.Active ||
                profile.ControlStrength == ControlStrength.Hard || profile.IsAoE && profile.DealsDamage) return null;
            if (strike && !SafeRangedSource(ctx.Unit, source.Ability, source.Target, profile)) return null;
            UnitEntityData target = strike ? source.Target : null;
            WeaponAttackEstimate weapon = null;
            float best = 0f;
            IEnumerable<UnitEntityData> targets = strike ? new[] { source.Target } : ctx.Enemies;
            foreach (var enemy in targets)
            {
                var attack = MagusWeapon(ctx, enemy, full, full, strike, activeModes: false);
                if (attack == null) continue;
                float value = DamageValue(enemy, attack) * AttackWeight(ctx, enemy, true) * WakeFactor(enemy, attack.ExpectedDamage);
                if (weapon == null || value > best) { best = value; weapon = attack; target = enemy; }
            }
            if (weapon == null) return null;
            // Every spell projectile shares the FIRST arrow's roll; there is no retry on later arrows/Manyshot.
            Candidate result;
            using (strike ? CombatMath.MagusRollScope.FirstArrow(source.Ability, source.Target, weapon,
                profile.Damage.All(d => d is Kingmaker.UnitLogic.Mechanics.Actions.ContextActionDealDamage p && !p.IgnoreCritical)) : null)
            using (AbilityProfile.AimedAt(new TargetWrapper(source.Target)))
            {
                result = EvaluateAbility(ctx, source.Ability, onlyTarget: source.Target).FirstOrDefault();
                if (result == null || result.NeedsMove || result.MoveFirst) return null;
                float damage = target == source.Target && profile.DealsDamage
                    ? CombatMath.EstimateAbilityDamage(source.Ability, profile, target) *
                      CombatMath.EffectChance(ctx.Unit, target, source.Ability, profile, true) : 0f;
                float spellChance = target == source.Target ? CombatMath.EffectChance(ctx.Unit, target, source.Ability, profile, true) : 0f;
                float any = strike ? weapon.AnyHitChance : 1f - (1f - spellChance) * (1f - weapon.AnyHitChance);
                float credit = Math.Max(0f, DamageValue(target, damage + weapon.ExpectedDamage, any, weapon.EndsRegeneration) -
                    DamageValue(target, damage, spellChance, false));
                result.Value += credit * AttackWeight(ctx, target, true) * WakeFactor(target, weapon.ExpectedDamage) * result.MagusCompletionFactor;
            }
            result.Slot = full ? ActionSlot.FullRound : source.Slot;
            result.MagusPairedCast = true;
            result.MagusSourceStrike = strike;
            result.PureBuff = false;
            result.Label += full ? " [native Ranged Spell Combat]" : " [native Ranged Spellstrike]";
            return result;
        }

        private static void AddEldritchPayloads(TacticalContext ctx, List<Candidate> candidates)
        {
            var armed = ArmedRangedSpell(ctx.Unit);
            if (armed == null) return;
            foreach (var c in candidates.Where(c => c.Kind == CandidateKind.WeaponAttack && c.Target != null).ToArray())
            {
                if (HeldChargeBlocked(ctx, armed, c.Target)) { candidates.Remove(c); continue; }
                if (c.NeedsMove || c.MoveFirst) continue; // Motion clears the native payload. Movement policy is unchanged.
                var weapon = c.MagusFollowCombat
                    ? MagusWeapon(ctx, c.Target, c.FullAttack, true, false, activeModes: false)
                    : CombatMath.EstimateWeaponAttack(ctx.Unit, c.Target, c.FullAttack);
                if (weapon?.IsRanged != true || !InRangeNow(ctx.Unit, c.Target.Position, weapon.Reach, true)) continue;
                c.MagusRangedCharge = ReadMagusPayload(ctx, armed, c.Target, ranged: true);
                c.Value += RangedPayloadValue(c, weapon) * c.ModeFactor;
                if (c.MagusRangedCharge != null) c.Label += " [armed Ranged Spellstrike: first arrow]";
            }
        }

        private static void PriceEldritchReservation(TacticalContext ctx, List<Candidate> candidates)
        {
            if (ArmedRangedSpell(ctx.Unit) == null) return;
            float reserved = MagusChargeReservation(ctx, candidates);
            foreach (var c in candidates)
            {
                if (c.Kind != CandidateKind.Ability || c.Ability == null) continue;
                if (NativeRangedStrike(ctx.Unit, c.Ability)) { c.Value = 0f; continue; }
                if (reserved > 0f && (c.Ability.Blueprint.Type == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityType.Spell ||
                    c.Ability.Blueprint.Type == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityType.SpellLike))
                { c.Value -= reserved; c.Label += " [replaces armed ranged spell]"; }
            }
        }
    }
}
