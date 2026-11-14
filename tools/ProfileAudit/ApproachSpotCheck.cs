// ★ S2 (docs/TACTICAL_AI.md §163): the native contracts the turn-based free-side approach rests on, the compiled routes of
// the change, and the scalar rules through the compiled mod. No actor, path search, command or Unity execution.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.TurnBasedMode.Pathfinding;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Owlcat.Runtime.Core.Utils;
using UnityEngine;

namespace ProfileAudit
{
    internal static class ApproachSpotCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                            BindingFlags.DeclaredOnly;

        private static List<MemberInfo> Body(Type type, string name, int parameters = -1)
        {
            var method = type.GetMethods(Hidden).FirstOrDefault(m => m.Name == name && (parameters < 0 || m.GetParameters().Length == parameters))
                         ?? throw new MissingMethodException(type.FullName, name);
            return SaveBoostCheck.ReferencedMembers(method).ToList();
        }

        /// <summary>The method's IL holds a call (0x28) to <paramref name="callee"/> of the same module, by its metadata token.</summary>
        private static bool CallsToken(MethodInfo method, MethodInfo callee)
        {
            if (method == null || callee == null || method.Module != callee.Module) return false;
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            var token = BitConverter.GetBytes(callee.MetadataToken);
            for (int i = 0; i + 4 < il.Length; i++)
                if (il[i] == 0x28 && il[i + 1] == token[0] && il[i + 2] == token[1] && il[i + 3] == token[2] && il[i + 4] == token[3]) return true;
            return false;
        }

        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("APPROACH-SPOT FAILED: " + why); } }
            try
            {
                // --- native: what "in reach" is to a command, and where the turn-based path search ends ---
                var closeEnough = Body(typeof(UnitCommand), "IsUnitCloseEnough", 6);
                Check(Calls(closeEnough, typeof(GeometryUtils), "SqrMechanicsDistance") && Calls(closeEnough, typeof(UnitCommand), "SightIsClear"),
                      "UnitCommand.IsUnitCloseEnough is the game's mechanics distance and its sight test");
                var grid = typeof(ABPathWithClearance.LosPathConditionForGrid);
                var found = Body(grid, "TargetFound");
                Check(found.Any(m => m.Name == "get_AllyBlocked") && Calls(found, typeof(GeometryUtils), "SqrMechanicsDistance") &&
                      found.Any(m => m.Name == "HasObstacle"),
                      "the turn-based path search ends on a cell centre in the radius that no ally holds and that has sight");
                Check(found.Any(m => m.DeclaringType == typeof(ABPathWithClearance.MaxDistanceEndingCondition) && m.Name == "TargetFound"),
                      "... or, before any such cell, on its base condition");
                var baseFound = Body(typeof(ABPathWithClearance.MaxDistanceEndingCondition), "TargetFound");
                Check(baseFound.Any(m => m.Name == "TargetFound" && m.DeclaringType != typeof(ABPathWithClearance.MaxDistanceEndingCondition)),
                      "the base condition is the search's end node (the free cell nearest the goal) or the distance cap: no other side is tried");
                Check(typeof(UnitCommand).GetProperty("AwaitMovementFinish", Hidden) != null &&
                      new[] { typeof(UnitAttack), typeof(UnitUseAbility) }.All(t => t.GetProperty("AwaitMovementFinish", Hidden) == null),
                      "attacks and casts act as soon as the game's test passes (neither overrides AwaitMovementFinish)");

                // --- native scalars: the game's distance is flat while the height differs little ---
                Check(Math.Abs(GeometryUtils.MechanicsDistance(Vector3.zero, new Vector3(3f, 1.4f, 4f)) - 5f) < 1e-4f,
                      "a height difference of 1.4 m does not lengthen the game's distance (3-4-5 flat)");
                Check(Math.Abs(GeometryUtils.MechanicsDistance(Vector3.zero, new Vector3(0f, 4f, 0.5f)) - 2f) < 1e-4f,
                      "beyond 1.5 m of height the game's distance is at least half the height");
                Check(Vector3.Distance(Vector3.zero, new Vector3(3f, 1.4f, 4f)) > 5.15f,
                      "the straight distance of the same pair is longer: the old shortfall text overstated it on slopes");

                // --- compiled routes of the change ---
                var applies = typeof(SpotRules).GetMethod("Applies", BindingFlags.Public | BindingFlags.Static);
                Check(applies != null && applies.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(float), typeof(bool) }),
                      "SpotRules.Applies no longer takes the combat mode");
                // The large engine methods name members of assemblies the offline harness does not carry: their IL is
                // searched for each call's own token instead of resolving every member.
                MethodInfo Own(Type type, string name) => type.GetMethods(Hidden).First(m => m.Name == name);
                var plan = Own(typeof(TacticalEngine), "PlanApproach");
                Check(CallsToken(plan, applies) && CallsToken(plan, Own(typeof(TacticalEngine), "CellSpot")) &&
                      CallsToken(plan, Own(typeof(TacticalEngine), "CellFreeAt")) && CallsToken(plan, Own(typeof(TacticalEngine), "TakenAt")),
                      "PlanApproach reads spot claims and looks for a free cell");
                var cellSpot = Body(typeof(TacticalEngine), "CellSpot");
                Check(Calls(cellSpot, typeof(CompanionAI_Pathfinder.Analysis.BattlefieldGrid), "CellAt") && Calls(cellSpot, typeof(SpotRules), "CellFree") &&
                      Calls(cellSpot, typeof(UnitCommand), "IsUnitCloseEnough"),
                      "a free side is a grid cell, free by the game's rule, with the target in reach and sight by the game's test");
                Check(cellSpot.OfType<FieldInfo>().Any(f => f.DeclaringType == typeof(SpotRules) && f.Name == "CellReachMargin") ||
                      SpotRules.CellReachMargin > SpotRules.CellArrival,
                      "the reach test spares more than the walk's arrival radius");
                var verdict = Body(typeof(CommandDispatcher), "PathEndVerdict");
                Check(Calls(verdict, typeof(UnitCommand), "IsUnitCloseEnough") && Calls(verdict, typeof(GeometryUtils), "MechanicsDistance") &&
                      Calls(verdict, typeof(SpotRules), "JudgePathEnd"),
                      "the path-end verdict uses the game's test and the game's distance");
                var prepare = Own(typeof(CommandDispatcher), "TryPrepare");
                Check(CallsToken(prepare, Own(typeof(CommandDispatcher), "PathEndVerdict")) &&
                      CallsToken(prepare, Own(typeof(CommandDispatcher), "TryAttachPath")),
                      "preparation judges the end of the path it attached");
                foreach (string entry in new[] { "Run", "CanPrepare" })
                    Check(typeof(CommandDispatcher).GetMethods(BindingFlags.Public | BindingFlags.Static)
                              .Any(m => m.Name == entry && m.GetParameters().Any(p => p.Name == "oneMoveWalk" && p.HasDefaultValue && Equals(p.DefaultValue, false))),
                          $"CommandDispatcher.{entry} takes the one-move walk rule, off by default");
                Check(CallsToken(Own(typeof(TacticalEngine), "Execute"), Own(typeof(TacticalEngine), "OneMoveWalk")),
                      "the walk to a free side is issued under the one-move rule");

                // --- the scalar rules through the compiled mod ---
                Check(SpotRules.Applies(1.6f, false) && !SpotRules.Applies(9f, false) && !SpotRules.Applies(1.6f, true),
                      "contact approaches with movement left read spot claims, in either mode");
                Check(SpotRules.CellFree(true, 0) && !SpotRules.CellFree(true, 1) && !SpotRules.CellFree(false, 0),
                      "a cell is free when walkable and no ally holds it");
                Check(SpotRules.JudgePathEnd(false, 2.7f, 2.2f, true, out float shortBy) == SpotRules.PathEnd.Short && Math.Abs(shortBy - 0.5f) < 1e-4f,
                      "an end 0.5 m outside the radius is short by 0.5 m");
                Check(SpotRules.JudgePathEnd(false, 2.1f, 2.2f, true, out shortBy) == SpotRules.PathEnd.NoSight && shortBy == 0f,
                      "an end inside the radius that fails the test has no sight");
                Check(SpotRules.JudgePathEnd(true, 9f, 2.2f, true, out shortBy) == SpotRules.PathEnd.InReach,
                      "the game's own verdict wins");

                // --- the one-move rule of a free-side walk ---
                Check(CallsToken(Own(typeof(TacticalEngine), "OneMoveWalk"), typeof(SpotRules).GetMethod("OneMoveWalk")),
                      "the engine's one-move test is the pure rule");
                Check(SpotRules.OneMoveWalk(true, true, false, true), "a free-side walk before a standard action is held to one move in turn-based combat");
                Check(!SpotRules.OneMoveWalk(true, true, true, true), "real time keeps its own walk rules");
                Check(!SpotRules.OneMoveWalk(true, false, false, true), "a walk round an area keeps its existing rule");
                Check(!SpotRules.OneMoveWalk(false, true, false, true) && !SpotRules.OneMoveWalk(true, true, false, false),
                      "a plain advance to a free side spends nothing it was priced for");
                Check(typeof(Candidate).GetField("FreeSideWalk") != null, "the candidate carries the free-side flag");
            }
            catch (Exception ex) { bad++; lines.Add("APPROACH-SPOT FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"APPROACH-SPOT {checks} checks, failures {bad}; native IL contracts, native distance scalars, compiled routes and " +
                            "the mod's scalar rules; no actor, path search, command or Unity execution");
            return lines;
        }
    }
}
