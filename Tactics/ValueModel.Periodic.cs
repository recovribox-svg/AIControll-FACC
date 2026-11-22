using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        // ContextValueHelper adds the integer half to an empowered roll. Average that rounding too, without rolling dice.
        public static float PeriodicDiceMean(int count, int sides, int bonus, bool maximize, bool empower)
        {
            if (count < 0 || bonus < 0) throw new ArgumentOutOfRangeException();
            bool fixedRoll = count == 0 || sides <= 1 || maximize;
            double mean = bonus + count * (sides > 0 ? (maximize ? sides : (sides + 1d) / 2d) : 0d);
            if (!empower) return (float)mean;
            double odd = fixedRoll ? ((long)mean & 1L) :
                .5d * (1d - Math.Pow(sides % 2 == 0 ? 0d : -1d / sides, count) * (bonus % 2 == 0 ? 1d : -1d));
            return (float)(1.5d * mean - .5d * odd);
        }

        // ★ v0.3.30 C06R4: native BuffCollection ticks before removing a buff at an equal end time.
        public static int PeriodicTicks(double nextSeconds, double intervalSeconds, double remainingSeconds, double horizonSeconds)
        {
            if (double.IsNaN(nextSeconds) || !(intervalSeconds > 0d) || double.IsInfinity(intervalSeconds) ||
                !(remainingSeconds >= 0d) || !(horizonSeconds >= 0d) || double.IsInfinity(horizonSeconds)) return 0;
            double first = Math.Max(0d, nextSeconds), last = Math.Min(remainingSeconds, horizonSeconds);
            if (first > last || double.IsInfinity(first)) return 0;
            return (int)Math.Min(int.MaxValue, Math.Floor((last - first) / intervalSeconds) + 1d);
        }

        /// <summary>Expected executions of an already weighted tick, while a native save/removal can end later ticks.</summary>
        /// <summary>
        /// ★ Fusion coverage C6: the chance a buff's end (its Deactivated actions) comes within the horizon: always when its
        /// duration runs out there; otherwise only when one of its <paramref name="ticks"/> removes it (a removal deactivates it),
        /// each tick keeping it on with <paramref name="survivesTick"/>.
        /// </summary>
        public static float CarrierEndChance(int ticks, float survivesTick, bool durationWithinHorizon)
        {
            if (durationWithinHorizon) return 1f;
            if (ticks <= 0) return 0f;
            float stay = Math.Max(0f, Math.Min(1f, survivesTick));
            return (float)(1d - Math.Pow(stay, ticks));
        }

        /// <summary>
        /// ★ Fusion coverage C6: rounds a round-tick control is on, over <paramref name="ticks"/> ticks (Phantasmal Putrefaction).
        /// Each tick the bearer rolls a first save: a pass removes the buff and its control (a child); a failure while it has no
        /// control yet rolls a second save, whose failure puts the control on until the buff goes. A round counts once the tick
        /// at its start has left the control on.
        /// </summary>
        public static float TickControlRounds(float firstFail, float secondFail, int ticks)
        {
            if (ticks <= 0) return 0f;
            double w = Math.Max(0f, Math.Min(1f, firstFail)), f = Math.Max(0f, Math.Min(1f, secondFail));
            double free = 1d, held = 0d, rounds = 0d;
            for (int t = 0; t < ticks; t++)
            {
                double nextHeld = free * w * f + held * w;
                free = free * w * (1d - f);
                held = nextHeld;
                rounds += held;
            }
            return (float)rounds;
        }

        public static float PeriodicWeight(int ticks, float survivesTick)
        {
            if (ticks <= 0) return 0f;
            float stay = Math.Max(0f, Math.Min(1f, survivesTick));
            if (stay >= 1f) return ticks;
            if (stay <= 0f) return 1f;
            return (float)((1d - Math.Pow(stay, ticks)) / (1d - stay));
        }

        /// <summary>Future native poison doses, after its activation dose, using its actual remaining save/tick counters.</summary>
        public static float ActivePoisonDoses(float fail, int ticks, int savesNeeded, bool damageOnSuccess)
        {
            if (ticks <= 0 || savesNeeded <= 0) return 0f;
            fail = Math.Max(0f, Math.Min(1f, fail));
            if (savesNeeded > ticks) return ticks * (damageOnSuccess ? 1f : fail);
            var states = new double[savesNeeded]; states[0] = 1d;
            double doses = 0d;
            for (int tick = 0; tick < ticks; tick++)
            {
                double alive = 0d;
                for (int i = 0; i < states.Length; i++) alive += states[i];
                doses += alive * (damageOnSuccess ? 1d : fail);
                for (int i = states.Length - 1; i >= 0; i--)
                    states[i] = states[i] * fail + (i > 0 ? states[i - 1] * (1d - fail) : 0d);
            }
            return (float)doses;
        }

        /// <summary>Calibrate new enemy attacks only; attached damage stays at every position without calibration.</summary>
        public static float IncomingWithAttached(float enemyRaw, float scale, float recentEnemy, bool nearCurrent, float attached)
        {
            float danger = enemyRaw * scale;
            if (nearCurrent) danger = Math.Max(danger, Math.Min(recentEnemy, enemyRaw));
            return danger + Math.Max(0f, attached);
        }

        public static float AfterEnemyProtection(float incoming, float attached, float avoidedShare) =>
            incoming - Math.Max(0f, incoming - Math.Max(0f, attached)) * avoidedShare;

        public static float WithBackgroundDrop(float enemyDrop, float backgroundDrop)
        {
            if (!(backgroundDrop > 0f)) return enemyDrop;
            if (!(enemyDrop > 0f)) return backgroundDrop;
            return 1f - (1f - Math.Min(1f, enemyDrop)) * (1f - Math.Min(1f, backgroundDrop));
        }
    }
}
