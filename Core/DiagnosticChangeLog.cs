// ★ v0.3.52: bounded diagnostic state only; never cache a game calculation or retain an actor.
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Core
{
    internal sealed class DiagnosticChangeLog<T>
    {
        private readonly int capacity;
        private readonly Dictionary<string, T> values = new Dictionary<string, T>(StringComparer.Ordinal);
        private int epoch;
        private bool initialized;
        internal int Count => values.Count;

        internal DiagnosticChangeLog(int capacity = 128)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }

        // First observation or change is reportable. At capacity preserve existing identities/transitions:
        // new identities wait for the next fight, rather than clearing and causing repeated message bursts.
        internal bool Observe(int currentEpoch, string key, T value)
        {
            if (!initialized || epoch != currentEpoch)
            { values.Clear(); epoch = currentEpoch; initialized = true; }
            if (string.IsNullOrEmpty(key)) return false;
            if (values.TryGetValue(key, out var old))
            {
                if (EqualityComparer<T>.Default.Equals(old, value)) return false;
            }
            else if (values.Count >= capacity) return false;
            values[key] = value;
            return true;
        }
    }

    internal static class DiagnosticLogPolicy
    {
        internal static bool WriteDecision(bool realTime, bool chosen, bool hasOptions, bool verbose) =>
            !realTime || chosen || hasOptions && verbose;
    }
}
