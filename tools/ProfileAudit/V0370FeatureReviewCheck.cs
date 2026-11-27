using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;

namespace ProfileAudit
{
    /// <summary>
    /// ★ v0.3.70 features review (TB + RT logs): the routes of the fixes - the per-frame damage and effect-chance memo, an
    /// unmeasured caster's spell ban, the group-teleport bound and the far-escape range cap - and of the log trims.
    /// </summary>
    internal static class V0370FeatureReviewCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int failed = 0;
            void Check(bool valid, string detail)
            {
                if (!valid) failed++;
                lines.Add("V0370-FEATURE-REVIEW " + (valid ? "passed: " : "FAILED: ") + detail);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            bool Calls(Type owner, string method, string name) => owner.GetMethods(all).Where(m => m.Name == method)
                .SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).Any(member => member.Name == name);
            var engine = typeof(TacticalEngine);
            var math = typeof(CombatMath);
            var asm = engine.Assembly;

            // --- the memo ---
            Check(Calls(math, "EstimateAbilityDamage", "EstimateMemoUsable") && Calls(math, "EstimateAbilityDamage", "Remember") &&
                  Calls(math, "EffectChance", "EstimateMemoUsable") && Calls(math, "EffectChance", "Remember"),
                  "ability damage and effect chances are remembered for the frame");
            Check(Calls(math, "ClearAttackCache", "ClearEstimateMemo") && Calls(math, "InfusionStateChanged", "ClearAttackCache") &&
                  Calls(math, "EstimateMemoUsable", "get_PoolOutcomeActive") && Calls(math, "EstimateMemoUsable", "get_frameCount"),
                  "the memo is cleared with every state preview and the frame, and stands aside in a pool outcome or a Magus roll");

            // --- an unmeasured caster's spell ban ---
            Check(Calls(engine, "AppraiseParts", "UnmeasuredSpellEdgeOf") && Calls(engine, "UnmeasuredSpellEdgeOf", "UnmeasuredSpellEdge") &&
                  Calls(engine, "AppraiseParts", "TrySpellEdge") && Calls(engine, "AppraiseParts", "Loss"),
                  "a prohibition prices a caster its decisions have not measured yet from its spellbook; measured edges first");
            var unmeasured = typeof(ValueModel).GetMethod("UnmeasuredSpellEdge", all);
            var loss = asm.GetType("CompanionAI_Pathfinder.Tactics.SpellEdgeMemory")?.GetMethod("Loss", all);
            float edge = unmeasured == null ? 0f : (float)unmeasured.Invoke(null, new object[] { 20, 6, false });
            float lost = loss == null ? 0f : (float)loss.Invoke(null, new object[] { edge, 4.4f });
            Check(edge > 0f && lost > 33.7f,
                  $"the turn-based rage case: a 20th-level six-circle Pure Mage loses {edge:0.#}/rd, more than the rage gave the rest (33.7)");

            // --- companions decline a costly rage (author-approved) ---
            Check(!Calls(engine, "SwitchCostlyAccept", "IsOrdinarySummon") && Calls(engine, "SwitchCostlyAccept", "Decline") &&
                  Calls(engine, "Decline", "UnmeasuredSpellEdgeOf") && Calls(engine, "Decline", "PlayerOwnsToggle") &&
                  Calls(engine, "DrivenAllyDeclines", "AiDecidesFor") && Calls(engine, "AiDecidesFor", "ShouldControlUnit") &&
                  Calls(engine, "AiDecidesFor", "ShouldDrive") && Calls(engine, "AiDecidesFor", "get_RealTimeLegacyEngine"),
                  "any unit AI Control decides for declines an ally effect that stops spells worth more (unmeasured: the " +
                  "spellbook estimate); the performer counts it as declined only when this engine makes that unit's decisions");

            // --- teleports ---
            var group = engine.GetField("MaxTeleportGroup", all);
            var crowd = engine.GetField("MaxTeleportCombatants", all);
            int groupBound = group == null ? -1 : (int)group.GetRawConstantValue();
            int crowdBound = crowd == null ? -1 : (int)crowd.GetRawConstantValue();
            Check(groupBound >= crowdBound && crowdBound > 0,
                  $"a group teleport is quoted for a party group up to the crowd bound ({groupBound} / {crowdBound}); travellers stay at most four");
            Check(Calls(engine, "AddFarEscapes", "IsInfinity") && Calls(engine, "AddFarEscapes", "FightReach"),
                  "an Unlimited teleport range (infinite) is cut to the farthest enemy plus the fight reach before the spot search");

            // --- log trims ---
            var coverage = asm.GetType("CompanionAI_Pathfinder.Tactics.CoverageReport");
            var witness = asm.GetType("CompanionAI_Pathfinder.Tactics.UnusedWitness+Group");
            Check(coverage != null && Calls(coverage, "ReportIfChanged", "ReportKey") &&
                  !Calls(engine, "SkipNoFreeSide", "ReportOnce") && Calls(engine, "Decide", "FlushNoFreeSide") &&
                  witness != null && Calls(witness, "Report", "TryGetValue"),
                  "coverage is reported once per summon kind; free-side refusals one row a decision; unused verdicts only when changed");

            // --- issue #114: the settings panel follows UMM's UI scale ---
            var mainUi = asm.GetType("CompanionAI_Pathfinder.UI.MainUI");
            var blocklistUi = asm.GetType("CompanionAI_Pathfinder.UI.AbilityBlocklistUI");
            int fixedSizes = 0;
            foreach (var type in new[] { mainUi, blocklistUi })
                foreach (var m in type?.GetMethods(all).Where(m => m.DeclaringType == type && m.GetMethodBody() != null) ?? Enumerable.Empty<MethodInfo>())
                    fixedSizes += SaveBoostCheck.ReferencedMembers(m).Count(r => r.DeclaringType?.Name == "GUILayout" &&
                        (r.Name == "Width" || r.Name == "Height" || r.Name == "Space"));
            Check(mainUi != null && blocklistUi != null && fixedSizes == 0 && Calls(mainUi, "InitStyles", "Font") &&
                  Calls(mainUi, "InitStyles", "get_Factor"),
                  $"every width, height, space, font and rich-text size of the panel goes through UMM's UI scale ({fixedSizes} fixed left)");

            // --- issue #115: role and range from the build, for a character seen first ---
            var settingsType = asm.GetType("CompanionAI_Pathfinder.Settings.ModSettings");
            var charType = asm.GetType("CompanionAI_Pathfinder.Settings.CharacterSettings");
            var brain = asm.GetType("CompanionAI_Pathfinder.GameInterface.CustomBrainPatch");
            bool pendingDefault = true;
            try { pendingDefault = (bool)charType.GetProperty("SuggestionPending").GetValue(Activator.CreateInstance(charType)); } catch { }
            var shield = CompanionAI_Pathfinder.Settings.SuggestedSettingsRules.Suggest(
                new CompanionAI_Pathfinder.Settings.SuggestedSettingsRules.BuildFacts { Shield = true });
            var cleric = CompanionAI_Pathfinder.Settings.SuggestedSettingsRules.Suggest(
                new CompanionAI_Pathfinder.Settings.SuggestedSettingsRules.BuildFacts { FullCaster = true, Divine = true });
            Check(!pendingDefault && Calls(settingsType, "GetOrCreateSettings", "set_SuggestionPending") &&
                  Calls(brain, "ShouldControlUnit", "EnsureApplied") && Calls(mainUi, "DrawCharacterRow", "EnsureApplied") &&
                  shield.Role == CompanionAI_Pathfinder.Settings.AIRole.Tank && cleric.Role == CompanionAI_Pathfinder.Settings.AIRole.Support &&
                  cleric.Range == CompanionAI_Pathfinder.Settings.RangePreference.Ranged,
                  "a character seen for the first time gets the role and range its build suggests (saved entries untouched); " +
                  $"sword and shield -> {shield.Role}/{shield.Range}, divine full caster -> {cleric.Role}/{cleric.Range}");

            // --- author decision: summon control on by default ---
            bool summonsOn = false;
            try { summonsOn = (bool)settingsType.GetProperty("ControlSummons").GetValue(Activator.CreateInstance(settingsType, true)); } catch { }
            Check(summonsOn && Calls(settingsType, "Load", "set_SummonControlDefaultApplied") && Calls(settingsType, "Load", "set_ControlSummons"),
                  "summon control is on by default; an older settings file is switched on once, and stays as the player sets it afterwards");

            failures = failed;
            lines.Add($"V0370-FEATURE-REVIEW {lines.Count} checks, {failed} failures");
            return lines;
        }
    }
}
