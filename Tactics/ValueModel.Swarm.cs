// ★ v0.3.28 test 15 (author-approved movement change): how much of a swarm's round reaches a spot (CombatMath.Swarms.cs).
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// ★ v0.3.28 test 15: of a swarm's round, the share expected on someone outside its space but within its walk this
        /// round. The round comes once every 6 s of game time (turn-based: at the turn start after that time), the swarm's
        /// own turn at some other point of the round, so it lands after the swarm has walked onto the unit about half the time.
        /// </summary>
        public const float SwarmWalkShare = 0.5f;

        /// <summary>
        /// ★ v0.3.28 test 15: the share of a swarm's round (its Threat) that lands on someone at a spot. Struck where it stands
        /// (<paramref name="struck"/>: inside the swarm's space while one of its targets is in its inner circle): all of it,
        /// for everyone struck - the round is not shared out among them. Within its walk (<paramref name="reachable"/>):
        /// <see cref="SwarmWalkShare"/>, which the caller shares among those it could walk onto. Else nothing.
        /// </summary>
        public static float SwarmShare(bool struck, bool reachable) => struck ? 1f : reachable ? SwarmWalkShare : 0f;

        /// <summary>
        /// ★ v0.3.28 test 15: how hard a swarm's round is on one party member against the party's mean (its saves, its
        /// immunities, its scores: <paramref name="victimHp"/> and <paramref name="meanHp"/> are the round's worth in hit
        /// points), at most 3; 1 when the mean is unknown.
        /// </summary>
        public static float SwarmVictimFactor(float victimHp, float meanHp)
        {
            if (!(meanHp > 0f) || float.IsInfinity(meanHp) || float.IsNaN(victimHp)) return 1f;
            return Clamp(Math.Max(0f, victimHp) / meanHp, 0f, 3f);
        }
    }
}
