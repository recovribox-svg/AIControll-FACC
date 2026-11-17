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
                var others = Body(math, "OtherEngagers");
                Check(Calls(others, typeof(UnitCombatState), "get_EngagedBy") && others.Any(m => m.Name == "AnyCommandTargets") &&
                      Calls(others, typeof(TurnBased.Controllers.CombatController), "IsInTurnBasedCombat"),
                      "other engagers are counted by the native mode rule");
                var engages = Body(math, "AttackerEngages");
                Check(Calls(engages, typeof(UnitHelper), "GetThreatHand") && Calls(engages, typeof(UnitHelper), "GetThreatRange"),
                      "the attacker engages through the native threat hand");
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
                      "the partner's gain: +2 on melee attacks and its sneak dice, never against precision immunity");
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
