// ★ v0.3.28 test 6: critical hits, one rule for weapon attacks and for abilities with an attack roll.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// ★ v0.3.28 test 6: what a landed hit is worth with critical hits, as a factor on its damage. A threat is a roll in
        /// the weapon's critical range that also hits (RuleAttackRoll: IsCriticalRoll needs the hit), so its chance is the
        /// range's share of the d20, never more than the hit chance; the confirmation roll is made against about the same
        /// AC (TargetCriticalAC), so it passes at about the hit chance. Given a hit, the chance it is a critical is then
        /// min(threat, hit), and a critical deals <paramref name="multiplier"/> times the damage (the weapon's, or x2 for
        /// a magus or eldritch archer spell: ContextActionDealDamage.GetDamageInfo). 1 when nothing is known.
        /// </summary>
        public static float CritFactor(float threatChance, float hitChance, float multiplier)
        {
            if (float.IsNaN(threatChance) || float.IsNaN(hitChance) || float.IsNaN(multiplier)) return 1f;
            float critGivenHit = Clamp(Math.Min(threatChance, hitChance), 0f, 1f);
            return 1f + critGivenHit * Math.Max(0f, multiplier - 1f);
        }
    }
}
