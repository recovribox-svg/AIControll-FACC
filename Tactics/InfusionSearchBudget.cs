using System;
using System.Diagnostics;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>Cooperative work bound; a native call already in progress cannot be interrupted.</summary>
    public sealed class InfusionSearchBudget
    {
        public const int MaxAims = 4, MaxRecipients = 8, MaxComparisons = 24, Milliseconds = 8;
        private readonly Func<long> elapsed;
        public int Comparisons { get; private set; }
        public bool Exhausted => Comparisons >= MaxComparisons || elapsed() >= Milliseconds;

        public InfusionSearchBudget(Func<long> elapsedMilliseconds = null)
        {
            var watch = Stopwatch.StartNew();
            elapsed = elapsedMilliseconds ?? (() => watch.ElapsedMilliseconds);
        }

        public bool TryBegin()
        {
            if (Exhausted) return false;
            Comparisons++;
            return true;
        }
    }
}
