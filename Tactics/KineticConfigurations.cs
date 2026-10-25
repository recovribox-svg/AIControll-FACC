// ★ v0.3.22: enumerate legal configurations using capacities supplied by the game, without preferences or scores.
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class KineticConfigurations
    {
        /// <summary>Action budget from TurnController.CalculateKineticistPrediction: gather consumes an available
        /// Move first, then Standard. Inputs are the game's current action flags; no level/burn table is involved.</summary>
        public static bool GatherFits(int count, bool move, bool standard, bool swift, bool oneMainAction,
                                      bool castMove, bool castStandard, bool castSwift, bool castFullRound,
                                      out bool usedMove, out bool usedStandard)
        {
            usedMove = usedStandard = false;
            if (count < 0) return false;
            for (int n = 0; n < count; n++)
            {
                if (move) { move = false; usedMove = true; }
                else if (standard) { standard = false; usedStandard = true; }
                else return false;
                if (oneMainAction) move = standard = false;
            }
            return (!castFullRound || (move && standard && !oneMainAction)) &&
                   (!castStandard || standard) && (!castSwift || swift) && (!castMove || move || standard);
        }

        public sealed class Option
        {
            public int Group, Capacity, Weight;
            public bool Current, Locked;
        }

        // ★ v0.3.22: a neighborhood is a UNION of group choices, not their Cartesian product.
        public static List<bool[]> OneGroupNeighbors(IReadOnlyList<Option> options, int limit, out bool complete,
                                                    Func<bool> keepSearching = null)
        {
            var current = new bool[options.Count];
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < options.Count; i++)
            {
                current[i] = options[i].Current;
                if (!groups.TryGetValue(options[i].Group, out var indices)) groups[options[i].Group] = indices = new List<int>();
                indices.Add(i);
            }
            var result = new List<bool[]> { current };
            complete = true;
            foreach (var group in groups.Values)
            {
                var members = new List<Option>();
                foreach (int i in group) members.Add(options[i]);
                var sets = Enumerate(members, limit, out bool localComplete, keepSearching);
                if (!localComplete) { complete = false; result.Clear(); return result; }
                foreach (var set in sets)
                {
                    var alternative = (bool[])current.Clone();
                    bool changed = false;
                    for (int n = 0; n < group.Count; n++)
                    { changed |= alternative[group[n]] != set[n]; alternative[group[n]] = set[n]; }
                    if (!changed) continue;
                    if (result.Count >= limit) { complete = false; result.Clear(); return result; }
                    result.Add(alternative);
                }
            }
            return result;
        }

        public static List<bool[]> Enumerate(IReadOnlyList<Option> options, int limit, out bool complete, Func<bool> keepSearching = null)
        {
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
            var results = new List<bool[]>();
            var chosen = new bool[options.Count];
            var weights = new Dictionary<int, int>();
            bool overflow = false;
            void Visit(int index)
            {
                if (keepSearching != null && !keepSearching()) overflow = true;
                if (overflow) return;
                if (index == options.Count)
                {
                    if (results.Count == limit) { overflow = true; return; }
                    results.Add((bool[])chosen.Clone());
                    return;
                }
                var option = options[index];
                for (int on = 0; on <= 1; on++)
                {
                    bool take = on != 0;
                    if (option.Locked && take != option.Current) continue;
                    int before = weights.TryGetValue(option.Group, out int w) ? w : 0;
                    if (take && (option.Weight < 0 || option.Capacity < option.Weight || before > option.Capacity - option.Weight)) continue;
                    chosen[index] = take;
                    weights[option.Group] = before + (take ? option.Weight : 0);
                    Visit(index + 1);
                    weights[option.Group] = before;
                }
            }
            Visit(0);
            complete = !overflow;
            // A truncated search must not be presented as the best configuration.
            if (overflow) results.Clear();
            return results;
        }
    }
}
