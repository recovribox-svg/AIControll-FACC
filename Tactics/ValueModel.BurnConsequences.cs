// ★ v0.3.30 competence: actual nonlethal and save consequences of accepting burn, using the existing HP/day/save policy.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// Nonlethal loss uses real HP, the inherited day cost and increased next-turn drop chance. Save changes use the
        /// same weighted-save/incoming-damage/prevention policy as recipient buffs. Skills have no combat valuation here.
        /// Overflow is separate and must be credited once by the caller, after the consequence has been established.
        /// </summary>
        public static float KineticBurnConsequenceCost(float nonlethalLoss, float weightedSaveChange, float hp,
            float incoming, float worth, float horizon, float dayCostPerHp)
        {
            float loss = Math.Max(0f, nonlethalLoss);
            if (loss >= hp) return worth * 2f;
            float cost = loss * dayCostPerHp;
            if (incoming > 0f)
            {
                cost += (float)(Math.Exp(-(hp - loss) / incoming) - Math.Exp(-hp / incoming)) * worth;
                float keptPerRound = incoming * 0.025f * Math.Min(weightedSaveChange, 20f);
                cost -= PreventionValue(keptPerRound, horizon, hp, incoming, worth);
            }
            return cost;
        }
    }
}
