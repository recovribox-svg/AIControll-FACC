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
                    // ★ caster1 review: the played native BurnNumberProperty has a non-null Settings object.
                    // Exercise the shipped reader through cloned actual mental burn graphs. All transforms execute
                    // the game's PropertySettings.Apply before its ContextRankConfig, without invoking a getter/actor.
                    PropertySettings Settings(params (string Name, object Value)[] fields)
                    {
                        var settings = new PropertySettings();
                        foreach (var field in fields)
                        {
                            var info = typeof(PropertySettings).GetField(field.Name, instance);
                            info.SetValue(settings, info.FieldType.IsEnum ? Enum.Parse(info.FieldType, (string)field.Value) : field.Value);
                        }
                        return settings;
                    }
                    BlueprintFeature SettingsGraph(PropertySettings settings)
                    {
                        var graph = Graph();
                        var rank = graph.Buff.GetComponent<ContextRankConfig>();
                        var field = typeof(ContextRankConfig).GetField("m_CustomProperty", instance);
                        var property = Clone(((BlueprintUnitPropertyReference)field.GetValue(rank)).Get());
                        property.ComponentsArray = property.ComponentsArray.Select(Clone).ToArray();
                        property.GetComponent<Kingmaker.UnitLogic.Class.Kineticist.Properties.KineticistBurnPropertyGetter>().Settings = settings;
                        field.SetValue(rank, Reference<BlueprintUnitPropertyReference>(property));
                        return graph.Feature;
                    }
                    var defaults = Read(SettingsGraph(Settings()), 3);
                    Check(Modeled(defaults), "non-null default native burn settings are recognized");
                    Delta(defaults, 1, 0, -2f / 3f, "default settings preserve mental Will consequence instead of HP fallback");
                    var divided = SettingsGraph(Settings(("m_Progression", "Div2")));
                    Delta(Read(divided, 2), 1, 0, 0, "native getter division plateau before doubled mental rank");
                    var boundary = Read(divided, 3);
                    Delta(boundary, 1, 0, -2f / 3f, "native getter division boundary before doubled mental rank");
                    Delta(boundary, 1, 0, -2f / 3f, "native settings repeated quote remains cached and unchanged");
                    var limited = SettingsGraph(Settings(("m_Negate", true), ("m_LimitType", "MinMax"), ("m_Min", -3), ("m_Max", 0)));
                    Delta(Read(limited, 2), 1, 0, 2f / 3f, "native negation then limits then rank/stat multiplier");
                    Delta(Read(limited, 3), 1, 0, 0, "native getter cap before rank keeps later burn at plateau");
                    var malformed = Read(SettingsGraph(Settings(("m_Progression", "DivStep"), ("m_StepLevel", 0))), 3);
                    var malformedArgs = new object[] { malformed, 1, 0, 0f };
                    Check(!(bool)delta.Invoke(null, malformedArgs), "invalid native settings retain fallback rather than authoritative free burn");
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
                    // ★ v0.3.30 competence2: a second burn-ranked buff without a stat modifier passes only as a listed
                    // benefit shape; fact actions that deal damage keep the whole price unknown (fail-closed).
                    var hurting = Graph();
                    var extra = Clone((ContextActionApplyBuff)hurting.Trigger.Action.Actions.Single());
                    var extraBuff = Clone(hurting.Buff);
                    var factActions = Bare<AddFactContextActions>();
                    factActions.Activated = new ActionList { Actions = new GameAction[] { Bare<ContextActionDealDamage>() } };
                    extraBuff.ComponentsArray = new BlueprintComponent[] { factActions };
                    typeof(ContextActionApplyBuff).GetField("m_Buff", instance).SetValue(extra, Reference<BlueprintBuffReference>(extraBuff));
                    hurting.Trigger.Action = new ActionList { Actions = new GameAction[] { hurting.Trigger.Action.Actions.Single(), extra } };
                    Unknown(hurting.Feature, "second burn buff whose fact actions deal damage", "unsupported consequence component");
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
            // ★ v0.3.30 competence2 (PR #20 review): actor-shaped fact sets, not single features. From class level 6
            // ElementalOverflowProgression grants a second burn trigger (fortification/stat-choice buff), and the
            // Adaptation talents add energy-resistance triggers; alternate burn features reuse BurnEffectBuff.
            int kits = 0;
            void Kit(string label, bool modeled, int hp, float saves, params string[] guids)
            {
                var facts = new List<BlueprintScriptableObject>();
                foreach (var guid in guids)
                {
                    if (!byGuid.TryGetValue(guid, out var bp) || !(bp is BlueprintScriptableObject fact))
                    { lines.Add("BURN-CONSEQUENCE SKIPPED: kit " + label + " (" + guid + " not loaded)"); return; }
                    facts.Add(fact);
                }
                kits++;
                var model = read.Invoke(null, new object[] { facts.ToArray(), 3, 7, 11 });
                var reason = model.GetType().GetField("Unknown", instance).GetValue(model);
                Check(Modeled(model) == modeled, $"kit {label}: modeled={Modeled(model)} ({reason ?? "-"})");
                if (modeled && Modeled(model)) Delta(model, 1, hp, saves, "kit " + label + " one burn");
            }
            const string OverflowEffect = "74e4520801f18ae42af3acf261ac7e90", Aerial = "c8719b3c5c0d4694cb13abcc3b7e893b",
                Heat = "2825e3a53c76ad944a47c5c44fb6109f", Cold = "1ff5d6e76b7c2fa48be555b77d1ad8b2",
                DarkElementalist = "42c5a9a8661db2f47aedf87fb8b27aaf", OverwhelmingSoul = "a3051f965d971ed44b9c6c63bf240b79",
                Burnout = "090d39a5813d4537ba24b55fff7fe6be";
            Kit("ordinary L6+ with Overflow effect", true, 11, 0f, Ordinary, OverflowEffect);
            Kit("ordinary L6+ with Overflow and all Adaptations", true, 11, 0f, Ordinary, OverflowEffect, Aerial, Heat, Cold);
            Kit("mental with Aerial Adaptation", true, 0, -2f / 3f, Mental, Aerial);
            Kit("mental with Heat and Cold Adaptation", true, 0, -2f / 3f, Mental, Heat, Cold);
            Kit("Dark Elementalist burn", true, 11, 0f, DarkElementalist);
            Kit("Overwhelming Soul burn", true, 11, 0f, OverwhelmingSoul);
            Kit("Elemental Engine conditional burnout stays unknown", false, 0, 0f, Ordinary, Burnout);
            Kit("benefit triggers alone are not a known cost", false, 0, 0f, OverflowEffect, Aerial);
            var unknown = Read(null, 0);
            Check(!Modeled(unknown), "missing trigger is unknown, not free burn");
            var unknownArgs = new object[] { unknown, 1, 0, 0f };
            Check(!(bool)delta.Invoke(null, unknownArgs), "unknown graph cannot produce an authoritative zero consequence");
            failures = bad;
            lines.Insert(0, $"BURN-CONSEQUENCE {checks} checks, {fixtureCount}/2 actual feature fixtures, {kits}/8 actor-shaped kits, failures {bad}");
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
