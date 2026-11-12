// Selected exit path data and compiled native routes. No scene/actor, navmesh query or actual command executes here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using UnityEngine;

namespace ProfileAudit
{
    internal static class AreaExitCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("AREA-EXIT FAILED: " + why); } }
            try
            {
                const BindingFlags hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var method = typeof(AoeDangerAnalyzer).GetMethod("ValidateExitPath", hidden);
                var validate = (Func<IList<Vector3>, Func<Vector3, bool>, float, string>)method.CreateDelegate(
                    typeof(Func<IList<Vector3>, Func<Vector3, bool>, float, string>));
                int queried = 0;
                Func<Vector3, bool> outside = p => { queried++; return p.x > 5f; };
                Check(validate(null, outside, 9f) != null && queried == 0, "missing route cannot earn escape value");
                Check(validate(Array.Empty<Vector3>(), outside, 9f) != null, "empty native route is unavailable");
                Check(validate(new[] { Vector3.zero, new Vector3(4.8f, 0f, 0f) }, outside, 9f) != null,
                    "native cutback ending inside cannot inherit requested destination's escape value");
                Check(validate(new[] { Vector3.zero, new Vector3(6f, 0f, 0f) }, outside, 9f) == null,
                    "reachable clear endpoint is permitted");
                Check(validate(new[] { Vector3.zero, new Vector3(6f, 0f, 0f), new Vector3(4f, 0f, 0f) }, outside, 12f) != null,
                    "an outside intermediate waypoint does not prove the final endpoint is clear");
                Vector3 observed = default;
                validate(new[] { new Vector3(0f, 42.45f, 0f), new Vector3(2f, 39.68f, 0f) }, p => { observed = p; return false; }, 9f);
                Check(observed.y == 39.68f && observed.x == 2f, "clearance checks receive returned ground endpoint, not actor/request height");
                var detour = new[] { Vector3.zero, new Vector3(6f, 0f, 0f), new Vector3(6f, 0f, 4f) };
                Check(validate(detour, p => true, 8f) != null, "detour beyond native movement quote is not credited as an exit this action");
                Check(validate(detour, p => true, 12f) == null, "affordable native detour is retained");
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    Check(validate(new[] { Vector3.zero, new Vector3(invalid, 0f, 0f) }, outside, 9f) != null,
                        "nonfinite native endpoint is unavailable");
                foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                    Check(validate(new[] { Vector3.zero, new Vector3(6f, 0f, 0f) }, outside, invalid) != null,
                        "unknown/nonpositive movement budget cannot buy an exit");
                bool liveClear = true;
                var route = new[] { Vector3.zero, new Vector3(6f, 0f, 0f) };
                Func<Vector3, bool> current = p => liveClear;
                Check(validate(route, current, 9f) == null, "preflight can accept a currently clear endpoint");
                liveClear = false;
                Check(validate(route, current, 9f) != null, "issue-time recheck observes changed area clearance");

                var smoke = loaded.OfType<BlueprintAbilityAreaEffect>().FirstOrDefault(b => b.name == "BrimorakSmokeBreathAreaEffect");
                if (smoke == null) lines.Add("AREA-EXIT smoke fixture outside scope");
                else Check(smoke.Shape == AreaEffectShape.Cylinder && Math.Abs(smoke.Size.Meters - 1.524f) < 0.001f,
                    "actual supplied Brimorak smoke has a native five-foot cylinder graph");
                var field = typeof(Candidate).GetField("PathConstraint", hidden);
                foreach (string caller in new[] { "AddLeaveAreaCandidate", "Execute" })
                    Check(SaveBoostCheck.ReferencedMembers(typeof(TacticalEngine).GetMethod(caller, hidden)).Any(m =>
                        m.Module == field.Module && m.MetadataToken == field.MetadataToken),
                        "compiled " + caller + " propagates the decision-local exit path constraint");
                bool Calls(Type owner, string name, Type target, string member) =>
                    SaveBoostCheck.ReferencedMembers(owner.GetMethod(name, hidden)).Any(m => m.DeclaringType == target && m.Name == member);
                Check(Calls(typeof(AoeDangerAnalyzer), "FindExit", typeof(AoeDangerAnalyzer), "TryProjectExit"),
                    "exit search routes candidate hints through projection before ranking");
                Check(Calls(typeof(AoeDangerAnalyzer), "TryProjectExit", typeof(Kingmaker.View.ObstacleAnalyzer), "GetNearestNode"),
                    "projection uses supplied native nearest surface query");
                Check(SaveBoostCheck.ReferencedMembers(typeof(AoeDangerAnalyzer).GetMethod("TryProjectExit", hidden)).Any(m =>
                    m.DeclaringType?.FullName == "Pathfinding.NavmeshBase" && m.Name == "Linecast"),
                    "projection uses native linecast with shared start node");
                var constraintType = typeof(Func<IList<Vector3>, string>);
                foreach (string caller in new[] { "TryPrepare", "RealTimeWalkRefused" })
                    Check(Calls(typeof(CommandDispatcher), caller, typeof(CommandDispatcher), "PathConstraintFailure"),
                        "compiled " + caller + " evaluates the selected native path constraint");
                Check(Calls(typeof(CommandDispatcher), "PathConstraintFailure", constraintType, "Invoke"),
                    "compiled guard invokes the actual supplied constraint");
                var guard = (Func<Func<IList<Vector3>, string>, IList<Vector3>, string>)typeof(CommandDispatcher)
                    .GetMethod("PathConstraintFailure", hidden).CreateDelegate(typeof(Func<Func<IList<Vector3>, string>, IList<Vector3>, string>));
                Check(guard(null, null) == null, "unrelated moves have no active constraint");
                Check(guard(p => throw new InvalidOperationException("fixture"), route) != null,
                    "constraint exception becomes an ordinary refusal, allowing native path-claim cleanup");
            }
            catch (Exception ex) { bad++; lines.Add("AREA-EXIT FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"AREA-EXIT {checks} checks, failures {bad}; actual smoke graph, path samples and compiled native routes; no actor, navmesh query, command or Unity timing execution");
            return lines;
        }
    }
}
