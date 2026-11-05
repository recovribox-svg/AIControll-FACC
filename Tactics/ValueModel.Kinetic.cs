// ★ v0.3.28 (codex review): the existing kineticist preparation and Overflow policies, shared with pure tests.
// ★ v0.3.28 test 6: Overflow credited in expected damage over the unit's best other action, the arrows' price, and the
// in-combat refill's worth - every input read from the game at the time (burn limits, arrows, Overflow, costs), so the
// same rules hold from a level-3 Kinetic Sharpshooter to a level-20 one.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>The current quiver cannot pay one use of the costliest kinetic configuration the unit may use.</summary>
        public static bool NeedsKineticQuiver(int ranks, int abilityBurn) => abilityBurn > 0 && ranks < abilityBurn;

        /// <summary>Preparation burn would leave less than half the real (not temporary) maximum hit points.</summary>
        public static bool PreparationBurnBelowHpFloor(float realHp, int maxHp, float burnHpLoss) =>
            realHp - burnHpLoss < 0.5f * Math.Max(1, maxHp);

        /// <summary>
        /// ★ v0.3.28 test 6: what the Elemental Overflow a burn raises is worth over the rest of the fight. Overflow adds
        /// <paramref name="gain"/> damage to each own blast that hits (AddKineticistElementalOverflow), so a later blast
        /// gains <paramref name="gain"/> x <paramref name="landChance"/> (its hit chance). That raises the unit's later
        /// rounds only by what it lifts the best blast above the best other action: <paramref name="blastShortfall"/> is how
        /// far the best free blast falls short of it (0 when a blast is already best). One later blast a round, the rounds
        /// after this one (<paramref name="horizon"/> - 1). Null shortfall: no free blast was offered to compare, the old
        /// one-blast-a-round credit of the raw gain. An unknown (NaN) chance or shortfall credits nothing.
        /// </summary>
        public static float KineticOverflowCredit(int gain, float landChance, float? blastShortfall, float horizon)
        {
            if (gain <= 0 || !(horizon > 1f)) return 0f;
            float rounds = horizon - 1f;
            if (blastShortfall == null) return gain * rounds;
            if (float.IsNaN(blastShortfall.Value) || float.IsNaN(landChance)) return 0f;
            float perBlast = gain * Clamp(landChance, 0f, 1f);
            return Math.Max(0f, perBlast - Math.Max(0f, blastShortfall.Value)) * rounds;
        }

        /// <summary>
        /// ★ v0.3.28 test 6: the price of <paramref name="arrows"/> a kinetic ability takes from the Kinetic Sharpshooter's
        /// quiver. One burn makes <paramref name="arrowsPerQuiver"/> arrows (class level + Constitution bonus, read from
        /// the game), so while another quiver can be made an arrow costs that share of one burn's price; once it cannot
        /// (no burn left for Create Quiver, or the player blocked it) each arrow stands for a burn the unit cannot take, at
        /// a whole burn's price.
        /// </summary>
        public static float ArrowPrice(int arrows, float burnPrice, int arrowsPerQuiver, bool canRemake)
        {
            if (arrows <= 0 || !(burnPrice > 0f)) return 0f;
            return arrows * burnPrice / (canRemake ? Math.Max(1, arrowsPerQuiver) : 1);
        }

        /// <summary>
        /// ★ v0.3.28 test 6: Create Quiver in combat takes the turn's standard action, so it is worth what the arrows add
        /// over the rounds they last, less its own burn: <paramref name="gainPerRound"/> (the best blast the arrows pay
        /// for, over the best action without them) for the rounds left after this one or as many blasts as the arrows
        /// cover, whichever is fewer. The action it takes is the comparison itself: the candidate competes with that
        /// best action.
        /// </summary>
        public static float QuiverRefillWorth(float gainPerRound, int arrowsAfter, int burnPerBlast, float horizon, float quiverBurnPrice)
        {
            if (!(gainPerRound > 0f) || arrowsAfter <= 0) return 0f;
            float shots = arrowsAfter / (float)Math.Max(1, burnPerBlast);
            float uses = Math.Min(Math.Max(0f, horizon - 1f), shots);
            return gainPerRound * uses - Math.Max(0f, quiverBurnPrice);
        }
    }
}
