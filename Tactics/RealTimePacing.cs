// ★ v0.3.20 (audit PERF-001): how soon a real-time unit that just decided to wait decides again.
//
// Real time decides every half second (the "Real-Time Decision Interval" setting). While a unit's standard action is on
// its cooldown (six seconds after an attack or a spell), nearly every one of those decisions builds the whole picture -
// every enemy, every ability on every target - to conclude "wait": in the four real-time fights of the 0.3.20-test4 log,
// 92 of 125, 56 of 90, 100 of 132 and 74 of 97 decisions chose nothing, about three quarters of the AI's time in
// combat (6.1 s of 36 s in fight 1). A unit that waited with its standard action on cooldown now waits quietly until the
// cooldown ends, at most <see cref="MaxQuietSeconds"/>, and decides at once when something that could change its
// choice happens: it loses HP, its engagement changes (an enemy walks up to an archer), it stands in a new harmful area,
// an enemy joins or leaves the fight, or an ally goes down (a swift heal may be worth it now). No game types: unit-tested (tests/CompanionAI.Tests).
namespace CompanionAI_Pathfinder.Tactics
{
    public static class RealTimePacing
    {
        /// <summary>Longest quiet wait: the unit still looks again at least this often.</summary>
        public const float MaxQuietSeconds = 2f;

        /// <summary>
        /// Seconds the unit may wait without deciding after a decision that chose nothing, or 0 to decide again at the
        /// normal interval. Only while its standard action is on cooldown (<paramref name="standardCooldownLeft"/>
        /// seconds left): with it available, an option may become worth taking at any moment.
        /// </summary>
        public static float QuietSeconds(bool hasStandardAction, float standardCooldownLeft, float decisionInterval)
        {
            if (hasStandardAction || standardCooldownLeft <= 0f) return 0f;
            float quiet = standardCooldownLeft < MaxQuietSeconds ? standardCooldownLeft : MaxQuietSeconds;
            return quiet > decisionInterval ? quiet : 0f;
        }

        /// <summary>What a quiet wait watches; any change ends it.</summary>
        public struct Snapshot
        {
            public int Hp;
            public int EngagedBy;
            public int Enemies;
            public int Areas;
            /// <summary>Party members still up.</summary>
            public int Allies;
        }

        /// <summary>
        /// Whether a quiet wait ends now: its time is up, or the unit lost HP, was engaged or disengaged, stands in more
        /// harmful areas than before, the number of enemies changed, or an ally went down.
        /// </summary>
        public static bool ShouldWake(double now, double until, Snapshot then, Snapshot current) =>
            now >= until || current.Hp < then.Hp || current.EngagedBy != then.EngagedBy || current.Areas > then.Areas ||
            current.Enemies != then.Enemies || current.Allies < then.Allies;
    }
}
