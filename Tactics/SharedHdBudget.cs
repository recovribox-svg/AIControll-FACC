// ★ test1-scoring-fixes: a probability forecast of an ordered, finite shared resource.
// Native adapters supply target costs, graph reachability and resistance probabilities.
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class SharedHdBudget
    {
        // ★ Fusion coverage C8 (docs/FUSION_COVERAGE_C6_C8.md): Circle of Death's pool is d4 per caster level (up to 20, 25
        // intensified), half again empowered: 150. Visits are read once per recipient and pool interval (SharedHdControl), so
        // the bound on evaluations counts transitions, not native reads.
        public const int MaxBudget = 160;
        public const int MaxRecipients = 128;
        public const int MaxEvaluations = 32768;

        // Native: bonus before Empower's integer half; ContextCalculateSharedValue's modifier afterwards.
        internal static int PoolTotal(int roll, int bonus, double modifier, bool empowerable, bool empowered)
        {
            checked
            {
                int total = roll + bonus;
                if (empowerable && empowered) total += total / 2;
                if (double.IsNaN(modifier) || double.IsInfinity(modifier)) throw new ArgumentOutOfRangeException(nameof(modifier));
                return (int)(total * modifier);
            }
        }

        public readonly struct Visit
        {
            public readonly bool ReachesEffect;
            public readonly bool Spends;
            public readonly int Cost;

            public Visit(bool reachesEffect, bool spends, int cost)
            {
                ReachesEffect = reachesEffect;
                Spends = spends;
                Cost = cost;
            }
        }

        public sealed class Recipient
        {
            public readonly float ResistancePass;
            public readonly Func<int, Visit> Evaluate;

            public Recipient(float resistancePass, Func<int, Visit> evaluate)
            {
                ResistancePass = resistancePass;
                Evaluate = evaluate;
            }
        }

        /// <summary>
        /// Probability each effect node is reached given earlier recipients' consumption. The returned probability
        /// deliberately excludes this recipient's own resistance/save: the ordinary effect appraisal prices those.
        /// A supported graph spends after its save on either result, so saving throws are not transition inputs.
        /// Any unsupported/oversized forecast fails as a whole; no prefix of recipients is published.
        /// </summary>
        public static bool TryForecast(int initialBudget, IReadOnlyList<Recipient> recipients, out float[] eligibility) =>
            TryForecast(new[] { initialBudget }, new[] { 1f }, recipients, out eligibility);

        /// <summary>
        /// ★ Fusion coverage C8: the same forecast for a rolled pool, in one pass. The chain is linear in its starting
        /// distribution, so this equals the chance-weighted sum of one forecast per roll.
        /// </summary>
        public static bool TryForecast(IReadOnlyList<int> budgets, IReadOnlyList<float> chances, IReadOnlyList<Recipient> recipients,
                                       out float[] eligibility)
        {
            eligibility = null;
            if (budgets == null || chances == null || budgets.Count == 0 || budgets.Count != chances.Count ||
                recipients == null || recipients.Count > MaxRecipients) return false;
            int top = 0;
            for (int k = 0; k < budgets.Count; k++)
            {
                if (budgets[k] < 0 || budgets[k] > MaxBudget || float.IsNaN(chances[k]) || chances[k] < 0f) return false;
                top = Math.Max(top, budgets[k]);
            }
            var result = new float[recipients.Count];
            var current = new float[top + 1];
            var next = new float[current.Length];
            for (int k = 0; k < budgets.Count; k++) current[budgets[k]] += chances[k];
            int evaluations = 0;
            try
            {
                for (int i = 0; i < recipients.Count; i++)
                {
                    var recipient = recipients[i];
                    if (recipient?.Evaluate == null || float.IsNaN(recipient.ResistancePass) ||
                        recipient.ResistancePass < 0f || recipient.ResistancePass > 1f) return false;
                    Array.Clear(next, 0, next.Length);
                    for (int remaining = 0; remaining < current.Length; remaining++)
                    {
                        float probability = current[remaining];
                        if (probability <= 0f) continue;
                        if (++evaluations > MaxEvaluations) return false;
                        Visit visit = recipient.Evaluate(remaining);
                        if (visit.Cost < 0 || (visit.Spends && visit.Cost > remaining)) return false;
                        if (visit.ReachesEffect) result[i] += probability;
                        next[remaining] += probability * (1f - recipient.ResistancePass);
                        int after = visit.Spends ? remaining - visit.Cost : remaining;
                        next[after] += probability * recipient.ResistancePass;
                    }
                    var swap = current;
                    current = next;
                    next = swap;
                    result[i] = Math.Min(1f, result[i]);
                }
            }
            catch
            {
                return false;
            }
            eligibility = result;
            return true;
        }
    }
}
