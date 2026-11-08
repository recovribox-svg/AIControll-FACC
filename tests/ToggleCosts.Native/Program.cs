using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Facts;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.ElementsSystem;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using System.Runtime.CompilerServices;

namespace ToggleCostsNative
{
    internal static class Program
    {
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static int Main(string[] args)
        {
            // Reuse the real loader/readers without changing their strict/partial accounting or production tool.
            int audit = (int)typeof(ProfileAudit.Program).GetMethod("Main", Static).Invoke(null, new object[] { args });
            if (audit != 0) return audit;
            var all = (List<SimpleBlueprint>)typeof(ProfileAudit.Audit).GetField("s_All", Static).GetValue(null);
            var byName = all.GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.First());
            var byId = all.ToDictionary(x => x.AssetGuid.ToString());
            int checks = 0, failures = 0, kits = 0;
            var lines = new List<string>();
            void Check(bool ok, string label)
            { checks++; if (!ok) { failures++; lines.Add("TOGGLE-COST FAILED " + label); } }
            T Find<T>(string name) where T : SimpleBlueprint => byName.TryGetValue(name, out var b) && b is T t
                ? t : throw new InvalidDataException("Required fixture not loaded: " + name);
            var unitCost = typeof(TacticalEngine).GetMethod("TryToggleUnitCost", Static) ?? throw new MissingMethodException("TryToggleUnitCost");
            var activationCost = typeof(TacticalEngine).GetMethod("TryToggleActivationCost", Static) ?? throw new MissingMethodException("TryToggleActivationCost");
            var profile = typeof(TacticalEngine).GetMethod("ProfileOf", Static) ?? throw new MissingMethodException("ProfileOf");
            var patchArgs = new object[] { false };
            Check((bool)typeof(TacticalEngine).GetMethod("KnownToggleSpendPatches",Static).Invoke(null,patchArgs) && !(bool)patchArgs[0],
                "native spend/cost/explicit callback members bind without initializing a provider");
            var scoped = all.OfType<BlueprintActivatableAbility>().Where(b =>
                ProfileAudit.Audit.Paths.TryGetValue(b,out var path) && (path.StartsWith("Classes/",StringComparison.Ordinal) || path.StartsWith("Mythic/",StringComparison.Ordinal)))
                .Where(b => b.GetComponent<ActivatableAbilityResourceLogic>() != null).ToArray();
            foreach(var group in scoped.GroupBy(b => b.GetComponent<ActivatableAbilityResourceLogic>().SpendType).OrderBy(g => g.Key))
            {
                int admitted = group.Count(b => profile.Invoke(null,new object[]{b}) is object p && p.GetType().GetField("Excluded").GetValue(p) == null);
                lines.Add($"TOGGLE-SCOPE {group.Key}: {group.Count()} exported, {admitted} without structural command-profile exclusion (actor/policy not tested)");
            }

            HashSet<BlueprintUnitFact> Kit(string className, int level, string archetypeName = null)
            {
                var c = Find<BlueprintCharacterClass>(className);
                var roots = c.Progression.LevelEntries.Where(e => e.Level <= level)
                    .SelectMany(e => e.Features.Select(f => (e.Level, Feature:f))).ToList();
                if (archetypeName != null)
                {
                    var a = Find<BlueprintArchetype>(archetypeName);
                    foreach (var e in a.RemoveFeatures.Where(e => e.Level <= level))
                        foreach (var f in e.Features)
                        { int i = roots.FindIndex(x => x.Level == e.Level && x.Feature == f); if (i >= 0) roots.RemoveAt(i); }
                    roots.AddRange(a.AddFeatures.Where(e => e.Level <= level).SelectMany(e => e.Features.Select(f => (e.Level, Feature:f))));
                }
                var owned = new HashSet<BlueprintUnitFact>();
                void Grant(BlueprintUnitFact fact, int depth)
                {
                    if (fact == null || depth > 8 || !owned.Add(fact)) return;
                    if (fact is BlueprintProgression progression)
                        foreach (var e in progression.LevelEntries.Where(e => e.Level <= level)) foreach (var f in e.Features) Grant(f, depth + 1);
                    foreach (var facts in fact.GetComponents<AddFacts>()) foreach (var f in facts.Facts) Grant(f, depth + 1);
                    // Selection alternatives are not all granted. Tests add explicit chosen facts/states below.
                }
                foreach (var r in roots) Grant(r.Feature, 0);
                kits++;
                lines.Add($"TOGGLE-KIT {className}/{archetypeName ?? "base"} L{level}: {owned.Count} direct/progression/AddFacts facts; selections explicit");
                return owned;
            }
            bool Cost(ActivatableAbilityResourceLogic logic, HashSet<BlueprintUnitFact> owned, int expected, bool packed = false)
            {
                int before = owned.Count;
                var a = new object[] { logic, new Func<BlueprintUnitFact,bool>(owned.Contains), packed, 0 };
                bool ok = (bool)unitCost.Invoke(null, a);
                return ok && (int)a[3] == expected && before == owned.Count;
            }
            ActivatableAbilityResourceLogic Logic(string name) => Find<BlueprintActivatableAbility>(name).GetComponent<ActivatableAbilityResourceLogic>();
            var bard = Kit("BardClass", 1);
            bard.Add(Find<BlueprintUnitFact>("ExtraPerformance"));
            Check(Cost(Logic("InspireCourageToggleAbility"), bard, 1), "Bard L1 native round cost");
            var diva = Kit("BardClass", 13, "ChelishDivaArchetype");
            diva.Add(Find<BlueprintUnitFact>("ExtraPerformance"));
            diva.Add(Find<BlueprintUnitFact>("LingeringPerformance"));
            var rondo = Logic("BlazingRondoToggleAbility");
            Check(Cost(rondo, diva, 1), "Diva full kit ordinary cost");
            var increases = (List<BlueprintUnitFactReference>)typeof(ActivatableAbilityResourceLogic).GetField("m_ResourceCostIncreasingFacts", Instance).GetValue(rondo);
            diva.Add(increases.Single().Get());
            Check(Cost(rondo, diva, 2), "Diva actual increasing-fact transition");
            var priest = Kit("WarpriestClass", 7);
            Check(Cost(Logic("SacredArmorOnAbility"), priest, 1), "Warpriest L7 native minute cost");
            Check(Logic("SacredArmorOnAbility").SpendType == ActivatableAbilityResourceLogic.ResourceSpendType.OncePerMinute,
                "Sacred Armor uses timed renewal, not resource-as-rounds");
            var shaman = Kit("ShamanClass",8);
            var purity = Find<BlueprintUnitFact>("ShamanHexAuraOfPurityFeature");
            shaman.Add(purity);
            foreach(var facts in purity.GetComponents<AddFacts>()) foreach(var fact in facts.Facts) shaman.Add(fact);
            Check(Cost(Logic("ShamanHexAuraOfPurityActivatableAbility"),shaman,1), "Shaman Aura of Purity native minute cost");
            Check(!Find<BlueprintActivatableAbility>("ShamanHexAuraOfPurityActivatableAbility").DeactivateImmediately &&
                Find<BlueprintActivatableAbility>("ShifterAspectBearAbility").DeactivateImmediately,
                "actual timed blueprints distinguish persistent last-paid time from event-interruptible last-paid time");

            var blade = Kit("MagusClass", 20, "BladeboundArchetype");
            var blackBlade = Logic("BladeboundBlackBladeAbilityRight");
            var bladeUp = (List<BlueprintUnitFactReference>)typeof(ActivatableAbilityResourceLogic).GetField("m_ResourceCostIncreasingFacts", Instance).GetValue(blackBlade);
            blade.Remove(blackBlade.FreeBlueprint); foreach (var f in bladeUp) blade.Remove(f.Get());
            Check(Cost(blackBlade, blade, 1), "Bladebound full kit base cost");
            blade.Add(bladeUp.First().Get());
            Check(Cost(blackBlade, blade, 3), "native duplicate increasing entries each charge one");
            blade.Add(blackBlade.FreeBlueprint);
            Check(Cost(blackBlade, blade, 0), "native free fact in full kit");

            var inquisitor = Kit("InquisitorClass", 8);
            var paid = (BlueprintUnitFact)byId["c6ea4a6fcdf84133a7520b218d9351a4"];
            var watcher = (BlueprintUnitFact)byId["9b8bb2ce8f67e5b4fa634ed6a6671f7a"];
            var judgment = all.OfType<BlueprintActivatableAbility>().Where(b => b.GetComponent<ActivatableAbilityResourceLogic>()?.SpendType == ActivatableAbilityResourceLogic.ResourceSpendType.Judgment).ToArray();
            Check(judgment.Length == 15, "all fifteen supplied class judgments present");
            foreach (var bp in judgment)
            {
                var l = bp.GetComponent<ActivatableAbilityResourceLogic>();
                bool Activation(HashSet<BlueprintUnitFact> own, int expected)
                {
                    var a = new object[] { bp.Buff, l.RequiredResource, new Func<BlueprintUnitFact,bool>(own.Contains), 0 };
                    return (bool)activationCost.Invoke(null,a) && (int)a[3] == expected;
                }
                var own = new HashSet<BlueprintUnitFact>(inquisitor); own.Remove(paid); own.Remove(watcher); own.Remove(l.FreeBlueprint);
                Check(Activation(own,1), bp.name + " first activation spends one via watcher");
                own.Add(paid); Check(Activation(own,0), bp.name + " already paid, no second price");
                own.Remove(paid); own.Add(watcher); Check(Activation(own,0), bp.name + " existing Ignore watcher does not reactivate");
                own.Remove(watcher); own.Add(l.FreeBlueprint); Check(Activation(own,0), bp.name + " actual free-feature guard");
            }
            // Clone only the containers being changed; the supplied graph stays untouched. A shared-value guard
            // with a resource-spending branch must stay unknown, even though Smiting's real cosmetic/stat branches pass.
            T Clone<T>(T value) => (T)typeof(object).GetMethod("MemberwiseClone",Instance).Invoke(value,null);
            var smiting = Find<BlueprintBuff>("SmitingJudgmentBuffMagic");
            var altered = Clone(smiting);
            var originalActions = smiting.GetComponent<AddFactContextActions>();
            var alteredActions = Clone(originalActions);
            alteredActions.Activated = Clone(originalActions.Activated);
            alteredActions.Activated.Actions = originalActions.Activated.Actions.ToArray();
            var guard = Clone(alteredActions.Activated.Actions.OfType<Conditional>().First());
            guard.IfTrue = Clone(guard.IfTrue);
            guard.IfTrue.Actions = new GameAction[] { Find<BlueprintBuff>("JudgmentWatcherBuff").GetComponent<AddFactContextActions>()
                .Activated.Actions.OfType<Conditional>().Single().IfFalse.Actions.OfType<ContextSpendResource>().Single() };
            int guardIndex = Array.FindIndex(alteredActions.Activated.Actions,a => a is Conditional);
            alteredActions.Activated.Actions[guardIndex] = guard;
            altered.ComponentsArray = smiting.ComponentsArray.Select(c => c == originalActions ? alteredActions : c).ToArray();
            var unknownArgs = new object[] { altered, Logic("SmitingJudgmentAbilityMagic").RequiredResource,
                new Func<BlueprintUnitFact,bool>(inquisitor.Contains),0 };
            Check(!(bool)activationCost.Invoke(null,unknownArgs), "unknown guard with unequal resource branches stays fallback");
            foreach (var name in new[] { "SwordSaintPerfectStrikeAbility", "SwordSaintPerfectStrikeCritAbility", "DestructionDomainBaseActivateableAbility", "DestructionDomainBaseActivateableAbilitySeparatist" })
            {
                var bp = Find<BlueprintActivatableAbility>(name); var p = profile.Invoke(null,new object[]{bp});
                Check((string)p.GetType().GetField("Excluded").GetValue(p) != null, name + " remains outside command-toggle admission");
            }
            var cloned = (ActivatableAbilityResourceLogic)typeof(object).GetMethod("MemberwiseClone", Instance).Invoke(blackBlade,null);
            cloned.SpendType = (ActivatableAbilityResourceLogic.ResourceSpendType)65538;
            var noFacts = new HashSet<BlueprintUnitFact>();
            Check(Cost(cloned,noFacts,2,true), "installed TTT packed critical cost is two");
            Check(Cost(cloned,new HashSet<BlueprintUnitFact>{bladeUp.First().Get()},2,true), "TTT custom price ignores native increasing entries");
            Check(Cost(cloned,new HashSet<BlueprintUnitFact>{blackBlade.FreeBlueprint},0,true), "TTT custom free fact");
            var reduction = (ActivatableAbilityResourceLogic)typeof(object).GetMethod("MemberwiseClone", Instance).Invoke(rondo,null);
            typeof(ActivatableAbilityResourceLogic).GetField("m_ResourceCostDecreasingFacts", Instance).SetValue(reduction,
                new List<BlueprintUnitFactReference>{ increases.First(), increases.First() });
            Check(Cost(reduction,diva,0), "native reductions clamp a two-unit cost to zero");
            typeof(ActivatableAbilityResourceLogic).GetField("m_UseListForFreeBlueprints", Instance).SetValue(reduction,true);
            typeof(ActivatableAbilityResourceLogic).GetField("m_FreeBlueprints", Instance).SetValue(reduction,new[]{increases.First()});
            Check(Cost(reduction,diva,0), "native free list is evaluated against current kit");
            var runtimeType = typeof(EntityFactComponentDelegate<,>).GetNestedType("ComponentRuntime",BindingFlags.Public | BindingFlags.NonPublic)
                .MakeGenericType(typeof(UnitEntityData),typeof(ActivatableAbilityResourceLogic.ComponentData));
            var runtime = (EntityFactComponent)RuntimeHelpers.GetUninitializedObject(runtimeType);
            var dataField = runtimeType.GetField("m_Data",Instance);
            Check(!runtime.TryGetData<ActivatableAbilityResourceLogic.ComponentData>(out _) && dataField.GetValue(runtime) == null,
                "native TryGetData does not create missing timer data");
            var timer = new ActivatableAbilityResourceLogic.ComponentData { RoundsToSpendingCounter = 7 };
            dataField.SetValue(runtime,timer);
            Check(runtime.TryGetData<ActivatableAbilityResourceLogic.ComponentData>(out var readTimer) &&
                ReferenceEquals(timer,readTimer) && readTimer.RoundsToSpendingCounter == 7,
                "native renewal counter reads existing runtime data without resetting it");
            lines.Add($"TOGGLE-COST {checks} checks, {kits} progression-shaped kits, failures {failures}; no actor, command, mod initializer or Unity performance execution");
            foreach(var line in lines)Console.WriteLine(line);
            File.AppendAllLines(args.Last(),lines);
            return failures == 0 ? 0 : 1;
        }
    }
}
