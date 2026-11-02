// Test[4]: scalar timing diagnostics only; no actor state, native rules or contexts retained.
using System.Diagnostics;
using System.Linq;

namespace CompanionAI_Pathfinder.Tactics
{
    internal enum DecisionStage { Context, Weapons, Abilities, Kinetic, Movement, Preparation, Boosts, Infusions, Finish }

    internal sealed class DecisionTimings
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private long previous;
        private long context, weapons, abilities, kinetic, movement, preparation, boosts, infusions, finish;
        private long activeInfusions;
        private int activeCalls;

        internal DecisionTimings() { previous = started; }
        internal double ElapsedMilliseconds => Milliseconds(Stopwatch.GetTimestamp() - started);
        private static double Milliseconds(long ticks) => ticks * (1000d / Stopwatch.Frequency);

        internal void Mark(DecisionStage stage)
        {
            long now = Stopwatch.GetTimestamp(), ticks = now - previous;
            previous = now;
            switch (stage)
            {
                case DecisionStage.Context: context += ticks; break;
                case DecisionStage.Weapons: weapons += ticks; break;
                case DecisionStage.Abilities: abilities += ticks; break;
                case DecisionStage.Kinetic: kinetic += ticks; break;
                case DecisionStage.Movement: movement += ticks; break;
                case DecisionStage.Preparation: preparation += ticks; break;
                case DecisionStage.Boosts: boosts += ticks; break;
                case DecisionStage.Infusions: infusions += ticks; break;
                case DecisionStage.Finish: finish += ticks; break;
            }
        }

        // ★ v0.3.25 (test1 logs): the three abilities whose scoring cost the most this decision, and how many were scored
        // or skipped as unaffordable. Three slots and two counters: no allocation per ability.
        private readonly string[] costlyNames = new string[3];
        private readonly long[] costlyTicks = new long[3];
        private readonly int[] costlyCandidates = new int[3];
        private int scored, skipped;

        internal void NoteAbility(string name, long startedAt, int candidates)
        {
            long ticks = Stopwatch.GetTimestamp() - startedAt;
            scored++;
            int slot = -1;
            for (int i = 0; i < 3; i++)
                if (costlyNames[i] == null || ticks > costlyTicks[i]) { slot = i; break; }
            if (slot < 0) return;
            for (int i = 2; i > slot; i--)
            {
                costlyNames[i] = costlyNames[i - 1]; costlyTicks[i] = costlyTicks[i - 1]; costlyCandidates[i] = costlyCandidates[i - 1];
            }
            costlyNames[slot] = name; costlyTicks[slot] = ticks; costlyCandidates[slot] = candidates;
        }

        internal void NoteSkippedAbility() => skipped++;

        internal void NoteActiveInfusion(long startedAt)
        {
            activeInfusions += Stopwatch.GetTimestamp() - startedAt;
            activeCalls++;
        }

        // Formatting occurs only for the existing slow-decision diagnostic, never per frame/effect node.
        internal string Describe() => $"stage ms: context/threat={Milliseconds(context):F1}, weapons/modes={Milliseconds(weapons):F1}, " +
            $"abilities={Milliseconds(abilities):F1}, kinetic/recovery={Milliseconds(kinetic):F1}, movement/toggles={Milliseconds(movement):F1}, " +
            $"paths/policy={Milliseconds(preparation):F1}, marks/boosts={Milliseconds(boosts):F1}, infusion-search={Milliseconds(infusions):F1}, " +
            $"finish={Milliseconds(finish):F1}; active-infusion appraisal={Milliseconds(activeInfusions):F1} ({activeCalls} recipient calls, " +
            "included in enclosing stages, not additional time)" +
            $"; abilities scored={scored}, skipped unaffordable={skipped}" +
            (costlyNames[0] == null ? "" : "; costliest: " + string.Join(", ", System.Linq.Enumerable.Range(0, 3)
                .Where(i => costlyNames[i] != null)
                .Select(i => $"{costlyNames[i]} {Milliseconds(costlyTicks[i]):F1} ms ({costlyCandidates[i]} options)")));
    }
}
