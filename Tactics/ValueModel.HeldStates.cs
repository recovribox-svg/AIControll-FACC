using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// ★ v0.3.24 (Psionics): what spending a held state costs (TacticalEngine.HeldStates.cs). <paramref name="roundsAfter"/>
        /// = the fight's expected rounds left after the gained effect ends. Getting the state back takes
        /// <paramref name="restoreActions"/> standard actions, each worth one round of the unit (<paramref name="unitRound"/>);
        /// that is charged in full once at least a round is left, in proportion below. What ends with the state and can be
        /// read (<paramref name="readablePerRound"/>) is charged for every round left. Nothing when no round is left.
        /// </summary>
        public static float SpentStateCost(float roundsAfter, float restoreActions, float readablePerRound, float unitRound)
        {
            if (float.IsNaN(roundsAfter) || roundsAfter <= 0f) return 0f;
            return Math.Min(1f, roundsAfter) * Math.Max(0f, restoreActions) * Math.Max(0f, unitRound) +
                   Math.Max(0f, readablePerRound) * roundsAfter;
        }
    }
}
