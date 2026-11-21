// ★ Fusion coverage C6 (docs/FUSION_COVERAGE_C6_C8.md): the carrier buffs a spell puts on its target. Native hooks, the actual
// eleven buffs and their kinds, the harmful rule, fused admission, the two formulas and the compiled routes.
// Actor-free: no Game/Player, unit, context, Rulebook, cast or random roll.
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
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class CarrierCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) => body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>(); string info = "";
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("CARRIERS FAILED: " + why); } }
            try
            {
                var math = typeof(CombatMath); var engine = typeof(TacticalEngine);
                // --- native hooks ---
                var facts = typeof(Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions);
                Check(Body(facts, "OnActivate").Any(m => m.Name == "Activated") && Body(facts, "OnDeactivate").Any(m => m.Name == "Deactivated") &&
                      facts.GetMethods(Hidden).Any(m => m.GetMethodBody() != null && SaveBoostCheck.ReferencedMembers(m).Any(x => x.Name == "NewRound")),
                    "native: a fact's actions run when it lands, when it ends and each new round");
                var attack = typeof(Kingmaker.UnitLogic.Mechanics.Components.AddInitiatorAttackRollTrigger);
                var didTrigger = Body(attack, "OnEventDidTrigger");
                Check(didTrigger.Any(m => m.Name == "OnOwner") && didTrigger.Any(m => m.Name == "Action"),
                    "native: an attack-roll trigger runs its actions, on its owner with OnOwner");

                // --- the actual carriers ---
                var buffs = loaded.OfType<BlueprintBuff>().ToList();
                var planOf = math.GetMethod("CarrierPlanOf", Hidden);
                string KindOf(string name)
                {
                    var buff = buffs.FirstOrDefault(b => b.name == name);
                    if (buff == null) return "missing";
                    var plan = planOf.Invoke(null, new object[] { buff });
                    return plan.GetType().GetField("Kind", Hidden).GetValue(plan).ToString();
                }
                object Plan(string name) => planOf.Invoke(null, new object[] { buffs.First(b => b.name == name) });
                object Field(object o, string f) => o.GetType().GetField(f, Hidden).GetValue(o);
                var expected = new Dictionary<string, string>
                {
                    ["ReducePersonBuff"] = "Mixed", ["EnervationBuff"] = "Benefit", ["AcidicSprayBuff"] = "Damage",
                    ["PhantasmalPutrefactionBuff"] = "PeriodicControl", ["SerenityBuff"] = "Damage", ["TouchOfSlimeBuff"] = "Damage",
                    ["ExcruciatingDeformationBuff"] = "Damage", ["PeerThroughAvoidanceBuff"] = "Hinders",
                    ["PeerThroughSpellResistanceBuff"] = "Hinders", ["PeerThroughResistanceBuff"] = "Hinders",
                    ["CurseOfMagicNegationBuff"] = "None",
                };
                foreach (var kv in expected)
                    Check(KindOf(kv.Key) == kv.Value, $"actual {kv.Key}: {kv.Value} (read {KindOf(kv.Key)})");
                Check(Field(Plan("AcidicSprayBuff"), "Deactivated") != null && Field(Plan("SerenityBuff"), "OnAttack") != null &&
                      Field(Plan("TouchOfSlimeBuff"), "Round") != null && Field(Plan("ExcruciatingDeformationBuff"), "Activated") != null &&
                      Field(Plan("ExcruciatingDeformationBuff"), "Round") != null && Field(Plan("PhantasmalPutrefactionBuff"), "Control") != null,
                    "their damage is read at its own time: removal (Acidic Spray), attacking (Serenity), each round (Touch of Slime), landing and each round (Excruciating Deformation); Putrefaction's nausea");
                var harmful = typeof(AbilityProfile).GetMethod("IsHarmfulBuff", Hidden, null, new[] { typeof(BlueprintBuff), typeof(bool) }, null);
                bool Harmful(string name, bool carriers) => (bool)harmful.Invoke(null, new object[] { buffs.First(b => b.name == name), carriers });
                Check(Harmful("SerenityBuff", true) && Harmful("PhantasmalPutrefactionBuff", true) && !Harmful("SerenityBuff", false) &&
                      !Harmful("PeerThroughAvoidanceBuff", true) && !Harmful("EnervationBuff", true) && !Harmful("ReducePersonBuff", true),
                    "damage and round-control carriers count as harmful (cleansable, aimed at enemies); hindering, mixed and benefit kinds keep their rule; the on-hit reader keeps the older one");

                // --- fused admission ---
                var abilities = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => abilities.FirstOrDefault(b => b.name == name);
                var holder = abilities.FirstOrDefault(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                var model = engine.GetMethod("FusionOutcomeModel", Hidden); var withheld = engine.GetMethod("FusionWithheld", Hidden);
                bool Joint(string a, string b, SavingThrowType save, out string why)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new MagicHackData { Spell1 = Spell(a), Spell2 = Spell(b), DeliverBlueprint = Spell(a),
                        SavingThrowType = save, SpellSchool = SpellSchool.Evocation, SpellLevel = 5 };
                    var p = AbilityProfile.Get(data);
                    why = (string)withheld.Invoke(null, new object[] { data, p });
                    return why == null && (bool)model.Invoke(null, new object[] { data, p });
                }
                if (holder != null)
                {
                    foreach (var other in new[] { "ReducePerson", "Enervation", "Serenity", "ExcruciatingDeformation", "PhantasmalPutrefaction",
                                                  "PeerThroughAvoidance", "AcidicSpray" })
                        Check(Joint("Fireball", other, SavingThrowType.Reflex, out string why), $"Fireball with {other} is offered with the joint value (withheld: {why ?? "no"})");
                    Check(!Joint("MoltenOrb", "CurseOfMagicNegation", SavingThrowType.Reflex, out _),
                        "Curse of Magic Negation stays off the joint value (its spell resistance also stands against the party's later spells)");
                }

                // --- the two formulas ---
                Check(Math.Abs(ValueModel.TickControlRounds(1f, 1f, 3) - 3f) < 1e-4f && ValueModel.TickControlRounds(0f, 1f, 3) == 0f &&
                      Math.Abs(ValueModel.TickControlRounds(0.5f, 0.5f, 2) - (0.25f + 0.25f * 0.5f * 0.5f + 0.25f * 0.5f)) < 1e-4f,
                    "round-tick control: on after a failed first and second save, gone after a passed first");
                Check(ValueModel.CarrierEndChance(0, 1f, true) == 1f && ValueModel.CarrierEndChance(3, 1f, false) == 0f &&
                      Math.Abs(ValueModel.CarrierEndChance(2, 0.5f, false) - 0.75f) < 1e-4f,
                    "a buff's end comes within the horizon when its duration runs out there, or a tick's save removes it");

                // --- compiled routes ---
                var damage = Body(engine, "CarrierDamage");
                Check(Calls(damage, math, "CarrierListHarm") && Calls(damage, typeof(ValueModel), "CarrierEndChance") &&
                      Calls(damage, typeof(EnemyThreat), "AttackChance") && Calls(Body(engine, "PeriodicCarrierDamage"), engine, "CarrierDamage"),
                    "a damage carrier is priced at each of its times, ability damage by its HP equivalent, Serenity's by the chance the bearer attacks");
                Check(Calls(Body(math, "CarrierListHarm"), math, "DrainEquivalent") && Calls(Body(math, "IsPeriodicDamageCarrier"), math, "CarrierPlanOf"),
                    "ability damage counts by its HP equivalent; damage carriers join the damage-over-time pricing");
                var score = Body(engine, "ScoreAbility");
                Check(Calls(score, engine, "CarrierBenefitExpected") && Calls(score, engine, "TickControl") && Calls(score, engine, "OrdinaryPeriodicLater"),
                    "ordinary spells: temporary HP given is damage not taken, round-tick control by its saves, carriers behind a save by that branch");
                Check(Calls(Body(engine, "TryFusionValues"), engine, "CarrierBenefit") && Calls(Body(engine, "TryFusionControlRounds"), engine, "TickControl"),
                    "fused casts: the same in the joint value");
                var counts = new Dictionary<string, int>();
                foreach (var b in buffs)
                {
                    string k = planOf.Invoke(null, new object[] { b }) is object plan ? plan.GetType().GetField("Kind", Hidden).GetValue(plan).ToString() : "None";
                    if (k == "None") continue;
                    counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1;
                }
                info = "; loaded buffs read as carriers: " + string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value));
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"CARRIERS {checks} checks, {bad} failures{info}");
            return lines;
        }
    }
}
