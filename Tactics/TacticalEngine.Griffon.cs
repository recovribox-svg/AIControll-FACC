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
            bool full = CombatMath.GriffonCanPounce(unit, ctx.CanFullAttack);
            var candidates = new List<Candidate>();
            bool needLos = new UnitUseAbility(ability, new Kingmaker.Utility.TargetWrapper(unit)).NeedLoS;
            var budget = new GriffonQueryBudget();
            var targets = onlyTarget == null ? ctx.Enemies.OrderBy(ctx.Distance) : Enumerable.Repeat(onlyTarget, 1);
            foreach (var enemy in targets)
            {
                if (enemy?.View == null || !unit.IsEnemy(enemy) || !enemy.State.IsConscious ||
                    !ability.CanTarget(new Kingmaker.Utility.TargetWrapper(enemy))) continue;
                float radius = ability.GetApproachDistance(enemy);
                if (!InRangeNow(unit, enemy.Position, radius, needLos)) continue;
                if (!budget.TryLanding()) break;
                if (!CombatMath.TryGriffonLanding(unit, enemy, out var landing)) continue;
                var plan = new GriffonPlan { From = unit.Position, TargetAt = enemy.Position, Landing = landing, Charge = charge,
                    Full = full, RealTime = CombatMemory.IsRealTime, Bodies = BodiesFor(ctx), Enemies = ctx.Enemies,
                    SwarmRecipients = SwarmTargets(ctx) };
                if (!GriffonLandingClear(unit, enemy, plan, freshBodies: false)) continue;
                if (!budget.TryWeaponQuote()) break;
                var attack = CombatMath.EstimateGriffonAttack(unit, enemy, full, charge);
                if (attack == null || !(attack.ExpectedDamage > 0f) || !CombatMath.GriffonLandingReaches(unit, enemy, landing, attack.Reach)) continue;
                plan.Reach = attack.Reach;
                float value = DamageValue(enemy, attack) * AttackWeight(ctx, enemy, false) * WakeFactor(enemy, attack.ExpectedDamage);
                // The native flight suppresses traversal AoOs. Its landing still risks the same exposure and loss
                // of cover for allies as other relocations; the real charge child's temporary AC penalty is a cost.
                float exposure = ExposureCost(ctx, landing);
                float leaves = ValueModel.LeavesAlliesCost(PartyShift(ctx, landing), OwnFall(ctx, landing));
                float armor = Math.Min(0f, DefenseValue(ctx, landing, charge.Armor));
                value = value - exposure - leaves + armor;
                candidates.Add(new Candidate { Kind = CandidateKind.Ability, Slot = slot, Ability = ability, Target = enemy,
                    Offensive = true, Value = value, Griffon = plan, ApproachRadius = radius, NeedLoS = needLos,
                    Key = $"griffon:{CombatMemory.AbilityKey(ability)}:{enemy.UniqueId}",
                    Label = $"{ability.Name}->{enemy.CharacterName} (native leap, {(full ? attack.Attacks + " pounce attacks" : "one attack")}" +
                        (exposure + leaves - armor > 0f ? $", landing cost {exposure + leaves - armor:0.#}" : "") + ")" });
            }
            if (candidates.Count == 0)
                ReportOnce($"griffon-no-landing|{CombatMemory.Fight}|{unit.UniqueId}|{CombatMemory.AbilityKey(ability)}",
                    () => $"[Griffon] {unit.CharacterName}: {ability.Name} has no current eligible safe landing/weapon attack " +
                        $"({budget.LandingAttempts}/{GriffonQueryBudget.MaxLandingAttempts} landings, " +
                        $"{budget.WeaponQuotes}/{GriffonQueryBudget.MaxWeaponQuotes} native weapon quotes)");
            return candidates;
        }

        private static bool GriffonLandingClear(UnitEntityData unit, UnitEntityData target, GriffonPlan plan, bool freshBodies)
        {
            var at = plan.Landing;
            // Check the endpoint only: neither a line across Grease nor a normal navmesh route is the custom flight.
            if (AoeDangerAnalyzer.IsPositionDangerous(unit, at) || AoeDangerAnalyzer.EnteringDanger(unit, at, at, out _) > 0f) return false;
            // The native nearest-node snap can move its ideal body-touch landing inward. Weapon reach alone is
            // an upper distance bound; apply the same existing body-overlap rule to the actual target too.
            if (Math.Abs(target.Position.y - at.y) <= SpotRules.SameFloor &&
                SpotRules.Takes(target.Position.x, target.Position.z, target.Corpulence, at.x, at.z, unit.Corpulence)) return false;
            foreach (var body in plan.Bodies)
            {
                var other = body.Unit;
                if (other == null || other == target || other == unit || other.State.IsDead) continue;
                Vector3 position = freshBodies ? (CombatMemory.IsRealTime ? WalkClaim(other) : null) ?? other.Position : body.At;
                float radius = freshBodies ? other.Corpulence : body.Radius;
                if (Math.Abs(position.y - at.y) <= SpotRules.SameFloor &&
                    SpotRules.Takes(position.x, position.z, radius, at.x, at.z, unit.Corpulence)) return false;
            }
            foreach (var enemy in plan.Enemies)
            {
                if (enemy == null || CombatMath.IsDisabled(enemy)) continue;
                var swarm = CombatMath.SwarmSpaceOf(enemy);
                if (swarm != null && CombatMath.SwarmStrikes(enemy, swarm, unit, at, null, plan.SwarmRecipients)) return false;
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
                !CombatMath.GriffonLandingReaches(unit, target, landing, plan.Reach) || !GriffonLandingClear(unit, target, plan, freshBodies: true))
                failure = "native leap landing changed, blocked, dangerous or outside the queued attack's reach";
            else if (!CombatMath.TryGriffonCharge(ability, out var charge) || charge.Buff != plan.Charge.Buff ||
                charge.Attack != plan.Charge.Attack || charge.Armor != plan.Charge.Armor ||
                plan.Full && !CombatMath.GriffonCanPounce(unit, unit.HasFullRoundAction() && !unit.UsedOneMoveAction() && !unit.IsMoveActionRestricted()))
                failure = "native charge effect or pounce state changed";
            return failure == null;
        }
    }
}
