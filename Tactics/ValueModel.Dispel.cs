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
        public static float ExpectedDispelWorth(IReadOnlyList<KeyValuePair<float, float>> chanceAndWorth, int stopAfter)
        {
            if (chanceAndWorth == null || chanceAndWorth.Count == 0) return 0f;
            if (stopAfter <= 0)
            {
                float all = 0f;
                foreach (var e in chanceAndWorth) all += Clamp(e.Key, 0f, 1f) * e.Value;
                return all;
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
                    total += reach[k] * p * e.Value;
                    reach[k + 1] += reach[k] * p;
                    reach[k] *= 1f - p;
                }
            }
            return total;
        }
    }
}
