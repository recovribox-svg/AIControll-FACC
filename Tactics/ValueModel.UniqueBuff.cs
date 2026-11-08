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

        /// <summary>
        /// ★ v0.3.30 competence3 (author's play-test note: Profane Invocation still repeated): rounds a unique buff its caster put
        /// on an ally in this fight stays there before it may move again. The competence2 real-time log moved invocations 5-10
        /// times a fight - every 6-second span, for net gains of 1.2-3.3, and one Constitution buff went Knight Commander ->
        /// Aeiluvetar -> Knight Commander within three spans - as the fight's numbers shifted.
        /// </summary>
        public const int UniqueSettleRounds = 3;

        /// <summary>A move gaining at least this much is taken before the buff has settled (an ally about to drop).</summary>
        public const float UniqueSettleOverride = 5f;

        /// <summary>
        /// A unique buff may leave its holder: the move gains something (<see cref="BuffSwitchGain"/>) and the buff was not put
        /// there in this fight within <see cref="UniqueSettleRounds"/> (<paramref name="roundsSincePlaced"/> null: not by this
        /// caster in this fight), or it gains at least <see cref="UniqueSettleOverride"/>.
        /// </summary>
        public static bool UniqueMoveAllowed(int? roundsSincePlaced, float gain)
        {
            if (float.IsNaN(gain) || !(gain > 0f)) return false;
            if (!roundsSincePlaced.HasValue || roundsSincePlaced.Value >= UniqueSettleRounds) return true;
            return gain >= UniqueSettleOverride;
        }
    }
}
