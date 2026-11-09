using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.View.MapObjects.SriptZones;
using CompanionAI_Pathfinder.Analysis;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static List<UnitEntityData> SwarmTargets(TacticalContext ctx)
        {
            // Native target selection excludes dead, not unconscious. Keep downed allies as possible burst anchors.
            if (ctx.DownedAllies.Count == 0) return ctx.Allies;
            if (ctx.SwarmRecipients != null) return ctx.SwarmRecipients;
            var all = new List<UnitEntityData>(ctx.Allies.Count + ctx.DownedAllies.Count);
            all.AddRange(ctx.Allies);
            foreach (var ally in ctx.DownedAllies) if (!all.Contains(ally)) all.Add(ally);
            return ctx.SwarmRecipients = all;
        }

        private static List<SwarmGeometry.Circle> SwarmExitCircles(TacticalContext ctx,
            List<(UnitEntityData swarm, CombatMath.SwarmSpace area)> striking)
        {
            var circles = new List<SwarmGeometry.Circle>();
            var who = ctx.Unit;
            foreach (var s in striking)
            {
                if (s.area.Cast != null) s.area.Cast.AddExitCircles(who, SwarmTargets(ctx), circles);
                else
                {
                    var area = s.area.Area;
                    // Read the actual live native shape, including any changed radius/centre. Other shapes retain the
                    // existing search. The final native Contains check is still mandatory for every chosen hint.
                    if (!(area?.View?.Shape is ScriptZoneCylinder cylinder) || cylinder.GetType() != typeof(ScriptZoneCylinder)) return null;
                    var transform = cylinder.transform;
                    var scale = transform.lossyScale;
                    if (Math.Abs(Math.Abs(scale.x) - Math.Abs(scale.z)) > .0001f ||
                        Math.Abs(Vector3.Dot(transform.up, Vector3.up)) < .9999f) return null;
                    // Contains inverse-transforms the point, then adds the native body in local space. Its world circle
                    // therefore scales the whole radius/body sum. Nonuniform or tilted cylinders are not world circles.
                    var centre = transform.TransformPoint(cylinder.Center);
                    circles.Add(new SwarmGeometry.Circle(centre.x, centre.z, Math.Abs(scale.x) * (cylinder.Radius + who.Corpulence)));
                }
            }
            return circles.Count > 0 ? circles : null;
        }

        /// <summary>Twelve directions, as before; one boundary sample per direction. Movement risk is priced only on
        /// the chosen legal spot. This changes only swarm exit search, not the general retreat/path/dispatch rules.</summary>
        private static Vector3? FindSwarmExitPoint(TacticalContext ctx, float maximum, bool step,
            List<SwarmGeometry.Circle> circles, Func<Vector3, bool> clear)
        {
            var unit = ctx.Unit;
            Vector3 centre = Vector3.zero;
            int allies = 0;
            foreach (var ally in ctx.Allies) if (ally != unit) { centre += ally.Position; allies++; }
            centre = allies > 0 ? centre / allies : unit.Position;
            List<UnitEntityData> bodies = null;
            if (step) { bodies = new List<UnitEntityData>(ctx.Allies); bodies.AddRange(ctx.Enemies); bodies.Remove(unit); }
            Vector3? best = null;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad, dx = Mathf.Cos(angle), dz = Mathf.Sin(angle);
                // Move commands may stop 0.3 m short. Add the existing 0.05 m geometry tolerance beyond that.
                float distance = SwarmGeometry.ExitDistance(circles, unit.Position.x, unit.Position.z, dx, dz, maximum, 0.35f);
                if (!(distance > 0f)) continue;
                var point = unit.Position + new Vector3(dx, 0f, dz) * distance;
                if (!clear(point) || AoeDangerAnalyzer.EnteringDanger(unit, unit.Position, point, out _) > 0f) continue;
                if (CombatMemory.IsRealTime && !AoeDangerAnalyzer.IsPositionValid(unit.Position, point)) continue;
                if (step && !StepIsClear(unit, point, bodies)) continue;
                float nearest = float.PositiveInfinity;
                foreach (var enemy in ctx.Enemies) nearest = Math.Min(nearest, Vector3.Distance(point, enemy.Position) - enemy.Corpulence);
                float score = nearest - 0.15f * Vector3.Distance(point, centre);
                if (score > bestScore) { bestScore = score; best = point; }
            }
            // Actual native hazard clearance, then the existing gain/cost competition, supplies the exit criterion.
            return best;
        }
    }
}
