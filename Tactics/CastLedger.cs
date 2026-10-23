// ★ v0.3.20 (audit LIF-001): what the AI has cast this combat, counted by casts the game performed.
//
// CombatMemory recorded a cast when the command was issued. A command interrupted before it acted (a new order, a
// replan when the unit lost HP or stood in a new area, a stuck walk cancelled, the game dropping the party's orders at
// combat start) still counted: an ability valued by the once-per-combat rule for effects the AI cannot see (an
// enchantment, a dispel) was then never offered again on that target for the rest of the fight, a buff counted as
// "cast this round", and a debuff's repeat discount grew. The record is now kept open until the caster's command ends,
// then confirmed (it acted) or taken back (it did not). No game types: unit-tested (tests/CompanionAI.Tests).
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class CastLedger
    {
        /// <summary>"caster|ability|target" → round it was cast (this combat).</summary>
        private readonly Dictionary<string, int> m_Casts = new Dictionary<string, int>();
        /// <summary>"caster|ability" → times used this combat.</summary>
        private readonly Dictionary<string, int> m_Uses = new Dictionary<string, int>();
        /// <summary>caster → its last record, until its command has ended.</summary>
        private readonly Dictionary<string, Open> m_Open = new Dictionary<string, Open>();

        private sealed class Open
        {
            public string CastKey, UseKey;
            public int? PreviousRound;
        }

        /// <summary>The game performed the command: it started and acted. A command force-finished before it started
        /// (turn-based approach that stopped) or interrupted before acting did nothing.</summary>
        public static bool Performed(bool started, bool acted) => started && acted;

        private static string CastKey(string caster, string ability, string target) => $"{caster}|{ability}|{target ?? "-"}";
        private static string UseKey(string caster, string ability) => $"{caster}|{ability}";

        /// <summary>A cast was issued; it stays open until <see cref="Confirm"/> or <see cref="TakeBack"/>.</summary>
        public void Record(string caster, string ability, string target, int round)
        {
            // An earlier record still open is kept, as before this ledger existed.
            m_Open.Remove(caster);
            string castKey = CastKey(caster, ability, target), useKey = UseKey(caster, ability);
            m_Open[caster] = new Open { CastKey = castKey, UseKey = useKey, PreviousRound = m_Casts.TryGetValue(castKey, out int r) ? r : (int?)null };
            m_Casts[castKey] = round;
            m_Uses.TryGetValue(useKey, out int n);
            m_Uses[useKey] = n + 1;
        }

        /// <summary>Whether the caster has a record waiting for its command to end.</summary>
        public bool IsOpen(string caster) => m_Open.ContainsKey(caster);

        /// <summary>The caster's command acted: its record stands.</summary>
        public void Confirm(string caster) => m_Open.Remove(caster);

        /// <summary>The caster's command did nothing: its record is taken back (the round and use count before it).</summary>
        public bool TakeBack(string caster)
        {
            if (!m_Open.TryGetValue(caster, out var open)) return false;
            m_Open.Remove(caster);
            if (open.PreviousRound.HasValue) m_Casts[open.CastKey] = open.PreviousRound.Value;
            else m_Casts.Remove(open.CastKey);
            if (m_Uses.TryGetValue(open.UseKey, out int n))
            {
                if (n <= 1) m_Uses.Remove(open.UseKey);
                else m_Uses[open.UseKey] = n - 1;
            }
            return true;
        }

        public int? LastRound(string caster, string ability, string target) =>
            m_Casts.TryGetValue(CastKey(caster, ability, target), out int r) ? r : (int?)null;

        public int TimesUsed(string caster, string ability) => m_Uses.TryGetValue(UseKey(caster, ability), out int n) ? n : 0;

        /// <summary>The targets the caster aimed this ability at this combat, with the round.</summary>
        public List<KeyValuePair<string, int>> Targets(string caster, string ability)
        {
            var result = new List<KeyValuePair<string, int>>();
            string prefix = $"{caster}|{ability}|";
            foreach (var cast in m_Casts)
                if (cast.Key.StartsWith(prefix)) result.Add(new KeyValuePair<string, int>(cast.Key.Substring(prefix.Length), cast.Value));
            return result;
        }

        public void Clear()
        {
            m_Casts.Clear();
            m_Uses.Clear();
            m_Open.Clear();
        }
    }
}
