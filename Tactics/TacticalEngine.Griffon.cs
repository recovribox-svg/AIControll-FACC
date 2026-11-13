// ★ v0.3.50 review2: Native Death from Above: one custom leap, a fixed native landing, then the game's own queued charge attack.
using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private enum GriffonRefusal { None, NativeTarget, RangeOrSight, Endpoint, Hazard, TargetBody, OtherBody, Swarm, Weapon, WeaponReach }

        internal sealed class GriffonPlan
        {
            internal Vector3 From, TargetAt, Landing;
            internal AbilityProfile.GriffonChargeEffect Charge;
            internal float Reach;
            internal bool Full, RealTime;
            internal List<Body> Bodies;
            internal List<UnitEntityData> Enemies, SwarmRecipients;
        }

        private static IEnumerable<Candidate> EvaluateGriffonAttack(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
                                                                    UnitEntityData onlyTarget)
        {
            var unit = ctx.Unit;
            var none = Enumerable.Empty<Candidate>();
            if (profile.GriffonAttack == null || ability.Spellbook != null || ability.SourceItem != null ||
                ability.MagicHackData != null || ability.ShadowSpellSettings != null || ConsumablePolicy.SpentMaterial(ability) != null ||
                CastOutcome.HeldChargeOf(unit) != null || ArmedRangedSpell(unit) != null ||
                !ctx.Permissions.CanWalk || ctx.Permissions.MovementOnly || unit.RiderPart != null || unit.SaddledPart != null ||
                !unit.IsInCombat || !ability.IsAvailable) return none;
            var slot = SlotOf(ability);
            // The native component makes this full-round in TB, standard in RT. Uninspected action overrides do not
            // turn the dedicated leap into a free preparation or an ordinary walking attack.
            if (slot != (CombatMemory.IsRealTime ? ActionSlot.Standard : ActionSlot.FullRound) ||
                slot == ActionSlot.FullRound && !ctx.HasFullRound || slot == ActionSlot.Standard && !ctx.HasStandard) return none;
            if (!CombatMath.TryGriffonCharge(ability, out var charge))
            {
                ReportOnce($"griffon-charge|{CombatMemory.Fight}|{unit.UniqueId}|{CombatMemory.AbilityKey(ability)}",
                    () => $"[Griffon] {unit.CharacterName}: {ability.Name} withheld: native charge effect, current stealth/charge, mount or immunity state not supported");
                return none;
            }
            bool full = CombatMath.GriffonCanPounce(unit);
            var candidates = new List<Candidate>();
            bool needLos = new UnitUseAbility(ability, new Kingmaker.Utility.TargetWrapper(unit)).NeedLoS;
            var budget = new GriffonQueryBudget();
            int nativeRefused = 0, rangeRefused = 0;
            var firstRefusal = GriffonRefusal.None;
            GriffonPlan witness = null;
            UnitEntityData witnessTarget = null, witnessBlocker = null;
            void Refused(GriffonRefusal reason, UnitEntityData enemy, GriffonPlan plan, UnitEntityData blocker = null)
            {
                if (firstRefusal != GriffonRefusal.None) return;
                firstRefusal = reason; witnessTarget = enemy; witness = plan; witnessBlocker = blocker;
            }
            var targets = onlyTarget == null ? ctx.Enemies.OrderBy(ctx.Distance) : Enumerable.Repeat(onlyTarget, 1);
            foreach (var enemy in targets)
            {
                if (enemy?.View == null || !unit.IsEnemy(enemy) || !enemy.State.IsConscious) continue;
                if (!ability.CanTarget(new Kingmaker.Utility.TargetWrapper(enemy))) { nativeRefused++; continue; }
                float radius = ability.GetApproachDistance(enemy);
                if (!InRangeNow(unit, enemy.Position, radius, needLos)) { rangeRefused++; continue; }
                if (!budget.TryLanding()) break;
                if (!CombatMath.TryGriffonLanding(unit, enemy, out var landing))
                { Refused(GriffonRefusal.Endpoint, enemy, null); continue; }
                var plan = new GriffonPlan { From = unit.Position, TargetAt = enemy.Position, Landing = landing, Charge = charge,
                    Full = full, RealTime = CombatMemory.IsRealTime, Bodies = BodiesFor(ctx), Enemies = ctx.Enemies,
                    SwarmRecipients = SwarmTargets(ctx) };
                if (!GriffonLandingClear(unit, enemy, plan, freshBodies: false, out var refusal, out var blocker))
                { Refused(refusal, enemy, plan, blocker); continue; }
                if (!budget.TryWeaponQuote()) break;
                var attack = CombatMath.EstimateGriffonAttack(unit, enemy, full, charge);
                if (attack == null || !(attack.ExpectedDamage > 0f)) { Refused(GriffonRefusal.Weapon, enemy, plan); continue; }
                if (!CombatMath.GriffonLandingReaches(unit, enemy, landing, attack.Reach))
                { Refused(GriffonRefusal.WeaponReach, enemy, plan); continue; }
                plan.Reach = attack.Reach;
                float factor = AttackWeight(ctx, enemy, false) * WakeFactor(enemy, attack.ExpectedDamage);
                float value = DamageValue(enemy, attack) * factor;
                // The native flight suppresses traversal AoOs. Its landing still risks the same exposure and loss
                // of cover for allies as other relocations; the real charge child's temporary AC penalty is a cost.
                float exposure = ExposureCost(ctx, landing);
                float leaves = ValueModel.LeavesAlliesCost(PartyShift(ctx, landing), OwnFall(ctx, landing));
                float armor = Math.Min(0f, DefenseValue(ctx, landing, charge.Armor));
                value = value - exposure - leaves + armor;
                var candidate = new Candidate { Kind = CandidateKind.Ability, Slot = slot, Ability = ability, Target = enemy,
                    Offensive = true, Value = value, Griffon = plan, ApproachRadius = radius, NeedLoS = needLos,
                    ModeAt = landing, ModeFactor = factor,
                    Key = $"griffon:{CombatMemory.AbilityKey(ability)}:{enemy.UniqueId}",
                    Label = $"{ability.Name}->{enemy.CharacterName} (native leap, {(full ? attack.Attacks + " pounce attacks" : "one attack")}" +
                        (exposure + leaves - armor > 0f ? $", landing cost {exposure + leaves - armor:0.#}" : "") + ")" };
                // ★ v0.3.52: compare the same owned/allowed attack modes as an ordinary attack, while its exact
                // routine is still cached. Mode sets reprice lines only; no extra node/path/own-weapon quote.
                float originalValue = candidate.Value;
                string originalLabel = candidate.Label;
                try { ChooseAttackModes(ctx, candidate, attack); }
                catch (Exception ex)
                {
                    candidate.Value = originalValue; candidate.Label = originalLabel; candidate.Modes = null;
                    ReportOnce($"griffon-modes|{CombatMemory.Fight}|{unit.UniqueId}|{ex.GetType().Name}",
                        () => $"[Griffon] {unit.CharacterName}: mode appraisal unavailable ({ex.GetType().Name}); retains current native attack estimate");
                }
                candidates.Add(candidate);
            }
            if (candidates.Count == 0)
            {
                if (firstRefusal == GriffonRefusal.None)
                    firstRefusal = rangeRefused > 0 ? GriffonRefusal.RangeOrSight : GriffonRefusal.NativeTarget;
                // Bounded shared ReportOnce memo; distinguish changed refusal stages within the fight. Build the
                // witness text lazily from results already read. Never re-query a node, path, target or weapon for logs.
                ReportOnce($"griffon-no-landing|{CombatMemory.Fight}|{unit.UniqueId}|{CombatMemory.AbilityKey(ability)}|{firstRefusal}",
                    () => $"[Griffon] {unit.CharacterName}: {ability.Name} found no safe attack within budget; first refusal={firstRefusal}; " +
                        $"native target refusals={nativeRefused}, range/sight refusals={rangeRefused}; " +
                        $"({budget.LandingAttempts}/{GriffonQueryBudget.MaxLandingAttempts} landings, " +
                        $"{budget.WeaponQuotes}/{GriffonQueryBudget.MaxWeaponQuotes} native weapon quotes); " +
                        $"ability={CombatMemory.AbilityKey(ability)}, target={witnessTarget?.UniqueId ?? "none"}" +
                        (witness == null ? "" : $", from={witness.From}, targetAt={witness.TargetAt}, landing={witness.Landing}") +
                        (witnessBlocker == null ? "" : $", blocker={witnessBlocker.UniqueId}"));
            }
            else
                ReportOnce($"griffon-admitted|{CombatMemory.Fight}|{unit.UniqueId}|{CombatMemory.AbilityKey(ability)}",
                    () => $"[Griffon] {unit.CharacterName}: {ability.Name} admitted {candidates.Count} safe attack(s); " +
                        $"sample={candidates[0].Label}, value={candidates[0].Value:0.#}; ability={CombatMemory.AbilityKey(ability)}");
            return candidates;
        }

        private static bool GriffonLandingClear(UnitEntityData unit, UnitEntityData target, GriffonPlan plan, bool freshBodies,
                                                out GriffonRefusal refusal, out UnitEntityData blocker)
        {
            refusal = GriffonRefusal.None; blocker = null;
            var at = plan.Landing;
            // Check the endpoint only: neither a line across Grease nor a normal navmesh route is the custom flight.
            if (AoeDangerAnalyzer.IsPositionDangerous(unit, at) || AoeDangerAnalyzer.EnteringDanger(unit, at, at, out _) > 0f)
            { refusal = GriffonRefusal.Hazard; return false; }
            // The native nearest-node snap can move its ideal body-touch landing inward. Weapon reach alone is
            // an upper distance bound; apply the same existing body-overlap rule to the actual target too.
            if (Math.Abs(target.Position.y - at.y) <= SpotRules.SameFloor &&
                SpotRules.Takes(target.Position.x, target.Position.z, target.Corpulence, at.x, at.z, unit.Corpulence))
            { refusal = GriffonRefusal.TargetBody; blocker = target; return false; }
            foreach (var body in plan.Bodies)
            {
                var other = body.Unit;
                if (other == null || other == target || other == unit || other.State.IsDead) continue;
                Vector3 position = freshBodies ? (CombatMemory.IsRealTime ? WalkClaim(other) : null) ?? other.Position : body.At;
                float radius = freshBodies ? other.Corpulence : body.Radius;
                if (Math.Abs(position.y - at.y) <= SpotRules.SameFloor &&
                    SpotRules.Takes(position.x, position.z, radius, at.x, at.z, unit.Corpulence))
                { refusal = GriffonRefusal.OtherBody; blocker = other; return false; }
            }
            foreach (var enemy in plan.Enemies)
            {
                if (enemy == null || CombatMath.IsDisabled(enemy)) continue;
                var swarm = CombatMath.SwarmSpaceOf(enemy);
                if (swarm != null && CombatMath.SwarmStrikes(enemy, swarm, unit, at, null, plan.SwarmRecipients))
                { refusal = GriffonRefusal.Swarm; blocker = enemy; return false; }
            }
            return true;
        }

        private static bool ValidateGriffonIssue(UnitEntityData unit, Candidate candidate, out string failure)
        {
            failure = null;
            var plan = candidate.Griffon;
            if (plan == null) return true;
            var ability = candidate.Ability;
            var target = candidate.Target;
            if (candidate.Kind != CandidateKind.Ability || candidate.NeedsMove || candidate.MoveFirst || ability == null || target == null ||
                ability.Caster?.Unit != unit || AbilityProfile.Get(ability)?.GriffonAttack == null ||
                CastOutcome.HeldChargeOf(unit) != null || ArmedRangedSpell(unit) != null ||
                CombatMemory.IsRealTime != plan.RealTime || SlotOf(ability) != candidate.Slot ||
                !unit.IsInCombat || !ability.IsAvailable || !unit.State.CanMove || !target.State.IsConscious ||
                !unit.IsEnemy(target) || !ability.CanTarget(new Kingmaker.Utility.TargetWrapper(target)) ||
                candidate.Slot == ActionSlot.FullRound && !unit.HasFullRoundAction() ||
                candidate.Slot == ActionSlot.Standard && !unit.HasStandardAction()) failure = "native leap ability, target, mode or action state changed";
            else if (unit.Position != plan.From || target.Position != plan.TargetAt ||
                !InRangeNow(unit, target.Position, ability.GetApproachDistance(target), candidate.NeedLoS) ||
                !CombatMath.TryGriffonLanding(unit, target, out var landing) || landing != plan.Landing ||
                !CombatMath.GriffonLandingReaches(unit, target, landing, plan.Reach) || !GriffonLandingClear(unit, target, plan, freshBodies: true, out _, out _))
                failure = "native leap landing changed, blocked, dangerous or outside the queued attack's reach";
            else if (!CombatMath.TryGriffonCharge(ability, out var charge) || charge.Buff != plan.Charge.Buff ||
                charge.Attack != plan.Charge.Attack || charge.Armor != plan.Charge.Armor ||
                plan.Full && !CombatMath.GriffonCanPounce(unit))
                failure = "native charge effect or pounce state changed";
            return failure == null;
        }
    }
}
