// ★ v0.3.22: cache native read queries only during one synchronous decision, in both combat modes.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Utility;
using Kingmaker.ElementsSystem;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    internal sealed class DecisionReadScope : IDisposable
    {
        // AbilityData implements value equality. Separate copies can still carry different runtime state:
        // key read caches by object identity, never by Blueprint/AbilityData.Equals.
        private readonly struct Identity<T> : IEquatable<Identity<T>> where T : class
        {
            private readonly T value;
            internal Identity(T value) { this.value = value; }
            public bool Equals(Identity<T> other) => ReferenceEquals(value, other.value);
            public override bool Equals(object other) => other is Identity<T> key && Equals(key);
            public override int GetHashCode() => value == null ? 0 : RuntimeHelpers.GetHashCode(value);
        }
        [ThreadStatic] internal static DecisionReadScope Current;
        private readonly DecisionReadScope previous;
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private double last;
        private readonly Dictionary<string, double> stages = new Dictionary<string, double>();
        private readonly Dictionary<(Identity<AbilityData>, (bool, Identity<UnitEntityData>, Vector3, float, bool)), AbilityParams> parameters =
            new Dictionary<(Identity<AbilityData>, (bool, Identity<UnitEntityData>, Vector3, float, bool)), AbilityParams>();
        internal readonly Dictionary<UnitEntityData, List<AbilityData>> Abilities = new Dictionary<UnitEntityData, List<AbilityData>>();
        internal readonly Dictionary<(UnitEntityData, Type), object> Components = new Dictionary<(UnitEntityData, Type), object>();
        internal int ParameterQueries, ParameterHits, ParameterBypasses, RuleBypasses, ContextBuilds, ConditionContextHits, ConditionResultHits, FactScans, DiscoveryBuilds, SkippedAbilities;
        private string slowAbility;
        private double slowAbilityMs;

        internal DecisionReadScope() { previous = Current; Current = this; }

        internal AbilityParams Parameters(AbilityData ability)
        {
            // A live mechanics scope has mutable targets/shared values that mod handlers can inspect. Do not
            // memoize inside it. Normal scorer calls outside such scopes still share native parameters per aim.
            // Never cache execution contexts: their shared values, target scopes and rulebook snapshots are mutable.
            if (ContextData<MechanicsContext.Data>.Current != null) { ParameterQueries++; ParameterBypasses++; return ability.CalculateParams(); }
            // ★ v0.3.22: TriggerEvent replaces the EMPTY root context after every top-level rule. Its object
            // identity is not a state change and used to defeat virtually every cache hit in a live game.
            // Active events can expose mutable data to handlers, so they still bypass rather than share results.
            if (Rulebook.CurrentContext?.CurrentEvent != null) { ParameterQueries++; RuleBypasses++; return ability.CalculateParams(); }
            var aim = AbilityProfile.CurrentAim;
            var key = (new Identity<AbilityData>(ability),
                (aim != null, new Identity<UnitEntityData>(aim?.Unit), aim?.Point ?? default,
                 aim?.Orientation ?? 0f, aim?.IsOrientationSpecified ?? false));
            if (parameters.TryGetValue(key, out var cached)) { ParameterHits++; return cached?.Clone(); }
            ParameterQueries++;
            var result = ability.CalculateParams();
            parameters[key] = result?.Clone();
            return result?.Clone();
        }

        internal void InvalidateState()
        {
            parameters.Clear(); Abilities.Clear(); Components.Clear();
        }

        internal void Mark(string stage)
        {
            double now = watch.Elapsed.TotalMilliseconds;
            stages[stage] = (stages.TryGetValue(stage, out double total) ? total : 0d) + now - last;
            last = now;
        }

        internal void AbilityTime(AbilityData ability, double milliseconds)
        {
            if (milliseconds <= slowAbilityMs) return;
            slowAbilityMs = milliseconds;
            slowAbility = ability?.Name;
        }

        internal string Summary() => "stages " + string.Join(", ", stages.OrderByDescending(s => s.Value).Take(3)
            .Select(s => $"{s.Key}={s.Value:F1}ms")) +
            (slowAbility == null ? "" : $"; top ability {slowAbility}={slowAbilityMs:F1}ms") +
            $"; params {ParameterQueries} native/{ParameterHits} reused/{ParameterBypasses} ambient/{RuleBypasses} active-rule, contexts {ContextBuilds}/{ConditionContextHits} condition reuses/{ConditionResultHits} guard hits, fact scans {FactScans}, discovery {DiscoveryBuilds}, slot skips {SkippedAbilities}";

        public void Dispose() { Current = previous; InvalidateState(); }
    }
}
