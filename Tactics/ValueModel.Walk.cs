using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>Real time: the seconds of one round (the game's action cooldowns and the engine's horizon).</summary>
        public const float RealTimeRoundSeconds = 6f;

        /// <summary>
        /// ★ v0.3.23 (build3 logs): real time, an action that walks first comes that much later. Walking during the action's
        /// own cooldown is free; the rest of the walk delays it. The same action later is worth it at the rate it is made:
        /// round / (round + delay) - nothing lost with no delay, half after a whole round of walking. Seelah walked 13.6 m
        /// (about 4.4 s) past the enemy front line to a Cultist Archer and was shot and clawed on the way.
        /// </summary>
        public static float RealTimeWalkFactor(float walkSeconds, float cooldownLeftSeconds)
        {
            float delay = Math.Max(0f, walkSeconds - Math.Max(0f, cooldownLeftSeconds));
            if (float.IsNaN(delay) || float.IsInfinity(delay)) return 1f;
            return RealTimeRoundSeconds / (RealTimeRoundSeconds + delay);
        }
    }
}
