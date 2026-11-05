// ★ user-ability-blacklist (docs/TASK_USER_ABILITY_BLACKLIST.md): the "Abilities AI must not use" panels - the shared list
// at the top level, and per character a catalogue of its abilities and toggles with Block / Unblock at the right list.
//
//   Cost: closed, a panel reads two list counts. The catalogue is built when the panel opens or Refresh is pressed (one
//   AbilityDiscovery.Gather, no blueprint scan, no effect appraisal), sorted once, filtered only when the search, the filter
//   or the lists change (on a Layout event, so IMGUI's Layout and Repaint passes see the same rows), and shown a page at a
//   time. Each shown row costs two set lookups. Edits save once (ModSettings.SetBlocked).
using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using UnityEngine;
using CompanionAI_Pathfinder.Settings;
using CompanionAI_Pathfinder.Tactics;

namespace CompanionAI_Pathfinder.UI
{
    internal static class AbilityBlocklistUI
    {
        private const int PageSize = 12;
        private const float NameWidth = 380f;
        private const float StatusWidth = 220f;
        private const float ButtonHeight = 28f;

        private static string L(string key) => Localization.Get(key);

        /// <summary>One row: one policy identity (all copies of a spell, item copies included, are one row).</summary>
        private sealed class Row
        {
            public string Guid;
            public BlueprintGuid Key;
            public string Name;
            public string Kind;
            public readonly List<string> Tags = new List<string>();
            public string TagText;
            public string FamilyGuid;
            public BlueprintGuid FamilyKey;
            public string FamilyName;
            public string Search;
        }

        // The open character panel.
        private static string s_UnitId;
        private static bool s_Open;
        private static List<Row> s_Rows;
        private static HashSet<string> s_RowGuids;
        private static string s_Search = "";
        private static bool s_BlockedOnly;
        private static bool s_HideBlocked;
        private static bool s_AddShared;
        private static int s_Page;
        private static List<Row> s_View;
        private static string s_ViewSearch;
        private static bool s_ViewBlockedOnly;
        private static bool s_ViewHideBlocked;
        private static int s_ViewRevision = int.MinValue;
        private static List<AbilityBlockEntry> s_Missing = new List<AbilityBlockEntry>();

        // The shared panel.
        private static bool s_SharedOpen;
        private static int s_SharedPage;

        /// <summary>The one edit a click asked for, applied after the rows are drawn (the lists change once, outside loops).</summary>
        private struct Edit
        {
            public bool Pending, Shared, Block;
            public string Guid, Name, Kind;
        }

        private static Edit Ask(bool shared, string guid, string name, string kind, bool block) =>
            new Edit { Pending = true, Shared = shared, Guid = guid, Name = name, Kind = kind, Block = block };

        /// <summary>The shared list (top level, collapsed by default).</summary>
        internal static void DrawShared(GUIStyle box, GUIStyle bold, GUIStyle description)
        {
            var settings = ModSettings.Instance;
            if (settings == null) return;
            var list = settings.SharedBlockedAbilities ?? new List<AbilityBlockEntry>();
            GUILayout.BeginVertical(box);
            // The pass that sees the click still draws the old state (IMGUI's Layout and input passes must match).
            bool open = s_SharedOpen;
            if (GUILayout.Button($"<b>{(open ? "▼" : "▶")} {L("BL_SharedTitle")} ({list.Count})</b>", bold))
                s_SharedOpen = !s_SharedOpen;
            if (open)
            {
                GUILayout.Label($"<color=#D8D8D8><i>{L("BL_SharedDesc")}</i></color>", description);
                GUILayout.Label($"<color=#888888><size=11>{L("BL_Boundary")}</size></color>", description);
                GUILayout.Space(5);
                if (list.Count == 0) GUILayout.Label($"<color=#D8D8D8><i>{L("BL_NoShared")}</i></color>", description);
                else
                {
                    int pages = (list.Count + PageSize - 1) / PageSize;
                    s_SharedPage = Mathf.Clamp(s_SharedPage, 0, pages - 1);
                    AbilityBlockEntry remove = null;
                    for (int i = s_SharedPage * PageSize; i < Math.Min(list.Count, (s_SharedPage + 1) * PageSize); i++)
                    {
                        var entry = list[i];
                        GUILayout.BeginHorizontal("box");
                        GUILayout.Label(EntryText(entry), GUILayout.Width(NameWidth + StatusWidth));
                        if (GUILayout.Button(L("BL_Remove"), GUILayout.Width(120f), GUILayout.Height(ButtonHeight))) remove = entry;
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    DrawPager(ref s_SharedPage, pages);
                    if (remove != null) settings.SetBlocked(true, null, null, remove.Guid, remove.Name, remove.Kind, false);
                }
            }
            GUILayout.EndVertical();
        }

        /// <summary>The character's section inside its expanded settings (collapsed by default).</summary>
        internal static void DrawForCharacter(UnitEntityData unit, string unitId, string unitName, GUIStyle bold, GUIStyle description)
        {
            var settings = ModSettings.Instance;
            if (settings == null || string.IsNullOrEmpty(unitId)) return;
            if (s_UnitId != unitId)
            {
                s_UnitId = unitId;
                s_Open = false;
                s_Rows = null;
                s_View = null;
                s_Page = 0;
            }
            int own = settings.GetOrCreateSettings(unitId, unitName).BlockedAbilities?.Count ?? 0;
            int shared = settings.SharedBlockedAbilities?.Count ?? 0;
            GUILayout.Space(5);
            bool open = s_Open;   // the pass that sees the click still draws the old state
            if (GUILayout.Button($"<b>{(open ? "▼" : "▶")} {L("BL_Title")}</b> <color=#D8D8D8>({string.Format(L("BL_Count"), own, shared)})</color>", bold))
            {
                s_Open = !s_Open;
                if (s_Open) { s_Rows = null; s_View = null; }   // read afresh on opening
            }
            if (!open) return;

            GUILayout.Label($"<color=#888888><size=11>{L("BL_Boundary")}</size></color>", description);
            GUILayout.Label($"<color=#888888><size=11>{L("BL_CharacterDesc")}</size></color>", description);
            if (unit == null)
            {
                GUILayout.Label($"<color=#D8D8D8><i>{L("BL_NoUnit")}</i></color>", description);
                return;
            }

            bool layout = Event.current.type == EventType.Layout;
            if (s_Rows == null && layout) BuildRows(unit);
            if (s_Rows == null) return;

            // Search, refresh, filter.
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("BL_Search"), GUILayout.Width(70f));
            s_Search = GUILayout.TextField(s_Search ?? "", GUILayout.Width(260f)) ?? "";
            GUILayout.Space(10);
            if (GUILayout.Button(L("BL_Refresh"), GUILayout.Width(120f), GUILayout.Height(ButtonHeight))) s_Rows = null;
            GUILayout.Space(10);
            if (GUILayout.Button((s_BlockedOnly ? "☑ " : "☐ ") + L("BL_BlockedOnly"), GUI.skin.label, GUILayout.Height(ButtonHeight)))
            {
                s_BlockedOnly = !s_BlockedOnly;
                if (s_BlockedOnly) s_HideBlocked = false;
            }
            // ★ v0.3.28: catalogue view only, mutually exclusive with "Show blocked only"; neither edits a block list.
            GUILayout.Space(10);
            if (GUILayout.Button((s_HideBlocked ? "☑ " : "☐ ") + L("BL_HideBlocked"), GUI.skin.label, GUILayout.Height(ButtonHeight)))
            {
                s_HideBlocked = !s_HideBlocked;
                if (s_HideBlocked) s_BlockedOnly = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Where new blocks go.
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("BL_BlockFor"), GUILayout.Width(180f));
            if (GUILayout.Toggle(!s_AddShared, L("BL_ThisCharacter"), GUI.skin.button, GUILayout.Width(150f), GUILayout.Height(ButtonHeight))) s_AddShared = false;
            if (GUILayout.Toggle(s_AddShared, L("BL_Everyone"), GUI.skin.button, GUILayout.Width(150f), GUILayout.Height(ButtonHeight))) s_AddShared = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            int revision = UserAbilityPolicy.Revision;
            if (layout && (s_View == null || s_ViewSearch != s_Search || s_ViewBlockedOnly != s_BlockedOnly ||
                           s_ViewHideBlocked != s_HideBlocked || s_ViewRevision != revision))
                BuildView(settings, unitId, unitName, revision);
            if (s_View == null) return;

            var scope = UserAbilityPolicy.ScopeFor(unitId);
            if (s_View.Count == 0) GUILayout.Label($"<color=#D8D8D8><i>{L("BL_NoRows")}</i></color>", description);
            int pages = Math.Max(1, (s_View.Count + PageSize - 1) / PageSize);
            s_Page = Mathf.Clamp(s_Page, 0, pages - 1);
            var edit = default(Edit);
            for (int i = s_Page * PageSize; i < Math.Min(s_View.Count, (s_Page + 1) * PageSize); i++)
                DrawRow(scope, s_View[i], ref edit);
            if (s_View.Count > PageSize) DrawPager(ref s_Page, pages);

            if (s_Missing.Count > 0)
            {
                GUILayout.Space(5);
                GUILayout.Label($"<color=#D8D8D8>{L("BL_NotFound")}</color>", description);
                foreach (var entry in s_Missing)
                {
                    GUILayout.BeginHorizontal("box");
                    GUILayout.Label(EntryText(entry), GUILayout.Width(NameWidth + StatusWidth));
                    if (GUILayout.Button(L("BL_Remove"), GUILayout.Width(120f), GUILayout.Height(ButtonHeight)))
                        edit = Ask(false, entry.Guid, entry.Name, entry.Kind, false);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            }
            if (edit.Pending) settings.SetBlocked(edit.Shared, unitId, unitName, edit.Guid, edit.Name, edit.Kind, edit.Block);
        }

        private static void DrawRow(BlockScope<BlueprintGuid> scope, Row row, ref Edit edit)
        {
            var exact = scope.SourceOf(row.Key);
            var family = row.FamilyGuid != null ? scope.SourceOf(row.FamilyKey) : BlockSource.None;
            GUILayout.BeginHorizontal("box");
            string variant = row.FamilyName != null ? ", " + string.Format(L("BL_VariantOf"), row.FamilyName) : "";
            GUILayout.Label($"{(exact != BlockSource.None || family != BlockSource.None ? "<color=#FF8C69>" : "<color=#FFFFFF>")}{row.Name}</color> " +
                            $"<color=#888888><size=11>({row.TagText}{variant})</size></color>", GUILayout.Width(NameWidth));
            string status = "";
            if ((exact & BlockSource.Character) != 0) status = L("BL_BlockedHere");
            if ((exact & BlockSource.Shared) != 0) status += (status.Length > 0 ? "; " : "") + L("BL_BlockedShared");
            if (family != BlockSource.None)
                status += (status.Length > 0 ? "; " : "") + string.Format(L("BL_BlockedFamily"), row.FamilyName) +
                          $" ({((family & BlockSource.Shared) != 0 ? L("BL_Everyone") : L("BL_ThisCharacter"))})";
            GUILayout.Label($"<color=#FF8C69><size=11>{status}</size></color>", GUILayout.Width(StatusWidth));

            if (exact == BlockSource.None && family == BlockSource.None)
            {
                if (GUILayout.Button(L("BL_Block"), GUILayout.Width(110f), GUILayout.Height(ButtonHeight)))
                    edit = Ask(s_AddShared, row.Guid, row.Name, row.Kind, true);
                if (row.FamilyGuid != null && GUILayout.Button(L("BL_BlockAllVariants"), GUILayout.Width(170f), GUILayout.Height(ButtonHeight)))
                    edit = Ask(s_AddShared, row.FamilyGuid, row.FamilyName, AbilityBlockEntry.AbilityKind, true);
            }
            else
            {
                // Removal at the list that blocks it (a character's own entry cannot lift a shared one).
                if ((exact & BlockSource.Character) != 0 && GUILayout.Button(L("BL_UnblockHere"), GUILayout.Height(ButtonHeight)))
                    edit = Ask(false, row.Guid, row.Name, row.Kind, false);
                if ((exact & BlockSource.Shared) != 0 && GUILayout.Button(L("BL_UnblockShared"), GUILayout.Height(ButtonHeight)))
                    edit = Ask(true, row.Guid, row.Name, row.Kind, false);
                if ((family & BlockSource.Character) != 0 && GUILayout.Button(L("BL_UnblockFamilyHere"), GUILayout.Height(ButtonHeight)))
                    edit = Ask(false, row.FamilyGuid, row.FamilyName, AbilityBlockEntry.AbilityKind, false);
                if ((family & BlockSource.Shared) != 0 && GUILayout.Button(L("BL_UnblockFamilyShared"), GUILayout.Height(ButtonHeight)))
                    edit = Ask(true, row.FamilyGuid, row.FamilyName, AbilityBlockEntry.AbilityKind, false);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static void DrawPager(ref int page, int pages)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("BL_Prev"), GUILayout.Width(120f), GUILayout.Height(ButtonHeight)) && page > 0) page--;
            GUILayout.Label(string.Format(L("BL_Page"), page + 1, pages), GUILayout.Width(110f), GUILayout.Height(ButtonHeight));
            if (GUILayout.Button(L("BL_Next"), GUILayout.Width(120f), GUILayout.Height(ButtonHeight)) && page < pages - 1) page++;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static string EntryText(AbilityBlockEntry entry)
        {
            string name = string.IsNullOrEmpty(entry.Name) ? entry.Guid : entry.Name;
            string kind = entry.Kind == AbilityBlockEntry.ToggleKind ? L("BL_KindToggle") : L("BL_KindAbility");
            return $"{name} <color=#888888><size=11>({kind}, {entry.Guid})</size></color>";
        }

        /// <summary>The catalogue: the character's abilities as AI Control discovers them, and its visible toggles.</summary>
        private static void BuildRows(UnitEntityData unit)
        {
            var rows = new List<Row>();
            var byGuid = new Dictionary<string, Row>(StringComparer.Ordinal);
            try
            {
                foreach (var ability in AbilityDiscovery.Gather(unit))
                {
                    try { AddAbility(ability, rows, byGuid); }
                    catch (Exception ex) { Main.Verbose($"[Blocklist] catalogue {ability?.Name}: {ex.Message}"); }
                }
                foreach (var toggle in unit.ActivatableAbilities?.Enumerable ?? new List<Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility>())
                {
                    var bp = toggle?.Blueprint;
                    if (bp == null || bp.HiddenInUI) continue;
                    string guid = bp.AssetGuid.ToString();
                    if (byGuid.ContainsKey(guid)) continue;
                    var row = new Row { Guid = guid, Key = bp.AssetGuid, Name = NameOf(toggle.Name, bp.name), Kind = AbilityBlockEntry.ToggleKind };
                    row.Tags.Add(L("BL_KindToggle"));
                    byGuid[guid] = row;
                    rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                Main.Error($"[Blocklist] {unit.CharacterName}: ability list unreadable - {ex.Message}");
            }
            foreach (var row in rows)
            {
                row.TagText = string.Join(", ", row.Tags);
                row.Search = (row.Name + " " + row.FamilyName).ToLowerInvariant();
            }
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            s_Rows = rows;
            s_RowGuids = new HashSet<string>(byGuid.Keys, StringComparer.Ordinal);
            s_View = null;
            s_Page = 0;
        }

        private static void AddAbility(AbilityData ability, List<Row> rows, Dictionary<string, Row> byGuid)
        {
            if (ability?.Blueprint == null) return;
            // A held touch charge is its spell (the policy matches it that way).
            var identity = ability.StickyTouch ?? ability;
            var bp = identity.Blueprint;
            if (bp == null) return;
            string tag = ability.StickyTouch != null ? L("BL_KindCharge")
                       : ability.SourceItem != null ? ability.SourceItem.Name
                       : identity.Spellbook != null ? L("BL_KindSpell") : L("BL_KindAbility");
            string guid = bp.AssetGuid.ToString();
            if (byGuid.TryGetValue(guid, out var known))
            {
                if (!known.Tags.Contains(tag) && known.Tags.Count < 4) known.Tags.Add(tag);
                return;
            }
            // The family: the blueprint's variant parent, or the parent it was expanded from when that one has it as a variant.
            BlueprintAbility parent = bp.Parent;
            if (parent == null)
            {
                var from = identity.ConvertedFrom?.Blueprint;
                if (from != null && from != bp && from.HasVariant(bp)) parent = from;
            }
            var row = new Row { Guid = guid, Key = bp.AssetGuid, Name = NameOf(bp.Name, bp.name), Kind = AbilityBlockEntry.AbilityKind };
            row.Tags.Add(tag);
            if (parent != null)
            {
                row.FamilyGuid = parent.AssetGuid.ToString();
                row.FamilyKey = parent.AssetGuid;
                row.FamilyName = NameOf(parent.Name, parent.name);
            }
            byGuid[guid] = row;
            rows.Add(row);
        }

        private static string NameOf(string display, string internalName) =>
            string.IsNullOrEmpty(display) ? internalName ?? "?" : display;

        /// <summary>The rows to show (search, block visibility) and the character's entries it does not have now.</summary>
        private static void BuildView(ModSettings settings, string unitId, string unitName, int revision)
        {
            var scope = UserAbilityPolicy.ScopeFor(unitId);
            string search = (s_Search ?? "").Trim().ToLowerInvariant();
            var view = new List<Row>();
            foreach (var row in s_Rows)
            {
                if (search.Length > 0 && row.Search.IndexOf(search, StringComparison.Ordinal) < 0) continue;
                // ★ v0.3.28: filter the existing rows before paging, using the same exact/family status as DrawRow.
                if (s_BlockedOnly || s_HideBlocked)
                {
                    bool blocked = scope.SourceOf(row.Key) != BlockSource.None ||
                                   (row.FamilyGuid != null && scope.SourceOf(row.FamilyKey) != BlockSource.None);
                    if ((s_BlockedOnly && !blocked) || (s_HideBlocked && blocked)) continue;
                }
                view.Add(row);
            }
            if (s_ViewSearch != s_Search || s_ViewBlockedOnly != s_BlockedOnly || s_ViewHideBlocked != s_HideBlocked) s_Page = 0;
            s_View = view;
            s_ViewSearch = s_Search;
            s_ViewBlockedOnly = s_BlockedOnly;
            s_ViewHideBlocked = s_HideBlocked;
            s_ViewRevision = revision;

            s_Missing = new List<AbilityBlockEntry>();
            var own = settings.GetOrCreateSettings(unitId, unitName).BlockedAbilities;
            if (own == null) return;
            var families = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in s_Rows) if (row.FamilyGuid != null) families.Add(row.FamilyGuid);
            foreach (var entry in own)
                if (entry?.Guid != null && !s_RowGuids.Contains(entry.Guid) && !families.Contains(entry.Guid)) s_Missing.Add(entry);
        }
    }
}
