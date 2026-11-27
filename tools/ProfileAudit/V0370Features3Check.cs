using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;

namespace ProfileAudit
{
    /// <summary>
    /// ★ v0.3.70 features3 review (RT logs): the real-time re-decision rules keep the option they drop open, and leave a
    /// pending heal of the unit itself or a walk away running after a loss of HP; log fixes (the weapon-set note, blank names).
    /// </summary>
    internal static class V0370Features3Check
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int failed = 0;
            void Check(bool valid, string detail)
            {
                if (!valid) failed++;
                lines.Add("V0370-FEATURES3 " + (valid ? "passed: " : "FAILED: ") + detail);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            bool Calls(Type owner, string method, string name) => owner != null && owner.GetMethods(all).Where(m => m.Name == method)
                .SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).Any(member => member.Name == name);
            var asm = typeof(TacticalEngine).Assembly;
            var rt = typeof(RealTimeTactics);
            var controller = asm.GetType("CompanionAI_Pathfinder.GameInterface.RealTimeController");

            // --- the re-decision rules ---
            Check(Calls(rt, "ShouldReplanAfterDamage", "Reconsider") && Calls(rt, "ShouldReplanAfterDamage", "DropsAfterDamage") &&
                  Calls(rt, "ShouldReplanAfterDamage", "AnswersHpLoss") && !Calls(rt, "ShouldReplanAfterDamage", "Remove"),
                  "a command dropped after a loss of HP is decided again, not failed (its option stays open); one that answers " +
                  "the loss is left running");
            Check(Calls(rt, "ShouldReplanInArea", "Reconsider"),
                  "a command dropped on entering a harmful area is decided again, not failed");
            Check(controller != null && Calls(controller, "ProcessUnit", "ReconsiderPending") &&
                  Calls(rt, "ReconsiderPending", "Reconsider"),
                  "every command the controller drops to decide again is kept open, also one dropped alongside");
            Check(Calls(rt, "AnswersHpLoss", "KeepsAfterDamage") && Calls(rt, "Act", "AnswersHpLoss"),
                  "a heal of the unit itself or a walk away is marked when it is issued");

            // The fight-8 case: Wenduag, 100 max HP, a potion issued at 34 and dropped at 9; a walk out of a swarm.
            bool potionDropsBefore = RealTimeReplanRules.DropsAfterDamage(34, 9, 100);
            bool keepsPotion = RealTimeReplanRules.KeepsAfterDamage(healsItself: true, walksAway: false);
            bool keepsEscape = RealTimeReplanRules.KeepsAfterDamage(healsItself: false, walksAway: true);
            bool dropsAttack = !RealTimeReplanRules.KeepsAfterDamage(false, false) && RealTimeReplanRules.DropsAfterDamage(97, 34, 100);
            bool keepsHealthy = !RealTimeReplanRules.DropsAfterDamage(100, 80, 100) && !RealTimeReplanRules.DropsAfterDamage(100, 60, 100);
            Check(potionDropsBefore && keepsPotion && keepsEscape && dropsAttack && keepsHealthy,
                  "Wenduag's potion (34 -> 9 of 100 HP) and a walk out of a swarm are kept; an attack chosen at full HP is still " +
                  "re-decided at 97 -> 34; a fifth lost, or above half, changes nothing");

            // --- log fixes ---
            var witness = asm.GetType("CompanionAI_Pathfinder.Tactics.UnusedWitness+Group");
            var coverage = asm.GetType("CompanionAI_Pathfinder.Tactics.CoverageReport");
            Check(Calls(witness, "Report", "LogName") && Calls(coverage, "ReportAbilities", "LogName"),
                  "an ability with an empty display name is logged by its blueprint name ([Unused], [Coverage])");
            Check(Calls(typeof(TacticalEngine), "AddWeaponSetCandidate", "IsOrdinarySummon") &&
                  Calls(typeof(TacticalEngine), "AddWeaponSetCandidate", "get_IsNatural"),
                  "the weapon-set note is once per summon kind and not for creatures with natural weapons");

            failures = failed;
            lines.Add($"V0370-FEATURES3 {lines.Count} checks, {failed} failures");
            return lines;
        }
    }
}
