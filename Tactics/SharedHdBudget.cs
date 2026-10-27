// ★ test1-scoring-fixes: a probability forecast of an ordered, finite shared resource.
// Native adapters supply target costs, graph reachability and resistance probabilities.
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class SharedHdBudget
    {
        public const int MaxBudget = 64;
        public const int MaxRecipients = 128;
        public const int MaxEvaluations = 8192;

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
        public static bool TryForecast(int initialBudget, IReadOnlyList<Recipient> recipients, out float[] eligibility)
        {
            eligibility = null;
            if (initialBudget < 0 || initialBudget > MaxBudget || recipients == null || recipients.Count > MaxRecipients)
                return false;
            var result = new float[recipients.Count];
            var current = new float[initialBudget + 1];
            var next = new float[current.Length];
            current[initialBudget] = 1f;
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
