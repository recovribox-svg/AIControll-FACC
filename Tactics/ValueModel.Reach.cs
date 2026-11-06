// ★ v0.3.28 test 7 (author-approved, fight 7 of the test-6 log): how much of a melee enemy's round reaches a spot.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// ★ v0.3.28 test 7: the share of a melee enemy's round (its Threat: a full attack) that lands on someone standing at
        /// a spot. In its reach from where it stands: all of it. Within a 5-foot step of that reach: all of it, unless an
        /// area it stands in holds it this round (<paramref name="heldChance"/>: Grease's fall, a web), and then one attack
        /// once it is up again (<paramref name="singleShare"/>: its single attack against its full attack, as the game's
        /// attack rule gives them). Farther, it must walk first: one attack, and only if the area does not hold it. The
        /// Ferocious Schir swung its bardiche from inside Camellia's Grease at Camellia and Seelah on its edge; a step out of
        /// its reach was worth nothing to the engine, which counted the full attack at any spot it could walk to.
        /// </summary>
        public static float StrikeShare(bool inReach, bool inStep, float heldChance, float singleShare)
        {
            if (inReach) return 1f;
            float held = float.IsNaN(heldChance) ? 0f : Clamp(heldChance, 0f, 1f);
            float single = float.IsNaN(singleShare) ? 1f : Clamp(singleShare, 0f, 1f);
            if (inStep) return (1f - held) + held * single;
            return (1f - held) * single;
        }
    }
}
