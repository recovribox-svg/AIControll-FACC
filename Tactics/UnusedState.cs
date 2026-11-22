namespace CompanionAI_Pathfinder.Tactics
{
    internal enum UnusedReason : byte
    {
        NotScored, Unavailable, CombatPolicy, ActionUnavailable, ScoreNotNeeded, Blocklist,
        SummonPolicy, Unread, CoupDelivery, FusionWithheld, PureMage, ConsumablePolicy, NoTarget,
        NoApproach, ItemOnSummon, FriendlyFire, NoGain, ItemBar, SlotCopy,
        /// <summary>A candidate was formed in this decision and lost to another option, or to a later selection bar.</summary>
        Scored
    }

    /// <summary>
    /// ★ consumables review: what the [Unused] witness keeps for one option (pure, unit-tested). The last decision's verdict
    /// wins, but within one decision an option that formed a candidate on some target keeps "scored": the exclusions of
    /// its other targets (one out of range, another a summon) do not explain why it was not chosen.
    /// </summary>
    internal struct UnusedState
    {
        public float Best;
        public int Issued;
        public UnusedReason Reason;
        private int scoredIn;

        public void Record(UnusedReason reason, float value, int decision)
        {
            if (value > Best) Best = value;
            if (Reason == UnusedReason.Scored && scoredIn == decision) return;
            Reason = reason;
        }

        public void Score(float value, int decision)
        {
            if (value > Best) Best = value;
            if (!(value > 0f)) return;
            Reason = UnusedReason.Scored;
            scoredIn = decision;
        }
    }
}
