// ★ v0.3.22: observe game/other-mod pacing without changing clocks, settings or animation state.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;

namespace CompanionAI_Pathfinder.GameInterface
{
    internal static class CombatPacingDiagnostics
    {
        private static readonly HashSet<string> Seen = new HashSet<string>();
        internal static void NewFight() { Seen.Clear(); Report("combat start"); }
        internal static void Report(string where)
        {
            try
            {
                var game = Game.Instance;
                var ui = game?.Player?.UISettings;
                var time = game?.TimeController;
                if (ui == null || time == null) return;
                float playerDefault = Convert.ToSingle(AccessTools.Property(ui.GetType(), "DefaultTimeScaleInPlayerTurn").GetValue(ui));
                float otherDefault = Convert.ToSingle(AccessTools.Property(ui.GetType(), "DefaultTimeScaleInNonPlayerTurn").GetValue(ui));
                var values = $"TB defaults player x{playerDefault:0.##}/other x{otherDefault:0.##}, " +
                    $"effective x{ui.TimeScaleInPlayerTurn:0.##}/x{ui.TimeScaleInNonPlayerTurn:0.##}, speed-up={ui.IsTBMSpeedUp}; " +
                    $"player scale x{time.PlayerTimeScale:0.##}, debug x{time.DebugTimeScale:0.##}, Unity x{UnityEngine.Time.timeScale:0.##}";
                if (!Seen.Add(values)) return;
                var owners = Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(Kingmaker.UI.PlayerUISettings), "TimeScaleInPlayerTurn"))?.Owners;
                Main.Log($"[Pacing] {where}: {values}; AI Control leaves these values unchanged" +
                    (owners?.Count > 0 ? "; player-speed getter patches: " + string.Join(", ", owners) : ""));
            }
            catch (Exception ex) { Main.Verbose($"[Pacing] observation unavailable: {ex.Message}"); }
        }

        internal static long NativeSummonStart(UnitEntityData unit)
        {
            if (!Main.Enabled || unit?.IsPlayerFaction != true || !unit.IsInCombat ||
                unit.Get<Kingmaker.UnitLogic.Parts.UnitPartSummonedMonster>() == null) return 0;
            return System.Diagnostics.Stopwatch.GetTimestamp();
        }
        internal static void NativeSummonEnd(UnitEntityData unit, long started)
        {
            if (started == 0 || !Main.Enabled || unit == null) return;
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            if (ms < 50d) return;
            Tactics.AbilityDiscovery.ReportOnce($"unmanaged-summon|{unit.UniqueId}|{Tactics.CombatMemory.Round}",
                () => $"[Perf] unmanaged summon brain: {unit.CharacterName} {ms:F1} ms; native/other-mod pipeline (outside TacticalEngine.Decide)");
        }
    }
}
