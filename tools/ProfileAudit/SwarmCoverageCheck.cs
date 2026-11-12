using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace ProfileAudit
{
    internal static class SwarmCoverageCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            var all = loaded.ToList();
            var reader = typeof(CombatMath).GetMethod("ReadSwarmCastBlueprint", BindingFlags.NonPublic | BindingFlags.Static);
            void Check(bool ok, string label) { checks++; if (!ok) { bad++; lines.Add("SWARM-COVERAGE FAILED: " + label); } }
            var names = new[] { "CorruptedRatSwarmDamageAbility", "CR14_BhogaSwarmDamageAbility", "CR21_BhogaSwarmDamageAbility" };
            var abilities = names.Select(n => all.OfType<BlueprintAbility>().SingleOrDefault(b => b.name == n)).ToArray();
            if (abilities.Any(a => a == null))
            { failures = 0; return new List<string> { "SWARM-COVERAGE SKIPPED: required Rat/Bhoga/Gluttonous Bhoga input graphs are absent; this input subset does not establish swarm cast coverage" }; }
            try
            {
                foreach (var bp in abilities)
                    Check(reader?.Invoke(null, new object[] { bp }) == bp.GetComponent<AbilityTargetsAround>(), bp.name + " native cast selected");
                Check(abilities[1].EffectOnEnemy == AbilityEffectOnUnit.None, "Bhoga damage remains readable despite absent harmful label");
                var range = abilities[0].CustomRange.Meters;
                Check(range < 0f && Math.Abs(abilities[0].GetComponent<AbilityTargetsAround>().AoERadius.Meters - 1.524f) < .001f,
                    "actual negative custom range and separate five-foot target-centred radius");

                var cloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
                BlueprintAbility Copy(Action<BlueprintAbility, AbilityTargetsAround> change)
                {
                    var bp = (BlueprintAbility)cloneMethod.Invoke(abilities[0], null);
                    var around = (AbilityTargetsAround)cloneMethod.Invoke(abilities[0].GetComponent<AbilityTargetsAround>(), null);
                    bp.ComponentsArray = abilities[0].ComponentsArray.Select(c => c is AbilityTargetsAround ? around : c).ToArray();
                    change(bp, around);
                    return bp;
                }
                void Reject(BlueprintAbility bp, string label) => Check(reader.Invoke(null, new object[] { bp }) == null, label);
                Reject(Copy((bp, a) => bp.Range = AbilityRange.Close), "ordinary ranged burst is not a swarm space");
                Reject(Copy((bp, a) => bp.CanTargetFriends = true), "uninspected anchor allegiance stays generic");
                Reject(Copy((bp, a) => typeof(AbilityTargetsAround).GetField("m_Condition", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(a, new ConditionsChecker { Conditions = new Condition[] { new Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionIsEnemy() } })),
                    "uninspected selector condition stays generic");
                Reject(Copy((bp, a) => bp.ComponentsArray = bp.ComponentsArray.Concat(new BlueprintComponent[] { a }).ToArray()),
                    "multiple selectors stay generic");

                var carrier = all.OfType<BlueprintBuff>().Single(b => b.name == "CorruptedRatSwarmDamageBuff");
                var ordinary = all.OfType<BlueprintBuff>().Single(b => b.name == "FaerieFireBuff");
                var blueprint = typeof(EntityFact).GetField("<Blueprint>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                var active = typeof(EntityFact).GetField("<IsActive>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                EntityFact Fact(BlueprintFact bp, bool on)
                {
                    var fact = (EntityFact)RuntimeHelpers.GetUninitializedObject(typeof(EntityFact));
                    blueprint.SetValue(fact, bp); active.SetValue(fact, on); return fact;
                }
                var stateType = typeof(CombatMath).GetNestedType("SwarmFacts", BindingFlags.NonPublic);
                var snapshot = Activator.CreateInstance(stateType, true);
                var update = (Func<List<EntityFact>, bool>)stateType.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .CreateDelegate(typeof(Func<List<EntityFact>, bool>), snapshot);
                var roundsField = stateType.GetField("Rounds", BindingFlags.Instance | BindingFlags.NonPublic);
                int Rounds() => ((IList)roundsField.GetValue(snapshot)).Count;
                var fact1 = Fact(carrier, false); var facts = new List<EntityFact> { fact1 };
                Check(update(facts) && Rounds() == 0, "inactive native carrier has no round source");
                active.SetValue(fact1, true);
                Check(update(facts) && Rounds() == 1, "activation at unchanged count discovers round source");
                Check(!update(facts) && Rounds() == 1, "unchanged active snapshot is reused");
                active.SetValue(fact1, false);
                Check(update(facts) && Rounds() == 0, "deactivation at unchanged count removes round source");
                facts[0] = Fact(carrier, true);
                Check(update(facts) && Rounds() == 1, "equal-count replacement is observed");
                facts[0] = Fact(ordinary, true);
                Check(update(facts) && Rounds() == 0, "replacement with ordinary buff drops stale source");
                var bodyField = stateType.GetField("Body", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (string name in new[] { "SwarmTinyFeature", "SwarmDiminutiveFeature" })
                {
                    var feature = all.OfType<BlueprintFact>().Single(b => b.name == name);
                    facts[0] = Fact(feature, true);
                    Check(update(facts) && (bool)bodyField.GetValue(snapshot), name + " supplies the actual active native swarm body");
                    active.SetValue(facts[0], false);
                    Check(update(facts) && !(bool)bodyField.GetValue(snapshot), name + " inactive body does not admit a cast hazard");
                }
                var bufferField = stateType.GetField("Facts", BindingFlags.Instance | BindingFlags.NonPublic);
                var buffer = bufferField.GetValue(snapshot);
                facts.Add(Fact(ordinary, true));
                Check(update(facts) && ReferenceEquals(buffer, bufferField.GetValue(snapshot)), "count growth reuses existing snapshot capacity");
                facts.RemoveAt(1);
                Check(update(facts) && ReferenceEquals(buffer, bufferField.GetValue(snapshot)) && ((Array)buffer).GetValue(1) == null,
                    "count shrink reuses capacity and clears removed fact references");
                for (int i = 0; i < 100; i++) update(facts);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++) update(facts);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(bytes == 0, "10000 unchanged native fact snapshots allocate zero managed bytes (observed " + bytes + ")");

                var cylinderType = typeof(Kingmaker.View.MapObjects.SriptZones.ScriptZoneCylinder);
                Check(cylinderType.GetField("Radius")?.FieldType == typeof(float) && cylinderType.GetField("Center")?.FieldType == typeof(UnityEngine.Vector3),
                    "actual native cylinder local radius/centre fields bind");
                Check(cylinderType.GetMethod("Contains", new[] { typeof(UnityEngine.Vector3), typeof(float) })?.ReturnType == typeof(bool),
                    "native Contains point/body signature binds; live Transform execution requires Unity");
                Check(Kingmaker.UnitLogic.Commands.Base.UnitCommand.IsUnitCloseEnough(new UnityEngine.Vector3(1, 0, 0),
                    UnityEngine.Vector3.zero, UnityEngine.Vector3.zero, -2f, false, 0f), "native signed approach radius is squared, not rejected as negative");
                Check(!Kingmaker.UnitLogic.Commands.Base.UnitCommand.IsUnitCloseEnough(new UnityEngine.Vector3(3, 0, 0),
                    UnityEngine.Vector3.zero, UnityEngine.Vector3.zero, -2f, false, 0f), "native approach gate rejects a point beyond that squared radius");

                var epoch = typeof(CombatMath).GetField("s_SwarmDecision", BindingFlags.NonPublic | BindingFlags.Static);
                var frame = typeof(CombatMath).GetField("s_SwarmFrame", BindingFlags.NonPublic | BindingFlags.Static);
                int savedEpoch = (int)epoch.GetValue(null), savedFrame = (int)frame.GetValue(null);
                try
                {
                    frame.SetValue(null, 23);
                    typeof(CombatMath).GetMethod("NewSwarmDecision", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                    Check((int)frame.GetValue(null) == -1 && (int)epoch.GetValue(null) == unchecked(savedEpoch + 1),
                        "next decision invalidates even within the same native frame");
                }
                finally { epoch.SetValue(null, savedEpoch); frame.SetValue(null, savedFrame); }
            }
            catch (Exception ex) { bad++; lines.Add("SWARM-COVERAGE FAILED: " + ex.GetBaseException()); }
            lines.Insert(0, $"SWARM-COVERAGE {checks} checks, failures {bad}; actual cast graphs and native fact layouts; no actor constructors, native casts or Unity performance execution");
            failures = bad;
            return lines;
        }
    }
}
