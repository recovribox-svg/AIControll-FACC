using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>Weapon damage recovered by the native before/after concealment passes. Keep mirror images and
        /// other factors already in the estimate; an unchanged weather/blindness miss chance earns nothing.</summary>
        public static float ConcealmentRemovalGain(float currentDamage, float beforePass, float afterPass) =>
            currentDamage > 0f && beforePass > 0f && afterPass > beforePass
                ? Math.Max(0f, currentDamage * (afterPass / beforePass - 1f)) : 0f;
    }
}
