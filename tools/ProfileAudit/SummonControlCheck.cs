// ★ summon control (docs/TACTICAL_AI.md §156): offline checks of the feature's contracts on the real assemblies -
// settings (off by default, no row for a summon), the temporary settings view, the compiled routes of every policy
// read, and the native turn / summon contracts the turn-based bridge relies on. No actor, command, Harmony patch
// application or Unity timing is executed: turn completion and pack performance are gameplay checks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Settings;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.AI;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Parts;
using Newtonsoft.Json;
using TurnBased.Controllers;

namespace ProfileAudit
{
    internal static class SummonControlCheck
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <summary>Every member a type's own methods reference, compiler-generated nested types included.</summary>
        private static List<MemberInfo> References(Type type)
        {
            var result = new List<MemberInfo>();
            var types = new List<Type> { type };
            for (int i = 0; i < types.Count; i++) types.AddRange(types[i].GetNestedTypes(All));
            foreach (var t in types)
                foreach (var method in t.GetMethods(All))
                {
                    if (method.GetMethodBody() == null) continue;
                    try { result.AddRange(SaveBoostCheck.ReferencedMembers(method)); } catch { }
                }
            return result;
        }

        private static bool Calls(IEnumerable<MemberInfo> references, Type type, string name) =>
            references.Any(m => m.DeclaringType == type && m.Name == name);

        private static List<MemberInfo> Body(Type type, string name, params Type[] parameters)
        {
            var method = parameters.Length == 0
                ? type.GetMethods(All).FirstOrDefault(m => m.Name == name)
                : type.GetMethod(name, All, null, parameters, null);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return SaveBoostCheck.ReferencedMembers(method).ToList();
        }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("SUMMON-CONTROL FAILED: " + why); } }
            try
            {
                // --- settings: off by default, old files load off, no row for a read ---
                var fresh = new ModSettings();
                Check(!fresh.ControlSummons, "a new settings object has summon control off");
                var old = JsonConvert.DeserializeObject<ModSettings>("{\"ControlCompanions\":true}");
                Check(old != null && !old.ControlSummons, "a settings file without the switch loads it off");
                var on = JsonConvert.DeserializeObject<ModSettings>(JsonConvert.SerializeObject(new ModSettings { ControlSummons = true }));
                Check(on.ControlSummons, "the switch survives a Newtonsoft round trip");
                Check(fresh.PeekSettings("summon-1") == null && fresh.CharacterSettings.Count == 0, "a read-only lookup creates no row");
                Check(fresh.PeekSettings(null) == null && fresh.PeekSettings("") == null, "empty ids read as no row");
                var row = fresh.GetOrCreateSettings("seelah", "Seelah");
                Check(fresh.CharacterSettings.Count == 1 && ReferenceEquals(fresh.PeekSettings("seelah"), row), "an existing row is read as it is");
                Check(Localization.Get("ControlSummons") != "ControlSummons" && Localization.Get("ControlSummonsDesc").Contains("old real-time engine"),
                    "the checkbox and its help text exist (the help names the old real-time engine limit)");

                // --- the temporary view: neutral style, the summoner's party-facing choices, never the summoner's row ---
                var owner = new CharacterSettings
                {
                    CharacterId = "nenio", RangePreference = RangePreference.Ranged, PureMage = true, Role = AIRole.Support,
                    HealAtHPPercent = 55, SpellPolicy = SpellResourcePolicy.Progressive, AvoidFriendlyFire = false,
                };
                owner.BlockedAbilities.Add(new AbilityBlockEntry());
                var view = new CharacterSettings { RangePreference = RangePreference.Melee, PureMage = true, Role = AIRole.Tank };
                view.BlockedAbilities.Add(new AbilityBlockEntry());
                SummonSettingsRule.Refresh(view, owner);
                Check(view.RangePreference == RangePreference.Mixed && view.Role == AIRole.DPS && !view.PureMage,
                    "the view is Mixed / DPS / no Pure Mage whatever the summoner chose");
                Check(!PureMagePolicy.IsActive(view), "a Pure Mage summoner does not take a melee summon's weapon attacks away");
                Check(view.HealAtHPPercent == 55 && view.SpellPolicy == SpellResourcePolicy.Progressive && !view.AvoidFriendlyFire,
                    "the view inherits the summoner's heal threshold, spell policy and friendly-fire rule");
                Check(view.BlockedAbilities.Count == 0 && view.CharacterId == "" && view.EnableCustomAI,
                    "the view carries no blocklist or id of its own (the summoner's list applies through the scope)");
                Check(PureMagePolicy.IsActive(owner) && owner.Role == AIRole.Support && owner.BlockedAbilities.Count == 1,
                    "the summoner's row is not changed");
                owner.SpellPolicy = SpellResourcePolicy.Aggressive; owner.AvoidFriendlyFire = true;
                SummonSettingsRule.Refresh(view, owner);
                Check(view.SpellPolicy == SpellResourcePolicy.Aggressive && view.AvoidFriendlyFire, "inherited values follow the summoner on every read");
                SummonSettingsRule.Refresh(view, null);
                var defaults = new CharacterSettings();
                Check(view.SpellPolicy == defaults.SpellPolicy && view.HealAtHPPercent == defaults.HealAtHPPercent &&
                      view.AvoidFriendlyFire == defaults.AvoidFriendlyFire, "without a summoner row the view has the defaults");

                // --- compiled routes: one non-persisting read path ---
                var mod = typeof(TacticalEngine).Assembly;
                var summonControl = typeof(SummonControl);
                var modSettings = typeof(ModSettings);
                foreach (var type in new[] { typeof(TacticalEngine), typeof(ProgressiveGate), typeof(PureMagePolicy),
                                             mod.GetType("CompanionAI_Pathfinder.Analysis.PreBuffAnalyzer", true) })
                {
                    var refs = References(type);
                    Check(!Calls(refs, modSettings, "GetOrCreateSettings"), type.Name + " creates no settings row itself");
                    Check(Calls(refs, summonControl, "SettingsFor"), type.Name + " reads settings through SummonControl.SettingsFor");
                }
                var control = References(summonControl);
                Check(Calls(control, modSettings, "PeekSettings"), "the summoner's row is read without creating one");
                Check(Body(summonControl, "Read").All(m => m.Name != "GetOrCreateSettings") &&
                      Body(summonControl, "BlockScopeId").All(m => m.Name != "GetOrCreateSettings"),
                    "the control check and the blocklist scope create no row");
                var should = Body(typeof(CustomBrainPatch), "ShouldControlUnit", typeof(UnitEntityData), typeof(string).MakeByRefType());
                int summonBranch = should.FindIndex(m => m.DeclaringType == summonControl && m.Name == "IsOrdinarySummon");
                int upsert = should.FindIndex(m => m.DeclaringType == modSettings && m.Name == "GetOrCreateSettings");
                int direct = should.FindIndex(m => m.Name == "get_IsDirectlyControllable");
                Check(summonBranch >= 0 && upsert > summonBranch && direct > summonBranch,
                    "ShouldControlUnit decides a summon before the direct-control test and the per-character row");
                Check(Calls(Body(typeof(UserAbilityPolicy), "ScopeFor", typeof(UnitEntityData)), summonControl, "BlockScopeId"),
                    "the blocklist scope of a summon is its summoner's");
                Check(Calls(References(typeof(UserAbilityPolicy)), summonControl, "Allowed"), "the allowed-ability list applies the summon filter");
                Check(Calls(Body(typeof(TacticalEngine), "Execute"), summonControl, "AbilityRefusal"), "Execute refuses a summon's forbidden ability before side effects");
                var dispatcher = mod.GetType("CompanionAI_Pathfinder.GameInterface.CommandDispatcher", true);
                var run = dispatcher.GetMethods(All).Where(m => m.Name == "Run").SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).ToList();
                Check(Calls(run, summonControl, "ShouldDriveForIssue") && Calls(run, summonControl, "AbilityRefusal"),
                    "the dispatcher uses the fresh authority check and rechecks the ability at issue");
                var finalGate = summonControl.GetMethod("ShouldDriveForIssue", All);
                var finalIl = finalGate.GetMethodBody().GetILAsByteArray();
                Check(Calls(Body(summonControl, "ShouldDriveForIssue"), summonControl, "ReadAuthority") &&
                      finalIl.Length == 9 && finalIl[0] == 0x02 && finalIl[1] == 0x03 && finalIl[2] == 0x17 && finalIl[3] == 0x28 && finalIl[8] == 0x2a,
                    "final issue passes literal fresh=true to the shared authority reader (Release IL)");
                Check(Calls(Body(summonControl, "Read"), typeof(CombatMath), "IsSwarmActor") &&
                      summonControl.GetNestedType("State", All).GetField("SwarmRead", All) == null,
                    "active-fact swarm classification is not cached for the creature's lifetime");
                Check(Body(typeof(CombatMath), "IsSwarmActor").Any(m => m.Name == "Update") &&
                      Body(typeof(CombatMath), "IsSwarmActor").Any(m => m.Name == "SpaceRead"),
                    "fresh swarm classification updates the shared fact snapshot and invalidates dependent geometry");
                // Only the mod's BCL state is exercised; no entity is initialized or logged.
                var stateType = summonControl.GetNestedType("State", All);
                var record = summonControl.GetMethod("RecordAdoption", All);
                var fightField = summonControl.GetField("s_Fight", All);
                var countField = summonControl.GetField("AdoptedThisFight", All);
                var oldFight = fightField.GetValue(null); var oldCount = countField.GetValue(null);
                try
                {
                    var state = Activator.CreateInstance(stateType, true);
                    fightField.SetValue(null, 17); countField.SetValue(null, 0);
                    bool Adopt(object value) => (bool)record.Invoke(null, new[] { value });
                    int Count() => (int)countField.GetValue(null);
                    Check(Adopt(state) && Count() == 1, "first adoption counts the creature once");
                    Check(!Adopt(state) && Count() == 1, "repeated service does not count another creature");
                    stateType.GetField("AdoptionLogged", All).SetValue(state, false);
                    Check(!Adopt(state) && Count() == 1, "release and reacquisition in one fight do not inflate cohort size");
                    fightField.SetValue(null, 18); countField.SetValue(null, 0);
                    Check(Adopt(state) && Count() == 1, "a persistent creature counts in its next fight");
                    Check(Adopt(Activator.CreateInstance(stateType, true)) && Count() == 2, "a different creature counts separately");
                }
                finally { fightField.SetValue(null, oldFight); countField.SetValue(null, oldCount); }
                var preBuff = mod.GetType("CompanionAI_Pathfinder.GameInterface.PreBuffController", true);
                Check(Calls(Body(preBuff, "GetPartyMembers"), summonControl, "IsOrdinarySummon"), "the pre-buff routine leaves summons out (casters and recipients)");
                var ui = mod.GetType("CompanionAI_Pathfinder.UI.MainUI", true);
                Check(Calls(References(ui), summonControl, "IsOrdinarySummon"), "the character catalogue lists no summon");

                // --- the bridge: adapters only, nothing of the game's turn is written ---
                var bridge = typeof(SummonTurnBridge);
                var bridgeRefs = References(bridge);
                var patchRefs = References(typeof(CustomBrainPatch)).Concat(References(typeof(TurnBasedPatches))).ToList();
                foreach (var refs in new[] { bridgeRefs, patchRefs, control })
                    Check(!refs.Any(m => m.Name == "set_Status" || m.Name == "SwitchFactions" || m.Name == "set_GroupId" || m.Name == "UpdateGroup"),
                        "no status, faction or group write in the summon code paths");
                Check(Calls(bridgeRefs, mod.GetType("CompanionAI_Pathfinder.Core.TurnBased.TurnBasedController", true), "ProcessTurn"),
                    "the bridge calls the existing turn-based driver");
                Check(!Calls(bridgeRefs, typeof(TacticalEngine), "Decide") && !Calls(bridgeRefs, typeof(TacticalEngine), "Execute"),
                    "the bridge neither decides nor issues by itself");
                Check(Calls(bridgeRefs, typeof(TurnBasedPatches), "CompleteTurn") &&
                      Calls(Body(typeof(TurnBasedPatches), "TurnControllerTick_Postfix"), typeof(TurnBasedPatches), "CompleteTurn"),
                    "party members and summons share one end-of-turn handling");
                var complete = Body(typeof(TurnBasedPatches), "CompleteTurn");
                Check(Calls(complete, typeof(TurnController), "ForceToEnd") && Calls(complete, typeof(TurnBasedPatches), "ReadyToEndTurn"),
                    "the shared completion waits (projectiles, idle pause, cap) and then ends through the game's ForceToEnd");
                Check(Calls(Body(typeof(CustomBrainPatch), "TickBrain_Prefix"), bridge, "OnBrainTick") &&
                      Calls(Body(typeof(TurnBasedPatches), "TurnControllerTick_Postfix"), bridge, "OnTurnTick"),
                    "the brain prefix and the Tick postfix forward a summon to the bridge");
                foreach (string name in new[] { "Prepare_Prefix", "Prepare_Postfix" })
                {
                    var patch = bridge.GetMethod(name, All);
                    Check(patch != null && patch.IsStatic && patch.GetParameters().Length == 1 &&
                          patch.GetParameters()[0].ParameterType == typeof(TurnController) && patch.GetParameters()[0].Name == "__instance",
                        name + " has the Harmony instance signature");
                }
                Check(!(bool)summonControl.GetField("TurnBridgeBound", All).GetValue(null), "the bridge is unbound until Main binds all of its patches (nothing is patched here)");
                var bind = typeof(Main).GetMethod("PatchTurnControllerPrepareManually", All);
                Check(bind != null && SaveBoostCheck.ReferencedMembers(bind).Any(m => m.DeclaringType == summonControl && m.Name == "TurnBridgeBound") &&
                      SaveBoostCheck.ReferencedMembers(bind).Any(m => m.Name == "get_TickBrainPatched"),
                    "the bridge is bound only with the brain prefix in place");
                var realTime = mod.GetType("CompanionAI_Pathfinder.GameInterface.RealTimeController", true);
                Check(Calls(Body(realTime, "ForgetSummonPacing"), typeof(SummonRules), "Prune") &&
                      Calls(References(typeof(CombatPatches)), realTime, "ForgetSummonPacing"),
                    "summon pacing entries and the pending queue are dropped when a fight starts or ends");

                // --- native contracts the bridge relies on (IL of the supplied game assembly) ---
                var prepare = Body(typeof(TurnController), "Prepare");
                int forceTick = prepare.FindIndex(m => m.DeclaringType == typeof(AiBrainController) && m.Name == "ForceTick");
                int status = prepare.FindIndex(m => m.DeclaringType == typeof(TurnController) && m.Name == "set_Status");
                Check(forceTick >= 0 && status > forceTick, "native Prepare calls the brain before it sets the turn's status");
                Check(typeof(TurnController).GetMethod("Prepare", All)?.GetParameters().Length == 0, "native Prepare() takes no parameters");
                Check(Calls(Body(typeof(TurnController), "Tick"), typeof(AiBrainController), "ForceTick"), "native Tick calls the brain of an idle AI unit");
                Check(Calls(Body(typeof(AiBrainController), "ForceTick"), typeof(AiBrainController), "TickBrain"), "native ForceTick reaches TickBrain");
                var tickBrain = typeof(AiBrainController).GetMethod("TickBrain", All);
                Check(tickBrain != null && tickBrain.IsStatic && tickBrain.GetParameters().Length == 1 &&
                      tickBrain.GetParameters()[0].ParameterType == typeof(UnitEntityData), "native TickBrain(UnitEntityData) is the patched entry");
                Check(Body(typeof(TurnController), "CanEndTurn").Any(m => m.Name == "get_IsDirectlyControllable"),
                    "native CanEndTurn requires direct control (not usable for a summon)");
                var forceToEnd = typeof(TurnController).GetMethod("ForceToEnd", All, null, new[] { typeof(bool) }, null);
                Check(forceToEnd != null && SaveBoostCheck.ReferencedMembers(forceToEnd).All(m => m.Name != "get_IsDirectlyControllable"),
                    "native ForceToEnd(bool) has no controllability requirement");
                Check(Calls(Body(typeof(TurnController), "OnInterruptCommand"), typeof(TurnController), "ForceToEnd"),
                    "the game itself ends an AI unit's turn through ForceToEnd");
                var continueActing = Body(typeof(TurnController), "ContinueActing");
                Check(continueActing.Any(m => m.Name == "TimeWaitedForIdleAI") && continueActing.Any(m => m.Name == "FramesWaitedForStuckAI"),
                    "native ContinueActing ends an idle or stuck AI unit's turn by itself");
                Check(Body(typeof(RuleSummonUnit), "OnTrigger").Any(m => m.DeclaringType == typeof(UnitPartSummonedMonster) && m.Name == "Init"),
                    "RuleSummonUnit gives the creature the summon part and its summoner");
                Check(References(typeof(ContextActionSpawnMonster)).Any(m => m.DeclaringType == typeof(RuleSummonUnit)),
                    "ContextActionSpawnMonster goes through RuleSummonUnit");
                Check(typeof(UnitPartSummonedMonster).GetProperty("Summoner") != null && typeof(UnitPartSummonedMonster).GetProperty("IsLinkedToSummoner") != null,
                    "the summon part exposes its summoner and whether it is linked");
                var controllable = typeof(UnitEntityData).GetProperty("IsDirectlyControllable").GetGetMethod();
                Check(SaveBoostCheck.ReferencedMembers(controllable).OfType<MethodInfo>()
                        .Any(m => m.IsGenericMethod && m.GetGenericArguments().Contains(typeof(UnitPartSummonedMonster))),
                    "the game's direct-control test reads the summon part (a summon is an AI unit by default)");

                // --- blueprints: what a summon is never offered (recognised spawn actions) ---
                var abilities = loaded.OfType<BlueprintAbility>().ToList();
                int spawns = 0, read = 0; var unread = new List<string>();
                foreach (var ability in abilities)
                {
                    var actions = ability.GetComponent<AbilityEffectRunAction>()?.Actions?.Actions;
                    if (actions == null || !actions.Any(a => a is ContextActionSpawnMonster)) continue;
                    spawns++;
                    bool summons;
                    try { summons = AbilityProfile.Get(CasterPolicyCheck.Data(ability))?.Summons ?? false; } catch { summons = false; }
                    if (summons) read++; else unread.Add(ability.name);
                }
                foreach (string name in new[] { "SummonMonsterISingle", "SummonMonsterVIIISingle", "ElementalSwarmAir", "CreepingDoom" })
                {
                    var fixture = abilities.FirstOrDefault(a => a.name == name);
                    if (fixture == null) { lines.Add($"SUMMON-CONTROL SKIPPED: {name} not loaded"); continue; }
                    Check(AbilityProfile.Get(CasterPolicyCheck.Data(fixture))?.Summons ?? false, name + " reads as a summoning spell (never offered to a summon)");
                }
                if (spawns > 0)
                {
                    Check(unread.Count == 0, $"every ability with a top-level spawn action reads as summoning ({unread.Count} do not: {string.Join(", ", unread.Take(8))})");
                    lines.Add($"SUMMON-CONTROL spawn abilities: {read}/{spawns} with a top-level ContextActionSpawnMonster read as summoning; " +
                              "nested or mod-specific spawn actions are a coverage limit");
                }
            }
            catch (Exception ex) { bad++; lines.Add("SUMMON-CONTROL FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"SUMMON-CONTROL {checks} checks, failures {bad}; settings objects, compiled routes, native IL contracts and exported " +
                            "spawn graphs; no actor, patch application, command, turn or Unity timing execution");
            return lines;
        }
    }
}
