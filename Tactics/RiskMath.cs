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
