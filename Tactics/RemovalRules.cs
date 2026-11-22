using System;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>Recipient and loss rules shared by the dispel/removal appraisal in both combat modes.</summary>
    public static class RemovalRules
    {
        /// <summary>A clicked point is a unit recipient only through a target selector (or a fused payload's delivery).</summary>
        public static bool CanDispelUnit(bool pointOnly, bool selectsUnits, bool fusion) => !pointOnly || selectsUnits || fusion;

        /// <summary>Lost helpful output within the horizon. Suppression that outlasts the buff has no removal cost.</summary>
        public static float HelpfulLoss(float share, float outputPerRound, float remainingRounds, float suppressedRounds, float horizon)
        {
            if (!(share > 0f) || !(outputPerRound > 0f) || !(remainingRounds > 0f) || !(horizon > 0f)) return 0f;
            float active = SuppressionRules.ActiveRounds(remainingRounds, Math.Max(0f, suppressedRounds), horizon);
            return Math.Min(1f, share) * outputPerRound * active;
        }
    }
}
