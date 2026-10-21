// ★ v0.2.119: Single entry point for every unit command the mod issues.
//
// Root cause this fixes (verified against Assembly-CSharp, WotR 2.x):
//   UnitCommand.TickApproaching(), in turn-based combat for a unit that is in combat, never path-finds on its
//   own. It follows `ForcedPath`, and if that is null it falls back to
//   `PathVisualizer.Instance.CurrentPathForUnit(view)` - the path the UI predicted to the MOUSE CURSOR for the
//   directly-controllable current unit (TurnController.UpdateActionPredictions -> DefaultPointerController.WorldPosition).
//   If neither exists the command is interrupted ("forcedPath was null or empty").
//   The game's own AI therefore always sets `ForcedPath` (AiBrainController.SelectAction: ForcedPath = context.BestPath,
//   computed by AiAction.ScorePath via UnitMovementAgent.FindPath + AstarPath.BlockUntilCalculated).
//   Commands issued by the mod had no ForcedPath, so companions either walked along the mouse-hover path or the
//   command was silently interrupted (Commands.Empty == true right after "Command registered").
//
// This class reproduces what native AI does - compute the path with the game's pathfinder and attach it as
// ForcedPath - and nothing more. Real-time combat and out-of-combat movement do not use ForcedPath
// (TickApproaching calls view.MoveTo there), so commands are passed through unchanged in those modes.
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Pathfinding;
using Pathfinding.Util;
using TurnBased.Controllers;
using UnityEngine;

namespace CompanionAI_Pathfinder.GameInterface
{
    public static class CommandDispatcher
    {
        /// <summary>Claim key used while our path is being calculated (mirrors DecisionContext's claiming).</summary>
        private const string PathClaimKey = "companionai_dispatcher_path";

        /// <summary>
        /// Initialise, (in turn-based combat) attach a game-computed path, and run the command.
        /// Returns false with a reason when the command could not be issued or was rejected immediately.
        /// </summary>
        /// <param name="avoidAreas">★ v0.3.17: the AI priced this move as entering no harmful area: refuse it when the
        /// game's path does (turn-based, where the path is computed here). Camellia walked into Grease to touch a summon
        /// along a path that bent through it, while the move had been priced along a straight line that did not.</param>
        /// <param name="fiveFootOnly">★ v0.3.18 (test10): turn-based, the move was chosen as a 5-foot step (it provokes
        /// nothing and keeps the full attack): refused when the real path is longer than the step allows. The step was
        /// priced as a straight line; around an ally or a corner the path grew to 3.3 m and 6.6 m, became a normal move
        /// (ApplyTurnBasedMovementLimit) and provoked - Aeiluvetar went down from full HP.</param>
        public static bool Run(UnitEntityData unit, UnitCommand command, string label, out string failure, bool avoidAreas = false,
                               bool fiveFootOnly = false)
        {
            failure = null;
            if (unit == null || command == null)
            {
                failure = "null unit or command";
                return false;
            }

            try
            {
                // UnitCommands.Run() calls Init() only when Executor is null; Init() is where commands compute
                // ApproachRadius (UnitAttack/UnitUseAbility.Init), which we need before path-finding.
                if (command.Executor == null)
                    command.Init(unit);

                float pathLength = 0f;
                bool hasPath = false;
                if (NeedsPrecomputedPath(unit, command))
                {
                    // Native AiAction.ScorePath rejects actions that need movement when none is left
                    // (GetRemainingMovementRange(total: true) <= 0). Without this the command is accepted, cannot
                    // move, and UnitCommandController force-finishes it as "Success" without acting.
                    TurnController turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
                    if (turn != null && turn.Rider == unit && turn.GetRemainingMovementRange(unit, true, false) <= 0f)
                    {
                        failure = "target out of reach and no movement left this turn";
                        CommandTrace.LogRejected(unit, command, label, failure);
                        return false;
                    }

                    if (!TryAttachPath(unit, command, out pathLength, out failure))
                    {
                        CommandTrace.LogRejected(unit, command, label, failure);
                        return false;
                    }

                    if (fiveFootOnly && turn != null && turn.Rider == unit &&
                        (!turn.HasFiveFootStep(unit) || pathLength > turn.GetRemainingFiveFootStepRange(unit) + 0.05f))
                    {
                        command.ForcedPath = null;
                        failure = $"the 5-foot step's path is {pathLength:F1}m - longer than the step";
                        CommandTrace.LogRejected(unit, command, label, failure);
                        return false;
                    }

                    // ★ v0.3.10: once a 5-foot step was taken, the game allows no other movement this turn
                    // (TurnController.ShouldRestrictNormalMovement). Lann stepped out of melee and then had three commands
                    // force-finished without acting; Albedo issued the same Advance fifteen times.
                    if (turn != null && turn.Rider == unit && turn.GetMetersMovedByFiveFootStep(unit) > 0f &&
                        pathLength > turn.GetRemainingFiveFootStepRange(unit) + 0.05f)
                    {
                        command.ForcedPath = null;
                        failure = $"a 5-foot step was taken this turn: {pathLength:F1}m of movement is not allowed";
                        CommandTrace.LogRejected(unit, command, label, failure);
                        return false;
                    }

                    // ★ v0.3.10: the path must end within reach, as native AI checks (AiAction.ScorePath:
                    // UnitCommand.IsUnitCloseEnough on the last waypoint). A path trimmed short by an ally standing at its
                    // end left Seelah, Albedo and Camellia next to their target with the attack force-finished unstarted.
                    if (!(command is UnitMoveTo) && !PathEndsInReach(unit, command, out float shortBy))
                    {
                        command.ForcedPath = null;
                        failure = $"path ends out of reach ({shortBy:F1}m short)";
                        CommandTrace.LogRejected(unit, command, label, failure);
                        return false;
                    }

                    if (avoidAreas && PathEntersArea(unit, command, out string area))
                    {
                        command.ForcedPath = null;
                        failure = $"the path walks into {area}";
                        CommandTrace.LogRejected(unit, command, label, failure);
                        return false;
                    }

                    // ★ v0.3.0: a full attack is only legal after at most a 5-foot step; ForceFullAttack bypasses the
                    // game's own check, so enforce it here before anything moves.
                    if (command is UnitAttack forcedFull && forcedFull.ForceFullAttack && turn != null &&
                        (!turn.HasFiveFootStep(unit) || pathLength > turn.GetRemainingFiveFootStepRange(unit) + 0.05f))
                    {
                        command.ForcedPath = null;
                        failure = $"full attack needs {pathLength:F1}m of movement (more than a 5-foot step)";
                        CommandTrace.LogRejected(unit, command, label, failure);
                        return false;
                    }
                    hasPath = true;
                    ApplyTurnBasedMovementLimit(unit, pathLength);
                }
                // ★ v0.3.18 (test10): real time checks the walk against harmful areas too. The game's real-time approach
                // follows its own navmesh path, which bends around obstacles where the straight line the move was priced
                // on did not: Seelah and Camellia walked into Grease that the straight line missed and lay prone for most
                // of the fight (0.3.18-test9 real-time log). The path is asked of the pathfinder on its own (not the
                // unit's seeker, which may be steering the unit right now) and only checked, never attached: the game
                // still walks its own path; a refused option makes the engine take the next best one.
                else if (avoidAreas && RealTimePathEntersArea(unit, command, out string rtArea))
                {
                    failure = $"{WalksIntoAreaFailure} {rtArea}";
                    CommandTrace.LogRejected(unit, command, label, failure);
                    return false;
                }

                unit.Commands.Run(command);

                // UnitCommands.Run() returns silently when CanRunCommand() fails (unconscious, CantUseStandardActions)
                // or when a special handler consumed the command; in both cases our instance is not in a slot.
                if (!IsInSlots(unit, command))
                {
                    failure = command.IsFinished ? $"command ended immediately ({command.Result})" : "command not accepted by UnitCommands.Run";
                    CommandTrace.LogRejected(unit, command, label, failure);
                    return false;
                }

                CommandTrace.Track(unit, command, label, hasPath ? pathLength : (float?)null);
                return true;
            }
            catch (Exception ex)
            {
                failure = $"exception: {ex.Message}";
                Main.Error($"[Dispatch] {unit.CharacterName}: {label}: {ex}");
                return false;
            }
        }

        /// <summary>Convenience overload for call sites that only log failures.</summary>
        public static bool Run(UnitEntityData unit, UnitCommand command, string label)
        {
            return Run(unit, command, label, out _);
        }

        /// <summary>
        /// ★ v0.3.0: Attach a game-computed path to a command the GAME created for a unit (e.g. the mount command
        /// SaddledUnitController builds from a rider's order). Same rules as for our own commands.
        /// </summary>
        public static bool AttachPathIfNeeded(UnitEntityData executor, UnitCommand command, out float pathLength, out string failure)
        {
            pathLength = 0f;
            failure = null;
            if (executor == null || command == null) return false;
            if (!NeedsPrecomputedPath(executor, command)) return false;
            if (!TryAttachPath(executor, command, out pathLength, out failure)) return false;
            ApplyTurnBasedMovementLimit(executor, pathLength);
            return true;
        }

        /// <summary>The command's own arrival test (UnitCommand.IsUnitCloseEnough) applied to the end of its ForcedPath.</summary>
        private static bool PathEndsInReach(UnitEntityData unit, UnitCommand command, out float shortBy)
        {
            shortBy = 0f;
            try
            {
                var points = command.ForcedPath?.vectorPath;
                if (points == null || points.Count == 0) return true;
                Vector3 end = points[points.Count - 1];
                Vector3 eye = end + (unit.EyePosition - unit.Position);
                if (UnitCommand.IsUnitCloseEnough(command.ApproachPoint, end, eye, command.ApproachRadius, command.NeedLoS,
                                                  command.GetTargetLOSObjectFudgeRadius()))
                    return true;
                shortBy = Math.Max(0f, Vector3.Distance(end, command.ApproachPoint) - command.ApproachRadius);
                return false;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Dispatch] {unit.CharacterName}: reach check skipped: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// ★ v0.3.17: the command's ForcedPath, up to where the unit arrives (its own approach radius), walks into a
        /// harmful area it is not already in (AoeDangerAnalyzer.EnteringDanger, the same test the AI prices moves with).
        /// </summary>
        private static bool PathEntersArea(UnitEntityData unit, UnitCommand command, out string area) =>
            PathEntersArea(unit, command.ForcedPath?.vectorPath, command, out area);

        /// <summary>The start of the failure text of a real-time walk refused for a harmful area.</summary>
        public const string WalksIntoAreaFailure = "the path walks into";

        /// <summary>★ v0.3.18 (test10): real time - the walk the game would take, asked of the pathfinder directly.</summary>
        private static bool RealTimePathEntersArea(UnitEntityData unit, UnitCommand command, out string area)
        {
            area = null;
            if (CombatController.IsInTurnBasedCombat() || !unit.IsInCombat || unit.RiderPart != null) return false;
            if (command is UnitMoveAlongPath || !command.ShouldUnitApproach) return false;
            ABPath path = null;
            try
            {
                // Only when a harmful (or pending) area lies near the straight line: the circle around the
                // midpoint covers every walk that does not detour far, and a far detour leaves the check unneeded.
                Vector3 start = unit.Position, goal = command.ApproachPoint;
                float half = Vector3.Distance(start, goal) * 0.5f;
                if (!Analysis.AoeDangerAnalyzer.HarmfulAreaNear(unit, (start + goal) * 0.5f, half + 3f)) return false;
                path = ABPath.Construct(start, goal, null);
                path.nnConstraint.graphMask = 1;   // the walkable navmesh, as the game's own movement queries
                path.Claim(PathClaimKey);
                AstarPath.StartPath(path, pushToFront: true);
                AstarPath.BlockUntilCalculated(path);
                if (path.error || path.CompleteState != PathCompleteState.Complete || path.vectorPath == null || path.vectorPath.Count == 0)
                    return false;   // no answer: the move is not refused for it
                return PathEntersArea(unit, GameWalk(path, start), command, out area);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Dispatch] {unit.CharacterName}: real-time path check skipped: {ex.Message}");
                return false;
            }
            finally
            {
                path?.Release(PathClaimKey, false);
            }
        }

        /// <summary>
        /// ★ v0.3.18 (test11): the walk a real-time unit takes along a computed path. A path without the seeker's modifiers
        /// is the centres of the navmesh triangles it crosses, from the centre of the unit's own triangle to the centre of
        /// the goal's - a zigzag that cut into the Stinking Cloud beside spots placed at its edge, and refused 22 approaches
        /// the straight line had cleared (Lann idle for two rounds). The game's movement agent starts at the unit's own
        /// position, ends at the closest point to the goal on the last triangle (Seeker.startEndModifier) and pulls the
        /// corners tight with the funnel modifier out of turn-based combat (UnitMovementAgent): the same here.
        /// </summary>
        private static List<Vector3> GameWalk(ABPath path, Vector3 start)
        {
            var points = path.vectorPath;
            if (points.Count == 1) points.Add(points[0]);
            points[0] = start;
            points[points.Count - 1] = path.endPoint;
            var walk = new List<Vector3>();
            List<Funnel.PathPart> parts = null;
            try
            {
                parts = Funnel.SplitIntoParts(path);
                if (parts.Count == 0) return points;
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    if (!part.isLink)
                    {
                        var funnel = Funnel.ConstructFunnelPortals(path.path, part);
                        var corners = Funnel.Calculate(funnel, true, false);
                        walk.AddRange(corners);
                        ListPool<Vector3>.Release(ref funnel.left);
                        ListPool<Vector3>.Release(ref funnel.right);
                        ListPool<Vector3>.Release(ref corners);
                    }
                    else
                    {
                        if (i == 0 || parts[i - 1].isLink) walk.Add(part.startPoint);
                        if (i == parts.Count - 1 || parts[i + 1].isLink) walk.Add(part.endPoint);
                    }
                }
                return walk.Count > 0 ? walk : points;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Dispatch] funnel skipped: {ex.Message}");
                return points;
            }
            finally
            {
                if (parts != null) ListPool<Funnel.PathPart>.Release(ref parts);
            }
        }

        private static bool PathEntersArea(UnitEntityData unit, List<Vector3> points, UnitCommand command, out string area)
        {
            area = null;
            try
            {
                if (points == null || points.Count == 0) return false;
                Vector3 target = command.ApproachPoint;
                float radius = Mathf.Max(0f, command.ApproachRadius);
                Vector3 from = unit.Position;
                foreach (var to in points)
                {
                    // Stop where the unit arrives: the first spot on the segment within its approach radius of the target
                    // (flat), sampled every 0.25 m - an enemy standing in the area at the path's end is attacked from
                    // outside it.
                    Vector3 end = to;
                    bool arrives = false;
                    float length = Vector3.Distance(from, to);
                    int steps = Math.Max(1, Mathf.CeilToInt(length / 0.25f));
                    for (int i = 1; i <= steps; i++)
                    {
                        Vector3 p = Vector3.Lerp(from, to, i / (float)steps);
                        Vector3 flat = p - target;
                        flat.y = 0f;
                        if (flat.magnitude > radius) continue;
                        end = p;
                        arrives = true;
                        break;
                    }
                    if (Analysis.AoeDangerAnalyzer.EnteringDanger(unit, from, end, out string name) > 0f)
                    {
                        area = name ?? "a harmful area";
                        return true;
                    }
                    if (arrives) break;
                    from = end;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Dispatch] {unit.CharacterName}: area check of the path skipped: {ex.Message}");
            }
            return false;
        }

        private static bool IsInSlots(UnitEntityData unit, UnitCommand command)
        {
            foreach (var cmd in unit.Commands.Raw)
            {
                if (ReferenceEquals(cmd, command))
                    return !cmd.IsFinished;
            }
            return false;
        }

        /// <summary>
        /// Same condition under which UnitCommand.TickApproaching() requires ForcedPath:
        /// turn-based combat, executor in combat, command not a UnitMoveAlongPath, and the unit has to move.
        /// </summary>
        private static bool NeedsPrecomputedPath(UnitEntityData unit, UnitCommand command)
        {
            if (!CombatController.IsInTurnBasedCombat()) return false;
            if (!unit.IsInCombat) return false;
            if (command.ForcedPath != null) return false;
            if (command is UnitMoveAlongPath) return false;
            // Riders do not move themselves (TickApproaching sets IsMoveUnit and returns); the mount's command
            // is generated by SaddledUnitController. Leave mounted units to the game.
            if (unit.RiderPart != null) return false;
            return command.ShouldUnitApproach;
        }

        /// <summary>
        /// Path-finds exactly like AiAction.ScorePath: UnitMovementAgent.FindPath(destination, cb, approachRadius, needLOS)
        /// followed by AstarPath.BlockUntilCalculated, then attaches it as the command's ForcedPath.
        /// </summary>
        private static bool TryAttachPath(UnitEntityData unit, UnitCommand command, out float pathLength, out string failure)
        {
            pathLength = 0f;
            failure = null;

            var agent = unit.View?.AgentASP;
            if (agent == null)
            {
                failure = "unit has no movement agent";
                return false;
            }

            Vector3 destination = command.ApproachPoint;
            float approachRadius = Mathf.Max(0f, command.ApproachRadius);

            // FindPath combines the callback with its own via Delegate.Combine, so null is valid.
            Path path = agent.FindPath(destination, null, approachRadius, command.NeedLoS, command.GetTargetLOSObjectFudgeRadius());
            if (path == null)
            {
                // FindPath returns null while a previous request of this agent is still pending.
                failure = "path request already pending for this unit";
                return false;
            }

            path.Claim(PathClaimKey);
            try
            {
                AstarPath.BlockUntilCalculated(path);

                if (path.error || path.CompleteState != PathCompleteState.Complete || path.vectorPath == null || path.vectorPath.Count == 0)
                {
                    failure = $"no path to ({destination.x:F1},{destination.z:F1}) state={path.CompleteState}";
                    return false;
                }

                path = FixPathIfTooShort(unit, path, destination, approachRadius);
                TrimAllyBlockedEnd(path);

                if (path.vectorPath.Count == 0)
                {
                    failure = "path empty after trimming";
                    return false;
                }

                pathLength = GetLength(path.vectorPath);
                command.ForcedPath = path; // ForcedPath setter claims the path for the command
                return true;
            }
            finally
            {
                path.Release(PathClaimKey, false);
            }
        }

        /// <summary>
        /// Port of AiAction.FixPathIfTooShort: when start and end resolve to the same node, the seeker returns a
        /// degenerate path; replace it with a straight two-point path that stops at the approach radius.
        /// </summary>
        private static Path FixPathIfTooShort(UnitEntityData unit, Path path, Vector3 destination, float approachRadius)
        {
            var points = path.vectorPath;
            if (points.Count > 2) return path;

            Vector3 delta = points.Count == 2 ? points[0] - points[1] : Vector3.zero;
            if (delta.x * delta.x + delta.z * delta.z > 0.01f) return path;

            Vector3 start = unit.View.AgentASP.transform.position;
            Vector3 toDest = destination - start;
            float distance = toDest.magnitude;
            if (distance > 0.01f)
            {
                float travel = Mathf.Clamp(distance - approachRadius + 0.01f, 0f, distance);
                destination = start + toDest.normalized * travel;
            }

            path.Release(PathClaimKey, false);
            Path fake = ABPath.FakePath(new List<Vector3> { start, destination }, null);
            fake.Claim(PathClaimKey);
            return fake;
        }

        /// <summary>
        /// Native ScorePath never lets a unit end its move on an ally-occupied node; drop such trailing points.
        /// </summary>
        private static void TrimAllyBlockedEnd(Path path)
        {
            var points = path.vectorPath;
            try
            {
                while (points.Count > 1)
                {
                    // ★ v0.3.16: the cell itself. With NNConstraint.Default the lookup skips ally-blocked nodes (Suitable:
                    // constrainAllyBlocked), so the end was never found blocked and this trim never happened.
                    var node = Analysis.BattlefieldGrid.CellAt(points[points.Count - 1]);
                    if (node == null || node.AllyBlocked <= 0) break;
                    points.RemoveAt(points.Count - 1);
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Dispatch] TrimAllyBlockedEnd skipped: {ex.Message}");
            }
        }

        /// <summary>
        /// For the directly-controllable current unit, UnitMovementAgent clamps movement through
        /// TurnController.TickMovement using CurrentMovementLimit - a UI value that the smart cursor changes while the
        /// mouse hovers enemies (UpdateSmartCursorVariants may select FiveFootStep). Choose the limit the way native
        /// AI does (AiAction.ScorePath: five-foot step when the whole path fits in it) instead of inheriting it from
        /// the cursor. TwoActions + TickMovement(total:false) still spends only the move action while the standard
        /// action is unused, so a following attack/spell is preserved.
        /// </summary>
        private static void ApplyTurnBasedMovementLimit(UnitEntityData unit, float pathLength)
        {
            TurnController turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            if (turn == null || turn.Rider != unit || turn.IsMoving) return;

            TurnController.MovementLimit limit =
                (turn.HasFiveFootStep(unit) && pathLength <= turn.GetRemainingFiveFootStepRange(unit))
                    ? TurnController.MovementLimit.FiveFootStep
                    : TurnController.MovementLimit.TwoActions;

            if (turn.CurrentMovementLimit != limit)
            {
                Main.Verbose($"[Dispatch] {unit.CharacterName}: movement limit {turn.CurrentMovementLimit} -> {limit} (path {pathLength:F1}m)");
                turn.SetMovementLimit(limit);
            }
        }

        private static float GetLength(List<Vector3> points)
        {
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
                total += Vector3.Distance(points[i - 1], points[i]);
            return total;
        }
    }
}
