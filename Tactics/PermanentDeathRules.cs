// ★ coverage3 review / Fusion coverage C3 (21 November 2026, author's policy; docs/COVERAGE3_REVIEW.md): the pure rules for a
// cast that can kill an ally for good. No game types: the game side is CombatMath.PermanentDeath.cs.
// The author's policy, verbatim: "I want a disintegrate that would permakill an ally to be weighed in such a manner that it
// will not be used. If an ally is at a low enough health where there is genuinely high risk of it killing them, it should be
// avoided entirely no matter the circumstances."
// Native contracts (read from the supplied assembly): Disintegrate's damage is followed by
// Conditional(ContextConditionCompareTargetHP Less 0) -> ContextActionApplyBuff(DisintegrateBuff), whose Activated actions run
// ContextActionKill (State.MarkedForDeath): the dust. The comparison reads HPLeft after the damage, so a downed ally is turned
// to dust by any hit, even one that deals nothing.
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class PermanentDeathRules
    {
        /// <summary>A risk at or above this never lets the cast be used, whatever its value and the settings.</summary>
        public const float VetoRisk = 0.01f;

        /// <summary>Below the veto, a permanent death costs this many times the ally's worth.</summary>
        public const float WorthMultiple = 100f;

        public static bool Vetoed(float risk) => float.IsNaN(risk) || risk >= VetoRisk;

        /// <summary>What a small chance of killing an ally for good costs, in damage-equivalent points.</summary>
        public static float Cost(float risk, float worth) =>
            risk > 0f && !float.IsNaN(risk) ? Math.Min(1f, risk) * WorthMultiple * Math.Max(1f, worth) : 0f;

        /// <summary>One damage roll: <see cref="Count"/> dice of <see cref="Sides"/> plus <see cref="Bonus"/>.</summary>
        public struct DiceGroup
        {
            public int Count, Sides, Bonus;
            /// <summary>Every die at its highest (Maximize).</summary>
            public bool Maximize;
            /// <summary>The roll times this, rounded down (Empower 1.5, half damage 0.5); 1 otherwise.</summary>
            public float Factor;
        }

        /// <summary>Dice past this count are unread for the distribution budget; they cannot prove a cast safe.</summary>
        public const int MaxDice = 200;
        // Bound work before allocating or multiplying distributions. Unread/oversized rolls cannot prove safety.
        internal const int MaxDistributionLength = 4096;
        private const long MaxConvolutionProducts = 1000000;
        private const int MaxCachedCells = 65536;

        /// <summary>
        /// The chance the sum of <paramref name="groups"/> is at least <paramref name="threshold"/>: exact within the
        /// distribution budget, otherwise the conservative bound 1. A threshold at or below 0 is already reached.
        /// </summary>
        public static float ChanceAtLeast(IReadOnlyList<DiceGroup> groups, int threshold)
        {
            if (threshold <= 0) return 1f;
            if (groups == null || groups.Count == 0) return 0f;
            double[] total = { 1d };   // P(sum = i)
            foreach (var g in groups)
            {
                var pmf = GroupPmf(g);
                if (pmf == null) return 1f;
                total = Convolve(total, pmf);
                if (total == null) return 1f;
            }
            double p = 0d;
            for (int i = Math.Min(threshold, total.Length); i < total.Length; i++) p += total[i];
            return (float)Math.Min(1d, Math.Max(0d, p));
        }

        private static double[] GroupPmf(DiceGroup g)
        {
            if (g.Count < 0 || g.Sides < 0 || g.Count > MaxDice || g.Sides > 100 ||
                float.IsNaN(g.Factor) || float.IsInfinity(g.Factor)) return null;
            float factor = g.Factor > 0f ? g.Factor : 1f;
            int bonus = Math.Max(0, g.Bonus);
            double maximum = Math.Floor(((long)g.Count * g.Sides + bonus) * (double)factor);
            if (maximum >= MaxDistributionLength) return null;
            if (g.Sides <= 0 || g.Count <= 0 || g.Maximize)
            {
                int value = (int)maximum;
                var single = new double[value + 1];
                single[value] = 1d;
                return single;
            }
            var dice = DicePmf(g.Count, g.Sides);
            if (dice == null) return null;
            int top = (int)maximum;
            var result = new double[Math.Max(1, top + 1)];
            for (int s = 0; s < dice.Length; s++)
            {
                if (dice[s] <= 0d) continue;
                int v = (int)Math.Floor((s + (double)bonus) * factor);
                if (v < 0) v = 0;
                if (v >= result.Length) v = result.Length - 1;
                result[v] += dice[s];
            }
            return result;
        }

        private static readonly Dictionary<long, double[]> DiceCache = new Dictionary<long, double[]>();
        private static readonly object CacheLock = new object();
        private static int s_CachedCells;

        /// <summary>P(sum of <paramref name="count"/> d<paramref name="sides"/> = s), cached: dice distributions never change.</summary>
        internal static double[] DicePmf(int count, int sides)
        {
            if (count < 0 || count > MaxDice || sides <= 0 || sides > 100 ||
                (long)count * sides >= MaxDistributionLength) return null;
            long key = ((long)count << 32) | (uint)sides;
            lock (CacheLock)
            {
                if (DiceCache.TryGetValue(key, out var cached)) return cached;
                double[] pmf = { 1d };
                double face = 1d / sides;
                var die = new double[sides + 1];
                for (int f = 1; f <= sides; f++) die[f] = face;
                for (int i = 0; i < count; i++)
                {
                    pmf = Convolve(pmf, die);
                    if (pmf == null) return null;
                }
                if (s_CachedCells + pmf.Length > MaxCachedCells) { DiceCache.Clear(); s_CachedCells = 0; }
                DiceCache[key] = pmf;
                s_CachedCells += pmf.Length;
                return pmf;
            }
        }

        private static double[] Convolve(double[] a, double[] b)
        {
            if ((long)a.Length + b.Length - 1 > MaxDistributionLength ||
                (long)a.Length * b.Length > MaxConvolutionProducts) return null;
            var r = new double[a.Length + b.Length - 1];
            for (int i = 0; i < a.Length; i++)
            {
                double ai = a[i];
                if (ai == 0d) continue;
                for (int j = 0; j < b.Length; j++) r[i + j] += ai * b[j];
            }
            return r;
        }

        /// <summary>
        /// The dust's trigger: the HP left after the damage is below <paramref name="below"/> (Less). With
        /// <paramref name="hpBeforeDown"/> = HP left plus temporary HP (temporary HP absorb first), the damage must reach
        /// hpBeforeDown − below + 1.
        /// </summary>
        public static int DamageToTrigger(float hpBeforeDown, int below) =>
            (int)Math.Floor(hpBeforeDown) - below + 1;

        /// <summary>The native dust gate reads lethal HPLeft, even when temporary HP remain or nonlethal damage
        /// has already knocked the recipient out. An already satisfied gate needs no further damage.</summary>
        public static int DamageToTrigger(int hpLeft, int temporaryHp, int below)
        {
            if (hpLeft < below) return 0;
            long needed = (long)hpLeft + Math.Max(0, temporaryHp) - below + 1;
            return needed > int.MaxValue ? int.MaxValue : (int)needed;
        }
    }
}
