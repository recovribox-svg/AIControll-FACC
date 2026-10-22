// ★ v0.3.19: other mods this one has to share work with, found through Unity Mod Manager's own mod list (no reference to
// their code). Buff It 2 The Limit (Id "BuffIt2TheLimit", its Info.json) switches activatable abilities by setting IsOn
// itself - at combat start, after a configured round limit and when combat ends (BuffExecutor, RoundLimitWatcher) - and
// casts buff routines from spell slots and items. It does not say which abilities a player gave it, so nothing here is
// decided per ability: the settings "AI manages activatable abilities" and "After fights, AI companions recast their
// long-lasting self buffs" are the owner choice, and this only tells the player (settings page, log) that the choice
// matters. The player's settings are never changed for them.
using System;
using UnityModManagerNet;

namespace CompanionAI_Pathfinder.GameInterface
{
    public static class ModInterop
    {
        public const string BuffItToTheLimitId = "BuffIt2TheLimit";

        private static bool? _buffIt;
        private static float _checkedAt = -100f;

        /// <summary>Buff It 2 The Limit is installed and switched on in the mod manager (looked up at most every 5 s).</summary>
        public static bool BuffItToTheLimitActive
        {
            get
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (_buffIt.HasValue && now - _checkedAt < 5f) return _buffIt.Value;
                _checkedAt = now;
                bool active;
                try
                {
                    var entry = UnityModManager.FindMod(BuffItToTheLimitId);
                    active = entry != null && entry.Enabled;
                }
                catch
                {
                    active = false;   // unknown: behave as without it (the settings decide anyway)
                }
                if (_buffIt != active) ReportState(active);
                _buffIt = active;
                return active;
            }
        }

        private static void ReportState(bool active)
        {
            if (!active)
            {
                if (_buffIt == true) Main.Log("[Interop] Buff It 2 The Limit is no longer active");
                return;
            }
            var s = Settings.ModSettings.Instance;
            Main.Log("[Interop] Buff It 2 The Limit is active" +
                     (s == null ? "" :
                      $" - AI manages activatable abilities: {(s.AiManagesToggles ? "on" : "off")}, recasts self buffs after fights: " +
                      $"{(s.AiRecastsSelfBuffs ? "on" : "off")}" +
                      (s.AiManagesToggles || s.AiRecastsSelfBuffs
                          ? " (switch these off if Buff It 2 The Limit manages your companions' toggles or buffs)" : "")));
        }
    }
}
