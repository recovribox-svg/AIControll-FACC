// ★ v0.3.28 test 7: the link QuiverAbility relies on - Create Quiver's profile puts on its caster the very buff the
// kineticist part names as the quiver (BurnFeature's AddKineticistPart.QuiverBuff), so the AI can find the ability that
// makes the arrows. Run on the loaded blueprints, without a unit.
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;

namespace ProfileAudit
{
    internal static class QuiverBindingCheck
    {
        private const string CreateQuiver = "92dbe543f55f4962942dda395176c24c";

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            failures = 0;
            var all = loaded.ToList();
            var ability = all.FirstOrDefault(b => b.AssetGuid.ToString() == CreateQuiver) as BlueprintAbility;
            var part = all.OfType<BlueprintScriptableObject>()
                          .Select(b => b.GetComponent<AddKineticistPart>()).FirstOrDefault(p => p?.QuiverBuff != null);
            if (ability == null || part == null)
            {
                lines.Add("QUIVER-BINDING SKIPPED: Create Quiver or a kineticist part with a quiver buff not loaded");
                return lines;
            }
            var profile = AbilityProfile.Get(ability);
            bool found = profile != null && (profile.CasterBuffs.Contains(part.QuiverBuff) || profile.TargetBuffs.Contains(part.QuiverBuff));
            if (!found)
            {
                failures++;
                lines.Add($"QUIVER-BINDING FAILED: Create Quiver's profile does not put {part.QuiverBuff.name} on anyone " +
                          $"(caster {string.Join(",", profile?.CasterBuffs.Select(b => b.name) ?? new string[0])}; " +
                          $"target {string.Join(",", profile?.TargetBuffs.Select(b => b.name) ?? new string[0])})");
            }
            else lines.Add($"QUIVER-BINDING ok: Create Quiver puts {part.QuiverBuff.name} on its caster");
            return lines;
        }
    }
}
