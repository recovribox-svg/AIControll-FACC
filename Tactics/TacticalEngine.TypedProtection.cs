using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Parts;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static float TypedProtectionPerRound(TacticalContext ctx, UnitEntityData ally, Defence defence,
            List<(BuffPart Part, float Amount)> protections)
        {
            float kept = 0f;
            int sampled = 0;
            foreach (var enemy in ctx.Enemies)
            {
                if (CombatMath.IsDisabled(enemy) || !EnemyThreat.TryMainCast(enemy, ally, out var cast, out float expected) ||
                    !(expected > 0f)) continue;
                if (sampled++ >= 8) break;
                try
                {
                    if (!cast.IsAvailable) continue;
                    var profile = AbilityProfile.Get(cast);
                    if (profile == null) continue;
                    float share = 0f;
                    foreach (var protection in protections)
                    {
                        var part = protection.Part;
                        switch (part.Kind)
                        {
                            case BuffPartKind.SpellResistance:
                                share = Math.Max(share, ResistanceProtectionShare(enemy, ally, cast, (int)protection.Amount));
                                break;
                            case BuffPartKind.SavesAgainstAbility:
                                if (cast.Blueprint.Type == part.AgainstAbilityType)
                                    share = Math.Max(share, TypedSaveProtectionShare(ally, cast, profile, part, protection.Amount));
                                break;
                            case BuffPartKind.ConditionImmunity:
                                // A known condition-only payload can be stopped. Mixed damage, maneuver and
                                // secondary payloads retain their existing risk rather than being credited as blocked.
                                if (!ally.State.HasConditionImmunity(part.ImmuneCondition) && OnlyAppliesCondition(profile, part.ImmuneCondition))
                                    share = 1f;
                                break;
                            case BuffPartKind.MovementDescriptorImmunity:
                                if (!(bool)enemy.State.Features.MythicReduceResistances && OnlyBlockedBuff(cast, ally, profile, part.Against))
                                    share = 1f;
                                break;
                        }
                    }
                    kept += expected * share;
                }
                catch
                {
                    // An unread cast never produces protection value.
                }
            }

            if (defence.AttackQuote?.GrappleChance > 0f && protections.Exists(p => p.Part.Kind == BuffPartKind.GrappleImmunity))
                kept += Math.Min(defence.AttackIncoming, AllyPerRound(ally) * defence.AttackQuote.GrappleChance);
            return Math.Min(defence.Incoming, kept);
        }

        private static float ResistanceProtectionShare(UnitEntityData enemy, UnitEntityData ally, AbilityData cast, int resistance)
        {
            var existing = ally.Get<UnitPartSpellResistance>();
            // Read plain max-SR replacement only. The native bypass/penetration read below also checks immunities.
            if (resistance <= 0 || cast.HasMetamagic(Metamagic.Piercing) ||
                existing != null && (existing.AllSRPenalty != 0 || existing.Bonuses.Count != 0)) return 0f;
            var roll = HitChanceCalculator.CalculateSpellResistance(enemy, ally, cast, null, readPenetrationWithoutResistance: true);
            if (!roll.AllowsSR || roll.IsImmune || roll.TargetSR >= resistance) return 0f;
            float after = ResistanceRule.PassChance(false, false, true, false, resistance, roll.SpellPenetration);
            return ValueModel.PreventedEffectShare(roll.PenetrationChance, after);
        }

        private static float TypedSaveProtectionShare(UnitEntityData ally, AbilityData cast, AbilityProfile profile,
                                                       BuffPart part, float amount)
        {
            StatType stat;
            switch (profile.Save)
            {
                case SavingThrowType.Fortitude: stat = StatType.SaveFortitude; break;
                case SavingThrowType.Reflex: stat = StatType.SaveReflex; break;
                case SavingThrowType.Will: stat = StatType.SaveWill; break;
                default: return 0f;
            }
            if (profile.NestedSaves?.Count > 0) return 0f;
            int bonus = (int)Increment(ally, stat, part, amount);
            if (bonus <= 0) return 0f;
            float before = CombatMath.SaveFailChance(cast, profile.Save, ally);
            float after = CombatMath.SaveFailChance(cast, profile.Save, ally, -bonus);
            // Half-on-save is the conservative retained part of a mixed save-dependent payload.
            return ValueModel.PreventedEffectShare(ValueModel.SaveEffectFactor(before, profile.HalfOnSave),
                                                    ValueModel.SaveEffectFactor(after, profile.HalfOnSave));
        }

        private static bool OnlyBlockedBuff(AbilityData cast, UnitEntityData ally, AbilityProfile profile, SpellDescriptor mask)
        {
            if (profile.ControlStrength == ControlStrength.None || profile.DealsDamage || profile.Maneuvers.Count != 0 ||
                profile.TargetBuffs.Count != 1 || profile.SpawnedAreas.Any()) return false;
            foreach (var buff in profile.TargetBuffs)
                return buff != null && ((cast.SpellDescriptor | buff.SpellDescriptor) & mask) != 0 &&
                    !CombatMath.BuffImmune(ally, buff, cast);
            return false;
        }

        private static bool OnlyAppliesCondition(AbilityProfile profile, UnitCondition condition)
        {
            if (profile.ControlStrength == ControlStrength.None || profile.DealsDamage || profile.Maneuvers.Count != 0 ||
                profile.TargetBuffs.Count != 1 || profile.SpawnedAreas.Any()) return false;
            foreach (var buff in profile.TargetBuffs)
            {
                if (buff?.ComponentsArray == null) return false;
                bool applies = false;
                foreach (var component in buff.ComponentsArray)
                {
                    if (component is SpellDescriptorComponent || component is RemoveWhenCombatEnded) continue;
                    if (component is AddCondition added && added.Condition == condition ||
                        component is BuffStatusCondition status && status.Condition == condition)
                        applies = true;
                    else return false;
                }
                if (!applies) return false;
            }
            return true;
        }
    }
}
