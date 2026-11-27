using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        // With one independent extra die and the lower kept, both dice must pass.
        internal static float WorstOfTwoLostShare(float pass) => pass > 0f ? 1f - Clamp(pass, 0f, 1f) : 0f;

        internal static float RedirectedAttackWorth(float chance, float threatened, bool beneficiaryCertain) =>
            beneficiaryCertain ? Clamp(chance, 0f, 1f) * Math.Max(0f, threatened) : 0f;

        internal static float TeleportApproachWorth(float followUp, float alternative, bool retainsAttack, float advanceShare)
        {
            if (!(followUp > 0f)) return 0f;
            return retainsAttack ? Math.Max(0f, followUp - Math.Max(0f, alternative))
                : AdvanceWorth(followUp, Math.Max(0f, alternative), advanceShare);
        }

        /// <summary>
        /// ★ v0.3.70 F1 (author request): what moving a ranged unit with its move action left, after its main action, to a
        /// safer spot is worth: the lower chance to go down before its next turn times its worth, and the HP-reserve price of
        /// the expected damage kept off (negative when the spot takes more), by the role's tolerance.
        /// </summary>
        internal static float RepositionGain(float dropHere, float dropThere, float worth, float incomingHere, float incomingThere,
                                             float tolerance)
        {
            if (float.IsNaN(dropHere) || float.IsNaN(dropThere) || float.IsNaN(incomingHere) || float.IsNaN(incomingThere) ||
                !(worth >= 0f) || !(tolerance >= 0f)) return 0f;
            return ((dropHere - dropThere) * worth + (Math.Max(0f, incomingHere) - Math.Max(0f, incomingThere)) * HpReservePerPoint) *
                   tolerance;
        }

        /// <summary>
        /// ★ v0.3.70 P1 (author-approved movement): a teleport to an escape walk's spot, before its landing and the cast's own
        /// risks: what reaching the spot is worth (<paramref name="gross"/>) less what the teleport gives up - the walk's own
        /// losses, and for a main-action teleport the best main action this turn (the walk keeps it) - with the easy-fight
        /// restraint on what it spends (<paramref name="reserve"/>).
        /// </summary>
        internal static float TeleportEscapeWorth(float gross, float walkGivenUp, float bestMain, bool spendsMain, float reserve)
        {
            float givenUp = Math.Max(0f, walkGivenUp);
            if (spendsMain) givenUp = Math.Max(givenUp, Math.Max(0f, bestMain));
            return Math.Max(0f, gross - givenUp) * Clamp(reserve, 0f, 1f);
        }

        /// <summary>
        /// ★ v0.3.70 P2 (author-approved): a summon AI Control drives declines an ally's area effect it has accepted (Inspired
        /// Rage's acceptance toggle) when what the effect stops - its spells' measured edge per round - beats what the effect
        /// gives it per round, by at least <see cref="AcceptSwitchMargin"/> points. Unmeasured: nothing is declined.
        /// </summary>
        internal static bool DeclinesGrant(float stoppedPerRound, float givenPerRound) =>
            stoppedPerRound > Math.Max(0f, givenPerRound) + AcceptSwitchMargin;

        /// <summary>
        /// ★ v0.3.70 features review (turn-based log): a spellbook caster whose spells no decision of its own has measured yet
        /// (its first turn of the session has not come) still loses them to a prohibition. Camellia's Inspired Rage counted the
        /// Knight Commander, a Pure Mage, as 0 - "stops its spells" - and the Knight Commander then ended its turn having cast
        /// nothing. Until its own decisions measure it (SpellEdgeMemory), its spells are taken as a round of a unit of its caster
        /// level (<see cref="PerRound"/>) by the share of full casting its highest spell level gives (a ninth-level caster's 5th
        /// circle is a little over half), halved when it also has a weapon attack it uses. Measured edges replace it.
        /// </summary>
        internal static float UnmeasuredSpellEdge(int casterLevel, int maxSpellLevel, bool usesWeapon)
        {
            if (casterLevel <= 0 || maxSpellLevel <= 0) return 0f;
            float full = Math.Max(1f, (casterLevel + 1) / 2f);
            float share = Math.Min(1f, maxSpellLevel / full);
            return PerRound(casterLevel) * share * (usesWeapon ? 0.5f : 1f);
        }

        /// <summary>
        /// ★ v0.3.70 P2: a declined effect is accepted again when what it gives clearly beats what it stops (a quarter more and
        /// the margin), so the switch does not flip back and forth between two close values.
        /// </summary>
        internal static bool ReacceptsGrant(float stoppedPerRound, float givenPerRound) =>
            givenPerRound > Math.Max(0f, stoppedPerRound) * 1.25f + AcceptSwitchMargin;

        internal const float AcceptSwitchMargin = 0.5f;

        /// <summary>
        /// ★ v0.3.70 F2 (author request): what switching to the other weapon set is worth. <paramref name="altNow"/>: the
        /// attack it allows after the switch this turn (turn-based: one attack, the move action is spent; real time: 0, the
        /// attacks go on); then, for <paramref name="rounds"/> more rounds, what its full attack gains over the best the
        /// current weapon can do (<paramref name="currentPerRound"/>, walking included), counted only past a margin: the
        /// other set is estimated without its special abilities and off hand.
        /// </summary>
        internal static float WeaponSetSwitchWorth(float altNow, float altPerRound, float currentPerRound, float rounds)
        {
            float margin = Math.Max(0f, currentPerRound) * 1.2f + 1f;
            float later = altPerRound > margin ? (altPerRound - Math.Max(0f, currentPerRound)) * Math.Max(0f, rounds) : 0f;
            return Math.Max(0f, altNow) + later;
        }

        /// <summary>
        /// ★ v0.3.70 F3 (author request): a summoned creature's worth per round before it has been seen fighting: an ally of
        /// its CR (<see cref="PerRound"/>), half for a summon, by how it measures up to the enemies (its CR against theirs,
        /// within half and one and a half).
        /// </summary>
        internal static float SummonPrior(float creatureCr, float enemyLevel)
        {
            float cr = Math.Max(0.5f, creatureCr), enemy = Math.Max(1f, enemyLevel);
            return PerRound(cr) * SummonFactor * Clamp((cr + 3f) / (enemy + 3f), 0.5f, 1.5f);
        }

        /// <summary>
        /// ★ v0.3.70 F3: the prior and what the kind has dealt per round so far, weighted by the rounds seen (at most 60 %
        /// measured: a creature also holds enemies off, which its damage does not show).
        /// </summary>
        internal static float SummonPerRound(float prior, float measuredPerRound, int roundsSeen)
        {
            if (roundsSeen <= 0 || float.IsNaN(measuredPerRound)) return Math.Max(0f, prior);
            float weight = Math.Min(0.6f, roundsSeen / (roundsSeen + 6f));
            return Math.Max(0f, prior) * (1f - weight) + Math.Max(0f, measuredPerRound) * weight;
        }

        /// <summary>
        /// ★ v0.3.70 F3: the share of <paramref name="newBodies"/> new creatures that have an enemy to fight: about two bodies
        /// an enemy, less the party's summons already fighting; at least a quarter.
        /// </summary>
        internal static float SummonBodyShare(float newBodies, int fightingSummons, int enemies)
        {
            if (!(newBodies > 0f)) return 0f;
            float room = 2f * Math.Max(1, enemies) - Math.Max(0, fightingSummons);
            return Clamp(room / newBodies, 0.25f, 1f);
        }

        /// <summary>
        /// ★ v0.3.23 (build5 review): the share of a new summon's worth left while <paramref name="ownSummons"/> creatures
        /// the same caster summoned still fight: 1 / (1 + n) - a second creature is worth half, a fourth a quarter. An AI
        /// policy, not a game rule: each further creature has fewer enemies left to itself, and a day's summons are not
        /// spent at once on a fight already covered. A negative count is treated as none.
        /// </summary>
        public static float SummonCrowdShare(int ownSummons) => 1f / (1f + Math.Max(0, ownSummons));
    }
}
