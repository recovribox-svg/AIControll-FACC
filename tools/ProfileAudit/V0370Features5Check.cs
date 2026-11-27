using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;

namespace ProfileAudit
{
    /// <summary>
    /// ★ v0.3.70 features5 review: the drop model reads each enemy's strike data once and does not read again an ally a
    /// walk cannot concern (same numbers); an ability that walks to its target first counts as being cast on it; a fused
    /// spell's saves are logged and remembered by the fusion.
    /// </summary>
    internal static class V0370Features5Check
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int failed = 0;
            void Check(bool valid, string detail)
            {
                if (!valid) failed++;
                lines.Add("V0370-FEATURES5 " + (valid ? "passed: " : "FAILED: ") + detail);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            bool Calls(Type owner, string method, string name) => owner != null && owner.GetMethods(all).Where(m => m.Name == method)
                .SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).Any(member => member.Name == name);
            bool CallsOverload(Type owner, string method, string name, Type parameter) => owner != null && owner.GetMethods(all)
                .Where(m => m.Name == method).SelectMany(m => SaveBoostCheck.ReferencedMembers(m))
                .OfType<MethodInfo>().Any(m => m.Name == name && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == parameter);
            var engine = typeof(TacticalEngine);
            var memory = typeof(CombatMemory);
            var saves = typeof(SaveObserver);

            // --- the drop model (performance, same numbers) ---
            Check(Calls(engine, "PartyShift", "MoverChangedRows") && Calls(engine, "PartyShift", "ChangeTouches") &&
                  Calls(engine, "MoverChangedRows", "HasSwarm"),
                  "a walk re-reads only the allies an enemy whose choice it changes can pick; every ally with a swarm in the fight");
            Check(Calls(engine, "StandingDropChance", "RowStrikeShare") && Calls(engine, "StandingDropChance", "OwnStrikeShare") &&
                  Calls(engine, "DropTableOf", "ReadStrike") && Calls(engine, "ReadStrike", "GetThreatHandMelee") &&
                  Calls(engine, "RowStrikeShare", "StrikeShare") && Calls(engine, "RowStrikeShare", "HeldThisRound") &&
                  Calls(engine, "RowSingle", "SingleAttackShare"),
                  "each enemy's melee strike data is read once per decision, and the share follows the same rule (ValueModel.StrikeShare)");
            Check(Calls(engine, "StandingDropChance", "StrikeShareAt") && Calls(engine, "StandingDropChance", "FullStrikeLands"),
                  "the rules themselves still decide when the unit's body cannot be read");
            var listType = typeof(List<(float chance, float drop)>);
            Check(CallsOverload(engine, "StandingDropChance", "CombinedDrop", listType) &&
                  ValueModel.CombinedDrop(new List<(float, float)> { (0.5f, 0.4f), (1f, 0.2f) }) ==
                  ValueModel.CombinedDrop(new[] { (0.5f, 0.4f), (1f, 0.2f) }.AsEnumerable()),
                  "the threats are combined from the table's own list, with the same arithmetic");
            Check(ValueModel.TargetChance(false, 0.8f, false, 2f, 0.6f) == 0f && ValueModel.TargetChance(false, 0f, false, 2f, 0.6f) == 0f,
                  "an ally in neither an enemy's reach nor its zone is never its pick, whatever the walker changes (the skip's ground)");

            // --- a walk to cast counts as the cast ---
            Check(Calls(engine, "Execute", "NotePendingWalk") && Calls(engine, "NotePendingWalk", "NotePendingCast") &&
                  Calls(engine, "ScoreAbility", "CastPendingByOther"),
                  "an ability that walks to its target first (around an area, to a free side, to touch range) counts as being cast " +
                  "on it while it walks: another ally's Coup de Grace, debuff or mark on that target is worth 0 meanwhile");
            Check(Calls(memory, "CastPendingByOther", "get_IsFinished"),
                  "the walk's end clears it; the cast is noted again when it is issued");
            var coup = blueprints.OfType<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility>()
                .FirstOrDefault(b => b.AssetGuid.ToString() == "32280b137ca642c45be17e2d92898758");
            var restriction = coup?.ComponentsArray?.OfType<Kingmaker.UnitLogic.Abilities.Components.TargetCheckers.AbilityTargetHasOneOfConditionsOrHP>().FirstOrDefault();
            Check(restriction != null && restriction.Condition.Contains(Kingmaker.UnitLogic.UnitCondition.Sleeping) &&
                  restriction.Condition.Contains(Kingmaker.UnitLogic.UnitCondition.Helpless),
                  "native Coup de Grace: a helpless, sleeping or unconscious target (one ally's delivery is enough)");

            // --- saves against fused spells ---
            var lookup = saves.GetMethod("Lookup", BindingFlags.Public | BindingFlags.Static);
            Check(lookup != null && lookup.GetParameters().Any(p => p.ParameterType == typeof(Kingmaker.UnitLogic.Abilities.AbilityData)) &&
                  Calls(saves, "Lookup", "FusionIdentityOf") && Calls(saves, "OnEventDidTrigger", "FusionIdentityOf") &&
                  Calls(typeof(CombatMath), "SaveFailChance", "Lookup"),
                  "a save seen against a fused spell is remembered for that fusion (all share one native holder ability)");
            Check(Calls(saves, "OnEventDidTrigger", "SaveName") && Calls(saves, "SaveName", "get_Name") && Calls(saves, "SaveName", "LogName"),
                  "the [Save] rows name the fused spell (its holder blueprint has no name), else the blueprint");

            failures = failed;
            lines.Add($"V0370-FEATURES5 {lines.Count} checks, {failed} failures");
            return lines;
        }
    }
}
