// Author-approved area exits: native ground projection and selected-path endpoint validation only.
using System;
using System.Collections.Generic;
using Kingmaker.View;
using Pathfinding;
using UnityEngine;

namespace CompanionAI_Pathfinder.Analysis
{
    public static partial class AoeDangerAnalyzer
    {
        internal const float ExitArrivalRadius = 0.15f; // Same native UnitMoveTo stopping radius as the exit command.

        private static bool FiniteExitPoint(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
            !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);

        private static bool TryProjectExit(NNInfo start, Vector3 requested, float maximum, AreasInside inside, out Vector3 point)
        {
            point = default;
            if (!FiniteExitPoint(requested)) return false;
            // No hint: native GetNearestNode's hint shortcut preserves the supplied Y instead of projecting it.
            var end = ObstacleAnalyzer.GetNearestNode(requested, null);
            if (end.node == null || end.node.Area != start.node.Area || !ReferenceEquals(end.node.Graph, start.node.Graph) ||
                !(start.node.Graph is NavmeshBase graph)) return false;
            var projected = end.position;
            var shift = projected - requested; shift.y = 0f;
            if (!FiniteExitPoint(projected) || shift.sqrMagnitude > 4f || !inside.IsOutsideWithClearance(projected, ExitArrivalRadius)) return false;
            // Same native linecast as ObstacleAnalyzer.TraceAlongNavmesh, reusing this search's ground start node.
            NavmeshBase.Linecast(graph, start.position, projected, start.node, out var hit, null);
            point = hit.point;
            if (!FiniteExitPoint(point) || Vector3.Distance(point, projected) > 1f ||
                Vector3.Distance(start.position, point) > maximum + 0.05f) return false;
            return inside.IsOutsideWithClearance(point, ExitArrivalRadius);
        }

        internal static Func<IList<Vector3>, string> ExitPathCheck(AreasInside inside, float maximum)
        {
            Func<Vector3, bool> outside = p => inside.IsOutsideWithClearance(p, ExitArrivalRadius);
            return path => ValidateExitPath(path, outside, maximum);
        }

        internal static string ValidateExitPath(IList<Vector3> path, Func<Vector3, bool> outside, float maximum)
        {
            if (path == null || path.Count == 0) return "exit path unavailable";
            if (!(maximum > 0f) || float.IsInfinity(maximum)) return "exit movement budget unavailable";
            float distance = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                if (!FiniteExitPoint(path[i])) return "exit path has invalid geometry";
                if (i > 0) distance += Vector3.Distance(path[i - 1], path[i]);
            }
            if (distance > maximum + 0.05f) return "exit path exceeds its priced movement distance";
            return outside(path[path.Count - 1]) ? null : "exit path endpoint remains inside a current harmful area";
        }
    }
}
