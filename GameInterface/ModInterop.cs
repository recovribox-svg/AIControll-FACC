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

        // ★ v0.3.21: Wrath Tactics (Id "WrathTactics", Gh05d; checked against 1.34.0) - player-written rules that issue
        // commands on their own 3 s tick. Reached only by reflection (no reference to it); any member missing or throwing
        // switches the handoff off for the session, logged once, and AI Control behaves as without it.
        public const string WrathTacticsId = "WrathTactics";

        private static bool? _wrath;
        private static float _wrathCheckedAt = -100f;
        private static bool _wrathBroken;
        private static bool _wrathBound;
        private static Action<string> _wrathRequestTick;
        private static System.Reflection.FieldInfo _wrathTickCounter;
        private static System.Reflection.PropertyInfo _wrathConfig;
        private static System.Reflection.MethodInfo _wrathIsEnabled, _wrathRulesFor;
        private static System.Reflection.PropertyInfo _wrathGlobalRules, _wrathRuleEnabled;
        private static Func<Kingmaker.EntitySystem.Entities.UnitEntityData, Kingmaker.UnitLogic.Commands.Base.UnitCommand, bool> _wrathIsOurs;
        private static readonly System.Collections.Generic.Dictionary<string, (float At, bool Manages)> WrathManaged =
            new System.Collections.Generic.Dictionary<string, (float, bool)>();

        /// <summary>★ v0.3.21: Wrath Tactics is installed, switched on, and its members were found (looked up every 5 s).</summary>
        public static bool WrathTacticsActive
        {
            get
            {
                if (_wrathBroken) return false;
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (_wrath.HasValue && now - _wrathCheckedAt < 5f) return _wrath.Value;
                _wrathCheckedAt = now;
                bool active = false;
                try
                {
                    var entry = UnityModManager.FindMod(WrathTacticsId);
                    active = entry != null && entry.Enabled && entry.Active && BindWrath(entry);
                }
                catch (Exception ex)
                {
                    WrathFailed($"lookup failed: {ex.Message}");
                    active = false;
                }
                if (active && _wrath != true)
                    Main.Log("[Interop] Wrath Tactics is active - real time: its rules go first, AI Control fills the gaps " +
                             ((Settings.ModSettings.Instance?.WrathTacticsFirst ?? true) ? "(on)" : "(off in the settings)"));
                else if (!active && _wrath == true)
                    Main.Log("[Interop] Wrath Tactics is no longer active");
                _wrath = active;
                return active;
            }
        }

        private static bool BindWrath(UnityModManager.ModEntry entry)
        {
            if (_wrathBound) return true;
            var asm = entry.Assembly;
            if (asm == null) return false;   // not loaded yet: looked up again later
            const System.Reflection.BindingFlags Static = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                                         System.Reflection.BindingFlags.Static;
            var evaluator = asm.GetType("WrathTactics.Engine.TacticsEvaluator");
            var guard = asm.GetType("WrathTactics.Engine.PlayerCommandGuard");
            var configManager = asm.GetType("WrathTactics.Persistence.ConfigManager");
            var config = asm.GetType("WrathTactics.Models.TacticsConfig");
            var rule = asm.GetType("WrathTactics.Models.TacticsRule");
            var request = evaluator?.GetMethod("RequestTick", Static, null, new[] { typeof(string) }, null);
            var isOurs = guard?.GetMethod("IsOurs", Static);
            _wrathTickCounter = evaluator?.GetField("tickCounter", Static);
            _wrathConfig = configManager?.GetProperty("Current", Static);
            _wrathIsEnabled = config?.GetMethod("IsEnabled", new[] { typeof(string) });
            _wrathRulesFor = config?.GetMethod("GetRulesForCharacter", new[] { typeof(string) });
            _wrathGlobalRules = config?.GetProperty("GlobalRules");
            _wrathRuleEnabled = rule?.GetProperty("Enabled");
            string missing = request == null ? "TacticsEvaluator.RequestTick" : isOurs == null ? "PlayerCommandGuard.IsOurs" :
                             _wrathTickCounter == null || _wrathTickCounter.FieldType != typeof(int) ? "TacticsEvaluator.tickCounter" :
                             _wrathConfig == null ? "ConfigManager.Current" : _wrathIsEnabled == null ? "TacticsConfig.IsEnabled" :
                             _wrathRulesFor == null ? "TacticsConfig.GetRulesForCharacter" : _wrathGlobalRules == null ? "TacticsConfig.GlobalRules" :
                             _wrathRuleEnabled == null ? "TacticsRule.Enabled" : null;
            if (missing != null)
            {
                WrathFailed($"{entry.Info?.Version}: {missing} not found");
                return false;
            }
            _wrathRequestTick = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), request);
            _wrathIsOurs = (Func<Kingmaker.EntitySystem.Entities.UnitEntityData, Kingmaker.UnitLogic.Commands.Base.UnitCommand, bool>)
                Delegate.CreateDelegate(typeof(Func<Kingmaker.EntitySystem.Entities.UnitEntityData, Kingmaker.UnitLogic.Commands.Base.UnitCommand, bool>), isOurs);
            _wrathBound = true;
            return true;
        }

        private static void WrathFailed(string why)
        {
            if (_wrathBroken) return;
            _wrathBroken = true;
            _wrath = false;
            Main.Log($"[Interop] Wrath Tactics {why} - its rules are not given the first turn (AI Control works as without it)");
        }

        /// <summary>★ v0.3.21: Wrath Tactics is enabled for <paramref name="unit"/> and has at least one enabled rule for it
        /// (global or its own). Cached 2 s per unit - rules change only in its window.</summary>
        public static bool WrathManages(Kingmaker.EntitySystem.Entities.UnitEntityData unit)
        {
            if (!_wrathBound || _wrathBroken || unit == null) return false;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (WrathManaged.TryGetValue(unit.UniqueId, out var cached) && now - cached.At < 2f) return cached.Manages;
            bool manages = false;
            try
            {
                var config = _wrathConfig.GetValue(null);
                if (config != null && (bool)_wrathIsEnabled.Invoke(config, new object[] { unit.UniqueId }))
                    manages = AnyEnabled(_wrathGlobalRules.GetValue(config)) ||
                              AnyEnabled(_wrathRulesFor.Invoke(config, new object[] { unit.UniqueId }));
            }
            catch (Exception ex)
            {
                WrathFailed($"rules could not be read: {ex.Message}");
                return false;
            }
            WrathManaged[unit.UniqueId] = (now, manages);
            return manages;
        }

        private static bool AnyEnabled(object rules)
        {
            if (!(rules is System.Collections.IEnumerable list)) return false;
            foreach (var r in list)
                if (r != null && (bool)_wrathRuleEnabled.GetValue(r)) return true;
            return false;
        }

        /// <summary>★ v0.3.21: Wrath Tactics' evaluation counter (it moves each time its rules were looked at).</summary>
        public static int WrathTick()
        {
            try { return (int)_wrathTickCounter.GetValue(null); }
            catch (Exception ex) { WrathFailed($"tick counter: {ex.Message}"); return 0; }
        }

        /// <summary>★ v0.3.21: ask Wrath Tactics to evaluate its rules on its next update.</summary>
        public static void RequestWrathTick(string reason)
        {
            try { _wrathRequestTick?.Invoke(reason); }
            catch (Exception ex) { WrathFailed($"RequestTick: {ex.Message}"); }
        }

        /// <summary>★ v0.3.21: <paramref name="command"/> is one Wrath Tactics issued (known to it once it ran it).</summary>
        public static bool IsWrathCommand(Kingmaker.EntitySystem.Entities.UnitEntityData unit, Kingmaker.UnitLogic.Commands.Base.UnitCommand command)
        {
            if (!_wrathBound || _wrathBroken || command == null) return false;
            try { return _wrathIsOurs(unit, command); }
            catch { return false; }
        }

        /// <summary>★ v0.3.21: at combat start (rule changes between fights are read again).</summary>
        public static void ClearWrathCache() => WrathManaged.Clear();

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
