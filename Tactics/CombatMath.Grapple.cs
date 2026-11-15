// ★ G1 (docs/TACTICAL_AI.md §170): a grapple a weapon hit starts (Griffon/Bear/Lizard/Tiger Grabber, the Smilodon's
// grab), read from the game's own blueprints and parts. Nothing here rolls, issues a command or changes a unit.
//
// The inspected native shape (GriffonGrappleBuff and its kin): an AddInitiatorAttackWithWeaponTrigger that runs on a hit
// with one weapon type, optionally behind a Conditional on the caster's facts whose true branch is empty, into a
// ContextActionCombatManeuverCustom Grapple whose success is one ContextActionGrapple. Its holder buff carries
// ShifterGrabInitiatorBuff (the automatic maintenance, pin and free natural attack each round). Any other flag, branch or
// action keeps the old forecast: a trigger we do not read exactly is not modelled.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.UnitLogic.Parts;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>★ G1: a recognised grab trigger (blueprint metadata only).</summary>
    public sealed class GrabTrigger
    {
        public AddInitiatorAttackWithWeaponTrigger Component;
        public BlueprintWeaponType WeaponType;
        public bool CheckCategory;
        public Kingmaker.Enums.WeaponCategory Category;
        public bool CheckRange;
        public WeaponRangeType RangeType;
        /// <summary>The Conditional's checker in front of the maneuver (null when there is none): the grapple runs when it is false.</summary>
        public ConditionsChecker Gate;
        public BlueprintBuff HolderBuff;
        /// <summary>The holder buff's simple CMB bonus to grapple (CMBBonusForManeuver: +5 circumstance for the Griffon).</summary>
        public int MaintainBonus;
        /// <summary>The holder buff's Dexterity change (ShifterGrabBuffBase: -4), which lowers the holder's CMD while it holds.</summary>
        public int HolderDexterity;

        /// <summary>A hit with <paramref name="weapon"/> runs the trigger (AbstractWeaponTrigger.IsSuitable for the flags read).</summary>
        public bool Suits(ItemEntityWeapon weapon) => Suits(weapon?.Blueprint);

        /// <summary>The same test on the weapon's blueprint (the native range test reads only its attack type).</summary>
        public bool Suits(BlueprintItemWeapon blueprint)
        {
            if (blueprint == null) return false;
            if (WeaponType != null && WeaponType != blueprint.Type) return false;
            if (CheckCategory && Category != blueprint.Category) return false;
            if (CheckRange && !RangeType.IsSuitableWeapon(blueprint)) return false;
            return true;
        }
    }

    /// <summary>★ G1: what a grapple started against one target would do, read once a frame.</summary>
    public sealed class GrappleOpening
    {
        public int Frame;
        public GrabTrigger Trigger;
        /// <summary>The grapple maneuver's chance on a suitable hit (CMB against CMD, immunity, concealment).</summary>
        public float Success;
        /// <summary>The holder's roll each new round (with the holder buff's bonus).</summary>
        public float Maintain;
        /// <summary>The victim's own try to break free each round (its CMB, or its better skill, against the holder's CMD).</summary>
        public float BreakFree;
    }

    public static partial class CombatMath
    {
        private static readonly Dictionary<AddInitiatorAttackWithWeaponTrigger, GrabTrigger> GrabTriggers =
            new Dictionary<AddInitiatorAttackWithWeaponTrigger, GrabTrigger>();
        private static readonly Dictionary<AddInitiatorAttackWithWeaponTrigger, string> GrabRefusals =
            new Dictionary<AddInitiatorAttackWithWeaponTrigger, string>();

        /// <summary>
        /// ★ G1: the trigger as a grab, or null with <paramref name="refusal"/> naming what is not read (null refusal: not a
        /// grapple at all). Blueprint metadata only, cached per component.
        /// </summary>
        public static GrabTrigger ReadGrabTrigger(AddInitiatorAttackWithWeaponTrigger t, out string refusal)
        {
            refusal = null;
            if (t == null) return null;
            if (GrabTriggers.TryGetValue(t, out var known)) return known;
            if (GrabRefusals.TryGetValue(t, out refusal)) return null;
            var read = ReadGrabTriggerCore(t, out refusal);
            if (read != null) GrabTriggers[t] = read;
            else GrabRefusals[t] = refusal;
            return read;
        }

        private static GrabTrigger ReadGrabTriggerCore(AddInitiatorAttackWithWeaponTrigger t, out string refusal)
        {
            refusal = null;
            var actions = t.Action?.Actions;
            if (actions == null || actions.Length != 1) { refusal = GrappleIn(t.Action) ? "trigger runs more than one action" : null; return null; }
            ConditionsChecker gate = null;
            GameAction maneuver = actions[0];
            if (maneuver is Conditional conditional)
            {
                if (!GrappleIn(conditional.IfFalse) && !GrappleIn(conditional.IfTrue)) return null;
                if (conditional.IfTrue?.Actions?.Length > 0 || conditional.IfFalse?.Actions?.Length != 1 ||
                    conditional.ConditionsChecker?.Conditions == null ||
                    !conditional.ConditionsChecker.Conditions.All(c => c != null && c.GetType() == typeof(ContextConditionCasterHasFact)))
                { refusal = "grapple behind a branch other than an empty caster-fact gate"; return null; }
                gate = conditional.ConditionsChecker;
                maneuver = conditional.IfFalse.Actions[0];
            }
            if (!(maneuver is ContextActionCombatManeuverCustom custom))
            { refusal = GrappleIn(t.Action) ? "grapple through " + maneuver?.GetType().Name : null; return null; }
            if (custom.Type != CombatManeuver.Grapple) return null;
            if (custom.Failure?.Actions?.Length > 0 || custom.Success?.Actions?.Length != 1 ||
                !(custom.Success.Actions[0] is ContextActionGrapple grapple))
            { refusal = "grapple maneuver with other success or failure actions"; return null; }
            // Every flag the native suitability test reads beyond hit, weapon type, category and range keeps the old forecast.
            if (t.TriggerBeforeAttack || !t.OnlyHit || t.OnMiss || t.OnlyOnFullAttack || t.OnlyOnFirstAttack || t.OnlyOnFirstHit ||
                t.CriticalHit || t.OnlyNatural20 || t.OnlySneakAttack || t.NotSneakAttack || t.CheckWeaponBlueprint ||
                t.CheckWeaponGroup || t.CheckPhysicalDamageForm || t.ReduceHPToZero || t.DamageMoreTargetMaxHP || t.CheckDistance ||
                t.AllNaturalAndUnarmed || t.DuelistWeapon || t.NotExtraAttack || t.OnCharge || t.ActionsOnInitiator)
            { refusal = "trigger flags beyond hit, weapon type, category and range"; return null; }
            var holder = grapple.CasterBuff;
            if (holder == null || holder.GetComponent<ShifterGrabInitiatorBuff>() == null)
            { refusal = "holder buff without the native maintenance (ShifterGrabInitiatorBuff)"; return null; }
            int bonus = 0;
            foreach (var cmb in holder.GetComponents<CMBBonusForManeuver>())
            {
                if (cmb.Maneuvers == null || !cmb.Maneuvers.Contains(CombatManeuver.Grapple)) continue;
                if (cmb.CheckFact || cmb.Value == null || !cmb.Value.IsValueSimple) { refusal = "holder buff with a conditional or computed grapple bonus"; return null; }
                bonus += cmb.Value.Value;
            }
            int dexterity = 0;
            var grab = holder.GetComponent<ShifterGrabInitiatorBuff>();
            if (GrabDexterity != null && GrabDexterity.GetValue(grab) is int dex) dexterity = dex;
            return new GrabTrigger
            {
                Component = t,
                WeaponType = t.WeaponType,
                CheckCategory = t.CheckWeaponCategory,
                Category = t.Category,
                CheckRange = t.CheckWeaponRangeType,
                RangeType = t.RangeType,
                Gate = gate,
                HolderBuff = holder,
                MaintainBonus = bonus,
                HolderDexterity = dexterity
            };
        }

        private static readonly System.Reflection.FieldInfo GrabDexterity =
            HarmonyLib.AccessTools.Field(typeof(ShifterGrabBuffBase), "m_DexterityBonus");

        private static bool GrappleIn(ActionList list) => list?.Actions != null && list.Actions.Any(a =>
            a is ContextActionGrapple || a is ContextActionCombatManeuverCustom m && m.Type == CombatManeuver.Grapple && GrappleIn(m.Success) ||
            a is ContextActionCombatManeuver p && p.Type == CombatManeuver.Grapple ||
            a is Conditional c && (GrappleIn(c.IfTrue) || GrappleIn(c.IfFalse)));

        /// <summary>The gate lets the grapple run: its checker is false for this caster (ConditionsChecker.Check: And/Or, Not).</summary>
        private static bool GateOpen(ConditionsChecker gate, UnitEntityData unit)
        {
            if (gate == null) return true;
            bool any = false, all = true;
            foreach (var condition in gate.Conditions)
            {
                var hasFact = (ContextConditionCasterHasFact)condition;
                bool result = unit.Descriptor.HasFact(hasFact.Fact) != hasFact.Not;
                any |= result;
                all &= result;
            }
            bool check = gate.Operation == Operation.Or ? any : all;
            return !check;
        }

        private static readonly Dictionary<UnitEntityData, KeyValuePair<int, GrabTrigger>> UnitGrabs =
            new Dictionary<UnitEntityData, KeyValuePair<int, GrabTrigger>>();

        /// <summary>
        /// ★ G1: the recognised grab trigger <paramref name="unit"/>'s buffs carry now, once a frame (Grabber is a buff of an
        /// activatable). Only party units: an enemy's routine keeps the old forecast (its hold is not valued for the party).
        /// </summary>
        internal static GrabTrigger ActiveGrab(UnitEntityData unit)
        {
            if (unit == null || !unit.IsPlayerFaction) return null;
            int frame = Time.frameCount;
            if (UnitGrabs.TryGetValue(unit, out var known) && known.Key == frame) return known.Value;
            GrabTrigger found = null;
            try
            {
                foreach (var buff in unit.Buffs)
                {
                    if (buff?.Blueprint?.ComponentsArray == null || !buff.Active) continue;
                    foreach (var component in buff.Blueprint.ComponentsArray)
                    {
                        if (!(component is AddInitiatorAttackWithWeaponTrigger trigger)) continue;
                        var grab = ReadGrabTrigger(trigger, out _);
                        if (grab != null && GateOpen(grab.Gate, unit)) { found = grab; break; }
                    }
                    if (found != null) break;
                }
            }
            catch (Exception ex) { Main.Verbose($"[Grapple] {unit.CharacterName}: grab triggers unreadable: {ex.Message}"); }
            if (UnitGrabs.Count > 128) UnitGrabs.Clear();
            UnitGrabs[unit] = new KeyValuePair<int, GrabTrigger>(frame, found);
            return found;
        }

        private static readonly Dictionary<string, GrappleOpening> Openings = new Dictionary<string, GrappleOpening>();

        /// <summary>The per-frame grab and odds reads (a same-frame state preview clears the routines: ClearAttackCache).</summary>
        private static void ClearGrappleReads()
        {
            UnitGrabs.Clear();
            Openings.Clear();
        }
        private static readonly AbilityProfile.ManeuverEffect GrappleManeuver =
            new AbilityProfile.ManeuverEffect { Type = CombatManeuver.Grapple };

        /// <summary>
        /// ★ G1: the grapple a suitable hit of <paramref name="unit"/> would start on <paramref name="target"/>, or null when
        /// none can: no recognised grab, the unit holds one already, the target is held already or immune to maneuvers
        /// (ContextActionGrapple refuses both pairs). Once a frame per pair; the native CMB/CMD rules only.
        /// </summary>
        internal static GrappleOpening GrappleOpeningOn(UnitEntityData unit, UnitEntityData target)
        {
            if (unit == null || target == null || unit == target) return null;
            var grab = ActiveGrab(unit);
            if (grab == null || unit.Get<UnitPartGrappleInitiator>() != null || target.Get<UnitPartGrappleTarget>() != null) return null;
            int frame = Time.frameCount;
            string key = unit.UniqueId + "|" + target.UniqueId;
            if (Openings.TryGetValue(key, out var known) && known.Frame == frame && known.Trigger == grab) return known;
            var opening = new GrappleOpening { Frame = frame, Trigger = grab };
            try
            {
                if (target.Descriptor.State.HasCondition(UnitCondition.ImmuneToCombatManeuvers)) return null;
                ManeuverOdds(unit, target, null, GrappleManeuver, out opening.Success, out _);
                // The holder's roll each round: the same rule with the holder buff's bonus (RuleCombatManeuver adds it).
                ManeuverValues(unit, target, null, GrappleManeuver, out int cmb, out int cmd);
                opening.Maintain = ValueModel.ManeuverRollChance(cmb + grab.MaintainBonus, cmd, unit.Descriptor.State.Features.AlwaysChance) *
                                   ManeuverConcealmentShare(unit, target);
                opening.BreakFree = BreakFreeChance(target, unit, grab);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Grapple] {unit.CharacterName}->{target.CharacterName}: {ex.Message}");
                return null;
            }
            if (Openings.Count > 256) Openings.Clear();
            Openings[key] = opening;
            return opening;
        }

        /// <summary>
        /// The victim's try to break free (UnitHelper.TryBreakFree with BreakFreeFlags.Default, as UnitGrappleController calls
        /// it): against the holder's CMD, by a grapple maneuver when its CMB beats its Mobility and Athletics, else by the better
        /// of those skills. The holder's CMD while it holds: the holder buff's Dexterity change taken into its Dexterity bonus.
        /// </summary>
        private static float BreakFreeChance(UnitEntityData victim, UnitEntityData holder, GrabTrigger grab)
        {
            ManeuverValues(victim, holder, null, GrappleManeuver, out int cmb, out int cmd);
            int dexterity = holder.Stats.Dexterity.ModifiedValue;
            cmd += DexterityBonus(dexterity + grab.HolderDexterity) - DexterityBonus(dexterity);
            int skill = Math.Max((int)victim.Stats.SkillMobility, (int)victim.Stats.SkillAthletics);
            if (skill < cmb)
                return ValueModel.ManeuverRollChance(cmb, cmd, victim.Descriptor.State.Features.AlwaysChance) *
                       ManeuverConcealmentShare(victim, holder);
            return ValueModel.SkillCheckChance(skill, cmd);
        }

        private static int DexterityBonus(int score) => (int)Math.Floor((score - 10) / 2.0);
    }

    /// <summary>★ G1: the grapple pair a unit is in now, as the game's parts hold it (read only).</summary>
    public static class GrappleState
    {
        /// <summary>
        /// "holds Ghoul Stalker", "held by Ulbrig (pinned)", or null: the pair as UnitGrappleController keeps it (each side
        /// names the other and the other is conscious), so a pair the controller is about to drop is not reported.
        /// </summary>
        public static string Describe(UnitEntityData unit)
        {
            try
            {
                var holds = unit?.Get<UnitPartGrappleInitiator>();
                var victim = holds?.Target.Value;
                if (victim != null && victim.Get<UnitPartGrappleTarget>()?.Initiator == unit && victim.Descriptor.State.IsConscious)
                    return "holds " + victim.CharacterName;
                var held = unit?.Get<UnitPartGrappleTarget>();
                var holder = held?.Initiator.Value;
                if (holder != null && holder.Get<UnitPartGrappleInitiator>()?.Target == unit && holder.Descriptor.State.IsConscious)
                    return "held by " + holder.CharacterName + (held.IsPinned ? " (pinned)" : "");
            }
            catch { }
            return null;
        }
    }
}
