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

        /// <summary>
        /// ★ v0.3.25 (Psionics): the share of the unit's round one feature that works only while a held state is held is
        /// worth (Psionic Weapon's extra dice, Psionic Shot's), and how many such features count at most. The features
        /// themselves cannot be read as numbers; an AI policy, about 1d6 to 2d6 extra damage a round at level 6.
        /// </summary>
        public const float DependentShare = 0.15f;
        public const int DependentCap = 3;

        /// <summary>
        /// ★ v0.3.25 (Psionics): what getting a held state back is worth (TacticalEngine.HeldStates.cs, RegainValue): for
        /// the <paramref name="roundsAfter"/> rounds of the fight left once it is back, what comes back with it and can be
        /// read (<paramref name="readablePerRound"/>) plus <see cref="DependentShare"/> of the unit's round
        /// (<paramref name="unitRound"/>) per feature that works only while it is held (at most <see cref="DependentCap"/>);
        /// and the best toggle it lets the unit start again (<paramref name="togglePerRound"/>) for the rounds left after
        /// that toggle's own command (<paramref name="toggleRounds"/>). Nothing when no round is left.
        /// </summary>
        public static float HeldStateWorth(float roundsAfter, float readablePerRound, int dependents, float unitRound,
                                           float togglePerRound, float toggleRounds)
        {
            if (float.IsNaN(roundsAfter) || roundsAfter <= 0f) return 0f;
            float perRound = Math.Max(0f, readablePerRound) +
                             DependentShare * Math.Min(Math.Max(0, dependents), DependentCap) * Math.Max(0f, unitRound);
            float afterToggle = Math.Max(0f, roundsAfter - Math.Max(0f, toggleRounds));
            return perRound * roundsAfter + Math.Max(0f, togglePerRound) * afterToggle;
        }
    }
}
