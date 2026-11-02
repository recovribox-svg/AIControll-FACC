// ★ user-ability-blacklist (docs/TASK_USER_ABILITY_BLACKLIST.md): the mod's own matching (UserAbilityPolicy.SourceOf) on the
// game's real blueprints. AbilityData needs a unit to be built normally; here it is a bare object with only its blueprint and
// the ability it was converted from - the two inputs the matching reads besides a held charge (which needs a unit fact and
// is covered by the unit tests and the decompiled UnitPartTouch.Init).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using CompanionAI_Pathfinder.Tactics;

namespace ProfileAudit
{
    internal static class BlocklistCheck
    {
        private static readonly FieldInfo BlueprintField = typeof(AbilityData).GetField("Blueprint", BindingFlags.Instance | BindingFlags.Public);
        private static readonly FieldInfo ConvertedField = typeof(AbilityData).GetField("m_ConvertedFrom", BindingFlags.Instance | BindingFlags.NonPublic);

        private static AbilityData Data(BlueprintAbility blueprint, AbilityData convertedFrom = null)
        {
            var data = (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
            BlueprintField.SetValue(data, blueprint);
            ConvertedField.SetValue(data, convertedFrom);
            return data;
        }

        private static BlockScope<BlueprintGuid> Scope(params BlueprintScriptableObject[] blocked) =>
            CompiledBlocklist<BlueprintGuid>.Compile(blocked.Select(b => b.AssetGuid.ToString()),
                                                     new List<KeyValuePair<string, IEnumerable<string>>>(),
                                                     UserAbilityPolicy.TryParse).For("unit");

        private static string Cost(string label, BlockScope<BlueprintGuid> scope, List<AbilityData> actor)
        {
            for (int i = 0; i < 3; i++) foreach (var a in actor) UserAbilityPolicy.SourceOf(scope, a);   // warm up
            const int rounds = 250;
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int blocked = 0;
            for (int r = 0; r < rounds; r++)
                foreach (var a in actor)
                    if (UserAbilityPolicy.SourceOf(scope, a) != BlockSource.None) blocked++;
            watch.Stop();
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            double checks = (double)rounds * actor.Count;
            return $"BLOCKLIST cost, {label}: {watch.Elapsed.TotalMilliseconds * 1e6 / checks:0.#} ns and {bytes / checks:0.##} bytes a check " +
                   $"({checks:0} checks, {blocked / rounds} of {actor.Count} blocked); a {actor.Count}-ability decision: " +
                   $"{watch.Elapsed.TotalMilliseconds / rounds * 1000:0.#} µs";
        }

        /// <summary>Report lines; <paramref name="failures"/> counts checks that did not hold.</summary>
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            failures = 0;
            var lines = new List<string>();
            if (BlueprintField == null || ConvertedField == null)
            {
                failures++;
                lines.Add("BLOCKLIST FAILED: AbilityData.Blueprint / m_ConvertedFrom not found (game version changed?)");
                return lines;
            }
            var abilities = loaded.OfType<BlueprintAbility>().ToList();
            int parents = 0, children = 0, linked = 0, unlinked = 0, siblingsChecked = 0;
            var problems = new List<string>();
            foreach (var parent in abilities)
            {
                var component = parent.GetComponent<AbilityVariants>();
                if (component == null) continue;
                var variants = component.Variants.Where(v => v != null).Distinct().ToList();
                if (variants.Count == 0) continue;
                parents++;
                var parentData = Data(parent);
                foreach (var child in variants)
                {
                    children++;
                    if (child.Parent == parent) linked++; else unlinked++;
                    // Expanded from its parent at run time (AbilityData.GetConversions: ConvertedFrom = the parent).
                    var childData = Data(child, parentData);
                    if (UserAbilityPolicy.SourceOf(Scope(parent), childData) != BlockSource.Shared)
                        problems.Add($"all variants of {parent.name} did not block {child.name}");
                    if (UserAbilityPolicy.SourceOf(Scope(child), childData) != BlockSource.Shared)
                        problems.Add($"{child.name} did not block itself");
                    var sibling = variants.FirstOrDefault(v => v != child && v.Parent != child && child.Parent != v);
                    if (sibling != null)
                    {
                        siblingsChecked++;
                        if (UserAbilityPolicy.SourceOf(Scope(sibling), childData) != BlockSource.None)
                            problems.Add($"blocking {sibling.name} also blocked its sibling {child.name}");
                    }
                }
            }
            lines.Add($"BLOCKLIST variants: {parents} parents, {children} variants; parent link set on {linked}, not set on {unlinked} " +
                      $"(those match through the run-time parent); {siblingsChecked} sibling checks");

            // Conversions that are not variants (a Cure from a Bless slot): the source slot's ban does not carry over.
            var spells = abilities.Where(a => a.IsSpell && !a.HasVariants && a.GetComponent<AbilityShadowSpell>() == null).Take(400).ToList();
            int conversions = 0;
            for (int i = 0; i + 1 < spells.Count; i += 2)
            {
                var from = spells[i];
                var to = spells[i + 1];
                if (from.HasVariant(to)) continue;
                conversions++;
                var converted = Data(to, Data(from));
                if (UserAbilityPolicy.SourceOf(Scope(from), converted) != BlockSource.None)
                    problems.Add($"{to.name} converted from {from.name} inherited the ban on {from.name}");
                if (UserAbilityPolicy.SourceOf(Scope(to), converted) != BlockSource.Shared)
                    problems.Add($"{to.name} converted from {from.name} was not blocked by its own entry");
            }
            var bless = abilities.FirstOrDefault(a => a.name == "Bless");
            var cure = abilities.FirstOrDefault(a => a.name == "CureLightWounds");
            if (bless != null && cure != null)
            {
                conversions++;
                var cureFromBless = Data(cure, Data(bless));
                bool inherits = UserAbilityPolicy.SourceOf(Scope(bless), cureFromBless) != BlockSource.None;
                if (inherits) problems.Add("CureLightWounds from a Bless slot inherited the ban on Bless");
                lines.Add($"BLOCKLIST Cure Light Wounds from a Bless slot with Bless blocked: {(inherits ? "BLOCKED (wrong)" : "allowed (right)")}");
            }
            lines.Add($"BLOCKLIST conversions that are not variants: {conversions} checked");

            // Empty lists: nothing is blocked.
            var empty = CompiledBlocklist<BlueprintGuid>.Empty.For("unit");
            if (abilities.Take(200).Any(a => UserAbilityPolicy.SourceOf(empty, Data(a)) != BlockSource.None))
                problems.Add("an ability was blocked with empty lists");

            // Cost of one check on this machine (the game's Mono differs): a 400-ability actor, empty lists and 50 entries.
            var actor = abilities.Take(400).Select(a => Data(a)).ToList();
            var fifty = abilities.Skip(1000).Take(49).Concat(new[] { actor[actor.Count / 2].Blueprint }).ToArray();
            lines.Add(Cost("empty lists", empty, actor));
            lines.Add(Cost("50 entries", Scope(fifty), actor));
            var allowedEmpty = UserAbilityPolicy.Allowed(empty, null, actor);
            var allowedFifty = UserAbilityPolicy.Allowed(Scope(fifty), null, actor);
            if (!ReferenceEquals(allowedEmpty, actor)) problems.Add("Allowed copied the list with empty lists");
            if (allowedFifty.Count != actor.Count - 1) problems.Add($"Allowed left {actor.Count - allowedFifty.Count} out with one blocked (expected 1)");
            lines.Add($"BLOCKLIST view: empty lists keep the same list ({ReferenceEquals(allowedEmpty, actor)}); one of {actor.Count} blocked leaves {allowedFifty.Count}");

            failures = problems.Count;
            lines.Add(problems.Count == 0 ? "BLOCKLIST: all checks held"
                                          : $"BLOCKLIST FAILED: {problems.Count} check(s): " + string.Join("; ", problems.Take(20)));
            return lines;
        }
    }
}
