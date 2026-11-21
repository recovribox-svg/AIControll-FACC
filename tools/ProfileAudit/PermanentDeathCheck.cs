// ★ coverage3 review / Fusion coverage C3 (docs/COVERAGE3_REVIEW.md): casts that could kill an ally for good, and allies under
// an enemy's control. Native contracts, the actual Disintegrate graphs, a fused cast, the export census and compiled routes.
// Actor-free: no Game/Player, Rulebook, cast or random roll.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;

namespace ProfileAudit
{
    internal static class PermanentDeathCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo ScanAbility = typeof(CombatMath).GetMethod("DeathScanOf", Hidden, null, new[] { typeof(AbilityData) }, null);
        private static readonly MethodInfo ScanBlueprint = typeof(CombatMath).GetMethod("DeathScanOf", Hidden, null, new[] { typeof(BlueprintAbility) }, null);
        private static readonly MethodInfo CanKill = typeof(CombatMath).GetMethod("CanKillPermanently", Hidden);
        private static readonly MethodInfo KillsOnStart = typeof(AbilityProfile).GetMethod("KillsOnStart", Hidden);

        /// <summary>A read-only view of the engine's internal CombatMath.DeathScan.</summary>
        private sealed class Scan
        {
            internal readonly List<KeyValuePair<string, ContextActionDealDamage>> Damage = new List<KeyValuePair<string, ContextActionDealDamage>>();
            internal readonly List<KeyValuePair<string, int?>> Gates = new List<KeyValuePair<string, int?>>();
            internal int Kills;
            internal bool SavePaths;
            internal SavingThrowType Save;
            internal bool Empty => Gates.Count == 0 && Kills == 0;

            internal Scan(object raw)
            {
                object Field(string name) => raw.GetType().GetField(name, Hidden).GetValue(raw);
                foreach (var pair in (IEnumerable)Field("Damage"))
                    Damage.Add(new KeyValuePair<string, ContextActionDealDamage>(Pair(pair, "Key").ToString(), (ContextActionDealDamage)Pair(pair, "Value")));
                foreach (var pair in (IEnumerable)Field("Gates"))
                    Gates.Add(new KeyValuePair<string, int?>(Pair(pair, "Key").ToString(), (int?)Pair(pair, "Value")));
                Kills = ((ICollection)Field("Kills")).Count;
                SavePaths = (bool)Field("SavePaths");
                Save = (SavingThrowType)Field("Save");
            }

            private static object Pair(object pair, string name) => pair.GetType().GetProperty(name).GetValue(pair);
        }

        private static Scan Of(AbilityData ability) => new Scan(ScanAbility.Invoke(null, new object[] { ability }));
        private static Scan Of(BlueprintAbility blueprint) => new Scan(ScanBlueprint.Invoke(null, new object[] { blueprint }));

        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("PERMADEATH FAILED: " + why); } }
            try
            {
                // --- native contracts ---
                Check(typeof(ContextConditionCompareTargetHP).GetField("m_CompareType", Hidden) != null &&
                      Body(typeof(ContextConditionCompareTargetHP), "CheckCondition").Any(m => m.Name == "get_HPLeft"),
                    "native: the HP comparison reads the target's HP left (after the damage before it)");
                Check(Body(typeof(ContextActionKill), "RunAction").Any(m => m.Name == "set_MarkedForDeath"),
                    "native: a kill marks the unit for death (a real death, not a knock-out)");
                var saved = Body(typeof(ContextActionConditionalSaved), "RunAction");
                Check(saved.Any(m => m.Name == "get_SavingThrow") && saved.Any(m => m.Name == "get_IsPassed"),
                    "native: the saved branches follow the rolled save; with none, neither runs");
                Check(typeof(UnitDescriptor).GetProperty("FactionOriginal") != null &&
                      Body(typeof(ChangeFaction), "OnTurnOn").Any(m => m.Name == "GetFactionSwitcher") &&
                      Body(typeof(UnitDescriptor.FactionSwitcher), "Start").Any(m => m.Name == "SwitchFactions"),
                    "native: Dominate's faction change is a temporary switcher; the unit keeps its original faction");

                // --- the actual Disintegrate graphs ---
                var all = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => all.FirstOrDefault(b => b.name == name);
                var disintegrate = Spell("Disintegrate");
                var dust = loaded.OfType<BlueprintBuff>().FirstOrDefault(b => b.AssetGuid.ToString() == "f7a6a7d2cfeb36643915aece45349827");
                Check(dust != null && (bool)KillsOnStart.Invoke(null, new object[] { dust }), "DisintegrateBuff (f7a6a7d2) kills on activation");
                var scan = disintegrate != null ? Of(disintegrate) : null;
                Check(scan != null && scan.SavePaths && scan.Save == SavingThrowType.Fortitude && scan.Kills == 0 &&
                      scan.Gates.Count == 2 && scan.Gates.All(g => g.Value == 0) &&
                      scan.Gates.Any(g => g.Key == "Failed") && scan.Gates.Any(g => g.Key == "Passed"),
                    "Disintegrate: a dust gate (HP left below 0) after the damage on both outcomes of its Fortitude save");
                Check(scan != null && scan.Damage.Count == 2 &&
                      scan.Damage.Any(d => d.Key == "Passed" && d.Value.Value.DiceCountValue.Value == 5) &&
                      scan.Damage.Any(d => d.Key == "Failed" && d.Value.Value.DiceCountValue.ValueType == Kingmaker.UnitLogic.Mechanics.ContextValueType.Rank),
                    "Disintegrate: 5d6 on a passed save, the ranked 2d6 per level on a failed one");

                // --- a fused cast: both payloads; the C3 guard is admitted ---
                var holder = all.Single(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                AbilityData Fuse(string a, string b, SavingThrowType save)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new MagicHackData { Spell1 = Spell(a), Spell2 = Spell(b), DeliverBlueprint = Spell(a),
                        SavingThrowType = save, SpellSchool = SpellSchool.Evocation, SpellLevel = 4 };
                    return data;
                }
                var fused = Fuse("Fireball", "Disintegrate", SavingThrowType.Reflex);
                var fusedScan = Of(fused);
                Check(fusedScan.Gates.Count == 2 && fusedScan.Damage.Count > scan.Damage.Count,
                    "Fireball/Disintegrate: the dust gates, and Fireball's damage before them as well as Disintegrate's");
                var independent = typeof(AbilityProfile).GetProperty("FusionIndependentGuards", Hidden);
                var profile = AbilityProfile.Get(fused);
                Check(independent != null && (bool)independent.GetValue(profile),
                    "Fireball/Disintegrate: the dust gate is a stable guard now (C3), on the dust application only");
                var withheld = typeof(TacticalEngine).GetMethod("FusionWithheld", Hidden);
                string why = (string)withheld.Invoke(null, new object[] { fused, profile });
                Check(why == null || !why.Contains("CompareTargetHP"),
                    $"Fireball/Disintegrate is no longer withheld by the HP comparison (withheld: {why ?? "no"})");
                Check((bool)CanKill.Invoke(null, new object[] { fused }) && (bool)CanKill.Invoke(null, new object[] { CasterPolicyCheck.Data(disintegrate) }),
                    "a fused or ordinary Disintegrate is known to be able to kill for good");

                // --- no false positives ---
                foreach (var name in new[] { "Fireball", "Sleep", "HoldPerson", "ExplosiveRunes", "IncendiaryRunes", "AcidArrow" })
                {
                    var bp = Spell(name);
                    Check(bp != null && Of(bp).Empty, $"{name} cannot kill anyone for good");
                }

                // --- the export census ---
                int dustCount = 0, killCount = 0;
                var dustNames = new List<string>();
                var killNames = new List<string>();
                foreach (var bp in all)
                {
                    Scan s;
                    try { s = Of(bp); } catch { continue; }
                    if (s.Gates.Count > 0) { dustCount++; if (dustNames.Count < 10) dustNames.Add(bp.name); }
                    else if (s.Kills > 0) { killCount++; if (killNames.Count < 10) killNames.Add(bp.name); }
                }
                Check(dustCount >= 1, $"the export census finds dust casts: {dustCount}");
                lines.Add($"PERMADEATH exports: {dustCount} abilities with a dust gate (e.g. {string.Join(", ", dustNames)}); " +
                          $"{killCount} with a direct kill or a buff that kills on activation (e.g. {string.Join(", ", killNames)})");

                // --- the policy ---
                Check(PermanentDeathRules.VetoRisk == 0.01f && PermanentDeathRules.Vetoed(0.01f) && !PermanentDeathRules.Vetoed(0.009f) &&
                      PermanentDeathRules.Cost(0.005f, 50f) > 20f,
                    "the author's policy: at 1% the cast is never used; below it, a hundred times the ally's worth");

                // --- compiled routes ---
                var engine = typeof(TacticalEngine);
                var score = Body(engine, "ScoreAbility");
                Check(Calls(score, typeof(CombatMath), "CanKillPermanently") && Calls(score, engine, "PermanentDeathVetoes") &&
                      score.Any(m => m.Name == "TurnedAllies") && Calls(score, engine, "IsCoupDeGrace"),
                    "ScoreAbility vetoes or prices a permanent death and refuses a Coup de Grace on an ally under an enemy's control");
                var vetoes = Body(engine, "PermanentDeathVetoes");
                Check(Calls(vetoes, typeof(CombatMath), "PermanentDeathRisk") && Calls(vetoes, typeof(PermanentDeathRules), "Vetoed") &&
                      Calls(vetoes, engine, "IsSummon"),
                    "the veto reads the exact risk; a summon (which vanishes anyway) keeps the ordinary price");
                // Decide has two overloads: the candidate filter is a lambda named after them.
                var decide = new[] { engine }.Concat(engine.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                    .SelectMany(t => t.GetMethods(Hidden)).Where(m => m.Name.StartsWith("<Decide>", StringComparison.Ordinal))
                    .SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).ToList();
                Check(Calls(decide, engine, "SparesTurnedAlly") && Calls(Body(engine, "BuildContext"), engine, "IsTurnedAlly") &&
                      Body(engine, "IsTurnedAlly").Any(m => m.Name == "get_FactionOriginal"),
                    "an ally under an enemy's control is known by its original faction and spared weapon attacks");
                var evaluate = Body(typeof(BurstWatch), "Evaluate");
                Check(Calls(evaluate, typeof(BurstWatch), "PermanentDeathTo") && Calls(evaluate, engine, "IsTurnedAlly"),
                    "the burst re-check cancels for an ally who could be killed for good, and counts a dominated ally as an ally");
                Check(Calls(Body(typeof(BurstWatch), "ShouldCancel"), typeof(BurstRules), "MayCancel"),
                    "the cancel limit still applies to every other verdict");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"PERMADEATH {checks} checks, {bad} failures");
            return lines;
        }
    }
}
