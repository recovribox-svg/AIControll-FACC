// ★ coverage7 follow-ups (docs/COVERAGE7_FOLLOWUPS.md): Abandon Flesh's removal heal, point dispels that end areas,
// priced advances through an area the real path crosses, approach-planning timers and the faction transition rows.
// Native contracts, the actual Vermlek and Dispel Magic graphs, and compiled routes. Actor-free: no Game/Player, Rulebook,
// area, buff instance, cast, path or random roll.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class Coverage7FollowupCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("COVERAGE7-FOLLOWUPS FAILED: " + why); } }
            try
            {
                var engine = typeof(TacticalEngine);
                var all = loaded.ToList();

                // --- 1. Abandon Flesh: the heal its removal gives ---
                var flesh = all.OfType<BlueprintBuff>().FirstOrDefault(b => b.AssetGuid.ToString() == "3a4c5a3c73b6142428c5815373ed98f4");
                var abandon = all.OfType<BlueprintAbility>().FirstOrDefault(b => b.AssetGuid.ToString() == "27695731aa092bc488c6092bfc68e31d");
                var heal = engine.GetMethod("DeactivationHeal", Hidden);
                Check(flesh != null && heal != null && Math.Abs((float)heal.Invoke(null, new object[] { flesh }) - 10f) < 0.001f,
                    "actual Flesh Armor: its removal heals 2d6+3 (10 on average)");
                var profile = abandon != null ? AbilityProfile.Get(abandon) : null;
                Check(profile != null && profile.CasterRemovedBuffs.Contains(flesh) && !profile.RemovesAnything && profile.DispelActions.Count == 0,
                    "actual Abandon Flesh: removes only its caster's Flesh Armor (no target removal or dispel)");
                Check(heal != null && (float)heal.Invoke(null, new object[] { new BlueprintBuff() }) == 0f,
                    "a buff with no deactivation heal gives none");
                var score = Body(engine, "ScoreAbility");
                Check(Calls(score, engine, "CasterRemovalHeal") && Calls(score, engine, "CasterRemovalCost"),
                    "a caster-buff removal is priced by its loss and by the heal its removal gives");
                Check(Calls(Body(engine, "CasterRemovalHeal"), engine, "HealGain") && Calls(score, engine, "HealGain"),
                    "that heal and ordinary heals share one heal-gain formula");

                // --- 2. point dispels end areas ---
                var runAction = Body(typeof(ContextActionDispelMagic), "RunAction");
                Check(runAction.Any(m => m.Name == "AreaEffects") && runAction.Any(m => m.Name == "TryDispelAreaEffect") &&
                      runAction.Any(m => m.Name == "Contains"),
                    "native: with no target unit a dispel tries the game's area effects whose view contains the point");
                var tryArea = SaveBoostCheck.ReferencedMembers(typeof(ContextActionDispelMagic).GetMethod("TryDispelAreaEffect", Hidden)).ToList();
                Check(tryArea.Any(m => m.Name == "get_SourceAbility") && tryArea.Any(m => m.Name == "OnlyEnemyAreaEffects") &&
                      tryArea.Any(m => m.Name == "CheckSchoolsAndDescriptor"),
                    "native: an area needs a source ability, the dispel's schools/descriptor, and an enemy's with OnlyEnemyAreaEffects");
                var point = all.OfType<BlueprintAbility>().FirstOrDefault(b => b.AssetGuid.ToString() == "9f6daa93291737c40b8a432c374226a7");
                var pointProfile = point != null ? AbilityProfile.Get(point) : null;
                var targetsUnits = engine.GetMethod("DispelTargetsUnits", Hidden);
                Check(pointProfile != null && pointProfile.Dispels && targetsUnits != null &&
                      !(bool)targetsUnits.Invoke(null, new object[] { pointProfile }),
                    "actual Dispel Magic (Point): a point-only dispel");
                var pointValue = Body(engine, "PointDispelValue");
                Check(Calls(pointValue, typeof(CompanionAI_Pathfinder.Analysis.AoeDangerAnalyzer), "CoversPoint") &&
                      Calls(pointValue, engine, "DispelSchoolsMatch") && Calls(pointValue, typeof(ValueModel), "ExpectedDispelWorth") &&
                      Calls(pointValue, engine, "AreaRemovalWorth") && Calls(pointValue, typeof(ValueModel), "DispelChance"),
                    "point dispel value: covered areas, native filters, ordered and counted chances, area worth");
                Check(Calls(Body(engine, "TryDispelValue"), engine, "PointDispelValue"),
                    "a point-only dispel is priced by the areas it ends, not by unit buffs");
                Check(Calls(Body(engine, "AreaRemovalWorth"), typeof(CompanionAI_Pathfinder.Analysis.AoeDangerAnalyzer), "AreaDangerOn"),
                    "an area's worth is its danger to the units inside");
                var dispelChance = typeof(ValueModel).GetMethod("DispelChance");
                Check(dispelChance != null && Math.Abs((float)dispelChance.Invoke(null, new object[] { 11 }) - 0.5f) < 0.001f,
                    "dispel check: needing 11 on the d20 succeeds half the time");

                // --- 3. a priced advance through an area the real path crosses (real time) ---
                var dispatcher = typeof(CompanionAI_Pathfinder.GameInterface.CommandDispatcher);
                Check(SaveBoostCheck.ReferencedMembers(dispatcher.GetMethod("RealTimeWalkRefused", Hidden)).Any(m => m.Name == "LastAreaCrossing"),
                    "a walk refused for an area records where the game's path enters it");
                Check(Calls(Body(typeof(RealTimeTactics), "Act"), typeof(CombatMemory), "NoteAreaCrossing"),
                    "the real-time driver remembers that crossing for the unit and target");
                var through = Body(engine, "AddThroughAreaAdvance");
                Check(Calls(through, typeof(CombatMemory), "AreaCrossing") && Calls(through, engine, "AreaStayCost") &&
                      Calls(through, typeof(CompanionAI_Pathfinder.Analysis.AoeDangerAnalyzer), "EnteringDanger"),
                    "the through-area advance is priced as passing through that area (as an attack walk into one)");
                Check(Calls(Body(engine, "AddAdvanceCandidate"), engine, "AddThroughAreaAdvance"),
                    "advances offer it once a crossing is known");
                Check(Body(typeof(CombatMemory), "ResetCombat").Any(m => m.Name == "Clear") &&
                      SaveBoostCheck.ReferencedMembers(typeof(CombatMemory).GetMethod("AreaCrossing", Hidden)).Any(m => m.Name == "get_IsRealTime"),
                    "real time only; forgotten with the fight");

                // --- 4. approach-planning timers and faction rows (measurement and diagnostics only) ---
                var plan = Body(engine, "PlanApproach");
                Check(Calls(plan, engine, "PlanApproachMiss") && plan.Any(m => m.Name == "GetTimestamp"),
                    "approach-planning misses are timed for the slow-decision row");
                var decide = engine.GetMethods(Hidden).Where(m => m.Name == "Decide" && m.GetParameters().Length == 2).ToList();
                Check(decide.Count == 1 && Calls(SaveBoostCheck.ReferencedMembers(decide[0]), engine, "ApproachNote") &&
                      Calls(SaveBoostCheck.ReferencedMembers(decide[0]), engine, "ObserveFaction"),
                    "the logged decision reports approach timings and its unit's faction state");
                Check(Calls(Body(engine, "ObserveFaction"), typeof(Kingmaker.EntitySystem.Entities.UnitEntityData), "get_IsDirectlyControllable"),
                    "faction rows name the native direct-control state");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"COVERAGE7-FOLLOWUPS {checks} checks, {bad} failures; actual Vermlek/Dispel Magic graphs, native contracts and routes, no actor or area execution");
            return lines;
        }
    }
}
