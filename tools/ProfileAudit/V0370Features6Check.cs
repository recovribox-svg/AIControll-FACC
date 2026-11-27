using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;

namespace ProfileAudit
{
    /// <summary>
    /// ★ v0.3.70 features6 review: the weapon-set switch compares with the current weapon's price whatever this moment's
    /// actions allow; half drained counts as badly hurt (author-approved), by the game's own drain-death rule.
    /// </summary>
    internal static class V0370Features6Check
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int failed = 0;
            void Check(bool valid, string detail)
            {
                if (!valid) failed++;
                lines.Add("V0370-FEATURES6 " + (valid ? "passed: " : "FAILED: ") + detail);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            bool Calls(Type owner, string method, string name) => owner != null && owner.GetMethods(all).Where(m => m.Name == method)
                .SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).Any(member => member.Name == name);
            var engine = typeof(TacticalEngine);
            var math = typeof(CombatMath);

            // --- weapon sets ---
            Check(Calls(engine, "AddWeaponSetCandidate", "CurrentPrice") && Calls(engine, "AddWeaponSetCandidate", "PricedThisRound") &&
                  Calls(engine, "AddWeaponSetCandidate", "NoteCurrentPrice") && Calls(engine, "AddWeaponSetCandidate", "HasStandard"),
                  "the switch compares with the current weapon's options whatever this moment's actions allow, else its price " +
                  "earlier this turn; an attack after the switch needs the standard action");
            bool priced = WeaponSetRules.CurrentPrice(false, 0f, 10.8f, out float price) && price == 10.8f &&
                          !WeaponSetRules.CurrentPrice(false, 0f, null, out _);
            Check(priced && WeaponSetRules.Fresh(3, 3, false) && !WeaponSetRules.Fresh(3, 4, false) && WeaponSetRules.Fresh(3, 4, true),
                  "Camellia after Inspired Rage: the greataxe's 10.8 from earlier this turn, not 0; nothing priced, no switch");

            // --- drain ---
            var life = typeof(Kingmaker.Controllers.Units.UnitLifeController).GetMethod("ForceTickOnUnit", all);
            var lifeMembers = life == null ? new List<MemberInfo>() : SaveBoostCheck.ReferencedMembers(life).ToList();
            Check(lifeMembers.Any(m => m.Name == "get_Count") && lifeMembers.Any(m => m.Name == "get_CharacterLevel"),
                  "native UnitLifeController.ForceTickOnUnit compares the negative levels' count with the character level (death)");
            Check(Calls(engine, "BadlyHurt", "HalfDrained") && Calls(engine, "BadlyHurt", "CautionBelow") &&
                  Calls(engine, "MoveRisk", "BadlyHurt") && Calls(engine, "AddMovementCandidates", "BadlyHurt") &&
                  Calls(engine, "BuildContext", "ReadDrain") && Calls(math, "ReadDrain", "get_Count") &&
                  Calls(math, "ReadDrain", "get_CharacterLevel"),
                  "a withdrawal and a walk's exposure follow 'badly hurt' by HP or by half the levels drained");
            Check(!ValueModel.HalfDrained(4, 1) && ValueModel.HalfDrained(4, 2) && ValueModel.HalfDrained(5, 3) && !ValueModel.HalfDrained(5, 2) &&
                  !ValueModel.HalfDrained(4, 0),
                  "arcan (Arcanist 4) badly hurt from 2 levels drained, Aeiluvetar (level 5) from 3; none drained, never");

            failures = failed;
            lines.Add($"V0370-FEATURES6 {lines.Count} checks, {failed} failures");
            return lines;
        }
    }
}
