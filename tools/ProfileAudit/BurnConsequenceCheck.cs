// ★ v0.3.30 competence: shipped component reader on actual loaded burn feature/buff/property graphs, without an actor.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Facts.Restrictions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Properties;
using Kingmaker.EntitySystem.Properties.Getters;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Properties;

namespace ProfileAudit
{
    internal static class BurnConsequenceCheck
    {
        // Fixture identities select evidence, never runtime recognition or valuation.
        private const string Ordinary = "57e3577a0eb53294e9d7cc649d5239a3";
        private const string Mental = "2fa48527ba627254ba9bf4556330a4d4";

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
            var read = typeof(TacticalEngine).GetMethod("ReadBurnConsequences", flags)
                ?? throw new MissingMethodException("TacticalEngine.ReadBurnConsequences");
            var delta = typeof(TacticalEngine).GetMethod("BurnConsequenceDelta", flags)
                ?? throw new MissingMethodException("TacticalEngine.BurnConsequenceDelta");
            var lines = new List<string>();
            int bad = 0, checks = 0, fixtureCount = 0;
            var byGuid = loaded.ToDictionary(b => b.AssetGuid.ToString());
            void Check(bool ok, string what) { checks++; if (!ok) { bad++; lines.Add("BURN-CONSEQUENCE FAILED: " + what); } }
            object Read(BlueprintScriptableObject source, int accepted) => read.Invoke(null,
                new object[] { source == null ? Array.Empty<BlueprintScriptableObject>() : new[] { source }, accepted, 7, 11 });
            bool Modeled(object model) => (bool)model.GetType().GetField("Modeled", instance).GetValue(model);
            void Delta(object model, int burn, int hp, float saves, string label)
            {
                var args = new object[] { model, burn, 0, 0f };
                bool supported = (bool)delta.Invoke(null, args);
                Check(supported && (int)args[2] == hp && Math.Abs((float)args[3] - saves) < 0.0001f,
                    $"{label}: supported={supported}, HP={args[2]}, saves={args[3]}");
            }
            void Fixture(string guid, bool mental, string label)
            {
                if (!byGuid.TryGetValue(guid, out var bp) || !(bp is BlueprintScriptableObject source))
                { lines.Add("BURN-CONSEQUENCE SKIPPED: " + label + " feature not loaded"); return; }
                fixtureCount++;
                var model = Read(source, 3);
                Check(Modeled(model), label + " direct graph recognized: " + model.GetType().GetField("Unknown", instance).GetValue(model));
                if (!Modeled(model)) return;
                Delta(model, 0, 0, 0, label + " unchanged burn");
                Delta(model, 1, mental ? 0 : 11, mental ? -2f / 3f : 0f, label + " one burn");
                Delta(model, 2, mental ? 0 : 22, mental ? -4f / 3f : 0f, label + " two burn");
                Delta(model, 1, mental ? 0 : 11, mental ? -2f / 3f : 0f, label + " repeated quote does not accumulate/mutate");
                var earlier = Read(source, 0);
                Delta(earlier, 2, mental ? 0 : 22, mental ? -4f / 3f : 0f, label + " zero accepted transition");
                if (mental)
                {
                    // Rebuild only small harness-local graph clones. Cached references point to clones, never to
                    // ResourcesLibrary; loaded blueprints/components and any native actors remain untouched.
                    (BlueprintFeature Feature, AddKineticistBurnValueChangedTrigger Trigger, BlueprintBuff Buff) Graph()
                    {
                        var feature = Clone((BlueprintFeature)source);
                        var trigger = Clone(source.GetComponent<AddKineticistBurnValueChangedTrigger>());
                        var apply = Clone((ContextActionApplyBuff)trigger.Action.Actions.Single());
                        var buff = Clone(apply.Buff);
                        buff.ComponentsArray = buff.ComponentsArray.Select(Clone).ToArray();
                        typeof(ContextActionApplyBuff).GetField("m_Buff", instance).SetValue(apply,
                            Reference<BlueprintBuffReference>(buff));
                        trigger.Action = new ActionList { Actions = new GameAction[] { apply } };
                        feature.ComponentsArray = new BlueprintComponent[] { trigger };
                        return (feature, trigger, buff);
                    }
                    void Unknown(BlueprintScriptableObject altered, string what, string expectedReason)
                    {
                        var unsupported = Read(altered, 3);
                        var reason = (string)unsupported.GetType().GetField("Unknown", instance).GetValue(unsupported);
                        Check(!Modeled(unsupported) && reason != null && reason.Contains(expectedReason), what + " is explicitly unknown: " + reason);
                        var args = new object[] { unsupported, 1, 0, 0f };
                        Check(!(bool)delta.Invoke(null, args), what + " cannot produce an authoritative zero cost");
                    }
                    Check(Modeled(Read(Graph().Feature, 3)), "unchanged native graph clone remains supported");
                    var conditional = Graph();
                    conditional.Trigger.Action = new ActionList
                    {
                        Actions = new GameAction[] { Bare<Conditional>() }
                    };
                    Unknown(conditional.Feature, "conditional burn-trigger action graph", "direct permanent replacement buff");
                    var custom = Graph();
                    var customRank = custom.Buff.GetComponent<ContextRankConfig>();
                    var propertyField = typeof(ContextRankConfig).GetField("m_CustomProperty", instance);
                    var property = Clone(((BlueprintUnitPropertyReference)propertyField.GetValue(customRank)).Get());
                    property.ComponentsArray = new BlueprintComponent[] { Bare<UnsupportedBurnPropertyGetter>() };
                    propertyField.SetValue(customRank, Reference<BlueprintUnitPropertyReference>(property));
                    Unknown(custom.Feature, "custom non-native burn property getter", "additional or unsupported arithmetic");
                    var minimal = Graph();
                    minimal.Buff.GetComponent<AddContextStatBonus>().HasMinimal = true;
                    Unknown(minimal.Feature, "minimal-value burn stat modifier", "unsupported consequence component");
                    var restricted = Graph();
                    typeof(AddContextStatBonus).GetField("Restrictions", instance).SetValue(
                        restricted.Buff.GetComponent<AddContextStatBonus>(), new RestrictionCalculator
                        {
                            Property = new PropertyCalculator { Getters = new[] { Bare<CharacterLevelGetter>() } }
                        });
                    Unknown(restricted.Feature, "nonempty burn stat restriction", "conditional restriction");
                    // Counterfactual capped rank config, cloned out of the loaded graph. This exercises native cap
                    // progression at a transition without modifying the actual blueprint, a live actor or its contexts.
                    var stats = (IList)model.GetType().GetField("Stats", instance).GetValue(model);
                    ((IDictionary)model.GetType().GetField("Deltas", instance).GetValue(model)).Clear();
                    foreach (var stat in stats)
                    {
                        var field = stat.GetType().GetField("Rank", instance);
                        var original = (ContextRankConfig)field.GetValue(stat);
                        var clone = (ContextRankConfig)RuntimeHelpers.GetUninitializedObject(typeof(ContextRankConfig));
                        for (var t = typeof(ContextRankConfig); t != null; t = t.BaseType)
                            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                                f.SetValue(clone, f.GetValue(original));
                        typeof(ContextRankConfig).GetField("m_UseMax", instance).SetValue(clone, true);
                        typeof(ContextRankConfig).GetField("m_Max", instance).SetValue(clone, 5);
                        field.SetValue(stat, clone);
                    }
                    Delta(model, 2, 0, 0, "capped mental rank: already capped");
                    model.GetType().GetField("Accepted", instance).SetValue(model, 1);
                    ((IDictionary)model.GetType().GetField("Deltas", instance).GetValue(model)).Clear();
                    Delta(model, 2, 0, -1f, "capped mental rank: cross cap adds only three Will penalty");
                    foreach (var stat in stats) stat.GetType().GetField("CurrentAmount", instance).SetValue(stat, (int?)0);
                    ((IDictionary)model.GetType().GetField("Deltas", instance).GetValue(model)).Clear();
                    Delta(model, 1, 0, -4f / 3f, "missing held buff: next burn applies full total instead of rank increment");
                }
            }
            Fixture(Ordinary, false, "ordinary");
            Fixture(Mental, true, "mental");
            var unknown = Read(null, 0);
            Check(!Modeled(unknown), "missing trigger is unknown, not free burn");
            var unknownArgs = new object[] { unknown, 1, 0, 0f };
            Check(!(bool)delta.Invoke(null, unknownArgs), "unknown graph cannot produce an authoritative zero consequence");
            failures = bad;
            lines.Insert(0, $"BURN-CONSEQUENCE {checks} checks, {fixtureCount}/2 actual feature fixtures, failures {bad}");
            return lines;
        }

        private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static T Clone<T>(T original)
        {
            var result = RuntimeHelpers.GetUninitializedObject(original.GetType());
            for (var t = original.GetType(); t != null; t = t.BaseType)
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    field.SetValue(result, field.GetValue(original));
            return (T)result;
        }

        private static TReference Reference<TReference>(SimpleBlueprint blueprint) where TReference : BlueprintReferenceBase, new()
        {
            var reference = new TReference();
            typeof(BlueprintReferenceBase).GetProperty("Cached", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(reference, blueprint);
            return reference;
        }

        // The production reader must reject this type without invoking its arbitrary native value calculation.
        private sealed class UnsupportedBurnPropertyGetter : PropertyValueGetter
        {
            protected override int GetBaseValue(UnitEntityData unit) => throw new InvalidOperationException("Custom property evaluator must not run in burn recognition");
        }
    }
}
