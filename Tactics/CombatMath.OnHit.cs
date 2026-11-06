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

        /// <summary>
        /// The ability-score points one hit with these components' attack triggers takes directly (AddInitiatorAttackWithWeaponTrigger
        /// on hit, ContextActionDealDamage of type AbilityDamage: a Shadow's touch, 1d6 Strength); both branches of a Conditional
        /// count (the larger). ★ v0.3.28 test 14: the engine reads hits with CombatMath.OnHitAffliction (poisons, saves, control
        /// too); this blueprint-only reading stays for tools/ProfileAudit.
        /// </summary>
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
