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
        /// ★ v0.3.23 (build2 logs): nor a command the game force-finished before it started. Turn-based, an approaching
        /// command whose unit stops walking ends "Success" and acted but never started (the same rule as
        /// <see cref="CastLedger.Performed"/> and the turn controller): Orvath's Color Spray "into Entangle" stopped after
        /// 5.6 m and was struck "did nothing to its target (1/2)" although it was never cast.
        /// </summary>
        public static SettleStep Settle(bool commandPresent, bool commandFinished, bool executed, bool commandStarted,
            bool commandActed, bool evidence)
        {
            if (commandPresent && !commandFinished) return SettleStep.Wait;
            if (commandPresent && !executed && !CastLedger.Performed(commandStarted, commandActed)) return SettleStep.NotCast;
            return evidence ? SettleStep.Worked : SettleStep.Inspect;
        }

        /// <summary>
        /// ★ v0.3.28 test 12: a cast that worked but has not yet reached the enemy it was aimed at, while the game is still
        /// executing it (a cone or a line travels outward after its command ends: AbilityDeliverProjectile's step-based
        /// delivery, the execution's end reported after the last unit), is judged when that execution ends - up to
        /// <paramref name="limitSeconds"/> after it was issued. Judged at the next decision, test 11 struck "centred on X,
        /// reached Seelah (1/2)" for Fan of Flames casts that went on to burn X; two such strikes leave X out of the area.
        /// </summary>
        public static bool AwaitDelivery(bool executed, bool executionEnded, bool watchTarget, bool targetReached,
            double ageSeconds, double limitSeconds) =>
            executed && !executionEnded && watchTarget && !targetReached && ageSeconds <= limitSeconds;

        /// <summary>
        /// v0.3.22 Log2 (PR #14): a damage packet whose own attack roll missed (Holy Water's splash keeps the missed roll) is
        /// not a direct-hit sample for the estimate calibration. Packets without an attack roll (saves, automatic hits) are
        /// samples; the defense observations of every packet are separate and unaffected.
        /// </summary>
        public static bool SkipCalibrationSample(bool hasAttackRoll, bool attackHit) => hasAttackRoll && !attackHit;

        /// <summary>★ v0.3.30 shifter1 review: ranged Spellstrike reuses one physical arrow roll for every ray.
        /// Attack-roll identity cannot distinguish those projectiles from multiple damage actions of one ray.
        /// Withhold the per-projectile calibration sample; real packet-defense observations remain separate.</summary>
        public static bool SkipCorrelatedProjectileSample(bool projectileSpell, bool eldritchArcher, bool physicalRangedRoll) =>
            projectileSpell && eldritchArcher && physicalRangedRoll;
    }
}
