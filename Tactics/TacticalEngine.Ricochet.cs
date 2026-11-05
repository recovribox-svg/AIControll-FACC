// ★ v0.3.25 (author request: Chain Arrows in automatic infusion selection, docs/TACTICAL_AI.md §84).
//
//   Chain Arrows (ChainArrowsInfusionBuff: only its burn and parameters) works through the blasts themselves: each
//   blast ability carries an AbilityDeliverRicochet (layer 1) whose before-condition is "the caster has
//   ChainArrowsInfusionBuff or DLC3_KineticRicochetBuff". After the blast hits, Deliver jumps from the current target to
//   the nearest living enemy of the caster within Radius (10 ft) of it, not hit yet, up to TargetsCount times (a caster
//   property: 1 + level/2); every jump reuses the original attack roll (RuleAttackRoll.CopyWithNewTarget with the first
//   target) and executes the blast graph at the ricochet's own layer (plain blasts select their half-damage branch).
//   Blinking units are skipped half the time. It was withheld for lack of "a
//   read-only recipient query"; the selection is pure geometry over the game's own unit list, so it is forecast here
//   without launching anything. Each jump's recipient takes the blast's estimated damage at the first hit's chance,
//   adjusted for its own spell resistance.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static readonly System.Reflection.FieldInfo RicochetBeforeField =
            HarmonyLib.AccessTools.Field(typeof(AbilityDeliverRicochet), "m_BeforeCondition");
        private static readonly System.Reflection.FieldInfo RicochetTargetTypeField =
            HarmonyLib.AccessTools.Field(typeof(AbilityDeliverRicochet), "m_TargetType");
        private static readonly System.Reflection.FieldInfo RicochetTargetConditionField =
            HarmonyLib.AccessTools.Field(typeof(AbilityDeliverRicochet), "m_TargetCondition");
        private static readonly System.Reflection.FieldInfo RicochetAgainField =
            HarmonyLib.AccessTools.Field(typeof(AbilityDeliverRicochet), "m_UsedTargetsAgain");

        /// <summary>A blast's ricochet that <see cref="Buff"/> switches on, with the other buffs that would also switch it on.</summary>
        private sealed class RicochetInfo
        {
            internal AbilityDeliverRicochet Component;
            internal BlueprintBuff[] Others;
        }

        // Immutable blueprint facts only (blast, buff) → its ricochet or null.
        private static readonly Dictionary<KeyValuePair<BlueprintAbility, BlueprintBuff>, RicochetInfo> s_Ricochets =
            new Dictionary<KeyValuePair<BlueprintAbility, BlueprintBuff>, RicochetInfo>();

        /// <summary>Components a delivery-only infusion buff may have besides its effect on the blasts (Chain Arrows).</summary>
        internal static bool IsDeliveryOnlyInfusion(BlueprintBuff buff) =>
            buff != null && buff.ComponentsArray.All(c =>
                c is Kingmaker.UnitLogic.Class.Kineticist.AddKineticistBurnModifier ||
                c is Kingmaker.UnitLogic.Mechanics.Components.ContextCalculateAbilityParamsBasedOnClass ||
                c is Kingmaker.Designers.Mechanics.Facts.RecalculateOnStatChange ||
                c is Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig);

        /// <summary>
        /// The ricochet on <paramref name="blast"/> that <paramref name="buff"/> switches on - an Or of "the caster has
        /// buff X" conditions naming it, an enemy target type, no target condition, no repeated targets - or null.
        /// </summary>
        private static RicochetInfo RicochetOf(BlueprintAbility blast, BlueprintBuff buff)
        {
            if (blast == null || buff == null) return null;
            var key = new KeyValuePair<BlueprintAbility, BlueprintBuff>(blast, buff);
            if (s_Ricochets.TryGetValue(key, out var known)) return known;
            RicochetInfo found = null;
            try
            {
                if (RicochetBeforeField != null && RicochetTargetTypeField != null && RicochetTargetConditionField != null &&
                    RicochetAgainField != null)
                    foreach (var r in blast.GetComponents<AbilityDeliverRicochet>())
                    {
                        if (r.GetType() != typeof(AbilityDeliverRicochet) || r.TargetDead || (bool)RicochetAgainField.GetValue(r)) continue;
                        if ((TargetType)RicochetTargetTypeField.GetValue(r) != TargetType.Enemy) continue;
                        if (((ConditionsChecker)RicochetTargetConditionField.GetValue(r))?.HasConditions == true) continue;
                        var before = (ConditionsChecker)RicochetBeforeField.GetValue(r);
                        var conditions = before?.Conditions;
                        if (conditions == null || conditions.Length == 0) continue;
                        if (conditions.Length > 1 && before.Operation != Operation.Or) continue;
                        if (!conditions.All(c => c is ContextConditionHasBuff h && !h.Not && h.Buff != null)) continue;
                        var buffs = conditions.Cast<ContextConditionHasBuff>().Select(h => h.Buff).ToArray();
                        if (!buffs.Contains(buff)) continue;
                        found = new RicochetInfo { Component = r, Others = buffs.Where(b => b != buff).ToArray() };
                        break;
                    }
            }
            catch (Exception ex) { Main.Verbose($"[Kinetic] ricochet read {blast.name}: {ex.Message}"); }
            s_Ricochets[key] = found;
            return found;
        }

        // The blast's estimate on one jump recipient, once per frame: a scored blast is asked for every target it could
        // start from, and their jumps share recipients.
        private static readonly Dictionary<(AbilityData, UnitEntityData, int), float> s_ChainDamage =
            new Dictionary<(AbilityData, UnitEntityData, int), float>();
        private static int s_ChainDamageFrame = -1;

        private static float ChainDamage(AbilityData blast, AbilityProfile profile, UnitEntityData recipient, int layer)
        {
            if (Time.frameCount != s_ChainDamageFrame) { s_ChainDamage.Clear(); s_ChainDamageFrame = Time.frameCount; }
            // ★ v0.3.28 (codex review): never reuse a primary/other-layer forecast for this native delivery layer.
            var key = (blast, recipient, layer);
            if (!s_ChainDamage.TryGetValue(key, out float damage))
                s_ChainDamage[key] = damage = CombatMath.EstimateAbilityDamageAtDeliveryLayer(blast, profile, recipient, layer);
            return damage;
        }

        /// <summary>
        /// What the jumps of <paramref name="ricochet"/> add to a blast at <paramref name="start"/> landing at
        /// <paramref name="chance"/>: nothing when another buff of the caster already switches the ricochet on.
        /// </summary>
        private static float RicochetValue(TacticalContext ctx, AbilityData blast, RicochetInfo ricochet, UnitEntityData start, float chance)
        {
            var unit = ctx.Unit;
            if (start == null || !(chance > 0f) || ricochet.Others.Any(b => unit.Buffs.HasFact(b))) return 0f;
            var context = CombatMath.CreateContext(blast, new TargetWrapper(start));
            if (context == null) return 0f;
            int count = Math.Min(16, Math.Max(0, ricochet.Component.TargetsCount.Calculate(context)));
            if (count == 0) return 0f;
            float radius = ricochet.Component.Radius.Meters;
            var profile = AbilityProfile.Get(blast);
            float startPass = CombatMath.SpellResistancePass(unit, start, blast);
            if (!(startPass > 0f)) return 0f;
            var pool = Game.Instance?.State?.Units?.Where(u => u != null && unit.IsEnemy(u) && !u.Descriptor.State.IsDead).ToList();
            if (pool == null) return 0f;
            var used = new HashSet<UnitEntityData> { start };
            var current = start;
            float value = 0f;
            for (int i = 0; i < count; i++)
            {
                UnitEntityData next = null;
                float nearest = float.MaxValue;
                foreach (var u in pool)
                {
                    if (used.Contains(u)) continue;
                    float d = (u.Position - current.Position).magnitude;
                    if (d <= radius && d < nearest) { nearest = d; next = u; }
                }
                if (next == null) break;
                used.Add(next);
                current = next;
                float land = chance * Math.Min(1f, CombatMath.SpellResistancePass(unit, next, blast) / startPass);
                try { if ((bool)next.State.Features.Blinking && !unit.Descriptor.IsSeeInvisibility) land *= 0.5f; } catch { }
                if (!(land > 0f)) continue;
                float damage = ChainDamage(blast, profile, next, ricochet.Component.Layer);
                bool endsRegeneration = CombatMath.AbilityEndsRegeneration(profile, next);
                value += DamageValue(next, damage * land, land, endsRegeneration) * WakeFactor(next, damage * land);
            }
            return value * RoleWeight(ctx, "damage");
        }
    }
}
