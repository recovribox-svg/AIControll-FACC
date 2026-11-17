// ★ suppressed buffs (17 November 2026; docs/BURST_AIM_AND_TACTICS_PLAN.md §5, slice C): the game side of the native buff
// suppression rule (UnitPartBuffSuppress). Polymorph forms (wild shape, Shifter forms, Elemental Body, dragon forms) suppress
// Enlarge/Reduce Person, Haste, Displacement, Mirror Image, Mage Armor, rages and more; Ameliorating hexes and similar
// effects suppress harmful conditions. A buff suppressed as it arrives does nothing (C1); a harmful buff already suppressed
// does nothing until its suppressor ends (C2). Pure rules: SuppressionRules.
// Read only: the part's four sets through cached field references; no buff is created, attached or released.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    internal static class BuffSuppression
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<UnitPartBuffSuppress, MultiSet<BlueprintBuff>> ListedRef =
            Ref<MultiSet<BlueprintBuff>>("m_Buffs");
        private static readonly HarmonyLib.AccessTools.FieldRef<UnitPartBuffSuppress, MultiSet<BlueprintBuff>> ExcludedRef =
            Ref<MultiSet<BlueprintBuff>>("m_ExcludeBuffs");
        private static readonly HarmonyLib.AccessTools.FieldRef<UnitPartBuffSuppress, MultiSet<SpellDescriptor>> DescriptorsRef =
            Ref<MultiSet<SpellDescriptor>>("m_SpellDescriptors");
        private static readonly HarmonyLib.AccessTools.FieldRef<UnitPartBuffSuppress, MultiSet<SpellSchool>> SchoolsRef =
            Ref<MultiSet<SpellSchool>>("m_SpellSchools");

        /// <summary>The single descriptor flags, as the native part splits a descriptor (UnitPartBuffSuppress.GetValues).</summary>
        private static readonly SpellDescriptor[] Flags = Enum.GetValues(typeof(SpellDescriptor)).Cast<SpellDescriptor>()
            .Where(v => v != SpellDescriptor.None).ToArray();

        private static HarmonyLib.AccessTools.FieldRef<UnitPartBuffSuppress, T> Ref<T>(string field)
        {
            try { return HarmonyLib.AccessTools.FieldRefAccess<UnitPartBuffSuppress, T>(field); }
            catch { return null; }
        }

        /// <summary>The fields can be read (an update of the game that renamed them disables the rule, logged once).</summary>
        internal static bool Readable => ListedRef != null && ExcludedRef != null && DescriptorsRef != null && SchoolsRef != null;

        private static bool s_UnreadableNoted;

        /// <summary>
        /// C1: <paramref name="buff"/>, applied by <paramref name="ability"/>, would be suppressed on <paramref name="recipient"/>
        /// the moment it is attached (Buff.OnAttach asks the part). Its context takes the ability's descriptor plus its own, and
        /// its own school, else the ability's, else its AddSpellSchool (MechanicsContext).
        /// </summary>
        internal static bool OnArrival(AbilityData ability, BlueprintBuff buff, UnitEntityData recipient)
        {
            SpellDescriptor parent = SpellDescriptor.None;
            SpellSchool school = SpellSchool.None;
            try
            {
                if (ability?.Blueprint != null)
                {
                    // AbilityExecutionContext: the blueprint's descriptor component; the ability's school (a fusion's own).
                    parent = ability.Blueprint.SpellDescriptor;
                    school = ability.SpellSchool;
                }
            }
            catch { }
            return OnArrival(buff, recipient, parent, school);
        }

        /// <summary>C1 with the parent context's descriptor and school given (a toggle's own buff: none).</summary>
        internal static bool OnArrival(BlueprintBuff buff, UnitEntityData recipient, SpellDescriptor parentDescriptor, SpellSchool parentSchool)
        {
            if (buff == null || recipient == null) return false;
            UnitPartBuffSuppress part;
            try { part = recipient.Get<UnitPartBuffSuppress>(); }
            catch { return false; }
            if (part == null) return false;
            if (!Readable)
            {
                if (!s_UnreadableNoted)
                {
                    s_UnreadableNoted = true;
                    Main.Log("[Buff] the game's buff suppression part could not be read; suppressed buffs are not recognized");
                }
                return false;
            }
            try
            {
                return Suppresses(part, buff, ContextDescriptor(buff, parentDescriptor), ContextSchool(buff, parentSchool));
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Buff] suppression of {buff.name} on {recipient.CharacterName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>The buff context's descriptor: the parent's plus the buff's own component (MechanicsContext).</summary>
        private static SpellDescriptor ContextDescriptor(BlueprintBuff buff, SpellDescriptor parent)
        {
            var own = buff.GetComponent<SpellDescriptorComponent>();
            return own != null ? parent | own.Descriptor.Value : parent;
        }

        /// <summary>The buff context's school: its SpellComponent, else the parent's, else its AddSpellSchool (MechanicsContext).</summary>
        private static SpellSchool ContextSchool(BlueprintBuff buff, SpellSchool parent)
        {
            var spell = buff.GetComponent<SpellComponent>();
            SpellSchool school = spell != null ? spell.School : parent;
            if (school == SpellSchool.None)
                school = buff.GetComponent<Kingmaker.UnitLogic.Buffs.Components.AddSpellSchool>()?.School ?? SpellSchool.None;
            return school;
        }

        /// <summary>The native test (UnitPartBuffSuppress.IsSuppressed) for a buff with this blueprint, descriptor and school.</summary>
        private static bool Suppresses(UnitPartBuffSuppress part, BlueprintBuff buff, SpellDescriptor descriptor, SpellSchool school)
        {
            bool descriptorSuppressed = false;
            if (descriptor != SpellDescriptor.None)
            {
                var set = DescriptorsRef(part);
                foreach (var flag in Flags)
                    if ((descriptor & flag) != 0 && set.Contains(flag)) { descriptorSuppressed = true; break; }
            }
            return SuppressionRules.Suppressed(ExcludedRef(part).Contains(buff), ListedRef(part).Contains(buff), descriptorSuppressed,
                                               SchoolsRef(part).Contains(school));
        }

        /// <summary>The facts on <paramref name="unit"/> whose SuppressBuffs components suppress this buff.</summary>
        private static IEnumerable<Kingmaker.EntitySystem.EntityFact> Suppressors(UnitEntityData unit, BlueprintBuff buff,
                                                                                SpellDescriptor descriptor, SpellSchool school)
        {
            foreach (var fact in unit.Facts.List)
            {
                if (!(fact?.Blueprint is BlueprintScriptableObject blueprint) || !fact.Active) continue;
                foreach (var component in blueprint.GetComponents<SuppressBuffs>())
                {
                    if (component.ExcludedBuffs.Contains(buff)) continue;
                    bool hits = component.Buffs.Contains(buff) ||
                                component.Descriptor.Value != SpellDescriptor.None && (component.Descriptor.Value & descriptor) != 0 ||
                                component.Schools != null && component.Schools.Contains(school);
                    if (hits) { yield return fact; break; }
                }
            }
        }

        /// <summary>For the decision log: what suppresses <paramref name="buff"/> on <paramref name="unit"/> (its name), or "its form".</summary>
        internal static string SuppressorName(AbilityData ability, BlueprintBuff buff, UnitEntityData unit)
        {
            try
            {
                var descriptor = ContextDescriptor(buff, ability?.Blueprint?.SpellDescriptor ?? SpellDescriptor.None);
                var school = ContextSchool(buff, ability?.SpellSchool ?? SpellSchool.None);
                var fact = Suppressors(unit, buff, descriptor, school).FirstOrDefault();
                if (fact != null) return fact.Name ?? fact.Blueprint?.name ?? "a suppressing effect";
            }
            catch { }
            return "a suppressing effect";
        }

        /// <summary>
        /// C2: a harmful <paramref name="buff"/> on its bearer is worth removing now: it is not suppressed, or it would outlast
        /// what suppresses it. A suppressed one with no suppressor found counts as before (unknown).
        /// </summary>
        internal static bool StillMatters(Buff buff)
        {
            if (buff == null || !buff.IsSuppressed) return true;
            try
            {
                var unit = buff.Owner?.Unit;
                if (unit == null) return true;
                float left = buff.IsPermanent ? float.PositiveInfinity : (float)buff.TimeLeft.TotalSeconds;
                float suppressor = float.NaN;
                foreach (var fact in Suppressors(unit, buff.Blueprint, buff.Context?.SpellDescriptor ?? SpellDescriptor.None,
                                                 buff.Context?.SpellSchool ?? SpellSchool.None))
                {
                    float lasts = fact is Buff b && !b.IsPermanent ? (float)b.TimeLeft.TotalSeconds : float.PositiveInfinity;
                    suppressor = float.IsNaN(suppressor) ? lasts : Math.Max(suppressor, lasts);
                }
                return SuppressionRules.StillMatters(true, left, suppressor);
            }
            catch
            {
                return true;
            }
        }
    }
}
