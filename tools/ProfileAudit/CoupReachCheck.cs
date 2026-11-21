// Native blueprint, provider metadata and compiled production routes. No live actor, cast or path execution.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class CoupReachCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, string provider, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("COUP-REACH FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var mod = typeof(TacticalEngine).Assembly;
                var delivery = mod.GetType("CompanionAI_Pathfinder.Tactics.CoupDeGraceDelivery", true);
                var read = delivery.GetMethod("ReadRoute", all);
                string Route(BlueprintAbility bp) => read.Invoke(null, new object[] { bp }).ToString();
                var actual = loaded.OfType<BlueprintAbility>().SingleOrDefault(b => b.AssetGuid.ToString() == "32280b137ca642c45be17e2d92898758");
                Check(actual != null, "actual Coup de Grace graph is loaded (supply stock missing-only fixture if absent)");
                Check(actual != null && actual.Range == AbilityRange.Weapon && actual.IsFullRoundAction &&
                      actual.ActionType == Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Standard,
                    "stock ability is Weapon range and Standard/full-round");
                Check(actual != null && Route(actual) == "Native", "actual stock action list is the recognized native melee delivery");
                var predicate = (Func<AbilityData, bool>)Delegate.CreateDelegate(typeof(Func<AbilityData, bool>), typeof(TacticalEngine).GetMethod("IsCoupDeGrace", all));
                AbilityData Data(BlueprintAbility bp)
                {
                    var data = (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                    typeof(AbilityData).GetField("Blueprint", all).SetValue(data, bp); return data;
                }
                var actualData = Data(actual); var otherData = Data(new BlueprintAbility()); var emptyData = Data(null);
                Check(predicate(actualData) && !predicate(otherData) && !predicate(emptyData) && !predicate(null),
                    "actual Coup identity is recognized; unrelated/null abilities are unaffected");
                for (int i = 0; i < 100; i++) { predicate(actualData); predicate(otherData); }
                // The best of three timed loops: a method the JIT tiers up mid-loop can allocate once on this thread, while a real
                // per-call allocation shows in every loop (10,000 calls x at least 24 bytes).
                long allocated = long.MaxValue; int hits = 0;
                for (int attempt = 0; attempt < 3 && allocated != 0; attempt++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread(); hits = 0;
                    for (int i = 0; i < 10000; i++) if (predicate((i & 1) == 0 ? actualData : otherData)) hits++;
                    allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
                }
                Check(hits == 5000 && allocated == 0, "hot Coup identity guard allocates no GUID strings");
                lines.Add($"COUP-REACH identity guard: 10000 warm calls, {allocated} bytes; inert ability wrappers, no actor");

                var attack = new ContextActionMeleeAttack { AutoHit = true, AutoCritThreat = true, AutoCritConfirmation = true, ExtraAttack = true };
                var run = new AbilityEffectRunAction { Actions = new ActionList { Actions = new GameAction[] { attack } } };
                var fixture = new BlueprintAbility { Range = AbilityRange.Weapon, ComponentsArray = new BlueprintComponent[] { run } };
                Check(Route(fixture) == "Native", "verified native first melee attack is admitted");
                attack.SelectNewTarget = true; Check(Route(fixture) == "Unsupported", "retargeting attack is refused"); attack.SelectNewTarget = false;
                attack.FullAttack = true; Check(Route(fixture) == "Unsupported", "full attack replacement is refused"); attack.FullAttack = false;
                attack.AutoHit = false; Check(Route(fixture) == "Unsupported", "different attack flags are refused"); attack.AutoHit = true;
                fixture.ComponentsArray = new BlueprintComponent[] { run, run };
                Check(Route(fixture) == "Unsupported", "ambiguous multiple effect providers are refused");
                fixture.ComponentsArray = Array.Empty<BlueprintComponent>();
                Check(Route(fixture) == "Unsupported" && Route(null) == "Unsupported", "missing delivery is refused");
                fixture.ComponentsArray = new BlueprintComponent[] { run }; fixture.Range = AbilityRange.Close;
                Check(Route(fixture) == "Unsupported", "different range contract is refused"); fixture.Range = AbilityRange.Weapon;

                if (provider != null)
                {
                    var assembly = Assembly.LoadFrom(provider);
                    var type = assembly.GetType("TabletopTweaks.Core.NewComponents.OwlcatReplacements.CoupDeGraceTTT", true);
                    fixture.ComponentsArray = new[] { (BlueprintComponent)RuntimeHelpers.GetUninitializedObject(type) };
                    Check(Route(fixture) == "TabletopTweaks", "real TTT replacement binds without a mod compile dependency");
                    fixture.ComponentsArray = new BlueprintComponent[] { fixture.ComponentsArray[0], run };
                    Check(Route(fixture) == "Unsupported", "mixed native/custom delivery is refused");
                    var iterator = type.GetNestedTypes(all).Single(t => t.Name.StartsWith("<Deliver>", StringComparison.Ordinal));
                    var body = SaveBoostCheck.ReferencedMembers(iterator.GetMethod("MoveNext", all)).ToArray();
                    Check(body.Any(m => m.DeclaringType == typeof(UnitHelper) && m.Name == "GetThreatHand") &&
                          !body.Any(m => m.Name == "GetThreatHandMelee"), "real TTT iterator selects GetThreatHand (including Snap Shot), not an invented melee substitute");
                }
                else lines.Add("COUP-REACH provider: stock route checked; use --optional-provider to verify the installed TTT replacement");

                bool Calls(MethodInfo method, string name) => SaveBoostCheck.ReferencedMembers(method).Any(m => m.Name == name);
                var weapon = delivery.GetMethod("TryWeapon", all);
                Check(Calls(weapon, "ReadRoute") && Calls(weapon, "GetThreatHandMelee") && Calls(weapon, "GetThreatHand") && Calls(weapon, "get_IsMelee"),
                    "actual hand selection follows delivery and rejects a selected ranged/no-melee weapon");
                var radius = delivery.GetMethod("TryRadius", all);
                Check(Calls(radius, "GetApproachDistance") && Calls(radius, "GetApproachRadius") && Calls(radius, "TryRadius"),
                    "actual range is bounded by both native ability and delivering weapon radii");
                var nativeRadius = typeof(UnitAttack).GetMethods(all).Single(m => m.Name == "GetApproachRadius" && m.GetParameters().Length == 3);
                var radiusRefs = SaveBoostCheck.ReferencedMembers(nativeRadius).ToArray();
                Check(radiusRefs.Any(m => m.Name == "get_AttackRange") && radiusRefs.Count(m => m.Name == "get_Corpulence") == 2,
                    "native weapon radius retains the actual weapon reach and both bodies");
                var melee = typeof(UnitHelper).GetMethod("GetThreatHandMelee", all);
                Check(Calls(melee, "get_PrimaryHand") && Calls(melee, "get_SecondaryHand") && Calls(melee, "get_AdditionalLimbs"),
                    "native melee selector covers primary, secondary and extra natural limbs");
                var threat = SaveBoostCheck.ReferencedMembers(typeof(UnitHelper).GetMethod("IsThreatHand", all)).ToArray();
                Check(threat.Any(m => m.Name == "SnapShot") && threat.Any(m => m.Name == "ImprovedUnarmedStrike") &&
                      Calls(typeof(UnitHelper).GetMethod("IsThreatHandMelee", all), "get_IsMelee"),
                    "native Snap Shot/unarmed eligibility is retained, with an additional selected-weapon melee restriction");
                var effect = typeof(ContextActionMeleeAttack).GetMethod("RunAction", all);
                Check(Calls(effect, "GetThreatHandMelee"), "stock actual effect uses the inspected melee selector");
                var nativeAbilityRange = typeof(AbilityData).GetMethod("GetApproachDistance", all);
                Check(Calls(nativeAbilityRange, "GetFirstWeapon"), "native Weapon ability range can differ from delivering hand");
                Check(Calls(typeof(UnitUseAbility).GetMethod("Init", all), "GetApproachDistance"),
                    "native command Init takes that ability approach distance");

                var prepare = delivery.GetMethod("TryPrepare", all);
                Check(Calls(prepare, "IsCoupDeGrace") && Calls(prepare, "TryWeapon") && Calls(prepare, "CanTarget") &&
                      Calls(prepare, "TryRadius") && Calls(prepare, "set_ApproachRadius"), "dispatch rechecks current hand/target and caps initialized approach radius");
                var dispatch = typeof(CommandDispatcher).GetMethod("TryPrepare", all);
                var refs = SaveBoostCheck.ReferencedMembers(dispatch).ToArray();
                int cap = Array.FindIndex(refs, m => m.DeclaringType == delivery && m.Name == "TryPrepare");
                Check(Array.FindIndex(refs, m => m.Name == "Init") < cap && cap < Array.FindIndex(refs, m => m.Name == "NeedsPrecomputedPath"),
                    "shared preview/issue cap is after Init and before any path preparation");
                var runCommand = typeof(CommandDispatcher).GetMethods(all).Single(m => m.Name == "Run" && m.GetParameters().Length > 3 &&
                    m.GetParameters()[1].ParameterType == typeof(Kingmaker.UnitLogic.Commands.Base.UnitCommand));
                Check(Calls(typeof(CommandDispatcher).GetMethod("CanPrepare", all), "TryPrepare") && Calls(runCommand, "TryPrepare") &&
                      Calls(runCommand, "NoteIssued"), "preview and final issue share preparation, with diagnostics only after command acceptance");
                Check(Calls(typeof(TacticalEngine).GetMethod("EvaluateAbility", all), "TryWeapon") &&
                      Calls(typeof(TacticalEngine).GetMethod("EvaluateAbility", all), "TryRadius"), "both drivers' shared appraisal uses the actual delivery contract");
                var score = SaveBoostCheck.ReferencedMembers(typeof(TacticalEngine).GetMethod("CoupDeGraceValue", all)).ToArray();
                Check(score.Any(m => m.Name == "TryWeapon") && score.OfType<MethodInfo>().Any(m => m.Name == "EstimateCoupDeGrace" &&
                      m.GetParameters().Length == 5), "damage appraisal passes the actual selected weapon");
                Check(Calls(typeof(TacticalEngine).GetMethod("CoupDeGraceAllies", all), "TryWeapon"), "sleep preservation requires an ally with supported melee delivery");
                var reaches = typeof(TacticalEngine).GetNestedType("CoupDeGraceAlly", all).GetMethod("Reaches", all);
                Check(Calls(reaches, "TryRadius"), "sleep preservation uses the same actual weapon reach, without a fixed reach bonus");
                Check(!Calls(radius, "FindPath") && !Calls(weapon, "Trigger") && !Calls(weapon, "CreateContext") &&
                      !Calls(prepare, "FindPath"), "range contract adds no path/rule/context forecast");
                Check((bool)prepare.Invoke(null, new object[] { null, null, null }), "non-Coup preparation is unchanged");
            }
            catch (Exception ex) { bad++; lines.Add("COUP-REACH FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"COUP-REACH {checks} checks, failures {bad}; blueprint/provider and compiled routes; live actor/path/gameplay NOT TESTED");
            return lines;
        }
    }
}
