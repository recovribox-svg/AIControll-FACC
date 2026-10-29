// Recommendation A (docs/INTEGRATION_0322_FOLLOWUPS.md): the observers' learning gates as pure rules, with no game types
// so they are unit-tested (tests/CompanionAI.Tests/ObserverRulesTests.cs). CastOutcome and DamageObserver read the game
// facts and ask these.
namespace CompanionAI_Pathfinder.Tactics
{
    public static class ObserverRules
    {
        /// <summary>What <c>CastOutcome.Settle</c> does with a pending cast before its touch/charge checks.</summary>
        public enum SettleStep
        {
            /// <summary>The command is still running: judge it later.</summary>
            Wait,
            /// <summary>The command ended before the game cast anything: nothing was tried, nothing is counted.</summary>
            NotCast,
            /// <summary>Something the cast did was seen: it worked.</summary>
            Worked,
            /// <summary>No evidence yet: look at the touch charge and the remaining checks.</summary>
            Inspect
        }

        /// <summary>
        /// v0.3.22 Log2 (PR #14): a never-executed, unacted command is discarded before Evidence is read. Evidence is broad (an
        /// old Grease save on the target sets it), so it cannot turn an order interrupted on its approach into a cast tried
        /// on its target. A cast without a command object (already cleared by the game) is never discarded here.
        /// </summary>
        public static SettleStep Settle(bool commandPresent, bool commandFinished, bool executed, bool commandActed, bool evidence)
        {
            if (commandPresent && !commandFinished) return SettleStep.Wait;
            if (commandPresent && !executed && !commandActed) return SettleStep.NotCast;
            return evidence ? SettleStep.Worked : SettleStep.Inspect;
        }

        /// <summary>
        /// v0.3.22 Log2 (PR #14): a damage packet whose own attack roll missed (Holy Water's splash keeps the missed roll) is
        /// not a direct-hit sample for the estimate calibration. Packets without an attack roll (saves, automatic hits) are
        /// samples; the defense observations of every packet are separate and unaffected.
        /// </summary>
        public static bool SkipCalibrationSample(bool hasAttackRoll, bool attackHit) => hasAttackRoll && !attackHit;
    }
}
