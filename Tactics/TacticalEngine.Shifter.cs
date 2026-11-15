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
            if (ally != ctx.Unit) return result;
            if (PolymorphFacts.HasForm(ally)) { result.Detail = "existing/suppressed form retained"; return FinishFormAppraisal(ctx, ability, ally, result); }
            if (PureMagePolicy.IsActive(ctx.Settings)) { result.Detail = "Pure Mage: physical form preparation withheld"; return FinishFormAppraisal(ctx, ability, ally, result); }
            if (FurySelectionPresent(ally.Get<ShiftersFuryPart>())) { result.Detail = "current/pending Fury selection retained"; return FinishFormAppraisal(ctx, ability, ally, result); }
            var forms = buffs.Where(b => PolymorphFacts.Form(b) != null &&
                KnownFormBranch(profile.BuffAppliesTo(b, ability, ally))).Distinct().ToList();
            // A branch whose native condition cannot be evaluated is not permission to forecast all alternative forms.
            if (forms.Count != 1) { result.Detail = "major form branch is not uniquely known"; return FinishFormAppraisal(ctx, ability, ally, result); }
            var buff = forms[0];
            var form = PolymorphFacts.Form(buff);
            if (!ShifterFormProjection.Supports(form)) { result.Detail = "form layout outside the supported natural-weapon projection"; return FinishFormAppraisal(ctx, ability, ally, result); }
            var off = OffenceOf(ctx, ally);
            if (off.Caster) { result.Detail = "spellcasting cost of a new form is not modelled"; return FinishFormAppraisal(ctx, ability, ally, result); }
            if (!TryShifterReference(off, out var enemy, out var baseline))
            { result.Detail = "current native weapon quote unavailable; no paired form comparison"; return FinishFormAppraisal(ctx, ability, ally, result); }
            var decision = ShifterDecisions.GetValue(ctx, _ => new ShifterDecision());
            if (!decision.Forms.TryGetValue(buff, out var quote))
            {
                // No cross-target search. Repeated duration/ability variants share one shape/nearest-enemy quote.
                if (decision.Forms.Count >= 8) { result.Detail = "major form quote budget reached"; return FinishFormAppraisal(ctx, ability, ally, result); }
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
            if (!quote.Damage.HasValue) { result.Detail = "native form quote unavailable"; return FinishFormAppraisal(ctx, ability, ally, result); }
            var source = CombatMath.CreateContext(ability, new TargetWrapper(ally));
            float duration = EffectiveRounds(ability, profile, buff, source);
            float rounds = ValueModel.UsefulRounds(duration, ctx.ExpectedRounds, Math.Min(3f, ctx.ExpectedRounds));
            // ★ shifter-forms (§174): a shift that takes the turn's Standard or full-round action leaves no attack this
            // round, so the form's attacks begin next round; its defence counts from now. The best main action it gives
            // up this turn is the competing candidate itself.
            var slot = SlotOf(ability);
            bool takesTurnAttack = slot == ActionSlot.Standard || slot == ActionSlot.FullRound;
            float attackRounds = ValueModel.FormAttackRounds(rounds, takesTurnAttack);
            bool ranged = form.AdditionalLimbs.First().IsRanged;
            float damageGain = quote.Damage.Value * PreferenceWeight(ctx, ranged) - baseline.ExpectedDamage * PreferenceWeight(ctx, baseline.IsRanged);
            var def = DefenceOf(ctx, ally);
            if (quote.Projection.HpLoss >= def.Hp) { result.Detail = "form would remove its remaining hit points"; return FinishFormAppraisal(ctx, ability, ally, result); }
            int ac = quote.Projection.ArmorChange;
            float kept = ValueModel.ArmorGain(def.Incoming, def.EnemyHitChance, ac) - def.Incoming * 0.025f * quote.Projection.SaveLoss;
            float protection = ValueModel.PreventionValue(kept, rounds, def.Hp,
                def.Incoming + CombatMath.AttachedDamageOf(ctx, ally).NextRisk, def.Worth);
            float risk = def.Incoming + CombatMath.AttachedDamageOf(ctx, ally).NextRisk;
            float hpCost = Math.Min(1f, Math.Max(0f, RiskMath.RoundDropChance(def.Hp - quote.Projection.HpLoss, risk) -
                RiskMath.RoundDropChance(def.Hp, risk)) * rounds) * def.Worth;
            result.Rounds = rounds;
            result.Value = (damageGain * attackRounds + protection - hpCost) * RoleWeight(ctx, "buff");
            result.Detail = $"form attacks {baseline.ExpectedDamage:0.#}->{quote.Damage.Value:0.#} dmg/rd vs {enemy.CharacterName} for " +
                $"{attackRounds:0.#} rd{(takesTurnAttack ? " from next round" : "")}, AC {ac:+0;-0;0} for {rounds:0.#} rd; " +
                "core natural attacks only; extra form facts uncredited";
            return FinishFormAppraisal(ctx, ability, ally, result);
        }

        private static bool TryShifterReference(Offence off, out UnitEntityData target, out WeaponAttackEstimate baseline)
        {
            target = off?.WeaponTarget;
            baseline = off?.WeaponQuote;
            // Zero is a valid native result (e.g. immunity), not permission to switch to another enemy.
            // An unreadable current routine stays unknown instead of becoming an invented zero baseline.
            return !ReferenceEquals(target, null) && baseline != null;
        }

        private static BuffAppraisal FinishFormAppraisal(TacticalContext ctx, AbilityData ability, UnitEntityData ally, BuffAppraisal result)
        {
            s_LastAppraisal[AppraisalKey(ctx.Unit, ability, ally)] = $"{result.Value:0.#}: {result.Detail}";
            // One row per ability/unit/fight, including a zero quote. Without it, a withheld branch/unsupported
            // body and an unprofitable but successful quote both looked like "never uses the form". No new rules,
            // contexts, target scans or extra forecast search.
            // ★ shifter-forms (§174): and again, at most MaxFormNoteRepeats times a fight, when the appraisal turns or
            // moves materially (ShifterRules.FormNoteChanged). fusion2g1b RT1: Griffon Shape was the best option at 34.9,
            // then vanished from the next decision's list after Griffon Aspect, and the log could not say why.
            string key = $"{ctx.Unit.UniqueId}|{ability.Blueprint.AssetGuid}";
            if (!FormNotes.TryGetValue(key, out var note) || note.Fight != CombatMemory.Fight)
            {
                if (FormNotes.Count > 64) FormNotes.Clear();
                FormNotes[key] = new FormNote { Fight = CombatMemory.Fight, Value = result.Value };
                Main.Log($"[Shifter] {ctx.Unit.CharacterName}: {ability.Name} form appraisal {result.Value:0.#}: {result.Detail}");
            }
            else if (note.Repeats < ShifterRules.MaxFormNoteRepeats && ShifterRules.FormNoteChanged(note.Value, result.Value))
            {
                Main.Log($"[Shifter] {ctx.Unit.CharacterName}: {ability.Name} form appraisal now {result.Value:0.#} (was {note.Value:0.#}, " +
                         $"round {CombatMemory.Round}): {result.Detail}");
                note.Value = result.Value;
                note.Repeats++;
            }
            return result;
        }

        /// <summary>★ shifter-forms (§174): the last logged form appraisal per unit and form ability, for this fight only.</summary>
        private sealed class FormNote { internal int Fight; internal float Value; internal int Repeats; }
        private static readonly Dictionary<string, FormNote> FormNotes = new Dictionary<string, FormNote>();

        /// <summary>★ shifter-forms (§174): a candidate that casts a new Shifter-style major form.</summary>
        private static bool IsNewShifterForm(Candidate c) => c != null && c.Kind == CandidateKind.Ability && c.Ability != null &&
            PolymorphFacts.GivesShifterForm(AbilityProfile.Get(c.Ability));

        /// <summary>
        /// ★ shifter-forms (§174): the turn's quick action when it is a Shifter's Fury selection in a human body. Choosing
        /// the Fury there withholds a new major form for the rest of the fight (AppraiseShifterForm), so the Fury waits
        /// only while the form is this turn's main action and beats the immediate action plus the Fury's own gain this
        /// round (ValueModel.HoldFuryForForm); the next quick action, if any, is taken instead. Before, any positive form
        /// kept the Fury off, even one that lost to the full attack: fusion2g1b fight 3 R1, Griffon Shape (22.8) lost to the
        /// full attack on the Large Air Elemental in reach (28.4, then 35.9 after Griffon Aspect) and Wenduag attacked
        /// without the Fury, which was worth 51-173 in her other fights. She selected no Fury in that fight.
        /// </summary>
        private static Candidate HoldFuryForForm(TacticalContext ctx, List<Candidate> usable, Candidate fury, Candidate main, bool preview)
        {
            if (fury == null || !fury.ShifterFury || ctx.Unit.Body.IsPolymorphed || !IsNewShifterForm(main)) return fury;
            var immediate = usable.FirstOrDefault(c => c != main && !c.PureBuff && !QuickAction(c) && c.Value >= MinMainValue);
            float furyRound = fury.Value / Math.Max(1f, ctx.ExpectedRounds);
            if (!ValueModel.HoldFuryForForm(main.Value, immediate?.Value ?? 0f, furyRound)) return fury;
            if (!preview)
                ReportOnce($"furyhold|{ctx.Unit.UniqueId}|{CombatMemory.Round}",
                           $"[Shifter] {ctx.Unit.CharacterName}: {fury.Label} waits - {main.Label} ({main.Value:0.#}) is this turn's main action " +
                           $"over {(immediate?.Label ?? "no immediate action")} ({(immediate?.Value ?? 0f):0.#}) and the Fury's {furyRound:0.#} this round");
            return usable.FirstOrDefault(c => c != fury && !c.ShifterFury && QuickAction(c) && c.Value >= QuickBar(c));
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
            // ★ shifter-forms (§174): a positive major form no longer keeps the Fury off here. Decide holds the Fury only
            // while the form is the turn's main action (HoldFuryForForm).
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
