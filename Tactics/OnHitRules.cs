using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>
    /// coverage7 follow-ups: how many hits of a weapon routine fire an on-hit rider (AbstractWeaponTrigger), from each attack
    /// line's hit chance and whether its weapon suits the rider's filters. Pure arithmetic, shared by both modes.
    /// </summary>
    public static class OnHitRules
    {
        /// <param name="suits">Whether each attack line's weapon passes the rider's weapon filters.</param>
        /// <param name="hits">Each attack line's chance to hit, in the same order.</param>
        /// <param name="firstAttackOnly">OnlyOnFirstAttack: the routine's first attack only.</param>
        /// <param name="firstHitOnly">OnlyOnFirstHit: the native trigger also retains HadHit between routines.</param>
        /// <param name="reached">An optional additional reach factor; production lines already include their individual reach.</param>
        /// <param name="hadHit">Native state before this routine. Unknown stays true: a lower bound, since a missed or
        /// unsuitable opening attack returns before the native HadHit reset.</param>
        public static float FiringHits(IList<bool> suits, IList<float> hits, bool firstAttackOnly, bool firstHitOnly, float reached,
                                       bool hadHit = true)
        {
            if (suits == null || hits == null || !(reached > 0f)) return 0f;
            int count = Math.Min(suits.Count, hits.Count);
            if (firstAttackOnly || firstHitOnly && hadHit) count = Math.Min(count, 1);
            float sum = 0f, none = 1f;
            for (int i = 0; i < count; i++)
            {
                if (!suits[i]) continue;
                if (float.IsNaN(hits[i]) || float.IsInfinity(hits[i])) return 0f;
                float p = hits[i] < 0f ? 0f : hits[i] > 1f ? 1f : hits[i];
                sum += p;
                none *= 1f - p;
            }
            return (firstHitOnly ? 1f - none : sum) * Math.Min(1f, reached);
        }
    }
}
