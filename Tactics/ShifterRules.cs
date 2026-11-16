// ★ v0.3.30: inspected scalar boundaries used only when projecting a not-yet-applied form.
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>Scalar rules from RuleCalculateAttacksCount / RuleCalculateWeaponStats. No actor state.</summary>
    internal static class ShifterRules
    {
        internal static int Iteratives(int baseAttack, bool companion)
        {
            int count = Math.Max(0, baseAttack / 5 - (baseAttack % 5 == 0 ? 1 : 0));
            return companion ? count : Math.Min(3, count);
        }

        // Native negative ability modifiers apply in full even to a secondary attack; positive halves/1.5 truncate.
        internal static int AttributeDamage(int bonus, float multiplier) => (int)(bonus * (bonus < 0 ? 1f : multiplier));

        /// <summary>★ shifter-forms (§174): the most rows a form appraisal logs again in one fight after its first.</summary>
        internal const int MaxFormNoteRepeats = 4;

        /// <summary>★ shifter-forms (§174): a form appraisal worth logging again in the same fight - it turned from
        /// paying to not paying (or back), or moved by more than a quarter of its last logged value and at least 3 points.
        /// The first row of a fight hid why a profitable form later vanished from the decision rows (fusion2g1b RT1).</summary>
        internal static bool FormNoteChanged(float logged, float now)
        {
            if (logged > 0.5f != now > 0.5f) return true;
            return Math.Abs(now - logged) > Math.Max(3f, 0.25f * Math.Abs(logged));
        }

        // Diagnostic scalars only. Preserve tracked identities at capacity instead of clearing mid-fight and
        // resetting their repeat allowance. Untracked identities wait until the next fight.
        internal sealed class FormNoteLog
        {
            private sealed class Note { internal float Value; internal int Repeats; }
            private readonly Dictionary<string, Note> notes = new Dictionary<string, Note>(StringComparer.Ordinal);
            private readonly int capacity;
            private int fight = -1;
            internal FormNoteLog(int capacity = 64) { this.capacity = Math.Max(1, capacity); }
            internal bool Observe(int currentFight, string key, float value, out bool first, out float previous)
            {
                first = false; previous = 0f;
                if (fight != currentFight) { notes.Clear(); fight = currentFight; }
                if (string.IsNullOrEmpty(key)) return false;
                if (!notes.TryGetValue(key, out var note))
                {
                    if (notes.Count >= capacity) return false;
                    notes[key] = new Note { Value = value }; first = true; return true;
                }
                previous = note.Value;
                if (note.Repeats >= MaxFormNoteRepeats || !FormNoteChanged(previous, value)) return false;
                note.Value = value; note.Repeats++; return true;
            }
        }
    }
}
