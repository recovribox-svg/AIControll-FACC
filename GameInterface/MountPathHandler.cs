// ★ v0.3.0: Give mount commands a ForcedPath when an AI-controlled rider gives an order in turn-based combat.
//
// Evidence (Assembly-CSharp): when a mounted unit runs a command, SaddledUnitController.TickDelegateRiderToMount builds a
// separate command for the MOUNT (new UnitMoveTo(...)/new UnitAttack(...).AddRiderCommand(riderCommand)) and runs it
// with owner.Commands.Run(mountCmd) - without copying any ForcedPath (TacticalCombatRider does copy it, the normal
// controller does not). In turn-based combat that mount command then follows PathVisualizer's mouse-cursor prediction
// (UnitCommand.TickApproaching), which is why a ridden pet sometimes walked to wherever the pointer hovered.
//
// IUnitRunCommandHandler is raised synchronously inside UnitCommands.Run, after Init/OnRun and before the command's
// first tick, so attaching the path here is in time. Player-issued orders (CreatedByPlayer) are left alone: for them
// the cursor prediction IS the intended path.
using System;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Commands.Base;
using TurnBased.Controllers;

namespace CompanionAI_Pathfinder.GameInterface
{
    public class MountPathHandler : IUnitRunCommandHandler
    {
        private static MountPathHandler _instance;

        public static void Subscribe()
        {
            if (_instance != null) return;
            _instance = new MountPathHandler();
            EventBus.Subscribe(_instance);
        }

        public static void Unsubscribe()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
        }

        public void HandleUnitRunCommand(UnitCommand cmd)
        {
            try
            {
                if (!Main.Enabled || cmd == null) return;
                var riderCommand = cmd.RiderCommand;
                if (riderCommand == null || cmd.ForcedPath != null || cmd.CreatedByPlayer || riderCommand.CreatedByPlayer) return;
                if (!CombatController.IsInTurnBasedCombat()) return;

                var rider = riderCommand.Executor;
                var mount = cmd.Executor;
                if (rider == null || mount == null || !CustomBrainPatch.ShouldControlUnit(rider)) return;

                if (CommandDispatcher.AttachPathIfNeeded(mount, cmd, out float length, out string failure))
                    Main.Log($"[Cmd] {mount.CharacterName}: mount command {cmd.GetType().Name} for rider {rider.CharacterName} given forcedPath={length:F1}m");
                else if (failure != null)
                {
                    // Without a path the command would follow the mouse-cursor prediction; stopping is the safer outcome.
                    Main.Log($"[Cmd] {mount.CharacterName}: mount command {cmd.GetType().Name} has no path ({failure}) - interrupted");
                    cmd.Interrupt();
                }
            }
            catch (Exception ex)
            {
                Main.Error($"[MountPath] {ex.Message}");
            }
        }
    }
}
