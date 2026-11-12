// Pure scheduling policy for bounded, selected-plan path checks. No actor/path state is cached.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class SelectedApproachFilter
    {
        public const int MaxChecks = 3;

        /// <summary>
        /// Check only the main plan selection would use. A rejected plan exposes the next already-scored option;
        /// no whole-decision retry or all-target path scan is needed. If the work budget is exhausted, unchecked
        /// walking plans are withheld for this invocation, while in-reach and quick options remain available.
        /// ★ v0.3.29 test 1: and the unchecked plans <paramref name="keepUnchecked"/> names (a plain move: nothing is spent
        /// before it, and its walk is checked when it is issued). Lann's three walk-then-shoots were each refused as longer
        /// than one move; the budget then withheld his advances too, and he ended his turn with every action unused.
        /// </summary>
        public static List<T> Apply<T>(IEnumerable<T> ordered, Func<IReadOnlyList<T>, T> selectMain,
            Func<T, bool> needsCheck, Func<T, bool> canPrepare, Action<T> rejected = null,
            Action<int> budgetLimited = null, Func<T, bool> keepUnchecked = null,
            Func<IReadOnlyList<T>, bool> hasUsefulAction = null, Func<IEnumerable<T>> idleFallback = null) where T : class
        {
            var usable = ordered.ToList();
            var checkedPlans = new HashSet<T>();
            int checks = 0;
            bool refused = false;
            List<T> Finish()
            {
                // A plan needing a step can suppress advances before its path is tested. Recover only after an
                // actual refusal leaves no useful action. The factory supplies plain moves, checked at issue;
                // never restart selection/path preparation or spend a fourth selected-plan check here.
                if (refused && idleFallback != null && hasUsefulAction != null && !hasUsefulAction(usable))
                {
                    var fallback = idleFallback();
                    // Remaining sub-threshold plans must not hide a useful fallback from first-main selection.
                    if (fallback != null) usable.InsertRange(0, fallback);
                }
                return usable;
            }
            while (true)
            {
                var main = selectMain(usable);
                if (main == null || !needsCheck(main) || checkedPlans.Contains(main)) return Finish();
                if (checks >= MaxChecks)
                {
                    int omitted = usable.RemoveAll(c => needsCheck(c) && !checkedPlans.Contains(c) &&
                                                        !(keepUnchecked?.Invoke(c) ?? false));
                    budgetLimited?.Invoke(omitted);
                    return Finish();
                }
                checks++;
                if (canPrepare(main)) checkedPlans.Add(main);
                else
                {
                    usable.Remove(main);
                    refused = true;
                    rejected?.Invoke(main);
                }
            }
        }
    }
}
