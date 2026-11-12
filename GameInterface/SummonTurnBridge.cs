// ★ summon control (docs/SUMMON_CONTROL_PLAN.md §4, docs/TACTICAL_AI.md §152): the turn-based entry for a summoned
// creature AI Control drives. An entry adapter only - every decision is taken by the existing turn-based driver
// (Core/TurnBased/TurnBasedController.ProcessTurn) and every command goes through CommandDispatcher.Run.
//
// The game's turn for a unit that is not directly controllable (TurnController, decompiled 2.7.0x):
//  - Prepare(): resets the round's actions, ticks new-round effects, calls AiBrainController.ForceTick() - the brain's
//    first command is issued here - and only then sets Status = Acting.
//  - Tick(), Acting with no command: ForceTick() again; a command that appears resets the idle timer and counts towards
//    the game's limit of 50 brain commands a turn. Then, with nothing running and no projectile in flight, the turn
//    ends when ContinueActing() says so: no time left (standard and move spent), or idle for 0.2 s / the animation
//    speed (TimeWaitedForIdleAI), or a walk that makes no progress for 15 frames. A command running for more than 9 s
//    is interrupted and the turn ended (UnitCommand.Tick -> OnInterruptCommand -> ForceToEnd).
//  - TurnBasedCombatController ticks before UnitCommandController (GameModesFactory), so a command issued in Tick is
//    first ticked in the same frame.
// So the game keeps running and ending the turn; the bridge only replaces which command the brain call produces:
//  1. the brain call inside Prepare is held back and nothing is decided in the half-prepared turn;
//  2. the Prepare postfix takes the first decision, in the same frame, before the game's first idle check;
//  3. each later brain call (Acting, no command) takes the next decision, once a frame;
//  4. the Tick postfix serves waits and timeouts while a command is pending, and - when the driver has ended the turn -
//     the shared end-of-turn handling (projectiles landed, idle pause, 4 s cap, then TurnController.ForceToEnd).
// Never written: TurnController.Status, controllability, faction or group. With the feature, the mode or the summoner's
// AI switched off the unit is simply not claimed and the game's brain runs as before.
using System;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using CompanionAI_Pathfinder.Core;
using CompanionAI_Pathfinder.Core.TurnBased;
using CompanionAI_Pathfinder.Tactics;
using TurnBased.Controllers;
using UnityEngine;

namespace CompanionAI_Pathfinder.GameInterface
{
    public static class SummonTurnBridge
    {
        /// <summary>The turn whose Prepare() is running (the brain call inside it is not answered).</summary>
        private static TurnController s_Preparing;
        /// <summary>The turn the bridge serves, the frame of its last service, and whether its driver ended it.</summary>
        private static TurnController s_Turn;
        private static int s_ServedFrame = -1;
        private static bool s_Finished;
        private static string s_EndReason;
        /// <summary>Diagnostics of the served turn.</summary>
        private static int s_Services, s_BrainCalls;

        /// <summary>Prefix of TurnController.Prepare: marks the turn so the brain call inside it decides nothing.</summary>
        public static void Prepare_Prefix(TurnController __instance)
        {
            s_Preparing = __instance;
        }

        /// <summary>
        /// Postfix of TurnController.Prepare: the first decision of a claimed summon's turn, after the game has prepared
        /// it (Status is Acting for a unit that is not directly controllable).
        /// </summary>
        public static void Prepare_Postfix(TurnController __instance)
        {
            s_Preparing = null;
            try
            {
                if (!Main.Enabled || !SummonControl.TurnBridgeBound) return;
                var rider = __instance?.Rider;
                // Every other unit leaves here after one part lookup.
                if (rider == null || rider.IsDirectlyControllable || !SummonControl.IsOrdinarySummon(rider)) return;
                if (__instance.Status != TurnController.TurnStatus.Acting) return;
                if (!CustomBrainPatch.ShouldControlUnit(rider)) return;
                Serve(__instance, rider, "prepare");
            }
            catch (Exception ex)
            {
                Main.Error($"[SummonTurn] Prepare: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// The game asks the brain of a not directly controllable summon to act in turn-based combat (the caller has
        /// checked that AI Control drives it). Returns false when the bridge has the turn - the game's brain is skipped.
        /// </summary>
        internal static bool OnBrainTick(UnitEntityData unit)
        {
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            bool current = turn != null && turn.Rider == unit;
            bool served = current && ReferenceEquals(turn, s_Turn);
            var action = SummonRules.OnBrainTick(
                eligible: SummonControl.TurnBridgeBound,
                isCurrentRider: current,
                insidePrepare: current && ReferenceEquals(turn, s_Preparing),
                acting: current && turn.Status == TurnController.TurnStatus.Acting,
                servedThisFrame: served && s_ServedFrame == Time.frameCount,
                turnFinished: served && s_Finished);
            if (action == SummonRules.BrainTick.Native) return true;
            if (action == SummonRules.BrainTick.Decide)
            {
                s_BrainCalls++;
                Serve(turn, unit, "brain");
            }
            return false;
        }

        /// <summary>
        /// Postfix of TurnController.Tick for a summon AI Control drives that the game runs as an AI unit: the waits
        /// and timeouts of a pending command, and the end of the turn once the driver has ended it.
        /// </summary>
        internal static void OnTurnTick(TurnController turn, UnitEntityData rider)
        {
            if (!SummonControl.TurnBridgeBound || turn.Status != TurnController.TurnStatus.Acting) return;
            // Adopted in the middle of its turn (the feature or its summoner's AI switched on): claimed by the next
            // brain call, when its current command has ended. Nothing of the game's is interrupted here.
            if (!ReferenceEquals(turn, s_Turn)) return;
            if (!s_Finished && s_ServedFrame != Time.frameCount) Serve(turn, rider, "tick");
            if (s_Finished) TurnBasedPatches.CompleteTurn(turn, rider, s_EndReason, nativeRider: true);
        }

        /// <summary>One call of the turn-based driver for the served turn, at most once a frame.</summary>
        private static void Serve(TurnController turn, UnitEntityData unit, string from)
        {
            if (!ReferenceEquals(turn, s_Turn))
            {
                s_Turn = turn;
                s_Finished = false;
                s_EndReason = null;
                s_Services = s_BrainCalls = 0;
                if (Main.VerboseOn)
                    Main.Verbose($"[SummonTurn] {unit.CharacterName}: turn taken over ({from}, frame {Time.frameCount})");
            }
            if (s_Finished) return;
            s_ServedFrame = Time.frameCount;
            s_Services++;
            ExecutionResult result;
            try
            {
                result = TurnBasedController.Instance.ProcessTurn(unit);
            }
            catch (Exception ex)
            {
                // One owner of the command stream: a claimed turn is not handed back to the game's brain after a
                // failure - it is ended through the shared, bounded completion.
                Main.Error($"[SummonTurn] {unit.CharacterName}: {ex.Message}\n{ex.StackTrace}");
                result = ExecutionResult.EndTurn("Exception: " + ex.Message);
            }
            if (result != null && result.Type == ResultType.EndTurn)
            {
                s_Finished = true;
                s_EndReason = result.Reason;
                if (Main.VerboseOn)
                    Main.Verbose($"[SummonTurn] {unit.CharacterName}: driver ended the turn ({result.Reason}) after " +
                                 $"{s_Services} services, {s_BrainCalls} brain calls");
            }
        }

        /// <summary>A new fight: no turn is served.</summary>
        internal static void Reset()
        {
            s_Preparing = null;
            s_Turn = null;
            s_ServedFrame = -1;
            s_Finished = false;
            s_EndReason = null;
        }
    }
}
