// ★ v0.3.0: Facts that must survive re-decisions: what was cast on whom this combat, and what failed this turn.
//
// Lesson from CompanionAI v3 (LESSONS_LEARNED #20): turn facts live in state that re-planning does not throw away.
// Buff detection (target already has the buff) is the primary anti-spam rule; this memory is the backstop for effects
// the detector cannot see (enchantments, utility abilities, buffs applied through custom components).
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
        public static void Sync()
        {
            int round = Round;
            if (round < _lastRound)
                ResetCombat();
            _lastRound = round;
        }

        public static void ResetCombat()
        {
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
