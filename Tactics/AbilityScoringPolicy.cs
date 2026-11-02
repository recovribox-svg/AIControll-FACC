namespace CompanionAI_Pathfinder.Tactics
{
    public static class AbilityScoringPolicy
    {
        // A missing action slot alone is insufficient: movement, held charges and spell-forbidding preparation
        // still need the future option's value. Only irrelevant unavailable-slot work can be skipped early.
        public static bool NeedsScore(bool slotAvailable, bool futureOffence, bool heldTouch, bool spellOpportunity) =>
            slotAvailable || futureOffence || heldTouch || spellOpportunity;

        // ★ v0.3.25 (test1 logs, performance): an offence kept only for later (no slot now, no held charge, no spell a
        // toggle would forbid) is read only by real time's "can it attack from here once the cooldown ends?": skipped in
        // turn-based (every reader there checks the action is affordable), and in real time once that is answered.
        public static bool SkipUnaffordableOffence(bool slotAvailable, bool heldTouch, bool spellOpportunity, bool realTime,
                                                   bool attackFromHereKnown) =>
            !slotAvailable && !heldTouch && !spellOpportunity && (!realTime || attackFromHereKnown);
    }
}
