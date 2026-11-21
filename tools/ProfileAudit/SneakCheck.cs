// ★ sneak attack refinements (docs/BURST_AIM_AND_TACTICS_PLAN.md §6, slice D): fortification against sneak attack and
// criticals, and the flanking a second engager creates. Native contracts on the real assembly, the fortification facts in
// the exports, and the compiled routes. Actor-free: no Game/Player, unit, rule, roll or Unity object.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Controllers.Combat;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Parts;

namespace ProfileAudit
{
    internal static class SneakCheck
    {
        private static List<MemberInfo> Body(Type type, string name) =>
            SaveBoostCheck.ReferencedMembers(BuffSuppressCheck.Method(type, name)).ToList();

        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("SNEAK FAILED: " + why); } }
            try
            {
                // --- native contracts ---
                var roll = Body(typeof(RuleAttackRoll), "OnTrigger");
                Check(Calls(roll, typeof(UnitPartFortification), "get_Value") &&
                      Calls(roll, typeof(RuleAttackRoll), "set_FortificationNegatesSneakAttack") &&
                      Calls(roll, typeof(RuleAttackRoll), "set_FortificationNegatesCriticalHit") &&
                      Calls(roll, typeof(RuleAttackRoll), "get_PreciseStrike"),
                      "one fortification roll negates a sneak attack, a confirmed critical and a precise strike together");
                Check(roll.Count(m => m.DeclaringType == typeof(UnitCombatState) && m.Name == "get_IsFlanked") >= 2,
                      "a sneak attack needs the target flat-footed or natively flanked when the roll is made");
                Check(Calls(Body(typeof(AddFortification), "OnTurnOn"), typeof(UnitPartFortification), "Add"),
                      "fortification facts add their value to the part; the part reads the highest");
                var flanked = BuffSuppressCheck.BodyWithLambdas(typeof(UnitCombatState), "get_IsFlanked");
                Check(Calls(flanked, typeof(UnitCombatState), "get_EngagedBy") && flanked.Any(m => m.Name == "CannotBeFlanked") &&
                      Calls(flanked, typeof(TurnBased.Controllers.CombatController), "IsInTurnBasedCombat") &&
                      flanked.Any(m => m.Name == "AnyCommandTargets"),
                      "flanking counts engagers (real time: those whose command targets the unit), never with CannotBeFlanked");
                var bonus = Body(typeof(RuleCalculateAttackBonus), "OnTrigger");
                Check(Calls(bonus, typeof(RuleCalculateAttackBonus), "get_TargetIsFlanked") &&
                      Calls(bonus, typeof(UnitCombatState), "get_IsFlanked"),
                      "the +2 flanking bonus follows the native state or the rule's TargetIsFlanked");
                var engage = Body(typeof(UnitCombatEngagementController), "TickUnit");
                Check(Calls(engage, typeof(UnitHelper), "GetThreatHand") && Calls(engage, typeof(UnitHelper), "GetThreatRange") &&
                      Calls(engage, typeof(UnitCombatState), "Engage"),
                      "a unit engages the enemies in its threat hand's reach (no positions beyond that)");
                // ★ review follow-up F1: the whole native engagement test, which the prediction now follows.
                Check(Calls(engage, typeof(UnitEngagementExtension), "IsEngage"),
                      "the engagement controller decides each pair through UnitEngagementExtension.IsEngage");
                var isEngage = Body(typeof(UnitEngagementExtension), "IsEngage");
                Check(Calls(isEngage, typeof(UnitHelper), "IsAttackOfOpportunityReach") &&
                      Calls(isEngage, typeof(Kingmaker.UnitLogic.Parts.UnitPartConcealment), "Calculate"),
                      "engagement needs threat reach (with sight) and no total concealment");
                var nativeEngages = Body(typeof(UnitCombatState), "IsEngage");
                Check(nativeEngages.Any(m => m.Name == "m_EngagedUnits") && !nativeEngages.Any(m => m.Name == "m_EngagedBy"),
                      "IsEngage(target) means this unit engages target, not that target engages this unit");
                var aooReach = Body(typeof(UnitHelper), "IsAttackOfOpportunityReach");
                Check(aooReach.Any(m => m.Name == "get_Corpulence") && aooReach.Any(m => m.Name == "HasLOS"),
                      "threat reach is both bodies plus the threat range, with line of sight");
                var approach = SaveBoostCheck.ReferencedMembers(BuffSuppressCheck.Method(typeof(Kingmaker.UnitLogic.Commands.UnitAttack),
                    "GetApproachRadius", typeof(Kingmaker.Items.ItemEntityWeapon), typeof(Kingmaker.EntitySystem.Entities.UnitEntityData),
                    typeof(Kingmaker.EntitySystem.Entities.UnitEntityData))).ToList();
                Check(Calls(approach, typeof(Kingmaker.Items.ItemEntityWeapon), "get_AttackRange") && approach.Any(m => m.Name == "get_Corpulence"),
                      "the native approach radius sums both view bodies and weapon range; this alone does not prove 3D threat reach");
                var low = new UnityEngine.Vector3(0f, 0f, 0f);
                var high = new UnityEngine.Vector3(0f, 4f, 0f);
                Check(Owlcat.Runtime.Core.Utils.GeometryUtils.SqrMechanicsDistance(low, high) <= 2.5f * 2.5f &&
                      (high - low).sqrMagnitude > 2.5f * 2.5f,
                      "native scalar witness: attack-readiness mechanics distance can pass where equal 3D threat reach fails");
                var conceal = BuffSuppressCheck.BodyWithLambdas(typeof(Kingmaker.UnitLogic.Parts.UnitPartConcealment), "Calculate");
                Check(conceal.Any(m => m.Name == "DistanceGreater") && conceal.Any(m => m.Name == "m_BlindsightRanges") &&
                      conceal.Any(m => m.Name == "IsConcealedFor") && conceal.Any(m => m.Name == "DistanceTo"),
                      "native concealment reads DistanceGreater, blindsight and targeted sources; stable future facts are an assumption");

                // --- the exports ---
                var facts = loaded.OfType<BlueprintScriptableObject>()
                    .Select(b => new { Blueprint = b, Parts = b.GetComponents<AddFortification>().ToList() })
                    .Where(x => x.Parts.Count > 0).ToList();
                var fixedValues = facts.SelectMany(x => x.Parts).Where(p => !p.UseContextValue).Select(p => p.Bonus).ToList();
                lines.Add($"SNEAK exports: {facts.Count} fortification facts ({fixedValues.Count} fixed: " +
                          string.Join(", ", fixedValues.GroupBy(v => v).OrderBy(g => g.Key).Select(g => $"{g.Key}% x{g.Count()}")) +
                          $"; {facts.SelectMany(x => x.Parts).Count(p => p.UseContextValue)} by context value)");
                int Named(string n) => facts.Where(x => x.Blueprint.name == n).SelectMany(x => x.Parts).Select(p => p.Bonus).FirstOrDefault();
                Check(Named("Fortification25Feature") == 25 && Named("Fortification50Feature") == 50 && Named("Fortification75Feature") == 75,
                      "the armour fortification features add 25, 50 and 75");
                Check(SneakRules.FortificationKeep(Named("Fortification75Feature")) == 0.25f,
                      "heavy fortification lets a quarter of the sneak and critical extra through");
                int precision = loaded.OfType<BlueprintScriptableObject>().Count(b =>
                    b.ComponentsArray != null && b.ComponentsArray.Any(c => c is AddImmunityToPrecisionDamage || c is GhostCriticalAndPrecisionImmunity));
                lines.Add($"SNEAK exports: {precision} facts grant precision immunity (no sneak dice, no flank credit for them)");
                Check(precision > 0, "the precision-immunity facts the reader looks for are in the exports");

                // --- the rule ---
                Check(SneakRules.PredictsFlank(false, false, true, 1) && !SneakRules.PredictsFlank(false, false, true, 0) &&
                      !SneakRules.PredictsFlank(false, true, true, 2) && !SneakRules.PredictsFlank(false, false, false, 1),
                      "pure rule: a melee attacker is the second engager with one other; never against CannotBeFlanked");
                Check(Math.Abs(SneakRules.LineDamage(0.5f, 10f, 7f, 2f, 0.25f) - (5f + 0.25f * 5.5f)) < 1e-4,
                      "pure rule: fortification takes the sneak dice and the critical extra, not the hit");

                // --- compiled routes ---
                var math = typeof(CombatMath);
                var read = Body(math, "ReadRoutineCore");
                Check(Calls(read, math, "PredictFlank") && Calls(read, math, "FortificationChance") &&
                      Calls(read, typeof(SneakRules), "FortificationKeep"),
                      "every routine is priced with the flank it will have and the target's fortification");
                var predict = Body(math, "PredictFlank");
                Check(Calls(predict, typeof(UnitCombatState), "get_IsFlanked") && Calls(predict, math, "AttackerEngages") &&
                      Calls(predict, math, "OtherEngagers") && Calls(predict, typeof(SneakRules), "PredictsFlank"),
                      "the prediction starts from the native state and adds this attacker as an engager");
                // ★ coverage3 review: an ally ordered to attack the target in melee, reaching it before this attack lands.
                Check(Calls(predict, math, "ArrivingEngagers"), "the prediction counts an ally arriving before this attack lands");
                var arriving = Body(math, "ArrivingEngagers");
                Check(new[] { "get_PartyAndPets", "get_Standard", "get_TargetUnit", "get_ApproachRadius", "get_EngagedBy", "GetThreatHand",
                              "GetApproachRadius" }.All(n => arriving.Any(m => m.Name == n)) &&
                      arriving.Any(m => m.DeclaringType == typeof(CombatMemory) && m.Name == "get_IsRealTime") &&
                      !arriving.Any(m => m.Name == "Run" || m.Name == "Interrupt" || m.Name == "FindPath" || m.Name == "StartPath"),
                      "arrivals are read from the allies' own attack commands, in real time, with no path query or command");
                var others = Body(math, "OtherEngagers");
                Check(Calls(others, typeof(UnitCombatState), "get_EngagedBy") && others.Any(m => m.Name == "AnyCommandTargets") &&
                      Calls(others, typeof(TurnBased.Controllers.CombatController), "IsInTurnBasedCombat"),
                      "other engagers are counted by the native mode rule");
                var engages = Body(math, "AttackerEngages");
                Check(Calls(engages, typeof(UnitHelper), "GetThreatHand") && Calls(engages, typeof(UnitHelper), "GetThreatRange"),
                      "the attacker engages through the native threat hand");
                Check(Calls(engages, typeof(UnitCombatState), "IsEngage") &&
                      Calls(engages, typeof(Kingmaker.UnitLogic.Parts.UnitPartConcealment), "Calculate") &&
                      Calls(engages, typeof(Kingmaker.Items.ItemEntityWeapon), "get_AttackRange") &&
                      Calls(engages, typeof(SneakRules), "WillEngage"),
                      "F1: the game's engagement when it stands; else threat reach covering the weapon and no total concealment");
                Check(SneakRules.WillEngage(true, false, 0f, 3f, true) && SneakRules.WillEngage(false, true, 1.5f, 1.5f, false) &&
                      !SneakRules.WillEngage(false, true, 1.5f, 3f, false) && !SneakRules.WillEngage(false, true, 1.5f, 1.5f, true) &&
                      !SneakRules.WillEngage(false, true, 1.495f, 1.5f, false),
                      "range projection: native engagement wins; shorter reach or total concealment gives none, without an invented tolerance");
                Check(Calls(Body(math, "ExpectedWeaponLine"), typeof(SneakRules), "LineDamage"),
                      "each attack line's sneak and critical extra pass the fortification factor");
                Check(Calls(Body(math, "AbilityCritFactor"), math, "FortifiedCritFactor"),
                      "attack-roll abilities' criticals pass it too");
                Check(Calls(Body(typeof(TacticalEngine), "ChooseBlastModes"), math, "FortificationChance"),
                      "and an archer blast's criticals while its attack modes are chosen");
                var engine = typeof(TacticalEngine);
                Check(Calls(Body(engine, "AddWeaponCandidates"), engine, "FlankPartnerBonus") &&
                      Calls(Body(engine, "FlankPartnerBonus"), math, "FlankPartnerGain"),
                      "a weapon attack that creates the flank counts the other engager's round of gain");
                Check(Calls(Body(math, "FlankPartnerGain"), typeof(SneakRules), "PartnerLineGain") &&
                      Calls(Body(math, "FlankPartnerGain"), math, "ImmuneToPrecision"),
                      "the partner's gain includes newly enabled sneak dice, never against precision immunity");
                var partner = Body(math, "FlankPartnerGain");
                Check(Calls(partner, typeof(RuleCalculateAttackBonus), "set_TargetIsFlanked") &&
                      partner.Any(m => m.DeclaringType == typeof(RuleCalculateAttackBonus) && m.Name == ".ctor") &&
                      partner.Count(m => m.DeclaringType == math && m.Name == "ExpectedWeaponLine") == 2,
                      "partner counterfactuals ask the native attack rule and compare the shared line kernel, including criticals and existing sneak");
                Check(partner.Count(m => m.Name == "PartnerFlankGain") >= 2,
                      "the counterfactual partner gain is cached on its existing per-frame routine");

                // Run the production line kernel with cached native-layout scalars. The attack-bonus delta is a fixture,
                // not a replacement for the native rule above; no unit, engagement or attack roll is constructed.
                const BindingFlags hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var lineType = math.GetNestedType("AttackLine", hidden);
                var line = Activator.CreateInstance(lineType, true);
                void Set(string name, object value) => lineType.GetField(name, hidden).SetValue(line, value);
                Set("AttackBonus", 10); Set("ArmorClass", 21); Set("RawPerHit", 12f); Set("GameHit", 12f);
                Set("ThreatChance", 0.1f); Set("CritMultiplier", 2);
                var kernel = BuffSuppressCheck.Method(math, "ExpectedWeaponLine");
                float Gain(float keep, float existingSneak, float newSneak)
                {
                    Set("Keep", keep); Set("Sneak", existingSneak);
                    float before = (float)kernel.Invoke(null, new object[] { line, 0, 0f, 1f, 0f, 0f });
                    object[] args = { line, 2, 0f, 1f, 0f, 0f };
                    float after = (float)kernel.Invoke(null, args);
                    return SneakRules.PartnerLineGain(before, after, (float)args[4], newSneak, keep);
                }
                Check(Math.Abs(Gain(0.25f, 10.5f, 0f) - 1.4925f) < 1e-4,
                      "production partner lines count extra hits carrying existing sneak and criticals, with heavy fortification once");
                Check(Math.Abs(Gain(0f, 10.5f, 0f) - 1.2f) < 1e-4,
                      "production partner lines preserve ordinary extra-hit damage under complete fortification");
                Check(Math.Abs(Gain(0.25f, 0f, 10.5f) - 2.805f) < 1e-4,
                      "production partner lines add newly enabled sneak once, after the critical/hit comparison");
            }
            catch (Exception ex)
            {
                Check(false, $"{ex.GetType().Name}: {ex.Message}");
            }
            lines.Insert(0, $"SNEAK {checks} checks, failures {bad}; native contracts, fortification facts and compiled routes; " +
                            "no actor, rule, roll or Unity object");
            failures = bad;
            return lines;
        }
    }
}
