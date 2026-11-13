// Production option registration and identity, using native scalar data only. No actor, cast, availability or Unity query.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;

namespace ProfileAudit
{
    internal static class AbilityDiscoveryCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("ABILITY-DISCOVERY FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                var blueprints = loaded.OfType<BlueprintAbility>().Take(2).ToArray();
                if (blueprints.Length < 2) throw new InvalidOperationException("Two actual ability blueprints are required");
                var add = typeof(AbilityDiscovery).GetMethod("AddOption", all);
                var conversionKey = typeof(AbilityDiscovery).GetMethod("ConversionKey", all);
                var parent = typeof(AbilityData).GetField("m_ConvertedFrom", all);
                AbilityData Data(BlueprintAbility blueprint, int level = 1, AbilityData source = null)
                {
                    var data = (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                    typeof(AbilityData).GetField("Blueprint", all).SetValue(data, blueprint);
                    data.OverrideSpellLevel = level;
                    parent.SetValue(data, source);
                    return data;
                }
                string Conversion(AbilityData data) => (string)conversionKey.Invoke(null, new object[] { data });
                var result = new List<AbilityData>(); var seen = new HashSet<string>();
                var indices = new Dictionary<string, int>(StringComparer.Ordinal);
                void Add(AbilityData data) => add.Invoke(null, new object[] { result, seen, indices, data });
                var root = Data(blueprints[0]); var otherRoot = Data(blueprints[1]);
                Add(root); Add(Data(blueprints[0]));
                Check(result.Count == 1 && ReferenceEquals(result[0], root), "a duplicate normal option preserves the first object and order");
                Check(indices.Count == 0, "normal options cannot occupy a conversion index");
                var first = Data(blueprints[1], source: root); Add(first);
                Check(result.Count == 2 && indices[Conversion(first)] == 1, "a directly encountered converted option registers its actual result index");
                var sameLevel = Data(blueprints[1], source: otherRoot); Add(sameLevel);
                Check(Conversion(first) == Conversion(sameLevel) && AbilityDiscovery.Key(first) == AbilityDiscovery.Key(sameLevel),
                    "equivalent conversions retain the original same-level key regardless of the source spell");
                Check(result.Count == 2 && ReferenceEquals(result[1], first), "an equivalent conversion neither appends nor replaces the first option during registration");
                var higher = Data(blueprints[1], source: Data(blueprints[0], 2)); Add(higher);
                Check(Conversion(first) != Conversion(higher) && indices[Conversion(higher)] == 2, "different source slot levels stay separate and ordered");
                var quickened = Data(blueprints[1], source: root);
                quickened.MetamagicData = new MetamagicData();
                typeof(MetamagicData).GetField("<MetamagicMask>k__BackingField", all).SetValue(quickened.MetamagicData, Metamagic.Quicken);
                Add(quickened);
                Check(Conversion(first) != Conversion(quickened) && indices[Conversion(quickened)] == 3, "metamagic conversions retain separate identities");
                var heightened = Data(blueprints[1], source: root);
                heightened.MetamagicData = new MetamagicData { HeightenLevel = 2 };
                Add(heightened);
                Check(Conversion(first) != Conversion(heightened) && indices[Conversion(heightened)] == 4, "heightening retains its original key component");
                Check(AbilityDiscovery.Key(root) != AbilityDiscovery.Key(Data(blueprints[0], source: root)), "a normal option and its same-blueprint conversion remain distinct");

                AbilityData FromItem(string id)
                {
                    var item = (ItemEntityUsable)RuntimeHelpers.GetUninitializedObject(typeof(ItemEntityUsable));
                    typeof(EntityDataBase).GetField("<UniqueId>k__BackingField", all).SetValue(item, id);
                    var fact = (Ability)RuntimeHelpers.GetUninitializedObject(typeof(Ability));
                    typeof(EntityFact).GetField("<SourceItem>k__BackingField", all).SetValue(fact, item);
                    var source = Data(blueprints[0]); typeof(AbilityData).GetField("m_Fact", all).SetValue(source, fact);
                    return Data(blueprints[1], source: source);
                }
                var itemA = FromItem("discovery-fixture-A"); var itemB = FromItem("discovery-fixture-B");
                Check(AbilityDiscovery.Key(itemA) != AbilityDiscovery.Key(itemB), "normal discovery still distinguishes exact source item IDs");
                Check(Conversion(itemA) == Conversion(itemB) && Conversion(itemA) == Conversion(first),
                    "the existing conversion equivalence is preserved rather than silently adding item identity");
                Add(itemA); Add(itemB);
                Check(result.Count == 7 && indices[Conversion(first)] == 1 && ReferenceEquals(result[1], first),
                    "multiple directly registered equivalents preserve the first converted index, matching FindIndex");
                result[1] = sameLevel;
                Check(indices[Conversion(first)] == 1 && ReferenceEquals(result[indices[Conversion(first)]], sameLevel),
                    "replacement keeps the stable index and surrounding order");
                Check(AbilityDiscovery.Gather(null).Count == 0, "null actor discovery remains empty");

                // The compiler can put capturing local functions on the containing type with a ref display-class
                // argument. Include that actual production location as well as nested closure methods.
                var localMethods = typeof(AbilityDiscovery).GetMethods(all)
                    .Concat(typeof(AbilityDiscovery).GetNestedTypes(all).SelectMany(t => t.GetMethods(all)))
                    .Where(m => m.Name.Contains("<Gather>") && m.GetMethodBody() != null).ToArray();
                var generated = localMethods.SelectMany(SaveBoostCheck.ReferencedMembers).ToArray();
                Check(generated.Any(m => m.Name == "AddOption") && generated.Any(m => m.Name == "TryGetValue"),
                    "compiled discovery uses production registration and keyed conversion lookup");
                Check(!generated.Any(m => m.Name == "FindIndex"), "compiled discovery has no result-wide conversion scan");
                var replace = SaveBoostCheck.ReferencedMembers(localMethods.Single(m => m.Name.Contains("g__AddConversions"))).ToList();
                int lookup = replace.FindIndex(m => m.Name == "TryGetValue");
                int priorAvailable = replace.FindIndex(m => m.DeclaringType == typeof(AbilityData) && m.Name == "get_IsAvailable");
                int nextAvailable = replace.FindIndex(priorAvailable + 1, m => m.DeclaringType == typeof(AbilityData) && m.Name == "get_IsAvailable");
                int replaceAt = replace.FindIndex(m => m.DeclaringType == typeof(List<AbilityData>) && m.Name == "set_Item");
                Check(lookup >= 0 && priorAvailable > lookup && nextAvailable > priorAvailable && replaceAt > nextAvailable,
                    "the existing native availability-based source replacement is retained");
                var identities = SaveBoostCheck.ReferencedMembers(conversionKey).ToArray();
                Check(identities.Any(m => m.Name == "MetamagicTag") && identities.Any(m => m.Name == "SafeLevel") &&
                      identities.Any(m => m.Name == "get_Spellbook") && identities.Any(m => m.Name == "FusionIdentityOf"),
                    "conversion lookup retains book, metamagic, source level and fusion identity readers");
                var setup = SaveBoostCheck.ReferencedMembers(typeof(TacticalEngine).GetMethod("AddAbilityCandidates", all)).ToArray();
                Check(new[] { "NoteDiscovery", "NotePreScoreChecks", "NoteAvailability", "NoteProfileRead" }.All(n => setup.Any(m => m.Name == n)),
                    "shared candidate builder records discovery and pre-score checks outside the callback timer");
            }
            catch (Exception ex) { bad++; lines.Add("ABILITY-DISCOVERY FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"ABILITY-DISCOVERY {checks} checks, failures {bad}; production ordered conversion indexing and native scalar identity; no initialized actor, cast, availability or Unity timing execution");
            return lines;
        }
    }
}
