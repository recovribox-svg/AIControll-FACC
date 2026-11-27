using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    // ★ v0.3.70 features review (RT log, performance): one decision prices a cone or line from many aims and casting spots
    // (straight, turned past allies, along enemies, walk-first spots, crowded free sides), and each aim asked the game again
    // for the same ability's damage on the same unit and for the same effect chance. A Shadow Demon's ten Dragon's Breaths
    // made 900 damage estimates each, 150 ms a breath, and an 844 ms decision with a garbage collection in it; Cone of Cold,
    // Burning Hands and Fan of Flames led the other slow rows. Within one frame neither number depends on the aim: the
    // caster has not moved (Point-Blank Shot reads the caster's position), and every state preview that changes them (prone,
    // infusions, Magus modes, forms) clears the attack cache, which clears this memo. Not used inside a Hit Dice pool
    // outcome or a Magus roll scope, where the same pair reads differently on purpose; nor for an effect chance with a
    // supplied resistance pass. Identity is by reference (two fusions share one holder blueprint).
    public static partial class CombatMath
    {
        private readonly struct EstimateKey : IEquatable<EstimateKey>
        {
            private readonly object m_A, m_B, m_C, m_D;
            private readonly int m_Flag;

            internal EstimateKey(object a, object b, object c, object d, int flag)
            {
                m_A = a; m_B = b; m_C = c; m_D = d; m_Flag = flag;
            }

            public bool Equals(EstimateKey other) =>
                ReferenceEquals(m_A, other.m_A) && ReferenceEquals(m_B, other.m_B) && ReferenceEquals(m_C, other.m_C) &&
                ReferenceEquals(m_D, other.m_D) && m_Flag == other.m_Flag;

            public override bool Equals(object obj) => obj is EstimateKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = RuntimeHelpers.GetHashCode(m_A);
                    h = h * 31 + (m_B == null ? 0 : RuntimeHelpers.GetHashCode(m_B));
                    h = h * 31 + (m_C == null ? 0 : RuntimeHelpers.GetHashCode(m_C));
                    h = h * 31 + (m_D == null ? 0 : RuntimeHelpers.GetHashCode(m_D));
                    return h * 31 + m_Flag;
                }
            }
        }

        private const int MaxEstimateMemo = 4096;
        private static readonly Dictionary<EstimateKey, float> DamageMemo = new Dictionary<EstimateKey, float>();
        private static readonly Dictionary<EstimateKey, float> ChanceMemo = new Dictionary<EstimateKey, float>();
        private static int s_EstimateMemoFrame = -1;

        /// <summary>Damage estimates and effect chances answered from the memo (slow-decision diagnostics only).</summary>
        internal static int DamageMemoHits, ChanceMemoHits;

        /// <summary>The memo may be read and written now: no scope that changes the same pair's answer, and this frame's.</summary>
        private static bool EstimateMemoUsable()
        {
            if (s_MagusRoll != null || AbilityProfile.PoolOutcomeActive) return false;
            int frame = Time.frameCount;
            if (frame != s_EstimateMemoFrame)
            {
                ClearEstimateMemo();
                s_EstimateMemoFrame = frame;
            }
            return true;
        }

        private static void ClearEstimateMemo()
        {
            DamageMemo.Clear();
            ChanceMemo.Clear();
        }

        private static void Remember(Dictionary<EstimateKey, float> memo, EstimateKey key, float value)
        {
            if (memo.Count >= MaxEstimateMemo) memo.Clear();
            memo[key] = value;
        }
    }
}
