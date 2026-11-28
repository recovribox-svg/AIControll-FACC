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
using System.Reflection;
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
        /// <summary>★ G2: the buff's other open grab triggers into the same hold (the Tiger and Smilodon grab with claw and
        /// bite: one trigger per weapon type). Null for a buff with one.</summary>
        public GrabTrigger[] Also;

        /// <summary>A hit with <paramref name="weapon"/> runs the trigger (AbstractWeaponTrigger.IsSuitable for the flags read).</summary>
        public bool Suits(ItemEntityWeapon weapon) => Suits(weapon?.Blueprint);

        /// <summary>The same test on the weapon's blueprint (the native range test reads only its attack type), for this
        /// trigger or any other of its buff (<see cref="Also"/>).</summary>
        public bool Suits(BlueprintItemWeapon blueprint)
        {
            if (SuitsOwn(blueprint)) return true;
            if (Also != null)
                foreach (var other in Also)
                    if (other.SuitsOwn(blueprint)) return true;
            return false;
        }

        private bool SuitsOwn(BlueprintItemWeapon blueprint)
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
        // Same post-quote kernel for ordinary attacks and cached leap mode alternatives. No actor/rule preview.
        private static float PriceGrabLine(AttackLine line, GrappleOpening grapple, float damage, float hit,
            ref ValueModel.GrappleRun run, ref float freeDamage, ref int natural)
        {
            if (grapple == null) return damage;
            damage *= run.Next(hit, grapple.Trigger.Suits(line.Weapon), grapple.Success);
            if (line.Weapon?.Blueprint?.IsNatural == true) { freeDamage += line.GameHit; natural++; }
            return damage;
        }

        private static void FinishGrabQuote(WeaponAttackEstimate result, GrappleOpening grapple,
            ValueModel.GrappleRun run, float freeDamage, int natural)
        {
            if (grapple == null || run.Starts <= 0f) return;
            result.GrappleChance = run.Starts;
            result.Grapple = grapple;
            result.ExpectedAttacks = run.Attacks;
            result.GrappleFreeDamage = natural > 0 ? freeDamage / natural : 0f;
        }

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
            if (!HolderTerms(holder, out int bonus, out int dexterity, out refusal)) return null;
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

        /// <summary>
        /// The holder buff's terms: the simple grapple CMB bonus of its CMBBonusForManeuver (a RuleCombatManeuver handler,
        /// so never in RuleCalculateCMB: it is added by hand, also while it is on) and its Dexterity change (a live stat
        /// modifier while it is on). False with <paramref name="refusal"/> for a buff without the native maintenance or
        /// with a conditional or computed bonus.
        /// </summary>
        private static bool HolderTerms(BlueprintBuff holder, out int bonus, out int dexterity, out string refusal)
        {
            bonus = 0; dexterity = 0; refusal = null;
            if (holder == null || holder.GetComponent<ShifterGrabInitiatorBuff>() == null)
            { refusal = "holder buff without the native maintenance (ShifterGrabInitiatorBuff)"; return false; }
            foreach (var cmb in holder.GetComponents<CMBBonusForManeuver>())
            {
                if (cmb.Maneuvers == null || !cmb.Maneuvers.Contains(CombatManeuver.Grapple)) continue;
                if (cmb.CheckFact || cmb.Value == null || !cmb.Value.IsValueSimple) { refusal = "holder buff with a conditional or computed grapple bonus"; return false; }
                bonus += cmb.Value.Value;
            }
            var grab = holder.GetComponent<ShifterGrabInitiatorBuff>();
            if (GrabDexterity != null && GrabDexterity.GetValue(grab) is int dex) dexterity = dex;
            return true;
        }

        private static readonly Dictionary<BlueprintBuff, GrabTrigger[]> BuffGrabs = new Dictionary<BlueprintBuff, GrabTrigger[]>();
        private static readonly Dictionary<KeyValuePair<BlueprintBuff, int>, GrabTrigger> GrabSets =
            new Dictionary<KeyValuePair<BlueprintBuff, int>, GrabTrigger>();

        /// <summary>
        /// ★ G2: what <paramref name="buff"/> grabs with for <paramref name="unit"/> (the toggle's buff of a Grabber, on or
        /// not yet on): its recognised grab triggers whose gate is open, into one hold, or null when none would run. Only a
        /// buff whose grab triggers all lead to the same holder buff is combined; otherwise the first open one alone.
        /// Blueprint metadata is cached per buff, the combination per buff and set of open gates (a stable identity).
        /// </summary>
        internal static GrabTrigger GrabOf(BlueprintBuff buff, UnitEntityData unit)
        {
            if (buff?.ComponentsArray == null || unit == null) return null;
            var triggers = GrabTriggersOf(buff);
            int open = 0;
            for (int i = 0; i < triggers.Length && i < 30; i++)
                if (GateOpen(triggers[i].Gate, unit)) open |= 1 << i;
            return ComposeGrab(buff, open);
        }

        private static readonly GrabTrigger[] NoGrabs = new GrabTrigger[0];

        /// <summary>The buff's recognised grab triggers, in component order (blueprint metadata, cached; most buffs share
        /// one empty array).</summary>
        public static GrabTrigger[] GrabTriggersOf(BlueprintBuff buff)
        {
            if (buff?.ComponentsArray == null) return NoGrabs;
            if (!BuffGrabs.TryGetValue(buff, out var triggers))
            {
                triggers = buff.ComponentsArray.OfType<AddInitiatorAttackWithWeaponTrigger>()
                               .Select(t => ReadGrabTrigger(t, out _)).Where(g => g != null).ToArray();
                if (triggers.Length == 0) triggers = NoGrabs;
                BuffGrabs[buff] = triggers;
            }
            return triggers;
        }

        /// <summary>The hold the triggers of <paramref name="buff"/> named by <paramref name="open"/> (bit i: trigger i's gate
        /// is open) grab into; null when none is open.</summary>
        public static GrabTrigger ComposeGrab(BlueprintBuff buff, int open)
        {
            var triggers = GrabTriggersOf(buff);
            if (triggers.Length == 0) return null;
            open &= triggers.Length >= 30 ? ~0 : (1 << triggers.Length) - 1;
            if (open == 0) return null;
            var key = new KeyValuePair<BlueprintBuff, int>(buff, open);
            if (GrabSets.TryGetValue(key, out var set)) return set;
            var chosen = triggers.Where((t, i) => (open & (1 << i)) != 0).ToList();
            var first = chosen[0];
            if (chosen.Count == 1 || chosen.Any(t => t.HolderBuff != first.HolderBuff || t.MaintainBonus != first.MaintainBonus))
                set = first;
            else
                set = new GrabTrigger
                {
                    Component = first.Component, WeaponType = first.WeaponType, CheckCategory = first.CheckCategory,
                    Category = first.Category, CheckRange = first.CheckRange, RangeType = first.RangeType, Gate = first.Gate,
                    HolderBuff = first.HolderBuff, MaintainBonus = first.MaintainBonus, HolderDexterity = first.HolderDexterity,
                    Also = chosen.Skip(1).ToArray()
                };
            GrabSets[key] = set;
            return set;
        }

        /// <summary>★ G2: the buff only grabs: every component a grab trigger the reader reads (the six forms' Grabber
        /// buffs). Such a toggle's only effect is the hold, so the attack-mode chooser can price it whole.</summary>
        public static bool IsGrabBuff(BlueprintBuff buff)
        {
            var components = buff?.ComponentsArray;
            if (components == null || components.Length == 0) return false;
            foreach (var component in components)
                if (!(component is AddInitiatorAttackWithWeaponTrigger trigger) || ReadGrabTrigger(trigger, out _) == null) return false;
            return true;
        }

        private static UnitEntityData s_AssumedGrabUnit;
        private static GrabTrigger s_AssumedGrab;

        /// <summary>★ G2: a scope in which <see cref="ActiveGrab"/> answers <paramref name="grab"/> (null: none) for
        /// <paramref name="unit"/>: the attack-mode chooser prices one attack with the Grabber on and off. Nothing changes in
        /// the game; the grapple odds' frame cache already checks which trigger it was read for.</summary>
        internal static GrabAssumption AssumeGrab(UnitEntityData unit, GrabTrigger grab)
        {
            var previous = new GrabAssumption(s_AssumedGrabUnit, s_AssumedGrab);
            s_AssumedGrabUnit = unit;
            s_AssumedGrab = grab;
            return previous;
        }

        internal struct GrabAssumption : IDisposable
        {
            private readonly UnitEntityData m_Unit;
            private readonly GrabTrigger m_Grab;
            internal GrabAssumption(UnitEntityData unit, GrabTrigger grab) { m_Unit = unit; m_Grab = grab; }
            public void Dispose() { s_AssumedGrabUnit = m_Unit; s_AssumedGrab = m_Grab; }
        }

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
            if (unit == s_AssumedGrabUnit) return s_AssumedGrab;   // ★ G2: an attack priced with the Grabber on or off
            int frame = Time.frameCount;
            if (UnitGrabs.TryGetValue(unit, out var known) && known.Key == frame) return known.Value;
            GrabTrigger found = null;
            try
            {
                // ★ G2: every open grab trigger of the buff into one hold (claw and bite of the Tiger and Smilodon).
                foreach (var buff in unit.Buffs)
                {
                    if (buff?.Blueprint?.ComponentsArray == null || !buff.Active) continue;
                    found = GrabOf(buff.Blueprint, unit);
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
            ManeuverImmunities.Clear();
        }

        private static readonly Dictionary<UnitEntityData, KeyValuePair<int, int>> ManeuverImmunities =
            new Dictionary<UnitEntityData, KeyValuePair<int, int>>();

        /// <summary>
        /// ★ fusion2g1b review: whether a <paramref name="type"/> maneuver against <paramref name="target"/> fails by the
        /// game's rule. RuleCombatManeuver.OnTrigger cancels it "because of immunity" on the ImmuneToCombatManeuvers
        /// condition or on AutoFailure, which every turned-on ManeuverImmunity component of that type (or of None) sets.
        /// Swarms carry ten of them (SwarmTinyFeature, SwarmDiminutiveFeature: Grapple, Trip, Bull Rush...), and no
        /// condition. Read once a frame per unit (cleared with the attack cache): the facts' runtime components, no roll.
        /// </summary>
        internal static bool ImmuneToManeuver(UnitEntityData target, CombatManeuver type)
        {
            if (target == null) return false;
            if (target.Descriptor.State.HasCondition(UnitCondition.ImmuneToCombatManeuvers)) return true;
            int frame = Time.frameCount;
            if (!ManeuverImmunities.TryGetValue(target, out var known) || known.Key != frame)
            {
                int mask = 0;
                try
                {
                    foreach (var fact in target.Facts.List)
                        foreach (var component in fact.Components)
                            if (component.IsTurnedOn && component.SourceBlueprintComponent is ManeuverImmunity immunity)
                                mask |= ValueModel.ManeuverImmunityBit((int)immunity.Type);
                }
                catch (Exception ex) { Main.Verbose($"[Maneuver] {target.CharacterName}: immunities unreadable: {ex.Message}"); }
                if (ManeuverImmunities.Count > 256) ManeuverImmunities.Clear();
                ManeuverImmunities[target] = known = new KeyValuePair<int, int>(frame, mask);
            }
            return ValueModel.ManeuverBlocked(known.Value, (int)type);
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
                // A swarm's ManeuverImmunity makes the grab fail: no hold and no routine cut short (fusion2g1b review).
                if (ImmuneToManeuver(target, CombatManeuver.Grapple)) return null;
                ManeuverOdds(unit, target, null, GrappleManeuver, out opening.Success, out _);
                // The holder's roll each round: the same rule with the holder buff's bonus (RuleCombatManeuver adds it).
                ManeuverValues(unit, target, null, GrappleManeuver, out int cmb, out int cmd);
                opening.Maintain = ValueModel.ManeuverRollChance(cmb + grab.MaintainBonus, cmd, unit.Descriptor.State.Features.AlwaysChance) *
                                   ManeuverConcealmentShare(unit, target);
                opening.BreakFree = BreakFreeChance(target, unit, grab.HolderDexterity);
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

        /// <summary>★ G2: the hold a unit keeps now, as the game's parts and its holder buff give it.</summary>
        internal sealed class HeldGrapple
        {
            public UnitEntityData Victim;
            public bool Pinned;
            /// <summary>The holder's roll at the next round's start (ShifterGrabInitiatorBuff.HandleNewRound).</summary>
            public float Maintain;
            /// <summary>The victim's own try to break free in that round.</summary>
            public float BreakFree;
            /// <summary>The free damage-only natural attack each maintained round (UnitGrappleInitiatorAttack).</summary>
            public float FreeDamage;
        }

        /// <summary>
        /// ★ G2: the grapple <paramref name="holder"/> holds now, or null: no pair the controller keeps, or a holder buff the
        /// G1 reader does not know (the old forecast stays). The odds are the native rules as the grab's: the holder buff's
        /// CMBBonusForManeuver added by hand (a RuleCombatManeuver handler), its Dexterity change already in the holder's
        /// Dexterity (a live modifier now). Read once a decision by the release appraisal; no roll and no change.
        /// </summary>
        private static readonly FieldInfo s_ConditionCounts = HarmonyLib.AccessTools.Field(typeof(UnitState), "m_Conditions");

        /// <summary>
        /// ★ v0.3.70 release review: whether <paramref name="holder"/> could walk once it lets go - the native
        /// UnitState.CanMove test with this hold's own CantMove taken away. UnitPartGrappleInitiator.Init adds exactly one
        /// CantMove (the grab forms' holder buffs add none) and its dispose removes it. Not when another source also bans
        /// movement (a second CantMove, MovementBan, ForceMove, helpless, dazed, stunned, prone, held itself), or the holder
        /// is overloaded (the encumbrance setting is not read: assumed to bind). An animation that holds it still now is
        /// the hold's own and ends with it. Unreadable: false, so the hold is priced as before.
        /// </summary>
        internal static bool WalksOnRelease(UnitEntityData holder)
        {
            try
            {
                var state = holder?.Descriptor?.State;
                if (state == null || holder.Get<UnitPartGrappleInitiator>() == null) return false;
                if (state.CanMove) return true;
                if (holder.Get<UnitPartGrappleTarget>() != null || state.IsHelpless || state.Prone.Active ||
                    state.HasCondition(UnitCondition.Dazed) || state.HasCondition(UnitCondition.Stunned) ||
                    state.HasCondition(UnitCondition.MovementBan) || state.HasCondition(UnitCondition.ForceMove) ||
                    holder.Descriptor.Encumbrance == Encumbrance.Overload)
                    return false;
                return s_ConditionCounts?.GetValue(state) is sbyte[] counts && (int)UnitCondition.CantMove < counts.Length &&
                       counts[(int)UnitCondition.CantMove] <= 1;
            }
            catch
            {
                return false;
            }
        }

        internal static HeldGrapple ReadHold(UnitEntityData holder)
        {
            try
            {
                var victim = holder?.Get<UnitPartGrappleInitiator>()?.Target.Value;
                var held = victim?.Get<UnitPartGrappleTarget>();
                if (held == null || held.Initiator != holder || !victim.Descriptor.State.IsConscious) return null;
                BlueprintBuff holderBuff = null;
                foreach (var buff in holder.Buffs)
                    if (buff?.Blueprint != null && buff.Active && buff.Blueprint.GetComponent<ShifterGrabInitiatorBuff>() != null)
                    { holderBuff = buff.Blueprint; break; }
                if (!HolderTerms(holderBuff, out int bonus, out _, out _)) return null;
                ManeuverValues(holder, victim, null, GrappleManeuver, out int cmb, out int cmd);
                var result = new HeldGrapple
                {
                    Victim = victim,
                    Pinned = held.IsPinned,
                    Maintain = ValueModel.ManeuverRollChance(cmb + bonus, cmd, holder.Descriptor.State.Features.AlwaysChance) *
                               ManeuverConcealmentShare(holder, victim),
                    BreakFree = BreakFreeChance(victim, holder, 0)
                };
                var routine = ReadRoutineCore(holder, victim, true, 0);
                if (routine != null)
                {
                    float free = 0f;
                    int natural = 0;
                    foreach (var line in routine.Lines)
                        if (line.Weapon?.Blueprint?.IsNatural == true) { free += line.GameHit; natural++; }
                    result.FreeDamage = natural > 0 ? free / natural : 0f;
                }
                return result;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Grapple] {holder?.CharacterName}: hold unreadable: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The victim's try to break free (UnitHelper.TryBreakFree with BreakFreeFlags.Default, as UnitGrappleController calls
        /// it): against the holder's CMD, by a grapple maneuver when its CMB beats its Mobility and Athletics, else by the better
        /// of those skills. The holder's CMD while it holds: the holder buff's Dexterity change taken into its Dexterity bonus.
        /// </summary>
        private static float BreakFreeChance(UnitEntityData victim, UnitEntityData holder, int holderDexterityChange)
        {
            ManeuverValues(victim, holder, null, GrappleManeuver, out int cmb, out int cmd);
            int dexterity = holder.Stats.Dexterity.ModifiedValue;
            cmd += DexterityBonus(dexterity + holderDexterityChange) - DexterityBonus(dexterity);
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
                var victimPart = victim?.Get<UnitPartGrappleTarget>();
                // Compare the reciprocal UnitReference directly, as the native controller and ReadHold do. The nullable
                // expression instead converts it back to an entity for equality, requiring another proxy resolution.
                if (victim != null && victimPart != null && victimPart.Initiator == unit && victim.Descriptor.State.IsConscious)
                    return "holds " + victim.CharacterName;
                var held = unit?.Get<UnitPartGrappleTarget>();
                var holder = held?.Initiator.Value;
                var holderPart = holder?.Get<UnitPartGrappleInitiator>();
                if (holder != null && holderPart != null && holderPart.Target == unit && holder.Descriptor.State.IsConscious)
                    return "held by " + holder.CharacterName + (held.IsPinned ? " (pinned)" : "");
            }
            catch { }
            return null;
        }
    }
}
