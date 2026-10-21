// ★ v0.3.0: Facts that must survive re-decisions: what was cast on whom this combat, and what failed this turn.
//
// Lesson from CompanionAI v3 (LESSONS_LEARNED #20): turn facts live in state that re-planning does not throw away.
// Buff detection (target already has the buff) is the primary anti-spam rule; this memory is the backstop for effects
// the detector cannot see (enchantments, utility abilities, buffs applied through custom components).
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands.Base;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class CombatMemory
    {
        /// <summary>"caster|ability|target" → round it was cast (this combat).</summary>
        private static readonly Dictionary<string, int> Casts = new Dictionary<string, int>();
        /// <summary>"caster|ability" → times used this combat.</summary>
        private static readonly Dictionary<string, int> Uses = new Dictionary<string, int>();
        private static readonly Dictionary<string, TurnMemory> Turns = new Dictionary<string, TurnMemory>();

        /// <summary>★ v0.3.7: Progressive log bookkeeping for this combat (what was already reported, so transitions log once).</summary>
        public static readonly HashSet<string> ProgressiveLogged = new HashSet<string>();
        /// <summary>Boss unit id → name, while the boss bypass is active.</summary>
        public static readonly Dictionary<string, string> ActiveBosses = new Dictionary<string, string>();
        private static int _lastRound = int.MaxValue;
        /// <summary>★ v0.3.18 (AA-2): "unit|toggle" → round the AI last switched it (on or off), this combat.</summary>
        private static readonly Dictionary<string, int> ToggleSwitches = new Dictionary<string, int>();
        /// <summary>★ v0.3.18 (AA-2): toggles the AI switched on this combat (only those it may switch off again).</summary>
        private static readonly HashSet<string> ToggledOnByAi = new HashSet<string>();

        private static string ToggleKey(UnitEntityData unit, Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility toggle) =>
            $"{unit.UniqueId}|{toggle.Blueprint.AssetGuid}";

        /// <summary>★ v0.3.18: the AI switched this toggle this round already (no second switch in a round: no thrashing).</summary>
        public static bool ToggleSwitchedThisRound(UnitEntityData unit, Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility toggle) =>
            ToggleSwitches.TryGetValue(ToggleKey(unit, toggle), out int r) && r == Round;

        public static bool ToggleStartedByAi(UnitEntityData unit, Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility toggle) =>
            ToggledOnByAi.Contains(ToggleKey(unit, toggle));

        public static void RecordToggle(UnitEntityData unit, Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility toggle, bool on)
        {
            string key = ToggleKey(unit, toggle);
            ToggleSwitches[key] = Round;
            if (on) ToggledOnByAi.Add(key); else ToggledOnByAi.Remove(key);
        }

        /// <summary>
        /// The combat round. Turn-based: the game's round number. ★ v0.3.14: real time has no rounds, so it is six seconds
        /// of game time since this combat's first decision (the game's own real-time round length), counting from 1.
        /// </summary>
        public static int Round
        {
            get
            {
                if (TurnBased.Controllers.CombatController.IsInTurnBasedCombat())
                    return Game.Instance?.TurnBasedCombatController?.RoundNumber ?? 0;
                double now = Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;
                if (_realTimeStart < 0d) _realTimeStart = now;
                return 1 + (int)((now - _realTimeStart) / 6d);
            }
        }

        /// <summary>Game time (seconds) of this combat's first real-time decision; negative until then.</summary>
        private static double _realTimeStart = -1d;

        /// <summary>★ v0.3.14: the decision runs in real-time combat.</summary>
        public static bool IsRealTime => !TurnBased.Controllers.CombatController.IsInTurnBasedCombat();

        /// <summary>Called on every decision; a round number going backwards means a new combat started.</summary>
        /// <remarks>★ v0.3.18 (review): not when the fight switched between turn-based and real time - the two count
        /// rounds differently (the game's round number, 6-second windows), so the count went back and the toggle locks,
        /// casts and uses of the fight were wiped mid-fight. New fights are also reset by the party-combat events.</remarks>
        public static void Sync()
        {
            int round = Round;
            bool turnBased = !IsRealTime;
            if (_lastRound != int.MaxValue && turnBased != _lastTurnBased)
            {
                // Into real time: its 6-second rounds go on from the turn-based round reached.
                if (!turnBased)
                {
                    double now = Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;
                    _realTimeStart = now - Math.Max(0, _lastRound) * 6d;
                    round = Round;
                }
                _lastTurnBased = turnBased;
                _lastRound = round;
                return;
            }
            _lastTurnBased = turnBased;
            if (round < _lastRound)
                ResetCombat();
            _lastRound = round;
        }

        private static bool _lastTurnBased;

        /// <summary>
        /// ★ v0.3.18 (review): when the fight ends, the toggles the AI switched on that the game keeps on out of combat
        /// (DeactivateIfCombatEnded false - Mobility halves speed until switched off) go off again; the player had them
        /// off. Called before the combat memory is reset (after it, they would no longer be known as the AI's).
        /// </summary>
        public static void SwitchOffAiToggles()
        {
            foreach (string key in new List<string>(ToggledOnByAi))
            {
                try
                {
                    int bar = key.IndexOf('|');
                    if (bar < 0) continue;
                    string unitId = key.Substring(0, bar), guid = key.Substring(bar + 1);
                    UnitEntityData unit = null;
                    foreach (var u in Game.Instance.State.Units)
                        if (u.UniqueId == unitId) { unit = u; break; }
                    if (unit?.ActivatableAbilities == null) continue;
                    foreach (var toggle in unit.ActivatableAbilities.Enumerable)
                    {
                        if (toggle?.Blueprint == null || toggle.Blueprint.AssetGuid.ToString() != guid) continue;
                        if (toggle.IsOn && !toggle.Blueprint.DeactivateIfCombatEnded)
                        {
                            toggle.IsOn = false;
                            Main.Log($"[Toggle] {unit.CharacterName}: {toggle.Name} off - the fight is over (the AI had switched it on)");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Main.Verbose($"[Toggle] after combat {key}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// ★ v0.3.18 (test10): real time - an ally's cast of an ability on a target whose command is still running. Real
        /// time decides for every unit at once: three party members demoralized the same Cultist Champion within a second
        /// (19 such duplicates in the 0.3.18-test9 real-time log) and two of them tried to Coup de Grace the same Cambion.
        /// Turn-based never has one running when another unit decides.
        /// </summary>
        private sealed class PendingCast
        {
            public UnitEntityData Caster;
            public UnitEntityData Target;
            public string Ability;
            public UnitCommand Command;
            public double Until;
        }

        private static readonly List<PendingCast> PendingCasts = new List<PendingCast>();

        private static double GameSeconds => Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;

        public static void NotePendingCast(UnitEntityData caster, AbilityData ability, UnitEntityData target, UnitCommand command)
        {
            if (!IsRealTime || caster == null || target == null || ability?.Blueprint == null || command == null) return;
            PendingCasts.RemoveAll(p => p.Command.IsFinished || GameSeconds > p.Until);
            PendingCasts.Add(new PendingCast { Caster = caster, Target = target, Ability = AbilityKey(ability), Command = command, Until = GameSeconds + 8d });
        }

        /// <summary>Another unit's cast of this ability on <paramref name="target"/> is still running (real time).</summary>
        public static bool CastPendingByOther(UnitEntityData self, AbilityData ability, UnitEntityData target)
        {
            if (!IsRealTime || PendingCasts.Count == 0 || target == null || ability?.Blueprint == null) return false;
            string key = AbilityKey(ability);
            double now = GameSeconds;
            foreach (var p in PendingCasts)
                if (p.Caster != self && p.Target == target && p.Ability == key && !p.Command.IsFinished && now <= p.Until)
                    return true;
            return false;
        }

        /// <summary>★ v0.3.18 (test10): "unit|ability" → the caster's burn and the dead around it when it last used a burn
        /// relief (Soul Power).</summary>
        private static readonly Dictionary<string, KeyValuePair<int, int>> BurnReliefs = new Dictionary<string, KeyValuePair<int, int>>();

        public static void NoteBurnRelief(UnitEntityData unit, Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility ability, int burn, int deadAround) =>
            BurnReliefs[$"{unit.UniqueId}|{ability?.AssetGuid}"] = new KeyValuePair<int, int>(burn, deadAround);

        /// <summary>The burn relief was used and neither the burn nor the dead around changed since.</summary>
        public static bool BurnReliefUnchanged(UnitEntityData unit, Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility ability, int burn, int deadAround) =>
            BurnReliefs.TryGetValue($"{unit.UniqueId}|{ability?.AssetGuid}", out var last) && last.Key == burn && last.Value == deadAround;

        /// <summary>★ v0.3.18 (test10): unit → the round it chose to stay prone (TacticalEngine.ShouldStandUp).</summary>
        private static readonly Dictionary<string, int> ProneStays = new Dictionary<string, int>();

        /// <summary>
        /// The unit may stay prone this round: it did not already stay down last round. Staying down does not avoid the
        /// attacks of opportunity of standing, it puts them off; waiting one turn lets the party change the situation (kill
        /// or draw off the enemies next to it, heal it), waiting longer only loses turns - Aeiluvetar lay prone for seven
        /// turns in a row (0.3.18-test9 turn-based log).
        /// </summary>
        public static bool MayStayProne(UnitEntityData unit)
        {
            int round = Round;
            if (ProneStays.TryGetValue(unit.UniqueId, out int stayed))
            {
                if (stayed == round) return true;             // already decided this turn
                if (stayed == round - 1) return false;        // stayed down last turn: stand now
            }
            ProneStays[unit.UniqueId] = round;
            return true;
        }

        public static void ResetCombat()
        {
            PendingCasts.Clear();
            ProneStays.Clear();
            BurnReliefs.Clear();
            ToggleSwitches.Clear();
            ToggledOnByAi.Clear();
            Casts.Clear();
            Uses.Clear();
            Turns.Clear();
            ProgressiveLogged.Clear();
            ActiveBosses.Clear();
            ProgressiveGate.ResetCombat();
            _lastRound = int.MaxValue;
            _realTimeStart = -1d;
            AbilityProfile.ClearConditionCache();
        }

        public static string AbilityKey(AbilityData ability)
        {
            return ability?.Blueprint?.AssetGuid.ToString() ?? "none";
        }

        public static void RecordCast(UnitEntityData caster, AbilityData ability, UnitEntityData target)
        {
            string key = $"{caster.UniqueId}|{AbilityKey(ability)}|{target?.UniqueId ?? "-"}";
            Casts[key] = Round;
            string useKey = $"{caster.UniqueId}|{AbilityKey(ability)}";
            Uses.TryGetValue(useKey, out int n);
            Uses[useKey] = n + 1;
        }

        /// <summary>Round the ability was last cast on that target this combat, or null.</summary>
        public static int? LastCastRound(UnitEntityData caster, AbilityData ability, UnitEntityData target)
        {
            return Casts.TryGetValue($"{caster.UniqueId}|{AbilityKey(ability)}|{target?.UniqueId ?? "-"}", out int r) ? r : (int?)null;
        }

        public static int TimesUsed(UnitEntityData caster, AbilityData ability)
        {
            return Uses.TryGetValue($"{caster.UniqueId}|{AbilityKey(ability)}", out int n) ? n : 0;
        }

        public static TurnMemory ForTurn(UnitEntityData unit)
        {
            int round = Round;
            if (!Turns.TryGetValue(unit.UniqueId, out var memory) || memory.Round != round)
            {
                memory = new TurnMemory(round);
                Turns[unit.UniqueId] = memory;
            }
            return memory;
        }
    }

    public sealed class TurnMemory
    {
        public int Round { get; }
        public int Decisions { get; set; }
        /// <summary>★ v0.3.1: wait bookkeeping in seconds of ticked time (frame counts depend on FPS).</summary>
        public bool Waiting { get; set; }
        public float WaitSeconds { get; set; }
        public float IdleSeconds { get; set; }
        public float LastTick { get; set; }
        public UnityEngine.Vector3 LastPosition { get; set; }
        public bool Finished { get; set; }
        /// <summary>★ v0.3.14: orders given this turn to stand the unit up (TurnBasedController.StandUp).</summary>
        public int StandUpAttempts { get; set; }
        /// <summary>★ v0.3.14 (test5): "cannot act but can walk" was reported this turn.</summary>
        public bool MovementOnlyLogged { get; set; }
        public readonly HashSet<string> Failed = new HashSet<string>();
        /// <summary>★ v0.3.11: enemies (unique ids) a path could not reach this turn - not worth advancing towards.</summary>
        public readonly HashSet<string> Unreachable = new HashSet<string>();

        /// <summary>The last command we issued and the candidate it came from (to learn from no-op results).</summary>
        public UnitCommand LastCommand { get; set; }
        public string LastCandidateKey { get; set; }
        public ActionSlot LastSlot { get; set; }
        public CandidateKind LastKind { get; set; }
        public UnityEngine.Vector3 LastIssuePosition { get; set; }

        public TurnMemory(int round)
        {
            Round = round;
        }
    }
}
