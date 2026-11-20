// ★ burst aim (17 November 2026, author-approved; docs/BURST_AIM_AND_TACTICS_PLAN.md §3): an area burst that can hurt
// allies and lands after its cast goes off (a delay or a projectile's flight) is aimed at the ground where its anchor
// enemy will be when it lands, not at the unit. Aimed at a unit, the game picks the burst's victims around that unit
// wherever it has walked by then (AbilityTargetsAround.Select, AbilityDeliverDelay, AbilityDeliverProjectile): outcome2's
// Explosive Runes fusions went off 4-9 s after their orders and hit Seelah, Wenduag, Bandit and Tripper.
// Pure rules: BurstRules. The cast is re-checked before it goes off and its landing spot is shared with the other AI
// units (BurstWatch, AoeDangerAnalyzer); a turn-based turn waits for it to land (CustomBrainPatch.ReadyToEndTurn).
using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using Kingmaker.Visual.Animation.Kingmaker.Actions;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>
        /// A burst aimed at the ground: where, when it lands and what the decision counted. Kept with the candidate and,
        /// once issued, by <see cref="BurstWatch"/> for the re-check before the cast goes off.
        /// </summary>
        internal sealed class BurstPlan
        {
            public AbilityData Ability;
            /// <summary>The enemy the burst is aimed at (its walk is led).</summary>
            public UnitEntityData Anchor;
            public Vector3 Point;
            public float Radius;
            /// <summary>The native approach distance to a point (AbilityData.GetApproachDistance).</summary>
            public float Range;
            public float Walk, Cast, Delay, Flight;
            public float Landing => BurstRules.LandingSeconds(Walk, Cast, Delay, Flight);
            /// <summary>Metres between where the anchor stood and the point.</summary>
            public float Lead;
            public bool RealTime;
            public bool AvoidFriendlyFire;
            /// <summary>The candidate's final value, set when the candidate is built.</summary>
            public float Value;
            /// <summary>The largest damage estimate of one recipient (the re-check's fallback for an ally's cost).</summary>
            public float Damage;
            /// <summary>The allies (standing, walking in, downed, the caster) the decision counted.</summary>
            public HashSet<UnitEntityData> Allies = new HashSet<UnitEntityData>();
            public int Enemies;
            /// <summary>The lead follows the anchor's own path (the game's planned walk), not the straight line to its end.</summary>
            public bool OnPath;
            /// <summary>
            /// ★ review follow-up A-R1/A3: what the burst's control would cost each ally the decision did not count, if it
            /// walked in: the decision's own friendly-control rule and price, kept for the re-check. Null when none would be.
            /// </summary>
            public Dictionary<UnitEntityData, float> AllyControl;

            public string Describe()
            {
                string anchor = Anchor?.CharacterName ?? "?";
                string where = Lead >= BurstRules.MinLeadMeters
                    ? $"{Lead:F1} m ahead of {anchor}{(OnPath ? " along its path" : "")}" : $"where {anchor} stands";
                return $"ground {where}, lands in {Landing:F1} s (walk {Walk:F1} + cast {Cast:F1} + delay {Delay:F1} + flight {Flight:F1})";
            }
        }

        /// <summary>
        /// A1: a burst gets ground aim when it picks its victims around its anchor as it lands, can hurt allies (selector
        /// Any, not spared by Selective), lands after the cast goes off, makes no attack roll (a direct hit needs the unit)
        /// and the cast can target a point. An enemy-only burst is better aimed at the unit: it follows the target.
        /// ★ coverage2 review (docs/COVERAGE2_REVIEW.md, finding 1): in real time the cast itself is a lag too. A burst
        /// with no delay or flight still picks its victims around its anchor when the cast goes off, 2 s after it starts
        /// (UnitUseAbility.m_CastTime, BurstRules.CastSeconds) and after any walk to cast. arcan's Incendiary Runes /
        /// Frigid Touch fusion went off 2-6 s after its decisions and caught Orvath, Wenduag, Bandit and a summon in four of
        /// five real-time casts (76 HP), while nothing re-checked it. Turn-based, nobody else moves during the cast.
        /// </summary>
        internal static bool GroundAimBurst(AbilityData ability, AbilityProfile profile) =>
            GroundAimBurst(ability, profile, CombatMemory.IsRealTime);

        internal static bool GroundAimBurst(AbilityData ability, AbilityProfile profile, bool realTime)
        {
            if (ability == null || profile == null) return false;
            if (!profile.SelectsAround || !profile.IsAoE || profile.NeedsAttackRoll) return false;
            if (!BurstRules.LandsLater(profile.HasDeliveryLag, realTime, realTime ? CastSecondsOf(ability, true) : 0f)) return false;
            if (profile.AoETargets != Kingmaker.UnitLogic.Abilities.Components.TargetType.Any) return false;
            try { return ability.CanTargetPoint && !profile.SelectiveSparesAllies(ability); }
            catch { return false; }
        }

        /// <summary>The native cast time of <paramref name="ability"/> (UnitUseAbility), for a decision's forecast.</summary>
        internal static float CastSecondsOf(AbilityData ability, bool realTime)
        {
            try
            {
                bool wand = ability.SourceItemUsableBlueprint != null && ability.SourceItemUsableBlueprint.Type == UsableItemType.Wand;
                bool immediate = ability.Blueprint.Animation == UnitAnimationActionCastSpell.CastAnimationStyle.Immediate;
                return BurstRules.CastSeconds(realTime, ability.RequireFullRoundAction, ability.SourceItem != null, wand, immediate);
            }
            catch { return realTime ? BurstRules.StandardCastSeconds : 0f; }
        }

        /// <summary>The flight of the burst's projectile from the caster to <paramref name="point"/>; 0 without one.</summary>
        internal static float FlightSecondsTo(AbilityProfile profile, Vector3 from, Vector3 point) =>
            BurstRules.FlightSeconds(Vector3.Distance(from, point), profile.ProjectileSpeed, profile.ProjectileMinTime);

        /// <summary>
        /// A2: the ground point for <paramref name="ability"/> aimed at <paramref name="target"/>. Real time, an enemy with a
        /// walk of its own is led to where it will be when the burst lands - when the caster can cast at that spot without a
        /// walk; otherwise the point is where it stands (the walk-first cast then has its walk in the forecast). Turn-based,
        /// nobody else moves during the turn: where it stands.
        /// </summary>
        private static BurstPlan PlanBurst(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                           bool needLos)
        {
            bool realTime = CombatMemory.IsRealTime;
            var plan = new BurstPlan
            {
                Ability = ability,
                Anchor = target,
                Point = target.Position,
                Radius = profile.AoERadius,
                Range = ability.GetApproachDistance(null),
                Cast = CastSecondsOf(ability, realTime),
                Delay = profile.DeliverDelaySeconds,
                RealTime = realTime,
                AvoidFriendlyFire = ctx.Settings?.AvoidFriendlyFire ?? true
            };
            var eye = ctx.Unit.EyePosition;
            var route = realTime ? RouteOf(ctx, target) : null;
            if (route != null)
            {
                float seconds = plan.Cast + plan.Delay + FlightSecondsTo(profile, eye, target.Position);
                route.At(seconds, out float x, out float y, out float z);   // the height along its walk too
                var lead = new Vector3(x, y, z);
                if (HorizontalDistance(target.Position, lead) >= BurstRules.MinLeadMeters && InRangeNow(ctx.Unit, lead, plan.Range, needLos))
                {
                    plan.Point = lead;
                    plan.OnPath = route.Native;
                }
            }
            plan.Lead = HorizontalDistance(target.Position, plan.Point);
            plan.Flight = FlightSecondsTo(profile, eye, plan.Point);
            return plan;
        }

        /// <summary>The burst plan being scored for <paramref name="ability"/>, or null.</summary>
        private static BurstPlan BurstFor(TacticalContext ctx, AbilityData ability) =>
            ctx.Burst != null && ReferenceEquals(ctx.Burst.Ability, ability) ? ctx.Burst : null;

        /// <summary>
        /// A pool selects current recipients, not the independent payload of a future fused burst. Keep the pool's
        /// current-position control forecast, while letting the burst's other payload use its predicted recipients.
        /// </summary>
        internal static bool UseCurrentPoolRecipients(AbilityProfile profile, bool supportedPool, bool plannedBurst) =>
            supportedPool && (!plannedBurst || profile?.Fusion == null);

        /// <summary>
        /// ★ review follow-up A-R1: <paramref name="unit"/>'s own walk (<see cref="ReadRoute"/>), read once per decision; real
        /// time only. Null when it is not walking.
        /// </summary>
        private static WalkRoute RouteOf(TacticalContext ctx, UnitEntityData unit)
        {
            if (!CombatMemory.IsRealTime || unit == null) return null;
            var cache = ctx.BurstRoutes ?? (ctx.BurstRoutes = new Dictionary<UnitEntityData, WalkRoute>());
            if (!cache.TryGetValue(unit, out var route)) cache[unit] = route = ReadRoute(unit, null);
            return route;
        }

        // The agent's next waypoint (UnitMovementAgent.m_NextPointIndex, private): read only; null leaves the straight walk.
        private static readonly HarmonyLib.AccessTools.FieldRef<Kingmaker.View.UnitMovementAgent, int> NextPointRef = ReadNextPointRef();

        private static HarmonyLib.AccessTools.FieldRef<Kingmaker.View.UnitMovementAgent, int> ReadNextPointRef()
        {
            try { return HarmonyLib.AccessTools.FieldRefAccess<Kingmaker.View.UnitMovementAgent, int>("m_NextPointIndex"); }
            catch { return null; }
        }

        /// <summary>Walks read this session along the game's path, and walks that fell back to the straight line.</summary>
        internal static int RoutesOnPath, RoutesStraight;
        /// <summary>★ R5: of those, walks of an attack that had already started and chases its target.</summary>
        internal static int RoutesPursuing;

        /// <summary>
        /// ★ review follow-up A-R1 (author-approved): where <paramref name="unit"/>'s own walk takes it, as the game plans it.
        /// The command it walks for (WalkingCommand) is followed along the movement agent's current path from its next
        /// waypoint (UnitMovementAgent.Path, m_NextPointIndex). The stop uses native mechanics distance and the actual
        /// approach radius; future LOS can delay the true stop and is not proved here. Without a usable path of that
        /// walk (none yet, another walk's, longer than WalkRoute.MaxPoints) the straight walk to SpotRules.WalkEnd. Reads
        /// the agent's existing path only: no path search, raycast or world query. Null when the unit is not walking.
        /// ★ R5 (docs/R_ITEMS_PLAN.md): a started attack that chases its target walks too (PursuingAttack). The burst
        /// forecast is this method's only reader; WalkingCommand and the spot rules (WalkClaim) are unchanged.
        /// </summary>
        internal static WalkRoute ReadRoute(UnitEntityData unit, WalkRoute into)
        {
            try
            {
                var command = WalkingCommand(unit);
                if (command == null && (command = PursuingAttack(unit)) != null) RoutesPursuing++;
                if (command == null) return null;
                var route = into ?? new WalkRoute();
                var from = unit.Position;
                var to = command.ApproachPoint;
                route.Start(from.x, from.y, from.z, unit.CurrentSpeedMps);
                if (AddNativePath(unit, command, route))
                {
                    StopRouteAtApproachDistance(route, to, command.ApproachRadius);
                    route.Native = true;
                    RoutesOnPath++;
                    return route;
                }
                route.Start(from.x, from.y, from.z, unit.CurrentSpeedMps);
                SpotRules.WalkEnd(from.x, from.z, to.x, to.z, command.ApproachRadius, out float x, out float z);
                route.Add(x, to.y, z);
                RoutesStraight++;
                return route;
            }
            catch { return null; }
        }

        /// <summary>
        /// ★ R5: an attack that has started and still chases its target. Native UnitCommandController.TickCommand keeps
        /// calling TickApproaching for a started command when it can move after its start (UnitAttack.CanMoveAfterStart) and
        /// the unit can move; TickApproaching walks it to the target while it is not close enough (IsUnitCloseEnough), by
        /// the same MoveTo as before the start. A melee ally chasing the enemy a burst is aimed at walks where the burst lands.
        /// </summary>
        private static UnitCommand PursuingAttack(UnitEntityData unit)
        {
            try
            {
                var raw = unit?.Commands?.Raw;
                if (raw == null || !unit.Descriptor.State.CanMove) return null;
                int seen = 0;
                foreach (var command in raw)
                {
                    if (++seen > 8) break;   // the game's command slots are few; no unbounded custom queue is scanned
                    if (!(command is UnitAttack attack) || !attack.IsStarted || attack.IsFinished || attack.Executor != unit ||
                        !attack.CanMoveAfterStart || attack.TargetUnit == null) continue;
                    if (attack.IsUnitCloseEnough()) continue;   // in reach: it attacks where it stands
                    return attack;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The rest of the agent's current path, from its next waypoint, when its original request matches this command's
        /// point and the terminal is distance-eligible. Endpoint proximity alone cannot identify a path's request.
        /// </summary>
        private static bool AddNativePath(UnitEntityData unit, Kingmaker.UnitLogic.Commands.Base.UnitCommand command, WalkRoute route)
        {
            if (NextPointRef == null) return false;
            var agent = unit.View?.AgentASP;
            var path = agent?.Path;
            var points = path?.vectorPath;
            if (points == null || points.Count < 2) return false;
            int next = NextPointRef(agent);
            if (next < 1 || next >= points.Count) return false;   // not following it yet (PathFailed) or past its end
            if (points.Count - next + 1 > WalkRoute.MaxPoints) return false;
            if (!NativePathMatchesAim(path, command.ApproachPoint, command.ApproachRadius)) return false;
            for (int i = next; i < points.Count; i++)
            {
                var p = points[i];
                if (!route.Add(p.x, p.y, p.z)) return false;
            }
            return route.Count > 1;
        }

        /// <summary>ABPath/XPath retain the original requested endpoint even when their vector path ends sooner or is
        /// postprocessed. Match that request using the same Vector3 equality as native PathTo; validate its terminal with
        /// the command's native mechanics-distance rule. Forced/unknown paths retain the straight fallback.</summary>
        private static bool NativePathMatchesAim(Pathfinding.Path path, Vector3 point, float radius)
        {
            if (!(path is Pathfinding.ABPath request) || request.originalEndPoint != point) return false;
            var points = path.vectorPath;
            return points != null && points.Count > 0 &&
                Owlcat.Runtime.Core.Utils.GeometryUtils.SqrMechanicsDistance(points[points.Count - 1], point) <= radius * radius;
        }

        /// <summary>Use the actual native approach radius for the distance stop, not SpotRules' conservative straight-
        /// endpoint share. Native sight can delay the stop; resolving future sight remains a separate design question.</summary>
        private static void StopRouteAtApproachDistance(WalkRoute route, Vector3 point, float radius) =>
            route.StopWithin(point.x, point.y, point.z, radius);

        /// <summary>Where <paramref name="unit"/> will be in <paramref name="seconds"/>, and whether it walks.</summary>
        private static Vector3 PredictedAt(TacticalContext ctx, UnitEntityData unit, float seconds, out bool walking)
        {
            var route = RouteOf(ctx, unit);
            walking = route != null;
            if (!walking) return unit.Position;
            route.At(seconds, out float x, out float y, out float z);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// The enemies surely inside the burst when it lands: at their predicted position, within the radius and their body
        /// less the prediction's uncertainty (an enemy that may walk out is not counted as hit).
        /// </summary>
        private static List<UnitEntityData> BurstEnemiesInside(TacticalContext ctx, BurstPlan plan)
        {
            var inside = new List<UnitEntityData>();
            float landing = plan.Landing;
            foreach (var enemy in ctx.Enemies)
            {
                if (enemy == null) continue;
                var at = PredictedAt(ctx, enemy, landing, out bool walking);
                float u = BurstRules.Uncertainty(walking, enemy.CurrentSpeedMps, landing);
                if (BurstRules.SurelyInside(Vector3.Distance(at, plan.Point), plan.Radius + enemy.Corpulence, u)) inside.Add(enemy);
            }
            return inside;
        }

        /// <summary>
        /// The allies the burst may catch, beyond <paramref name="counted"/> (those standing inside now): an ally whose own
        /// walk may take it inside around the landing time (WalkRoute.MayBeInside), and the caster where its walk to
        /// cast ends (<paramref name="castOrigin"/>). Real time only; turn-based, nobody else moves during the turn.
        /// </summary>
        private static List<UnitEntityData> BurstWalkingAllies(TacticalContext ctx, BurstPlan plan, List<UnitEntityData> counted,
                                                               Vector3 castOrigin, Func<UnitEntityData, bool> reaches)
        {
            List<UnitEntityData> found = null;
            try
            {
                var caster = ctx.Unit;
                if (!counted.Contains(caster) && Vector3.Distance(castOrigin, plan.Point) <= plan.Radius + caster.Corpulence &&
                    reaches(caster))
                    (found = new List<UnitEntityData>()).Add(caster);
                if (!plan.RealTime) return found;
                float landing = plan.Landing;
                foreach (var ally in ctx.Allies)
                {
                    if (ally == null || ally == caster || counted.Contains(ally)) continue;
                    var route = RouteOf(ctx, ally);   // ★ A-R1: along its own path
                    if (route == null || !route.MayBeInside(plan.Point.x, plan.Point.z, plan.Radius + ally.Corpulence, landing) ||
                        !reaches(ally)) continue;
                    (found ?? (found = new List<UnitEntityData>())).Add(ally);
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Burst] walking allies for {ctx.Unit.CharacterName}: {ex.Message}");
            }
            return found;
        }
    }
}
