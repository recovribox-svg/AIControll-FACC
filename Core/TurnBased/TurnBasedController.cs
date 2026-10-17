using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using UnityEngine;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Tactics;

namespace CompanionAI_Pathfinder.Core.TurnBased
{
    /// <summary>
    /// ★ v0.3.0: Turn-based controller driving Tactics.TacticalEngine one action at a time.
    ///
    /// Each time the unit's command slots are empty, the engine re-reads the battlefield and the unit's remaining
    /// actions and picks the single best next action (or ends the turn). There is no whole-turn plan to invalidate:
    /// the v0.2.x planner replanned whenever the unit itself moved and then found "no targets" (see docs/INVESTIGATION.md).
    /// </summary>
    public class TurnBasedController
    {
        private static TurnBasedController _instance;
        public static TurnBasedController Instance => _instance ?? (_instance = new TurnBasedController());

        /// <summary>
        /// ★ v0.3.1: seconds our command may show no progress (not moving, not running) before the turn is given up.
        /// v0.3.0 used 300 frames, which at ~70 FPS is 4.3 s and cut off Camellia's 7.6 m walk that took 4.3 s.
        /// </summary>
        private const float COMMAND_IDLE_TIMEOUT = 4f;

        /// <summary>Absolute cap on waiting for one command, progress or not.</summary>
        private const float COMMAND_MAX_WAIT = 15f;

        /// <summary>Seconds a command we did not issue may sit idle at our turn before it is interrupted.</summary>
        private const float FOREIGN_IDLE_TIMEOUT = 1f;

        /// <summary>
        /// Longest time one tick may count. TurnController.Tick (our postfix) only runs while the game is unpaused, in
        /// Default mode and not waiting for UI (CombatController.Tick), so a pause, dialog or alt-tab must not count.
        /// </summary>
        private const float MAX_TICK_SECONDS = 0.1f;

        /// <summary>Safety net against decision loops; every real decision spends an action, so this is never reached normally.</summary>
        private const int MAX_DECISIONS_PER_TURN = 16;

        /// <summary>Candidates tried per tick when the dispatcher rejects one (e.g. no path).</summary>
        private const int MAX_ATTEMPTS_PER_TICK = 3;

        public ExecutionResult ProcessTurn(UnitEntityData unit)
        {
            if (unit == null) return ExecutionResult.Failure("Unit is null");
            string unitName = unit.CharacterName;

            try
            {
                CombatMemory.Sync();
                var memory = CombatMemory.ForTurn(unit);
                if (memory.Finished) return ExecutionResult.EndTurn("Turn finished");

                // Wait for the previous command to finish (the game ticks it while the turn is Acting).
                if (!GameAPI.IsReadyForNextAction(unit))
                {
                    var wait = Wait(unit, memory);
                    if (wait != null) return wait;
                }
                memory.Waiting = false;

                // Learn from the previous command: if it ended without acting (interrupted, no path, target gone),
                // do not pick the same option again this turn.
                if (memory.LastCommand != null)
                {
                    var last = memory.LastCommand;
                    bool isMove = memory.LastKind == CandidateKind.Move;
                    float moved = Vector3.Distance(unit.Position, memory.LastIssuePosition);
                    string notActed = null;
                    if (!last.IsActed)
                    {
                        // ★ v0.3.2: a move that was cut short (its move action ran out) still made progress; the next
                        // decision may continue with the standard action. v0.3.1 ended Albedo's turn after 8.9 m.
                        if (!(isMove && moved >= 1f))
                            notActed = $"ended without acting ({last.Result})";
                    }
                    else if (!last.IsStarted && !isMove)
                        // ★ v0.3.1: UnitCommandController.TickCommandTurnBased force-finishes an approaching command whose
                        // unit stopped moving (or that has no action left) as "Success" - IsActed, but never started.
                        // Albedo repeated 'Move+Attack->Dretch' (0.7 m, no attack) four times. Moves are finished that
                        // way when they arrive (5-foot step), so this only applies to attacks and abilities.
                        notActed = $"was force-finished before it started ({last.Result})";
                    else if (!last.IsIgnoreCooldown && !ActionWasSpent(unit, memory.LastSlot))
                        // Commands with IsIgnoreCooldown legitimately spend no action (sticky touch, spell combat...).
                        notActed = $"ended ({last.Result}) but its {memory.LastSlot} action is still unused";

                    if (notActed != null && memory.LastCandidateKey != null)
                    {
                        memory.Failed.Add(memory.LastCandidateKey);
                        Main.Log($"[TBController] {unitName}: '{memory.LastCandidateKey}' {notActed} - not retrying this turn");
                    }
                    memory.LastCommand = null;
                    memory.LastCandidateKey = null;
                }

                if (memory.Decisions >= MAX_DECISIONS_PER_TURN)
                {
                    Main.Log($"[TBController] {unitName}: decision safety limit reached - ending turn");
                    memory.Finished = true;
                    return ExecutionResult.EndTurn("Decision limit");
                }

                for (int attempt = 0; attempt < MAX_ATTEMPTS_PER_TICK; attempt++)
                {
                    var candidate = TacticalEngine.Decide(unit, memory);
                    if (candidate == null)
                    {
                        memory.Finished = true;
                        return ExecutionResult.EndTurn("Nothing worth doing");
                    }

                    memory.Decisions++;
                    if (TacticalEngine.Execute(unit, candidate, out var command, out string failure))
                    {
                        memory.LastCommand = command;
                        memory.LastCandidateKey = candidate.Key;
                        memory.LastSlot = candidate.Slot;
                        memory.LastKind = candidate.Kind;
                        memory.LastIssuePosition = unit.Position;
                        return ExecutionResult.Waiting($"Issued {candidate.Label}");
                    }

                    Main.Log($"[TBController] {unitName}: '{candidate.Label}' could not be issued ({failure}) - trying next best");
                    memory.Failed.Add(candidate.Key);
                }

                return ExecutionResult.Waiting("Retrying next tick");
            }
            catch (Exception ex)
            {
                Main.Error($"[TBController] {unitName}: {ex}");
                return ExecutionResult.EndTurn($"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Waiting for a command. Returns null when the caller may decide now (a foreign leftover command was cleared),
        /// otherwise Waiting or EndTurn.
        /// </summary>
        private static ExecutionResult Wait(UnitEntityData unit, TurnMemory memory)
        {
            float now = Time.unscaledTime;
            if (!memory.Waiting)
            {
                memory.Waiting = true;
                memory.WaitSeconds = 0f;
                memory.IdleSeconds = 0f;
                memory.LastPosition = unit.Position;
                memory.LastTick = now;
            }
            float dt = Mathf.Clamp(now - memory.LastTick, 0f, MAX_TICK_SECONDS);
            memory.LastTick = now;
            memory.WaitSeconds += dt;

            // Progress: the unit moves or its command is executing (animation). A command that is still approaching
            // is not "running" (UnitCommand.IsStarted only becomes true on arrival), so movement is tracked separately.
            bool running = GameAPI.HasRunningCommand(unit);
            if (running || Vector3.Distance(unit.Position, memory.LastPosition) > 0.05f)
            {
                memory.IdleSeconds = 0f;
                memory.LastPosition = unit.Position;
            }
            else
            {
                memory.IdleSeconds += dt;
            }

            // ★ v0.3.1: a command we did not issue this turn (left over from real time, an out-of-combat buff routine
            // or the previous combat) blocked Camellia's turn until the timeout. Clear it and decide.
            // The game's own commands (toggling an activatable ability, attacks of opportunity) are left alone.
            if (memory.LastCommand == null && !running && memory.IdleSeconds > FOREIGN_IDLE_TIMEOUT &&
                unit.Commands.Contains(IsClearableLeftover))
            {
                Main.Log($"[TBController] {unit.CharacterName}: idle leftover command at turn start - interrupting it");
                unit.Commands.InterruptAll(IsClearableLeftover);
                memory.Waiting = false;
                return GameAPI.IsReadyForNextAction(unit) ? null : ExecutionResult.Waiting("Clearing leftover command");
            }

            if (memory.IdleSeconds > COMMAND_IDLE_TIMEOUT || memory.WaitSeconds > COMMAND_MAX_WAIT)
            {
                Main.Log($"[TBController] {unit.CharacterName}: command wait timeout ({memory.IdleSeconds:F1}s without progress, " +
                         $"{memory.WaitSeconds:F1}s total) - ending turn");
                memory.Finished = true;
                return ExecutionResult.EndTurn("Wait timeout");
            }
            return ExecutionResult.Waiting("Command in progress");
        }

        private static bool IsClearableLeftover(UnitCommand command)
        {
            // Same protections as the game's own UnitCommands.InterruptAll(): interruptible, not cutscene/scripted-TB.
            return command != null && !command.IsStarted && !command.IsFinished &&
                   command.IsInterruptible && !command.Cutscene && !command.DontInterruptInTurnBasedMode &&
                   !(command is UnitActivateAbility) && !(command is UnitAttackOfOpportunity);
        }

        /// <summary>Did the command that just ended use the action it was chosen for?</summary>
        private static bool ActionWasSpent(UnitEntityData unit, ActionSlot slot)
        {
            try
            {
                switch (slot)
                {
                    case ActionSlot.Standard:
                    case ActionSlot.FullRound:
                        return !unit.HasStandardAction();
                    case ActionSlot.Swift:
                        return !unit.HasSwiftAction();
                    default:
                        return true; // free actions, and moves (a move may legitimately leave movement unused)
                }
            }
            catch
            {
                return true;
            }
        }

        public void ResetAll()
        {
            CombatMemory.ResetCombat();
            Main.Log("[TBController] All states reset");
        }
    }
}
