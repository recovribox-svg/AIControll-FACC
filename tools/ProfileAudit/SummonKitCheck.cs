// ★ summon-kit inventory (S1, docs/SUMMON_KIT_INVENTORY.md): which creatures the loaded graphs can summon, what each
// creature's blueprint gives it, and what the mod's own readers make of every active ability in that kit.
//
// What it is: a static inventory. Spawn sources are found by their ContextActionSpawnMonster elements; a creature's kit is
// read from its blueprint (facts, class levels, selections, templates, race, body, brain); every ability goes through
// the same verdict as the report's ability rows (Audit.VerdictOf) and the summon policy (SummonRules.AbilityRefusal).
//
// What it is not: no unit is created, so nothing a live creature adds is seen (facts and buffs a summoner's feats add at
// the spawn, difficulty-gated facts, level-up automation, another mod's patches), and a "read" verdict says only that a
// reader recognises the blueprint's effect - not that the engine uses the ability well, in time, or at all in a fight.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.AI.Blueprints;
using Kingmaker.Assets.UnitLogic.Mechanics.Actions;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class SummonKitCheck
    {
        // ---------------------------------------------------------------------------------------------------------
        // The object graph of one blueprint: its own components and elements, never another blueprint's.
        // ---------------------------------------------------------------------------------------------------------

        private static readonly Dictionary<Type, FieldInfo[]> s_Fields = new Dictionary<Type, FieldInfo[]>();

        private static FieldInfo[] FieldsOf(Type type)
        {
            if (s_Fields.TryGetValue(type, out var known)) return known;
            var list = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                list.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                               .Where(f => !f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType != typeof(string) &&
                                           !f.FieldType.IsPointer && !typeof(Delegate).IsAssignableFrom(f.FieldType) &&
                                           !typeof(MemberInfo).IsAssignableFrom(f.FieldType)));
            return s_Fields[type] = list.ToArray();
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        /// <summary>
        /// Every object reachable from <paramref name="root"/> without entering another blueprint: its components, their
        /// action lists and conditions, and the references they hold (a reference is visited, never followed).
        /// </summary>
        private static void Walk(object root, Action<object> visit)
        {
            var seen = new HashSet<object>(new ReferenceComparer());
            var stack = new Stack<object>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var o = stack.Pop();
                if (o == null || !seen.Add(o)) continue;
                var type = o.GetType();
                if (type.IsPrimitive || type.IsEnum || o is string || o is Type || o is Delegate) continue;
                if (o is SimpleBlueprint && !ReferenceEquals(o, root)) continue;      // another blueprint: not this graph
                if (!ReferenceEquals(o, root)) visit(o);
                if (o is BlueprintReferenceBase) continue;                             // visited, not followed
                if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                if (o is IEnumerable sequence && !(o is IDictionary))
                {
                    if (type.IsArray && (type.GetElementType().IsPrimitive || type.GetElementType().IsEnum || type.GetElementType() == typeof(string))) continue;
                    IEnumerator items;
                    try { items = sequence.GetEnumerator(); } catch { items = null; }
                    if (items != null)
                    {
                        while (true)
                        {
                            object item;
                            try { if (!items.MoveNext()) break; item = items.Current; } catch { break; }
                            if (item != null) stack.Push(item);
                        }
                        if (type.IsArray || type.IsGenericType) continue;
                    }
                }
                foreach (var f in FieldsOf(type))
                {
                    object value;
                    try { value = f.GetValue(o); } catch { continue; }
                    if (value != null) stack.Push(value);
                }
            }
        }

        private static readonly FieldInfo s_SpawnUnit =
            typeof(ContextActionSpawnMonster).GetField("m_Blueprint", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo s_UnitBrain =
            typeof(BlueprintUnit).GetField("m_Brain", BindingFlags.Instance | BindingFlags.NonPublic);

        private static string Short(BlueprintGuid guid) => guid.ToString();

        // UnitDescriptor adds BlueprintRoot.SystemMechanics.ChargeAbility to every unit, and UnitProgressionData adds
        // BlueprintRoot.Progression.FeatsProgression. The root blueprint is rarely part of a dump: without it the two
        // are looked up by the ids the stock 2.7.0x root names, and the report says which was used.
        private const string StockChargeAbility = "c78506dd0e14f7c45a599990e4e65038";
        private const string StockFeatsProgression = "5b72dd2ca2cb73b49903806ee8986325";
        private static readonly List<BlueprintUnitFact> s_UniversalSeeds = new List<BlueprintUnitFact>();
        private static string s_UniversalSource = "not loaded";

        private static void FindUniversal(List<BlueprintScriptableObject> all)
        {
            s_UniversalSeeds.Clear();
            var root = all.OfType<Kingmaker.Blueprints.Root.BlueprintRoot>().FirstOrDefault();
            BlueprintUnitFact charge = null, feats = null;
            if (root != null)
            {
                try { charge = root.SystemMechanics?.ChargeAbility; feats = root.Progression?.FeatsProgression; } catch { }
                s_UniversalSource = "the loaded root blueprint";
            }
            if (charge == null && feats == null)
            {
                charge = all.OfType<BlueprintAbility>().FirstOrDefault(b => b.AssetGuid.ToString() == StockChargeAbility);
                feats = all.OfType<BlueprintProgression>().FirstOrDefault(b => b.AssetGuid.ToString() == StockFeatsProgression);
                s_UniversalSource = charge != null || feats != null ? "the ids the stock 2.7.0x root names (the root blueprint is not loaded)" : "not loaded";
            }
            if (charge != null) s_UniversalSeeds.Add(charge);
            if (feats != null) s_UniversalSeeds.Add(feats);
        }

        // BlueprintReferenceBase.IsEmpty is also true for a named blueprint that is not loaded: read the id itself.
        private static bool Named(BlueprintReferenceBase r) => r != null && r.Guid != BlueprintGuid.Empty;

        private static SimpleBlueprint Resolve(BlueprintReferenceBase r)
        {
            try { return r.GetBlueprint(); } catch { return null; }
        }

        // References that carry presentation or campaign state: an unloaded one changes nothing a kit reader uses.
        private static readonly string[] s_Presentation =
            { "Asks", "Portrait", "Faction", "VisualPreset", "Etude", "Dialog", "Cue", "Area", "Loot", "UnlockableFlag", "Quest", "Cutscene", "Customization" };

        private static bool KitRelevant(BlueprintReferenceBase r)
        {
            string name = r.GetType().Name;
            if (r.GetType().DeclaringType != null) name = r.GetType().DeclaringType.Name + name;
            return !s_Presentation.Any(p => name.IndexOf(p, StringComparison.Ordinal) >= 0);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Rows
        // ---------------------------------------------------------------------------------------------------------

        private sealed class Spawn
        {
            public BlueprintScriptableObject Source;
            public ContextActionSpawnMonster Action;
            public BlueprintUnit Unit;         // null: the unit graph is not loaded, or the action names none
            public string UnitGuid;            // the referenced id, also when the unit is not loaded
            public bool Linked;
        }

        private sealed class SourceInfo
        {
            public BlueprintScriptableObject Bp;
            public readonly List<Spawn> Spawns = new List<Spawn>();
            public bool OnSpellList, GrantedByFeature, ItemAbility, UnitAbility, ReadAsSummoning;
            public string Category;
            /// <summary>A party member can plausibly cast it: a spell-list spell, or a feature / item ability no creature blueprint names.</summary>
            public bool PartyPlausible => OnSpellList || ((GrantedByFeature || ItemAbility) && !UnitAbility);
        }

        private enum Reading { Read, ReadPartly, Guessed, Unread, NotOffered, OnByDefault, PolicyRefused, Error }

        private sealed class AbilityRow
        {
            public BlueprintUnit Unit;
            public SimpleBlueprint Ability;
            public string Origin;              // fact | spell | brain only
            public Reading Reading;
            public string Reason;              // the reader's own words
            public string FullReason;          // ... untruncated, for grouping
            public bool BrainCasts;            // the creature's own brain blueprint casts it
        }

        private sealed class Kit
        {
            public BlueprintUnit Unit;
            public readonly HashSet<BlueprintUnitFact> Facts = new HashSet<BlueprintUnitFact>();
            public readonly List<AbilityRow> Abilities = new List<AbilityRow>();
            /// <summary>What the brain blueprint casts but no blueprint fact gives: brains are shared between creatures.</summary>
            public readonly List<AbilityRow> BrainOnly = new List<AbilityRow>();
            /// <summary>What the game gives every creature (Charge, the basic feats progression): counted once, not per kit.</summary>
            public readonly List<AbilityRow> Universal = new List<AbilityRow>();
            public readonly List<string> Weapons = new List<string>();
            public readonly List<string> Dynamic = new List<string>();     // what the blueprint leaves to the running game
            public readonly HashSet<string> MissingRefs = new HashSet<string>();
            public readonly List<string> Mechanisms = new List<string>();
            public bool Swarm, BrainLoaded, BrainNamed;
            public int ClassLevels, BrainCasts;
            public int Linked, Unlinked, PartySources;
            /// <summary>Named by an external unit list (a mod's code-built summons): the list's labels.</summary>
            public readonly List<string> External = new List<string>();
            public bool PartyReachable => PartySources > 0 || External.Count > 0;
        }

        // ---------------------------------------------------------------------------------------------------------

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, string detailPath, List<string> unitLists, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string label) { checks++; if (!ok) { bad++; lines.Add("SUMMON-KITS FAILED: " + label); } }
            try
            {
                var all = loaded.OfType<BlueprintScriptableObject>().ToList();

                // --- 1. spawn sources: every loaded blueprint whose own graph holds a ContextActionSpawnMonster ---
                var sources = new Dictionary<BlueprintScriptableObject, SourceInfo>();
                var spawns = new List<Spawn>();
                int otherRoutes = 0, elementsAgree = 0, elementsMiss = 0;
                foreach (var bp in all)
                {
                    List<ContextActionSpawnMonster> found = null;
                    bool other = false;
                    Walk(bp, o =>
                    {
                        if (o is ContextActionSpawnMonster spawn) (found ?? (found = new List<ContextActionSpawnMonster>())).Add(spawn);
                        else if (o is ContextActionSpawnUnlinkedMonster || o is SpawnMonsterAfterInitialized) other = true;
                    });
                    if (other) otherRoutes++;
                    if (found == null) continue;
                    // The game's own flat element list (m_AllElements) names the same actions: an independent count.
                    int listed = bp.ElementsArray.OfType<ContextActionSpawnMonster>().Count();
                    if (listed == found.Count) elementsAgree++; else elementsMiss++;
                    var info = sources[bp] = new SourceInfo { Bp = bp };
                    foreach (var action in found)
                    {
                        var reference = s_SpawnUnit?.GetValue(action) as BlueprintReferenceBase;
                        var spawn = new Spawn
                        {
                            Source = bp, Action = action, Unit = action.Blueprint,
                            UnitGuid = Named(reference) ? Short(reference.Guid) : null,
                            Linked = !action.DoNotLinkToCaster
                        };
                        info.Spawns.Add(spawn);
                        spawns.Add(spawn);
                    }
                }
                if (sources.Count == 0)
                {
                    failures = 0;
                    return new List<string> { "SUMMON-KITS SKIPPED: no loaded blueprint holds a ContextActionSpawnMonster; this input subset does not establish summon coverage" };
                }
                Check(s_SpawnUnit != null && s_UnitBrain != null, "the native spawn-unit and unit-brain reference fields are found");
                Check(elementsMiss == 0, $"the graph walk and the game's element list name the same spawn actions ({elementsMiss} sources differ)");

                // --- 2. how a party member could come by each source (static references only) ---
                var featureGranted = new HashSet<BlueprintUnitFact>();
                var unitNamed = new HashSet<BlueprintUnitFact>();
                var itemAbilities = new HashSet<BlueprintAbility>();
                foreach (var bp in all)
                {
                    if (bp is BlueprintUnit unit)
                    {
                        foreach (var f in unit.AddFacts) if (f != null) unitNamed.Add(f);
                        foreach (var cast in BrainCasts(unit.DefaultBrain)) unitNamed.Add(cast);
                    }
                    else if (bp is BlueprintFeatureBase || bp is BlueprintActivatableAbility)
                    {
                        foreach (var add in bp.ComponentsArray.OfType<AddFacts>())
                            foreach (var f in add.Facts) if (f != null) featureGranted.Add(f);
                    }
                    else if (bp is BlueprintItemEquipment item && item.Ability != null) itemAbilities.Add(item.Ability);
                }
                foreach (var info in sources.Values)
                {
                    var ability = info.Bp as BlueprintAbility;
                    if (ability == null)
                    {
                        info.Category = "triggered: " + info.Bp.GetType().Name.Replace("Blueprint", "");
                        continue;
                    }
                    var chain = new List<BlueprintAbility> { ability };
                    if (ability.Parent != null) chain.Add(ability.Parent);
                    info.OnSpellList = chain.Any(a => a.GetComponents<SpellListComponent>().Any(c => c.SpellList != null) ||
                                                      a.ComponentsArray.Any(c => c is SpellListComponent));
                    info.GrantedByFeature = chain.Any(a => featureGranted.Contains(a));
                    info.ItemAbility = chain.Any(a => itemAbilities.Contains(a));
                    info.UnitAbility = chain.Any(a => unitNamed.Contains(a));
                    try { info.ReadAsSummoning = AbilityProfile.Get(CasterPolicyCheck.Data(ability))?.Summons ?? false; } catch { }
                    info.Category = info.OnSpellList ? "spell-list spell"
                        : (info.GrantedByFeature || info.ItemAbility) && !info.UnitAbility ? (info.ItemAbility ? "item ability" : "feature ability")
                        : info.UnitAbility ? "creature ability" : "ability, no static owner";
                }

                // --- 3. the unique creatures and their kits ---
                FindUniversal(all);
                var kits = new Dictionary<BlueprintUnit, Kit>();
                foreach (var spawn in spawns.Where(s => s.Unit != null))
                {
                    if (!kits.TryGetValue(spawn.Unit, out var kit)) kits[spawn.Unit] = kit = BuildKit(spawn.Unit);
                    if (spawn.Linked) kit.Linked++; else kit.Unlinked++;
                }
                foreach (var kit in kits.Values)
                    kit.PartySources = sources.Values.Count(s => s.PartyPlausible && s.Spawns.Any(x => x.Unit == kit.Unit && x.Linked));

                // --- 3b. creatures a source outside the loaded graphs summons (a mod's abilities built in code) ---
                var byGuid = all.OfType<BlueprintUnit>().ToDictionary(u => u.AssetGuid.ToString(), u => u);
                int externalIds = 0, externalNew = 0;
                var externalUnloaded = new List<string>();
                foreach (var file in unitLists ?? new List<string>())
                {
                    string list = Path.GetFileNameWithoutExtension(file);
                    foreach (var raw in File.ReadAllLines(file))
                    {
                        var text = raw.Trim();
                        if (text.Length == 0 || text[0] == '#') continue;
                        var parts = text.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        string id = parts[0].ToLowerInvariant(), label = list + (parts.Length > 1 ? ": " + parts[1].Trim() : "");
                        externalIds++;
                        if (!byGuid.TryGetValue(id, out var unit)) { externalUnloaded.Add(label + " " + id); continue; }
                        if (!kits.TryGetValue(unit, out var kit)) { kits[unit] = kit = BuildKit(unit); externalNew++; }
                        kit.External.Add(label);
                    }
                }

                // --- 4. counts ---
                int references = spawns.Count;
                var unresolved = spawns.Where(s => s.Unit == null && s.UnitGuid != null).ToList();
                var unnamed = spawns.Where(s => s.Unit == null && s.UnitGuid == null).ToList();
                int repeated = references - unresolved.Count - unnamed.Count - (kits.Count - externalNew);
                var rows = kits.Values.SelectMany(k => k.Abilities).ToList();
                Check(references == unresolved.Count + unnamed.Count + spawns.Count(s => s.Unit != null),
                      "every spawn reference is a loaded unit, an unloaded unit id, or names no unit");
                Check(kits.Values.All(k => k.Abilities.GroupBy(r => r.Ability).All(g => g.Count() == 1)),
                      "every ability of a creature carries exactly one classification");
                Check(rows.All(r => !string.IsNullOrEmpty(r.Reason)), "every classification carries the reader's reason");
                foreach (string name in new[] { "SummonMonsterISingle", "SummonMonsterVIIISingle" })
                {
                    var fixture = sources.Keys.FirstOrDefault(b => b.name == name);
                    if (fixture == null) { lines.Add($"SUMMON-KITS note: fixture {name} not loaded"); continue; }
                    Check(sources[fixture].Spawns.All(s => s.Unit != null) && sources[fixture].OnSpellList && sources[fixture].PartyPlausible,
                          name + " resolves its creature and reads as a party spell");
                }

                // A live creature of the played v0.3.51 log has Sunder Armor through Improved Sunder, which gives the action
                // only when the creature does not have it yet (AddFeatureIfHasFact, Not): the conditional-fact reading.
                var abrikandilu = kits.Values.FirstOrDefault(k => k.Unit.AssetGuid.ToString() == "35758d873d826214a8c95c526a401c90");
                if (abrikandilu != null)
                    Check(abrikandilu.Abilities.Any(r => r.Ability.name == "SunderAction" && r.Origin == "fact") &&
                          abrikandilu.Abilities.Any(r => r.Ability.name == "CauseFear") && abrikandilu.Abilities.Any(r => r.Ability.name == "PowerAttackToggleAbility"),
                          "the Abrikandilu kit holds what the live creature's action bar showed (Sunder through a conditional fact, Cause Fear, Power Attack)");
                var partyKits = kits.Values.Where(k => k.PartyReachable).ToList();
                var supplementUnits = kits.Keys.Count(u => Audit.Supplement.Contains(u));
                int supplementSources = sources.Keys.Count(b => Audit.Supplement.Contains(b));
                int supplementAbilities = rows.Select(r => r.Ability).Distinct().Count(a => Audit.Supplement.Contains(a));
                string Count(IEnumerable<AbilityRow> set) =>
                    string.Join(", ", Enum.GetValues(typeof(Reading)).Cast<Reading>()
                        .Select(r => new { r, n = set.Count(x => x.Reading == r) }).Where(x => x.n > 0).Select(x => $"{Label(x.r)} {x.n}"));

                lines.Add($"SUMMON-KITS sources: {sources.Count} blueprints hold {references} spawn references " +
                          $"({string.Join(", ", sources.Values.GroupBy(s => s.Category).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}); " +
                          $"{sources.Values.Count(s => s.PartyPlausible)} party-plausible by static references; " +
                          $"{sources.Values.Count(s => s.Bp is BlueprintAbility && !s.ReadAsSummoning)} abilities not read as summoning (nested spawn); " +
                          $"{otherRoutes} blueprints use a spawn route without the summon part (never driven)");
                lines.Add($"SUMMON-KITS units: {kits.Count - externalNew} unique loaded creatures from {references - unresolved.Count - unnamed.Count} resolved references " +
                          $"({repeated} repeats; {kits.Values.Count(k => k.Linked + k.Unlinked > 1)} creatures have more than one source); " +
                          $"{kits.Values.Count(k => k.Linked > 0)} linked to the caster by at least one source, {kits.Values.Count(k => k.Linked == 0 && k.Unlinked > 0)} only unlinked; " +
                          $"{kits.Values.Count(k => k.PartySources > 0)} reachable from a party-plausible linked source");
                if (externalIds > 0)
                    lines.Add($"SUMMON-KITS external unit lists: {externalIds} creature ids named by {unitLists.Count} list(s) for sources outside the loaded graphs; " +
                              $"{externalIds - externalUnloaded.Count} loaded ({externalNew} that no loaded source summons), {externalUnloaded.Count} not loaded" +
                              (externalUnloaded.Count > 0 ? ": " + string.Join(", ", externalUnloaded.Take(8)) : "") +
                              "; the list is the only evidence of the source (its ability graph, count, link and spawn-time buffs are not read)");
                lines.Add($"SUMMON-KITS provenance: read from supplementary fixtures (not from a dump folder): {supplementSources} of {sources.Count} sources, " +
                          $"{supplementUnits} of {kits.Count} creatures, {supplementAbilities} of {rows.Select(r => r.Ability).Distinct().Count()} distinct kit abilities; " +
                          $"{Audit.Supplement.Count} supplementary blueprints loaded in all");
                lines.Add($"SUMMON-KITS missing or dynamic: {unresolved.Count} references to {unresolved.Select(s => s.UnitGuid).Distinct().Count()} unit ids not loaded; " +
                          $"{unnamed.Count} spawn actions name no unit; {kits.Values.Count(k => k.MissingRefs.Count > 0)} creatures have unloaded kit references " +
                          $"({kits.Values.SelectMany(k => k.MissingRefs).Distinct().Count()} ids); {kits.Values.Count(k => k.Dynamic.Count > 0)} creatures have a part the blueprint leaves to the running game; " +
                          $"{kits.Values.Count(k => k.BrainNamed && !k.BrainLoaded)} name a brain that is not loaded");
                lines.Add($"SUMMON-KITS kits: {kits.Values.Count(k => k.Swarm)} swarm bodies (refused by the summon policy: the game's brain keeps them); " +
                          $"{kits.Values.Count(k => !k.Swarm && k.Abilities.Count == 0)} weapon-only kits (no active ability or toggle in the blueprint); " +
                          $"{kits.Values.Count(k => !k.Swarm && k.Abilities.Count > 0)} with active abilities or toggles");
                lines.Add($"SUMMON-KITS ability readings, all creatures: {rows.Count} creature-ability pairs over {rows.Select(r => r.Ability).Distinct().Count()} distinct abilities: {Count(rows)}");
                var partyRows = partyKits.Where(k => !k.Swarm).SelectMany(k => k.Abilities).ToList();
                lines.Add($"SUMMON-KITS ability readings, party-reachable non-swarm creatures ({partyKits.Count(k => !k.Swarm)}, external lists included): {partyRows.Count} pairs over {partyRows.Select(r => r.Ability).Distinct().Count()} distinct abilities: {Count(partyRows)}; " +
                          $"{partyRows.Count(r => r.BrainCasts)} pairs are cast by the creature's own brain, of which {partyRows.Count(r => r.BrainCasts && r.Reading != Reading.Read)} are not fully read");
                var reachable = partyKits.Where(k => !k.Swarm).ToList();
                var universalRows = kits.Values.SelectMany(k => k.Universal).GroupBy(r => r.Ability).Select(g => g.First()).ToList();
                lines.Add($"SUMMON-KITS universal actions (given to every creature by the game, counted once and kept out of the kits; from {s_UniversalSource}): " +
                          (universalRows.Count == 0 ? "none found" : $"{universalRows.Count} abilities and switches: {Count(universalRows)}"));
                lines.Add($"SUMMON-KITS mechanisms in party-reachable non-swarm kits: {reachable.Count(k => k.Mechanisms.Contains("on-hit trigger"))} creatures with an on-hit trigger, " +
                          $"{reachable.Count(k => k.Mechanisms.Contains("aura"))} with an aura, {reachable.Count(k => k.Mechanisms.Contains("grapple on hit"))} that grapple on a hit; " +
                          $"{reachable.SelectMany(k => k.Abilities).Count(r => r.Reading == Reading.PolicyRefused && r.BrainCasts)} policy-refused creature-ability pairs the creature's own brain would cast " +
                          "(the game's brain summons more creatures; a driven summon does not)");
                lines.Add("SUMMON-KITS scope: a reading says a reader recognises the blueprint's effect; it is not evidence that the engine uses the ability well. " +
                          "Nothing a live creature adds is seen (spawn-time facts and buffs, difficulty gates, level-up automation, other mods' patches).");

                var gaps = Gaps(reachable);
                var families = Families(reachable);
                foreach (var f in families.Take(10)) lines.Add("SUMMON-KITS gap family: " + f);

                if (detailPath != null) WriteDetail(detailPath, sources.Values, kits.Values, unresolved, unnamed, gaps, families);
            }
            catch (Exception ex) { bad++; lines.Add("SUMMON-KITS FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"SUMMON-KITS {checks} checks, failures {bad}; a static inventory of spawn graphs and creature blueprints through the mod's " +
                            "ability readers and summon policy; no actor, spawn, cast, command or Unity execution");
            return lines;
        }

        private static string Label(Reading r)
        {
            switch (r)
            {
                case Reading.Read: return "read";
                case Reading.ReadPartly: return "read with a guessed part";
                case Reading.Guessed: return "guessed";
                case Reading.Unread: return "unread";
                case Reading.NotOffered: return "not offered";
                case Reading.OnByDefault: return "on by default (no decision)";
                case Reading.PolicyRefused: return "refused by the summon policy";
                default: return "reader error";
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // One creature's kit, from its blueprint
        // ---------------------------------------------------------------------------------------------------------

        private static IEnumerable<BlueprintAbility> BrainCasts(BlueprintBrain brain)
        {
            if (brain == null) yield break;
            IEnumerable<BlueprintAiAction> actions;
            try { actions = brain.Actions?.ToList(); } catch { actions = null; }
            if (actions == null) yield break;
            foreach (var action in actions)
            {
                if (!(action is BlueprintAiCastSpell cast)) continue;
                if (cast.Variant != null) yield return cast.Variant;
                else if (cast.Ability != null) yield return cast.Ability;
            }
        }

        private static Kit BuildKit(BlueprintUnit unit)
        {
            var kit = new Kit { Unit = unit };
            var pending = new Queue<BlueprintUnitFact>();
            void Add(BlueprintUnitFact fact) { if (fact != null && kit.Facts.Add(fact)) pending.Enqueue(fact); }
            void AddEntries(IEnumerable<LevelEntry> entries, int level)
            {
                foreach (var entry in entries ?? Enumerable.Empty<LevelEntry>())
                    if (entry != null && entry.Level <= level)
                        foreach (var feature in entry.Features) Add(feature);
            }

            // UnitDescriptor's unit setup: the blueprint's ability components, its facts, its templates
            foreach (var c in unit.ComponentsArray.OfType<AddAbilityToCharacterComponent>()) foreach (var a in c.Abilities) Add(a);
            foreach (var c in unit.ComponentsArray.OfType<AddActivatableAbilityComponent>()) foreach (var a in c.ActivatableAbilities) Add(a);
            foreach (var fact in unit.AddFacts) Add(fact);
            foreach (var template in unit.AdditionalTemplates)
                if (template != null) foreach (var fact in template.AddFacts) Add(fact);
            foreach (var add in unit.ComponentsArray.OfType<AddFacts>())
            {
                if (add.HasDifficultyRequirements) kit.Dynamic.Add("difficulty-gated facts");
                foreach (var fact in add.Facts) Add(fact);
            }
            if (unit.Race != null) foreach (var feature in unit.Race.Features) Add(feature);

            var spells = new List<BlueprintAbility>();
            var levels = unit.ComponentsArray.OfType<AddClassLevels>().ToList();
            foreach (var acl in levels)
            {
                var cls = acl.CharacterClass;
                if (cls == null) continue;
                kit.ClassLevels += acl.Levels;
                var removed = new HashSet<BlueprintFeatureBase>();
                foreach (var archetype in acl.Archetypes)
                    if (archetype != null)
                    {
                        foreach (var entry in archetype.RemoveFeatures) if (entry != null && entry.Level <= acl.Levels) foreach (var f in entry.Features) removed.Add(f);
                        AddEntries(archetype.AddFeatures, acl.Levels);
                    }
                if (cls.Progression != null)
                    foreach (var entry in cls.Progression.LevelEntries)
                        if (entry != null && entry.Level <= acl.Levels)
                            foreach (var feature in entry.Features) if (!removed.Contains(feature)) Add(feature);
                foreach (var selection in acl.Selections ?? new SelectionEntry[0])
                {
                    if (selection == null) continue;
                    foreach (var feature in selection.Features) Add(feature);
                    if (selection.IsParametrizedFeature && selection.ParametrizedFeature != null) Add(selection.ParametrizedFeature);
                }
                bool book = cls.Spellbook != null || acl.Archetypes.Any(a => a?.ReplaceSpellbook != null);
                var chosen = acl.SelectSpells.Concat(acl.MemorizeSpells).Where(s => s != null).Distinct().ToList();
                spells.AddRange(chosen);
                if (book && chosen.Count == 0) kit.Dynamic.Add($"spellbook of {cls.name} with no spell named in the blueprint");
            }

            // Facts that give further facts. A class-level gate is read against the creature's whole level (an upper bound);
            // a fact given "if the creature has / has not another fact" is settled once the rest is known.
            int guard = 0;
            void Settle(bool own)
            {
                bool changed = true;
                while (changed && guard < 4000)
                {
                    while (pending.Count > 0 && guard++ < 4000)
                    {
                        var fact = pending.Dequeue();
                        if (fact is BlueprintFeatureSelection selection && !levels.Any(l => (l.Selections ?? new SelectionEntry[0]).Any(s => s?.Selection == selection)))
                        { if (own) kit.Dynamic.Add("a feature selection with no choice named in the blueprint"); continue; }
                        if (fact is BlueprintProgression progression) AddEntries(progression.LevelEntries, Math.Max(kit.ClassLevels, 1));
                        foreach (var component in fact.ComponentsArray)
                        {
                            if (component is AddFacts add) { foreach (var f in add.Facts) Add(f); }
                            else if (component is AddFeatureOnClassLevel onLevel && onLevel.Level <= kit.ClassLevels && !onLevel.BeforeThisLevel) Add(onLevel.Feature);
                        }
                    }
                    changed = false;
                    foreach (var fact in kit.Facts.ToList())
                        foreach (var conditional in fact.ComponentsArray.OfType<AddFeatureIfHasFact>())
                        {
                            var given = conditional.Feature;
                            if (given == null || kit.Facts.Contains(given)) continue;
                            bool has = conditional.CheckedFact != null && kit.Facts.Contains(conditional.CheckedFact);
                            if (has != conditional.Not) { Add(given); changed = true; }
                        }
                }
            }
            Settle(true);

            // What the game gives every creature whatever its blueprint says (the root blueprint's Charge ability and the
            // basic feats progression: Fight Defensively, the skill actions, ...). Kept apart: they are no part of a
            // creature's own kit and would otherwise repeat on every row.
            var own = new HashSet<BlueprintUnitFact>(kit.Facts);
            foreach (var seed in s_UniversalSeeds) Add(seed);
            Settle(false);
            var universal = new HashSet<BlueprintUnitFact>(kit.Facts.Where(f => !own.Contains(f)));

            // unloaded references of the creature's own graph and of its facts
            void Missing(BlueprintScriptableObject owner)
            {
                Walk(owner, o =>
                {
                    if (o is BlueprintReferenceBase r && Named(r) && KitRelevant(r) && Resolve(r) == null)
                        kit.MissingRefs.Add(r.GetType().Name.Replace("Reference", "") + ":" + Short(r.Guid));
                });
            }
            Missing(unit);
            foreach (var fact in kit.Facts) Missing(fact);

            // mechanisms that change what a kit asks of a tactical engine
            bool grapple = false, swarmRound = false, onHit = false, aura = false;
            // a creature's own facts, and the buff a switch of its kit carries (Grab is a switch whose buff grapples on a hit)
            var carriers = kit.Facts.Cast<BlueprintScriptableObject>()
                .Concat(kit.Facts.OfType<BlueprintActivatableAbility>().Where(t => t.Buff != null).Select(t => (BlueprintScriptableObject)t.Buff)).Distinct().ToList();
            foreach (var fact in carriers)
            {
                if (fact.ComponentsArray.Any(c => c is SwarmDamageResistance)) kit.Swarm = true;
                if (fact.ComponentsArray.Any(c => c is Kingmaker.UnitLogic.Mechanics.Components.AddInitiatorAttackWithWeaponTrigger)) onHit = true;
                if (fact.ComponentsArray.Any(c => c is Kingmaker.UnitLogic.Buffs.Components.AddAreaEffect)) aura = true;
                foreach (var element in fact.ElementsArray)
                {
                    if (element is ContextActionGrapple) grapple = true;
                    else if (element is ContextActionSwarmAttack) swarmRound = true;
                }
            }
            if (swarmRound) kit.Swarm = true;
            if (grapple) kit.Mechanisms.Add("grapple on hit");
            if (onHit) kit.Mechanisms.Add("on-hit trigger");
            if (aura) kit.Mechanisms.Add("aura");

            // body
            void Weapon(string slot, BlueprintItemEquipmentHand hand)
            {
                if (hand == null) return;
                var weapon = hand as BlueprintItemWeapon;
                kit.Weapons.Add(slot + " " + hand.name + (weapon != null && weapon.IsNatural ? " (natural)" : "") + (weapon != null && weapon.IsRanged ? " (ranged)" : ""));
            }
            if (unit.Body != null)
            {
                Weapon("main", unit.Body.PrimaryHand);
                Weapon("off", unit.Body.SecondaryHand);
                foreach (var limb in unit.Body.AdditionalLimbs) Weapon("limb", limb);
                foreach (var limb in unit.Body.AdditionalSecondaryLimbs) Weapon("secondary limb", limb);
            }

            // brain
            var brainReference = s_UnitBrain?.GetValue(unit) as BlueprintReferenceBase;
            kit.BrainNamed = Named(brainReference);
            var brain = unit.DefaultBrain;
            kit.BrainLoaded = brain != null;
            var brainCasts = new HashSet<BlueprintAbility>(BrainCasts(brain));
            kit.BrainCasts = brainCasts.Count;
            if (brain != null)
                Walk(brain, o =>
                {
                    if (o is BlueprintReferenceBase r && Named(r) && r.GetType().Name.Contains("AiAction") && Resolve(r) == null)
                        kit.MissingRefs.Add("AiAction:" + Short(r.Guid));
                });

            // active abilities: ability facts and named spells (a variant parent stands for its variants), then toggles
            var seen = new HashSet<SimpleBlueprint>();
            void AddAbility(BlueprintAbility ability, string origin)
            {
                if (ability == null) return;
                var variants = ability.GetComponent<AbilityVariants>();
                if (variants != null)
                {
                    foreach (var variant in variants.Variants) AddAbility(variant, origin);
                    return;
                }
                if (!seen.Add(ability)) return;
                var row = Classify(ability);
                row.Unit = unit; row.Origin = origin;
                row.BrainCasts = brainCasts.Contains(ability) || (ability.Parent != null && brainCasts.Contains(ability.Parent));
                (origin == "brain only" ? kit.BrainOnly : origin == "universal" ? kit.Universal : kit.Abilities).Add(row);
            }
            foreach (var ability in kit.Facts.OfType<BlueprintAbility>().Where(a => !universal.Contains(a)).OrderBy(a => a.name, StringComparer.Ordinal)) AddAbility(ability, "fact");
            foreach (var spell in spells.OrderBy(a => a.name, StringComparer.Ordinal)) AddAbility(spell, "spell");
            // What the brain casts but no blueprint fact gives. A brain is shared between creatures (one elemental brain
            // names every size's ability), so these are listed apart and never counted as part of the kit.
            foreach (var cast in brainCasts.OrderBy(a => a.name, StringComparer.Ordinal))
                if (!seen.Contains(cast) && cast.GetComponent<AbilityVariants>() == null) AddAbility(cast, "brain only");
            if (kit.BrainOnly.Count > 0) kit.Dynamic.Add($"its brain casts {kit.BrainOnly.Count} abilities no blueprint fact gives");
            foreach (var ability in kit.Facts.OfType<BlueprintAbility>().Where(a => universal.Contains(a)).OrderBy(a => a.name, StringComparer.Ordinal)) AddAbility(ability, "universal");
            foreach (var toggle in kit.Facts.OfType<BlueprintActivatableAbility>().OrderBy(a => a.name, StringComparer.Ordinal))
            {
                if (!seen.Add(toggle)) continue;
                var verdict = Audit.VerdictOf(toggle);
                var reading = verdict.Verdict.StartsWith("toggle valued", StringComparison.Ordinal) || verdict.Verdict.StartsWith("infusion selection: supported", StringComparison.Ordinal) ? Reading.Read
                            : verdict.Verdict.StartsWith("toggle GUESSED", StringComparison.Ordinal) ? Reading.Guessed
                            : verdict.Verdict.StartsWith("toggle not managed", StringComparison.Ordinal) ? Reading.NotOffered
                            : verdict.Verdict.Contains("error") ? Reading.Error : Reading.Unread;
                // A switch the game turns on by itself when the creature appears asks for no decision; one that starts off
                // stays off unless something switches it (the mod does not manage it, and no brain action does).
                string state = toggle.IsOnByDefault ? "on by default" : "off by default";
                if (reading != Reading.Read && toggle.IsOnByDefault) reading = Reading.OnByDefault;
                (universal.Contains(toggle) ? kit.Universal : kit.Abilities).Add(new AbilityRow
                {
                    Unit = unit, Ability = toggle, Origin = universal.Contains(toggle) ? "universal toggle" : "toggle",
                    Reason = verdict.Verdict + "; " + state + Detail(verdict.Detail), FullReason = verdict.Verdict + "; " + state, Reading = reading
                });
            }
            kit.Dynamic.Sort(StringComparer.Ordinal);
            var distinct = kit.Dynamic.Distinct().ToList();
            kit.Dynamic.Clear(); kit.Dynamic.AddRange(distinct);
            return kit;
        }

        // The reader's detail uses " | " between its own parts; the detail file uses it between columns.
        private static string Detail(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";
            detail = detail.Replace(" | ", " ; ");
            return " [" + (detail.Length > 220 ? detail.Substring(0, 220) + "..." : detail) + "]";
        }

        private static string Full(Audit.Row verdict) =>
            verdict.Verdict + (string.IsNullOrEmpty(verdict.Detail) ? "" : " [" + verdict.Detail.Replace(" | ", " ; ") + "]");

        /// <summary>The order the engine applies: the summon policy first, then what the action bar offers, then the readers.</summary>
        private static AbilityRow Classify(BlueprintAbility ability)
        {
            var row = new AbilityRow { Ability = ability };
            bool summons;
            try { summons = AbilityProfile.Get(CasterPolicyCheck.Data(ability))?.Summons ?? false; }
            catch (Exception ex) { row.Reading = Reading.Error; row.Reason = "profile: " + ex.GetBaseException().GetType().Name; return row; }
            bool material = ability.MaterialComponent != null && ability.MaterialComponent.Item != null;
            string refusal = SummonRules.AbilityRefusal(summons, false, material);
            if (refusal != null) { row.Reading = Reading.PolicyRefused; row.Reason = refusal; return row; }
            if (ability.Hidden) { row.Reading = Reading.NotOffered; row.Reason = "hidden from the action bar (AbilityDiscovery offers only what the bar shows)"; return row; }
            var verdict = Audit.VerdictOf(ability);
            row.Reason = verdict.Verdict + Detail(verdict.Detail);
            row.FullReason = Full(verdict);
            string v = verdict.Verdict ?? "";
            row.Reading = v.StartsWith("valued", StringComparison.Ordinal) ? (v.Contains("guessed") ? Reading.ReadPartly : Reading.Read)
                        : v.StartsWith("GUESSED", StringComparison.Ordinal) ? Reading.Guessed
                        : v.StartsWith("NEVER OFFERED", StringComparison.Ordinal) || v.StartsWith("not offered", StringComparison.Ordinal) ? Reading.NotOffered
                        : v.StartsWith("error", StringComparison.Ordinal) ? Reading.Error
                        : Reading.Unread;
            return row;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Gap ranking and the detail file
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Abilities of party-reachable, non-swarm creatures that are not fully read, ranked by the creatures whose own
        /// brain casts them (the designers' statement that the ability is part of the creature's play), then by the
        /// creatures that have them. A ranking of coverage, not a measurement of gameplay value.
        /// </summary>
        private static List<string> Gaps(List<Kit> kits)
        {
            var result = new List<string>();
            var groups = kits.SelectMany(k => k.Abilities).Where(IsGap)
                .GroupBy(r => r.Ability)
                .Select(g => new
                {
                    Ability = g.Key, Rows = g.ToList(),
                    Brain = g.Count(r => r.BrainCasts), Units = g.Select(r => r.Unit).Distinct().Count(),
                    Sources = g.Select(r => r.Unit).Distinct().Sum(u => Reach(kits.First(k => k.Unit == u)))
                })
                .OrderByDescending(g => g.Brain).ThenByDescending(g => g.Sources).ThenByDescending(g => g.Units).ThenBy(g => g.Ability.name, StringComparer.Ordinal);
            foreach (var g in groups)
                result.Add($"{g.Ability.name} | {Label(g.Rows[0].Reading)} | creatures {g.Units} (their brain casts it: {g.Brain}) | party-plausible linked sources {g.Sources} | " +
                           $"{g.Rows[0].Reason} | e.g. {string.Join(", ", g.Rows.Select(r => r.Unit.name).Distinct().Take(3))}");
            return result;
        }

        /// <summary>How many ways a party member reaches the creature: party-plausible linked sources and external list entries.</summary>
        private static int Reach(Kit k) => k.PartySources + k.External.Count;

        private static bool IsGap(AbilityRow r) =>
            r.Reading != Reading.Read && r.Reading != Reading.PolicyRefused && r.Reading != Reading.OnByDefault;

        /// <summary>The reader's reason without what differs between abilities of one mechanism (sides, sizes, numbers).</summary>
        private static string FamilyKey(AbilityRow r)
        {
            string reason = r.FullReason ?? r.Reason ?? "";
            int sides = reason.IndexOf(" ; sides:", StringComparison.Ordinal);
            if (sides >= 0) reason = reason.Substring(0, sides);
            sides = reason.IndexOf("[sides:", StringComparison.Ordinal);
            if (sides >= 0) reason = reason.Substring(0, sides);
            int flags = reason.IndexOf("[flags:", StringComparison.Ordinal);
            if (flags >= 0) reason = reason.Substring(0, flags);
            reason = System.Text.RegularExpressions.Regex.Replace(reason, @"aura [0-9.]+ m: [^;|\]]*", "aura");
            reason = System.Text.RegularExpressions.Regex.Replace(reason, @"[0-9]+(\.[0-9]+)?", "#");
            reason = reason.Trim().TrimEnd('[').Trim();
            if (reason.Length > 260) reason = reason.Substring(0, 260) + "...";
            return Label(r.Reading) + " | " + reason;
        }

        /// <summary>
        /// The gaps grouped by the reader's reason: one row per mechanism the readers do not fully cover, with the
        /// abilities, creatures and sources it touches. The order is coverage (brain-cast pairs, then sources), not value.
        /// </summary>
        private static List<string> Families(List<Kit> kits)
        {
            return kits.SelectMany(k => k.Abilities).Where(IsGap).GroupBy(FamilyKey)
                .Select(g => new
                {
                    g.Key, Abilities = g.Select(r => r.Ability).Distinct().Count(), Units = g.Select(r => r.Unit).Distinct().ToList(),
                    Brain = g.Count(r => r.BrainCasts), Names = g.Select(r => r.Ability.name).Distinct().OrderBy(n => n, StringComparer.Ordinal).Take(6).ToList()
                })
                .Select(g => new { g.Key, g.Abilities, g.Brain, g.Names, Units = g.Units.Count, Sources = g.Units.Sum(u => Reach(kits.First(k => k.Unit == u))) })
                .OrderByDescending(g => g.Brain).ThenByDescending(g => g.Sources).ThenByDescending(g => g.Units).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key} | abilities {g.Abilities} | creatures {g.Units} | brain-cast pairs {g.Brain} | party-plausible linked sources {g.Sources} | {string.Join(", ", g.Names)}")
                .ToList();
        }

        private static string Origin(SimpleBlueprint bp) => Audit.Supplement.Contains(bp) ? "supplement" : "dump";

        private static void WriteDetail(string path, IEnumerable<SourceInfo> sources, IEnumerable<Kit> kits, List<Spawn> unresolved,
                                        List<Spawn> unnamed, List<string> gaps, List<string> families)
        {
            using (var w = new StreamWriter(path))
            {
                w.WriteLine("# SUMMON-KITS detail. Static blueprint inventory; a reading is reader recognition, not tactical competence.");
                w.WriteLine();
                w.WriteLine("## sources: name | guid | type | category | party-plausible | read as summoning | spawn references (unit, linked, count, pool)");
                foreach (var s in sources.OrderBy(s => s.Category, StringComparer.Ordinal).ThenBy(s => s.Bp.name, StringComparer.Ordinal))
                    w.WriteLine($"{s.Bp.name} | {s.Bp.AssetGuid} | {s.Bp.GetType().Name} | {s.Category} | {(s.PartyPlausible ? "yes" : "no")} | " +
                                $"{(s.Bp is BlueprintAbility ? (s.ReadAsSummoning ? "yes" : "NO") : "n/a")} | " +
                                string.Join("; ", s.Spawns.Select(x => $"{(x.Unit != null ? x.Unit.name : x.UnitGuid != null ? "UNLOADED " + x.UnitGuid : "NO UNIT")}" +
                                    $"{(x.Linked ? "" : " unlinked")} x{CountText(x.Action)}{(x.Action.SummonPool != null ? " pool " + x.Action.SummonPool.name : "")}")) +
                                $" | {Origin(s.Bp)}");
                w.WriteLine();
                w.WriteLine("## unresolved unit references: unit guid | sources");
                foreach (var g in unresolved.GroupBy(s => s.UnitGuid).OrderBy(g => g.Key, StringComparer.Ordinal))
                    w.WriteLine($"{g.Key} | {string.Join(", ", g.Select(s => s.Source.name).Distinct())}");
                foreach (var s in unnamed) w.WriteLine($"(no unit named) | {s.Source.name}");
                w.WriteLine();
                w.WriteLine("## units: name | guid | origin | sources linked/unlinked | party-plausible linked sources | class levels | swarm | weapons | " +
                            "mechanisms | brain (casts) | abilities by reading | dynamic parts | unloaded kit references");
                foreach (var k in kits.OrderByDescending(Reach).ThenBy(k => k.Unit.name, StringComparer.Ordinal))
                    w.WriteLine($"{k.Unit.name} | {k.Unit.AssetGuid} | {Origin(k.Unit)} | {k.Linked}/{k.Unlinked} | {k.PartySources}{(k.External.Count > 0 ? " + external " + k.External.Count : "")} | {k.ClassLevels} | {(k.Swarm ? "SWARM" : "-")} | " +
                                $"{(k.Weapons.Count > 0 ? string.Join(", ", k.Weapons) : "-")} | {(k.Mechanisms.Count > 0 ? string.Join(", ", k.Mechanisms) : "-")} | " +
                                $"{(k.BrainLoaded ? k.Unit.DefaultBrain.name + " (" + k.BrainCasts + ")" : k.BrainNamed ? "NOT LOADED" : "none")} | " +
                                $"{(k.Abilities.Count > 0 ? string.Join(", ", k.Abilities.GroupBy(r => r.Reading).OrderBy(g => g.Key).Select(g => $"{Label(g.Key)} {g.Count()}")) : "-")} | " +
                                $"{(k.Dynamic.Count > 0 ? string.Join("; ", k.Dynamic) : "-")} | {k.MissingRefs.Count}");
                w.WriteLine();
                w.WriteLine("## abilities: unit | ability | guid | origin | reading | the creature's brain casts it | reason");
                foreach (var k in kits.OrderByDescending(Reach).ThenBy(k => k.Unit.name, StringComparer.Ordinal))
                    foreach (var r in k.Abilities.Concat(k.BrainOnly))
                        w.WriteLine($"{k.Unit.name} | {r.Ability.name} | {r.Ability.AssetGuid} | {r.Origin}{(r.Origin == "brain only" ? " (not counted)" : "")} | {Label(r.Reading)} | {(r.BrainCasts ? "yes" : "no")} | {r.Reason}");
                w.WriteLine();
                w.WriteLine("## universal actions (every creature; not counted in the kits): ability | guid | origin | reading | reason");
                foreach (var r in kits.SelectMany(k => k.Universal).GroupBy(r => r.Ability).Select(g => g.First()).OrderBy(r => r.Ability.name, StringComparer.Ordinal))
                    w.WriteLine($"{r.Ability.name} | {r.Ability.AssetGuid} | {r.Origin} | {Label(r.Reading)} | {r.Reason}");
                w.WriteLine();
                w.WriteLine("## external unit lists: unit | guid | list entries | swarm | weapons | mechanisms | abilities by reading");
                foreach (var k in kits.Where(k => k.External.Count > 0).OrderBy(k => k.Unit.name, StringComparer.Ordinal))
                    w.WriteLine($"{k.Unit.name} | {k.Unit.AssetGuid} | {string.Join("; ", k.External)} | {(k.Swarm ? "SWARM" : "-")} | " +
                                $"{(k.Weapons.Count > 0 ? string.Join(", ", k.Weapons) : "-")} | {(k.Mechanisms.Count > 0 ? string.Join(", ", k.Mechanisms) : "-")} | " +
                                $"{(k.Abilities.Count > 0 ? string.Join(", ", k.Abilities.GroupBy(r => r.Reading).OrderBy(g => g.Key).Select(g => $"{Label(g.Key)} {g.Count()}")) : "-")}");
                w.WriteLine();
                w.WriteLine("## unloaded kit references: unit | reference type:guid");
                foreach (var k in kits.Where(k => k.MissingRefs.Count > 0).OrderBy(k => k.Unit.name, StringComparer.Ordinal))
                    foreach (var m in k.MissingRefs.OrderBy(m => m, StringComparer.Ordinal)) w.WriteLine($"{k.Unit.name} | {m}");
                w.WriteLine();
                w.WriteLine("## gap families (party-reachable, non-swarm creatures): reading | reason | abilities | creatures | brain-cast pairs | sources | examples");
                foreach (var f in families) w.WriteLine(f);
                w.WriteLine();
                w.WriteLine("## gaps (party-reachable, non-swarm creatures; not fully read): ability | reading | creatures | sources | reason | examples");
                foreach (var g in gaps) w.WriteLine(g);
            }
        }

        private static string CountText(ContextActionSpawnMonster action)
        {
            var c = action.CountValue;
            if (c == null) return "?";
            try
            {
                string dice = c.DiceType.ToString() == "Zero" || c.DiceType.ToString() == "One" ? "" : $"{Value(c.DiceCountValue)}{c.DiceType}+";
                return dice + Value(c.BonusValue);
            }
            catch { return "?"; }
        }

        private static string Value(Kingmaker.UnitLogic.Mechanics.ContextValue v) =>
            v == null ? "0" : v.ValueType == Kingmaker.UnitLogic.Mechanics.ContextValueType.Simple ? v.Value.ToString() : v.ValueType.ToString();
    }
}
