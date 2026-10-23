// ★ v0.3.21: real time with the Wrath Tactics mod - its rules go first, AI Control fills every gap.
//
// Wrath Tactics evaluates its rules every 3 s (its "tick interval"); AI Control decides within half a second of a unit
// going idle, so it filled every gap before Wrath Tactics looked and the player's rules rarely fired. With the setting on,
// a unit Wrath Tactics manages that goes idle is offered to Wrath Tactics first (its own RequestTick): AI Control waits
// until Wrath Tactics has evaluated once since then (its tick counter moved) or a short timeout, then decides itself if
// Wrath Tactics gave no command. Once per idle stretch: a unit that stays idle (AI Control chose to wait) is not offered
// again until it has carried out a command. No fixed hold - a unit is never left idle for a rule that does not fire.
// No game types: unit-tested (tests/CompanionAI.Tests).
namespace CompanionAI_Pathfinder.Tactics
{
    public static class WrathHandoff
    {
        /// <summary>Longest wait for Wrath Tactics to evaluate (seconds): it ticks on the next frame when asked, and
        /// lets a reactive tick through at most every 0.5 s.</summary>
        public const float TimeoutSeconds = 0.6f;

        public enum Step
        {
            /// <summary>Ask Wrath Tactics to evaluate now (first look at this idle stretch).</summary>
            Ask,
            /// <summary>Asked; Wrath Tactics has not evaluated yet - no decision.</summary>
            Wait,
            /// <summary>Wrath Tactics had its turn (or did not answer in time): AI Control decides.</summary>
            Decide
        }

        /// <summary>Per-unit state for one idle stretch.</summary>
        public sealed class State
        {
            public bool Asked;
            public bool Answered;
            public int TickAtAsk;
            public float AskedAt;
        }

        /// <summary>What to do for a unit with no command (<paramref name="wrathTick"/>: Wrath Tactics' tick counter now).</summary>
        public static Step Next(State s, float now, int wrathTick)
        {
            if (s.Answered) return Step.Decide;
            if (!s.Asked) return Step.Ask;
            if (wrathTick != s.TickAtAsk || now - s.AskedAt >= TimeoutSeconds)
            {
                s.Answered = true;
                return Step.Decide;
            }
            return Step.Wait;
        }

        /// <summary>The ask was sent at <paramref name="now"/>, when Wrath Tactics' counter read <paramref name="wrathTick"/>.</summary>
        public static void MarkAsked(State s, float now, int wrathTick)
        {
            s.Asked = true;
            s.Answered = false;
            s.TickAtAsk = wrathTick;
            s.AskedAt = now;
        }

        /// <summary>The unit has a command again: the next idle stretch starts a new handoff.</summary>
        public static void Reset(State s)
        {
            s.Asked = false;
            s.Answered = false;
        }
    }
}
