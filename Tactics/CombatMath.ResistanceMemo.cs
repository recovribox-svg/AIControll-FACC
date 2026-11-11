using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        [ThreadStatic] private static ResistanceScope s_ResistanceScope;
        private static int s_ResistanceVersion;
        internal static int ResistanceReads, ResistanceMemoHits;

        // Scope is one synchronous EvaluateAbility call. No result survives into the next ability evaluation/decision.
        internal static IDisposable MemoizeResistance() => new ResistanceScope();
        private sealed class ResistanceScope : IDisposable
        {
            internal readonly ReferenceReadMemo Values = new ReferenceReadMemo();
            private readonly ResistanceScope previous;
            private bool disposed;
            internal ResistanceScope() { previous = s_ResistanceScope; s_ResistanceScope = this; }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true; s_ResistanceScope = previous;
            }
        }
        internal static void InvalidateResistanceMemo() { unchecked { s_ResistanceVersion++; } }
        internal static bool TryResistanceMemo(UnitEntityData caster, UnitEntityData target, AbilityData ability, out float value)
        {
            value = 0f;
            if (s_ResistanceScope == null || !s_ResistanceScope.Values.TryGet(caster, target, ability, s_ResistanceVersion, out value)) return false;
            ResistanceMemoHits++; return true;
        }
        internal static void StoreResistanceMemo(UnitEntityData caster, UnitEntityData target, AbilityData ability, float value)
            => s_ResistanceScope?.Values.Store(caster, target, ability, s_ResistanceVersion, value);
    }
}
