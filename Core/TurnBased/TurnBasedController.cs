using System;
using Kingmaker.EntitySystem.Entities;
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

        /// <summary>Frames to wait for a running command before giving up on the turn (~5 s).</summary>
        private const int COMMAND_WAIT_TIMEOUT = 300;

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
                    if (++memory.WaitFrames > COMMAND_WAIT_TIMEOUT)
                    {
                        Main.Log($"[TBController] {unitName}: command wait timeout - ending turn");
                        memory.Finished = true;
                        return ExecutionResult.EndTurn("Wait timeout");
                    }
                    return ExecutionResult.Waiting("Command in progress");
                }
                memory.WaitFrames = 0;

                // Learn from the previous command: if it ended without acting (interrupted, no path, target gone),
                // do not pick the same option again this turn.
                if (memory.LastCommand != null)
                {
                    if (!memory.LastCommand.IsActed && memory.LastCandidateKey != null)
                    {
                        memory.Failed.Add(memory.LastCandidateKey);
                        Main.Log($"[TBController] {unitName}: '{memory.LastCandidateKey}' ended without acting ({memory.LastCommand.Result}) - not retrying this turn");
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

        public void ResetAll()
        {
            CombatMemory.ResetCombat();
            Main.Log("[TBController] All states reset");
        }
    }
}
