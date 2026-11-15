using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        // F2: overlapping control is an envelope, not two independent lost turns. Scratch contains scalars only.
        internal sealed class ControlEnvelope
        {
            internal const int Capacity = 32;
            private readonly float[] shares = new float[Capacity], durations = new float[Capacity];
            private int count;
            internal void Clear() { count = 0; }
            internal bool Add(float share, float rounds)
            {
                if (float.IsNaN(share) || float.IsInfinity(share) || float.IsNaN(rounds) || float.IsInfinity(rounds)) return false;
                if (share <= 0f || rounds <= 0f) return true;
                if (count == Capacity) return false;
                shares[count] = Clamp(share, 0f, 1f); durations[count++] = rounds;
                return true;
            }
            internal float Rounds()
            {
                float at = 0f, total = 0f;
                for (int step = 0; step < count; step++)
                {
                    float next = float.MaxValue, share = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        if (durations[i] <= at) continue;
                        next = Math.Min(next, durations[i]); share = Math.Max(share, shares[i]);
                    }
                    if (next == float.MaxValue) break;
                    total += (next - at) * share; at = next;
                }
                return total;
            }
        }

        // F3: branch before applying nonlinear HP/kill utility. The root save is shared, not a second delivery roll.
        internal static void FusionValue(float failChance, float deliveryChance, float failedDamage, float passedDamage,
            float hp, float damageWorth, float controlWorth, float controlRoundsOnFailure, bool regenerating,
            bool endsRegeneration, out float damage, out float control)
        {
            float fail = Clamp(failChance, 0f, 1f), delivery = Clamp(deliveryChance, 0f, 1f);
            float failedKill = KillChance(1f, failedDamage, hp), passedKill = KillChance(1f, passedDamage, hp);
            damage = delivery * (fail * DamageValue(failedDamage, hp, damageWorth,
                                     RegeneratingKill(failedKill, regenerating, endsRegeneration)) +
                                (1f - fail) * DamageValue(passedDamage, hp, damageWorth,
                                     RegeneratingKill(passedKill, regenerating, endsRegeneration)));
            // A recipient downed by this outcome cannot also lose the same future actions to its failed-save control.
            control = delivery * fail * (1f - failedKill) * Math.Max(0f, controlWorth) * Math.Max(0f, controlRoundsOnFailure);
        }
    }
}
