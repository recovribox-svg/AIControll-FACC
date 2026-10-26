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
        /// <param name="pathAttackers">★ v0.3.20 (test8): counts the attacks of opportunity along a walk (the engine's own
        /// rule, TacticalEngine.PathAttackers); with <paramref name="pricedAttacks"/>, the number the move was priced for on
        /// the straight line. The walk the game will really take is counted, and a walk that provokes more is refused
        /// (<see cref="MoreAttacksOfOpportunityFailure"/>) so the engine prices the option again with it. A path around an
        /// ally or a corner can pass enemies the straight line did not.</param>
        public static bool Run(UnitEntityData unit, UnitCommand command, string label, out string failure, bool avoidAreas = false,
                               bool fiveFootOnly = false, Func<IList<Vector3>, List<UnitEntityData>> pathAttackers = null,
                               int pricedAttacks = 0)
        {
            failure = null;
            if (unit == null || command == null)
            {
                failure = "null unit or command";
                return false;
            }

            try
            {
                if (!TryPrepare(unit, command, out float pathLength, out bool hasPath, out failure,
                                avoidAreas, fiveFootOnly, pathAttackers, pricedAttacks, preflight: false))
                {
                    CommandTrace.LogRejected(unit, command, label, failure);
                    return false;
                }
                if (hasPath) ApplyTurnBasedMovementLimit(unit, pathLength);

                unit.Commands.Run(command);

                // UnitCommands.Run() returns silently when CanRunCommand() fails (unconscious, CantUseStandardActions)
                // or when a special handler consumed the command; in both cases our instance is not in a slot.
                // ★ v0.3.19 (test5): queued behind a command the game put first (Gather Power before a blast, the Kinetic
                // Blade's activation before an attack: UnitCommands.TryUseCustomHandler) is accepted - it runs when that
                // one ends. Read as refused, the engine issued its next option at once, and UnitCommands.Run cleared the
                // queue: the blast or attack was lost every time.
                if (!IsInSlots(unit, command) && unit.Commands.ContainsOrQueued(command) && !command.IsFinished)
                {
                    UnitCommand first = null;
                    foreach (var c in unit.Commands.Raw) if (c != null && !c.IsFinished) { first = c; break; }
                    Main.Log($"[Dispatch] {unit.CharacterName}: {label} queued behind {(first?.GetType().Name ?? "another command")}" +
                             (first is UnitUseAbility before ? $" ({before.Ability?.Name})" : ""));
                }
                else if (!IsInSlots(unit, command))
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

        /// <summary>
        /// ★ test1-scoring-fixes: validate one proposed approach without issuing a command, changing movement limits,
        /// activating a mode or retaining a path. Actual execution calls Run and validates again against current state.
        /// </summary>
        public static bool CanPrepare(UnitEntityData unit, UnitCommand command, out string failure, bool avoidAreas = false,
                                      bool fiveFootOnly = false, Func<IList<Vector3>, List<UnitEntityData>> pathAttackers = null,
                                      int pricedAttacks = 0)
        {
            failure = null;
            try
            {
                // Ordinary UnitAttack.Init configures animation handles/speed. The game's inspection mode does not;
                // restore only its native approach radius, which inspection intentionally leaves unset.
                if (command is UnitAttack attack && command.Executor == null)
                {
                    attack.OnlyForInspect = true;
                    attack.Init(unit);
                    attack.ApproachRadius = UnitAttack.GetApproachRadius(
                        attack.PlannedAttack?.Hand?.Weapon ?? unit.GetFirstWeapon(), unit, attack.Target);
                }
                return TryPrepare(unit, command, out _, out _, out failure,
                                  avoidAreas, fiveFootOnly, pathAttackers, pricedAttacks, preflight: true);
            }
            catch (Exception ex)
            {
                failure = $"approach validation unavailable: {ex.Message}";
                return false;
            }
            finally
            {
                // ForcedPath owns a claim on the native path; the preview must release it on every exit.
                if (command != null) command.ForcedPath = null;
            }
        }

        private static bool NeedsTemporaryApproachPath(UnitEntityData unit, UnitCommand command) =>
            unit != null && unit.IsInCombat && unit.RiderPart == null && command.ForcedPath == null &&
            !(command is UnitMoveAlongPath) && command.ShouldUnitApproach;

        /// <summary>Shared preparation rules. This method never changes live movement limits or command slots.</summary>
        private static bool TryPrepare(UnitEntityData unit, UnitCommand command, out float pathLength, out bool hasPath,
                                       out string failure, bool avoidAreas, bool fiveFootOnly,
                                       Func<IList<Vector3>, List<UnitEntityData>> pathAttackers, int pricedAttacks,
                                       bool preflight)
        {
            pathLength = 0f;
            hasPath = false;
            failure = null;
            if (unit == null || command == null)
            {
                failure = "null unit or command";
                return false;
            }
            // UnitCommands.Run() calls Init() only when Executor is null; Init() is where commands compute
            // ApproachRadius (UnitAttack/UnitUseAbility.Init), which we need before path-finding.
            if (command.Executor == null)
                command.Init(unit);

            if (NeedsPrecomputedPath(unit, command) || (preflight && NeedsTemporaryApproachPath(unit, command)))
            {
                // Native AiAction.ScorePath rejects actions that need movement when none is left
                // (GetRemainingMovementRange(total: true) <= 0). Without this the command is accepted, cannot
                // move, and UnitCommandController force-finishes it as "Success" without acting.
                TurnController turn = CombatController.IsInTurnBasedCombat()
                    ? Game.Instance?.TurnBasedCombatController?.CurrentTurn : null;
                if (turn != null && turn.Rider == unit && turn.GetRemainingMovementRange(unit, true, false) <= 0f)
                {
                    failure = "target out of reach and no movement left this turn";
                    return false;
                }

                if (!TryAttachPath(unit, command, out pathLength, out failure))
                {
                    return false;
                }

                if (fiveFootOnly && turn != null && turn.Rider == unit &&
                    (!turn.HasFiveFootStep(unit) || pathLength > turn.GetRemainingFiveFootStepRange(unit) + 0.05f))
                {
                    command.ForcedPath = null;
                    failure = $"the 5-foot step's path is {pathLength:F1}m - longer than the step";
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
                    return false;
                }

                // ★ v0.3.20 (0.3.19 release logs): a standard action after the walk - the walk must fit one move action
                // (TurnController.GetRemainingMovementTime with singleActionMove, the game's own split). A longer
                // path is walked with the standard action too, and the attack or spell is then force-finished
                // unstarted: Aeiluvetar (15.0 m), Lann (16.3 m), Orvath (15.7 m) and Camellia's Grease (13.0 m) lost
                // their turns in one round of the turn-based log, the navmesh path being far longer than the line
                // the option was priced on. Refused here, the engine takes its next best option.
                if (turn != null && turn.Rider == unit && command.Type == UnitCommand.CommandType.Standard &&
                    !(command is UnitAttack fullAttack && fullAttack.ForceFullAttack) &&
                    !(command is UnitUseAbility walkThenCast && (walkThenCast.Ability.RequireFullRoundAction ||
                                                                  Analysis.AbilityClassifier.IsChargeAbility(walkThenCast.Ability))) &&
                    !(turn.HasFiveFootStep(unit) && pathLength <= turn.GetRemainingFiveFootStepRange(unit) + 0.05f))
                {
                    float oneMove = turn.GetRemainingMovementTime(unit, false, true) * unit.CurrentSpeedMps;
                    if (pathLength > oneMove + 0.3f)
                    {
                        command.ForcedPath = null;
                        failure = $"the path ({pathLength:F1}m) is {LongerThanOneMoveFailure} ({oneMove:F1}m)";
                        return false;
                    }
                }

                // ★ v0.3.10: the path must end within reach, as native AI checks (AiAction.ScorePath:
                // UnitCommand.IsUnitCloseEnough on the last waypoint). A path trimmed short by an ally standing at its
                // end left Seelah, Albedo and Camellia next to their target with the attack force-finished unstarted.
                if (!(command is UnitMoveTo) && !PathEndsInReach(unit, command, out float shortBy))
                {
                    command.ForcedPath = null;
                    // ★ v0.3.20: within range at the end but no line of sight read "0.0m short".
                    failure = shortBy < 0.05f && command.NeedLoS ? NoSightAtPathEndFailure
                                                                 : $"{OutOfReachFailure} ({shortBy:F1}m short)";
                    return false;
                }

                if (avoidAreas && PathEntersArea(unit, command, out string area))
                {
                    command.ForcedPath = null;
                    failure = $"the path walks into {area}";
                    return false;
                }

                // ★ v0.3.0: a full attack is only legal after at most a 5-foot step; ForceFullAttack bypasses the
                // game's own check, so enforce it here before anything moves.
                if (command is UnitAttack forcedFull && forcedFull.ForceFullAttack && turn != null &&
                    (!turn.HasFiveFootStep(unit) || pathLength > turn.GetRemainingFiveFootStepRange(unit) + 0.05f))
                {
                    command.ForcedPath = null;
                    failure = $"full attack needs {pathLength:F1}m of movement (more than a 5-foot step)";
                    return false;
                }

                // A walk the game makes as a 5-foot step provokes nothing (ApplyTurnBasedMovementLimit below); a longer
                // one (an advance) goes only as far as this turn's movement. Its time left, not GetRemainingMovementRange:
                // that answers the 5-foot step's range while the limit set for the last command is still the step.
                bool ownTurn = turn != null && turn.Rider == unit;
                if (pathAttackers != null &&
                    !(ownTurn && turn.HasFiveFootStep(unit) && pathLength <= turn.GetRemainingFiveFootStepRange(unit)) &&
                    WalkProvokesMore(unit, command.ForcedPath?.vectorPath, command, pathAttackers, pricedAttacks, out failure,
                                     ownTurn ? turn.GetRemainingTime(unit) * unit.CurrentSpeedMps : float.MaxValue))
                {
                    command.ForcedPath = null;
                    return false;
                }
                hasPath = true;
            }
            // ★ v0.3.18 (test10): real time checks the walk against harmful areas too. The game's real-time approach
            // follows its own navmesh path, which bends around obstacles where the straight line the move was priced
            // on did not: Seelah and Camellia walked into Grease that the straight line missed and lay prone for most
            // of the fight (0.3.18-test9 real-time log). The path is asked of the pathfinder on its own (not the
            // unit's seeker, which may be steering the unit right now) and only checked, never attached: the game
            // still walks its own path; a refused option makes the engine take the next best one.
            // ★ v0.3.20 (test8): and against attacks of opportunity, on the same walk (asked once for both checks).
            else if ((avoidAreas || pathAttackers != null) && RealTimeWalkRefused(unit, command, avoidAreas, pathAttackers, pricedAttacks, out failure))
            {
                return false;
            }
            return true;
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

        /// <summary>★ v0.3.20: failure texts the engine reads (TacticalDriver.RecordRejected) - the path ends short of the
        /// target, or in range without seeing it; a walk-then-act path longer than one move action.</summary>
        public const string OutOfReachFailure = "path ends out of reach";
        public const string NoSightAtPathEndFailure = "path ends without line of sight to the target";
        public const string LongerThanOneMoveFailure = "longer than one move action";

        /// <summary>★ v0.3.20 (test8): the failure text of a walk refused for provoking more attacks of opportunity than
        /// the move was priced for (TacticalDriver.RecordRejected reads it).</summary>
        public const string MoreAttacksOfOpportunityFailure = "the walk provokes more attacks of opportunity";

        /// <summary>
        /// ★ v0.3.18 (test10): real time - the walk the game would take, asked of the pathfinder directly, checked against
        /// harmful areas (<paramref name="avoidAreas"/>) and ★ v0.3.20 (test8) attacks of opportunity
        /// (<paramref name="pathAttackers"/>). The path is asked only when a check needs it: a harmful area or an enemy
        /// near the straight line.
        /// </summary>
        private static bool RealTimeWalkRefused(UnitEntityData unit, UnitCommand command, bool avoidAreas,
                                                Func<IList<Vector3>, List<UnitEntityData>> pathAttackers, int pricedAttacks,
                                                out string failure)
        {
            failure = null;
            if (CombatController.IsInTurnBasedCombat() || !unit.IsInCombat || unit.RiderPart != null) return false;
            if (command is UnitMoveAlongPath || !command.ShouldUnitApproach) return false;
            try
            {
                // The circle around the midpoint covers every walk that does not detour far, and a far detour leaves
                // the check unneeded.
                Vector3 start = unit.Position, goal = command.ApproachPoint;
                Vector3 middle = (start + goal) * 0.5f;
                float half = Vector3.Distance(start, goal) * 0.5f;
                bool areaCheck = avoidAreas && Analysis.AoeDangerAnalyzer.HarmfulAreaNear(unit, middle, half + 3f);
                bool attackCheck = pathAttackers != null && EnemyNear(unit, middle, half + 4f);
                if (!areaCheck && !attackCheck) return false;
                var walk = RealTimeWalk(start, goal);
                if (walk == null) return false;   // no answer: the move is not refused for it
                if (areaCheck && PathEntersArea(unit, walk, command, out string area))
                {
                    failure = $"{WalksIntoAreaFailure} {area}";
                    return true;
                }
                return attackCheck && WalkProvokesMore(unit, walk, command, pathAttackers, pricedAttacks, out failure);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Dispatch] {unit.CharacterName}: real-time path check skipped: {ex.Message}");
                return false;
            }
        }

        /// <summary>The walk the pathfinder gives a real-time unit from <paramref name="start"/> to <paramref name="goal"/>
        /// (not the unit's seeker, which may be steering it right now); null without an answer.</summary>
        private static List<Vector3> RealTimeWalk(Vector3 start, Vector3 goal)
        {
            ABPath path = null;
            try
            {
                path = ABPath.Construct(start, goal, null);
                path.nnConstraint.graphMask = 1;   // the walkable navmesh, as the game's own movement queries
                path.Claim(PathClaimKey);
                AstarPath.StartPath(path, pushToFront: true);
                AstarPath.BlockUntilCalculated(path);
                if (path.error || path.CompleteState != PathCompleteState.Complete || path.vectorPath == null || path.vectorPath.Count == 0)
                    return null;
                return GameWalk(path, start);
            }
            finally
            {
                path?.Release(PathClaimKey, false);
            }
        }

        /// <summary>A conscious enemy of <paramref name="unit"/> within <paramref name="radius"/> of <paramref name="point"/>.</summary>
        private static bool EnemyNear(UnitEntityData unit, Vector3 point, float radius)
        {
            foreach (var other in Game.Instance.State.Units)
            {
                if (other == null || other == unit || !other.IsInGame || other.Descriptor?.State == null ||
                    !other.Descriptor.State.IsConscious || !unit.IsEnemy(other)) continue;
                if (Vector3.Distance(other.Position, point) <= radius + other.Corpulence) return true;
            }
            return false;
        }

        /// <summary>
        /// ★ v0.3.20 (test8): the walk along <paramref name="points"/>, up to where the unit arrives, provokes more attacks of
        /// opportunity than <paramref name="pricedAttacks"/> (counted by <paramref name="pathAttackers"/>).
        /// </summary>
        private static bool WalkProvokesMore(UnitEntityData unit, List<Vector3> points, UnitCommand command,
                                             Func<IList<Vector3>, List<UnitEntityData>> pathAttackers, int pricedAttacks,
                                             out string failure, float maxLength = float.MaxValue)
        {
            failure = null;
            try
            {
                if (points == null || points.Count == 0) return false;
                var found = pathAttackers(Truncate(WalkToArrival(unit, points, command), maxLength));
                if (found == null || found.Count <= pricedAttacks) return false;
                failure = $"{MoreAttacksOfOpportunityFailure} ({found.Count}, priced for {pricedAttacks}: " +
                          $"{string.Join(", ", found.ConvertAll(e => e.CharacterName))})";
                return true;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Dispatch] {unit.CharacterName}: attack of opportunity check of the path skipped: {ex.Message}");
                return false;
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
                var walk = WalkToArrival(unit, points, command);
                for (int i = 1; i < walk.Count; i++)
                {
                    if (Analysis.AoeDangerAnalyzer.EnteringDanger(unit, walk[i - 1], walk[i], out string name) > 0f)
                    {
                        area = name ?? "a harmful area";
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Dispatch] {unit.CharacterName}: area check of the path skipped: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// The walk from the unit along <paramref name="points"/>, up to where it arrives: the first spot on a segment within
        /// the command's approach radius of the target (flat), sampled every 0.25 m - an enemy standing in an area at the
        /// path's end is attacked from outside it. ★ v0.3.20 (test8): shared by the area and attack of opportunity checks.
        /// </summary>
        private static List<Vector3> WalkToArrival(UnitEntityData unit, List<Vector3> points, UnitCommand command)
        {
            Vector3 target = command.ApproachPoint;
            float radius = Mathf.Max(0f, command.ApproachRadius);
            Vector3 from = unit.Position;
            var walk = new List<Vector3> { from };
            foreach (var to in points)
            {
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
                walk.Add(end);
                if (arrives) break;
                from = end;
            }
            return walk;
        }

        /// <summary>The first <paramref name="maxLength"/> metres of <paramref name="walk"/>.</summary>
        private static List<Vector3> Truncate(List<Vector3> walk, float maxLength)
        {
            float left = maxLength;
            for (int i = 1; i < walk.Count; i++)
            {
                float length = Vector3.Distance(walk[i - 1], walk[i]);
                if (length < left) { left -= length; continue; }
                var cut = walk.GetRange(0, i);
                cut.Add(Vector3.Lerp(walk[i - 1], walk[i], length > 0f ? left / length : 0f));
                return cut;
            }
            return walk;
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
