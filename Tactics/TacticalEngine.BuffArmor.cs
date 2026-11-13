// ★ v0.3.50 review2: price a modelled integer AC change from the already-read native roll, in the correct direction.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>
        /// Incoming protection adds AC to the current quote; retaining a held buff prevents the AC loss when it is
        /// removed. Raw native AB/AC preserve how far a roll lies beyond 5%/95%. No additional actor/rule/target query.
        /// The strongest-enemy and stat-link approximations remain; unreadable/fractional changes keep their fallback.
        /// </summary>
        private static bool TryArmorProtectionFromQuote(Defence defence, float armor, bool held, out float protection)
        {
            protection = 0f;
            var quote = defence?.AttackQuote;
            if (quote == null || !(defence.AttackIncoming > 0f) || float.IsInfinity(defence.AttackIncoming) ||
                armor == 0f || float.IsNaN(armor) || float.IsInfinity(armor) || armor != Math.Truncate(armor)) return false;
            double changedAc = held ? (double)quote.FirstArmorClass - armor : (double)quote.FirstArmorClass + armor;
            if (changedAc < int.MinValue || changedAc > int.MaxValue) return false;
            float before = CombatMath.HitProbability(quote.FirstAttackBonus, quote.FirstArmorClass);
            float after = CombatMath.HitProbability(quote.FirstAttackBonus, (int)changedAc);
            protection = ValueModel.ArmorProtectionFromRolls(defence.AttackIncoming, before, after, held);
            return true;
        }
    }
}
