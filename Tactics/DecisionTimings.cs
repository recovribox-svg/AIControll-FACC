// Test[4]: scalar timing diagnostics only; no actor state, native rules or contexts retained.
using System.Diagnostics;

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
            "included in enclosing stages, not additional time)";
    }
}
