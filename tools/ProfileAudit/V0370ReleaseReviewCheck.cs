using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;

namespace ProfileAudit
{
    /// <summary>
    /// ★ v0.3.70 release review: a hold is weighed against the holder's lost turn only when letting go gives it an action back
    /// this round; a melee attacker gains +4 more against a helpless ally (the game's RuleCalculateAC).
    /// </summary>
    internal static class V0370ReleaseReviewCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int failed = 0;
            void Check(bool valid, string detail)
            {
                if (!valid) failed++;
                lines.Add("V0370-RELEASE-REVIEW " + (valid ? "passed: " : "FAILED: ") + detail);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            IEnumerable<MemberInfo> Members(Type owner, string method) => owner == null ? Enumerable.Empty<MemberInfo>()
                : owner.GetMethods(all).Where(m => m.Name == method).SelectMany(m => SaveBoostCheck.ReferencedMembers(m));
            bool Calls(Type owner, string method, string name) => Members(owner, method).Any(member => member.Name == name);
            var engine = typeof(TacticalEngine);

            // --- the hold right after the grab ---
            var keepCalls = Members(engine, "GrappleReleaseValue").OfType<MethodInfo>()
                .Where(m => m.DeclaringType == typeof(ValueModel) && m.Name == "GrappleKeepValue").ToList();
            Check(keepCalls.Any(m => m.GetParameters().Length == 8) && Calls(engine, "GrappleReleaseValue", "HasStandard") &&
                  Calls(engine, "GrappleReleaseValue", "HasMove"),
                  "Release Grapple weighs the holder's lost turn only when its standard or move action is still to use this round");
            var has = typeof(Kingmaker.EntitySystem.Entities.UnitEntityData).GetMethod("HasStandardAction", all, null, Type.EmptyTypes, null);
            var hasMembers = has == null ? new List<MemberInfo>() : SaveBoostCheck.ReferencedMembers(has).ToList();
            Check(has != null && hasMembers.Any(m => m.Name == "UsedStandardAction") &&
                  !hasMembers.Any(m => m.Name == "HasCondition"),
                  "native UnitEntityData.HasStandardAction reads what is used, not the grapple's CantUseStandardActions");
            Check(ValueModel.GrappleKeepValue(0.46f, 3f, 29.9f, 28f, 14.3f, 0.5f, 1f, true) < -0.5f &&
                  ValueModel.GrappleKeepValue(0.46f, 3f, 29.9f, 28f, 14.3f, 0.5f, 1f, false) > 0f,
                  "Wenduag's Corrupted Jungle Bulette: let go at once with her turn charged, kept while her full attack's actions are spent");

            // --- a helpless ally in melee ---
            var ac = typeof(Kingmaker.RuleSystem.Rules.RuleCalculateAC).GetMethod("OnTrigger", all);
            var acMembers = ac == null ? new List<MemberInfo>() : SaveBoostCheck.ReferencedMembers(ac).ToList();
            Check(acMembers.Any(m => m.Name == "get_IsHelpless") && acMembers.Any(m => m.Name == "IsMelee"),
                  "native RuleCalculateAC.OnTrigger lowers AC against melee for a helpless target");
            Check(Calls(engine, "DropTableOf", "MeleeDefenceLoss") && Calls(engine, "DropTableOf", "get_IsHelpless") &&
                  ValueModel.MeleeDefenceLoss(true, false, true) == 4 && ValueModel.MeleeDefenceLoss(true, true, true) == 8 &&
                  ValueModel.MeleeDefenceLoss(false, true, true) == 0,
                  "the drop model's hit weights give melee +4 against a prone ally and +4 more against a helpless one");

            failures = failed;
            lines.Add($"V0370-RELEASE-REVIEW {lines.Count} checks, {failed} failures");
            return lines;
        }
    }
}
