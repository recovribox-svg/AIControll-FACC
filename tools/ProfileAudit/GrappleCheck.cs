// ★ G1 (docs/TACTICAL_AI.md §170): the native grapple contracts the hold forecast rests on, the mod's grab-trigger reader on
// the real blueprints, and the compiled routes of the change. No actor, Rulebook roll, command or Unity execution.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.ElementsSystem;
using Kingmaker.Controllers.Units;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Parts;

namespace ProfileAudit
{
    internal static class GrappleCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                            BindingFlags.DeclaredOnly;

        private static List<MemberInfo> Body(Type type, string name)
        {
            var method = type.GetMethods(Hidden).FirstOrDefault(m => m.Name == name) ?? throw new MissingMethodException(type.FullName, name);
            return SaveBoostCheck.ReferencedMembers(method).ToList();
        }

        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        private static bool GetsPart(IEnumerable<MemberInfo> body, Type part) =>
            body.OfType<MethodInfo>().Any(m => m.Name == "Get" && m.IsGenericMethod && m.GetGenericArguments()[0] == part);

        private static bool CallsMod(Type type, string method, Type calleeType, string callee) =>
            type.GetMethods(Hidden).Where(m => m.Name == method)
                .Any(m => SaveBoostCheck.ReferencedMembers(m).Any(r => r.DeclaringType == calleeType && r.Name == callee));

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("GRAPPLE FAILED: " + why); } }
            int accepted = 0, refusedCount = 0, other = 0;
            try
            {
                // --- native: what a started grapple does, and how it ends ---
                var next = Body(typeof(UnitAttack), "TryStartNextAttack");
                Check(Calls(next, typeof(UnitState), "HasCondition"),
                      "UnitAttack.TryStartNextAttack refuses the next attack of a Standard command under a condition (CantUseStandardActions)");
                var init = Body(typeof(UnitPartGrappleInitiator), "Init");
                Check(Calls(init, typeof(UnitState), "AddCondition") && init.Any(m => m.Name == "AddBuff"),
                      "UnitPartGrappleInitiator.Init adds the holder buff and its conditions (CantMove, CantUseStandardActions, no AoO)");
                Check(Calls(Body(typeof(UnitPartGrappleTarget), "TrySetPinned"), typeof(UnitState), "AddCondition"),
                      "a pin adds a condition to the victim (CantUseStandardActions)");
                var round = Body(typeof(ShifterGrabInitiatorBuff), "HandleNewRound");
                Check(round.Any(m => m is ConstructorInfo c && c.DeclaringType == typeof(RuleCombatManeuver)) &&
                      Calls(round, typeof(UnitPartGrappleTarget), "TrySetPinned") &&
                      round.Any(m => m is ConstructorInfo c && c.DeclaringType == typeof(UnitGrappleInitiatorAttack)) &&
                      round.Any(m => m.Name == "ReleaseGrapple"),
                      "each new round the holder rolls the grapple: success pins and queues the free attack, failure releases");
                var mode = typeof(UnitGrappleInitiatorAttack).GetProperty("Mode", Hidden)?.GetGetMethod(true)?.GetMethodBody()?.GetILAsByteArray();
                Check(mode != null && mode.Length == 2 && mode[0] == 0x16 && mode[1] == 0x2A,
                      "the free attack is damage only (Mode returns AttackMode.DamageOnly = 0)");
                Check(Body(typeof(UnitGrappleInitiatorAttack), "IsSuitableWeapon").Any(m => m.Name == "get_IsNatural"),
                      "... with a natural weapon");
                var tick = Body(typeof(UnitGrappleController), "TickForTarget");
                Check(tick.Any(m => m.Name == "TryBreakFree") && tick.Any(m => m.Name == "get_HasMoved") && tick.Any(m => m.Name == "ReleaseGrapple"),
                      "the victim tries to break free by itself each round; a victim that moves ends the hold");
                Check(Body(typeof(UnitGrappleController), "TickForInitiator").Any(m => m.Name == "get_HasMoved"),
                      "a holder that moves ends the hold");
                Check((int)UnitHelper.BreakFreeFlags.Default == ((int)UnitHelper.BreakFreeFlags.CanUseCMB | (int)UnitHelper.BreakFreeFlags.CMD2DC),
                      "the controller's break-free uses the CMB and the holder's CMD (BreakFreeFlags.Default)");
                var breakFree = Body(typeof(UnitHelper), "TryBreakFree");
                Check(breakFree.Any(m => m.Name == "SkillMobility") && breakFree.Any(m => m.Name == "SkillAthletics") &&
                      breakFree.Any(m => m is ConstructorInfo c && c.DeclaringType == typeof(RuleCalculateCMD)),
                      "TryBreakFree compares the victim's CMB with its Mobility and Athletics, against the holder's CMD");
                var pair = Body(typeof(ContextActionGrapple), "RunAction");
                Check(GetsPart(pair, typeof(UnitPartGrappleInitiator)) && GetsPart(pair, typeof(UnitPartGrappleTarget)),
                      "ContextActionGrapple refuses a holder that holds and a victim already held");
                Check(Body(typeof(ContextActionCombatManeuverCustom), "RunAction").Any(m => m is ConstructorInfo c && c.DeclaringType == typeof(RuleCombatManeuver)),
                      "the grab's maneuver is the game's RuleCombatManeuver");

                // --- the reader on the real blueprints ---
                var reasons = new Dictionary<string, int>();
                var byName = new Dictionary<string, List<GrabTrigger>>();
                var refusedNames = new Dictionary<string, HashSet<string>>();
                foreach (var blueprint in loaded.OfType<BlueprintScriptableObject>())
                {
                    if (blueprint.ComponentsArray == null) continue;
                    foreach (var trigger in blueprint.ComponentsArray.OfType<AddInitiatorAttackWithWeaponTrigger>())
                    {
                        var grab = CombatMath.ReadGrabTrigger(trigger, out string refusal);
                        if (grab != null)
                        {
                            accepted++;
                            if (!byName.TryGetValue(blueprint.name, out var list)) byName[blueprint.name] = list = new List<GrabTrigger>();
                            list.Add(grab);
                        }
                        else if (refusal != null)
                        {
                            refusedCount++;
                            reasons[refusal] = reasons.TryGetValue(refusal, out int n) ? n + 1 : 1;
                            if (!refusedNames.TryGetValue(blueprint.name, out var set)) refusedNames[blueprint.name] = set = new HashSet<string>();
                            set.Add(refusal);
                        }
                        else other++;
                    }
                }
                foreach (var reason in reasons.OrderByDescending(r => r.Value))
                    lines.Add($"GRAPPLE refused: {reason.Key} x{reason.Value} (" +
                              string.Join(", ", refusedNames.Where(r => r.Value.Contains(reason.Key)).Select(r => r.Key).OrderBy(n => n).Take(6)) + ")");
                lines.Add("GRAPPLE read: " + string.Join(", ", byName.OrderBy(b => b.Key).Select(b => $"{b.Key} x{b.Value.Count}")));
                foreach (var name in new[] { "GriffonGrappleBuff", "BearGrappleBuff", "LizardGrappleBuff", "TigerGrappleBuff", "SmilodonGrappleBuff" })
                    Check(byName.ContainsKey(name), name + " is read as a grab");
                Check(byName.TryGetValue("SmilodonGrappleBuff", out var smilodon) && smilodon.Count == 2 &&
                      smilodon.All(g => g.Gate != null && g.Gate.Operation == Operation.Or),
                      "the Smilodon's two grab triggers sit behind its Or gate");
                Check(byName.TryGetValue("GriffonGrappleBuff", out var griffon) && griffon.Count == 1 && griffon[0].MaintainBonus == 5 &&
                      griffon[0].HolderDexterity == -4 && griffon[0].Gate?.Conditions?.Length == 1 && griffon[0].WeaponType != null &&
                      !griffon[0].CheckCategory && !griffon[0].CheckRange,
                      "Griffon: claw type only, behind its caster-fact gate, +5 to keep the hold, -4 Dexterity while it holds");
                Check(byName.TryGetValue("BearGrappleBuff", out var bear) && bear.All(g => g.Gate == null),
                      "Bear: no gate");
                Check(byName.Values.SelectMany(l => l).All(g => g.HolderBuff?.GetComponent<ShifterGrabInitiatorBuff>() != null),
                      "every grab read has the native maintenance on its holder buff");
                Check(refusedNames.TryGetValue("GolemAutumnGrabFeature", out var golem) && golem.Contains("grapple through ContextActionCombatManeuver") &&
                      refusedNames.TryGetValue("MimicOozeGrabFeature", out var mimic) && mimic.Contains("grapple maneuver with other success or failure actions") &&
                      !byName.ContainsKey("GolemAutumnGrabFeature") && !byName.ContainsKey("MimicOozeGrabFeature"),
                      "the monster grabs with extra damage stay unread (old forecast): a plain maneuver (Autumn Golem), more success actions (Mimic Ooze)");
                if (griffon != null && griffon.Count == 1)
                {
                    var weapons = loaded.OfType<BlueprintItemWeapon>().ToList();
                    var claws = weapons.Where(w => w.Type == griffon[0].WeaponType).ToList();
                    var bites = weapons.Where(w => w.Type != null && w.Type != griffon[0].WeaponType && w.Type.name.Contains("Bite")).ToList();
                    Check(claws.Count > 0 && claws.All(w => griffon[0].Suits(w)), $"the Griffon grab suits its claw weapons ({claws.Count})");
                    Check(bites.Count > 0 && bites.All(w => !griffon[0].Suits(w)), $"... and no bite ({bites.Count}): the bite after the claws does not grab");
                }

                // --- compiled routes of the change ---
                Check(CallsMod(typeof(CombatMath), "EstimateWeaponAttackCore", typeof(CombatMath), "GrappleOpeningOn"),
                      "the weapon estimate reads the grapple opening");
                Check(CallsMod(typeof(TacticalEngine), "DamageValue", typeof(TacticalEngine), "GrappleHold"),
                      "the shared weapon value adds the hold (both modes)");
                Check(CallsMod(typeof(UnitStatus), "Describe", typeof(GrappleState), "Describe"),
                      "the decision header names the grapple pair");
                Check(CallsMod(typeof(CombatMath), "GrappleOpeningOn", typeof(CombatMath), "ManeuverOdds") &&
                      CallsMod(typeof(CombatMath), "GrappleOpeningOn", typeof(CombatMath), "ManeuverValues") &&
                      CallsMod(typeof(CombatMath), "ManeuverOdds", typeof(CombatMath), "ManeuverValues") &&
                      CallsMod(typeof(CombatMath), "ManeuverOdds", typeof(CombatMath), "ManeuverConcealmentShare"),
                      "the grapple odds use the existing maneuver reading (refactored, not copied)");
                Check(CallsMod(typeof(CombatMath), "ActiveGrab", typeof(UnitEntityData), "get_IsPlayerFaction"),
                      "only party units' grabs are valued (an enemy's routine keeps the old forecast)");

                // --- the scalar rules through the compiled mod ---
                Check(Math.Abs(ValueModel.GrappleHoldValue(0.8f, 3f, 30f, 30f, 10f, 0.4f, 1f) - 26.4f) < 1e-3f,
                      "an equal victim: first turn held (0.4) plus 1.44 pinned rounds of the free hit");
                Check(ValueModel.GrappleHoldValue(0.9f, 3f, 5f, 34f, 8f, 0.4f, 1f) < 0f, "a weak victim costs the holder more than it loses");
                var run = ValueModel.GrappleRun.Begin();
                run.Next(0.8f, true, 0.9f); run.Next(0.8f, true, 0.9f); run.Next(0.7f, false, 0.9f);
                Check(Math.Abs(run.Attacks - 1.3584f) < 1e-3f, "claw, claw, bite at 80% / 90%: 1.36 of 3 attacks expected");
            }
            catch (Exception ex)
            {
                bad++;
                lines.Add("GRAPPLE FAILED: " + ex.GetType().Name + ": " + ex.Message);
            }
            failures = bad;
            lines.Insert(0, $"GRAPPLE {checks} checks, failures {bad}; native IL contracts, grab triggers read {accepted}, refused {refusedCount}, " +
                            $"other triggers {other}; compiled routes and scalar rules; no actor, Rulebook roll, command or Unity execution");
            return lines;
        }
    }
}
