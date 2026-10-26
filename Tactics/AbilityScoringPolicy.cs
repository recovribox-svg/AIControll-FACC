namespace CompanionAI_Pathfinder.Tactics
{
    public static class AbilityScoringPolicy
    {
        // A missing action slot alone is insufficient: movement, held charges and spell-forbidding preparation
        // still need the future option's value. Only irrelevant unavailable-slot work can be skipped early.
        public static bool NeedsScore(bool slotAvailable, bool futureOffence, bool heldTouch, bool spellOpportunity) =>
            slotAvailable || futureOffence || heldTouch || spellOpportunity;
    }
}
