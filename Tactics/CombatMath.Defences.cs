// ★ v0.3.28 test 3 (the author's question on lightly armoured, high-Dexterity fighters): the defences an attack roll
// meets after it beats the AC - concealment and mirror images - read the way the game applies them.
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Parts;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        private static readonly Dictionary<(UnitEntityData, UnitEntityData), (float Lands, float Pass)> s_Lands =
            new Dictionary<(UnitEntityData, UnitEntityData), (float Lands, float Pass)>();
        private static int s_LandsFrame = -1;

        /// <summary>
        /// The share of <paramref name="attacker"/>'s hits that reach <paramref name="target"/> itself
        /// (ValueModel.LandsPastDefences): the concealment check of an attack roll (RuleAttackRoll ->
        /// RuleConcealmentCheck: UnitPartConcealment.Calculate(attacker, target, attack: true), 20 % partial, 50 % total -
        /// Blur, Displacement, invisibility, fog and darkness, and what strips it: Faerie Fire, See Invisibility), then the
        /// mirror images (UnitPartMirrorImage.TryAbsorbHit: the target is picked 1 in images + 1). The hit chances were the
        /// bare d20 against AC: a blurred or displaced unit was as easy to hit as without. Once per pair and frame.
        /// </summary>
        public static float LandsPastDefences(UnitEntityData attacker, UnitEntityData target) =>
            LandsPastDefences(attacker, target, out _);

        /// <param name="concealmentPass">The share past the concealment alone (what seeing through it gives back).</param>
        public static float LandsPastDefences(UnitEntityData attacker, UnitEntityData target, out float concealmentPass)
        {
            concealmentPass = 1f;
            if (attacker == null || target == null) return 1f;
            int frame = Time.frameCount;
            if (frame != s_LandsFrame)
            {
                s_Lands.Clear();
                s_LandsFrame = frame;
            }
            if (s_Lands.TryGetValue((attacker, target), out var known))
            {
                concealmentPass = known.Pass;
                return known.Lands;
            }
            int miss = 0, images = 0;
            try
            {
                var concealment = UnitPartConcealment.Calculate(attacker, target, attack: true);
                miss = concealment == Concealment.Total ? 50 : concealment == Concealment.Partial ? 20 : 0;
            }
            catch { }
            try { images = target.Get<UnitPartMirrorImage>()?.MechanicsImages?.Count ?? 0; } catch { }
            float lands = ValueModel.LandsPastDefences(miss, images);
            concealmentPass = ValueModel.LandsPastDefences(miss, 0);
            s_Lands[(attacker, target)] = (lands, concealmentPass);
            return lands;
        }
    }
}
