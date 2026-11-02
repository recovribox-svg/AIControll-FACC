// ★ user-ability-blacklist (docs/TASK_USER_ABILITY_BLACKLIST.md): the "Abilities AI must not use" lists as they are saved
// in settings.json, and the rules that keep them valid. Game-independent (unit-tested: tests/CompanionAI.Tests).
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Settings
{
    /// <summary>
    /// One ability or toggle AI Control must not start by itself. Identified by the blueprint GUID only: the same spell from
    /// another spellbook, with metamagic, or from a scroll or wand shares it. <see cref="Name"/> is what the list shows when
    /// the ability is not on the character any more (respec, item gone, mod removed).
    /// </summary>
    public sealed class AbilityBlockEntry
    {
        public const string AbilityKind = "Ability";
        public const string ToggleKind = "Toggle";

        /// <summary>The blueprint GUID, 32 lower-case hex digits once normalised.</summary>
        public string Guid { get; set; }
        /// <summary>The name when it was blocked (for display only, never for matching).</summary>
        public string Name { get; set; }
        /// <summary><see cref="AbilityKind"/> or <see cref="ToggleKind"/>. Text, so an unknown value cannot break loading.</summary>
        public string Kind { get; set; }
    }

    public static class AbilityBlocklist
    {
        /// <summary>The GUID in the game's own text form (Guid "N": 32 lower-case hex digits), or null when it is not one.</summary>
        public static string NormalizeGuid(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!System.Guid.TryParse(text.Trim(), out var guid) || guid == System.Guid.Empty) return null;
            return guid.ToString("N");
        }

        /// <summary>
        /// A list as loaded from settings.json, made valid: a missing list becomes empty, an entry without a valid GUID is
        /// left out (and named in <paramref name="rejected"/>), duplicates become one entry (the first, with the first name
        /// found), names are trimmed and the kind is one of the two known ones. Entries the game cannot find are kept.
        /// </summary>
        public static List<AbilityBlockEntry> Normalize(List<AbilityBlockEntry> entries, List<string> rejected = null)
        {
            var result = new List<AbilityBlockEntry>();
            if (entries == null) return result;
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    rejected?.Add("(empty entry)");
                    continue;
                }
                string guid = NormalizeGuid(entry.Guid);
                string name = entry.Name?.Trim() ?? "";
                if (guid == null)
                {
                    rejected?.Add(string.IsNullOrEmpty(name) ? $"'{entry.Guid}'" : $"{name} ('{entry.Guid}')");
                    continue;
                }
                if (index.TryGetValue(guid, out int at))
                {
                    if (string.IsNullOrEmpty(result[at].Name) && name.Length > 0) result[at].Name = name;
                    continue;
                }
                index[guid] = result.Count;
                result.Add(new AbilityBlockEntry
                {
                    Guid = guid,
                    Name = name,
                    Kind = string.Equals(entry.Kind, AbilityBlockEntry.ToggleKind, StringComparison.OrdinalIgnoreCase)
                        ? AbilityBlockEntry.ToggleKind : AbilityBlockEntry.AbilityKind
                });
            }
            return result;
        }

        /// <summary>Whether the list names this GUID (any text form).</summary>
        public static bool Contains(List<AbilityBlockEntry> list, string guid)
        {
            string key = NormalizeGuid(guid);
            if (key == null || list == null) return false;
            foreach (var entry in list)
                if (entry != null && entry.Guid == key) return true;
            return false;
        }

        /// <summary>Adds an entry; false when the GUID is not valid or already listed (the list is unchanged).</summary>
        public static bool Add(List<AbilityBlockEntry> list, string guid, string name, string kind)
        {
            string key = NormalizeGuid(guid);
            if (key == null || list == null || Contains(list, key)) return false;
            list.Add(new AbilityBlockEntry
            {
                Guid = key,
                Name = name?.Trim() ?? "",
                Kind = kind == AbilityBlockEntry.ToggleKind ? AbilityBlockEntry.ToggleKind : AbilityBlockEntry.AbilityKind
            });
            return true;
        }

        /// <summary>Removes the entry with this GUID; false when there was none.</summary>
        public static bool Remove(List<AbilityBlockEntry> list, string guid)
        {
            string key = NormalizeGuid(guid);
            if (key == null || list == null) return false;
            return list.RemoveAll(e => e != null && e.Guid == key) > 0;
        }
    }
}
