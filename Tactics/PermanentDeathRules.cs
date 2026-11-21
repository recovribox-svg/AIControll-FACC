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

        public static bool Vetoed(float risk) => risk >= VetoRisk;

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

        /// <summary>Dice past this count are not distributed (no ability rolls that many); the group then counts at its most.</summary>
        public const int MaxDice = 200;

        /// <summary>
        /// The chance the sum of <paramref name="groups"/> is at least <paramref name="threshold"/>, exactly (each group's own
        /// distribution, convolved). 1 for a threshold at or below 0: any outcome reaches it.
        /// </summary>
        public static float ChanceAtLeast(IReadOnlyList<DiceGroup> groups, int threshold)
        {
            if (threshold <= 0) return 1f;
            if (groups == null || groups.Count == 0) return 0f;
            double[] total = { 1d };   // P(sum = i)
            foreach (var g in groups)
            {
                var pmf = GroupPmf(g);
                if (pmf == null) continue;
                total = Convolve(total, pmf);
                if (total.Length > 200000) return 1f;   // guard: an absurd roll counts as reaching anything
            }
            double p = 0d;
            for (int i = Math.Min(threshold, total.Length); i < total.Length; i++) p += total[i];
            return (float)Math.Min(1d, Math.Max(0d, p));
        }

        private static double[] GroupPmf(DiceGroup g)
        {
            float factor = g.Factor > 0f ? g.Factor : 1f;
            int bonus = Math.Max(0, g.Bonus);
            if (g.Sides <= 0 || g.Count <= 0 || g.Maximize || g.Count > MaxDice)
            {
                int most = g.Sides > 0 && g.Count > 0 ? Math.Min(g.Count, MaxDice * 10) * g.Sides : 0;
                int value = (int)Math.Floor((most + bonus) * factor);
                if (value < 0) value = 0;
                var single = new double[value + 1];
                single[value] = 1d;
                return single;
            }
            var dice = DicePmf(g.Count, g.Sides);
            int top = (int)Math.Floor((dice.Length - 1 + bonus) * factor);
            var result = new double[Math.Max(1, top + 1)];
            for (int s = 0; s < dice.Length; s++)
            {
                if (dice[s] <= 0d) continue;
                int v = (int)Math.Floor((s + bonus) * factor);
                if (v < 0) v = 0;
                if (v >= result.Length) v = result.Length - 1;
                result[v] += dice[s];
            }
            return result;
        }

        private static readonly Dictionary<long, double[]> DiceCache = new Dictionary<long, double[]>();
        private static readonly object CacheLock = new object();

        /// <summary>P(sum of <paramref name="count"/> d<paramref name="sides"/> = s), cached: dice distributions never change.</summary>
        internal static double[] DicePmf(int count, int sides)
        {
            long key = (long)count * 1000 + sides;
            lock (CacheLock)
            {
                if (DiceCache.TryGetValue(key, out var cached)) return cached;
                double[] pmf = { 1d };
                double face = 1d / sides;
                var die = new double[sides + 1];
                for (int f = 1; f <= sides; f++) die[f] = face;
                for (int i = 0; i < count; i++) pmf = Convolve(pmf, die);
                if (DiceCache.Count > 512) DiceCache.Clear();
                DiceCache[key] = pmf;
                return pmf;
            }
        }

        private static double[] Convolve(double[] a, double[] b)
        {
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
    }
}
