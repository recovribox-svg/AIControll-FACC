using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        public enum ToggleSpendCadence { Free, Activation, Round, Minute, TenMinutes, Hour, ExternalActivation }

        /// <summary>A bounded resource forecast, separate from native legality and the player's toggle policy.</summary>
        public readonly struct ToggleSpendWindow
        {
            public readonly float Rounds, NextPaymentRounds;
            public readonly int StartCost, ContinueCost;
            public readonly bool StopSavesResource;
            public bool Spends => StartCost > 0 || ContinueCost > 0;
            public ToggleSpendWindow(float rounds, float next, int start, int continuing, bool saves)
            { Rounds = rounds; NextPaymentRounds = next; StartCost = start; ContinueCost = continuing; StopSavesResource = saves; }
        }

        /// <summary>Native event callbacks check availability after spending. JudgmentSwitch waives the one-point
        /// requirement; an empty pool alone cannot end a Judgment while that mechanic is held.</summary>
        public static bool ToggleEventLifetimeUnknown(bool deactivateImmediately, int quantity, int cost,
            ToggleSpendWindow window, bool needsPositivePool) =>
            needsPositivePool && deactivateImmediately && cost > 0 && window.Rounds > 0f &&
            quantity - window.StartCost - window.ContinueCost <= 0;

        /// <summary>
        /// Native callbacks: Start/TurnOn pay once; timed modes pay at start and renew after their counter reaches zero;
        /// NewRound pays on each callback. An active toggle has already paid its start. The next callback may be due now.
        /// Event/manual costs require a separate triggering-action contract and are not admitted to this forecast.
        /// </summary>
        public static ToggleSpendWindow ToggleResourceWindow(ToggleSpendCadence cadence, int quantity, int cost,
            bool started, float expectedRounds, float nextRoundSeconds = 0f, int counter = 0,
            bool endsEmptyAfterStart = false)
        {
            float horizon = float.IsNaN(expectedRounds) ? 1f : Clamp(expectedRounds, 1f, 6f);
            cost = Math.Max(0, cost);
            quantity = Math.Max(0, quantity);
            int interval = cadence == ToggleSpendCadence.Round ? 1 : cadence == ToggleSpendCadence.Minute ? 10
                : cadence == ToggleSpendCadence.TenMinutes ? 100 : cadence == ToggleSpendCadence.Hour ? 600 : 0;
            bool paysStart = cadence == ToggleSpendCadence.Activation || cadence == ToggleSpendCadence.ExternalActivation || interval > 1;
            int startCost = !started && paysStart ? cost : 0;
            if (quantity < startCost || (!started && paysStart && endsEmptyAfterStart && quantity - startCost < 1))
                return new ToggleSpendWindow(0f, float.PositiveInfinity, startCost, 0, false);
            int left = quantity - startCost;
            if (interval == 0 || cost == 0)
                return new ToggleSpendWindow(horizon, float.PositiveInfinity, startCost, 0, false);
            float nextTick = started && !float.IsNaN(nextRoundSeconds) ? Clamp(nextRoundSeconds / 6f, 0f, 1f) : 0f;
            // TryStart sets TimeToNextRound=0. That first callback also decrements a newly reset timed counter.
            float next = interval == 1 ? nextTick : nextTick + Math.Max(0, (started ? counter : interval) - 1);
            int affordable = left / cost;
            float untilEmpty = next + (float)affordable * interval;
            float rounds = Math.Min(horizon, untilEmpty);
            int payments = next < horizon ? Math.Min(affordable, (int)Math.Ceiling((horizon - next) / interval)) : 0;
            // Stop at most one callback ahead. An unused paid minute/hour is not itself a saving.
            return new ToggleSpendWindow(rounds, next, startCost, payments * cost, started && next <= 1f && affordable > 0);
        }
    }
}
