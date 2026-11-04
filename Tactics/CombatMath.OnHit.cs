// ★ v0.3.28 (v0.3.27 turn-based log, fight 4): ability damage an attacker's hits deal (a Shadow's Strength-draining touch).
using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        private static readonly FieldInfo DealDamageType = HarmonyLib.AccessTools.Field(typeof(ContextActionDealDamage), "m_Type");

        private static readonly Dictionary<string, KeyValuePair<StatType, float>> s_OnHitAbilityDamage =
            new Dictionary<string, KeyValuePair<StatType, float>>();

        /// <summary>
        /// The ability-score points one hit of <paramref name="attacker"/>'s weapon is expected to take from its target, read
        /// from its facts the way the game runs them: an attack trigger on hit (AddInitiatorAttackWithWeaponTrigger, actions
        /// on the target) whose actions deal ability damage or drain (ContextActionDealDamage, type AbilityDamage) - a
        /// Shadow's touch, 1d6 Strength (Shadow_Feature_StrengthDamage). Its weapon deals no HP damage, so its attacks of
        /// opportunity were priced at nothing: Seelah cast Divine Zap beside one four rounds running and lost 4-6
        /// Strength to three hits. Both branches of a Conditional count (the larger). 0 when none.
        /// </summary>
        public static float OnHitAbilityDamage(UnitEntityData attacker, out StatType stat)
        {
            stat = StatType.Unknown;
            if (attacker == null || DealDamageType == null) return 0f;
            try
            {
                string key = attacker.UniqueId + "|" + attacker.Facts.List.Count;
                if (s_OnHitAbilityDamage.TryGetValue(key, out var known))
                {
                    stat = known.Key;
                    return known.Value;
                }
                float best = 0f;
                foreach (var fact in attacker.Facts.List)
                {
                    var components = fact?.Blueprint?.ComponentsArray;
                    if (components == null) continue;
                    float points = OnHitAbilityDamage(components, out var factStat);
                    if (points > best)
                    {
                        best = points;
                        stat = factStat;
                    }
                }
                if (s_OnHitAbilityDamage.Count > 256) s_OnHitAbilityDamage.Clear();
                s_OnHitAbilityDamage[key] = new KeyValuePair<StatType, float>(stat, best);
                return best;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] on-hit ability damage {attacker.CharacterName}: {ex.Message}");
                return 0f;
            }
        }

        /// <summary>The same reading for one blueprint's components (also used by tools/ProfileAudit on blueprint dumps).</summary>
        public static float OnHitAbilityDamage(IEnumerable<Kingmaker.Blueprints.BlueprintComponent> components, out StatType stat)
        {
            stat = StatType.Unknown;
            float best = 0f;
            if (components == null || DealDamageType == null) return 0f;
            foreach (var component in components)
            {
                if (!(component is AddInitiatorAttackWithWeaponTrigger trigger)) continue;
                if (!trigger.OnlyHit || trigger.OnMiss || trigger.CriticalHit || trigger.ActionsOnInitiator) continue;
                WalkAbilityDamage(trigger.Action, ref best, ref stat);
            }
            return best;
        }

        private static void WalkAbilityDamage(ActionList list, ref float best, ref StatType stat)
        {
            var actions = list?.Actions;
            if (actions == null) return;
            foreach (var action in actions)
            {
                if (action is ContextActionDealDamage deal)
                {
                    object type = DealDamageType.GetValue(deal);
                    if (type == null || type.ToString() != "AbilityDamage") continue;
                    float points = DiceAverage(deal.Value);
                    if (points > best)
                    {
                        best = points;
                        stat = deal.AbilityType;
                    }
                }
                else if (action is Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional conditional)
                {
                    WalkAbilityDamage(conditional.IfTrue, ref best, ref stat);
                    WalkAbilityDamage(conditional.IfFalse, ref best, ref stat);
                }
            }
        }

        /// <summary>The average of a fixed dice value (count dice + bonus; a value read from ranks counts as written).</summary>
        private static float DiceAverage(ContextDiceValue value)
        {
            if (value == null) return 0f;
            int count = value.DiceCountValue?.Value ?? 0;
            int bonus = value.BonusValue?.Value ?? 0;
            int sides = value.DiceType.Sides();
            return Math.Max(0f, count * (sides + 1) / 2f + bonus);
        }
    }
}
