using CompanionAI_Pathfinder.Core;
using Kingmaker.EntitySystem.Entities;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static readonly DiagnosticChangeLog<bool> EnemyAvailabilityLog = new DiagnosticChangeLog<bool>();

        private static void NoteEnemyAvailability(UnitEntityData unit, bool available)
        {
            if (Main.VerboseOn && EnemyAvailabilityLog.Observe(CombatMemory.Fight, unit.UniqueId, available) && !available)
                Main.Verbose($"[Tactics] {unit.CharacterName}: no reachable enemies - wait");
        }
    }
}
