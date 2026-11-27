namespace CompanionAI_Pathfinder.Tactics
{
    internal static class PotionHandoffRules
    {
        internal static bool CanOffer(bool quickSlot, bool canTargetFriends, bool personal) =>
            quickSlot && canTargetFriends && !personal;

        internal static bool CanReceive(bool self, bool enemy, bool summoned) =>
            !self && !enemy && !summoned;
    }
}
