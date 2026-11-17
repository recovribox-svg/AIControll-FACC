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

            public string Describe()
            {
                string anchor = Anchor?.CharacterName ?? "?";
                string where = Lead >= BurstRules.MinLeadMeters ? $"{Lead:F1} m ahead of {anchor}" : $"where {anchor} stands";
                return $"ground {where}, lands in {Landing:F1} s (walk {Walk:F1} + cast {Cast:F1} + delay {Delay:F1} + flight {Flight:F1})";
            }
        }

        /// <summary>
        /// A1: a burst gets ground aim when it picks its victims around its anchor as it lands, can hurt allies (selector
        /// Any, not spared by Selective), lands after the cast goes off, makes no attack roll (a direct hit needs the unit)
        /// and the cast can target a point. An enemy-only burst is better aimed at the unit: it follows the target.
        /// </summary>
        internal static bool GroundAimBurst(AbilityData ability, AbilityProfile profile)
        {
            if (ability == null || profile == null) return false;
            if (!profile.SelectsAround || !profile.IsAoE || !profile.HasDeliveryLag || profile.NeedsAttackRoll) return false;
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
            if (realTime && TryWalkOf(ctx, target, out var to))
            {
                float seconds = plan.Cast + plan.Delay + FlightSecondsTo(profile, eye, target.Position);
                var from = target.Position;
                BurstRules.At(from.x, from.z, to.x, to.z, target.CurrentSpeedMps, seconds, out float x, out float z);
                float walked = HorizontalDistance(from, to);
                float share = walked > 0f ? HorizontalDistance(from, new Vector3(x, from.y, z)) / walked : 0f;
                var lead = new Vector3(x, Mathf.Lerp(from.y, to.y, share), z);   // the height along its walk
                if (HorizontalDistance(from, lead) >= BurstRules.MinLeadMeters && InRangeNow(ctx.Unit, lead, plan.Range, needLos))
                    plan.Point = lead;
            }
            plan.Lead = HorizontalDistance(target.Position, plan.Point);
            plan.Flight = FlightSecondsTo(profile, eye, plan.Point);
            return plan;
        }

        /// <summary>The burst plan being scored for <paramref name="ability"/>, or null.</summary>
        private static BurstPlan BurstFor(TacticalContext ctx, AbilityData ability) =>
            ctx.Burst != null && ReferenceEquals(ctx.Burst.Ability, ability) ? ctx.Burst : null;

        /// <summary>
        /// Where <paramref name="unit"/>'s own walk ends (TacticalEngine.WalkClaim), read once per decision; real time only.
        /// </summary>
        private static bool TryWalkOf(TacticalContext ctx, UnitEntityData unit, out Vector3 to)
        {
            to = default(Vector3);
            if (!CombatMemory.IsRealTime || unit == null) return false;
            var cache = ctx.BurstWalks ?? (ctx.BurstWalks = new Dictionary<UnitEntityData, Vector3?>());
            if (!cache.TryGetValue(unit, out var claim)) cache[unit] = claim = WalkClaim(unit);
            if (!claim.HasValue) return false;
            to = claim.Value;
            return true;
        }

        /// <summary>Where <paramref name="unit"/> will be in <paramref name="seconds"/>, and whether it walks.</summary>
        private static Vector3 PredictedAt(TacticalContext ctx, UnitEntityData unit, float seconds, out bool walking)
        {
            var from = unit.Position;
            walking = TryWalkOf(ctx, unit, out var to);
            if (!walking) return from;
            BurstRules.At(from.x, from.z, to.x, to.z, unit.CurrentSpeedMps, seconds, out float x, out float z);
            return new Vector3(x, from.y, z);
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
        /// walk may take it inside around the landing time (BurstRules.WalkMayBeInside), and the caster where its walk to
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
                foreach (var body in BodiesFor(ctx))
                {
                    var ally = body.Unit;
                    if (!body.Walking || ally == caster || counted.Contains(ally) || !ctx.Allies.Contains(ally) || !reaches(ally)) continue;
                    var from = ally.Position;
                    if (!BurstRules.WalkMayBeInside(from.x, from.z, body.At.x, body.At.z, ally.CurrentSpeedMps, plan.Point.x, plan.Point.z,
                                                    plan.Radius + ally.Corpulence, landing)) continue;
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
