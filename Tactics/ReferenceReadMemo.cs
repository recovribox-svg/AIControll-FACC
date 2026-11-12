using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CompanionAI_Pathfinder.Tactics
{
    // C06R5: exact object identities, including distinct metamagic/item copies of the same blueprint.
    internal sealed class ReferenceReadMemo
    {
        private struct Key : IEquatable<Key>
        {
            internal object Caster, Target, Source;
            public bool Equals(Key other) => ReferenceEquals(Caster, other.Caster) && ReferenceEquals(Target, other.Target) && ReferenceEquals(Source, other.Source);
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Caster == null ? 0 : RuntimeHelpers.GetHashCode(Caster);
                    hash = hash * 397 ^ (Target == null ? 0 : RuntimeHelpers.GetHashCode(Target));
                    return hash * 397 ^ (Source == null ? 0 : RuntimeHelpers.GetHashCode(Source));
                }
            }
        }
        private Dictionary<Key, float> values;
        private int version;
        private void Refresh(int current)
        {
            if (version == current) return;
            values?.Clear(); version = current;
        }
        internal bool TryGet(object caster, object target, object source, int current, out float value)
        {
            Refresh(current); value = 0f;
            return values != null && values.TryGetValue(new Key { Caster = caster, Target = target, Source = source }, out value);
        }
        internal void Store(object caster, object target, object source, int current, float value)
        {
            Refresh(current);
            if (values == null) values = new Dictionary<Key, float>();
            values[new Key { Caster = caster, Target = target, Source = source }] = value;
        }
    }
}
