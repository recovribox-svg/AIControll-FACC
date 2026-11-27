namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>
    /// ★ v0.3.70 features3 review: the real-time re-decision rules' pure parts (unit-tested). The game side is
    /// <see cref="RealTimeTactics"/>.
    /// </summary>
    public static class RealTimeReplanRules
    {
        /// <summary>
        /// ★ v0.3.14 (test5): a command that has not started is dropped to decide again once the unit lost a quarter of its
        /// HP or more since it was issued and is now below half (integer quarters and halves, as before).
        /// </summary>
        public static bool DropsAfterDamage(int hpAtIssue, int hpNow, int maxHp)
        {
            int max = maxHp < 1 ? 1 : maxHp;
            return hpAtIssue - hpNow >= max / 4 && hpNow < max / 2;
        }

        /// <summary>
        /// ★ v0.3.70 features3 review: a command that already answers a loss of HP - it heals the unit itself, or walks it
        /// away - is not dropped for it.
        /// </summary>
        public static bool KeepsAfterDamage(bool healsItself, bool walksAway) => healsItself || walksAway;
    }
}
