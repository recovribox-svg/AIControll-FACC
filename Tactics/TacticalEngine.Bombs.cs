// ★ v0.3.23 (bombs task, docs/TASK_BOMBS_CARNAGE_QUIVER.md §1): Fast Bombs, on by default once learned (native
// Kingmaker.UnitLogic.FactLogic.FastBombs), turns one bomb order into a full-attack routine of throws: when the bomb's
// command acts the game builds that routine with an emulated bomb weapon put in the hand (with equipment events) and
// queues the same bomb at the same target again, cooldown ignored, while it stays available - one bomb from the daily
// resource each. There is no side-effect-free preview of that routine, so the engine still values an order as one throw;
// the log says so once, as evidence for the next play test.
using System;
using System.Linq;
using Kingmaker.EntitySystem.Entities;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static void NoteFastBombs(UnitEntityData unit, Candidate chosen)
        {
            try
            {
                if (chosen?.Kind != CandidateKind.Ability || chosen.Ability == null) return;
                if (!(AbilityProfile.Get(chosen.Ability)?.ThrowsDailyBomb ?? false)) return;
                var fast = unit.Descriptor?.State?.FastBombsFeature;
                if (fast?.Abilities == null || !fast.Abilities.Any(r => r?.Get() == chosen.Ability.Blueprint)) return;
                if (!CombatMemory.FirstThisFight($"fastbombs|{unit.UniqueId}|{chosen.Ability.Blueprint.AssetGuid}")) return;
                Main.Log($"[Bombs] {unit.CharacterName}: Fast Bombs is on - {chosen.Ability.Name} is thrown as a full attack (one bomb a " +
                           $"throw, {chosen.Ability.GetAvailableForCastCount()} left); the AI values the order as one throw");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Bombs] {unit?.CharacterName}: {ex.Message}");
            }
        }
    }
}
