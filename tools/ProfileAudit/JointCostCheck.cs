// Native scalar getters, actual boost/resource graphs and compiled read-only adapters; no initialized actors/casts.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;

namespace ProfileAudit
{
    internal static class JointCostCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("JOINT-COST FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                var type = typeof(TacticalEngine).GetNestedType("SpellCostReader", all);
                var source = type.GetMethod("SlotSource", all);
                var parentField = typeof(AbilityData).GetField("m_ConvertedFrom", all);
                var extra = typeof(AbilityData).GetField("<ExtraSpellSlotCost>k__BackingField", all);
                AbilityData Empty() => (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                var root = Empty(); var middle = Empty(); var converted = Empty();
                parentField.SetValue(middle, root); parentField.SetValue(converted, middle);
                Check(ReferenceEquals(source.Invoke(null, new object[] { converted }), root), "conversion resolves its actual source, not another same-name spell");
                Check(root.SpellSlotCost == 1 && converted.SpellSlotCost == 1, "native default slot cost is one even through conversions");
                extra.SetValue(root, 2); extra.SetValue(middle, 3); extra.SetValue(converted, 4);
                Check(converted.SpellSlotCost == 10, "native recursive extra slot costs accumulate across conversions");
                Check(root.SpellSlotCost == 3 && middle.SpellSlotCost == 6, "conversion queries do not mutate source slot costs");
                parentField.SetValue(root, converted);
                Check(source.Invoke(null, new object[] { converted }) == null, "cyclic conversion is bounded before native recursive pricing");
                parentField.SetValue(root, null);
                var deep = root;
                for (int i = 0; i < 20; i++) { var next = Empty(); parentField.SetValue(next, deep); deep = next; }
                Check(source.Invoke(null, new object[] { deep }) == null, "unbounded conversion chain is not priced");
                var book = (Spellbook)RuntimeHelpers.GetUninitializedObject(typeof(Spellbook));
                var counts = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
                typeof(Spellbook).GetField("m_SpontaneousSlots", all).SetValue(book, counts);
                Check(book.GetSpontaneousSlots(3) == 3 && book.GetSpontaneousSlots(7) == 7, "native remaining-slot getter preserves the level bucket");
                Check(counts.SequenceEqual(Enumerable.Range(0, 11)), "native slot queries do not spend anything");

                var blueprints = loaded.OfType<BlueprintAbility>().ToArray();
                foreach (var name in new[] { "ArcanistArcaneReservoirCLAbility", "ArcanistArcaneReservoirDCAbility" })
                {
                    var ability = blueprints.FirstOrDefault(b => b.name == name);
                    if (ability == null) { lines.Add("JOINT-COST SKIPPED: " + name + " not loaded"); continue; }
                    var logic = ability.GetComponent<AbilityResourceLogic>();
                    Check(logic != null && logic.GetType() == typeof(AbilityResourceLogic) && logic.IsSpendResource &&
                          !logic.CostIsCustom && logic.RequiredResource != null, name + " has the inspected native activation resource");
                    Check(logic.ResourceCostIncreasingFacts.Count == 0 && logic.ResourceCostDecreasingFacts.Count == 0,
                        name + " scalar fixture has no actor-dependent cost-fact branches");
                    if (logic.ResourceCostIncreasingFacts.Count == 0 && logic.ResourceCostDecreasingFacts.Count == 0)
                    {
                        var data = Empty(); typeof(AbilityData).GetField("Blueprint", all).SetValue(data, ability);
                        Check(data.ResourceCost == logic.Amount, name + " native AbilityData.ResourceCost queries the actual component price");
                        Check(ReferenceEquals(data.Resource, logic.RequiredResource), name + " uses the actual resource identity");
                    }
                }
                var read = type.GetMethod("Read", all);
                var resource = type.GetMethod("ReadResource", all);
                var prepared = type.GetMethod("PreparedSlots", all);
                var pool = type.GetMethod("AddPool", all);
                var members = SaveBoostCheck.ReferencedMembers(read).ToList();
                int rootIndex = members.FindIndex(m => m.Name == "SlotSource"), costIndex = members.FindIndex(m => m.Name == "get_SpellSlotCost");
                Check(rootIndex >= 0 && costIndex > rootIndex, "production bounds the source chain before the native slot-cost getter");
                Check(members.Any(m => m.Name == "get_SaveSpellbookSlot") && members.Any(m => m.Name == "get_CheckStatForSpellFool") &&
                      members.Any(m => m.Name == "GetSpellLevel") && members.Any(m => m.Name == "GetSpontaneousSlots"),
                    "production reads native slot-saving/level/remaining rules");
                Check(members.Any(m => m.Name == "get_UsagesPerDayResource") && members.Any(m => m.Name == "ReadResource"),
                    "daily usage and explicit resource logic are both accounted for");
                var rs = SaveBoostCheck.ReferencedMembers(resource).ToList();
                Check(rs.Any(m => m.Name == "get_ResourceLogic") && rs.Any(m => m.Name == "get_ResourceCost") &&
                      rs.Any(m => m.Name == "get_OverrideRequiredResource") && rs.Any(m => m.Name == "get_ConvertedFrom"),
                    "resource adapter retains override/source/current native cost semantics");
                int customIndex = rs.FindIndex(m => m.Name == "CostIsCustom"), resourceCostIndex = rs.FindIndex(m => m.Name == "get_ResourceCost");
                Check(customIndex >= 0 && resourceCostIndex > customIndex,
                    "uninspected custom calculators are withheld before calling them");
                var ps = SaveBoostCheck.ReferencedMembers(prepared).ToList();
                Check(ps.Any(m => m.Name == "GetMemorizedSpellSlots") && ps.Any(m => m.Name == "get_SpellShell") &&
                      ps.Any(m => m.Name == "Equals") && ps.Any(m => m.Name == "Reverse"), "prepared eligibility/order follows native spending");
                Check(SaveBoostCheck.ReferencedMembers(pool).Any(m => m.Name == "GetResourceAmount"), "resource balances come from the game's collection");
                foreach (var method in new[] { read, resource, prepared, pool, source })
                    Check(!SaveBoostCheck.ReferencedMembers(method).Any(m => new[] { "Spend", "SpendFromSpellbook", "SpendResources", "Restore", "RunAction", "AddBuff", "Trigger", "TriggerRule", "Recalculate", "CreateContext" }.Contains(m.Name)),
                        method.Name + " performs no spend/refund/action/rule/context preview");
                var quicken = typeof(TacticalEngine).GetMethod("QuickenGain", all);
                var qs = SaveBoostCheck.ReferencedMembers(quicken).ToList();
                Check(qs.Any(m => m.Name == "Invoke") && qs.Any(m => m.Name == "MetamagicFor"), "Quicken asks for its second action lazily after native eligibility");
                var coupling = SaveBoostCheck.ReferencedMembers(typeof(TacticalEngine).GetMethod("CoupleSpellBoosts", all)).ToList();
                Check(coupling.Any(m => m.Name == "CanPay") && coupling.Any(m => m.Name == "BoostGain") && coupling.Any(m => m.Name == "FirstThisFight"),
                    "coupling checks joint cost and bounds failure diagnostics");
                var generated = typeof(TacticalEngine).GetNestedTypes(all).SelectMany(t => t.GetMethods(all))
                    .Where(m => m.Name.Contains("<CoupleSpellBoosts>") && m.GetMethodBody() != null)
                    .SelectMany(SaveBoostCheck.ReferencedMembers).OfType<MethodBase>().ToArray();
                Check(generated.Any(m => m.DeclaringType == type && m.Name == "CanPay" && m.GetParameters().Length == 3),
                    "lazy second-action selection validates all three proposed costs");
            }
            catch (Exception ex) { bad++; lines.Add("JOINT-COST FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"JOINT-COST {checks} checks, failures {bad}; native scalar getters, exported costs and compiled read-only routes; no initialized actor, spend, cast or Unity timing execution");
            return lines;
        }
    }
}
