// ★ v0.3.30 test 1 (bug report, Profane Ascension): a buff that takes another one's place - moved from the ally holding it (the
// game's UniqueBuff: UnitPartUniqueBuffs.NewBuff removes the caster's old copy when the same buff goes on someone else), or
// put on a recipient whose sibling buff it removes (ContextActionRemoveBuff). Pure rule, unit-tested.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// How much better the new buff or recipient must be than what it replaces: the same margin as a unique mark moved to
        /// another enemy (TacticalEngine.Marks.cs). Values change a little every decision; without it a free-action buff hops
        /// back and forth between two allies, or two buffs swap on one.
        /// </summary>
        public const float BuffSwitchMargin = 1.25f;

        /// <summary>
        /// What a buff that takes another's place gains: its worth (<paramref name="newWorth"/>) less what is lost where it
        /// was or what it removes (<paramref name="heldWorth"/>), and nothing unless it is clearly better
        /// (<see cref="BuffSwitchMargin"/>). Unusable input gains nothing.
        /// </summary>
        public static float BuffSwitchGain(float newWorth, float heldWorth)
        {
            if (float.IsNaN(newWorth) || float.IsNaN(heldWorth) || float.IsInfinity(heldWorth) || !(newWorth > 0f)) return 0f;
            float kept = Math.Max(0f, heldWorth);
            if (newWorth <= kept * BuffSwitchMargin) return 0f;
            return newWorth - kept;
        }
    }
}
