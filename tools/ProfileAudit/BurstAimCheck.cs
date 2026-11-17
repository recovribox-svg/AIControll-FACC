// ★ burst aim (docs/BURST_AIM_AND_TACTICS_PLAN.md §3): bursts that land after their cast goes off are aimed at the
// ground where their anchor will be. Native contracts on the real assembly, the A1 classification on the real
// exported blueprints (and a fusion built on Explosive Runes), and the compiled routes of A2-A5.
// Actor-free: no Game/Player, context, selection, command, cast or Unity timing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
