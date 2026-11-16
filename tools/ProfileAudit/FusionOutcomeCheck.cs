// ★ Fusion F4 (docs/FUSION_OUTCOME_EFFECTS_PLAN.md): fused spells whose effect lands on a passed root save or on both
// outcomes. Actual source graphs, the shared admission rule, the two-outcome value and the compiled routes.
// Actor-free: no Game/Player, Rulebook, cast, command or Unity timing.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace ProfileAudit
{
    internal static class FusionOutcomeCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private delegate void OneOutcome(float fail, float delivery, float failed, float passed, float hp, float worth, float controlWorth,
            float rounds, bool regenerating, bool endsRegeneration, out float damage, out float control);
        private delegate void TwoOutcomes(float fail, float delivery, float failed, float passed, float hp, float worth, float controlWorth,
            float rounds, float passRounds, bool regenerating, bool endsRegeneration, out float damage, out float control);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FUSION-OUTCOME FAILED: " + why); } }
            try
            {
                var all = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => all.First(b => b.name == name);
                var holder = all.Single(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                AbilityData Fuse(string a, string b, SavingThrowType save)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new MagicHackData { Spell1 = Spell(a), Spell2 = Spell(b), DeliverBlueprint = Spell(a),
                        SavingThrowType = save, SpellSchool = SpellSchool.Evocation, SpellLevel = 4 };
                    return data;
                }
                var engine = typeof(TacticalEngine);
                var withheld = engine.GetMethod("FusionWithheld", Hidden);
                var model = engine.GetMethod("FusionOutcomeModel", Hidden);
                var unsupported = typeof(AbilityProfile).GetProperty("FusionUnsupported", Hidden);
                var location = typeof(AbilityProfile).GetProperty("FusionOutcomeEffectLocation", Hidden);
                var buffPath = typeof(AbilityProfile).GetMethod("FusionBuffPath", Hidden);
                var applications = typeof(AbilityProfile).GetProperty("BuffApplications", Hidden);
                var reason = typeof(AbilityProfile).GetField("FusionOutcomeMetadataReason", Hidden);
                string gate = (string)engine.GetField("FusionOutcomeGate", Hidden)?.GetValue(null);
                Check(withheld != null && model != null && unsupported != null && location != null && buffPath != null &&
                      applications != null && reason != null && gate != null, "production admission, path and qualification contracts exist");

                string Withheld(AbilityData a) => (string)withheld.Invoke(null, new object[] { a, AbilityProfile.Get(a) });
                string Paths(AbilityData a, string buffName)
                {
                    var profile = AbilityProfile.Get(a);
                    var found = ((IEnumerable)applications.GetValue(profile)).Cast<object>()
                        .Where(o => ((BlueprintBuff)o.GetType().GetField("Buff").GetValue(o)).name == buffName)
                        .Select(o => buffPath.Invoke(profile, new[] { o.GetType().GetField("Action", Hidden).GetValue(o) }).ToString())
                        .Distinct().OrderBy(s => s, StringComparer.Ordinal);
                    return string.Join("+", found);
                }

                // 1. The actual graphs: which root-save outcome runs each application.
                var fear = Fuse("Fireball", "Fear", SavingThrowType.Reflex);
                Check(Paths(fear, "Shaken") == "Passed" && Paths(fear, "Frightened") == "Failed",
                    $"actual Fear: shaken on the passed branch, frightened on the failed one ({Paths(fear, "Shaken")}/{Paths(fear, "Frightened")})");
                var bone = Fuse("HoldPerson", "Boneshatter", SavingThrowType.Will);
                Check(Paths(bone, "Fatigued") == "Passed" && Paths(bone, "Exhausted") == "Failed",
                    $"actual Boneshatter: fatigued on the passed branch, exhausted on the failed one ({Paths(bone, "Fatigued")}/{Paths(bone, "Exhausted")})");
                var frigid = Fuse("Slow", "FrigidTouchCast", SavingThrowType.Will);
                var frigidProfile = AbilityProfile.Get(frigid);
                var frigidPaths = ((IEnumerable)applications.GetValue(frigidProfile)).Cast<object>()
                    .Where(o => (int)o.GetType().GetField("SourceOrdinal", Hidden).GetValue(o) == 2)
                    .Select(o => buffPath.Invoke(frigidProfile, new[] { o.GetType().GetField("Action", Hidden).GetValue(o) }).ToString()).Distinct().ToList();
                Check(frigidPaths.Count == 1 && frigidPaths[0] == "Both", "actual Frigid Touch: its stagger runs on both outcomes (" + string.Join(",", frigidPaths) + ")");

                // 2. Admitted through the joint outcome value, under either root save.
                foreach (var a in new[] { fear, Fuse("Fireball", "Fear", SavingThrowType.Will), bone, Fuse("HoldPerson", "Boneshatter", SavingThrowType.Fortitude),
                                          frigid, Fuse("IncendiaryRunes", "Fear", SavingThrowType.Reflex), Fuse("ConeOfCold", "Fear", SavingThrowType.Will) })
                {
                    var profile = AbilityProfile.Get(a); var hack = a.MagicHackData;
                    Check(unsupported.GetValue(profile) == null && location.GetValue(profile) != null && Withheld(a) == null &&
                          (bool)model.Invoke(null, new object[] { a, profile }),
                        $"{hack.Spell1.name}/{hack.Spell2.name} ({hack.SavingThrowType}) is offered through the joint outcome value " +
                        $"(withheld: {Withheld(a) ?? "no"}, reason: {reason.GetValue(profile) ?? "none"})");
                }

                // 3. Withheld: an effect outside the failed branch that the joint value cannot yet price never falls back
                //    to the older forecast.
                void Gate(string a, string b, SavingThrowType save, string expected)
                {
                    var data = Fuse(a, b, save); var profile = AbilityProfile.Get(data);
                    string why = Withheld(data);
                    Check(unsupported.GetValue(profile) == null && location.GetValue(profile) != null && why == gate &&
                          ((string)reason.GetValue(profile) ?? "").Contains(expected),
                        $"{a}/{b} ({save}) stays withheld by the outcome gate for '{expected}' (withheld: {why ?? "no"}, reason: {reason.GetValue(profile) ?? "none"})");
                }
                Gate("Fireball", "MoltenOrb", SavingThrowType.Reflex, "ContextConditionIsMainTarget");
                Gate("Fireball", "Disintegrate", SavingThrowType.Reflex, "ContextConditionCompareTargetHP");
                Gate("Fireball", "ReducePerson", SavingThrowType.Reflex, "carrier");
                Gate("Fireball", "Enervation", SavingThrowType.Reflex, "non-HP damage");
                var sleep = Fuse("Snowball", "Sleep", SavingThrowType.Will);
                Check((string)unsupported.GetValue(AbilityProfile.Get(sleep)) == "shared Hit Dice selection" && Withheld(sleep) == "shared Hit Dice selection",
                    "an unrelated structural refusal (played Snowball/Sleep) is unchanged and reported as itself");

                // 4. Unchanged: a fusion with failed-branch control only carries no outcome requirement.
                foreach (var a in new[] { Fuse("Slow", "ConfusionSpell", SavingThrowType.Will), Fuse("IncendiaryRunes", "Snowball", SavingThrowType.Reflex),
                                          Fuse("ExplosiveRunes", "Blindness", SavingThrowType.Fortitude) })
                    Check(location.GetValue(AbilityProfile.Get(a)) == null && Withheld(a) == null,
                        $"{a.MagicHackData.Spell1.name}/{a.MagicHackData.Spell2.name} has no effect outside the failed branch and is offered as before");
                var noSave = Fuse("ExplosiveRunes", "FrigidTouchCast", SavingThrowType.Unknown);
                Check(location.GetValue(AbilityProfile.Get(noSave)) == null && Withheld(noSave) == null,
                    "played no-save Explosive Runes/Frigid Touch has one outcome and no outcome requirement");

                // 5. Slice B: Ray of Exhaustion's own exclusive branches are not a dependency; another source's earlier
                //    application of the fact it reads still is.
                var ray = Fuse("Fireball", "RayOfExhaustion", SavingThrowType.Reflex); var rayProfile = AbilityProfile.Get(ray);
                Check(Withheld(ray) == null && (bool)model.Invoke(null, new object[] { ray, rayProfile }),
                    $"Fireball/Ray of Exhaustion qualifies: its 'already fatigued' and saved branches are opposite branches of one read (reason: {reason.GetValue(rayProfile) ?? "none"})");
                Check(Paths(ray, "Exhausted") == "Both+Failed" && Paths(ray, "Fatigued") == "Passed",
                    $"actual Ray of Exhaustion: exhausted outright when already fatigued, else by the save ({Paths(ray, "Exhausted")}/{Paths(ray, "Fatigued")})");
                var after = Fuse("Boneshatter", "RayOfExhaustion", SavingThrowType.Fortitude); var afterProfile = AbilityProfile.Get(after);
                Check(Withheld(after) == gate && ((string)reason.GetValue(afterProfile) ?? "").Contains("earlier application changes a guard fact"),
                    $"Boneshatter then Ray of Exhaustion stays withheld: the first payload changes the fact the second reads (reason: {reason.GetValue(afterProfile) ?? "none"})");
                var twice = CasterPolicyCheck.Data(holder);
                twice.MagicHackData = new MagicHackData { Spell1 = Spell("RayOfExhaustion"), Spell2 = Spell("RayOfExhaustion"), DeliverBlueprint = Spell("RayOfExhaustion"),
                    SavingThrowType = SavingThrowType.Fortitude, SpellSchool = SpellSchool.Necromancy, SpellLevel = 3 };
                Check(Withheld(twice) == gate, "the same checker read by both source payloads is two native evaluations and stays a dependency");

                // 6. Compiled routes.
                bool Calls(MethodInfo m, string name) => m != null && SaveBoostCheck.ReferencedMembers(m).Any(x => x.Name == name);
                var evaluate = engine.GetMethod("EvaluateAbility", Hidden);
                var order = SaveBoostCheck.ReferencedMembers(evaluate).Select(m => m.Name).ToList();
                Check(order.IndexOf("SourceOf") >= 0 && order.IndexOf("FusionWithheld") > order.IndexOf("SourceOf"),
                    "the user's blocklist is read before Fusion admission: a blocked fused spell stays blocked");
                Check(Calls(withheld, "FusionOutcomeModel") && Calls(withheld, "get_FusionOutcomeEffectLocation") && Calls(withheld, "get_FusionUnsupported"),
                    "one admission rule joins the structural refusal and the outcome gate");
                var rounds = engine.GetMethod("TryFusionControlRounds", Hidden);
                Check(Calls(rounds, "FusionBuffPath") && Calls(rounds, "get_FusionControlPassScratch") || Calls(rounds, "FusionBuffPath") &&
                      SaveBoostCheck.ReferencedMembers(rounds).Any(m => m.Name == "FusionControlPassScratch"),
                    "the control reader fills a second envelope for the passed outcome");
                Check(!Calls(rounds, "Trigger") && !Calls(rounds, "SaveFailChance") && !Calls(rounds, "FindPath"),
                    "the control reader adds no rule, save or path query");
                var values = engine.GetMethod("TryFusionValues", Hidden);
                Check(Calls(values, "FusionValue") && Calls(values, "FusionOutcomeShare"), "the joint value weights each outcome's control by that outcome");
                var catches = engine.GetMethod("FusionControlCatches", Hidden);
                Check(Calls(catches, "TryFusionControlRounds"), "ally safety reads both outcomes through the same reader");

                // 7. The shared formula on closed forms, and its allocation.
                var overloads = typeof(ValueModel).GetMethods(Hidden).Where(m => m.Name == "FusionValue").ToList();
                var value = (TwoOutcomes)Delegate.CreateDelegate(typeof(TwoOutcomes), overloads.Single(m => m.GetParameters().Length == 13));
                var earlier = (OneOutcome)Delegate.CreateDelegate(typeof(OneOutcome), overloads.Single(m => m.GetParameters().Length == 12));
                var share = (Func<float, bool, bool, float>)Delegate.CreateDelegate(typeof(Func<float, bool, bool, float>),
                    typeof(ValueModel).GetMethod("FusionOutcomeShare", Hidden));
                value(0.3f, 1f, 0f, 0f, 100f, 0f, 30f, 0f, 1f, false, false, out _, out float passOnly);
                Check(Math.Abs(passOnly - 0.7f * 30f) < 0.001f, "pass-only control is worth the passed chance times its rounds");
                value(0.3f, 1f, 0f, 0f, 100f, 0f, 30f, 0.4f, 0.4f, false, false, out _, out float either);
                value(0.9f, 1f, 0f, 0f, 100f, 0f, 30f, 0.4f, 0.4f, false, false, out _, out float eitherLikely);
                Check(Math.Abs(either - 12f) < 0.001f && Math.Abs(eitherLikely - 12f) < 0.001f, "control on both outcomes does not depend on the fail chance");
                earlier(0.4f, 1f, 0f, 0f, 100f, 40f, 30f, 2f, false, false, out float oldDamage, out float old);
                value(0.4f, 1f, 0f, 0f, 100f, 40f, 30f, 2f, 0f, false, false, out float sameDamage, out float same);
                Check(old == same && oldDamage == sameDamage, "without a passed-outcome effect the value is bit-identical to the earlier formula");
                for (int i = 0; i < 128; i++) { value(0.3f, 0.8f, 20f, 10f, 60f, 40f, 30f, 2f, 1f, false, false, out _, out _); share(0.3f, true, true); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++)
                {
                    value(0.3f, 0.8f, 20f, 10f, 60f, 40f, 30f, 2f, 1f, false, false, out _, out _);
                    share(0.3f, true, true);
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(bytes == 0, "the two-outcome value allocates nothing when warm");
                lines.Add($"FUSION-OUTCOME value: 10000 warm two-outcome calls, {bytes} bytes; scalar microcheck, not Unity timing");
            }
            catch (Exception ex) { bad++; lines.Add("FUSION-OUTCOME FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"FUSION-OUTCOME {checks} checks, failures {bad}; actual source graphs, shared admission rule, two-outcome value and compiled routes; no actor, Rulebook, cast, command or Unity timing");
            return lines;
        }
    }
}
