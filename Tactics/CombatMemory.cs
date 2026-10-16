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
        private static int _lastRound = int.MaxValue;

        public static int Round => Game.Instance?.TurnBasedCombatController?.RoundNumber ?? 0;

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
            _lastRound = int.MaxValue;
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
        public int WaitFrames { get; set; }
        public bool Finished { get; set; }
        public readonly HashSet<string> Failed = new HashSet<string>();

        /// <summary>The last command we issued and the candidate it came from (to learn from no-op results).</summary>
        public UnitCommand LastCommand { get; set; }
        public string LastCandidateKey { get; set; }

        public TurnMemory(int round)
        {
            Round = round;
        }
    }
}
