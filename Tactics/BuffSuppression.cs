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

        /// <summary>C1 for a recipient's whole buff list: read its part and the cast metadata once. Keep the original
        /// list when nothing is suppressed; allocate a replacement only at the first suppressed buff.</summary>
        internal static List<BlueprintBuff> FilterOnArrival(AbilityData ability, List<BlueprintBuff> buffs,
                                                           UnitEntityData recipient, out BlueprintBuff firstSuppressed)
        {
            firstSuppressed = null;
            if (buffs == null || buffs.Count == 0 || recipient == null) return buffs;
            try
            {
                var part = recipient.Get<UnitPartBuffSuppress>();
                if (part == null || !CanRead()) return buffs;
                return Filter(part, buffs, ability?.Blueprint?.SpellDescriptor ?? SpellDescriptor.None,
                              ability?.SpellSchool ?? SpellSchool.None, out firstSuppressed);
            }
            catch (Exception ex)
            {
                firstSuppressed = null;
                Main.Verbose($"[Buff] suppression on {recipient.CharacterName}: {ex.Message}");
                return buffs;
            }
        }

        // Also exercised by the actor-free audit with a part filled through the native PrepareForSuppression API.
        private static List<BlueprintBuff> Filter(UnitPartBuffSuppress part, List<BlueprintBuff> buffs,
                                                  SpellDescriptor parent, SpellSchool school, out BlueprintBuff first)
        {
            first = null;
            List<BlueprintBuff> kept = null;
            for (int i = 0; i < buffs.Count; i++)
            {
                var buff = buffs[i];
                if (buff != null && Suppresses(part, buff, ContextDescriptor(buff, parent), ContextSchool(buff, school)))
                {
                    if (kept == null)
                    {
                        first = buff;
                        kept = new List<BlueprintBuff>(buffs.Count - 1);
                        for (int j = 0; j < i; j++) kept.Add(buffs[j]);
                    }
                }
                else kept?.Add(buff);
            }
            return kept ?? buffs;
        }

        private static bool CanRead()
        {
            if (Readable) return true;
            if (!s_UnreadableNoted)
            {
                s_UnreadableNoted = true;
                Main.Log("[Buff] the game's buff suppression part could not be read; suppressed buffs are not recognized");
            }
            return false;
        }

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
            if (!CanRead()) return false;
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
        internal static SpellDescriptor ContextDescriptor(BlueprintScriptableObject buff, SpellDescriptor parent)
        {
            var own = buff.GetComponent<SpellDescriptorComponent>();
            return own != null ? parent | own.Descriptor.Value : parent;
        }

        /// <summary>The buff context's school: its SpellComponent, else the parent's, else its AddSpellSchool (MechanicsContext).</summary>
        internal static SpellSchool ContextSchool(BlueprintScriptableObject buff, SpellSchool parent)
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
        /// C2 / F3: the share of removing a harmful <paramref name="buff"/> that falls within <paramref name="horizon"/>
        /// rounds (SuppressionRules.ActiveShare): 1 when it is not suppressed, the rounds it acts after its suppressor ends
        /// otherwise. A suppressed one with no suppressor found keeps all of it (unknown, as before).
        /// </summary>
        internal static float ActiveShare(Buff buff, float horizon)
        {
            if (buff == null || !buff.IsSuppressed) return 1f;
            try
            {
                float left = buff.IsPermanent ? float.PositiveInfinity : (float)buff.TimeLeft.TotalSeconds / 6f;
                return SuppressionRules.ActiveShare(true, left, SuppressedSeconds(buff) / 6f, horizon);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>
        /// How long <paramref name="buff"/> stays suppressed: 0 when it is not, the longest time left of the facts that
        /// suppress it (<see cref="float.PositiveInfinity"/> for a permanent one), NaN when none is found.
        /// </summary>
        internal static float SuppressedSeconds(Buff buff)
        {
            if (buff == null || !buff.IsSuppressed) return 0f;
            try
            {
                var unit = buff.Owner?.Unit;
                if (unit == null) return float.NaN;
                float suppressor = float.NaN;
                foreach (var fact in Suppressors(unit, buff.Blueprint, buff.Context?.SpellDescriptor ?? SpellDescriptor.None,
                                                 buff.Context?.SpellSchool ?? SpellSchool.None))
                {
                    float lasts = fact is Buff b && !b.IsPermanent ? (float)b.TimeLeft.TotalSeconds : float.PositiveInfinity;
                    suppressor = float.IsNaN(suppressor) ? lasts : Math.Max(suppressor, lasts);
                }
                return suppressor;
            }
            catch
            {
                return float.NaN;
            }
        }
    }
}
