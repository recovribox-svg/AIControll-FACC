// ★ v0.3.30: inspected scalar boundaries used only when projecting a not-yet-applied form.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>Scalar rules from RuleCalculateAttacksCount / RuleCalculateWeaponStats. No actor state.</summary>
    internal static class ShifterRules
    {
        internal static int Iteratives(int baseAttack, bool companion)
        {
            int count = Math.Max(0, baseAttack / 5 - (baseAttack % 5 == 0 ? 1 : 0));
            return companion ? count : Math.Min(3, count);
        }

        // Native negative ability modifiers apply in full even to a secondary attack; positive halves/1.5 truncate.
        internal static int AttributeDamage(int bonus, float multiplier) => (int)(bonus * (bonus < 0 ? 1f : multiplier));
    }
}
