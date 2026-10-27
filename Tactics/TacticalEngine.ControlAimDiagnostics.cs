// ★ Test[5]: carry bounded evidence from existing shared-HD appraisals to the actual chosen action's log.
// No score, game rule, selector, context, range or navigation query is performed by this diagnostic.
using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker.EntitySystem.Entities;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        internal sealed class ControlAimDiagnostics
        {
            // Logging caps only. Every remaining centre is still fully appraised by EvaluateAbility.
            private const int MaxNotes = 12;
            private struct Note
            {
                internal UnitEntityData Centre, First, Second, Third;
                internal float? Value;
                internal string Reason;
                internal int Eligible;
                internal bool Walk;
                internal float ProvokeLoss, Concentration, WalkStop, WalkCost;
            }

            private readonly Note[] notes = new Note[MaxNotes];
            private int count, seen;

            internal void Record(UnitEntityData centre, float? value, string reason,
                IReadOnlyCollection<UnitEntityData> hdEligible = null, bool walk = false,
                float provokeLoss = 0f, float concentration = 1f, float walkStop = 0f, float walkCost = 0f)
            {
                // Diagnostics must never remove an ability if metadata/formatting is unavailable.
                try
                {
                    seen++;
                    if (count == MaxNotes) return;
                    var note = new Note
                    {
                        Centre = centre, Value = value, Reason = reason,
                        Eligible = hdEligible?.Count ?? -1, Walk = walk,
                        ProvokeLoss = provokeLoss, Concentration = concentration, WalkStop = walkStop, WalkCost = walkCost
                    };
                    // The existing recipient set has already been queried and priced. Keep three identities only;
                    // do not create another set, enumerate the whole battlefield, or retain the set/native context.
                    if (hdEligible != null)
                    {
                        int named = 0;
                        foreach (var recipient in hdEligible)
                        {
                            if (named == 0) note.First = recipient;
                            else if (named == 1) note.Second = recipient;
                            else { note.Third = recipient; break; }
                            named++;
                        }
                    }
                    notes[count++] = note;
                }
                catch { }
            }

            internal string Describe()
            {
                var text = new StringBuilder(512);
                text.Append("aim appraisal after scored risks, before selected-path preparation/policy: ");
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) text.Append(" | ");
                    var note = notes[i];
                    AppendUnit(text, note.Centre);
                    text.Append('=');
                    text.Append(note.Value.HasValue ? note.Value.Value.ToString("F1") : "rejected");
                    text.Append(" (").Append(note.Reason);
                    if (note.Eligible >= 0)
                    {
                        text.Append("; HD-eligible enemies=").Append(note.Eligible);
                        if (note.First != null)
                        {
                            text.Append(" ["); AppendUnit(text, note.First);
                            if (note.Second != null) { text.Append(", "); AppendUnit(text, note.Second); }
                            if (note.Third != null) { text.Append(", "); AppendUnit(text, note.Third); }
                            if (note.Eligible > 3) text.Append(", ...");
                            text.Append(']');
                        }
                    }
                    if (note.Walk) text.Append("; approach");
                    if (note.ProvokeLoss > 0f) text.Append("; provoking -").Append(note.ProvokeLoss.ToString("F1"));
                    if (note.Concentration < 1f) text.Append("; concentration x").Append(note.Concentration.ToString("F2"));
                    if (note.WalkStop > 0f || note.WalkCost > 0f)
                        text.Append("; walk x").Append((1f - note.WalkStop).ToString("F2"))
                            .Append(" -").Append(note.WalkCost.ToString("F1"));
                    text.Append(')');
                }
                if (seen > count) text.Append(" | ").Append(seen - count).Append(" more centre notes omitted; scoring unchanged");
                text.Append(". HD eligibility is not a guaranteed beneficiary/save outcome; recipient names capped at three per centre.");
                return text.ToString();
            }

            private static void AppendUnit(StringBuilder text, UnitEntityData unit)
            {
                if (unit == null) { text.Append("unknown"); return; }
                text.Append(unit.CharacterName).Append('[').Append(unit.UniqueId).Append(']');
            }
        }

        private static ControlAimDiagnostics StartControlAimDiagnostics(AbilityProfile profile, bool capture)
        {
            // A cone's geometry set can include allies; this explanation is only for ordinary shared-HD areas.
            try { return capture && !profile.IsProjectileCone && profile.SharedHdBuffs.Count > 0 ? new ControlAimDiagnostics() : null; }
            catch { return null; }
        }

        private static void LogControlAimDiagnostics(TacticalContext ctx, Candidate chosen)
        {
            if (chosen?.ControlAims == null) return;
            try
            {
                Main.Log($"[HD-Aim] {ctx.Unit.CharacterName}: selected {chosen.Label}, decision value {chosen.Value:F1}; " +
                    chosen.ControlAims.Describe());
            }
            catch { } // A diagnostic cannot change which candidate Decide returns.
        }
    }
}
