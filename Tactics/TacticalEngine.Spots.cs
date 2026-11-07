// ★ v0.3.29 test 4 (author-approved, body blocking fix 1): spot claims in real time. The game plans a real-time walk on an
// empty map and steers round other bodies only as it goes (UnitMovementAgent avoidance; turn-based marks the cells units
// stand on, TurnBased.Controllers.CombatController, and that walk cannot stop on them). A walk whose end spot a companion
// already stands on, or is walking to, pushes against that body until a stuck rule ends it. One walk to an enemy in five
// in the v0.3.27-v0.3.29 real-time logs started while another companion walked to the same enemy, 1-2 m away, so both
// headed for one spot. The approach now counts where the other companions stand - or, walking, where their walk ends -
// and takes a free side of the target when its straight spot is taken (PlanApproach). With no free side a weapon walk or
// an advance to that enemy is not offered (another enemy or action instead).
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>A unit's body where a walk ends against it: where it stands, or the end of its own walk not yet made.</summary>
        internal struct Body
        {
            public UnitEntityData Unit;
            public Vector3 At;
            public float Radius;
            /// <summary><see cref="At"/> is where this ally's walk ends, not where it stands.</summary>
            public bool Walking;
        }

        /// <summary>Crowded approaches searched for a free side this decision, and how many found none (the slow-decision line).</summary>
        private static int s_FreeSideSearches, s_NoFreeSide;

        /// <summary>The other units' bodies for this decision's approaches, read once (allies first, then enemies).</summary>
        private static List<Body> BodiesFor(TacticalContext ctx)
        {
            if (ctx.Bodies != null) return ctx.Bodies;
            var bodies = new List<Body>(ctx.Allies.Count + ctx.Enemies.Count);
            foreach (var ally in ctx.Allies)
            {
                if (ally == null || ally == ctx.Unit) continue;
                var claim = CombatMemory.IsRealTime ? WalkClaim(ally) : null;
                bodies.Add(new Body { Unit = ally, At = claim ?? ally.Position, Radius = ally.Corpulence, Walking = claim.HasValue });
            }
            foreach (var enemy in ctx.Enemies)
                if (enemy != null) bodies.Add(new Body { Unit = enemy, At = enemy.Position, Radius = enemy.Corpulence });
            ctx.Bodies = bodies;
            return bodies;
        }

        /// <summary>
        /// Where <paramref name="ally"/>'s walk ends: a command of its own that has not started and is not yet close enough to
        /// its target by the game's own test (UnitCommand.IsUnitCloseEnough) - a walk to a spot, or to a target it stops
        /// short of by the command's approach radius, on the straight line (SpotRules.WalkEnd). Null when it is not walking.
        /// </summary>
        private static Vector3? WalkClaim(UnitEntityData ally)
        {
            try
            {
                var raw = ally.Commands?.Raw;
                if (raw == null) return null;
                int seen = 0;
                foreach (var command in raw)
                {
                    if (++seen > 8) break;   // the game's command slots are few; no unbounded custom queue is scanned
                    if (command == null || command.IsFinished || command.IsStarted || command.Executor != ally) continue;
                    if (!(command is UnitMoveTo) && !(command is UnitAttack) && !(command is UnitUseAbility)) continue;
                    if (command.IsUnitCloseEnough()) continue;   // there already: it stands where it is
                    var to = command.ApproachPoint;
                    var from = ally.Position;
                    SpotRules.WalkEnd(from.x, from.z, to.x, to.z, command.ApproachRadius, out float x, out float z);
                    return new Vector3(x, to.y, z);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The body that takes <paramref name="spot"/> for this unit (SpotRules.Takes), other than <paramref name="target"/>
        /// itself; null when it is free.
        /// </summary>
        private static Body? TakenAt(TacticalContext ctx, Vector3 spot, UnitEntityData target)
        {
            float own = ctx.Unit.Corpulence;
            foreach (var body in BodiesFor(ctx))
            {
                if (body.Unit == target || Math.Abs(body.At.y - spot.y) > SpotRules.SameFloor) continue;
                if (SpotRules.Takes(body.At.x, body.At.z, body.Radius, spot.x, spot.z, own)) return body;
            }
            return null;
        }

        /// <summary>
        /// The 16 spots on a ring of <paramref name="radius"/> round <paramref name="center"/> (PlanApproach's search), in
        /// ring order, or the nearest to <paramref name="from"/> first when only a few of them are priced.
        /// </summary>
        private static Vector3[] RingSpots(Vector3 center, float radius, Vector3 from, bool nearestFirst)
        {
            var spots = new Vector3[16];
            for (int i = 0; i < 16; i++)
            {
                float angle = i * 22.5f * Mathf.Deg2Rad;
                spots[i] = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            }
            if (nearestFirst)
            {
                var keys = new float[16];
                for (int i = 0; i < 16; i++) keys[i] = (spots[i] - from).sqrMagnitude;
                Array.Sort(keys, spots);
            }
            return spots;
        }

        /// <summary>For the decision log: who takes the straight approach's spot.</summary>
        private static string TakerNote(Body body) =>
            body.Walking ? $"{body.Unit.CharacterName} walks to where it would stand" : $"{body.Unit.CharacterName} stands where it would";

        /// <summary>
        /// A weapon walk or an advance to <paramref name="enemy"/> with no free side left (the approach's
        /// <see cref="Approach.NoFreeSide"/>) is not offered; said once a fight per unit and enemy.
        /// </summary>
        private static bool SkipNoFreeSide(TacticalContext ctx, UnitEntityData enemy, Approach approach)
        {
            if (approach?.NoFreeSide == null) return false;
            ReportOnce($"no-free-side|{CombatMemory.Fight}|{ctx.Unit.UniqueId}|{enemy.UniqueId}",
                () => $"[Walk] {ctx.Unit.CharacterName}: no free side beside {enemy.CharacterName} ({approach.NoFreeSide}) - " +
                      "no walk to it while that holds");
            return true;
        }

        /// <summary>
        /// ★ v0.3.29 test 5 (v0.3.29 test 4 real-time log): the allies not in <paramref name="counted"/> whose walk, in the
        /// time a cast takes (SpotRules.WalkInSeconds), passes a point where <paramref name="inside"/> says the area would
        /// catch them. Orvath's Fan of Flames was aimed past Seelah and Camellia where they stood; both were walking to the
        /// Cultist Archer and took 9 each when it landed 1.5 s later (Seelah 13 -> 4 HP). Real time only (turn-based, nobody
        /// else moves during a turn); a walking ally is where its own command not yet started takes it (BodiesFor).
        /// </summary>
        private static List<UnitEntityData> AlliesWalkingInto(TacticalContext ctx, ICollection<UnitEntityData> counted,
                                                              Func<UnitEntityData, Vector3, bool> inside)
        {
            if (!CombatMemory.IsRealTime) return null;
            List<UnitEntityData> found = null;
            try
            {
                foreach (var body in BodiesFor(ctx))
                {
                    var ally = body.Unit;
                    if (!body.Walking || ally == ctx.Unit || counted.Contains(ally) || !ctx.Allies.Contains(ally)) continue;
                    Vector3 from = ally.Position;
                    float distance = HorizontalDistance(from, body.At);
                    int steps = SpotRules.WalkSteps(distance, Math.Max(1.5f, ally.CurrentSpeedMps), SpotRules.WalkInSeconds,
                                                    SpotRules.WalkInStep, out float covered);
                    for (int k = 1; k <= steps; k++)
                    {
                        var point = Vector3.Lerp(from, body.At, covered * k / steps / distance);
                        if (!inside(ally, point)) continue;
                        (found ?? (found = new List<UnitEntityData>())).Add(ally);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] walking-in allies for {ctx.Unit.CharacterName}: {ex.Message}");
            }
            return found;
        }

        /// <summary>
        /// ★ v0.3.29 test 6 (author-approved): the option walks first and the unit is boxed in where it stands - two walks in a
        /// row got nowhere from this spot (CombatMemory.NoteStuckWalk) - so it is not offered for a round. Getting out of a
        /// harmful area or a swarm stays open (its walk is the way out, and staying costs). The unit acts from where it stands
        /// or waits for the bodies round it to move. Said once per boxed-in spell.
        /// </summary>
        private static bool BoxedInWalk(TacticalContext ctx, Candidate c)
        {
            if (!CombatMemory.IsRealTime || !(c.NeedsMove || c.MoveFirst || c.Kind == CandidateKind.Move)) return false;
            if (c.Key == "move:leave-area" || c.Key == "move:outofswarm") return false;
            if (!CombatMemory.BoxedIn(ctx.Unit, out double until)) return false;
            ReportOnce($"boxed-in|{ctx.Unit.UniqueId}|{until:F1}",
                () => $"[Walk] {ctx.Unit.CharacterName}: boxed in - no walk offered for now (e.g. {c.Label})");
            return true;
        }

        /// <summary>For the decision log: an ally counted because it walks into the area.</summary>
        private static string WalkingInTag(List<UnitEntityData> walkingIn, UnitEntityData ally) =>
            walkingIn != null && walkingIn.Contains(ally) ? " (walking in)" : "";

        /// <summary>The slow-decision line's note on crowded approaches.</summary>
        private static string FreeSideNote() =>
            s_FreeSideSearches > 0 ? $"; crowded approaches searched for a free side {s_FreeSideSearches}" +
                                     (s_NoFreeSide > 0 ? $" ({s_NoFreeSide} with none)" : "") : "";
    }
}
