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

        /// <summary>★ Fusion F7: expected control rounds when one more control joins with the given chance.</summary>
        internal static float PooledRounds(float without, float with, float chance)
        {
            float p = Clamp(chance, 0f, 1f);
            return (1f - p) * Math.Max(0f, without) + p * Math.Max(0f, with);
        }

        /// <summary>★ Fusion F4: the chance, among delivered casts, of the outcomes that carry an effect.</summary>
        internal static float FusionOutcomeShare(float failChance, bool onFailure, bool onPass)
        {
            float fail = Clamp(failChance, 0f, 1f);
            return (onFailure ? fail : 0f) + (onPass ? 1f - fail : 0f);
        }

        // F3: branch before applying nonlinear HP/kill utility. The root save is shared, not a second delivery roll.
        internal static void FusionValue(float failChance, float deliveryChance, float failedDamage, float passedDamage,
            float hp, float damageWorth, float controlWorth, float controlRoundsOnFailure, bool regenerating,
            bool endsRegeneration, out float damage, out float control) =>
            FusionValue(failChance, deliveryChance, failedDamage, passedDamage, hp, damageWorth, controlWorth,
                controlRoundsOnFailure, 0f, regenerating, endsRegeneration, out damage, out control);

        // ★ Fusion F4: control the native cast applies on a passed root save (Fear's shaken round, Boneshatter's
        // fatigue) or on both outcomes (Frigid Touch's stagger) is weighted by that outcome's chance.
        internal static void FusionValue(float failChance, float deliveryChance, float failedDamage, float passedDamage,
            float hp, float damageWorth, float controlWorth, float controlRoundsOnFailure, float controlRoundsOnPass,
            bool regenerating, bool endsRegeneration, out float damage, out float control) =>
            FusionValue(failChance, deliveryChance, failedDamage, passedDamage, 0f, 0f, hp, damageWorth, controlWorth,
                controlRoundsOnFailure, controlRoundsOnPass, regenerating, endsRegeneration, out damage, out control);

        /// <summary>
        /// ★ Fusion coverage C1 (docs/FUSION_MAIN_TARGET_DOT.md): <paramref name="laterOnFailure"/> and
        /// <paramref name="laterOnPass"/> are the expected damage over time the cast leaves on each outcome (a burning or
        /// acid buff's ticks within the horizon). They count as HP and progress towards a kill, capped at the HP left with
        /// the immediate damage; the chance to kill now, and the control that a kill now cancels, stay with the immediate
        /// damage. Without them the value is unchanged to the last bit.
        /// </summary>
        internal static void FusionValue(float failChance, float deliveryChance, float failedDamage, float passedDamage,
            float laterOnFailure, float laterOnPass, float hp, float damageWorth, float controlWorth,
            float controlRoundsOnFailure, float controlRoundsOnPass, bool regenerating, bool endsRegeneration,
            out float damage, out float control) =>
            FusionValue(failChance, deliveryChance, failedDamage, passedDamage, laterOnFailure, laterOnPass, hp, damageWorth,
                controlWorth, controlRoundsOnFailure, controlRoundsOnPass, regenerating, endsRegeneration, false, false,
                out damage, out control);

        /// <summary>
        /// ★ Fusion coverage C8 (docs/FUSION_COVERAGE_C6_C8.md): an outcome with a kill (Circle of Death's on a failed save) takes the
        /// recipient out for certain: its damage is the HP left and its kill chance 1, whatever its regeneration
        /// (ContextActionKill marks it for death). Without a kill the value is unchanged to the last bit.
        /// </summary>
        internal static void FusionValue(float failChance, float deliveryChance, float failedDamage, float passedDamage,
            float laterOnFailure, float laterOnPass, float hp, float damageWorth, float controlWorth,
            float controlRoundsOnFailure, float controlRoundsOnPass, bool regenerating, bool endsRegeneration,
            bool killOnFailure, bool killOnPass, out float damage, out float control)
        {
            float fail = Clamp(failChance, 0f, 1f), delivery = Clamp(deliveryChance, 0f, 1f);
            if (killOnFailure) failedDamage = Math.Max(failedDamage, Math.Max(1f, hp));
            if (killOnPass) passedDamage = Math.Max(passedDamage, Math.Max(1f, hp));
            float failedKill = KillChance(1f, failedDamage, hp), passedKill = KillChance(1f, passedDamage, hp);
            if (killOnFailure) failedKill = 1f;
            if (killOnPass) passedKill = 1f;
            damage = delivery * (fail * DamageValue(failedDamage + Math.Max(0f, laterOnFailure), hp, damageWorth,
                                     killOnFailure ? 1f : RegeneratingKill(failedKill, regenerating, endsRegeneration)) +
                                (1f - fail) * DamageValue(passedDamage + Math.Max(0f, laterOnPass), hp, damageWorth,
                                     killOnPass ? 1f : RegeneratingKill(passedKill, regenerating, endsRegeneration)));
            // A recipient downed by this outcome cannot also lose the same future actions to its failed-save control.
            control = delivery * fail * (1f - failedKill) * Math.Max(0f, controlWorth) * Math.Max(0f, controlRoundsOnFailure);
            // Added only when present, so a fusion without such an effect keeps its value to the last bit.
            if (controlRoundsOnPass > 0f)
                control += delivery * (1f - fail) * (1f - passedKill) * Math.Max(0f, controlWorth) * controlRoundsOnPass;
        }
    }
}
