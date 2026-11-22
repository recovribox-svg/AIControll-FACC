// Coverage8 review: native contracts and counterexamples. No live actor, cast, damage, removal or pathfinding.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Designers.EventConditionActionSystem.Actions;

namespace ProfileAudit
{
    internal static class Coverage8ReviewCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static List<MemberInfo> Body(Type t, string name) => BuffSuppressCheck.BodyWithLambdas(t, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type t, string name) => body.Any(m => m.DeclaringType == t && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("COVERAGE8-REVIEW FAILED: " + why); } }
            try
            {
                var engine = typeof(TacticalEngine);
                var all = loaded.ToList();
                var flesh = all.OfType<BlueprintBuff>().Single(b => b.AssetGuid.ToString() == "3a4c5a3c73b6142428c5815373ed98f4");
                var abandon = all.OfType<BlueprintAbility>().Single(b => b.AssetGuid.ToString() == "27695731aa092bc488c6092bfc68e31d");
                var plainHeal = flesh.GetComponents<AddFactContextActions>().SelectMany(c => c.Deactivated.Actions).OfType<ContextActionHealTarget>().Single();
                var heal = engine.GetMethod("DeactivationHeal", Any);
                float ReadHeal(BlueprintBuff b) => (float)heal.Invoke(null, new object[] { b });
                BlueprintBuff Callback(params GameAction[] actions) => new BlueprintBuff
                {
                    ComponentsArray = new BlueprintComponent[] { new AddFactContextActions { Deactivated = new ActionList { Actions = actions } } }
                };
                Check(ReadHeal(flesh) == 10f, "actual Flesh Armor's plain 2d6+3 heal stays supported");
                Check(ReadHeal(Callback(plainHeal, new ContextActionKill())) == 0f,
                    "a heal followed by an unmodelled harmful callback cannot earn positive healing credit");
                var withDispose = Callback(plainHeal);
                withDispose.GetComponent<AddFactContextActions>().Dispose = new ActionList { Actions = new GameAction[] { new ContextActionRemoveSelf() } };
                Check(ReadHeal(withDispose) == 0f, "an unread Dispose callback is not silently dropped from positive removal benefit");
                var removalProof = typeof(AbilityProfile).GetMethod("CertainlyRemovesCasterBuff", Any);
                Check((bool)removalProof.Invoke(AbilityProfile.Get(abandon), new object[] { flesh, null, null }),
                    "actual unconditional Abandon Flesh proves its caster removal without guessing another recipient");
                var remove = abandon.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>().Actions.Actions.OfType<ContextActionRemoveBuff>().Single();
                var conditional = new Conditional
                {
                    ConditionsChecker = new ConditionsChecker { Conditions = new Condition[] { new Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionHasFact() } }, IfTrue = new ActionList { Actions = new GameAction[] { remove } },
                    IfFalse = new ActionList { Actions = new GameAction[0] }
                };
                var unknown = new BlueprintAbility { ComponentsArray = new BlueprintComponent[]
                { new Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction { Actions = new ActionList { Actions = new GameAction[] { conditional } } } } };
                Check(AbilityProfile.Get(unknown).CasterRemovedBuffs.Contains(flesh) &&
                    !(bool)removalProof.Invoke(AbilityProfile.Get(unknown), new object[] { flesh, null, null }),
                    "a flat caster-removal name does not prove an unread guarded callback will happen");
                var casterHeal = Body(engine, "CasterRemovalHeal");
                Check(Calls(casterHeal, typeof(AbilityProfile), "CertainlyRemovesCasterBuff") &&
                    casterHeal.Any(m => m.Name == "get_IsSuppressed") && casterHeal.Any(m => m.Name == "get_IsTurnedOn") &&
                    casterHeal.Any(m => m.Name == "get_Active"), "a removal heal needs the actual active, unsuppressed fact and proved occurrence");
                Check(Body(typeof(AddFactContextActions), "OnDeactivate").Any(m => m.Name == "RunActionInContext"),
                    "native Deactivated actions execute through the removed fact's context");

                var nativeArea = Body(typeof(ContextActionDispelMagic), "TryDispelAreaEffect");
                var nativeBuff = Body(typeof(ContextActionDispelMagic), "TryDispelBuff");
                Check(nativeArea.Any(m => m.Name == "CheckBonus") && !nativeArea.Any(m => m.Name == "ContextBonus") &&
                    nativeBuff.Any(m => m.Name == "ContextBonus"), "native area checks do not consume the buff branch's ContextBonus");
                var point = Body(engine, "PointDispelValue");
                Check(!point.Any(m => m.Name == "ContextBonus") && Calls(point, typeof(AoeDangerAnalyzer), "DispelCoversPoint"),
                    "the point quote uses the correct bonus branch and exact native point membership");
                var coverage = Body(typeof(AoeDangerAnalyzer), "DispelCoversPoint");
                Check(coverage.Any(m => m.Name == "Contains") && !coverage.Any(m => m.Name == "Covers"),
                    "point membership does not use a movement-radius fallback");
                Check(Body(engine, "AreaRemovalWorth").Any(m => m.Name == "AreaRemovalValues") &&
                    Calls(Body(engine, "AreaRemovalWorth"), engine, "AreaRemovalWorthCore"),
                    "area worth is reused only inside the current tactical context");
                Check(Body(engine, "AreaRemovalWorth").Any(m => m.Name == "get_ForecastVersion") &&
                    Body(engine, "AreaRemovalWorth").Any(m => m.Name == "Clear"),
                    "state previews invalidate area-worth reuse together with the attack and resistance forecasts");
                Check(Body(engine, "AreaRemovalWorthCore").Any(m => m.Name == "get_IsConscious") &&
                    !Body(engine, "AreaRemovalWorthCore").Any(m => m.Name == "get_HPLeft"),
                    "an ally conscious below zero HP is still a recipient, using the native life state");

                var suit = typeof(AbstractWeaponTrigger).GetMethods(Any).Single(m => m.Name == "IsSuitable" &&
                    m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(RuleAttackWithWeapon));
                var nativeTrigger = SaveBoostCheck.ReferencedMembers(suit).ToList();
                Check(nativeTrigger.Any(m => m.Name == "HadHit") && nativeTrigger.Any(m => m.Name == "get_IsFirstAttack") &&
                    nativeTrigger.Any(m => m.Name == "get_IsHit"), "native first-hit eligibility depends on persistent HadHit and the opening hit");
                Check(OnHitRules.FiringHits(new[] { true, true }, new[] { 0f, 1f }, false, true, 1f) == 0f &&
                    OnHitRules.FiringHits(new[] { false, true }, new[] { 1f, 1f }, false, true, 1f) == 0f,
                    "a missed or unsuitable opening hit cannot certify a reset of unknown native HadHit");
                Check(Body(typeof(CombatMath), "ClearAttackCache").Any(m => m.Name == "ClearOffensiveRiderReads"),
                    "same-frame decisions and previews expire target-dependent rider reads");
                Check(Body(typeof(CombatMath), "EstimateWeaponAttackCore").Any(m => m.Name == "Reach") &&
                    Body(typeof(CombatMath), "GriffonModeQuote").Any(m => m.Name == "Reach"),
                    "ordinary and leap rider hit chances retain each attack line's grapple reach");

                var through = Body(engine, "AddThroughAreaAdvance");
                Check(through.Any(m => m.Name == "PathConstraint") && Calls(through, engine, "ThroughAreaPathFailure"),
                    "a recorded segment never authorizes an unchecked through-area route");
                Check(through.Any(m => m.Name == "get_RiderPart"),
                    "a mounted rider cannot use a permission whose native route the dispatcher does not inspect");
                var constraint = Body(engine, "ThroughAreaPathFailure");
                Check(Calls(constraint, typeof(CommandDispatcher), "WalkToArrival") &&
                    Calls(constraint, typeof(AoeDangerAnalyzer), "TryQuotePathAreas") &&
                    Calls(constraint, typeof(AreaQuoteRules), "CoversActualRisk"),
                    "the actual native route, trimmed to arrival, must remain within the priced risk");
                Check(!Calls(constraint, typeof(CommandDispatcher), "RealTimeWalk"), "the constraint reuses its supplied native path without a second path query");
                var pathAreas = Body(typeof(AoeDangerAnalyzer), "TryQuotePathAreas");
                Check(pathAreas.Any(m => m.Name == "PathWithinBudget") && pathAreas.Any(m => m.Name == "PendingOnWay") &&
                    pathAreas.Any(m => m.Name == "Covers") && pathAreas.Any(m => m.Name == "EvaluateEffectDanger"),
                    "bounded live-area sampling retains pending-delivery refusal and the shared native danger readers");
                Check(!AreaQuoteRules.CoversActualRisk(10f, .2f, 10f, .3f) &&
                    !AreaQuoteRules.CoversActualRisk(10f, .2f, 20f, .2f) && !AreaQuoteRules.PathWithinBudget(65, 1f, 3f),
                    "stronger stopping, longer exposure and over-budget routes cannot inherit the old entry's permission");
                Check(Body(typeof(CombatMemory), "ResetCombat").Any(m => m.Name == "LastAreaCrossing") &&
                    Body(typeof(CommandDispatcher), "RealTimeWalkRefused").Any(m => m.Name == "LastAreaCrossing"),
                    "dispatch crossing evidence has an attempt and combat lifetime");
                Check(Calls(Body(typeof(CombatMemory), "ResetCombat"), engine, "ResetFactionObservations") &&
                    Body(engine, "ResetFactionObservations").Any(m => m.Name == "Clear"),
                    "a new fight cannot inherit a previous faction transition memo");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"COVERAGE8-REVIEW {checks} checks, {bad} failures; native contracts, actual callbacks and counterexamples; no live cast or path");
            return lines;
        }
    }
}
