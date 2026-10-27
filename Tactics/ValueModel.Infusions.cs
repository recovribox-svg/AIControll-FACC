using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>Expected future HP damage, bounded by HP remaining after the immediate hit; no repeated kill bonus.</summary>
        public static float InfusionPeriodicDamage(float deliveryChance, float conditionalFutureDamage,
            float immediateDamage, float hp) => Math.Max(0f, Math.Min(1f, deliveryChance)) *
                Math.Min(Math.Max(0f, conditionalFutureDamage), Math.Max(0f, hp - Math.Max(0f, immediateDamage)));

        /// <summary>Marginal gain over the best actual main action; effects pay the blast's delivery discount once.</summary>
        public static float InfusionPlanGain(float currentBlast, float addedEffect, float removedEffect,
            float deliveryFactor, float currentBurnPrice, float newBurnPrice, float bestMain) =>
            currentBlast + (addedEffect - removedEffect) * Math.Max(0f, Math.Min(1f, deliveryFactor)) +
            currentBurnPrice - newBurnPrice - Math.Max(0f, bestMain);
    }
}
