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
    /// this round; a badly hurt holder's hold costs the withdrawal letting go would allow; a melee attacker gains +4 more
    /// against a helpless ally (the game's RuleCalculateAC).
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

            // --- the holder's own danger (the G1 limit) ---
            Check(Calls(engine, "GrappleReleaseValue", "BadlyHurt") && Calls(engine, "GrappleReleaseValue", "WalksOnRelease") &&
                  Calls(engine, "GrappleReleaseValue", "WithdrawWorth") && Calls(engine, "GrappleReleaseValue", "GrappleKeepWhileStuck") &&
                  Calls(engine, "AddMovementCandidates", "WithdrawWorth") && Calls(engine, "WithdrawWorth", "FindRetreatPoint") &&
                  Calls(engine, "WithdrawWorth", "MoveRisk") && Calls(engine, "WithdrawWorth", "SpotDrop"),
                  "a badly hurt holder's keep value is charged the withdrawal letting go allows, priced by the withdraw option's own code");
            var init = typeof(Kingmaker.UnitLogic.Parts.UnitPartGrappleInitiator).GetMethod("Init", all);
            var dispose = typeof(Kingmaker.UnitLogic.Parts.UnitPartGrappleInitiator).GetMethod("OnDispose", all);
            var counts = typeof(Kingmaker.UnitLogic.UnitState).GetField("m_Conditions", all);
            var canMove = typeof(Kingmaker.UnitLogic.UnitState).GetProperty("CanMove", all)?.GetGetMethod(true);
            Check(init != null && dispose != null && SaveBoostCheck.ReferencedMembers(init).Any(m => m.Name == "AddCondition") &&
                  SaveBoostCheck.ReferencedMembers(dispose).Any(m => m.Name == "RemoveCondition") &&
                  counts?.FieldType == typeof(sbyte[]) && canMove != null &&
                  SaveBoostCheck.ReferencedMembers(canMove).Any(m => m.Name == "HasCondition"),
                  "native: the hold adds a CantMove its dispose removes, and UnitState.CanMove reads the condition counts");
            var holderBuffs = blueprints.OfType<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff>()
                .Where(b => b.ComponentsArray?.OfType<Kingmaker.UnitLogic.FactLogic.ShifterGrabInitiatorBuff>().Any() == true).ToList();
            Check(holderBuffs.Count > 0 && holderBuffs.All(b => !b.ComponentsArray.OfType<Kingmaker.UnitLogic.FactLogic.AddCondition>()
                      .Any(c => c.Condition == Kingmaker.UnitLogic.UnitCondition.CantMove)),
                  $"the {holderBuffs.Count} grab holder buffs add no CantMove of their own, so letting go frees the holder's walk");
            Check(ValueModel.GrappleKeepWhileStuck(28.5f, 35f) < -0.5f && ValueModel.GrappleReleaseValue(ValueModel.GrappleKeepWhileStuck(28.5f, 35f), out _) > 0f &&
                  ValueModel.GrappleKeepWhileStuck(28.5f, 0f) == 28.5f && ValueModel.GrappleKeepWhileStuck(28.5f, float.NaN) == 28.5f,
                  "Wenduag's Rustclaw hold (keep +28.5): let go when badly hurt with a withdrawal worth 35, unchanged without one");

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
