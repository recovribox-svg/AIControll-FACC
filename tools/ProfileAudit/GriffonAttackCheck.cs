// Actual metadata and compiled native/production routes. No actor, landing-node query, flight, buff or attack is run.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.TurnBasedModifiers;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class GriffonAttackCheck
    {
        private sealed class UnknownGriffon : AbilityGriffinAttack { }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("GRIFFON-ATTACK FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var allBlueprints = loaded.ToArray();
                var ability = allBlueprints.OfType<BlueprintAbility>().FirstOrDefault(b => b.AssetGuid.ToString() == "4af0f63ebf3f4eb08dade5a8709ff5a5");
                var root = allBlueprints.OfType<BlueprintBuff>().FirstOrDefault(b => b.AssetGuid.ToString() == "f36da144a379d534cad8e21667079066");
                Check(ability != null, "actual GriffonDeathFromAboveAbility loaded");
                Check(root != null, "actual native root ChargeBuff fixture loaded; required, not a skipped check");
                if (ability == null || root == null) throw new InvalidOperationException("Missing required griffon or stock charge fixture scope");
                var reader = typeof(AbilityProfile).GetMethod("ReadGriffonAttack", all);
                object Read() => reader.Invoke(null, new object[] { ability });
                Check(Read() != null, "actual seven-component griffon graph is admitted");
                var profile = AbilityProfile.Get(ability);
                Check(profile.IsWeaponAttack && profile.HasDetectableEffect && !profile.IsCharge,
                    "native leap is a physical weapon effect without becoming an ordinary Charge");
                var leap = ability.GetComponent<AbilityGriffinAttack>();
                var mode = ability.GetComponent<AbilityIsFullRoundInTurnBased>();
                var savedComponents = ability.ComponentsArray;
                void RejectField(object target, string name, object changed)
                {
                    var field = HarmonyLib.AccessTools.Field(target.GetType(), name); var before = field.GetValue(target);
                    try { field.SetValue(target, changed); Check(Read() == null, "withholds changed " + target.GetType().Name + "." + name); }
                    finally { field.SetValue(target, before); }
                }
                RejectField(leap, "IgnoreMountedRestriction", true);
                RejectField(leap, "TakeoffTime", 2f); RejectField(leap, "LandingTime", 2f);
                RejectField(leap, "DisappearStartTime", 0f); RejectField(leap, "AppearTime", 0f);
                RejectField(mode, "FullRoundIfTurnBased", false);
                try
                {
                    ability.ComponentsArray = savedComponents.Where(c => c != leap).ToArray();
                    Check(Read() == null, "missing custom delivery does not invent an attack");
                    ability.ComponentsArray = savedComponents.Select(c => c == leap ?
                        (BlueprintComponent)RuntimeHelpers.GetUninitializedObject(typeof(UnknownGriffon)) : c).ToArray();
                    Check(Read() == null, "custom delivery subclass is not treated as the inspected native method");
                    ability.ComponentsArray = savedComponents.Concat(new BlueprintComponent[] { new AbilityEffectRunAction() }).ToArray();
                    Check(Read() == null, "an extra payload is outside the native-only contract");
                }
                finally { ability.ComponentsArray = savedComponents; }
                Check(Read() != null, "actual griffon fixture restored");

                var chargeReader = typeof(AbilityProfile).GetMethod("ReadGriffonCharge", all);
                object Charge() => chargeReader.Invoke(null, new object[] { root });
                var charge = Charge(); Check(charge != null, "actual root two-fact conditional and paired child lifetimes are understood");
                if (charge == null) throw new InvalidOperationException("Unsupported required ChargeBuff graph");
                var select = charge.GetType().GetMethod("Select", all);
                object Branch(bool has) => select.Invoke(charge, new object[] { (Func<Kingmaker.Blueprints.Facts.BlueprintUnitFact, bool>)(f => has) });
                int Number(object item, string name) => (int)item.GetType().GetField(name, all).GetValue(item);
                Check(Number(Branch(false), "Attack") == 2 && Number(Branch(false), "Armor") == -2,
                    "ordinary native charge child contributes actual +2 roll and -2 AC");
                Check(Number(Branch(true), "Attack") == 4 && Number(Branch(true), "Armor") == 0,
                    "both actual fact guards select the native cavalier child rather than a fixed guessed bonus");
                var flags = new List<bool>();
                var onlyFirst = select.Invoke(charge, new object[] { (Func<Kingmaker.Blueprints.Facts.BlueprintUnitFact, bool>)(f => { flags.Add(true); return flags.Count == 1; }) });
                Check(Number(onlyFirst, "Attack") == 2, "one true fact alone retains the native AND false branch");
                var carrier = root.GetComponent<AddBuffActions>(); var choice = (Conditional)carrier.Activated.Actions.Single();
                var oldActions = carrier.NewRound.Actions;
                try
                {
                    carrier.NewRound.Actions = new Kingmaker.ElementsSystem.GameAction[] { new ContextActionReleaseGrapple() };
                    Check(Charge() == null, "extra charge-carrier round action is withheld");
                }
                finally { carrier.NewRound.Actions = oldActions; }
                var noApply = (ContextActionApplyBuff)choice.IfFalse.Actions.Single();
                bool oldPermanent = noApply.Permanent;
                try { noApply.Permanent = true; Check(Charge() == null, "changed child duration is withheld"); }
                finally { noApply.Permanent = oldPermanent; }
                var child = noApply.Buff;
                var conditionalBonus = child.GetComponent<AttackOfOpportunityAttackBonus>();
                bool oldNotAoo = conditionalBonus.NotAttackOfOpportunity;
                try { conditionalBonus.NotAttackOfOpportunity = false; Check(Charge() == null, "AoO-only bonus cannot improve the leap's ordinary attack"); }
                finally { conditionalBonus.NotAttackOfOpportunity = oldNotAoo; }
                Check(Charge() != null, "actual charge fixture restored");
                var unused = allBlueprints.OfType<BlueprintBuff>().FirstOrDefault(b => b.AssetGuid.ToString() == "9a71a8b2b2c14d12b8cb96399ca7a9a9");
                var effectReader = typeof(AbilityProfile).GetMethod("ReadGriffonChargeEffect", all);
                Check(unused != null && effectReader.Invoke(null, new object[] { unused }) == null,
                    "separate unused griffon +damage buff is not mistaken for the actual native charge effect");

                var math = typeof(CombatMath); var engine = typeof(TacticalEngine);
                Check(math.GetField("GriffonEndpoint", all).GetValue(null) != null, "read-only delegate binds exact supplied native landing signature");
                var nativeEnd = typeof(AbilityGriffinAttack).GetMethod("GetEndPoint", all);
                var endpoint = SaveBoostCheck.ReferencedMembers(nativeEnd).ToArray();
                Check(endpoint.Any(m => m.Name == "GetNearestNode") && !endpoint.Any(m => m.Name == "Translocate" || m.Name == "AddBuff" || m.Name == "Trigger"),
                    "native landing helper is a nearest-node read, not a simulated flight");
                var nativeTarget = SaveBoostCheck.ReferencedMembers(typeof(AbilityGriffinAttack).GetMethod("CheckTargetRestriction", all)).ToArray();
                Check(nativeTarget.Any(m => m.Name == "GetMaxRangeMeters") && nativeTarget.Any(m => m.Name == "GetMinRangeMeters") &&
                    nativeTarget.Any(m => m.Name == "get_RiderPart") && nativeTarget.Any(m => m.Name == "get_SaddledPart"),
                    "actual native restriction owns min/max charge distance and mount refusal");
                var nativeRound = SaveBoostCheck.ReferencedMembers(typeof(BlueprintAbility).GetProperty("IsFullRoundAction").GetMethod).ToArray();
                Check(nativeRound.Any(m => m.Name == "FullRoundIfTurnBased") && nativeRound.Any(m => m.Name == "IsTurnBasedModeOn"),
                    "actual native blueprint derives TB full-round behavior by current mode");
                var slot = SaveBoostCheck.ReferencedMembers(engine.GetMethod("SlotOf", all)).ToArray();
                Check(slot.Any(m => m.Name == "get_RequireFullRoundAction") && slot.Any(m => m.Name == "get_RuntimeActionType"),
                    "production uses actual full-round/runtime action properties");
                var moveNext = typeof(AbilityGriffinAttack).GetNestedTypes(all).Single(t => t.Name.Contains("Deliver")).GetMethod("MoveNext", all);
                var delivery = SaveBoostCheck.ReferencedMembers(moveNext).ToArray();
                Check(delivery.Any(m => m.Name == "Translocate") && delivery.Any(m => m.Name == "set_IsCharge") &&
                    delivery.Any(m => m.Name == "IgnoreCooldown") && delivery.Any(m => m.Name == "AddToQueueFirst"),
                    "actual native delivery teleports then queues its own charge attack ignoring spent cooldown");
                var nativeAttacks = SaveBoostCheck.ReferencedMembers(typeof(Kingmaker.UnitLogic.Commands.UnitAttack).GetMethod("InitAttacks", all)).ToArray();
                var nativeFull = SaveBoostCheck.ReferencedMembers(typeof(Kingmaker.UnitLogic.Commands.UnitAttack).GetMethod("IsFullAttackRestricted", all)).ToArray();
                Check(nativeAttacks.Any(m => m.Name == "get_IsCharge") && nativeAttacks.Any(m => m.Name == "Pounce") &&
                    nativeAttacks.Any(m => m.Name == "IsFullAttackRestricted") && nativeFull.Any(m => m.Name == "get_IsIgnoreCooldown"),
                    "native charged Pounce chooses full attacks and its ignored cooldown bypasses the just-spent TB action restriction");
                var forecast = SaveBoostCheck.ReferencedMembers(math.GetMethod("EstimateGriffonAttack", all)).ToArray();
                Check(forecast.Any(m => m.Name == "EstimateWeaponAttack") && !forecast.Any(m => m.Name == "TriggerRule" || m.Name == "AddBuff" || m.Name == "Translocate"),
                    "forecast reuses ordinary native-derived weapons without an attack event or mutation");
                Check(forecast.OfType<MethodBase>().Any(m => m.Name == "EstimateWeaponAttack" && m.GetParameters().Length == 3),
                    "short-lived prospective charge roll bonus receives no guessed credit across a long attack timeline");
                var evaluate = SaveBoostCheck.ReferencedMembers(engine.GetMethod("EvaluateGriffonAttack", all)).ToArray();
                Check(evaluate.Any(m => m.Name == "TryGriffonLanding") && evaluate.Any(m => m.Name == "GriffonLandingClear") &&
                    evaluate.Any(m => m.Name == "GriffonLandingReaches"), "planner requires actual safe endpoint within queued weapon reach");
                Check(Array.FindIndex(evaluate, m => m.Name == "TryLanding") >= 0 &&
                    Array.FindIndex(evaluate, m => m.Name == "TryLanding") < Array.FindIndex(evaluate, m => m.Name == "TryGriffonLanding") &&
                    Array.FindIndex(evaluate, m => m.Name == "TryWeaponQuote") >= 0 &&
                    Array.FindIndex(evaluate, m => m.Name == "TryWeaponQuote") < Array.FindIndex(evaluate, m => m.Name == "EstimateGriffonAttack"),
                    "pure landing/quote bounds are applied before native node queries and weapon quotes");
                Check(!evaluate.Any(m => m.Name == "EvaluateCharge" || m.Name == "MoveRisk" || m.Name == "RealTimeWalk" || m.Name == "PlanApproach"),
                    "native flight gets no ordinary path, travel AoO or walking-interception model");
                var issue = SaveBoostCheck.ReferencedMembers(engine.GetMethod("ValidateGriffonIssue", all)).ToArray();
                Check(issue.Any(m => m.Name == "CanTarget") && issue.Any(m => m.Name == "get_IsAvailable") &&
                    issue.Any(m => m.Name == "TryGriffonLanding") && issue.Any(m => m.Name == "GriffonLandingClear"),
                    "final issue rechecks native availability, target and current safe landing");
                var clear = SaveBoostCheck.ReferencedMembers(engine.GetMethod("GriffonLandingClear", all)).ToArray();
                Check(clear.Count(m => m.Name == "Takes" && m.DeclaringType == typeof(SpotRules)) >= 2,
                    "actual target overlap and other body claims both gate the snapped native landing");
                var ordinary = SaveBoostCheck.ReferencedMembers(engine.GetMethods(all).Single(m => m.Name == "Decide" && m.GetParameters().Length == 5)).ToArray();
                Check(ordinary.Any(m => m.Name == "AddWeaponCandidates") && ordinary.Any(m => m.Name == "AddMovementCandidates"),
                    "ordinary attack and movement candidates remain in the shared decision competition");
                var execute = SaveBoostCheck.ReferencedMembers(engine.GetMethod("Execute", all)).ToArray();
                Check(execute.Any(m => m.Name == "ValidateGriffonIssue") && execute.Any(m => m.Name == "get_ShouldUnitApproach"),
                    "dispatch validates leap and rejects a native unpriced approach before Run");
                var verify = SaveBoostCheck.ReferencedMembers(engine.GetMethod("IsVerifiable", all)).ToArray();
                Check(verify.Any(m => m.Name == "get_GriffonAttack"), "queued post-cast attack is excluded from early no-effect verification");
                var finite = math.GetMethod("GriffonFinite", all);
                Check((bool)finite.Invoke(null, new object[] { new UnityEngine.Vector3(1f, 2f, 3f) }) &&
                    !(bool)finite.Invoke(null, new object[] { new UnityEngine.Vector3(float.NaN, 0f, 0f) }) &&
                    !(bool)finite.Invoke(null, new object[] { new UnityEngine.Vector3(0f, float.PositiveInfinity, 0f) }),
                    "nonfinite native landing coordinates are withheld");
            }
            catch (Exception ex) { bad++; lines.Add("GRIFFON-ATTACK FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"GRIFFON-ATTACK {checks} checks, failures {bad}; actual leap/charge graphs and native/compiled routes; no actor, landing query, cast, attack or Unity timing execution");
            return lines;
        }
    }
}
