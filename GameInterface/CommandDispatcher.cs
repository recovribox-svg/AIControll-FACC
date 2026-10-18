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
        public static bool Run(UnitEntityData unit, UnitCommand command, string label, out string failure)
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
                    var node = AstarPath.active?.GetNearest(points[points.Count - 1], NNConstraint.Default).node;
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
