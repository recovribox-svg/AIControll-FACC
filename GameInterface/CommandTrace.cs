// ★ v0.2.119: End-to-end trace of commands issued by the mod.
// One line when a command is issued and one when the game ends it (result + distance actually moved), so a single
// in-game test shows exactly where a movement request stops. Uses the game's EventBus (no Harmony).
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Commands.Base;
using TurnBased.Controllers;
using UnityEngine;

namespace CompanionAI_Pathfinder.GameInterface
{
    public class CommandTrace : IUnitCommandEndHandler
    {
        private struct Entry
        {
            public string Label;
            public Vector3 StartPosition;
            public float IssuedAt;
        }

        private const int MaxTracked = 64;

        private static CommandTrace _instance;
        private static readonly Dictionary<UnitCommand, Entry> Tracked = new Dictionary<UnitCommand, Entry>();

        public static void Subscribe()
        {
            if (_instance != null) return;
            _instance = new CommandTrace();
            EventBus.Subscribe(_instance);
        }

        public static void Unsubscribe()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
            Tracked.Clear();
        }

        public static void Track(UnitEntityData unit, UnitCommand command, string label, float? pathLength)
        {
            if (Tracked.Count >= MaxTracked)
                Tracked.Clear(); // commands that never reported an end (e.g. area unload) must not accumulate

            Tracked[command] = new Entry { Label = label, StartPosition = unit.Position, IssuedAt = Time.time };
            Main.Log($"[Cmd] {unit.CharacterName}: ISSUED {Describe(command)} '{label}' mode={Mode()} " +
                     (pathLength.HasValue ? $"forcedPath={pathLength.Value:F1}m" : "forcedPath=none") +
                     $" approachR={command.ApproachRadius:F1}");
        }

        public static void LogRejected(UnitEntityData unit, UnitCommand command, string label, string reason)
        {
            Main.Log($"[Cmd] {unit?.CharacterName}: REJECTED {Describe(command)} '{label}' mode={Mode()} - {reason}");
        }

        public void HandleUnitCommandDidEnd(UnitCommand command)
        {
            if (command == null || !Tracked.TryGetValue(command, out var entry)) return;
            Tracked.Remove(command);

            try
            {
                var unit = command.Executor;
                float moved = unit != null ? Vector3.Distance(entry.StartPosition, unit.Position) : 0f;
                Main.Log($"[Cmd] {unit?.CharacterName}: ENDED {Describe(command)} '{entry.Label}' result={command.Result} " +
                         $"acted={command.IsActed} moved={moved:F1}m after {Time.time - entry.IssuedAt:F1}s");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Cmd] trace error: {ex.Message}");
            }
        }

        private static string Describe(UnitCommand command)
        {
            return command?.GetType().Name ?? "null";
        }

        private static string Mode()
        {
            if (!CombatController.IsInTurnBasedCombat()) return "RT";
            return $"TB/{Game.Instance?.TurnBasedCombatController?.CurrentTurn?.Status}";
        }
    }
}
