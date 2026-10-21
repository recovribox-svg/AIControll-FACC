// ★ v0.3.14: one ability discovery for both combat modes (was TacticalEngine.GatherAbilities in turn-based and a
// separate SituationAnalyzer.GetAvailableAbilities in real time). Real time deduplicated by blueprint GUID only, so a
// metamagic copy or a second spellbook's copy of a spell vanished whenever the plain one existed, a wand or scroll of a
// known spell hid the spell itself, and variant spells (kineticist blasts, Command, Bestow Curse...) were never
// expanded - their parents are blacklisted as "not directly castable" - so real time never used them.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Class.Kineticist;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class AbilityDiscovery
    {
        /// <summary>
        /// Every ability the unit could use: its facts (class features, items in quick slots), then each spellbook's
        /// known, metamagic (custom) and special spells. Variant parents are replaced by their variants. Not filtered by
        /// availability, consumable settings or blacklists: each engine applies those.
        /// </summary>
        public static List<AbilityData> Gather(UnitEntityData unit)
        {
            var seen = new HashSet<string>();
            var result = new List<AbilityData>();
            if (unit == null) return result;
            var internals = KineticBladeInternals(unit);
            var leftOut = new List<string>();

            void Add(AbilityData a) => AddOne(a, true);

            void AddOne(AbilityData a, bool withConversions)
            {
                if (a?.Blueprint == null) return;
                // ★ v0.3.15: abilities only the game itself uses. Orvath's Kinetic Blade carries its own "Fire Blast" (the
                // damage the blade deals when its weapon hits, so no attack roll - valued 9.1 against the real blast's
                // 8.7); cast on its own it does nothing. 6 of 6 Fire Blasts in the last fight were this one.
                if (internals.Contains(a.Blueprint) || a.Blueprint.GetComponent<AbilityKineticBlade>() != null)
                {
                    leftOut.Add(a.Name);
                    return;
                }
                // Variant parents cannot be cast ("Can't cast spell with variants"): use their variants instead.
                if (a.Blueprint.HasVariants)
                {
                    try
                    {
                        // ★ v0.3.15: only the variants the game offers the player (MechanicActionBarSlotAbility
                        // .GetConvertedAbilityData keeps those whose IsVisible() is true).
                        foreach (var conv in a.GetConversions().ToList())
                        {
                            if (conv?.Blueprint == null || !a.Blueprint.HasVariant(conv.Blueprint)) continue;
                            if (conv.IsVisible()) Add(conv);
                            else leftOut.Add(conv.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Main.Verbose($"[Abilities] variants of {a.Name}: {ex.Message}");
                    }
                    return;
                }
                if (seen.Add(Key(a))) result.Add(a);
                if (withConversions) AddConversions(a);
            }

            // ★ v0.3.18 (AA-1): the conversions the action bar offers for this ability (MechanicActionBarSlotSpell /
            // MechanicActionBarSlotAbility.GetConvertedAbilityData): spontaneous cure and inflict from a prepared slot,
            // shadow spells, Tabletop Tweaks' Quick Channel and Quicken Blessing. They were dropped, so a cleric with no
            // Cure prepared could not heal. Variants are handled above; slot restores and slot-to-resource conversions
            // (Arcane Reservoir points, Pearl-of-Power-like abilities) are bookkeeping, not actions. A spell's conversions
            // are all offered; an ability's only those the game shows. Equivalent conversions (the same spell from two
            // prepared spells of one level) are one option, the one that can be cast now when there is one.
            void AddConversions(AbilityData a)
            {
                List<AbilityData> conversions;
                try { conversions = a.GetConversions().ToList(); }
                catch (Exception ex)
                {
                    Main.Verbose($"[Abilities] conversions of {a.Name}: {ex.Message}");
                    return;
                }
                foreach (var conv in conversions)
                {
                    if (conv?.Blueprint == null || conv.Blueprint == a.Blueprint || a.Blueprint.HasVariant(conv.Blueprint)) continue;
                    if (conv.Blueprint.GetComponent<AbilityRestoreSpellSlot>() != null ||
                        conv.Blueprint.GetComponent<AbilityConvertSpellLevelToResource>() != null) continue;
                    if (a.Spellbook == null && !conv.IsVisible()) { leftOut.Add(conv.Name); continue; }
                    string key = ConversionKey(conv);
                    int index = result.FindIndex(x => x.ConvertedFrom != null && ConversionKey(x) == key);
                    if (index >= 0)
                    {
                        if (!result[index].IsAvailable && conv.IsAvailable) result[index] = conv;
                        continue;
                    }
                    if (seen.Contains(Key(conv))) continue;
                    AddOne(conv, false);
                }
            }

            try
            {
                foreach (var ability in unit.Abilities.Enumerable)
                {
                    if (ability == null) continue;
                    // ★ v0.3.15: the action bar leaves out hidden abilities (ActionBarVM.CollectAbilities: !ability.Hidden).
                    // Ability.Hidden is also true for every item's ability (items have their own slots), so for those only
                    // the blueprint's own flag and visibility rules count.
                    // A held touch charge (UnitPartTouch adds it as a fact) is always the caster's to deliver.
                    bool item = ability.SourceItem is ItemEntityUsable;
                    bool hidden = ability.Data.StickyTouch == null &&
                                  (item ? ability.Blueprint.Hidden || !ability.Data.IsVisible() : ability.Hidden);
                    if (hidden)
                    {
                        leftOut.Add(ability.Name);
                        continue;
                    }
                    Add(ability.Data);
                }
            }
            catch (Exception ex)
            {
                ReportOnce($"facts|{unit.UniqueId}", $"[Abilities] {unit.CharacterName}: abilities unreadable - {ex.Message}");
            }

            foreach (var book in unit.Spellbooks ?? Enumerable.Empty<Kingmaker.UnitLogic.Spellbook>())
            {
                // ★ v0.3.12: a failing spell list is reported (once per book and list) instead of silently skipped.
                string bookId = book?.Blueprint?.AssetGuid.ToString();
                try { foreach (var s in book.GetAllKnownSpells()) Add(s); }
                catch (Exception ex) { ReportOnce($"book|{bookId}|known", $"[Abilities] {book?.Blueprint?.name}: known spells unreadable - {ex.Message}"); }
                try { foreach (var s in book.GetAllCustomSpells()) Add(s); }
                catch (Exception ex) { ReportOnce($"book|{bookId}|custom", $"[Abilities] {book?.Blueprint?.name}: metamagic spells unreadable - {ex.Message}"); }
                for (int level = 0; level <= 10; level++)
                {
                    try { foreach (var s in book.GetSpecialSpells(level)) Add(s); }
                    catch (Exception ex) { ReportOnce($"book|{bookId}|special{level}", $"[Abilities] {book?.Blueprint?.name}: special spells L{level} unreadable - {ex.Message}"); }
                }
            }
            if (leftOut.Count > 0)
                ReportOnce($"hidden|{unit.UniqueId}|{leftOut.Count}",
                           $"[Abilities] {unit.CharacterName}: left out {leftOut.Count} the game does not offer to the player: " +
                           string.Join(", ", leftOut.Distinct().Take(12)));
            return result;
        }

        /// <summary>
        /// ★ v0.3.15: the abilities behind the unit's kinetic blades (WeaponKineticBlade: the blade's activation and the
        /// blast it deals on a hit), found as the game's own blade toggle finds them (AddKineticistBlade on the
        /// activatable's buff). The game triggers them itself when the blade attacks.
        /// </summary>
        private static HashSet<BlueprintAbility> KineticBladeInternals(UnitEntityData unit)
        {
            var set = new HashSet<BlueprintAbility>();
            try
            {
                void AddBlade(WeaponKineticBlade blade)
                {
                    if (blade == null) return;
                    if (blade.Blast != null) set.Add(blade.Blast);
                    if (blade.ActivationAbility != null) set.Add(blade.ActivationAbility);
                }
                foreach (var toggle in unit.ActivatableAbilities.Enumerable)
                    AddBlade(toggle?.Blueprint?.Buff?.GetComponent<AddKineticistBlade>()?.Blade?.GetComponent<WeaponKineticBlade>());
                AddBlade(unit.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.GetComponent<WeaponKineticBlade>());
                AddBlade(unit.Body?.SecondaryHand?.MaybeWeapon?.Blueprint?.GetComponent<WeaponKineticBlade>());
            }
            catch (Exception ex)
            {
                ReportOnce($"blade|{unit.UniqueId}", $"[Abilities] {unit.CharacterName}: kinetic blade abilities unreadable - {ex.Message}");
            }
            return set;
        }

        /// <summary>
        /// What makes two AbilityData different options: blueprint, spellbook, source item, metamagic.
        /// ★ v0.3.18: and, for a conversion, that it is one: it spends another spell's slot (a known Cure and a Cure
        /// converted from Bless are both options).
        /// </summary>
        public static string Key(AbilityData a) =>
            $"{a.Blueprint.AssetGuid}|{a.Spellbook?.Blueprint?.AssetGuid}|{a.SourceItem?.UniqueId}|{MetamagicTag(a)}" +
            (a.ConvertedFrom != null ? $"|conv{SafeLevel(a.ConvertedFrom)}" : "");

        /// <summary>★ v0.3.18: conversions of one spell from slots of one level are the same option.</summary>
        private static string ConversionKey(AbilityData a) =>
            $"{a.Blueprint.AssetGuid}|{a.Spellbook?.Blueprint?.AssetGuid}|{MetamagicTag(a)}|{SafeLevel(a.ConvertedFrom ?? a)}";

        private static int SafeLevel(AbilityData a)
        {
            try { return a.Spellbook?.GetSpellLevel(a) ?? a.SpellLevel; }
            catch { return -1; }
        }

        /// <summary>★ v0.3.7: metamagic and heightening make a different spell (cost, slot level): keep them apart.</summary>
        public static string MetamagicTag(AbilityData ability)
        {
            var data = ability?.MetamagicData;
            if (data == null || !data.NotEmpty && data.HeightenLevel == 0) return "";
            return $"[{data.MetamagicMask}{(data.HeightenLevel > 0 ? "+H" + data.HeightenLevel : "")}]";
        }

        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>★ v0.3.12: one normal log line per key (bounded), for failures that remove an option silently.</summary>
        public static void ReportOnce(string key, string message)
        {
            if (Reported.Count > 1024) Reported.Clear();
            if (Reported.Add(key)) Main.Log(message);
        }
    }
}
