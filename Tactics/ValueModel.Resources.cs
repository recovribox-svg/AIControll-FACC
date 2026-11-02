using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// The share of a daily resource's damage and control that an easy fight takes off its value: levelled spell
        /// slots since v0.3.17, ★ v0.3.23 (bombs task) an Alchemist's daily bombs too. 1 - clamp(0.55 + 0.45 x difficulty,
        /// 0.55, 1): 41 % at the easiest fights (difficulty 0.1), 22.5 % at 0.5, nothing once the enemies' threat reaches the
        /// party's power (difficulty 1). An AI policy, not a game rule: a day's slots and bombs are kept for the fights that
        /// need them, so a limited option must clearly beat a reusable one (the weapon) when little is at stake. The value
        /// of the option's other parts (a buff, a heal) is not discounted. No count of uses: a later cast or throw in the
        /// same fight is judged the same way.
        /// </summary>
        public static float LimitedUseDiscount(float difficulty)
        {
            if (float.IsNaN(difficulty)) return 0f;
            return 1f - Clamp(MinLimitedUseShare + (1f - MinLimitedUseShare) * difficulty, MinLimitedUseShare, 1f);
        }

        /// <summary>
        /// ★ v0.3.25 (test1 logs): a used-up healing item's worth - its HP-restoring part (<paramref name="restoring"/>) with
        /// the easy-fight restraint of <see cref="LimitedUseDiscount"/>, what else it does (<paramref name="other"/>: the
        /// lower chance the ally drops, a revive) in full.
        /// </summary>
        public static float SpentHealValue(float restoring, float other, float difficulty) =>
            Math.Max(0f, restoring) * (1f - LimitedUseDiscount(difficulty)) + Math.Max(0f, other);

        /// <summary>What an easy fight leaves of a limited option's damage and control at the least.</summary>
        public const float MinLimitedUseShare = 0.55f;
    }
}
