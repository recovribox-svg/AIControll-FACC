// ★ burst aim (docs/BURST_AIM_AND_TACTICS_PLAN.md §3): bursts that land after their cast goes off are aimed at the
// ground where their anchor will be. Native contracts on the real assembly, the A1 classification on the real
// exported blueprints (and a fusion built on Explosive Runes), and the compiled routes of A2-A5.
// Actor-free: no Game/Player, context, selection, command, cast or Unity timing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Controllers.Units;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;

namespace ProfileAudit
{
    internal static class BurstAimCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                            BindingFlags.DeclaredOnly;

        private static List<MemberInfo> Body(Type type, string name)
        {
            var method = type.GetMethods(Hidden).FirstOrDefault(m => m.Name == name) ?? throw new MissingMethodException(type.FullName, name);
            return SaveBoostCheck.ReferencedMembers(method).ToList();
        }

        private static List<MemberInfo> Getter(Type type, string property) =>
            SaveBoostCheck.ReferencedMembers(type.GetProperty(property, Hidden)?.GetGetMethod(true)
                ?? throw new MissingMethodException(type.FullName, property)).ToList();

        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        /// <summary>
        /// The production overflow rule on a full list: with only casts that have not gone off it makes no room; with one
        /// burst that has gone off among them, that one goes. The list is restored empty.
        /// </summary>
        private static bool MakeRoomKeepsLiveWatches()
        {
            var watch = typeof(BurstWatch);
            var entries = (System.Collections.IList)watch.GetField("Entries", Hidden).GetValue(null);
            var entryType = watch.GetNestedType("Entry", BindingFlags.NonPublic);
            var max = (int)watch.GetField("MaxEntries", Hidden).GetValue(null);
            var makeRoom = watch.GetMethod("MakeRoom", Hidden);
            var landsAt = entryType.GetField("LandsAt");
            object Entry(double at)
            {
                var e = Activator.CreateInstance(entryType, true);
                landsAt.SetValue(e, at);
                return e;
            }
            try
            {
                entries.Clear();
                for (int i = 0; i < max; i++) entries.Add(Entry(double.NaN));
                bool live = !(bool)makeRoom.Invoke(null, null) && entries.Count == max;
                entries[max / 2] = Entry(20d);
                entries[max - 1] = Entry(10d);
                bool room = (bool)makeRoom.Invoke(null, null) && entries.Count == max - 1 &&
                            entries.Cast<object>().Count(e => !double.IsNaN((double)landsAt.GetValue(e))) == 1 &&
                            entries.Cast<object>().Any(e => (double)landsAt.GetValue(e) == 20d);
                return live && room;
            }
            finally { BurstWatch.Clear(); }
        }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("BURST-AIM FAILED: " + why); } }
            try
            {
                // --- native contracts ---
                var select = Body(typeof(AbilityTargetsAround), "Select");
                Check(Calls(select, typeof(TargetWrapper), "get_IsUnit") && Calls(select, typeof(TargetWrapper), "get_Point") &&
                      Calls(select, typeof(UnitEntityData), "get_EyePosition") && select.Any(m => m.Name == "GetTargetsAround"),
                      "AbilityTargetsAround.Select picks around the anchor unit's eye position, or the fixed point");
                Check(typeof(AbilityDeliverDelay).GetField("DelaySeconds")?.FieldType == typeof(float),
                      "AbilityDeliverDelay carries DelaySeconds (it delivers on the main target when they have passed)");
                var canPoint = Getter(typeof(AbilityData), "CanTargetPoint");
                Check(Calls(canPoint, typeof(MagicHackData), "get_DeliverBlueprint") || canPoint.Any(m => m.Name == "DeliverBlueprint"),
                      "AbilityData.CanTargetPoint follows a fusion's deliver spell");
                Check(typeof(UnitUseAbility).GetField("m_CastTime", Hidden)?.FieldType == typeof(float),
                      "UnitUseAbility keeps its native cast time (m_CastTime), read by the re-check");
                Check(Calls(Body(typeof(UnitUseAbility), "OnAction"), typeof(AbilityData), "Spend"),
                      "the spell is spent when the cast goes off (UnitUseAbility.OnAction)");
                Check(Body(typeof(UnitCommandController), "TickCommand").Any(m => m.Name == "SpendAction"),
                      "the action is spent when the command goes off (UnitCommandController.TickCommand)");
                Check(typeof(IUnitCommandActHandler).IsAssignableFrom(typeof(BurstWatch)),
                      "BurstWatch hears the native command-act event (IUnitCommandActHandler)");
                Check(typeof(BlueprintProjectile).GetField("Speed")?.FieldType == typeof(float) &&
                      typeof(BlueprintProjectile).GetField("MinTime")?.FieldType == typeof(float),
                      "BlueprintProjectile carries Speed and MinTime");

                // --- A1 classification on the real exports ---
                var all = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => all.FirstOrDefault(b => b.name == name);
                var groundAim = typeof(TacticalEngine).GetMethod("GroundAimBurst", Hidden) ??
                                throw new MissingMethodException(typeof(TacticalEngine).FullName, "GroundAimBurst");
                bool Ground(AbilityData a) => (bool)groundAim.Invoke(null, new object[] { a, AbilityProfile.Get(a) });
                AbilityData Data(BlueprintAbility b) => CasterPolicyCheck.Data(b);

                var poolRecipients = typeof(TacticalEngine).GetMethod("UseCurrentPoolRecipients", Hidden);
                var currentPool = (Func<AbilityProfile, bool, bool, bool>)Delegate.CreateDelegate(
                    typeof(Func<AbilityProfile, bool, bool, bool>), poolRecipients);

                var runes = Spell("ExplosiveRunes");
                var runesProfile = runes != null ? AbilityProfile.Get(runes) : null;
                Check(runesProfile != null && runesProfile.SelectsAround && Math.Abs(runesProfile.DeliverDelaySeconds - 2f) < 0.001f &&
                      runesProfile.AoETargets == TargetType.Any && !runesProfile.NeedsAttackRoll && runes.CanTargetPoint,
                      "Explosive Runes: around-selector (Any), 2.0 s delay, no attack roll, can target a point");
                Check(runes != null && Ground(Data(runes)), "Explosive Runes gets ground aim");
                var wail = Spell("WailOfBanshee");
                Check(wail != null && Math.Abs(AbilityProfile.Get(wail).DeliverDelaySeconds - 3f) < 0.001f && Ground(Data(wail)),
                      "Wail of the Banshee (3.0 s delay, 40 ft, Any) gets ground aim");
                var slumber = Spell("DeepSlumber");
                Check(slumber != null && AbilityProfile.Get(slumber).AoETargets == TargetType.Enemy && !Ground(Data(slumber)),
                      "Deep Slumber (enemy-only selector) keeps unit aim: it follows the target and cannot hurt allies");
                var eye = Spell("AngelEyeOfTheSun");
                Check(eye == null || !Ground(Data(eye)), "Eye of the Sun (enemy-only selector) keeps unit aim");
                var sleep = Spell("Sleep");
                Check(sleep != null && !AbilityProfile.Get(sleep).HasDeliveryLag && !Ground(Data(sleep)),
                      "ordinary Sleep (no delivery lag) keeps unit aim");
                if (runes != null)
                {
                    var selective = Data(runes);
                    selective.MetamagicData = new MetamagicData();
                    typeof(MetamagicData).GetField("<MetamagicMask>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.SetValue(selective.MetamagicData, Metamagic.Selective);
                    Check(selective.HasMetamagic(Metamagic.Selective) && !Ground(selective),
                          "Selective Explosive Runes spares allies, so it keeps unit aim");
                }

                // A fusion delivered by Explosive Runes: the profile and the point check follow the deliver spell.
                var holder = all.FirstOrDefault(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                var snowball = Spell("Snowball");
                if (holder != null && runes != null && sleep != null)
                {
                    var fused = Data(holder);
                    fused.MagicHackData = new MagicHackData { Spell1 = runes, Spell2 = sleep, DeliverBlueprint = runes,
                        SavingThrowType = Kingmaker.EntitySystem.Stats.SavingThrowType.Will, SpellSchool = SpellSchool.Enchantment, SpellLevel = 2 };
                    var fusedProfile = AbilityProfile.Get(fused);
                    Check(fused.CanTargetPoint && Math.Abs(fusedProfile.DeliverDelaySeconds - 2f) < 0.001f && fusedProfile.SelectsAround,
                          "a fusion delivered by Explosive Runes reads the deliver spell's delay, selector and point targeting");
                    Check(Ground(fused), "the Explosive Runes/Sleep fusion (outcome2's friendly hits) gets ground aim");
                    if (snowball != null)
                    {
                        var touch = Data(holder);
                        touch.MagicHackData = new MagicHackData { Spell1 = snowball, Spell2 = sleep, DeliverBlueprint = snowball,
                            SavingThrowType = Kingmaker.EntitySystem.Stats.SavingThrowType.Will, SpellSchool = SpellSchool.Enchantment, SpellLevel = 2 };
                        Check(AbilityProfile.Get(touch).NeedsAttackRoll && !Ground(touch),
                              "a fusion delivered by Snowball (an attack-roll projectile) keeps unit aim");
                    }
                }
                else Check(false, "the fusion holder slot, Explosive Runes and Sleep are loaded");

                // A future damaging fused burst must not be intersected with today's pool selector. Pool control
                // remains independently conservative; no future recipient is inserted into its budget.
                var poolHolder = all.First(b => b.name.StartsWith("MagicHackDefaultSlot", StringComparison.Ordinal));
                var pooledCast = Data(poolHolder);
                pooledCast.MagicHackData = new MagicHackData
                {
                    Spell1 = Spell("ExplosiveRunes"), Spell2 = Spell("Sleep"), DeliverBlueprint = Spell("ExplosiveRunes"),
                    SavingThrowType = Kingmaker.EntitySystem.Stats.SavingThrowType.Will, SpellLevel = 2,
                    SpellSchool = SpellSchool.Enchantment
                };
                var pooledProfile = AbilityProfile.Get(pooledCast);
                Check(!currentPool(pooledProfile, true, true), "a planned pooled fusion uses future recipients for its independent payload");
                Check(currentPool(pooledProfile, true, false), "a non-planned pooled fusion retains its current native recipient set");
                Check(currentPool(AbilityProfile.Get(Data(Spell("Sleep"))), true, true), "an ordinary pool retains current selection");
                Check(!currentPool(pooledProfile, false, true), "an unsupported pool cannot discard a burst's other recipients");
                Check(Body(typeof(TacticalEngine), "ScoreAbility").Any(m => m.Name == "UseCurrentPoolRecipients"),
                    "the shared score path uses the independent-payload recipient policy");

                // The whole export: which abilities qualify, and why projectile bursts may not resolve offline.
                int delayed = 0, projectile = 0, unresolved = 0, enemyOnly = 0, grounded = 0;
                var names = new List<string>();
                foreach (var b in all)
                {
                    var around = b.GetComponent<AbilityTargetsAround>();
                    if (around == null) continue;
                    var flight = b.GetComponent<AbilityDeliverProjectile>();
                    bool delay = b.GetComponent<AbilityDeliverDelay>()?.DelaySeconds > 0f;
                    bool simple = flight != null && flight.Type == AbilityProjectileType.Simple;
                    if (!delay && !simple) continue;
                    if (delay) delayed++;
                    if (simple)
                    {
                        projectile++;
                        if (flight.Projectiles.Length == 0 || flight.Projectiles[0] == null) unresolved++;
                    }
                    if (around.Targets == TargetType.Enemy) enemyOnly++;
                    bool g;
                    try { g = Ground(Data(b)); } catch { g = false; }
                    if (g)
                    {
                        grounded++;
                        if (names.Count < 12) names.Add(b.name);
                    }
                }
                lines.Add($"BURST-AIM exports: {delayed} around-bursts with a delivery delay, {projectile} with a simple projectile " +
                          $"({unresolved} whose projectile blueprint is not in these exports - the game loads it), {enemyOnly} enemy-only; " +
                          $"{grounded} get ground aim here: {string.Join(", ", names)}{(grounded > names.Count ? ", ..." : "")}");
                Check(grounded >= 2 && delayed >= grounded - projectile, "the export classification finds the delayed bursts");
                var fireball = Spell("Fireball");
                var fireFlight = fireball?.GetComponent<AbilityDeliverProjectile>();
                Check(fireball != null && fireFlight?.Type == AbilityProjectileType.Simple && !fireFlight.NeedAttackRoll &&
                      fireball.GetComponent<AbilityTargetsAround>()?.Targets == TargetType.Any && fireball.CanTargetPoint,
                      "Fireball is a simple-projectile around-burst (Any, no attack roll, point-capable): ground aim once its projectile loads");
                Check(fireball == null || fireFlight.Projectiles.Length == 0 || fireFlight.Projectiles[0] != null ||
                      AbilityProfile.Get(fireball).ProjectileSpeed == 0f,
                      "an unresolved projectile reads as no flight (no exception, no fabricated speed)");

                // --- compiled routes ---
                var engine = typeof(TacticalEngine);
                var evaluate = Body(engine, "EvaluateAbility");
                Check(Calls(evaluate, engine, "GroundAimBurst") && Calls(evaluate, engine, "PlanBurst") &&
                      Calls(evaluate, typeof(BurstWatch), "Refused") && Calls(evaluate, typeof(AbilityData), "CanTarget"),
                      "EvaluateAbility aims a qualifying burst at its planned point after the native point check");
                var score = Body(engine, "ScoreAbility");
                Check(Calls(score, engine, "BurstFor") && Calls(score, engine, "BurstEnemiesInside") && Calls(score, engine, "BurstWalkingAllies"),
                      "ScoreAbility counts the burst's enemies and allies where they will be when it lands");
                Check(Calls(Body(engine, "Execute"), typeof(BurstWatch), "Track"), "Execute hands an issued burst to BurstWatch");
                Check(Calls(Body(typeof(BurstWatch), "Track"), typeof(CompanionAI_Pathfinder.Analysis.AoeDangerAnalyzer), "RegisterPendingBurst") &&
                      Calls(Body(typeof(BurstWatch), "HandleUnitCommandDidAct"), typeof(CompanionAI_Pathfinder.Analysis.AoeDangerAnalyzer), "PendingBurstLandsIn"),
                      "A4: the landing spot is shared until the burst lands");
                var rt = typeof(CompanionAI_Pathfinder.GameInterface.RealTimeController);
                var process = Body(rt, "ProcessUnit");
                Check(Calls(process, typeof(BurstWatch), "ShouldCancel") && Calls(process, typeof(UnitCommand), "Interrupt"),
                      "A3: real time cancels a burst before its cast goes off when the re-check says so");
                var ready = engine.Assembly.GetTypes().SelectMany(t => t.GetMethods(Hidden)).FirstOrDefault(m => m.Name == "ReadyToEndTurn");
                Check(ready != null && SaveBoostCheck.ReferencedMembers(ready).Any(m => m.DeclaringType == typeof(BurstWatch) && m.Name == "PendingLanding"),
                      "A5: a turn-based turn waits for its own burst to land");
                Check(engine.Assembly.GetTypes().SelectMany(t => t.GetMethods(Hidden)).Where(m => m.Name == "OnToggle" || m.Name == "Load")
                          .Any(m => SaveBoostCheck.ReferencedMembers(m).Any(r => r.DeclaringType == typeof(BurstWatch) && r.Name == "Subscribe")),
                      "the mod subscribes BurstWatch when it loads or is switched on");

                // --- ★ review follow-ups A-R1, A3, A-P1 (docs/BURST_PATH_FORECAST.md) ---
                // Native: a command's walk is the movement agent's path, followed waypoint by waypoint at the unit's speed,
                // and it stops once the command is close enough.
                var agent = typeof(Kingmaker.View.UnitMovementAgent);
                var view = typeof(Kingmaker.View.UnitEntityView);
                Check(Calls(Body(typeof(UnitCommand), "TickApproaching"), view, "MoveTo") && Calls(Body(view, "MoveTo"), agent, "PathTo"),
                      "A-R1: an approaching command walks through the movement agent (TickApproaching -> MoveTo -> PathTo)");
                var nextField = agent.GetField("m_NextPointIndex", Hidden);
                var waypoint = Body(agent, "SetWaypoint");
                Check(nextField?.FieldType == typeof(int) && waypoint.Contains(nextField) && waypoint.Any(m => m.Name == "vectorPath"),
                      "A-R1: the agent follows Path.vectorPath from its next waypoint (m_NextPointIndex, int)");
                var pathProperty = typeof(Kingmaker.View.UnitMovementAgentBase).GetProperty("Path", Hidden);
                var vectorPath = pathProperty?.PropertyType.GetField("vectorPath");
                Check(pathProperty?.PropertyType.FullName == "Pathfinding.Path" &&
                      vectorPath?.FieldType == typeof(List<UnityEngine.Vector3>),
                      "A-R1: the agent's Path is a Pathfinding.Path whose vectorPath is a list of points");
                Check(Getter(typeof(Kingmaker.View.UnitMovementAgentBase), "MaxSpeed").Any(m => m.Name == "get_CurrentSpeedMps"),
                      "A-R1: the agent walks at the unit's CurrentSpeedMps (MaxSpeed)");
                var close = typeof(UnitCommand).GetMethods(Hidden).First(m => m.Name == "IsUnitCloseEnough" && m.GetParameters().Length == 0);
                var closeBody = SaveBoostCheck.ReferencedMembers(close).ToList();
                Check(Calls(closeBody, typeof(UnitCommand), "get_ApproachPoint") && Calls(closeBody, typeof(UnitCommand), "get_ApproachRadius"),
                      "A-R1: a command is close enough within its approach radius of its approach point (the walk's stop)");
                // Routes: the forecast reads the agent's existing path; no path search, raycast or world query.
                var readRoute = Body(engine, "ReadRoute");
                var addPath = Body(engine, "AddNativePath");
                Check(Calls(readRoute, engine, "WalkingCommand") && Calls(readRoute, engine, "AddNativePath") &&
                      Calls(readRoute, engine, "StopRouteAtApproachDistance") && Calls(readRoute, typeof(SpotRules), "WalkEnd") &&
                      addPath.Any(m => m.Name == "get_AgentASP") && addPath.Any(m => m.Name == "get_Path") && addPath.Contains(vectorPath),
                      "A-R1: ReadRoute follows the agent's path to the command's stop, else the straight walk");
                // Execute the production request matcher with real ABPath/XPath layouts, without PathPool/Seeker,
                // navmesh, commands or units. A terminal near the new aim is not proof of the old path's requested aim.
                var pathAssembly = pathProperty.PropertyType.Assembly;
                var abType = pathAssembly.GetType("Pathfinding.ABPath", true);
                var xpathType = pathAssembly.GetType("Pathfinding.XPath", true);
                var originalEnd = abType.GetField("originalEndPoint");
                var matchPath = engine.GetMethod("NativePathMatchesAim", Hidden);
                var aimPoint = new UnityEngine.Vector3(10f, 0f, 10f);
                bool Matches(Type type, UnityEngine.Vector3 requested, UnityEngine.Vector3 end, float radius = 2f)
                {
                    var path = RuntimeHelpers.GetUninitializedObject(type);
                    originalEnd.SetValue(path, requested);
                    vectorPath.SetValue(path, new List<UnityEngine.Vector3> { UnityEngine.Vector3.zero, end });
                    return (bool)matchPath.Invoke(null, new object[] { path, aimPoint, radius });
                }
                Check(!Matches(abType, new UnityEngine.Vector3(0f, 0f, 10f), new UnityEngine.Vector3(10f, 0f, 9f)),
                      "an unrelated path ending inside the new command's radius cannot supply its burst forecast");
                Check(Matches(abType, aimPoint, aimPoint), "the native ABPath with this requested endpoint is accepted");
                Check(Matches(xpathType, aimPoint, new UnityEngine.Vector3(9f, 0f, 10f)),
                      "an XPath may stop short while retaining the matching original endpoint");
                Check(!Matches(abType, aimPoint, UnityEngine.Vector3.zero),
                      "a partial terminal outside native approach distance is not treated as arrival");
                Check(Matches(abType, aimPoint, new UnityEngine.Vector3(10f, 4f, 10f), 2.5f),
                      "path admission uses native mechanics distance including its height rule, not an arbitrary horizontal tolerance");
                Check(Calls(addPath, engine, "NativePathMatchesAim") &&
                      Body(engine, "NativePathMatchesAim").Any(m => m.Name == "originalEndPoint"),
                      "the production route validates the original path request before reading its waypoints");
                var stopRoute = engine.GetMethod("StopRouteAtApproachDistance", Hidden);
                var nativeStop = new WalkRoute();
                nativeStop.Start(0f, 0f, 0f, 2f); nativeStop.Add(10f, 0f, 0f);
                stopRoute.Invoke(null, new object[] { nativeStop, new UnityEngine.Vector3(10f, 0f, 0f), 2f });
                Check(Math.Abs(nativeStop.EndX - 8f) < 1e-4,
                      "the production native-path distance stop uses the full command radius, not WalkEndShare's extra travel");
                var descending = new WalkRoute();
                descending.Start(0f, 8f, 0f, 2f); descending.Add(10f, 8f, 0f); descending.Add(11f, 0f, 0f);
                var destination = new UnityEngine.Vector3(11f, 0f, 0f);
                stopRoute.Invoke(null, new object[] { descending, destination, 2f });
                var stopPoint = new UnityEngine.Vector3(descending.EndX, descending.EndY, descending.EndZ);
                Check(Math.Abs(descending.EndX - 10.5f) < 1e-4 &&
                      Owlcat.Runtime.Core.Utils.GeometryUtils.SqrMechanicsDistance(stopPoint, destination) <= 4f &&
                      Owlcat.Runtime.Core.Utils.GeometryUtils.SqrMechanicsDistance(new UnityEngine.Vector3(10.4f, 4.8f, 0f), destination) > 4f,
                      "the production stop intersects the native height/planar contract; earlier horizontal overlap is not arrival");
                var lowPath = new WalkRoute();
                lowPath.Start(0f, 1f, 0f, 2f); lowPath.Add(10f, 1f, 0f);
                var lowDestination = new UnityEngine.Vector3(10f, 0f, 0f);
                stopRoute.Invoke(null, new object[] { lowPath, lowDestination, 1f });
                Check(Math.Abs(lowPath.EndX - 9f) < 1e-4 &&
                      Owlcat.Runtime.Core.Utils.GeometryUtils.SqrMechanicsDistance(
                          new UnityEngine.Vector3(lowPath.EndX, lowPath.EndY, lowPath.EndZ), lowDestination) == 1f,
                      "the production stop preserves native height forgiveness rather than replacing mechanics distance with 3D distance");
                var searches = new[] { "PathTo", "FindPath", "StartPath", "StartPathWithClerance", "Linecast", "Raycast", "HasObstacle" };
                bool SceneUnits(MemberInfo m) => m.Name == "Units" && m.DeclaringType == typeof(Kingmaker.EntitySystem.PersistentState);
                Check(!readRoute.Concat(addPath).Any(m => searches.Contains(m.Name) || SceneUnits(m)),
                      "A-R1: the route read starts no path search, raycast or unit scan");
                Check(engine.GetMethod("ReadNextPointRef", Hidden)?.Invoke(null, null) != null,
                      "A-R1: the next-waypoint read resolves on the supplied assembly");
                Check(Calls(Body(engine, "RouteOf"), engine, "ReadRoute") && Calls(Body(engine, "PlanBurst"), engine, "RouteOf") &&
                      Calls(Body(engine, "PredictedAt"), engine, "RouteOf") && Calls(Body(engine, "BurstWalkingAllies"), engine, "RouteOf") &&
                      Calls(Body(engine, "BurstWalkingAllies"), typeof(WalkRoute), "MayBeInside"),
                      "A-R1: the lead, the enemies inside and the walking allies are read along each unit's route, once per decision");
                // A3: the decision prices walk-in allies by its own control rule, and the re-check adds that price.
                var allyControl = engine.GetNestedType("BurstPlan", BindingFlags.NonPublic)?.GetField("AllyControl", Hidden);
                var evaluateWatch = Body(typeof(BurstWatch), "Evaluate");
                Check(allyControl != null && score.Contains(allyControl) && evaluateWatch.Contains(allyControl) &&
                      Calls(evaluateWatch, typeof(BurstWatch), "DamageTo"),
                      "A3: ScoreAbility keeps each walk-in ally's control price; the re-check adds it to the damage it estimates once");
                // A-P1: one roster per frame, no live watch dropped.
                Check(Calls(evaluateWatch, typeof(BurstWatch), "RosterNow") && Calls(evaluateWatch, engine, "ReadRoute") &&
                      !evaluateWatch.Any(SceneUnits) && Body(typeof(BurstWatch), "RosterNow").Any(SceneUnits) &&
                      Body(typeof(BurstWatch), "RosterNow").Any(m => m.Name == "get_frameCount"),
                      "A-P1: a re-check reads the units gathered once per frame");
                var track = Body(typeof(BurstWatch), "Track");
                Check(Calls(track, typeof(BurstWatch), "MakeRoom") && !track.Any(m => m.Name == "RemoveAt"),
                      "A-P1: Track makes room only through MakeRoom");
                Check(MakeRoomKeepsLiveWatches(), "A-P1: a full watch list drops the burst landing first, never a cast that has not gone off");
                var combatEnd = engine.Assembly.GetTypes().SelectMany(t => t.GetMethods(Hidden)).FirstOrDefault(m => m.Name == "OnPartyCombatEnd");
                Check(combatEnd != null && SaveBoostCheck.ReferencedMembers(combatEnd).Any(m => m.DeclaringType == typeof(BurstWatch) && m.Name == "ReportFight") &&
                      Calls(Body(typeof(BurstWatch), "Clear"), typeof(BurstWatch), "ReportFight"),
                      "A-P1: each fight's watch counts are logged when it ends (or when the next one starts without an end)");
                // The route itself: a walk round a wall is caught on its path and missed by the straight line.
                var round = new WalkRoute();
                round.Start(0f, 0f, 0f, 2f);
                round.Add(0f, 0f, 8f);
                round.Add(10f, 0f, 8f);
                round.Add(10f, 0f, 0f);
                var line = new WalkRoute();
                line.Start(0f, 0f, 0f, 2f);
                line.Add(10f, 0f, 0f);
                Check(round.MayBeInside(5f, 8f, 1.5f, 6.5f) && !line.MayBeInside(5f, 8f, 1.5f, 6.5f),
                      "A-R1: a walk round a wall is caught along its path, where the straight line misses it");
            }
            catch (Exception ex)
            {
                Check(false, $"{ex.GetType().Name}: {ex.Message}");
            }
            lines.Insert(0, $"BURST-AIM {checks} checks, failures {bad}; native contracts, real blueprints and compiled routes; " +
                            "no actor, selection, command, cast or Unity timing");
            failures = bad;
            return lines;
        }
    }
}
