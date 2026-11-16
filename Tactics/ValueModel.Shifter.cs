// ★ shifter-forms (docs/SHIFTER_FORM_DECISIONS.md, docs/TACTICAL_AI.md §174): when a new major form pays, and when
// Shifter's Fury waits for it. Pure rules; the engine side is Tactics/TacticalEngine.Shifter.cs.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>
        /// The rounds in which a new major form's natural attacks replace the current routine. A shift that takes the
        /// turn's Standard or full-round action leaves no attack this round (turn-based: only the move action remains;
        /// real time: the standard cooldown runs), so the form's attacks begin next round. Its defence counts from now.
        /// A swift, free or move-action shift keeps this round's attack.
        /// </summary>
        public static float FormAttackRounds(float usefulRounds, bool takesTurnAttack) =>
            Math.Max(0f, takesTurnAttack ? usefulRounds - 1f : usefulRounds);

        /// <summary>
        /// Shifter's Fury chosen in a human body withholds a new major form for the rest of the fight, while a form cast
        /// first still lets the Fury pick one of the form's weapons from the next turn. So the Fury waits only when the form
        /// beats, by the buff margin, what the turn gives without it: the best immediate main action plus the Fury's own
        /// gain this round. Otherwise the Fury is taken now and the form is left for this fight.
        /// </summary>
        public static bool HoldFuryForForm(float formValue, float bestImmediate, float furyRoundGain) =>
            formValue > 0f && BuffBeatsImmediate(formValue, Math.Max(0f, bestImmediate) + Math.Max(0f, furyRoundGain));
    }
}
