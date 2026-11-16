// ★ G1 (docs/TACTICAL_AI.md §170): the pure rules of a grapple that a weapon hit starts (Grabber and its kin).
// The game code behind them (Tactics/CombatMath.Grapple.cs reads it):
//   - a suitable hit runs RuleCombatManeuver Grapple; on success ContextActionGrapple pairs the two units, and
//     UnitPartGrappleInitiator.Init gives the holder CantMove, CantUseStandardActions and no attacks of opportunity;
//   - UnitAttack.TryStartNextAttack starts no further attack of a Standard command once CantUseStandardActions is set,
//     so the rest of a full attack is lost;
//   - each new round the holder rolls the grapple again (ShifterGrabInitiatorBuff): success pins the victim
//     (UnitPartGrappleTarget.TrySetPinned: CantUseStandardActions) and queues a free damage-only natural attack; failure
//     releases the pair;
//   - the victim tries to break free once a round by itself (UnitGrappleController), without an action.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// An attack routine in the game's order, cut where a hit starts a grapple. <see cref="Next"/> gives the chance the
        /// routine reaches the next attack (that attack's weight) and records the chance it starts the grapple.
        /// </summary>
        public struct GrappleRun
        {
            /// <summary>Chance the routine reaches the next attack.</summary>
            public float Reach;
            /// <summary>Chance a hit so far has started a grapple (the rest of the routine is then lost).</summary>
            public float Starts;
            /// <summary>Attacks the routine is expected to make.</summary>
            public float Attacks;

            public static GrappleRun Begin() => new GrappleRun { Reach = 1f };

            /// <param name="hit">The attack's chance to hit.</param>
            /// <param name="triggers">A hit with this attack's weapon runs the grapple (the trigger's weapon filter).</param>
            /// <param name="success">The grapple maneuver's chance to succeed against the target.</param>
            /// <returns>The attack's weight: the chance the routine reaches it. Its own damage is dealt in full: the trigger
            /// runs after the hit.</returns>
            public float Next(float hit, bool triggers, float success)
            {
                float weight = Reach;
                Attacks += weight;
                if (triggers)
                {
                    float grapple = Clamp(hit, 0f, 1f) * Clamp(success, 0f, 1f);
                    Starts += weight * grapple;
                    Reach = weight * (1f - grapple);
                }
                return weight;
            }
        }

        /// <summary>The chance a held grapple lasts another round: the holder's roll succeeds and the victim stays held.</summary>
        public static float GrapplePersists(float maintain, float breakFree) =>
            Clamp(maintain, 0f, 1f) * (1f - Clamp(breakFree, 0f, 1f));

        /// <summary>
        /// The rounds after the first that the grapple is expected to hold within <paramref name="horizon"/> rounds (the
        /// victim's first turn is held without a pin; each later one is pinned while the grapple lasts): sum of
        /// persist^t for t = 1 .. horizon - 1, the last one in part.
        /// </summary>
        public static float GrappleHeldRounds(float persist, float horizon)
        {
            float s = Clamp(persist, 0f, 1f);
            float remaining = Math.Min(horizon, 8f) - 1f;
            float chance = 1f, total = 0f;
            while (remaining > 0f)
            {
                chance *= s;
                total += chance * Math.Min(1f, remaining);
                remaining -= 1f;
            }
            return total;
        }

        /// <summary>
        /// What a grapple is worth once it starts, in damage-equivalent points, over <paramref name="horizon"/> rounds
        /// (the control horizon, at most 3). The victim: held (<paramref name="heldShare"/>, the soft-control share) on
        /// its first turn, pinned (<paramref name="pinnedShare"/>, hard control) on each later turn the grapple lasts.
        /// The holder: each of those rounds it gives up its standard actions and movement (its worth per round, at the
        /// hard share, as an ally caught by our own control costs) and gets the free damage-only natural attack
        /// (<paramref name="freeDamage"/>). Negative when holding costs the holder more than the victim loses.
        /// </summary>
        public static float GrappleHoldValue(float persist, float horizon, float victimWorth, float holderWorth,
                                             float freeDamage, float heldShare, float pinnedShare)
            => GrappleHoldValue(persist, horizon, victimWorth, holderWorth, freeDamage, heldShare, pinnedShare, false);

        /// <summary>
        /// ★ G2: the same, for a holder that can let go as a free action (its form's Release Grapple): it keeps a hold only
        /// while each pinned round pays (<see cref="GrappleRoundNet"/>), so a hold that would cost it leaves only the
        /// victim's first, held turn. The engine lets go by the same test (<see cref="GrappleKeepValue"/>).
        /// </summary>
        public static float GrappleHoldValue(float persist, float horizon, float victimWorth, float holderWorth,
                                             float freeDamage, float heldShare, float pinnedShare, bool canRelease)
        {
            if (horizon <= 0f) return 0f;
            float held = GrappleHeldRounds(persist, horizon);
            float first = Math.Max(0f, victimWorth) * Clamp(heldShare, 0f, 1f) * Math.Min(1f, horizon);
            float later = held * GrappleRoundNet(victimWorth, holderWorth, freeDamage, pinnedShare);
            return first + (canRelease ? Math.Max(0f, later) : later);
        }

        /// <summary>
        /// ★ G2: what one more pinned round of a hold is worth: the victim's lost turn (hard control share) and the free
        /// natural attack, less the holder's own lost turn (its standard actions and movement, at the same share).
        /// </summary>
        public static float GrappleRoundNet(float victimWorth, float holderWorth, float freeDamage, float pinnedShare) =>
            Math.Max(0f, victimWorth) * Clamp(pinnedShare, 0f, 1f) + Math.Max(0f, freeDamage) -
            Math.Max(0f, holderWorth) * Clamp(pinnedShare, 0f, 1f);

        /// <summary>
        /// ★ G2: what keeping a hold the holder has now is worth over <paramref name="horizon"/> rounds, against letting go
        /// this turn (a free action that gives the holder this turn's standard actions and movement back). This round: the
        /// victim stays held or pinned (<paramref name="nowShare"/>) and the holder gives up its turn; each later round the
        /// hold lasts (<see cref="GrappleHeldRounds"/>) is worth <see cref="GrappleRoundNet"/>, and only while that pays,
        /// since the holder can let go then too. Negative: let go now.
        /// </summary>
        public static float GrappleKeepValue(float persist, float horizon, float victimWorth, float holderWorth, float freeDamage,
                                             float nowShare, float pinnedShare)
        {
            if (horizon <= 0f) return 0f;
            float now = (Math.Max(0f, victimWorth) * Clamp(nowShare, 0f, 1f) - Math.Max(0f, holderWorth) * Clamp(pinnedShare, 0f, 1f)) *
                        Math.Min(1f, horizon);
            float later = GrappleHeldRounds(persist, horizon) * GrappleRoundNet(victimWorth, holderWorth, freeDamage, pinnedShare);
            return now + Math.Max(0f, later);
        }

        /// <summary>d20 + <paramref name="bonus"/> against <paramref name="dc"/> for a skill check (no automatic 1 or 20).</summary>
        public static float SkillCheckChance(int bonus, int dc) => Clamp((21f + bonus - dc) / 20f, 0f, 1f);

        /// <summary>
        /// A combat maneuver roll by the game's rule (RuleCombatManeuver.GetResultFromRoll): a natural 20 succeeds, a natural
        /// 1 fails unless <paramref name="alwaysChance"/>, otherwise d20 + CMB against the CMD.
        /// </summary>
        public static float ManeuverRollChance(int cmb, int cmd, bool alwaysChance)
        {
            int successes = 0;
            for (int d20 = 1; d20 <= 20; d20++)
                if (d20 == 20 || ((d20 != 1 || alwaysChance) && d20 + cmb >= cmd)) successes++;
            return successes / 20f;
        }

        /// <summary>
        /// ★ fusion2g1b review: the bit one ManeuverImmunity of <paramref name="type"/> (a CombatManeuver value) adds to a
        /// unit's mask. Type None (0) is immune to every maneuver (ManeuverImmunity.OnEventAboutToTrigger); a value outside
        /// the mask is read the same way, so an immunity never goes unseen.
        /// </summary>
        public static int ManeuverImmunityBit(int type) => type <= 0 || type > 30 ? 1 : 1 << type;

        /// <summary>Whether a mask built by <see cref="ManeuverImmunityBit"/> makes <paramref name="type"/> fail
        /// automatically (RuleCombatManeuver.AutoFailure).</summary>
        public static bool ManeuverBlocked(int mask, int type) =>
            (mask & 1) != 0 || (type > 0 && type <= 30 && (mask & (1 << type)) != 0);
    }
}
