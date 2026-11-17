// ★ sneak attack refinements (17 November 2026; docs/BURST_AIM_AND_TACTICS_PLAN.md §6, slice D2): a melee attack that makes
// its target flanked, as the second unit engaging it, helps the one other engager too: +2 on its melee attacks and its
// sneak dice for the next round. The attacker's own routine is already priced flanked (CombatMath.ReadRoutine).
using System;
using Kingmaker.EntitySystem.Entities;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>
        /// The partner's round of gain from the flank <paramref name="est"/> creates, as damage-equivalent points without a
        /// kill bonus (its own attacks carry that). Tags the candidate "[flanks]" whenever this attack creates the flank.
        /// </summary>
        private static float FlankPartnerBonus(UnitEntityData enemy, WeaponAttackEstimate est, out string tag)
        {
            tag = "";
            if (est == null || !est.FlankedBySecond || enemy == null) return 0f;
            var partner = est.FlankPartner;
            tag = " [flanks]";
            if (partner == null || est.Attacker == null || partner.IsEnemy(est.Attacker)) return 0f;
            float gain = CombatMath.FlankPartnerGain(partner, enemy);
            if (!(gain > 0f)) return 0f;
            tag = $" [flanks with {partner.CharacterName} +{gain:0.#}]";
            return ValueModel.DamageValue(gain, Math.Max(1, enemy.HPLeft), CombatMath.TargetWorth(enemy), 0f);
        }
    }
}
