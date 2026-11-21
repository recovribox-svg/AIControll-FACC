// ★ Fusion coverage C8 (docs/FUSION_COVERAGE_C6_C8.md): shared Hit Dice pools that kill or deal damage (Circle of Death), rolled
// pools, spreading delivery, and the pool's reach in the joint value, friendly fire and the permanent-death rule.
// Actor-free: no Game/Player, unit, context, selection, Rulebook, cast, command or Unity timing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;

namespace ProfileAudit
{
    internal static class HdPoolCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) => body.Any(m => m.DeclaringType == type && m.Name == name);
        private static bool CallsNamed(IEnumerable<MemberInfo> body, string name) => body.Any(m => m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>(); string info = "";
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("HD-POOL FAILED: " + why); } }
            try
            {
                var engine = typeof(TacticalEngine); var math = typeof(CombatMath);
                var control = engine.Assembly.GetType("CompanionAI_Pathfinder.Tactics.SharedHdControl", true);
                var game = typeof(ContextConditionHitDice).Assembly;

                // --- native contracts ---
                var hd = Body(typeof(ContextConditionHitDice), "CheckCondition");
                Check(CallsNamed(hd, "get_CharacterLevel") && CallsNamed(hd, "get_Item"),
                    "native: a pool's Hit Dice condition compares the recipient's character level with its limit plus the pool");
                Check(CallsNamed(Body(typeof(ContextActionChangeSharedValue), "RunAction"), "get_CharacterLevel"),
                    "native: SubHD takes the recipient's character level from the pool");
                var process = game.GetType("Kingmaker.Controllers.AbilityExecutionProcess", true);
                var approaching = game.GetType("Kingmaker.UnitLogic.Abilities.AbilityApproachingEffect", true);
                Check(CallsNamed(Body(process, "ApplyEffect"), "GetSpreadSpeed") && CallsNamed(Body(process, "ScheduleEffect"), "AddApproachingEffect"),
                    "native: a spreading area schedules each selected recipient as an approaching effect");
                var tick = Body(approaching, "TickApproaching");
                Check(CallsNamed(tick, "get_DeltaTime") && CallsNamed(tick, "get_magnitude"),
                    "native: an approaching effect lands once its front has travelled the distance from the clicked point (distance order)");
                // An iterator: its body is the generated state machine's MoveNext.
                var step = typeof(AbilityDeliverProjectile).GetNestedTypes(Hidden).Where(t => t.Name.Contains("StepBasedDeliver"))
                    .SelectMany(t => t.GetMethods(Hidden).Where(m => m.Name == "MoveNext")).SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).ToList();
                Check((CallsNamed(step, "Units") || CallsNamed(step, "get_Units")) &&
                      (CallsNamed(step, "PassedDistance") || CallsNamed(step, "get_PassedDistance")) && CallsNamed(step, "WouldTargetUnitCone"),
                    "native: a cone or line takes, each frame, every unit its projectile has passed in the game's unit order (why those pools stay withheld)");
                var dice = BuffSuppressCheck.BodyWithLambdas(game.GetType("Kingmaker.UnitLogic.Mechanics.ContextValueHelper", true), "CalculateDiceValue");
                Check(CallsNamed(dice, "HasMetamagic") && CallsNamed(dice, "Trigger") || CallsNamed(dice, "TriggerRule"),
                    "native: a pool's dice are rolled with the cast's Maximize and Empower");

                // --- the actual graphs ---
                var all = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => all.FirstOrDefault(b => b.name == name);
                var build = control.GetMethod("Build", Hidden);
                object PlanOf(BlueprintAbility b) => build.Invoke(null, new object[] { b, AbilityProfile.Get(CasterPolicyCheck.Data(b)) });
                T F<T>(object o, string f) => (T)o.GetType().GetField(f, Hidden).GetValue(o);
                var circle = Spell("CircleOfDeath");
                if (circle != null)
                {
                    var plan = PlanOf(circle);
                    var payload = F<HashSet<Kingmaker.ElementsSystem.GameAction>>(plan, "Payload");
                    Check(F<string>(plan, "Failure") == null && F<bool>(plan, "Spread") &&
                          payload.Count == 2 && payload.Any(a => a is ContextActionKill) && payload.Any(a => a is ContextActionDealDamage),
                        $"actual Circle of Death: a spreading pool whose failed save kills at 9 Hit Dice or less, else deals 100 (read: {F<string>(plan, "Failure") ?? "supported"})");
                    var calc = F<ContextCalculateSharedValue>(plan, "Calculator");
                    Check(calc != null && calc.Value.DiceType == Kingmaker.RuleSystem.DiceType.D4 && calc.Value.DiceCountValue.ValueType == ContextValueType.Rank,
                        "actual Circle of Death: its pool is d4 per caster level, rolled each cast");
                    var scan = math.GetMethod("DeathScanOf", Hidden, null, new[] { typeof(BlueprintAbility) }, null).Invoke(null, new object[] { circle });
                    var kills = F<System.Collections.IList>(scan, "Kills"); var killActions = F<System.Collections.IList>(scan, "KillActions");
                    Check(kills.Count == 1 && killActions.Count == 1 && killActions[0] is ContextActionKill,
                        "the permanent-death scan keeps Circle of Death's kill action, so its own 9 Hit Dice limit is read per ally");
                }
                var sleep = Spell("Sleep");
                if (sleep != null)
                {
                    var plan = PlanOf(sleep);
                    Check(F<string>(plan, "Failure") == null && !F<bool>(plan, "Spread") &&
                          F<HashSet<Kingmaker.ElementsSystem.GameAction>>(plan, "Payload").Count == 0 && F<HashSet<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff>>(plan, "Buffs").Count == 1,
                        "actual Sleep: still one dependent control buff, at once, a fixed pool (unchanged)");
                }

                // --- fused admission ---
                var holder = all.FirstOrDefault(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                var withheld = engine.GetMethod("FusionWithheld", Hidden); var detail = engine.GetMethod("FusionWithheldDetail", Hidden);
                var model = engine.GetMethod("FusionOutcomeModel", Hidden);
                AbilityData Fuse(string a, string b, string delivery, SavingThrowType save)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new MagicHackData { Spell1 = Spell(a), Spell2 = Spell(b), DeliverBlueprint = Spell(delivery),
                        SavingThrowType = save, SpellSchool = SpellSchool.Necromancy, SpellLevel = 6 };
                    return data;
                }
                string Why(AbilityData d) => (string)withheld.Invoke(null, new object[] { d, AbilityProfile.Get(d) });
                string Detail(AbilityData d) => (string)detail.Invoke(null, new object[] { d, AbilityProfile.Get(d) });
                bool Joint(AbilityData d) => (bool)model.Invoke(null, new object[] { d, AbilityProfile.Get(d) });
                if (holder != null && circle != null)
                {
                    var single = Fuse("HoldPerson", "CircleOfDeath", "HoldPerson", SavingThrowType.Will);
                    Check(Why(single) == null && Joint(single),
                        $"Hold Person with Circle of Death: offered with the joint value, the kill priced on the failed save (withheld: {Why(single) ?? "no"}, {Detail(single) ?? "-"})");
                    var sp = AbilityProfile.Get(single);
                    var kill = sp.Damage.OfType<ContextActionKill>().FirstOrDefault();
                    Check(kill != null && (bool)control.GetMethod("FusedPools", Hidden).Invoke(null, new object[] { single, sp, kill }),
                        "the fused kill is the pool's own (only a pooled kill joins the joint value)");
                    var spread = Fuse("PhantasmalWeb", "CircleOfDeath", "CircleOfDeath", SavingThrowType.Will);
                    Check(Why(spread) == null && Joint(spread),
                        $"Phantasmal Web with Circle of Death, delivered by its spreading area: read in distance order and offered ({Detail(spread) ?? "offered"})");
                    var projectile = Fuse("Fireball", "CircleOfDeath", "Fireball", SavingThrowType.Reflex);
                    Check(Detail(projectile) == "custom or repeated delivery is outside this forecast",
                        $"a pool behind a projectile delivery keeps the earlier refusal ({Detail(projectile) ?? "offered"})");
                    var cone = Fuse("Sleep", "BurningHands", "BurningHands", SavingThrowType.Reflex);
                    Check(Spell("BurningHands") == null || Detail(cone) == "cone, line, chain or repeated delivery is outside this forecast",
                        $"a pool in a cone stays withheld: its order is frame by frame ({Detail(cone) ?? "offered"})");
                }

                // --- the formulas ---
                SharedHdBudget.Recipient T(int cost) => new SharedHdBudget.Recipient(1f, r => new SharedHdBudget.Visit(r >= cost, r >= cost, cost));
                var rs = new[] { T(4), T(6), T(3) };
                bool ok = SharedHdBudget.TryForecast(new[] { 5, 10, 13 }, new[] { 0.25f, 0.5f, 0.25f }, rs, out var once);
                float[] Sum()
                {
                    var s = new float[3];
                    foreach (var kv in new[] { (5, 0.25f), (10, 0.5f), (13, 0.25f) })
                    { SharedHdBudget.TryForecast(kv.Item1, rs, out var e); for (int i = 0; i < 3; i++) s[i] += kv.Item2 * e[i]; }
                    return s;
                }
                var summed = Sum();
                Check(ok && Enumerable.Range(0, 3).All(i => Math.Abs(once[i] - summed[i]) < 1e-5f) && SharedHdBudget.MaxBudget >= 150,
                    "a rolled pool in one pass equals the weighted forecasts of its rolls; Circle of Death's largest pool fits the bound");
                // The kill overload, with the review's separate HP-damage parameters (review 92-99) left unset.
                var withKill = typeof(ValueModel).GetMethods(Hidden).Single(m => m.Name == "FusionValue" && m.GetParameters().Length == 19);
                var args = new object[] { 0.5f, 1f, 0f, 0f, 0f, 0f, 30f, 20f, 10f, 2f, 0f, false, false, true, false, 0f, 0f, null, null };
                withKill.Invoke(null, args);
                float killed = (float)args[15], killedControl = (float)args[16];
                Check(Math.Abs(killed - 0.5f * (30f + 20f)) < 1e-3f && killedControl == 0f,
                    "a kill on the failed save is the HP left and the whole worth, and no control after it");

                // --- compiled routes ---
                var score = Body(engine, "ScoreAbility");
                Check(Calls(score, typeof(AbilityProfile), "PoolOutcome") && CallsNamed(score, "DamageEligibility") && CallsNamed(score, "PooledAllyDamage"),
                    "ordinary scoring: each enemy's pooled payload read as covered and weighed by the forecast's chance; allies the same");
                var pooled = Body(engine, "TryPooledFusionValues");
                Check(Calls(Body(engine, "TryFusionValues"), engine, "TryPooledFusionValues") && Calls(pooled, math, "EstimateFusionDamage") &&
                      Calls(pooled, typeof(AbilityProfile), "PoolOutcome") && Calls(pooled, engine, "TryFusionValuesOf"),
                    "fused casts: the joint value with the pooled payload and without it, mixed by the forecast's chance");
                Check(CallsNamed(Body(engine, "PermanentDeathVetoes"), "AllyReach") && Calls(Body(math, "OutcomeDeath"), math, "KillRuledOut"),
                    "the permanent-death rule needs the pool to reach the ally, and a kill its own conditions allow");
                // Merged with review 92-99: its unread nested-save bound stays; a kill is ruled out only by its own Hit Dice test.
                var risk = Body(math, "PermanentDeathRisk");
                var ruled = Body(typeof(AbilityProfile), "RuledOutByHitDice");
                Check(Calls(risk, math, "AllKillsRuledOut") && Calls(risk, math, "DeliveryChance") &&
                      Calls(ruled, typeof(AbilityProfile), "SingleChecker") && Calls(ruled, typeof(AbilityProfile), "PoolOutcome") &&
                      !CallsNamed(Body(math, "KillRuledOut"), "AppliesTo") && !CallsNamed(Body(math, "KillRuledOut"), "BuffAppliesTo"),
                    "a kill counts as impossible only by its own Hit Dice test (character level is fixed for the cast), never by a condition the cast can change");
                if (circle != null)
                {
                    var scan = math.GetMethod("DeathScanOf", Hidden, null, new[] { typeof(BlueprintAbility) }, null).Invoke(null, new object[] { circle });
                    Check((bool)scan.GetType().GetField("UnreadSavePaths", Hidden).GetValue(scan),
                        "Circle of Death's Fortitude save is nested: the review's unread-save bound prices an ally its Hit Dice test does not rule out");
                }
                var veto = Body(engine, "PermanentDeathVetoes");
                Check(CallsNamed(veto, "PoolOutcome") && CallsNamed(veto, "PermanentDeathRisk") && CallsNamed(veto, "AllyReach"),
                    "a pooled cast's death risk is mixed with and without the pool (a fused cast's other spell can kill either way)");
                Check(Calls(Body(control, "TryForAim"), control, "RecipientOf") && Calls(Body(control, "TryForFusedAim"), control, "RecipientOf") &&
                      Calls(Body(control, "TryForAim"), control, "SpreadOrder") && Calls(Body(control, "TryForAim"), control, "TryOrdinaryRoll"),
                    "both forecasts read each recipient once per pool interval, in spread order, over the pool's rolled distribution");
                Check(Calls(Body(typeof(AbilityProfile), "CheckFor"), typeof(AbilityProfile), "CheckUncached"),
                    "a pool outcome in force is never a cached condition answer");

                // --- census: ordinary pools ---
                var supported = new List<string>(); var refused = new Dictionary<string, int>();
                foreach (var b in all)
                {
                    AbilityProfile p;
                    try { p = AbilityProfile.Get(CasterPolicyCheck.Data(b)); } catch { continue; }
                    if (p == null || !(bool)typeof(AbilityProfile).GetProperty("UsesSharedHdBudget", Hidden).GetValue(p)) continue;
                    string failure;
                    try { failure = F<string>(build.Invoke(null, new object[] { b, p }), "Failure"); } catch (Exception ex) { failure = ex.GetType().Name; }
                    if (failure == null) supported.Add(b.name);
                    else refused[failure] = refused.TryGetValue(failure, out int n) ? n + 1 : 1;
                }
                Check(supported.Contains("CircleOfDeath") && supported.Contains("Sleep"), "the census finds Circle of Death and Sleep supported");
                info = $"; ordinary pools read: {supported.Count} ({string.Join(", ", supported.Take(12))}{(supported.Count > 12 ? ", ..." : "")}); " +
                       $"refused: {string.Join("; ", refused.OrderByDescending(kv => kv.Value).Select(kv => kv.Value + " " + kv.Key))}";
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"HD-POOL {checks} checks, {bad} failures{info}");
            return lines;
        }
    }
}
