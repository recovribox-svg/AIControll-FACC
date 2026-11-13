// ★ v0.3.30: decision-local, read-only payment of a short proposed action sequence. No game types or live balances.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class JointCostBudget
    {
        public const int MaxCharges = 64;
        public const int MaxPreparedSlots = 256;

        public readonly struct Charge
        {
            public readonly object Pool;
            public readonly int Bucket, Available, Amount;
            public readonly bool Consume;
            public readonly IReadOnlyList<object> Slots;

            private Charge(object pool, int bucket, int available, int amount, bool consume, IReadOnlyList<object> slots)
            { Pool = pool; Bucket = bucket; Available = available; Amount = amount; Consume = consume; Slots = slots; }

            public static Charge FromPool(object pool, int bucket, int available, int amount, bool consume = true) =>
                new Charge(pool, bucket, available, amount, consume, null);
            public static Charge FromPrepared(IReadOnlyList<object> eligibleInSpendOrder, int amount) =>
                new Charge(null, 0, 0, amount, true, eligibleInSpendOrder);
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly object pool;
            private readonly int bucket;
            internal Key(object pool, int bucket) { this.pool = pool; this.bucket = bucket; }
            public bool Equals(Key other) => ReferenceEquals(pool, other.pool) && bucket == other.bucket;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => unchecked(RuntimeHelpers.GetHashCode(pool) * 397 ^ bucket);
        }
        private readonly struct Balance
        {
            internal readonly int Initial, Left;
            internal Balance(int initial, int left) { Initial = initial; Left = left; }
        }
        private sealed class Identity : IEqualityComparer<object>
        {
            internal static readonly Identity Instance = new Identity();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
        private readonly Dictionary<Key, Balance> balances = new Dictionary<Key, Balance>();
        private readonly HashSet<object> usedSlots = new HashSet<object>(Identity.Instance);

        public bool CanPay(IReadOnlyList<Charge> first, IReadOnlyList<Charge> second) => Check(first, second, null, false);
        public bool CanPay(IReadOnlyList<Charge> first, IReadOnlyList<Charge> second, IReadOnlyList<Charge> third) => Check(first, second, third, true);

        private bool Check(IReadOnlyList<Charge> first, IReadOnlyList<Charge> second, IReadOnlyList<Charge> third, bool includeThird)
        {
            balances.Clear(); usedSlots.Clear();
            int work = 0;
            return Apply(first, ref work) && Apply(second, ref work) && (!includeThird || Apply(third, ref work));
        }

        private bool Apply(IReadOnlyList<Charge> plan, ref int work)
        {
            if (plan == null || plan.Count > MaxCharges) return false; // null is unknown, empty is known free.
            foreach (var charge in plan)
            {
                if (++work > MaxCharges || charge.Amount < 0) return false;
                if (charge.Amount == 0) continue;
                if (charge.Slots != null)
                {
                    if (charge.Slots.Count > MaxPreparedSlots || charge.Amount > charge.Slots.Count) return false;
                    int needed = charge.Amount;
                    foreach (var slot in charge.Slots)
                    {
                        if (ReferenceEquals(slot, null)) return false;
                        if (usedSlots.Add(slot) && --needed == 0) break;
                    }
                    if (needed > 0) return false;
                }
                else
                {
                    if (ReferenceEquals(charge.Pool, null) || charge.Available < 0) return false;
                    var key = new Key(charge.Pool, charge.Bucket);
                    if (!balances.TryGetValue(key, out var balance)) balance = new Balance(charge.Available, charge.Available);
                    if (balance.Initial != charge.Available || balance.Left < charge.Amount) return false;
                    balances[key] = new Balance(balance.Initial, balance.Left - (charge.Consume ? charge.Amount : 0));
                }
            }
            return true;
        }
    }
}
