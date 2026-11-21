// ★ Fusion coverage C6 (author request; docs/FUSION_COVERAGE_C6_C8.md): the buffs a spell puts on its target that the engine
// read as "not harmful" because no flag or control marks them, though they only hurt or hinder their bearer. One reader, by
// mechanism, from the buff's own components (supplied exports and assembly):
//  - Damage: damage the buff deals its bearer - when it lands (AddFactContextActions / AddBuffActions Activated: Excruciating
//    Deformation), each round (NewRound: Touch of Slime's Constitution, Excruciating Deformation's Dexterity and fire), when it
//    ends (Deactivated: Acidic Spray's acid, behind a Reflex save), or when the bearer attacks, once a round
//    (AddInitiatorAttackRollTrigger on its owner with a one-round cooldown buff: Serenity's 3d6). Ability damage counts by its
//    hit-point equivalent (DrainEquivalent). A removal on fire or cold (AddEnergyDamageTrigger) and a movement penalty ride along.
//  - Hinders: only penalties the value reader does not price - touch AC (ACTouchBonus, Penalty), energy vulnerability
//    (AddDamageResistanceEnergy with a negative multiplier), a spell-resistance penalty (AddSpellResistance with
//    AllSpellResistancePenaltyDoNotUse): the Peer Through spells. Never a gain for the bearer; credited nothing.
//  - Mixed: a size decrease with its stat changes (Reduce Person: -2 Strength, +2 Dexterity, smaller). Priced by the stat
//    reader's net share, never above it, never below nothing.
//  - Benefit: temporary hit points only (Enervation's undead branch). Priced as damage the bearer will not take.
//  - PeriodicControl: each round a save ends it, else a second save puts a control on (Phantasmal Putrefaction: Will, then
//    Fortitude or nauseated, as its child). Priced by the chance it is on in each round.
// Anything else (Curse of Magic Negation's spell resistance and negated spellblight: it also stands against the party's own
// later spells) stays unread. Metadata only, once per buff: no buff, context or rule is created or run.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.Utility;
using Kingmaker.RuleSystem;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        internal enum CarrierKind { None, Damage, Hinders, Mixed, Benefit, PeriodicControl }

        internal sealed class CarrierPlan
        {
            internal CarrierKind Kind;
            internal string Unread;
            internal ActionList Activated, Round, Deactivated;
            /// <summary>Damage the bearer takes when it attacks, at most once a round (the cooldown buff's).</summary>
            internal ActionList OnAttack;
            /// <summary>PeriodicControl: the round's two saves and the control the second one puts on.</summary>
            internal ContextActionSavingThrow ControlFirstSave, ControlSecondSave;
            internal BlueprintBuff Control;
            internal TemporaryHitPointsRandom TempHp;
        }

        private static readonly Dictionary<BlueprintBuff, CarrierPlan> s_CarrierPlans = new Dictionary<BlueprintBuff, CarrierPlan>();
        private static readonly System.Reflection.FieldInfo TouchDescriptor =
            HarmonyLib.AccessTools.Field(typeof(Kingmaker.Designers.Mechanics.Facts.ACTouchBonus), "m_Descriptor");

        internal static CarrierPlan CarrierPlanOf(BlueprintBuff buff)
        {
            if (buff == null) return new CarrierPlan { Unread = "no buff" };
            if (s_CarrierPlans.TryGetValue(buff, out var known)) return known;
            var plan = new CarrierPlan();
            try { ReadCarrier(buff, plan); }
            catch (Exception ex) { plan.Kind = CarrierKind.None; plan.Unread = ex.GetType().Name; }
            if (plan.Kind == CarrierKind.None && plan.Unread == null) plan.Unread = "nothing it does to its bearer was read";
            if (s_CarrierPlans.Count > 4096) s_CarrierPlans.Clear();
            s_CarrierPlans[buff] = plan;
            return plan;
        }

        private static void ReadCarrier(BlueprintBuff buff, CarrierPlan plan)
        {
            bool damage = false, hinders = false, shrinks = false, stats = false, tempHp = false;
            foreach (var component in buff.ComponentsArray ?? Array.Empty<BlueprintComponent>())
            {
                switch (component)
                {
                    case null:
                    case SpellDescriptorComponent _:
                    case ContextCalculateSharedValue _:
                    case RemoveWhenCombatEnded _:
                        continue;
                    case ContextRankConfig config when CasterLevelConfig(config):
                        continue;
                    case AddFactContextActions facts:   // and AddBuffActions, which derives from it
                        if (!CarrierLists(buff, facts.Activated, facts.NewRound, facts.Deactivated, facts.Dispose, plan)) return;
                        damage |= plan.Activated != null || plan.Round != null || plan.Deactivated != null;
                        continue;
                    case AddInitiatorAttackRollTrigger trigger when trigger.OnOwner:
                        if (plan.OnAttack != null || !OnceARoundDamage(buff, trigger.Action, out var hit)) { plan.Unread = "attack trigger"; return; }
                        plan.OnAttack = hit;
                        damage = true;
                        continue;
                    case AddEnergyDamageTrigger energy when RemovalOnly(energy.Actions):
                        continue;   // ends it early on that energy: no credit, never a gain
                    case BuffMovementSpeed speed when speed.Value < 0:
                    case Kingmaker.Designers.Mechanics.Facts.ACTouchBonus touch when TouchDescriptor?.GetValue(touch) is Kingmaker.Enums.ModifierDescriptor d &&
                                                                                     d == Kingmaker.Enums.ModifierDescriptor.Penalty:
                    case AddSpellResistance resistance when resistance.AllSpellResistancePenaltyDoNotUse:
                        hinders = true;
                        continue;
                    case AddDamageResistanceEnergy vulnerable when vulnerable.UseValueMultiplier &&
                        vulnerable.ValueMultiplier?.ValueType == ContextValueType.Simple && vulnerable.ValueMultiplier.Value < 0:
                        hinders = true;
                        continue;
                    case ChangeUnitSize size when size.SizeDelta < 0:
                        shrinks = true;
                        continue;
                    case AddGenericStatBonus _:
                        stats = true;
                        continue;
                    case TemporaryHitPointsRandom random when plan.TempHp == null:
                        plan.TempHp = random;
                        tempHp = true;
                        continue;
                    default:
                        plan.Unread = component.GetType().Name;
                        return;
                }
            }
            if (plan.ControlFirstSave != null) plan.Kind = damage || hinders || shrinks || stats || tempHp ? CarrierKind.None : CarrierKind.PeriodicControl;
            else if (damage) plan.Kind = shrinks || stats || tempHp ? CarrierKind.None : CarrierKind.Damage;
            else if (tempHp) plan.Kind = hinders || shrinks || stats ? CarrierKind.None : CarrierKind.Benefit;
            else if (shrinks) plan.Kind = hinders ? CarrierKind.None : CarrierKind.Mixed;
            else if (hinders && !stats) plan.Kind = CarrierKind.Hinders;
            if (plan.Kind == CarrierKind.None && plan.Unread == null) plan.Unread = "a mix this reader does not take";
        }

        /// <summary>The fact actions: damage lists, or the round's control pattern; a dispose list must be empty.</summary>
        private static bool CarrierLists(BlueprintBuff buff, ActionList activated, ActionList round, ActionList deactivated, ActionList dispose,
                                         CarrierPlan plan)
        {
            if (!Empty(dispose)) { plan.Unread = "dispose actions"; return false; }
            if (!Empty(activated))
            {
                if (plan.Activated != null || !CarrierDamageList(buff, activated, 0)) { plan.Unread = "activation actions"; return false; }
                plan.Activated = activated;
            }
            if (!Empty(deactivated))
            {
                if (plan.Deactivated != null || !CarrierDamageList(buff, deactivated, 0)) { plan.Unread = "deactivation actions"; return false; }
                plan.Deactivated = deactivated;
            }
            if (!Empty(round))
            {
                if (plan.Round != null || plan.ControlFirstSave != null) { plan.Unread = "two round lists"; return false; }
                if (CarrierDamageList(buff, round, 0)) plan.Round = round;
                else if (!RoundControl(round, plan)) { plan.Unread = "round actions"; return false; }
            }
            return true;
        }

        /// <summary>Damage to the bearer only (hit points or ability damage), saves with their two branches, removal, effects.</summary>
        private static bool CarrierDamageList(BlueprintBuff buff, ActionList list, int depth)
        {
            if (depth > 8) return false;
            bool any = false;
            foreach (var action in list?.Actions ?? Array.Empty<GameAction>())
            {
                switch (action)
                {
                    case null:
                    case ContextActionSpawnFx _:
                        continue;
                    case ContextActionRemoveSelf _:
                        continue;
                    case ContextActionDealDamage deal when deal.GetType() == typeof(ContextActionDealDamage):
                        if (deal.Value == null || !TickValue(buff, deal.Value.DiceCountValue) || !TickValue(buff, deal.Value.BonusValue) ||
                            deal.Half || deal.HalfIfSaved || deal.ReadPreRolledFromSharedValue || deal.AddAdditionalDamage ||
                            deal.AddFavoredEnemyDamage || deal.UseWeaponDamageModifiers || deal.UseMinHPAfterDamage ||
                            deal.WriteResultToSharedValue || deal.WriteRawResultToSharedValue || deal.WriteCriticalToSharedValue ||
                            deal.Drain || deal.IsAoE || deal.DamageType == null) return false;
                        any = true;
                        continue;
                    case ContextActionSavingThrow save when save.GetType() == typeof(ContextActionSavingThrow):
                        if (save.FromBuff || save.Type == SavingThrowType.Unknown || save.HasCustomDC && save.CustomDC?.ValueType != ContextValueType.Simple ||
                            PeriodicSaveIncreases == null || PeriodicSaveIncreases.GetValue(save) is Array increases && increases.Length > 0 ||
                            save.Actions?.Actions?.Length != 1 || !(save.Actions.Actions[0] is ContextActionConditionalSaved saved)) return false;
                        if (!CarrierDamageList(buff, saved.Failed, depth + 1) && !Empty(saved.Failed)) return false;
                        if (!CarrierDamageList(buff, saved.Succeed, depth + 1) && !Empty(saved.Succeed)) return false;
                        any = true;
                        continue;
                    default:
                        return false;
                }
            }
            return any;
        }

        /// <summary>
        /// Phantasmal Putrefaction's round: a save whose pass removes the buff (ContextActionRemoveSelf) and whose failure, while
        /// the bearer has no control yet (Conditional HasBuff, empty if true), rolls a second save whose failure applies it.
        /// </summary>
        private static bool RoundControl(ActionList round, CarrierPlan plan)
        {
            var actions = round?.Actions;
            if (actions?.Length != 1 || !(actions[0] is ContextActionSavingThrow first) || first.GetType() != typeof(ContextActionSavingThrow) ||
                !PlainSave(first) || !(first.Actions.Actions[0] is ContextActionConditionalSaved firstBranch) ||
                !RemovesSelfOnly(firstBranch.Succeed) || firstBranch.Failed?.Actions?.Length != 1 ||
                !(firstBranch.Failed.Actions[0] is Conditional guard) || guard.GetType() != typeof(Conditional) ||
                guard.ConditionsChecker?.Conditions?.Length != 1 || !(guard.ConditionsChecker.Conditions[0] is ContextConditionHasBuff has) ||
                has.Not || has.Buff == null || !Empty(guard.IfTrue) || guard.IfFalse?.Actions?.Length != 1 ||
                !(guard.IfFalse.Actions[0] is ContextActionSavingThrow second) || second.GetType() != typeof(ContextActionSavingThrow) ||
                !PlainSave(second) || !(second.Actions.Actions[0] is ContextActionConditionalSaved secondBranch) ||
                !Empty(secondBranch.Succeed) || secondBranch.Failed?.Actions?.Length != 1 ||
                !(secondBranch.Failed.Actions[0] is ContextActionApplyBuff apply) || apply.Buff != has.Buff || apply.ToCaster)
                return false;
            plan.ControlFirstSave = first;
            plan.ControlSecondSave = second;
            plan.Control = apply.Buff;
            return true;
        }

        private static bool PlainSave(ContextActionSavingThrow save) =>
            !save.FromBuff && save.Type != SavingThrowType.Unknown && (!save.HasCustomDC || save.CustomDC?.ValueType == ContextValueType.Simple) &&
            PeriodicSaveIncreases != null && !(PeriodicSaveIncreases.GetValue(save) is Array increases && increases.Length > 0) &&
            save.Actions?.Actions?.Length == 1 && save.Actions.Actions[0] is ContextActionConditionalSaved;

        /// <summary>Serenity: Conditional(not HasBuff cooldown) → apply the cooldown (one round), deal damage. The damage list.</summary>
        private static bool OnceARoundDamage(BlueprintBuff buff, ActionList action, out ActionList damage)
        {
            damage = null;
            var actions = action?.Actions;
            if (actions?.Length != 1 || !(actions[0] is Conditional guard) || guard.GetType() != typeof(Conditional) ||
                guard.ConditionsChecker?.Conditions?.Length != 1 || !(guard.ConditionsChecker.Conditions[0] is ContextConditionHasBuff has) ||
                !has.Not || has.Buff == null || !Empty(guard.IfFalse)) return false;
            var inner = guard.IfTrue?.Actions;
            if (inner == null) return false;
            var deals = new List<GameAction>();
            bool cooldown = false;
            foreach (var a in inner)
            {
                if (a is ContextActionApplyBuff apply && apply.Buff == has.Buff && !apply.ToCaster &&
                    apply.DurationValue?.Rate == Kingmaker.UnitLogic.Mechanics.DurationRate.Rounds &&
                    apply.DurationValue.DiceCountValue?.ValueType == ContextValueType.Simple && apply.DurationValue.DiceCountValue.Value == 0 &&
                    apply.DurationValue.BonusValue?.ValueType == ContextValueType.Simple && apply.DurationValue.BonusValue.Value == 1) { cooldown = true; continue; }
                deals.Add(a);
            }
            if (!cooldown || deals.Count == 0) return false;
            damage = new ActionList { Actions = deals.ToArray() };
            return CarrierDamageList(buff, damage, 0);
        }

        private static bool RemovalOnly(ActionList actions)
        {
            if (Empty(actions)) return false;
            foreach (var a in actions.Actions)
                if (!(a is ContextActionRemoveSelf) && !(a is ContextActionRemoveBuffSingleStack) && !(a is ContextActionRemoveBuff)) return false;
            return true;
        }

        /// <summary>The expected damage one carrier list does to <paramref name="recipient"/> (ability damage by its HP
        /// equivalent), and the chance the buff is still on after it (a save in it may remove it).</summary>
        internal static PeriodicTick CarrierListHarm(Kingmaker.UnitLogic.Abilities.AbilityData ability, BlueprintBuff buff,
            Kingmaker.EntitySystem.Entities.UnitEntityData recipient, ActionList list)
        {
            int baseDC = ParamsOf(ability)?.DC ?? 10;
            bool maximize = false, empower = false;
            try
            {
                maximize = ability.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Maximize);
                empower = ability.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Empower);
            }
            catch { }
            return ReadPeriodicTick(list,
                save => SaveFailChance(ability, save.Type, recipient, save.HasCustomDC ? save.CustomDC.Value - baseDC : 0),
                deal =>
                {
                    var harm = new Affliction();
                    float mean = ValueModel.PeriodicDiceMean(
                        FusedDiceCount(ability, PeriodicRank(ability, buff, recipient, deal.Value.DiceCountValue)),
                        deal.Value.DiceType.Sides(), PeriodicRank(ability, buff, recipient, deal.Value.BonusValue), maximize, empower);
                    var kind = KindOf(deal);
                    harm.HitPoints = kind == DealKind.HitPoints ? AfterTargetReduction(recipient, deal, mean, ability)
                                                                : DrainEquivalent(deal, kind, mean, recipient);
                    return harm;
                },
                apply => new Affliction());
        }

        /// <summary>The temporary hit points a Benefit carrier gives: its dice's mean plus its bonus (per caster level when scaled).</summary>
        internal static float CarrierTempHp(Kingmaker.UnitLogic.Abilities.AbilityData ability, CarrierPlan plan)
        {
            var thp = plan?.TempHp;
            if (thp == null) return 0f;
            float dice = thp.Dice.Rolls * (thp.Dice.Dice.Sides() + 1) / 2f;
            float bonus = thp.Bonus?.ValueType == ContextValueType.Simple ? thp.Bonus.Value : 0f;
            if (thp.ScaleBonusByCasterLevel) bonus *= Math.Max(1, ParamsOf(ability)?.CasterLevel ?? 1);
            return Math.Max(0f, dice + bonus);
        }
    }
}
