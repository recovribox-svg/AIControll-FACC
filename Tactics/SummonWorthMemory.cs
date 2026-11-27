using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>
    /// ★ v0.3.70 F3 (author request): what each kind of summoned creature has dealt per round it fought, this session (pure,
    /// unit-tested). The rounds are counted once per creature and round it stood in a fight; the damage is the game's own
    /// final damage of its hits on enemies. Bounded: cleared when it reaches <see cref="MaxKinds"/> kinds.
    /// </summary>
    internal sealed class SummonWorthMemory
    {
        internal const int MaxKinds = 256;

        private struct Tally
        {
            public float Damage;
            public int Rounds;
        }

        private readonly Dictionary<string, Tally> kinds = new Dictionary<string, Tally>(StringComparer.Ordinal);

        internal int Count => kinds.Count;

        internal void NoteRound(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return;
            if (!kinds.ContainsKey(kind) && kinds.Count >= MaxKinds) kinds.Clear();
            kinds.TryGetValue(kind, out var t);
            t.Rounds++;
            kinds[kind] = t;
        }

        internal void NoteDamage(string kind, float damage)
        {
            if (string.IsNullOrEmpty(kind) || !(damage > 0f) || float.IsInfinity(damage)) return;
            if (!kinds.ContainsKey(kind) && kinds.Count >= MaxKinds) kinds.Clear();
            kinds.TryGetValue(kind, out var t);
            t.Damage += damage;
            kinds[kind] = t;
        }

        /// <summary>The kind's damage per round it fought; false before it has fought a round.</summary>
        internal bool TryPerRound(string kind, out float perRound, out int rounds)
        {
            perRound = 0f;
            rounds = 0;
            if (string.IsNullOrEmpty(kind) || !kinds.TryGetValue(kind, out var t) || t.Rounds <= 0) return false;
            rounds = t.Rounds;
            perRound = t.Damage / t.Rounds;
            return true;
        }

        internal void Clear() => kinds.Clear();
    }
}
