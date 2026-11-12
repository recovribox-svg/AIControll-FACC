// ★ v0.3.30: Shifter form appraisals and bounded native Fury selection in the shared planner.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Settings;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private sealed class ShifterDecision
        {
            internal readonly Dictionary<BlueprintBuff, FormQuote> Forms = new Dictionary<BlueprintBuff, FormQuote>();
            internal List<EntityFact> Transfers;
        }
        private sealed class FormQuote
        {
            internal ShifterFormProjection Projection;
            internal float? Damage;
        }
        private static readonly ConditionalWeakTable<TacticalContext, ShifterDecision> ShifterDecisions =
            new ConditionalWeakTable<TacticalContext, ShifterDecision>();
        private static readonly StatType[] FormAttributes = { StatType.Strength, StatType.Dexterity, StatType.Constitution,
            StatType.Intelligence, StatType.Wisdom, StatType.Charisma };

        private static List<EntityFact> ShifterTransfers(TacticalContext ctx, ShifterDecision decision)
        {
            if (decision.Transfers == null)
                decision.Transfers = ctx.Unit.Facts.List.Where(f => f != null && f.Active &&
                    f.Blueprint?.GetComponent<Kingmaker.UnitLogic.Buffs.PolymorphDamageTransfer>() != null).ToList();
            return decision.Transfers;
        }

        private static BuffAppraisal AppraiseShifterForm(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
            List<BlueprintBuff> buffs, UnitEntityData ally)
        {
            if (s_AppraiseWithout != null && PolymorphFacts.Form(s_AppraiseWithout.Blueprint as BlueprintBuff) != null)
                return new BuffAppraisal { Detail = "removing an existing form retains the prior fallback appraisal" };
            var result = new BuffAppraisal { Modelled = true, Detail = "major form is not an automatic numerical buff" };
            if (ally != ctx.Unit || PolymorphFacts.HasForm(ally) || PureMagePolicy.IsActive(ctx.Settings) ||
                FurySelectionPresent(ally.Get<ShiftersFuryPart>())) return result;
            var forms = buffs.Where(b => PolymorphFacts.Form(b) != null &&
                KnownFormBranch(profile.BuffAppliesTo(b, ability, ally))).Distinct().ToList();
            // A branch whose native condition cannot be evaluated is not permission to forecast all alternative forms.
            if (forms.Count != 1) { result.Detail = "major form branch is not uniquely known"; return result; }
            var buff = forms[0];
            var form = PolymorphFacts.Form(buff);
            if (!ShifterFormProjection.Supports(form)) { result.Detail = "form layout outside the supported natural-weapon projection"; return result; }
            var off = OffenceOf(ctx, ally);
            if (off.Caster) { result.Detail = "spellcasting cost of a new form is not modelled"; return result; }
            var enemy = off.Enemy ?? ctx.Enemies.FirstOrDefault(e => e?.View != null && !CombatMath.IsDisabled(e));
            if (enemy == null) return result;
            var decision = ShifterDecisions.GetValue(ctx, _ => new ShifterDecision());
            if (!decision.Forms.TryGetValue(buff, out var quote))
            {
                // No cross-target search. Repeated duration/ability variants share one shape/nearest-enemy quote.
                if (decision.Forms.Count >= 8) { result.Detail = "major form quote budget reached"; return result; }
                quote = new FormQuote();
                decision.Forms[buff] = quote;
                try
                {
                    quote.Projection = new ShifterFormProjection(ally, buff, form);
                    PriceLostFormLinks(ally, quote.Projection);
                    quote.Damage = CombatMath.ShifterFormDamage(quote.Projection, enemy, ShifterTransfers(ctx, decision));
                }
                catch (Exception ex) { Main.Verbose($"[Shifter] {buff.name}: {ex.Message}"); }
            }
            if (!quote.Damage.HasValue) { result.Detail = "native form quote unavailable"; return result; }
            var source = CombatMath.CreateContext(ability, new TargetWrapper(ally));
            float duration = EffectiveRounds(ability, profile, buff, source);
            float rounds = ValueModel.UsefulRounds(duration, ctx.ExpectedRounds, Math.Min(3f, ctx.ExpectedRounds));
            bool ranged = form.AdditionalLimbs.First().IsRanged;
            float damageGain = quote.Damage.Value * PreferenceWeight(ctx, ranged) - off.DamagePerRound * PreferenceWeight(ctx, off.Ranged);
            var def = DefenceOf(ctx, ally);
            if (quote.Projection.HpLoss >= def.Hp) { result.Detail = "form would remove its remaining hit points"; return result; }
            int ac = quote.Projection.ArmorChange;
            float kept = ValueModel.ArmorGain(def.Incoming, def.EnemyHitChance, ac) - def.Incoming * 0.025f * quote.Projection.SaveLoss;
            float protection = ValueModel.PreventionValue(kept, rounds, def.Hp,
                def.Incoming + CombatMath.AttachedDamageOf(ctx, ally).NextRisk, def.Worth);
            float risk = def.Incoming + CombatMath.AttachedDamageOf(ctx, ally).NextRisk;
            float hpCost = Math.Min(1f, Math.Max(0f, RiskMath.RoundDropChance(def.Hp - quote.Projection.HpLoss, risk) -
                RiskMath.RoundDropChance(def.Hp, risk)) * rounds) * def.Worth;
            result.Rounds = rounds;
            result.Value = (damageGain * rounds + protection - hpCost) * RoleWeight(ctx, "buff");
            result.Detail = $"form attacks {off.DamagePerRound:0.#}->{quote.Damage.Value:0.#} dmg/rd, AC {ac:+0;-0;0}, {rounds:0.#} rd; " +
                "core natural attacks only; extra form facts uncredited";
            s_LastAppraisal[AppraisalKey(ctx.Unit, ability, ally)] = $"{result.Value:0.#}: {result.Detail}";
            return result;
        }

        private static void PriceLostFormLinks(UnitEntityData unit, ShifterFormProjection form)
        {
            // Existing native stat links are read once/round. Price losses from disabled armor/suppressed buffs;
            // new form-only features, recovered penalties and positive derivative changes receive no extra credit.
            foreach (var stat in FormAttributes)
            {
                int loss = -form.ModifierChange(stat);
                if (loss <= 0) continue;
                var use = UnitStatUse(unit, stat);
                form.DerivedAttackLoss += (int)Math.Ceiling(loss * use.Attack);
                form.DerivedDamageLoss += (int)Math.Ceiling(loss * use.Damage);
                form.DerivedArmorLoss += (int)Math.Ceiling(loss * Math.Max(0f, use.Armor - (stat == StatType.Dexterity ? 1f : 0f)));
                form.SaveLoss += loss * use.Saves;
                form.HpLoss += loss * use.HitPoints;
            }
        }

        internal static bool IsShifterFury(ActivatableAbility toggle) =>
            toggle?.Blueprint?.GetComponent<ShiftersFuryItemAbility>() != null;

        private static bool KnownFormBranch(EffectApplies applies) =>
            applies == EffectApplies.Unconditional || applies == EffectApplies.Yes;

        private static bool FurySelectionPresent(ShiftersFuryPart part)
        {
            if (part == null) return false;
            if (part.AppliedFacts.Count > 16) return true; // outside the bounded selector contract
            // Includes pending on/started choices before their native buff data becomes available.
            foreach (var choice in part.AppliedFacts)
                if (choice != null && (choice.IsOn || choice.IsStarted)) return true;
            return false;
        }

        private static bool ValidFuryChoice(UnitEntityData unit, ActivatableAbility toggle, out ShiftersFuryItemData data)
        {
            data = null;
            var part = unit?.Get<ShiftersFuryPart>();
            if (part == null || FurySelectionPresent(part) || !IsShifterFury(toggle) || !part.AppliedFacts.Contains(toggle) ||
                toggle.IsOn || toggle.IsStarted || !toggle.IsAvailable) return false;
            var bp = toggle.Blueprint;
            if (bp.ActivationType != AbilityActivationType.Immediately || bp.ActivateWithUnitCommand || bp.IsTargeted || bp.Group != ActivatableAbilityGroup.None ||
                bp.ComponentsArray.Length != 1 || !(bp.ComponentsArray[0] is ShiftersFuryItemAbility)) return false;
            data = toggle.GetData<ShiftersFuryItemAbility, ShiftersFuryItemData>();
            if (data?.Weapon?.Blueprint?.IsNatural != true || data.Weapon.Blueprint.IsUnarmed) return false;
            // Native choices are regenerated on equipment/polymorph changes. Verify the actual weapon/hand identity.
            switch (data.Hand)
            {
                case ShiftersFury.HandType.PrimaryHand: return unit.Body.PrimaryHand?.MaybeWeapon == data.Weapon;
                case ShiftersFury.HandType.SecondaryHand: return unit.Body.SecondaryHand?.MaybeWeapon == data.Weapon;
                default:
                    var selected = data.Weapon;
                    return unit.Body.AdditionalLimbs.Any(l => l?.MaybeWeapon == selected);
            }
        }

        private static void AddShifterFuryCandidates(TacticalContext ctx, List<Candidate> list)
        {
            if (!(ModSettings.Instance?.AiManagesToggles ?? true) || PureMagePolicy.IsActive(ctx.Settings)) return;
            var unit = ctx.Unit;
            var part = unit.Get<ShiftersFuryPart>();
            // Preserve an existing selection, including the player's pre-fight choice. Restore only AI-enabled off->on
            // selections: the existing GUID-level lock/restore turns all generated children back off, never guesses
            // which of the same-GUID weapon instances was previously selected.
            if (part == null || part.AppliedFacts.Count == 0 || FurySelectionPresent(part)) return;
            if (!unit.Body.IsPolymorphed && list.Any(c => c.Ability != null && c.Value > 0f &&
                PolymorphFacts.GivesShifterForm(AbilityProfile.Get(c.Ability)))) return;
            var off = OffenceOf(ctx, unit);
            var enemy = off.Enemy;
            if (enemy?.View == null || off.Weapon == null || unit.View == null ||
                !InRangeNow(unit, enemy.Position, UnitAttack.GetApproachRadius(off.Weapon, unit, enemy), true)) return;
            var seen = new HashSet<string>();
            int quoted = 0;
            var decision = ShifterDecisions.GetValue(ctx, _ => new ShifterDecision());
            foreach (var toggle in part.AppliedFacts)
            {
                if (CombatMemory.PlayerOwnsToggle(unit, toggle) || CombatMemory.ToggleSwitchedThisRound(unit, toggle) ||
                    UserAbilityPolicy.SourceOf(ctx.Blocks, toggle) != BlockSource.None || !ValidFuryChoice(unit, toggle, out var data)) continue;
                string kind = $"{data.Weapon.Blueprint.AssetGuid}|{data.Hand}";
                if (!seen.Add(kind)) continue;
                if (quoted++ >= 4) break;
                float? damage = CombatMath.ShifterFuryDamage(unit, enemy, data, ShifterTransfers(ctx, decision));
                if (!damage.HasValue) continue;
                float gain = damage.Value - off.DamagePerRound;
                if (gain <= 0f) continue;
                list.Add(new Candidate
                {
                    Kind = CandidateKind.Toggle, Slot = ActionSlot.Free, Toggle = toggle, ShifterFury = true,
                    Value = gain * Math.Max(1f, ctx.ExpectedRounds) * RoleWeight(ctx, "damage"), PureBuff = true,
                    Key = $"fury:{data.Weapon.UniqueId}",
                    Label = $"Shifter's Fury: {data.Weapon.Name} ({off.DamagePerRound:0.#}->{damage.Value:0.#} dmg/rd)"
                });
            }
        }

        private static bool ValidateShifterIssue(UnitEntityData unit, Candidate candidate, out string failure)
        {
            failure = null;
            if (candidate.ShifterFury)
            {
                if (!(ModSettings.Instance?.AiManagesToggles ?? true) || PureMagePolicy.IsActive(unit) ||
                    !(unit.CombatState?.IsInCombat ?? false) ||
                    CombatMemory.PlayerOwnsToggle(unit, candidate.Toggle) || CombatMemory.ToggleSwitchedThisRound(unit, candidate.Toggle) ||
                    !ValidFuryChoice(unit, candidate.Toggle, out _)) failure = "native Shifter's Fury selection changed";
            }
            if (candidate.Ability != null)
            {
                var profile = AbilityProfile.Get(candidate.Ability);
                bool selfForm = profile != null && (profile.CasterBuffs.Any(b => PolymorphFacts.Form(b)?.AllowDamageTransfer == true) ||
                    candidate.Target == unit && profile.TargetBuffs.Any(b => PolymorphFacts.Form(b)?.AllowDamageTransfer == true));
                if (selfForm && (PolymorphFacts.HasForm(unit) || FurySelectionPresent(unit.Get<ShiftersFuryPart>()) ||
                    !(unit.CombatState?.IsInCombat ?? false))) failure = "major form is already present, Fury selected or combat has ended";
            }
            return failure == null;
        }

        private static bool ExecuteShifterFury(UnitEntityData unit, Candidate candidate, out UnitCommand command, out string failure)
        {
            command = null;
            failure = null;
            var toggle = candidate.Toggle;
            if (!ValidFuryChoice(unit, toggle, out var data)) { failure = "native Fury choice changed before activation"; return false; }
            // Record before entering a native setter that can partially succeed/throw. The original selection is
            // known to be all-off; failed immediate activations are rolled back and cannot be retried this round.
            CombatMemory.RecordToggle(unit, toggle, true, false);
            try
            {
                toggle.IsOn = true; // native generated choice, exactly as the action bar; no fake buff activation
                if (!toggle.IsOn || unit.Get<ShiftersFuryPart>()?.IsFuryWeapon(data.Weapon) != true)
                {
                    if (toggle.IsOn) toggle.IsOn = false;
                    failure = "native Shifter's Fury did not select the priced weapon";
                    return false;
                }
                CastOutcome.Forget(unit);
                Main.Log($"[Shifter] {unit.CharacterName}: {candidate.Label} = {candidate.Value:0.#}");
                return true;
            }
            catch (Exception ex)
            {
                try { if (toggle.IsOn) toggle.IsOn = false; }
                catch (Exception rollback) { Main.Verbose($"[Shifter] Fury rollback: {rollback.Message}"); }
                failure = ex.Message; return false;
            }
            finally { MagusStateChanged(); } // actual/partial native switches invalidate attack, appraisal and context memos
        }
    }
}
