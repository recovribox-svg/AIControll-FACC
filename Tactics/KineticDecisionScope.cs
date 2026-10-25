// ★ v0.3.22: lifetime of read-only native query/geometry caches is exactly one decision, including nested previews.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands.Base;
namespace CompanionAI_Pathfinder.Tactics
{
    internal sealed class KineticDecisionScope : IDisposable
    {
        [ThreadStatic] internal static KineticDecisionScope Current;
        private readonly KineticDecisionScope previous;
        private readonly Stopwatch watch = Stopwatch.StartNew();
        internal KineticSearchBudget Budget;
        internal readonly Dictionary<(UnitEntityData, UnitCommand.CommandType, bool), (bool Success, float Seconds)> ActionSeconds =
            new Dictionary<(UnitEntityData, UnitCommand.CommandType, bool), (bool, float)>();
        internal readonly KineticAreaPreview.Cache Areas = new KineticAreaPreview.Cache();
        internal int ActionQueries;
        internal double ElapsedMilliseconds => watch.Elapsed.TotalMilliseconds;
        internal KineticDecisionScope() { previous = Current; Current = this; }
        internal static void Check() { Current?.Budget?.Check(); }
        public void Dispose()
        {
            Current = previous;
            Areas.Dispose();
        }
    }
}
