// ★ Fusion coverage C4 (author request and approval, docs/FUSION_COVERAGE_REST.md): aiming a line (Lightning Bolt, Acidic
// Spray, Fire Snake, and the fusions delivered by them) along enemies and past allies, and walking to a spot that lines two
// enemies up (author-approved movement: "reposition to line up enemies for line delivery").
// Native contract: a line leaves the caster's eye (moved forward by its body) towards the aimed point, flat, for its Length (at
// most its MaxRange), 5 ft wide unless the blueprint says otherwise, and catches every unit in it that its target rule allows
// (Selective) - ProjectileLine binds the native WouldTargetUnitLine. Aimed at a point, only the direction counts
// (CorrectPosition). The lined-up spot is checked like an approach spot (PlanApproach): within this move, free, walkable, not in
// a harmful area or a swarm's space, the cast in range and sight from it, priced by MoveRisk and the real-time walk. The walk is
// issued first and the line is cast on the next decision from where the caster stands (the cone's MoveFirst rule).
// Bounded: at most 8 enemies, 12 turned directions per aim, 2 lined-up spots per ability and decision, one walk a round.
using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>Turns of a line's axis tried in place, in degrees (a 5 ft line: 3 degrees moves it 0.5 m at 10 m).</summary>
        private static readonly float[] LineTurns = { 3f, -3f, 6f, -6f, 10f, -10f, 15f, -15f };
        /// <summary>Shares of the angle between the aimed enemy and another one tried, to catch both.</summary>
        private static readonly float[] LineBetween = { 0.35f, 0.5f, 0.65f };
        private const int LineEnemiesConsidered = 8;
        private const int LineUpSpots = 2;
        /// <summary>How far a lined-up spot stays behind the near enemy, beyond the two bodies: out of a 5 ft reach.</summary>
        private const float LineUpClearance = 2f;

        /// <summary>A spot from which a line aimed at <see cref="Aim"/> also catches <see cref="Near"/> (and the others counted).</summary>
        internal sealed class LineUp
        {
            public UnitEntityData Aim, Near;
            public Vector3 Spot;
            public int Enemies, Allies;
            public Disengage Risk;
        }

        /// <summary>One lined-up walk per unit a round (fight, round): enemies move, and the next decision aims from the new spot.</summary>
        private static readonly Dictionary<string, KeyValuePair<int, int>> s_LineUpWalks = new Dictionary<string, KeyValuePair<int, int>>();
        private static int s_LineUpsOffered, s_LineUpsTaken;

        internal static void NoteLineUpWalk(UnitEntityData unit)
        {
            if (unit == null) return;
            if (s_LineUpWalks.Count > 256) s_LineUpWalks.Clear();
            s_LineUpWalks[unit.UniqueId] = new KeyValuePair<int, int>(CombatMemory.Fight, CombatMemory.Round);
            s_LineUpsTaken++;
        }

        private static bool LineUpWalkedThisRound(UnitEntityData unit) =>
            s_LineUpWalks.TryGetValue(unit.UniqueId, out var at) && at.Key == CombatMemory.Fight && at.Value == CombatMemory.Round;

        /// <summary>A unit's flat position and body, and whether the line can affect it at all (the native target rule).</summary>
        private struct LineBody
        {
            public UnitEntityData Unit;
            public float X, Z, Radius;
            public bool Enemy, Affectable;
        }

        private static List<LineBody> LineBodies(TacticalContext ctx, ProjectileLine.Query rule)
        {
            var list = new List<LineBody>();
            var seen = new HashSet<UnitEntityData>();
            foreach (var u in ctx.Enemies)
                if (u != null && u != ctx.Unit && seen.Add(u))
                    list.Add(new LineBody { Unit = u, X = u.EyePosition.x, Z = u.EyePosition.z, Radius = u.Corpulence, Enemy = true, Affectable = rule.CanAffect(u) });
            foreach (var u in ctx.Allies.Concat(ctx.DownedAllies))
                if (u != null && u != ctx.Unit && seen.Add(u))
                    list.Add(new LineBody { Unit = u, X = u.EyePosition.x, Z = u.EyePosition.z, Radius = u.Corpulence, Enemy = false, Affectable = rule.CanAffect(u) });
            return list;
        }

        /// <summary>Enemies and allies a line from (x, z) along the direction catches, by the native formula (pre-filter only).</summary>
        private static void LineCount(List<LineBody> bodies, float x, float z, float dirX, float dirZ, float body, float travel,
                                      float halfWidth, UnitEntityData must, out int enemies, out int allies, out bool hasMust)
        {
            enemies = allies = 0;
            hasMust = must == null;
            float lx = x + dirX * body, lz = z + dirZ * body;
            foreach (var b in bodies)
            {
                if (!b.Affectable || !LineGeometry.Catches(lx, lz, dirX, dirZ, travel, halfWidth, b.X, b.Z, b.Radius)) continue;
                if (b.Unit == must) hasMust = true;
                if (b.Enemy) enemies++;
                else allies++;
            }
        }

        /// <summary>
        /// A point aim for a line at <paramref name="target"/>, from where the caster stands, that catches fewer allies than the
        /// straight aim (<paramref name="directRecipients"/>, the native query's) or, catching no more of them, more enemies;
        /// null when none is better, the ability cannot be aimed at a point, or the line cannot be read.
        /// </summary>
        private static TargetWrapper LineAimAlong(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
            IReadOnlyCollection<UnitEntityData> directRecipients, out string note)
        {
            note = null;
            var unit = ctx.Unit;
            if (target == null || target == unit || directRecipients == null || !profile.IsProjectileLine || !ability.CanTargetPoint) return null;
            try
            {
                var straightAim = AbilityProfile.TargetOn(ability, target);
                if (straightAim == null || !ProjectileLine.TryCreate(ability, straightAim, unit.Position, out var rule)) return null;
                var bodies = LineBodies(ctx, rule);
                int straightEnemies = 0, straightAllies = 0;
                foreach (var b in bodies)
                    if (directRecipients.Contains(b.Unit)) { if (b.Enemy) straightEnemies++; else straightAllies++; }
                Vector3 eye = unit.EyePosition;
                if (!LineGeometry.Direction(eye.x, eye.z, target.Position.x, target.Position.z, out float ax, out float az)) return null;
                // Directions: small turns of the axis, and shares of the angle towards other enemies within the line's travel.
                var turns = new List<float>(LineTurns);
                foreach (var b in bodies.Where(b => b.Enemy && b.Affectable && b.Unit != target && !directRecipients.Contains(b.Unit))
                                        .Select(b => new { b, d = HorizontalDistance(eye, b.Unit.EyePosition) })
                                        .Where(x => x.d <= rule.Range + x.b.Radius)
                                        .OrderBy(x => x.d).Take(LineEnemiesConsidered).Select(x => x.b))
                {
                    if (!LineGeometry.Direction(eye.x, eye.z, b.X, b.Z, out float ex, out float ez)) continue;
                    float angle = LineGeometry.AngleBetween(ax, az, ex, ez);
                    if (Math.Abs(angle) > 30f) continue;
                    foreach (float share in LineBetween) turns.Add(angle * share);
                }
                if (straightAllies == 0 && turns.Count == LineTurns.Length) return null;   // nothing to spare, no one to add
                float bestTurn = 0f;
                int bestEnemies = straightEnemies, bestAllies = straightAllies;
                bool found = false;
                foreach (float turn in turns)
                {
                    LineGeometry.Turn(ax, az, turn, out float dx, out float dz);
                    LineCount(bodies, eye.x, eye.z, dx, dz, unit.Corpulence, rule.Range, rule.HalfWidth, target,
                              out int enemies, out int allies, out bool keeps);
                    if (!keeps) continue;
                    bool better = allies < bestAllies || allies == bestAllies &&
                                  (enemies > bestEnemies || enemies == bestEnemies && found && Math.Abs(turn) < Math.Abs(bestTurn));
                    if (!better) continue;
                    found = true;
                    bestTurn = turn;
                    bestEnemies = enemies;
                    bestAllies = allies;
                }
                if (!found) return null;
                LineGeometry.Turn(ax, az, bestTurn, out float bx, out float bz);
                float along = Math.Max(1f, Math.Min(HorizontalDistance(unit.Position, target.Position), 3f));
                var point = new TargetWrapper(unit.Position + new Vector3(bx, 0f, bz) * along);
                if (!ability.CanTarget(point)) return null;
                note = bestAllies < straightAllies
                    ? $"turned {bestTurn:+0;-0} deg past {bestAllies}/{straightAllies} allies"
                    : $"turned {bestTurn:+0;-0} deg along {bestEnemies} enemies";
                return point;
            }
            catch (Exception ex)
            {
                ReportOnce($"line-aim|{ability.Blueprint.AssetGuid}|{ex.GetType().Name}",
                    $"[Line] {ability.Name}: turned aims unavailable ({ex.GetType().Name}: {ex.Message}); the straight aim stays");
                return null;
            }
        }

        /// <summary>
        /// The lined-up spot for a line aimed at <paramref name="target"/>: one of the two best spots within this move from which
        /// the line catches more enemies than any line from here, checked as an approach spot (free, walkable, outside harmful
        /// areas and swarms, in range and sight, priced by MoveRisk). Null when there is none, or a lined-up walk was taken
        /// this round. Computed once per ability and decision.
        /// </summary>
        private static LineUp LineUpFor(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                        float range, bool needLos)
        {
            var unit = ctx.Unit;
            if (!profile.IsProjectileLine || target == null || !ctx.Unit.IsEnemy(target) || !ctx.HasMove || ctx.NoMovementLeft ||
                ctx.Enemies.Count < 2 || LineUpWalkedThisRound(unit)) return null;
            if (!ctx.LineUps.TryGetValue(ability, out var plans))
            {
                plans = PlanLineUps(ctx, ability, range, needLos);
                ctx.LineUps[ability] = plans;
            }
            return plans.FirstOrDefault(p => p.Aim == target);
        }

        private static List<LineUp> PlanLineUps(TacticalContext ctx, AbilityData ability, float range, bool needLos)
        {
            var plans = new List<LineUp>();
            var unit = ctx.Unit;
            try
            {
                var first = ctx.Enemies.FirstOrDefault(e => e != null && e != unit);
                var firstAim = first != null ? AbilityProfile.TargetOn(ability, first) : null;
                if (firstAim == null || !ProjectileLine.TryCreate(ability, firstAim, unit.Position, out var rule)) return plans;
                var bodies = LineBodies(ctx, rule);
                float body = unit.Corpulence, travel = rule.Range, half = rule.HalfWidth;
                Vector3 eye = unit.EyePosition, eyeOffset = unit.EyePosition - unit.Position;
                // The most enemies a line from here catches, whatever it is aimed at (by the same formula).
                int here = 0;
                foreach (var b in bodies)
                {
                    if (!b.Enemy || !b.Affectable || !LineGeometry.Direction(eye.x, eye.z, b.X, b.Z, out float dx, out float dz)) continue;
                    LineCount(bodies, eye.x, eye.z, dx, dz, body, travel, half, null, out int enemies, out _, out _);
                    here = Math.Max(here, enemies);
                }
                var near = bodies.Where(b => b.Enemy && b.Affectable)
                                 .OrderBy(b => HorizontalDistance(unit.Position, b.Unit.Position))
                                 .Take(LineEnemiesConsidered).ToList();
                var spots = new List<KeyValuePair<float, LineUp>>();
                float maxWalk = ctx.MoveRange * 0.95f;
                for (int i = 0; i < near.Count; i++)
                    for (int j = 0; j < near.Count; j++)
                    {
                        if (i == j) continue;
                        var a = near[i];   // the near one: the caster stands behind it
                        var f = near[j];   // the far one, beyond it
                        float gap = (float)Math.Sqrt((a.X - f.X) * (a.X - f.X) + (a.Z - f.Z) * (a.Z - f.Z));
                        float minBack = a.Radius + body + LineUpClearance;
                        float maxBack = LineGeometry.MaxBack(travel, f.Radius, gap, body, 0.5f);
                        if (!LineGeometry.BehindSpot(unit.Position.x, unit.Position.z, a.X, a.Z, f.X, f.Z, minBack, maxBack,
                                                     out float sx, out float sz)) continue;
                        var spot = new Vector3(sx, a.Unit.Position.y, sz);
                        float walk = HorizontalDistance(unit.Position, spot);
                        if (walk < 1f || walk > maxWalk) continue;   // a step's worth is the in-place aim's
                        if (!LineGeometry.Direction(sx, sz, f.X, f.Z, out float dx, out float dz)) continue;
                        LineCount(bodies, sx, sz, dx, dz, body, travel, half, f.Unit, out int enemies, out int allies, out bool keeps);
                        if (!keeps || enemies <= here) continue;
                        spots.Add(new KeyValuePair<float, LineUp>(enemies - 1.5f * allies - 0.05f * walk,
                            new LineUp { Aim = f.Unit, Near = a.Unit, Spot = spot, Enemies = enemies, Allies = allies }));
                    }
                if (spots.Count == 0) return plans;
                var others = ctx.Allies.Concat(ctx.Enemies).Where(u => u != unit).ToList();
                var aims = new HashSet<UnitEntityData>();
                foreach (var entry in spots.OrderByDescending(s => s.Key))
                {
                    if (plans.Count >= LineUpSpots) break;
                    var plan = entry.Value;
                    if (!aims.Add(plan.Aim)) continue;   // one spot per aimed enemy: the best
                    var spot = plan.Spot;
                    bool ok = UnitCommand.IsUnitCloseEnough(plan.Aim.Position, spot, spot + eyeOffset, range, needLos, 0f) &&
                              !others.Any(u => Vector3.Distance(u.Position, spot) < (u.Corpulence + body) * 0.9f) &&
                              (!CombatMemory.IsRealTime || TakenAt(ctx, spot, plan.Aim) == null) &&
                              AoeDangerAnalyzer.EnteringDanger(unit, unit.Position, spot, out _) <= 0f &&
                              AoeDangerAnalyzer.IsPositionValid(unit.Position, spot) &&
                              StruckBySwarm(ctx, spot) == null;
                    if (!ok) { aims.Remove(plan.Aim); continue; }
                    plan.Risk = MoveRisk(ctx, spot, alwaysExposure: true);
                    if (plan.Risk?.Area != null) { aims.Remove(plan.Aim); continue; }
                    plans.Add(plan);
                }
                s_LineUpsOffered += plans.Count;
            }
            catch (Exception ex)
            {
                ReportOnce($"line-up|{ability.Blueprint.AssetGuid}|{ex.GetType().Name}",
                    $"[Line] {ability.Name}: lined-up spots unavailable ({ex.GetType().Name}: {ex.Message})");
            }
            return plans;
        }

        /// <summary>For the fight summary: lined-up spots offered and walks taken (session totals).</summary>
        internal static string LineUpSummary() => $"line-ups offered {s_LineUpsOffered}, walked {s_LineUpsTaken}";
    }
}
