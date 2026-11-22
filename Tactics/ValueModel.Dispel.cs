using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// ★ v0.3.25 (Unraveling): the game's RuleDispelMagic succeeds when d20 + caster level + bonus >= DC, with no
        /// automatic 1 or 20 (IsSuccessRoll). <paramref name="need"/> = DC - caster level - bonus: the d20 needed.
        /// </summary>
        public static float DispelChance(int need)
        {
            if (need <= 1) return 1f;
            if (need > 20) return 0f;
            return (21 - need) / 20f;
        }

        /// <summary>
        /// ★ v0.3.25 (Unraveling): the expected worth of ContextActionDispelMagic.RunAction over buffs in the order the game
        /// tries them (highest caster level first), each attempt an independent roll of chance <c>p</c> removing worth
        /// <c>w</c> (negative for one of our own debuffs), stopping once <paramref name="stopAfter"/> are removed
        /// (0 = no limit: every buff is tried).
        /// </summary>
        public static float ExpectedDispelWorth(IReadOnlyList<KeyValuePair<float, float>> chanceAndWorth, int stopAfter) =>
            ExpectedDispelWorth(chanceAndWorth, stopAfter, false);

        /// <summary>Targeted Dispel Magic reuses one d20 across attempts (OneRollForAll). Enumerating its twenty
        /// outcomes preserves that dependence; the independent-roll recurrence remains for other dispels.</summary>
        public static float ExpectedDispelWorth(IReadOnlyList<KeyValuePair<float, float>> chanceAndWorth, int stopAfter,
                                                bool oneRollForAll) => ExpectedDispelWorth(chanceAndWorth, stopAfter, oneRollForAll, out _);

        /// <summary>The same expectation, retaining the expected loss separately for a spell with several dispel actions.</summary>
        public static float ExpectedDispelWorth(IReadOnlyList<KeyValuePair<float, float>> chanceAndWorth, int stopAfter,
                                                bool oneRollForAll, out float loss)
        {
            loss = 0f;
            if (chanceAndWorth == null || chanceAndWorth.Count == 0) return 0f;
            if (stopAfter <= 0 || stopAfter >= chanceAndWorth.Count)
            {
                float all = 0f;
                foreach (var e in chanceAndWorth)
                {
                    float part = Clamp(e.Key, 0f, 1f) * e.Value;
                    all += part;
                    if (part < 0f) loss -= part;
                }
                return all;
            }
            if (oneRollForAll)
            {
                float sum = 0f;
                for (int outcome = 0; outcome < 20; outcome++)
                {
                    // Midpoints avoid rounding at a 5% boundary; native caster-level/DC chances are multiples of 1/20.
                    float quantile = (outcome + 0.5f) / 20f;
                    int removed = 0;
                    foreach (var e in chanceAndWorth)
                    {
                        if (!(quantile < Clamp(e.Key, 0f, 1f))) continue;
                        sum += e.Value;
                        if (e.Value < 0f) loss -= e.Value;
                        if (++removed >= stopAfter) break;
                    }
                }
                loss /= 20f;
                return sum / 20f;
            }
            // reach[k] = chance of arriving at this buff with k removed so far (k < stopAfter).
            var reach = new float[stopAfter + 1];
            reach[0] = 1f;
            float total = 0f;
            foreach (var e in chanceAndWorth)
            {
                float p = Clamp(e.Key, 0f, 1f);
                for (int k = stopAfter - 1; k >= 0; k--)
                {
                    if (reach[k] <= 0f) continue;
                    float part = reach[k] * p * e.Value;
                    total += part;
                    if (part < 0f) loss -= part;
                    reach[k + 1] += reach[k] * p;
                    reach[k] *= 1f - p;
                }
            }
            return total;
        }

        /// <summary>
        /// ★ coverage4 (freeing an ally under an enemy's control): the rounds an effect still holds within a horizon of
        /// <paramref name="rounds"/> when its bearer re-saves each round and a pass ends it (Dominate's NewRound Will save,
        /// ContextActionRemoveSelf on success): round k counts while every save before it has failed, (1 - p)^k, for the share
        /// of that round inside the horizon. With no re-save (p = 0) it is the horizon itself.
        /// </summary>
        public static float HeldRounds(float rounds, float passChance)
        {
            if (!(rounds > 0f)) return 0f;
            float stay = 1f - Clamp(passChance, 0f, 1f);
            float held = 0f, still = 1f;
            for (int k = 0; k < rounds && still > 0f; k++)
            {
                held += still * Math.Min(1f, rounds - k);
                still *= stay;
            }
            return held;
        }

        /// <summary>A fused dispel runs on its own root-save paths. Benefit on a recipient already dropped by that
        /// outcome is redundant; costs of stripped friendly buffs need not claim that survival discount.</summary>
        public static float DispelOutcomeShare(float failChance, bool onFailure, bool onPass,
                                               float failedSurvival = 1f, float passedSurvival = 1f)
        {
            float fail = Clamp(failChance, 0f, 1f);
            return (onFailure ? fail * Clamp(failedSurvival, 0f, 1f) : 0f) +
                   (onPass ? (1f - fail) * Clamp(passedSurvival, 0f, 1f) : 0f);
        }
    }
}
