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

        /// <summary>
        /// ★ v0.3.70 features3 review (author-approved): the action provokes from an enemy threatening the unit - a
        /// defensive-casting check, the ability's own provoking actions, or a ranged attack roll (a shot, a ray, a bomb).
        /// </summary>
        public static bool Provokes(bool castCheck, bool casterProvokes, bool rangedRoll) => castCheck || casterProvokes || rangedRoll;

        /// <summary>
        /// ★ v0.3.70 features3 review (author-approved): a provoking action that waits to start where the unit stands is decided
        /// again when an enemy now threatens the unit that did not when it was chosen, and can make an attack of opportunity
        /// on it. The Drow Magus (real-time fight 7) threw a Stun Bomb chosen with nothing near her; two Venomhodags reached
        /// her while it waited, and the throw's two attacks of opportunity killed her.
        /// </summary>
        public static bool ReplanForProvoke(bool provokes, bool started, bool threatenedNow, bool countedWhenChosen, bool canAttack) =>
            provokes && !started && threatenedNow && !countedWhenChosen && canAttack;
    }
}
