using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>
    /// ★ v0.3.70 playtest: what a unit's spells and spell-like abilities are worth to it over its best other main action,
    /// measured from its own decisions (pure, unit-tested). A buff that forbids spellcasting (Inspired Rage, accepted by an
    /// ally) costs that recipient this edge for each round it lasts. In the mastermind save the summons under Camellia's
    /// rage picked options worth far less: Shadow Demon 35 -> 9 (no Dragon's Breath), Nabasu 48 -> 8, Brimorak 21 -> 7.
    /// </summary>
    /// <remarks>
    /// Kept per unit, and per kind for a unit that has not decided yet (a new summon of the same creature). The mean is a
    /// running average, so a breath weapon that recharges for some rounds counts as often as it is ready. Bounded: the
    /// table is cleared when it reaches <see cref="MaxEntries"/> keys.
    /// </remarks>
    internal sealed class SpellEdgeMemory
    {
        internal const int MaxEntries = 512;
        internal const float Alpha = 0.35f;

        private struct Edge
        {
            public float Mean;
            public int Samples;
        }

        private readonly Dictionary<string, Edge> edges = new Dictionary<string, Edge>(StringComparer.Ordinal);

        internal int Count => edges.Count;

        /// <summary>One decision's edge: the best spell's value less the best other main option's, at least 0.</summary>
        internal static float EdgeOf(float bestSpell, float bestOther)
        {
            if (!(bestSpell > 0f)) return 0f;
            return Math.Max(0f, bestSpell - Math.Max(0f, bestOther));
        }

        internal void Note(string unitKey, string kindKey, float edge)
        {
            if (float.IsNaN(edge) || float.IsInfinity(edge)) return;
            edge = Math.Max(0f, edge);
            if (edges.Count + 2 > MaxEntries) edges.Clear();
            Add(unitKey, edge);
            Add(kindKey, edge);
        }

        private void Add(string key, float edge)
        {
            if (string.IsNullOrEmpty(key)) return;
            edges.TryGetValue(key, out var e);
            e.Mean = e.Samples == 0 ? edge : e.Mean + Alpha * (edge - e.Mean);
            e.Samples++;
            edges[key] = e;
        }

        /// <summary>The unit's own mean, else its kind's; false when neither has been measured.</summary>
        internal bool TryGet(string unitKey, string kindKey, out float edge, out int samples)
        {
            if (!string.IsNullOrEmpty(unitKey) && edges.TryGetValue(unitKey, out var e) ||
                !string.IsNullOrEmpty(kindKey) && edges.TryGetValue(kindKey, out e))
            {
                edge = e.Mean;
                samples = e.Samples;
                return true;
            }
            edge = 0f;
            samples = 0;
            return false;
        }

        /// <summary>What a spell prohibition lasting <paramref name="rounds"/> rounds costs at this edge.</summary>
        internal static float Loss(float edgePerRound, float rounds) =>
            Math.Max(0f, edgePerRound) * Math.Max(0f, rounds);

        internal void Clear() => edges.Clear();
    }
}
