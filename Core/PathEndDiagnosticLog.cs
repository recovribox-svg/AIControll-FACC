// ★ v0.3.52 outcome2: diagnostic snapshots only; never cache an eligibility/path result or retain actors.
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Core
{
    internal sealed class PathEndDiagnosticLog
    {
        internal const int MaxUnits = 128, MaxQueriesPerUnit = 16, MaxReportsPerQuery = 5;

        internal struct Snapshot
        {
            internal float EndX, EndY, EndZ, GoalX, GoalY, GoalZ, Radius, Distance, PathLength;
            internal int Reason, Trimmed;
            internal bool NeedSight;

            // These are only diagnostic identities. Every native check still runs, including for a suppressed note.
            internal bool SameQuery(Snapshot other) => Reason == other.Reason && NeedSight == other.NeedSight &&
                GoalX == other.GoalX && GoalY == other.GoalY && GoalZ == other.GoalZ && Radius == other.Radius;

            internal bool Changed(Snapshot other)
            {
                float x = EndX - other.EndX, y = EndY - other.EndY, z = EndZ - other.EndZ;
                return x * x + y * y + z * z >= 0.25f * 0.25f ||
                    Math.Abs(Distance - other.Distance) >= 0.25f || Math.Abs(PathLength - other.PathLength) >= 0.5f ||
                    Trimmed != other.Trimmed;
            }
        }

        private sealed class Note { internal Snapshot Last; internal int Reports; }
        private readonly Dictionary<string, List<Note>> units = new Dictionary<string, List<Note>>(StringComparer.Ordinal);
        private int epoch;
        private bool initialized;

        internal bool Observe(int fight, string unitKey, Snapshot value)
        {
            if (!initialized || epoch != fight)
            { units.Clear(); epoch = fight; initialized = true; }
            if (string.IsNullOrEmpty(unitKey)) return false;
            if (!units.TryGetValue(unitKey, out var notes))
            {
                if (units.Count >= MaxUnits) return false;
                notes = new List<Note>(); units.Add(unitKey, notes);
            }
            foreach (var note in notes)
            {
                if (!value.SameQuery(note.Last)) continue;
                if (note.Reports >= MaxReportsPerQuery || !value.Changed(note.Last)) return false;
                note.Last = value; note.Reports++;
                return true;
            }
            if (notes.Count >= MaxQueriesPerUnit) return false;
            notes.Add(new Note { Last = value, Reports = 1 });
            return true;
        }
    }
}
