using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using Kingmaker.View;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private const int MaxTeleportTravellers = 4;
        private const int MaxTeleportGroup = 16;
        // ★ v0.3.70 playtest: the safety loops are cheap (at most four travellers against the units in the fight); the one
        // costly step is the single native relaxation. A cap of 24 withheld every teleport in the mastermind save, whose
        // fights had 17-24 party units and summons and 8-13 enemies.
        private const int MaxTeleportCombatants = 96;
        private static readonly MethodInfo DoorTargets = HarmonyLib.AccessTools.Method(typeof(AbilityCustomDimensionDoor), "GetTargets");
        private static readonly MethodInfo DoorMounts = HarmonyLib.AccessTools.Method(typeof(AbilityCustomDimensionDoor), "AddMounts");

        internal sealed class TeleportPlan
        {
            internal UnitEntityData Anchor;
            internal Vector3 Origin, AnchorAt;
            internal float AnchorFacing, WeaponReach;
            internal int Frame;
            internal bool RealTime;
            internal BlueprintComponent Delivery;
            internal List<UnitEntityData> Travellers;
            internal Vector3[] Starts, Landings;
            internal IReadOnlyList<UnitEntityData> Allies, Enemies, Downed;
        }

        private static bool CanOfferTeleport(TacticalContext ctx, AbilityData ability)
        {
            return ability.SourceItem == null && !SpendsMaterial(ability) && BurnOf(ability) == 0 &&
                ctx.Permissions.CanWalk && ctx.PureMageWeapon && ctx.Unit.View != null &&
                ctx.Unit.RiderPart == null && CastOutcome.HeldChargeOf(ctx.Unit) == null &&
                ArmedRangedSpell(ctx.Unit) == null && !CombatMath.OnlyDisabledEnemiesLeft &&
                ctx.Allies.Count + ctx.Enemies.Count + ctx.DownedAllies.Count <= MaxTeleportCombatants;
        }

        private static bool TeleportSlotAvailable(UnitEntityData unit, ActionSlot slot)
        {
            switch (slot)
            {
                case ActionSlot.Free: return true;
                case ActionSlot.Swift: return unit.HasSwiftAction();
                case ActionSlot.Move: return unit.HasMoveAction() || unit.HasStandardAction();
                case ActionSlot.Standard: return unit.HasStandardAction();
                case ActionSlot.FullRound: return unit.HasFullRoundAction();
                default: return false;
            }
        }

        private static void AddTeleportApproach(TacticalContext ctx, List<Candidate> candidates, TurnMemory memory)
        {
            if (ctx.TeleportAbilities == null) return;
            var unit = ctx.Unit;
            // The usual weapon/advance scoring supplied these destinations and prices. In a decision with a
            // teleport, examine at most the existing free-side budget of aims before one native relaxation.
            var approaches = candidates.Where(c => c.TeleportReach > 0f && c.TeleportPayoff > 0f &&
                c.RiskAt.HasValue && c.Target != null && !ctx.TurnedAllies.Contains(c.Target) &&
                !CombatMath.IsDisabled(c.Target))
                .OrderByDescending(c => c.TeleportPayoff).Take(SpotRules.PricedSpots).ToArray();
            if (approaches.Length == 0)
            {
                if (ctx.Unused != null)
                    foreach (var teleport in ctx.TeleportAbilities)
                        ctx.Unused.Record(ctx.Unused.Observe(teleport), UnusedReason.NoApproach);
                return;
            }
            float fromHere = candidates.Where(c => !WalksBeforeActing(c) && c.Offensive && Affordable(ctx, c))
                .Select(c => c.Value).DefaultIfEmpty(0f).Max();

            Candidate best = null;
            Candidate bestApproach = null;
            TargetWrapper bestAim = null;
            BlueprintComponent bestDelivery = null;
            float bestUnpriced = 0f;
            int aims = 0;
            // ★ summons review: each teleport keeps the reason its last examined aim stopped at, for the [Unused] witness.
            // One past the shared aim budget is not examined in this decision.
            Dictionary<AbilityData, UnusedReason> why = null;
            if (ctx.Unused != null)
            {
                why = new Dictionary<AbilityData, UnusedReason>(ctx.TeleportAbilities.Count);
                foreach (var teleport in ctx.TeleportAbilities) why[teleport] = UnusedReason.ScoreNotNeeded;
            }
            foreach (var ability in ctx.TeleportAbilities.OrderBy(SlotOf))
            {
                if (why != null) why[ability] = UnusedReason.NoGain;
                var slot = SlotOf(ability);
                bool nextAttackNow = (slot == ActionSlot.Free || slot == ActionSlot.Swift ||
                                      slot == ActionSlot.Move && unit.HasMoveAction()) && ctx.HasStandard;
                var delivery = AbilityProfile.Get(ability).TeleportDelivery;
                float reserve = CostsNothing(ability) ? 1f : 1f - ValueModel.LimitedUseDiscount(ctx.Difficulty);
                bool? needLos = null;
                foreach (var approach in approaches)
                {
                    if (aims++ >= SpotRules.PricedSpots) break;
                    float alternative = nextAttackNow ? Math.Max(fromHere, Affordable(ctx, approach) ? approach.Value : 0f) : ctx.StayValue;
                    if (approach.Kind == CandidateKind.Move && nextAttackNow)
                        alternative = Math.Max(alternative, ctx.StayValue);
                    float unpriced = ValueModel.TeleportApproachWorth(approach.TeleportPayoff, alternative, nextAttackNow, AdvanceShare) * reserve;
                    if (unpriced <= bestUnpriced) continue;

                    var enemy = approach.Target;
                    var aim = ability.CanTargetPoint ? new TargetWrapper(approach.RiskAt.Value) : new TargetWrapper(enemy);
                    if (!TryTeleportAnchor(unit, aim, out var beforeRelaxation) ||
                        HorizontalDistance(unit.Position, beforeRelaxation) < 0.5f || MovingForTeleport(enemy))
                    { if (why != null) why[ability] = UnusedReason.NoTarget; continue; }
                    if (!needLos.HasValue) needLos = new UnitUseAbility(ability, aim).NeedLoS;
                    if (!InRangeNow(unit, aim.Point, ability.GetApproachDistance(aim.Unit), needLos.Value) ||
                        !ability.CanTarget(aim))
                    { if (why != null) why[ability] = UnusedReason.NoTarget; continue; }

                    var option = new Candidate
                    {
                        Kind = CandidateKind.Ability, Slot = slot, Ability = ability, Target = aim.Unit,
                        Point = aim.Unit == null ? aim.Point : (Vector3?)null,
                        NeedLoS = needLos.Value, Key = $"teleport:{CombatMemory.AbilityKey(ability)}:{enemy.UniqueId}"
                    };
                    if (!Affordable(ctx, option) || memory.Failed.Contains(option.Key) ||
                        SilentlyFailing(ctx, option) || PolicyBlocks(ctx, option))
                    { if (why != null) why[ability] = UnusedReason.CombatPolicy; continue; }
                    best = option; bestAim = aim; bestDelivery = delivery; bestApproach = approach; bestUnpriced = unpriced;
                }
                if (aims >= SpotRules.PricedSpots) break;
            }
            if (why != null)
                foreach (var entry in why)
                    if (best == null || entry.Key != best.Ability)
                        ctx.Unused.Record(ctx.Unused.Observe(entry.Key), entry.Value);
            if (best == null) return;

            int witness = ctx.Unused?.Observe(best.Ability) ?? -1;
            var plan = QuoteTeleportLanding(ctx, bestAim, bestDelivery, bestApproach);
            if (plan == null)
            {
                ctx.Unused?.Record(witness, UnusedReason.TeleportLanding);
                return;
            }
            var abilityProfile = AbilityProfile.Get(best.Ability);
            var landing = plan.Landings[0];
            float payoff = bestUnpriced * ArcaneDeliveryChance(ctx, best.Ability, false) *
                CombatMath.CastingConcentrationChance(unit, best.Ability);
            float provoke = ProvokeLoss(ctx, best.Ability, abilityProfile, best.Slot, best.Target,
                unit.Position, payoff, out _, out _);
            float exposure = ExposureCost(ctx, landing) + ValueModel.LeavesAlliesCost(PartyShift(ctx, landing), OwnFall(ctx, landing));
            for (int i = 1; i < plan.Travellers.Count; i++)
            {
                var ally = plan.Travellers[i];
                float before = StandingDropChance(ctx, ally, plan.Starts[i], unit, plan.Origin);
                float after = StandingDropChance(ctx, ally, plan.Landings[i], unit, landing);
                exposure += Math.Max(0f, after - before) * AllyWorth(ctx, ally);
            }

            best.Value = payoff - provoke - exposure;
            best.CastRiskCost = provoke;
            if (!(best.Value > 0f))
            {
                ctx.Unused?.Record(witness, UnusedReason.NoGain);
                return;
            }

            best.Teleport = plan;
            best.Label = $"{best.Ability.Name}->{plan.Anchor.CharacterName} (teleport approach, {plan.Travellers.Count - 1} passengers, landing risk {exposure:F1})";
            candidates.Add(best);
            ctx.Unused?.Score(witness, best.Value);
        }

        // This offset is in the delivery coroutine. A unit aim uses the target's facing and both bodies;
        // a point aim uses the point as given. Calling the coroutine would actually start the teleport.
        private static bool TryTeleportAnchor(UnitEntityData caster, TargetWrapper aim, out Vector3 point)
        {
            point = aim.Point;
            var anchor = aim.Unit;
            if (anchor == null) return true;
            if (anchor.View == null || !anchor.State.IsConscious || !caster.IsEnemy(anchor)) return false;
            var offset = caster.View.Corpulence + anchor.View.Corpulence + GameConsts.MinWeaponRange.Meters;
            point += Quaternion.Euler(0f, anchor.Orientation, 0f) * Vector3.forward * offset;
            return true;
        }

        private static List<UnitEntityData> TeleportTravellers(UnitEntityData caster, BlueprintComponent delivery)
        {
            List<UnitEntityData> travellers;
            if (delivery is AbilityCustomDimensionDoor door)
            {
                if (door.Radius.Meters > 0f && (caster.Group == null || caster.Group.Count > MaxTeleportGroup)) return null;
                travellers = DoorTargets?.Invoke(door, new object[] { caster }) as List<UnitEntityData>;
            }
            else travellers = new List<UnitEntityData> { caster };
            if (travellers == null || travellers.Count == 0 || travellers.Count > MaxTeleportTravellers ||
                travellers[0] != caster || DoorMounts == null) return null;

            int count = travellers.Count;
            DoorMounts.Invoke(null, new object[] { travellers });
            // Mounted relocation has its own board/ride state. Keep the native passenger selection and
            // withhold this quote if that pair needs a mount-specific appraisal.
            if (travellers.Count != count) return null;
            foreach (var traveller in travellers)
            {
                if (traveller?.View == null || traveller.RiderPart != null || !traveller.State.IsConscious ||
                    caster.IsEnemy(traveller) || MovingForTeleport(traveller)) return null;
                var action = traveller.Commands?.Standard;
                if (traveller != caster && action?.IsRunning == true && !action.IsActed) return null;
            }
            return travellers;
        }

        private static bool MovingForTeleport(UnitEntityData unit) =>
            CombatMemory.IsRealTime && unit.View?.MovementAgent?.IsReallyMoving != false;

        private static TeleportPlan QuoteTeleportLanding(TacticalContext ctx, TargetWrapper aim, BlueprintComponent delivery,
                                                          Candidate approach)
        {
            var unit = ctx.Unit;
            var anchor = approach.Target;
            if (MovingForTeleport(anchor) || !TryTeleportAnchor(unit, aim, out var basePoint)) return null;
            var travellers = TeleportTravellers(unit, delivery);
            if (travellers == null) return null;
            var landings = new Vector3[travellers.Count];
            var starts = new Vector3[travellers.Count];
            var radii = new float[travellers.Count];
            for (int i = 0; i < travellers.Count; i++)
            {
                starts[i] = travellers[i].Position;
                landings[i] = starts[i] - unit.Position + basePoint;
                radii[i] = travellers[i].View.Corpulence;
            }
            // Exactly one such call for the chosen aim. Native RelaxPoints performs a world obstacle scan.
            if (delivery is AbilityCustomDimensionDoor)
                FreePlaceSelector.RelaxPoints(landings, radii, travellers.Count, null);

            var plan = new TeleportPlan
            {
                Anchor = anchor, Origin = unit.Position, AnchorAt = anchor.Position, AnchorFacing = anchor.Orientation,
                WeaponReach = approach.TeleportReach, Delivery = delivery, Frame = Time.frameCount,
                RealTime = CombatMemory.IsRealTime, Travellers = travellers, Starts = starts, Landings = landings,
                Allies = ctx.Allies, Enemies = ctx.Enemies, Downed = ctx.DownedAllies
            };
            return TeleportLandingSafe(unit, plan) ? plan : null;
        }

        private static bool TeleportLandingSafe(UnitEntityData unit, TeleportPlan plan)
        {
            var at = plan.Landings[0];
            var enemy = plan.Anchor;
            var eye = unit.EyePosition - unit.Position;
            if (HorizontalDistance(unit.Position, at) < 0.5f ||
                !UnitCommand.IsUnitCloseEnough(enemy.Position, at, at + eye, plan.WeaponReach, true, 0f)) return false;

            bool Taken(UnitEntityData by, Vector3 point, float radius)
            {
                if (by == null || by.State.IsDead || plan.Travellers.Contains(by)) return false;
                Vector3 pos = by.Position;
                return Math.Abs(pos.y - point.y) <= SpotRules.SameFloor &&
                    SpotRules.Takes(pos.x, pos.z, by.Corpulence, point.x, point.z, radius);
            }

            for (int i = 0; i < plan.Travellers.Count; i++)
            {
                var traveller = plan.Travellers[i];
                var point = plan.Landings[i];
                if (!ObstacleAnalyzer.IsPointInsideNavMesh(point) ||
                    AbilityCustomDimensionDoor.CheckTargetIsOnDisabledIsland(new TargetWrapper(point)) ||
                    AoeDangerAnalyzer.IsPositionDangerous(traveller, point) ||
                    AoeDangerAnalyzer.EnteringDanger(traveller, point, point, out _) > 0f) return false;
                foreach (var other in plan.Allies)
                    if (Taken(other, point, traveller.Corpulence)) return false;
                foreach (var other in plan.Downed)
                    if (Taken(other, point, traveller.Corpulence)) return false;
                for (int j = 0; j < i; j++)
                {
                    var otherAt = plan.Landings[j];
                    if (Math.Abs(otherAt.y - point.y) <= SpotRules.SameFloor &&
                        SpotRules.Takes(otherAt.x, otherAt.z, plan.Travellers[j].Corpulence, point.x, point.z, traveller.Corpulence))
                        return false;
                }
                foreach (var other in plan.Enemies)
                {
                    if (Taken(other, point, traveller.Corpulence)) return false;
                    if (other == null || CombatMath.IsDisabled(other)) continue;
                    var swarm = CombatMath.SwarmSpaceOf(other);
                    if (swarm != null && CombatMath.SwarmStrikes(other, swarm, traveller, point, null, plan.Allies)) return false;
                }
            }
            return true;
        }

        private static bool ValidateTeleportIssue(UnitEntityData unit, Candidate candidate, out string failure)
        {
            failure = null;
            var plan = candidate.Teleport;
            if (plan == null) return true;
            try
            {
                var ability = candidate.Ability;
                var anchor = plan.Anchor;
                var aim = candidate.Target == null ? new TargetWrapper(candidate.Point.Value) : new TargetWrapper(candidate.Target);
                // Both drivers choose and issue synchronously. Refuse a retained quote from another frame;
                // a fresh decision can make another native projection without an issue-time world scan.
                if (candidate.Kind == CandidateKind.Ability && !candidate.NeedsMove && !candidate.MoveFirst &&
                    plan.Frame == Time.frameCount && plan.RealTime == CombatMemory.IsRealTime &&
                    unit.Position == plan.Origin && anchor?.View != null && anchor.Position == plan.AnchorAt &&
                    (aim.Unit == null || anchor.Orientation == plan.AnchorFacing) && !MovingForTeleport(anchor) &&
                    !CombatMath.IsDisabled(anchor) && unit.IsEnemy(anchor) && !IsTurnedAlly(anchor) && unit.IsInCombat &&
                    unit.State.CanMove &&
                    CastOutcome.HeldChargeOf(unit) == null && ArmedRangedSpell(unit) == null &&
                    ability?.Caster?.Unit == unit && ability.IsAvailable && SlotOf(ability) == candidate.Slot &&
                    TeleportSlotAvailable(unit, candidate.Slot) &&
                    ReferenceEquals(AbilityProfile.Get(ability)?.TeleportDelivery, plan.Delivery) &&
                    ability.CanTarget(aim) && InRangeNow(unit, aim.Point, ability.GetApproachDistance(aim.Unit), candidate.NeedLoS))
                {
                    var travellers = TeleportTravellers(unit, plan.Delivery);
                    if (travellers != null && travellers.Count == plan.Travellers.Count)
                    {
                        for (int i = 0; i < travellers.Count; i++)
                            if (travellers[i] != plan.Travellers[i] || travellers[i].Position != plan.Starts[i])
                            {
                                failure = "native teleport travellers changed after the quote";
                                return false;
                            }
                        if (TeleportLandingSafe(unit, plan)) return true;
                    }
                }
            }
            catch (Exception ex) { Main.Verbose($"[Teleport] issue check: {ex.Message}"); }

            failure = "native teleport aim, range, travellers or landing no longer safe";
            return false;
        }
    }
}
