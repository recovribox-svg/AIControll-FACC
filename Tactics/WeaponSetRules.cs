namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>
    /// ★ v0.3.70 features6 review: the weapon-set switch's pure parts (unit-tested). The game side is
    /// <c>TacticalEngine.AddWeaponSetCandidate</c>.
    /// </summary>
    public static class WeaponSetRules
    {
        /// <summary>
        /// What the current weapon is worth against the other set: its options this decision when it has any, else the
        /// price it had earlier this turn (<paramref name="earlier"/>; turn-based, once the standard action is spent no weapon
        /// option is built). False with neither: the switch is not offered (it was compared with 0).
        /// </summary>
        public static bool CurrentPrice(bool priced, float best, float? earlier, out float price)
        {
            if (priced) { price = best; return true; }
            if (earlier.HasValue) { price = earlier.Value; return true; }
            price = 0f;
            return false;
        }

        /// <summary>An earlier price still stands: this round (turn-based, the same turn), or the last one in real time.</summary>
        public static bool Fresh(int pricedRound, int round, bool realTime) =>
            round >= pricedRound && round - pricedRound <= (realTime ? 1 : 0);
    }
}
