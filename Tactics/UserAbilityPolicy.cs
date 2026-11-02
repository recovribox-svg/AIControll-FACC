// ★ user-ability-blacklist (docs/TASK_USER_ABILITY_BLACKLIST.md): the one place that answers "may AI Control start this?"
// for an ability, a toggle and a decided option, from the "Abilities AI must not use" lists (Settings/AbilityBlocklist).
//
//   - Blocked = on the shared list OR on the unit's own list (Tactics/UserAbilityRules, unit-tested).
//   - An ability matches by its blueprint, its variant parents (BlueprintAbility.Parent; an entry on a parent is "all
//     variants"), the parent it was expanded from when that parent really has it as a variant (a Cure converted from a
//     Bless slot is not Bless), and for a held touch charge the spell it came from (AbilityData.StickyTouch, set by
//     UnitPartTouch.Init). Spellbook, metamagic and item copies share the blueprint.
//   - Only what AI Control itself starts: the game's own procs, manual orders and other mods never come through here.
//   - The lists are compiled when the settings load or change (ModSettings.BlocklistEdits); a check allocates nothing,
//     and with empty lists it returns at once. Revision changes with every compile, for caches of filtered answers.
using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class UserAbilityPolicy
    {
        private static CompiledBlocklist<BlueprintGuid> s_Compiled = CompiledBlocklist<BlueprintGuid>.Empty;
        private static ModSettings s_CompiledFor;
        private static int s_CompiledEdits = int.MinValue;

        /// <summary>Changes whenever the lists are compiled again (an edit, a reload): caches of filtered answers compare it.</summary>
        public static int Revision { get; private set; }

        private static CompiledBlocklist<BlueprintGuid> Current
        {
            get
            {
                var settings = ModSettings.Instance;
                int edits = settings?.BlocklistEdits ?? int.MinValue;
                if (!ReferenceEquals(settings, s_CompiledFor) || edits != s_CompiledEdits) Compile(settings, edits);
                return s_Compiled;
            }
        }

        private static void Compile(ModSettings settings, int edits)
        {
            s_CompiledFor = settings;
            s_CompiledEdits = edits;
            Revision++;
            s_Reported.Clear();
            if (settings == null)
            {
                s_Compiled = CompiledBlocklist<BlueprintGuid>.Empty;
                return;
            }
            var units = new List<KeyValuePair<string, IEnumerable<string>>>();
            if (settings.CharacterSettings != null)
                foreach (var pair in settings.CharacterSettings)
                    if (pair.Value?.BlockedAbilities != null && pair.Value.BlockedAbilities.Count > 0)
                        units.Add(new KeyValuePair<string, IEnumerable<string>>(pair.Key, Guids(pair.Value.BlockedAbilities)));
            s_Compiled = CompiledBlocklist<BlueprintGuid>.Compile(Guids(settings.SharedBlockedAbilities), units, TryKey);
            if (!s_Compiled.IsEmpty)
                Main.Log($"[Blocklist] lists compiled: {s_Compiled.SharedCount} shared, {units.Count} character list(s)");
        }

        private static IEnumerable<string> Guids(List<AbilityBlockEntry> entries)
        {
            if (entries == null) yield break;
            foreach (var entry in entries)
                if (entry?.Guid != null) yield return entry.Guid;
        }

        private static bool TryKey(string text, out BlueprintGuid key)
        {
            key = default;
            if (!Guid.TryParseExact(text, "N", out var guid) || guid == Guid.Empty) return false;
            key = new BlueprintGuid(guid);
            return true;
        }

        /// <summary>No list names anything (every check returns at once).</summary>
        public static bool IsEmpty => Current.IsEmpty;

        /// <summary>The lists that apply to this unit (shared + its own). Taken once per decision by the engine.</summary>
        public static BlockScope<BlueprintGuid> ScopeFor(UnitEntityData unit) =>
            unit == null ? default : Current.For(unit.UniqueId);

        private struct AbilityTree : IBlueprintTree<BlueprintAbility, BlueprintGuid>
        {
            public BlueprintGuid KeyOf(BlueprintAbility blueprint) => blueprint.AssetGuid;
            public BlueprintAbility ParentOf(BlueprintAbility blueprint) => blueprint.Parent;
        }

        /// <summary>The lists that block this ability for the scope's unit (None when it may be used).</summary>
        public static BlockSource SourceOf(BlockScope<BlueprintGuid> scope, AbilityData ability)
        {
            if (scope.IsEmpty || ability?.Blueprint == null) return BlockSource.None;
            try
            {
                var blueprint = ability.Blueprint;
                // A variant (Command - Halt, a kineticist blast's form) is expanded from its parent: ConvertedFrom, and the
                // parent has it among its variants. Any other conversion (a Cure from a Bless slot) is not followed.
                var from = ability.ConvertedFrom?.Blueprint;
                bool fromHasIt = from != null && from != blueprint && from.HasVariant(blueprint);
                // A held touch charge: the spell it was cast as (its own blueprint is the touch delivery).
                var origin = ability.StickyTouch?.Blueprint;
                return UserAbilityRules.MatchAbility<BlueprintAbility, BlueprintGuid, AbilityTree>(scope, blueprint, from, fromHasIt, origin, default);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Blocklist] {ability.Name}: {ex.Message}");
                return BlockSource.None;
            }
        }

        public static BlockSource SourceOf(BlockScope<BlueprintGuid> scope, BlueprintActivatableAbility toggle) =>
            scope.IsEmpty || toggle == null ? BlockSource.None : scope.SourceOf(toggle.AssetGuid);

        public static BlockSource SourceOf(BlockScope<BlueprintGuid> scope, ActivatableAbility toggle) =>
            SourceOf(scope, toggle?.Blueprint);

        /// <summary>AI Control must not use this ability for this unit.</summary>
        public static bool Blocks(UnitEntityData unit, AbilityData ability)
        {
            var scope = ScopeFor(unit);
            return !scope.IsEmpty && SourceOf(scope, ability) != BlockSource.None;
        }

        /// <summary>AI Control must not switch this toggle on for this unit.</summary>
        public static bool Blocks(UnitEntityData unit, ActivatableAbility toggle)
        {
            var scope = ScopeFor(unit);
            return !scope.IsEmpty && SourceOf(scope, toggle) != BlockSource.None;
        }

        /// <summary>
        /// For predictions about another party member (will an ally finish this sleeper, heal that one): the ability is
        /// blocked AND AI Control drives that ally. A character the player plays keeps every ability, lists or not.
        /// </summary>
        public static bool BlocksAiUse(UnitEntityData ally, AbilityData ability) =>
            Blocks(ally, ability) && GameInterface.CustomBrainPatch.ShouldControlUnit(ally);

        public static bool BlocksAiUse(UnitEntityData ally, ActivatableAbility toggle) =>
            Blocks(ally, toggle) && GameInterface.CustomBrainPatch.ShouldControlUnit(ally);

        /// <summary>The refusal text for <paramref name="what"/> (the drivers do not count it as a failed option).</summary>
        public static string Refusal(string what) => UserAbilityRules.RefusalPrefix + ": " + what;

        /// <summary>
        /// Whether a decided option would make AI Control start something blocked: its ability (or held charge), the toggle
        /// it switches on, the substance infusion it starts or the blast that justifies it, an attack mode it switches on
        /// for its attack, or a Gather Power the game will put before its blast. Switching off is never blocked.
        /// </summary>
        public static bool Blocks(BlockScope<BlueprintGuid> scope, UnitEntityData unit, Candidate c, out string what)
        {
            what = null;
            if (scope.IsEmpty || c == null) return false;
            if (c.Ability != null && SourceOf(scope, c.Ability) != BlockSource.None) { what = c.Ability.Name; return true; }
            var plan = c.InfusionSwitch;
            if (plan != null)
            {
                if (plan.Activate != null && SourceOf(scope, plan.Activate) != BlockSource.None) { what = plan.Activate.Name; return true; }
                if (plan.Blast != null && SourceOf(scope, plan.Blast) != BlockSource.None) { what = plan.Blast.Name; return true; }
            }
            else if (c.Toggle != null && !c.ToggleOff && SourceOf(scope, c.Toggle) != BlockSource.None) { what = c.Toggle.Name; return true; }
            if (c.Modes != null)
                foreach (var mode in c.Modes)
                    if (mode.Value && SourceOf(scope, mode.Key) != BlockSource.None) { what = mode.Key.Name; return true; }
            if (c.GatherPower > 0 && unit != null)
            {
                try
                {
                    var gather = unit.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>()?.GatherPowerAbility?.Data;
                    if (gather != null && SourceOf(scope, gather) != BlockSource.None) { what = gather.Name + " (before the blast)"; return true; }
                }
                catch { }
            }
            return false;
        }

        public static bool Blocks(UnitEntityData unit, Candidate c, out string what) => Blocks(ScopeFor(unit), unit, c, out what);

        /// <summary>
        /// The abilities AI Control may consider for this unit: the same list when nothing of it is blocked (no copy), else
        /// a copy without the blocked ones. Discovery itself stays unfiltered (AbilityDiscovery.Gather).
        /// </summary>
        public static List<AbilityData> Allowed(BlockScope<BlueprintGuid> scope, UnitEntityData unit, List<AbilityData> abilities)
        {
            if (scope.IsEmpty || abilities == null) return abilities;
            List<AbilityData> result = null;
            for (int i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
                var source = SourceOf(scope, ability);
                if (source == BlockSource.None)
                {
                    result?.Add(ability);
                    continue;
                }
                if (result == null)
                {
                    result = new List<AbilityData>(abilities.Count);
                    for (int j = 0; j < i; j++) result.Add(abilities[j]);
                }
                NoteSkipped(unit, ability.Blueprint.AssetGuid, ability.Name, source);
            }
            return result ?? abilities;
        }

        /// <summary>Once per unit, ability and list edit: what the AI leaves out because of the lists (evidence for play tests).</summary>
        public static void NoteSkipped(UnitEntityData unit, BlueprintGuid guid, string name, BlockSource source)
        {
            if (unit == null) return;
            if (s_Reported.Count > 512) s_Reported.Clear();
            if (!s_Reported.Add((unit.UniqueId, guid))) return;
            Main.Log($"[Blocklist] {unit.CharacterName}: {name} not used by AI Control ({Describe(source)})");
        }

        private static readonly HashSet<(string, BlueprintGuid)> s_Reported = new HashSet<(string, BlueprintGuid)>();

        public static string Describe(BlockSource source) =>
            source == (BlockSource.Shared | BlockSource.Character) ? "shared list and this character's list" :
            source == BlockSource.Shared ? "shared list" :
            source == BlockSource.Character ? "this character's list" : "not blocked";
    }
}
