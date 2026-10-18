// ★ v0.3.7: "Progressive" spell-resource policy - the pure tier gate (no game types, unit-tested in tests/).
//
// Among a caster's offensive spellbook spells, only the lowest effective slot level that has a viable option may
// compete in the normal decision; higher levels are held back unless an emergency bypass applies to that cast.
// Everything else (weapons, cantrips, buffs, heals, items...) is never touched. Design: docs/PROGRESSIVE_SPELLS.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>One already-eligible decision option, reduced to what the tier gate needs.</summary>
    public sealed class TierOption
    {
        /// <summary>Unique key of the option (Candidate.Key).</summary>
        public string Key;
        /// <summary>Resource scope: each spellbook is its own ladder. ★ v0.3.12: a unique key (the spellbook blueprint's
        /// GUID), so two books that share a name cannot merge into one ladder.</summary>
        public string Book;
        /// <summary>★ v0.3.12: the book's name, for the log only (falls back to <see cref="Book"/>).</summary>
        public string BookName;
        /// <summary>Effective slot level as the game will spend it (metamagic included); 0 = free.</summary>
        public int Level;
        /// <summary>Offensive spellbook spell (damage / control / debuff), not an item.</summary>
        public bool InScope;
        public float Value;
        /// <summary>The engine's own minimum value for this option's action slot.</summary>
        public float Threshold;
        /// <summary>Chance the effect lands on its primary target (spell resistance × failed save × hit), 0..1.</summary>
        public float SuccessChance = 1f;
        /// <summary>Emergency bypass applies to this cast (it would hit a boss).</summary>
        public bool Bypass;
        /// <summary>For the log.</summary>
        public string Label;
    }

    public sealed class TierDecision
    {
        public readonly HashSet<string> HeldBack = new HashSet<string>();
        /// <summary>Per book: the tier allowed to compete, and which lower tiers did not count as usable.</summary>
        public readonly List<string> Notes = new List<string>();
        public readonly List<string> BypassedLabels = new List<string>();
    }

    public static class SpellTierPolicy
    {
        /// <summary>
        /// Minimum chance that a lower-level spell works before it counts as "usable" and blocks higher levels.
        /// A spell the target will almost surely shrug off (high save, high spell resistance) does not.
        /// </summary>
        public const float MinSuccessChance = 0.25f;

        public static bool IsViable(TierOption o) =>
            o.Value > 0f && o.Value >= o.Threshold && o.SuccessChance >= MinSuccessChance;

        public static TierDecision Gate(IEnumerable<TierOption> options)
        {
            var decision = new TierDecision();
            var gated = options.Where(o => o != null && o.InScope && o.Level > 0).ToList();
            if (gated.Count == 0) return decision;

            foreach (var book in gated.GroupBy(o => o.Book ?? ""))
            {
                var viable = book.Where(IsViable).ToList();
                if (viable.Count == 0) continue;   // nothing in this book is worth a slot now: leave it to normal scoring

                int floor = viable.Min(o => o.Level);
                // ★ v0.3.12: lower tiers that are not viable do not set the floor, but they are never held back either:
                // they still compete in normal scoring (the gate only removes options above the floor).
                var skipped = book.Where(o => o.Level < floor && !IsViable(o))
                                  .Select(o => $"{o.Label} L{o.Level} {Reason(o)}").ToList();

                foreach (var o in book.Where(o => o.Level > floor))
                {
                    if (o.Bypass) { decision.BypassedLabels.Add(o.Label); continue; }
                    decision.HeldBack.Add(o.Key);
                }

                string name = book.Select(o => o.BookName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? book.Key;
                decision.Notes.Add($"{(string.IsNullOrEmpty(name) ? "spells" : name)}: tier L{floor}" +
                                   (skipped.Count > 0 ? $" (lower not viable, still selectable: {string.Join(", ", skipped)})" : ""));
            }
            return decision;
        }

        private static string Reason(TierOption o)
        {
            if (o.SuccessChance < MinSuccessChance) return $"{o.SuccessChance:P0} chance";
            return $"value {o.Value:F1}";
        }
    }
}
