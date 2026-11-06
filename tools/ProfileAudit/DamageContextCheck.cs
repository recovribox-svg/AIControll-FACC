// ★ v0.3.28 test 11: the shared damage context (CombatMath.DamageContext) on the loaded blueprints: the rank-config field it
// reads is bound, and which damage abilities may share one context across targets (ContextIsTargetFree) - the blasts must,
// an ability whose dice or ranks read the target must not.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class DamageContextCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            failures = 0;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var math = typeof(CombatMath);
            var field = math.GetField("RankBaseType", flags)?.GetValue(null) as FieldInfo;
            var free = math.GetMethod("ContextIsTargetFree", flags);
            if (field == null || free == null) { lines.Add("DAMAGE-CONTEXT FAILED: rank-config field or ContextIsTargetFree not bound"); failures = 1; return lines; }
            int shared = 0, own = 0, blasts = 0, blastsShared = 0, wrong = 0;
            var ownNames = new List<string>();
            foreach (var ability in loaded.OfType<BlueprintAbility>())
            {
                AbilityProfile profile;
                try { profile = AbilityProfile.Get(ability); } catch { continue; }
                if (profile == null || profile.Damage.Count == 0) continue;
                bool isFree = (bool)free.Invoke(null, new object[] { ability, profile });
                // The rule itself, read independently: any target-reading dice or rank config forbids sharing.
                bool readsTarget = profile.Damage.Any(a => !(a is ContextActionDealDamage) && !(a is ContextActionKill)) ||
                    profile.Damage.OfType<ContextActionDealDamage>().Any(d => TargetValue(d.Value?.DiceCountValue) || TargetValue(d.Value?.BonusValue)) ||
                    ability.GetComponents<ContextRankConfig>().Any(c => field.GetValue(c) is ContextRankBaseValueType t && t == ContextRankBaseValueType.TargetBuffRank) ||
                    ability.GetComponents<ContextCalculateSharedValue>().Any(c => TargetValue(c.Value?.DiceCountValue) || TargetValue(c.Value?.BonusValue));
                if (isFree == readsTarget) { wrong++; lines.Add($"DAMAGE-CONTEXT FAILED: {ability.name} shared={isFree} but reads the target={readsTarget}"); }
                if (isFree) shared++; else { own++; if (ownNames.Count < 8) ownNames.Add(ability.name); }
                if (ability.name.IndexOf("Blast", StringComparison.Ordinal) >= 0) { blasts++; if (isFree) blastsShared++; }
            }
            if (blasts > 0 && blastsShared == 0) { wrong++; lines.Add($"DAMAGE-CONTEXT FAILED: none of {blasts} blasts may share a context"); }
            failures = wrong;
            if (failures == 0)
                lines.Add($"DAMAGE-CONTEXT ok: {shared} damage abilities share one context per frame, {own} make their own" +
                          (ownNames.Count > 0 ? $" (e.g. {string.Join(", ", ownNames)})" : "") + $"; blasts {blastsShared}/{blasts} shared");
            return lines;
        }

        private static bool TargetValue(ContextValue v) => v != null &&
            (v.ValueType == ContextValueType.TargetProperty || v.ValueType == ContextValueType.TargetCustomProperty ||
             v.ValueType == ContextValueType.TargetNamedProperty || v.ValueType == ContextValueType.TargetBuffRank);
    }
}
