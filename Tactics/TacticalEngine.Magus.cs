// ★ C07: native melee Spellstrike/Spell Combat. At most four useful in-place casts are repriced per state;
// the two native toggles compete as separate free preparations, with existing ownership and restore rules.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Root;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        internal static void MagusStateChanged()
        {
            CombatMath.InfusionStateChanged(); // Existing scalar-preview invalidation includes contexts/conditions/SR.
            s_Offence.Clear();
            s_Defence.Clear();
        }
        private static bool NativeMagusWouldQueueWeapon(UnitEntityData unit, AbilityData ability, UnitEntityData target)
        {
            var part = unit?.Get<UnitPartMagus>();
            return part?.Spellstrike.Active == true && !part.EldritchArcher && target != null && unit.IsEnemy(target) &&
                ability?.Blueprint?.StickyTouch != null && MagusSpell(part, ability);
        }

        private static bool ValidateMagusIssue(UnitEntityData unit, Candidate candidate, out string failure)
        {
            failure = null;
            var native = unit.Get<UnitPartMagus>();
            if (candidate.MagusNeedsStrikeOff && native?.Spellstrike.Active == true)
                failure = "native Spellstrike must be off for this priced delivery";
            if (candidate.MagusPairedCast && (candidate.MagusSourceStrike && native?.Spellstrike.Active != true ||
                candidate.Slot == ActionSlot.FullRound && (native == null || !unit.HasFullRoundAction() || !unit.HasMoveAction() ||
                    !native.CanUseSpellCombat))) failure = "native paired Magus action is no longer available";
            if (candidate.MagusFollowCombat && !unit.PreparedSpellCombat() || candidate.MagusFollowStrike && !unit.PreparedSpellStrike())
                failure = "native Magus follow-up window/equipment no longer valid";
            var held = MagusMechanics.Held(unit);
            var delivery = candidate.Kind == CandidateKind.WeaponAttack && held != null
                ? CombatMath.EstimateWeaponAttack(unit, candidate.Target, candidate.FullAttack) : null;
            if (candidate.MagusCharge != null && (!ReferenceEquals(held, candidate.MagusCharge.Ability) || (delivery?.TouchHitChance ?? 0f) <= 0f))
                failure = "native held touch or eligible weapon delivery changed";
            if (held != null && delivery?.TouchHitChance > 0f)
            {
                var scope = UserAbilityPolicy.ScopeFor(unit);
                var profile = AbilityProfile.Get(held);
                if (UserAbilityPolicy.SourceOf(scope, held) != BlockSource.None ||
                    profile.Heals.Any(h => profile.AppliesTo(h, held, candidate.Target) != EffectApplies.No))
                    failure = "native weapon would deliver a blocked or helpful held charge to an enemy";
            }
            if ((candidate.MagusFollowCombat || candidate.MagusFollowStrike) && candidate.Target != null)
            {
                var weapon = unit.GetFirstWeapon();
                if (unit.View == null || candidate.Target.View == null || weapon == null ||
                    !InRangeNow(unit, candidate.Target.Position, UnitAttack.GetApproachRadius(weapon, unit, candidate.Target), true))
                    failure = "native Magus follow-up requires an in-place attack";
            }
            return failure == null;
        }

        internal sealed class MagusPayload
        {
            internal AbilityData Ability;
            internal float Damage, Other, DamageRole;
            internal bool Critical, EndsRegeneration;
        }

        private static bool MeleeMagus(TacticalContext ctx, out UnitPartMagus part)
        {
            part = ctx.Unit.Get<UnitPartMagus>();
            return part != null && (part.SpellCombat.Unlocked || part.Spellstrike.Unlocked) &&
                !part.EldritchArcher && !PureMagePolicy.IsActive(ctx.Settings);
        }
        internal static void ConfigureMagusFollowup(UnitAttack attack, bool combat, TimeSpan window)
        {
            attack.IgnoreCooldown(window);
            attack.ForceFullAttack = combat;
            attack.IsSingleAttack = !combat;
        }
        private static bool MagusSpell(UnitPartMagus part, AbilityData ability) => ability != null && ability.MagicHackData == null &&
            part.Spellbook != null && part.IsSpellFromMagusSpellList(ability);
        // A new Standard cast opens the native opportunity in HandleUnitCommandDidAct. An old opportunity clock
        // is therefore not a prerequisite here (in RT, IsSpellCombatThisRoundAllowed always checks that old clock).
        // Actual post-cast follow-ups still require PreparedSpellCombat and its finite native deadline.
        private static bool MagusFullCast(TacticalContext ctx, UnitPartMagus part) => ctx.HasStandard && ctx.HasMove && ctx.CanFullAttack &&
            part.CanUseSpellCombat;

        private static WeaponAttackEstimate MagusWeapon(TacticalContext ctx, UnitEntityData target, bool full, bool projectedCombat, bool freshTouch,
            bool activeModes = true)
        {
            var first = ctx.Unit.GetFirstWeapon();
            if (ctx.Unit.View == null || target?.View == null || first?.Blueprint?.IsMelee != true ||
                !InRangeNow(ctx.Unit, target.Position, UnitAttack.GetApproachRadius(first, ctx.Unit, target), true)) return null;
            // All C07 projections are in place. Reject distant pairs before calculating attacks, AC or damage.
            var root = BlueprintRoot.Instance.SystemMechanics;
            int extra = 0;
            if (freshTouch && full && ctx.Unit.Buffs.GetBuff(root.MagusSpellStrikeBuff)?.Active != true &&
                !MagusMechanics.StrikeExtra(root.MagusSpellStrikeBuff, out extra))
            {
                AbilityDiscovery.ReportOnce("magus-extra|" + root.MagusSpellStrikeBuff?.AssetGuid,
                    () => $"[Magus] {root.MagusSpellStrikeBuff?.name ?? "missing strike buff"}: unsupported native extra-attack graph; projected combo withheld");
                return null;
            }
            var modes = AttackModes(ctx);
            bool known = true;
            KeyValuePair<int, float> Delta(ItemEntityWeapon weapon, int index)
            {
                int attack = 0; float damage = 0f;
                foreach (var mode in activeModes ? modes : NoModes)
                {
                    int sign = (mode.On ? 1 : 0) - (mode.InEstimate ? 1 : 0);
                    if (mode.On) attack += mode.FullAttackOnly(weapon, full);
                    attack += sign * mode.Attack(weapon, full);
                    damage += sign * mode.Damage(weapon);
                }
                if (projectedCombat)
                {
                    if (!MagusMechanics.CombatPenalty(ctx.Unit, root.MagusSpellCombatBuff, weapon, out int penalty)) known = false;
                    else attack += penalty;
                }
                return new KeyValuePair<int, float>(attack, damage);
            }
            var estimate = CombatMath.MagusWeaponAttack(ctx.Unit, target, full, Delta, extra);
            if (!known) AbilityDiscovery.ReportOnce("magus-combat|" + root.MagusSpellCombatBuff?.AssetGuid,
                () => $"[Magus] {root.MagusSpellCombatBuff?.name ?? "missing combat buff"}: unsupported native attack-penalty graph; projected combo withheld");
            return known && estimate?.IsRanged == false && ctx.Unit.CanAttack(estimate.Weapon) ? estimate : null;
        }

        private static MagusPayload ReadMagusPayload(TacticalContext ctx, AbilityData held, UnitEntityData target)
        {
            if (!ReferenceEquals(ctx.MagusPayloadSource, held))
            {
                ctx.MagusPayloadSource = held;
                ctx.MagusPayloads = new Dictionary<(UnitEntityData, bool, bool), MagusPayload>();
            }
            var part = ctx.Unit.Get<UnitPartMagus>();
            var key = (target, part.SpellCombat.Active, part.Spellstrike.Active);
            if (ctx.MagusPayloads.TryGetValue(key, out var cached)) return cached;
            var profile = AbilityProfile.Get(held);
            if (!MagusMechanics.PlainTouch(held, profile) || profile.CasterBuffs.Count > 0 || profile.CasterHeals.Count > 0 ||
                !held.CanTarget(new TargetWrapper(target))) return ctx.MagusPayloads[key] = null;
            using (CombatMath.MemoizeResistance())
            using (new CombatMath.MagusRollScope(held, target, null))
            using (AbilityProfile.AimedAt(new TargetWrapper(target)))
            {
                float value = ScoreAbility(ctx, held, profile, ActionSlot.Standard, target, new TargetWrapper(target), ctx.Unit.Position,
                    out _, out _, out _, out _, valueActiveInfusions: false);
                float damageChance = profile.DealsDamage ? CombatMath.EffectChance(ctx.Unit, target, held, profile, true) : 0f;
                float damage = profile.DealsDamage ? CombatMath.EstimateAbilityDamage(held, profile, target) * damageChance : 0f;
                float role = RoleWeight(ctx, "damage");
                return ctx.MagusPayloads[key] = new MagusPayload { Ability = held, Damage = damage, DamageRole = role,
                    Other = Math.Max(0f, value - DamageValue(target, damage, damageChance, CombatMath.AbilityEndsRegeneration(profile, target)) * role),
                    Critical = profile.Damage.Count > 0 && profile.Damage.All(d => d is ContextActionDealDamage damagePart && !damagePart.IgnoreCritical),
                    EndsRegeneration = CombatMath.AbilityEndsRegeneration(profile, target) };
            }
        }

        private static float MagusChargeValue(Candidate candidate, WeaponAttackEstimate attack)
        {
            var payload = candidate.MagusCharge;
            if (payload == null || attack == null) return 0f;
            float landed = payload.Damage * (attack.TouchHitChance + (payload.Critical ? attack.TouchCritChance : 0f));
            float paired = DamageValue(candidate.Target, attack.ExpectedDamage + landed, attack.AnyHitChance,
                attack.EndsRegeneration || payload.EndsRegeneration);
            float weapon = DamageValue(candidate.Target, attack.ExpectedDamage, attack.AnyHitChance, attack.EndsRegeneration);
            return Math.Max(0f, paired - weapon) * payload.DamageRole + payload.Other * attack.TouchHitChance;
        }

        private static bool HeldChargeBlocked(TacticalContext ctx, AbilityData held, UnitEntityData target)
        {
            if (UserAbilityPolicy.SourceOf(ctx.Blocks, held) != BlockSource.None) return true;
            var profile = AbilityProfile.Get(held);
            // The native on-hit handler would also deliver a helpful held charge. Do not heal an enemy via a weapon.
            return profile.Heals.Any(h => profile.AppliesTo(h, held, target) != EffectApplies.No);
        }

        private static float MagusChargeReservation(TacticalContext ctx, List<Candidate> candidates)
        {
            float best = 0f;
            if (!candidates.Any(c => c.MagusCharge != null)) return best;
            var modes = AttackModes(ctx);
            foreach (var c in candidates)
            {
                if (c.MagusCharge == null || c.Value <= 0f) continue;
                var attack = CombatMath.EstimateWeaponAttack(ctx.Unit, c.Target, c.FullAttack, (weapon, index) =>
                {
                    int ab = 0; float damage = 0f;
                    foreach (var mode in modes)
                    {
                        bool selected = mode.On;
                        if (c.Modes != null)
                            foreach (var change in c.Modes) if (change.Key == mode.Toggle) selected = change.Value;
                        int sign = (selected ? 1 : 0) - (mode.InEstimate ? 1 : 0);
                        if (selected) ab += mode.FullAttackOnly(weapon, c.FullAttack);
                        ab += sign * mode.Attack(weapon, c.FullAttack);
                        damage += sign * mode.Damage(weapon);
                    }
                    if (c.MagusFollowCombat && MagusMechanics.CombatPenalty(ctx.Unit,
                        BlueprintRoot.Instance.SystemMechanics.MagusSpellCombatBuff, weapon, out int penalty)) ab += penalty;
                    return new KeyValuePair<int, float>(ab, damage);
                });
                // Casting over the charge loses its marginal payload, not the ordinary weapon routine as well.
                best = Math.Max(best, MagusChargeValue(c, attack) * c.ModeFactor);
            }
            return best;
        }

        private static void AddMagusWeaponCandidates(TacticalContext ctx, List<Candidate> candidates)
        {
            bool melee = MeleeMagus(ctx, out var part);
            if (!melee && MagusMechanics.Held(ctx.Unit) == null) return;
            bool combat = melee && ctx.Unit.PreparedSpellCombat();
            bool strike = melee && !combat && ctx.Unit.PreparedSpellStrike();
            if (combat || strike)
            {
                // Native MagusController owns the free follow-up's shape. An ordinary ForceFullAttack candidate
                // must not turn a Spellstrike-only free single into a free full routine, even with extra actions.
                candidates.RemoveAll(c => c.Kind == CandidateKind.WeaponAttack);
                foreach (var target in ctx.Enemies)
                {
                    var attack = MagusWeapon(ctx, target, combat, combat, false, activeModes: false);
                    if (attack == null || !InRangeNow(ctx.Unit, target.Position, attack.Reach, true)) continue;
                    float weight = AttackWeight(ctx, target, false) * WakeFactor(target, attack.ExpectedDamage);
                    candidates.Add(new Candidate { Kind = CandidateKind.WeaponAttack, Target = target, Offensive = true,
                        Slot = combat ? ActionSlot.Move : ActionSlot.Free, FullAttack = combat,
                        MagusFollowCombat = combat, MagusFollowStrike = strike,
                        Value = DamageValue(target, attack) * weight, ModeFactor = weight,
                        Label = $"{(combat ? "Spell Combat" : "Spellstrike")} follow-up({attack.Attacks})->{target.CharacterName}",
                        Key = $"magus-follow:{combat}:{target.UniqueId}" });
                }
            }
            var held = MagusMechanics.Held(ctx.Unit);
            if (held == null) return;
            foreach (var candidate in candidates.Where(c => c.Kind == CandidateKind.WeaponAttack && c.Target != null).ToList())
            {
                // Use the native routine before attack-mode correction, matching the ordinary candidate baseline.
                var attack = candidate.MagusFollowCombat
                    ? MagusWeapon(ctx, candidate.Target, candidate.FullAttack, true, false, activeModes: false)
                    : CombatMath.EstimateWeaponAttack(ctx.Unit, candidate.Target, candidate.FullAttack);
                if (attack == null || attack.TouchHitChance <= 0f) continue;
                // Remove it before generic attack-mode repricing, which would otherwise restore ordinary weapon value.
                if (HeldChargeBlocked(ctx, held, candidate.Target)) { candidates.Remove(candidate); continue; }
                if (!melee || !MagusSpell(part, held)) continue;
                var payload = ReadMagusPayload(ctx, held, candidate.Target);
                if (payload == null) continue;
                candidate.MagusCharge = payload;
                candidate.Value += MagusChargeValue(candidate, attack) * candidate.ModeFactor;
                candidate.Label += $" [held {held.Name}]";
            }
        }

        private static Candidate PriceMagusCast(TacticalContext ctx, UnitPartMagus part, Candidate source)
        {
            if (source.Ability == null || source.NeedsMove || source.MoveFirst || source.Target == null ||
                source.Ability.RuntimeActionType != UnitCommand.CommandType.Standard || !MagusSpell(part, source.Ability)) return null;
            if (source.PureBuff && CombatMath.OnlyDisabledEnemiesLeft) return null;
            var profile = AbilityProfile.Get(source.Ability);
            if (source.MagusNeedsStrikeOff && part.Spellstrike.Active || profile.IsAoE && profile.DealsDamage) return null;
            bool physical = MagusMechanics.PlainTouch(source.Ability, profile) && ctx.Unit.IsEnemy(source.Target);
            bool strike = physical && part.Spellstrike.Active;
            bool full = MagusFullCast(ctx, part);
            if (!full && !strike) return null;
            // Hard control can make its victim a bad attack target. Touch payloads land after their weapon hit;
            // other hard-control casts retain their existing value and are not speculatively paired here.
            if (!physical && profile.ControlStrength == ControlStrength.Hard) return null;
            UnitEntityData weaponTarget = strike ? source.Target : null;
            WeaponAttackEstimate weapon = null;
            float best = 0f;
            IEnumerable<UnitEntityData> targets = strike ? new[] { source.Target } : ctx.Enemies;
            foreach (var target in targets)
            {
                var attack = MagusWeapon(ctx, target, full, full, strike);
                if (attack == null || !InRangeNow(ctx.Unit, target.Position, attack.Reach, true)) continue;
                // Native item casts retain an existing charge unless they create another touch themselves.
                // A follow-up must still be permitted to deliver that charge, including through natural attacks.
                if (source.Ability.SourceItem != null && source.Ability.Blueprint.StickyTouch == null &&
                    MagusMechanics.Held(ctx.Unit) is AbilityData retained && attack.TouchHitChance > 0f &&
                    HeldChargeBlocked(ctx, retained, target)) continue;
                float value = DamageValue(target, attack) * AttackWeight(ctx, target, false);
                if (value > best) { best = value; weapon = attack; weaponTarget = target; }
            }
            if (weapon == null) return null;
            Candidate repriced;
            using (strike ? new CombatMath.MagusRollScope(source.Ability, source.Target, weapon,
                profile.Damage.All(d => d is ContextActionDealDamage damagePart && !damagePart.IgnoreCritical)) : null)
            using (AbilityProfile.AimedAt(new TargetWrapper(source.Target)))
            {
                repriced = EvaluateAbility(ctx, source.Ability, onlyTarget: source.Target).FirstOrDefault();
                if (repriced == null || repriced.NeedsMove || repriced.MoveFirst) return null;
                float spellDamage = weaponTarget == source.Target && profile.DealsDamage
                    ? CombatMath.EstimateAbilityDamage(source.Ability, profile, source.Target) *
                      CombatMath.EffectChance(ctx.Unit, source.Target, source.Ability, profile, true) : 0f;
                float spellHit = weaponTarget == source.Target ? CombatMath.EffectChance(ctx.Unit, source.Target, source.Ability, profile, true) : 0f;
                float combined = DamageValue(weaponTarget, spellDamage + weapon.ExpectedDamage,
                    strike ? weapon.AnyHitChance : 1f - (1f - spellHit) * (1f - weapon.AnyHitChance), weapon.EndsRegeneration);
                float prior = DamageValue(weaponTarget, spellDamage, spellHit, false);
                float credit = Math.Max(0f, combined - prior) * AttackWeight(ctx, weaponTarget, false) *
                    WakeFactor(weaponTarget, weapon.ExpectedDamage);
                repriced.Value += credit * repriced.MagusCompletionFactor;
            }
            repriced.Slot = full ? ActionSlot.FullRound : source.Slot;
            repriced.MagusPairedCast = true;
            repriced.MagusSourceStrike = strike;
            repriced.PureBuff = false; // This candidate also buys a native weapon routine in the same action budget.
            repriced.Label += full ? " [native Spell Combat]" : " [native Spellstrike]";
            return repriced;
        }

        private static void AddMagusCastPlans(TacticalContext ctx, List<Candidate> candidates, bool preview)
        {
            if (!MeleeMagus(ctx, out var part)) return;
            if (ctx.Timings == null) { AddMagusCastPlansCore(ctx, candidates, preview, part); return; }
            long started = System.Diagnostics.Stopwatch.GetTimestamp(), heap = GC.GetTotalMemory(false);
            int before = candidates.Count, contexts = CombatMath.ContextsMade, estimates = CombatMath.DamageEstimates;
            long estimateBytes = CombatMath.EstimateBytes, chanceBytes = CombatMath.ChanceBytes;
            CombatMath.MeasureHeap = true;
            try { AddMagusCastPlansCore(ctx, candidates, preview, part); }
            finally
            {
                CombatMath.MeasureHeap = false;
                long growth = GC.GetTotalMemory(false) - heap;
                ctx.Timings.NoteAbility("Native Magus plans (four-cast budget)", started, Math.Max(0, candidates.Count - before),
                    growth >= 0 ? growth : -1, CombatMath.ContextsMade - contexts, CombatMath.DamageEstimates - estimates,
                    CombatMath.EstimateBytes - estimateBytes, CombatMath.ChanceBytes - chanceBytes);
            }
        }

        private static void AddMagusCastPlansCore(TacticalContext ctx, List<Candidate> candidates, bool preview, UnitPartMagus part)
        {
            ReportOnce($"magus-state|{ctx.Unit.UniqueId}|{CombatMemory.Fight}", $"[Magus] {ctx.Unit.CharacterName}: " +
                $"native Spell Combat {part.SpellCombat.Active}/{part.SpellCombat.Unlocked}, Spellstrike {part.Spellstrike.Active}/{part.Spellstrike.Unlocked}; " +
                $"held {MagusMechanics.Held(ctx.Unit)?.Blueprint?.name ?? "none"}; native eligibility/clock owns follow-ups");
            var available = candidates.Where(c => c.Kind == CandidateKind.Ability && c.Ability != null && c.Target != null &&
                c.Slot == ActionSlot.Standard && !c.NeedsMove && !c.MoveFirst && c.Value > 0f &&
                !PointlessAgainstHelpless(ctx, c, preview: true) && MagusSpell(part, c.Ability))
                .OrderByDescending(c => c.Value).GroupBy(c => c.Ability).Select(g => g.First()).ToArray();
            var touch = available.FirstOrDefault(c => MagusMechanics.PlainTouch(c.Ability, AbilityProfile.Get(c.Ability)));
            var sources = (touch == null ? available : new[] { touch }.Concat(available)).Distinct().Take(4).ToArray();
            foreach (var source in sources)
            {
                var paired = PriceMagusCast(ctx, part, source);
                if (paired != null && paired.Value > source.Value) { candidates.Remove(source); candidates.Add(paired); }
            }
            if (preview || !(ctx.Global?.AiManagesToggles ?? true) || ctx.Unit.PreparedSpellCombat() || ctx.Unit.PreparedSpellStrike()) return;
            float baseline = candidates.Where(c => c.Value > 0f && Affordable(ctx, c) && !QuickAction(c)).Select(c => c.Value).DefaultIfEmpty(0f).Max();
            int triedModes = 0;
            foreach (var toggle in ctx.Unit.ActivatableAbilities.Enumerable)
            {
                var kind = MagusMechanics.Kind(toggle?.Blueprint);
                if (kind == MagusMechanics.Mode.None || CombatMemory.PlayerOwnsToggle(ctx.Unit, toggle) ||
                    CombatMemory.ToggleSwitchedThisRound(ctx.Unit, toggle)) continue;
                var feature = kind == MagusMechanics.Mode.Combat ? part.SpellCombat : part.Spellstrike;
                bool turnOn = !feature.Active;
                if (turnOn && (!feature.Unlocked || !toggle.IsAvailable || UserAbilityPolicy.SourceOf(ctx.Blocks, toggle) != BlockSource.None)) continue;
                int bit = 1 << (int)kind;
                if ((triedModes & bit) != 0) continue;
                triedModes |= bit; // At most the two native mode states, even if another mod duplicates a toggle.
                float future = 0f; string planned = null;
                using (new MagusMechanics.Preview(part, kind == MagusMechanics.Mode.Combat ? turnOn : part.SpellCombat.Active,
                    kind == MagusMechanics.Mode.Strike ? turnOn : part.Spellstrike.Active, ctx.Unit.Body.CurrentHandsEquipmentSet))
                {
                    foreach (var source in sources)
                    {
                        if (kind == MagusMechanics.Mode.Strike && !MagusMechanics.PlainTouch(source.Ability, AbilityProfile.Get(source.Ability))) continue;
                        var option = PriceMagusCast(ctx, part, source) ?? EvaluateAbility(ctx, source.Ability, onlyTarget: source.Target).FirstOrDefault();
                        if (part.Spellstrike.Active && ReferenceEquals(source.Ability, MagusMechanics.Held(ctx.Unit)))
                        {
                            var attack = MagusWeapon(ctx, source.Target, ctx.CanFullAttack, false, false);
                            if (attack != null && InRangeNow(ctx.Unit, source.Target.Position, attack.Reach, true) &&
                                !HeldChargeBlocked(ctx, source.Ability, source.Target))
                            {
                                var held = new Candidate { Target = source.Target, MagusCharge = ReadMagusPayload(ctx, source.Ability, source.Target) };
                                float value = (DamageValue(source.Target, attack) + MagusChargeValue(held, attack)) * AttackWeight(ctx, source.Target, false);
                                if (value > future) { future = value; planned = "native held-charge weapon delivery"; }
                            }
                        }
                        if (option != null && Affordable(ctx, option) && option.Value > future) { future = option.Value; planned = option.Label; }
                    }
                    if (!turnOn && kind == MagusMechanics.Mode.Strike && MagusMechanics.Held(ctx.Unit) is AbilityData charge)
                    {
                        var direct = EvaluateAbility(ctx, charge).FirstOrDefault();
                        if (direct != null)
                        {
                            var option = PriceMagusCast(ctx, part, direct) ?? direct;
                            if (Affordable(ctx, option) && option.Value > future) { future = option.Value; planned = option.Label; }
                        }
                    }
                    // Turning an AI-owned mode off can restore native two-handed damage or avoid discharging a
                    // blocked/helpful charge. Quote an in-place attack only; no new movement plan is introduced.
                    if (!turnOn && ctx.HasStandard)
                    foreach (var target in ctx.Enemies)
                    {
                        var attack = MagusWeapon(ctx, target, ctx.CanFullAttack, false, false);
                        if (attack == null || !InRangeNow(ctx.Unit, target.Position, attack.Reach, true)) continue;
                        if (MagusMechanics.Held(ctx.Unit) is AbilityData held &&
                            attack.TouchHitChance > 0f && HeldChargeBlocked(ctx, held, target)) continue;
                        float value = DamageValue(target, attack) * AttackWeight(ctx, target, false);
                        if (value > future) { future = value; planned = "native weapon attack"; }
                    }
                }
                float gain = future - baseline;
                if (gain <= Math.Max(0.5f, baseline * 0.05f)) continue;
                candidates.Add(new Candidate { Kind = CandidateKind.Toggle, Toggle = toggle, ToggleOff = !turnOn,
                    Slot = ActionSlot.Free, Value = gain, Label = $"{(turnOn ? "Enable" : "Disable")} {toggle.Name} for {planned}",
                    Key = $"magus-mode:{toggle.Blueprint.AssetGuid}:{turnOn}" });
            }
        }
    }
}
