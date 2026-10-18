// ★ v0.3.14: one ability discovery for both combat modes (was TacticalEngine.GatherAbilities in turn-based and a
// separate SituationAnalyzer.GetAvailableAbilities in real time). Real time deduplicated by blueprint GUID only, so a
// metamagic copy or a second spellbook's copy of a spell vanished whenever the plain one existed, a wand or scroll of a
// known spell hid the spell itself, and variant spells (kineticist blasts, Command, Bestow Curse...) were never
// expanded - their parents are blacklisted as "not directly castable" - so real time never used them.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;

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

            void Add(AbilityData a)
            {
                if (a?.Blueprint == null) return;
                // Variant parents cannot be cast ("Can't cast spell with variants"): use their variants instead.
                if (a.Blueprint.HasVariants)
                {
                    try
                    {
                        foreach (var conv in a.GetConversions().ToList())
                            if (conv?.Blueprint != null && a.Blueprint.HasVariant(conv.Blueprint)) Add(conv);
                    }
                    catch (Exception ex)
                    {
                        Main.Verbose($"[Abilities] variants of {a.Name}: {ex.Message}");
                    }
                    return;
                }
                if (seen.Add(Key(a))) result.Add(a);
            }

            try
            {
                foreach (var ability in unit.Abilities.Enumerable)
                    Add(ability?.Data);
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
            return result;
        }

        /// <summary>What makes two AbilityData different options: blueprint, spellbook, source item, metamagic.</summary>
        public static string Key(AbilityData a) =>
            $"{a.Blueprint.AssetGuid}|{a.Spellbook?.Blueprint?.AssetGuid}|{a.SourceItem?.UniqueId}|{MetamagicTag(a)}";

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
