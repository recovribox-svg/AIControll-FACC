// ★ v0.3.14: the chance that attacks drop a unit, from hit chance, damage per hit and critical hits
// (no game types, unit-tested in tests/).
//
// The engine priced attacks of opportunity by their *expected* damage only. A big hitter's single attack has a modest
// expectation but a real chance to take a character out in one blow: Camellia charged past the Large Water Elemental,
// and one slam dropped her. Here the damage of a hit is taken as spread evenly between half and one and a half times
// its average (close to the spread of a couple of dice plus a bonus), and a critical hit multiplies it.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class RiskMath
    {
        /// <summary>Chance that one hit of average damage <paramref name="perHit"/> deals at least <paramref name="hp"/>.</summary>
        public static float ChanceAtLeast(float perHit, float hp)
        {
            if (hp <= 0f) return 1f;
            if (perHit <= 0f) return 0f;
            // damage ~ uniform [0.5 D, 1.5 D]
            return Clamp01((1.5f * perHit - hp) / perHit);
        }

        /// <summary>
        /// Chance that one attack drops a unit with <paramref name="hp"/> HP: it hits (<paramref name="hitChance"/>), and
        /// the damage reaches the HP; a critical hit (<paramref name="critChance"/>, part of the hits) multiplies it.
        /// </summary>
        public static float AttackDropChance(float hitChance, float critChance, float perHit, float critMultiplier, float hp)
        {
            hitChance = Clamp01(hitChance);
            critChance = Math.Min(Clamp01(critChance), hitChance);
            float normal = (hitChance - critChance) * ChanceAtLeast(perHit, hp);
            float crit = critChance * ChanceAtLeast(perHit * Math.Max(1f, critMultiplier), hp);
            return Clamp01(normal + crit);
        }

        /// <summary>
        /// ★ v0.3.29 test 2 (author-approved, R3): chance that a full attack of <paramref name="attacks"/> attacks drops a unit
        /// with <paramref name="hp"/> HP - each attack hits with <paramref name="hitChance"/>, part of the hits critical
        /// (<paramref name="critChance"/>, damage x <paramref name="critMultiplier"/>), each hit averaging
        /// <paramref name="perHit"/>: every count of plain and critical hits by its chance (a multinomial), and the damage of
        /// those hits with the spread of <see cref="ChanceAtLeast"/>. One attack is <see cref="AttackDropChance"/>.
        /// </summary>
        public static float FullAttackDropChance(int attacks, float hitChance, float critChance, float perHit, float critMultiplier, float hp)
        {
            if (hp <= 0f) return 1f;
            if (attacks <= 0 || !(perHit > 0f) || !(hitChance > 0f)) return 0f;
            int n = Math.Min(attacks, MaxAttacks);
            float hit = Clamp01(hitChance);
            float crit = Math.Min(Clamp01(critChance), hit);
            double plain = hit - crit, miss = 1d - hit, m = Math.Max(1f, critMultiplier);
            // Powers once (the engine asks this for every spot it prices): plain^k, crit^j, miss^r for 0..n, in buffers kept
            // between calls (the engine runs on one thread) so pricing a spot allocates nothing.
            double[] pp = s_Plain, cp = s_Crit, mp = s_Miss;
            pp[0] = cp[0] = mp[0] = 1d;
            for (int i = 1; i <= n; i++) { pp[i] = pp[i - 1] * plain; cp[i] = cp[i - 1] * crit; mp[i] = mp[i - 1] * miss; }
            double drop = 0d;
            for (int k = 0; k <= n; k++)
                for (int j = 0; k + j <= n; j++)
                {
                    if (k + j == 0) continue;
                    double chance = Choose(n, k) * Choose(n - k, j) * pp[k] * cp[j] * mp[n - k - j];
                    if (chance <= 1e-9) continue;
                    drop += chance * ChanceAtLeast((float)(perHit * (k + j * m)), hp);
                }
            return Clamp01((float)drop);
        }

        /// <summary>Attacks of one full attack counted at most (a hydra's heads); the rest add little to a drop chance.</summary>
        private const int MaxAttacks = 10;

        private static readonly double[] s_Plain = new double[MaxAttacks + 1], s_Crit = new double[MaxAttacks + 1],
                                         s_Miss = new double[MaxAttacks + 1];

        private static double Choose(int n, int k)
        {
            if (k < 0 || k > n) return 0d;
            double c = 1d;
            for (int i = 1; i <= k; i++) c = c * (n - k + i) / i;
            return c;
        }

        /// <summary>Chance that at least one of several independent attacks drops the unit (each against full HP).</summary>
        public static float AnyDrops(params float[] chances)
        {
            float none = 1f;
            foreach (var c in chances) none *= 1f - Clamp01(c);
            return 1f - none;
        }

        /// <summary>The engine's model for "down before its next turn": exp(-HP / expected incoming damage).</summary>
        public static float RoundDropChance(float hp, float danger)
        {
            if (danger <= 0f) return 0f;
            return (float)Math.Exp(-Math.Max(1f, hp) / danger);
        }

        /// <summary>
        /// ★ v0.3.23 (build3 logs): in the same model, the damage a unit is expected to have taken when it is still up at the
        /// end of the round - what is left for the next blow to finish. E[D | D &lt; hp] for an exponential D with mean
        /// <paramref name="danger"/>: danger - hp·e^(-hp/danger) / (1 - e^(-hp/danger)).
        /// </summary>
        public static float ExpectedTakenIfStillUp(float hp, float danger)
        {
            if (danger <= 0f) return 0f;
            hp = Math.Max(1f, hp);
            double down = Math.Exp(-hp / danger);
            if (down >= 1.0) return 0f;
            double taken = danger - hp * down / (1.0 - down);
            return (float)Math.Max(0.0, Math.Min(hp, taken));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
