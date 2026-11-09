// ★ v0.3.30 competence: actual nonlethal and save consequences of accepting burn, using the existing HP/day/save policy.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>Inspected TTT composite entry arithmetic: each native integer term becomes float and is floored
        /// separately, then the two integers are summed. Unsupported overflow stays in the caller's fallback.</summary>
        public static int TomeBurnValue(int accepted, int baseScale, int featureAdjustment) =>
            checked((int)Math.Floor((float)checked(accepted * baseScale)) + (int)Math.Floor((float)featureAdjustment * accepted));

        /// <summary>
        /// Nonlethal loss uses real HP and the increased next-turn drop chance. Save changes use the same
        /// weighted-save/incoming-damage/prevention policy as recipient buffs. Skills have no combat valuation here.
        /// ★ v0.3.30 competence2 (PR #20 review): <paramref name="dayPrice"/> is the existing per-rest price of the burn
        /// itself (the daily burn pool, and a consequence that lasts until rest), charged whatever the consequence is.
        /// It was the HP-loss day cost; a part whose burn takes no HP (Psychokineticist: Will until rest) must not get
        /// burn for free when no danger is seen. Overflow is separate and must be credited once by the caller.
        /// </summary>
        public static float KineticBurnConsequenceCost(float nonlethalLoss, float weightedSaveChange, float hp,
            float incoming, float worth, float horizon, float dayPrice)
        {
            float loss = Math.Max(0f, nonlethalLoss);
            if (loss >= hp) return worth * 2f;
            float cost = Math.Max(0f, dayPrice);
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
