// ★ v0.3.22: integration checks against loaded game blueprints and the native cost method, in the offline process only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class KineticistChecks
    {
        internal static void Run(List<SimpleBlueprint> blueprints)
        {
            var blast = blueprints.OfType<BlueprintAbility>().First(b => !b.HasVariants && b.GetComponent<AbilityKineticist>() != null &&
                AbilityProfile.Get(b).DealsDamage);
            var original = AbilityProfile.Get(blast);
            var save = original.Save;
            int damage = original.Damage.Count, buffs = original.TargetBuffs.Count;
            int triggers = 0, maneuvers = 0, saves = 0, wrapperSaves = 0, periodic = 0;
            var effects = new List<AbilityProfile>();
            foreach (var owner in blueprints.OfType<BlueprintBuff>())
                foreach (var trigger in owner.GetComponents<AddKineticistInfusionDamageTrigger>())
                {
                    var effect = AbilityProfile.ForKineticInfusion(blast, trigger);
                    effects.Add(effect);
                    Require(effect.IsKineticEffect, "trigger was not marked as a delivered effect");
                    Require(!effect.NeedsAttackRoll, "infusion added a second attack roll");
                    foreach (var action in trigger.Actions?.Actions?.OfType<ContextActionCombatManeuver>() ?? Enumerable.Empty<ContextActionCombatManeuver>())
                    {
                        Require(effect.Maneuvers.Any(m => m.Action == action && m.Type == action.Type), "infusion maneuver was lost");
                        maneuvers++;
                    }
                    if (trigger.Actions?.Actions?.OfType<ContextActionSavingThrow>().Any() == true)
                    {
                        Require(effect.Save != SavingThrowType.Unknown, "infusion save was lost");
                        saves++;
                    }
                    foreach (var apply in trigger.Actions?.Actions?.OfType<ContextActionApplyBuff>() ?? Enumerable.Empty<ContextActionApplyBuff>())
                        foreach (var onFact in apply.Buff?.GetComponents<Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions>() ??
                                                   Enumerable.Empty<Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions>())
                        {
                            foreach (var nestedSave in onFact.Activated?.Actions?.OfType<ContextActionSavingThrow>() ?? Enumerable.Empty<ContextActionSavingThrow>())
                            {
                                Require(effect.Save == nestedSave.Type, "a carrier buff's activation save was lost");
                                wrapperSaves++;
                            }
                        }
                    var ticks = (System.Collections.ICollection)typeof(AbilityProfile).GetField("PeriodicEffects", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(effect);
                    periodic += ticks.Count;
                    Require(original.Save == save && original.Damage.Count == damage && original.TargetBuffs.Count == buffs,
                        "reading an infusion changed the cached blast profile");
                    triggers++;
                }
            Require(effects.Any(e => (bool)typeof(AbilityProfile).GetProperty("BuffOnBothSaveOutcomes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(e)),
                "no successful-save short-duration infusion was preserved");
            Require(triggers > 0 && maneuvers > 0 && saves > 0 && wrapperSaves >= 4 && periodic >= 2, "dataset does not exercise infusion/save/maneuver checks");

            // Native data distinguishes Wall's on-enter hit from Cloud/Deadly Earth's round ticks.
            var areaBlasts = blueprints.OfType<BlueprintAbility>().Where(b => b.GetComponent<AbilityKineticist>() != null)
                .Select(AbilityProfile.Get).Where(p => p.SpawnedAreas.Any()).ToList();
            Require(areaBlasts.Count > 0, "no lasting forms in the supplied dataset");
            foreach (var p in areaBlasts)
            {
                var roundAreas = (System.Collections.Generic.HashSet<BlueprintAbilityAreaEffect>)typeof(AbilityProfile)
                    .GetField("PeriodicAreas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p);
                foreach (var area in p.SpawnedAreas)
                {
                    bool roundDamage = area.GetComponents<Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectRunAction>()
                        .Any(r => r.Round?.Actions?.Any(a => a is ContextActionDealDamage) == true);
                    Require(roundAreas.Contains(area) == roundDamage, "area damage cadence differs from its native round list");
                }
            }

            var modifierNote = typeof(TacticalEngine).GetMethod("KineticModifierNote", BindingFlags.NonPublic | BindingFlags.Static);
            int selectable = blueprints.OfType<Kingmaker.UnitLogic.ActivatableAbilities.BlueprintActivatableAbility>()
                .Count(b => (modifierNote.Invoke(null, new object[] { b }) as string)?.StartsWith("per-blast") == true);
            Require(selectable >= 20, "native free-command infusion toggles were excluded from configuration search");

            var component = blast.GetComponent<AbilityKineticist>();
            int before = component.CalculateBurnCost((UnitDescriptor)null, blast).Total;
            var type = typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics.KineticPreview", true);
            var harmony = new Harmony("CompanionAI.offline.kinetic-checks");
            try
            {
                foreach (var patch in type.GetNestedTypes(BindingFlags.NonPublic)) harmony.CreateClassProcessor(patch).Patch();
                var dispelType = typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics.NativeDispelPreview", true);
                foreach (var patch in dispelType.GetNestedTypes(BindingFlags.NonPublic))
                    if (patch.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0) harmony.CreateClassProcessor(patch).Patch();
                Require((bool)dispelType.GetProperty("HooksReady", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null),
                    "native dispel layout did not accept the pre-roll safety gates");
                // Run the native DC/CL prefix on detached data. The gate must stop before creating any die roll.
                var targetContext = (Kingmaker.UnitLogic.Mechanics.MechanicsContext)System.Runtime.CompilerServices.RuntimeHelpers
                    .GetUninitializedObject(typeof(Kingmaker.UnitLogic.Mechanics.MechanicsContext));
                var sourceContext = (Kingmaker.UnitLogic.Mechanics.MechanicsContext)System.Runtime.CompilerServices.RuntimeHelpers
                    .GetUninitializedObject(typeof(Kingmaker.UnitLogic.Mechanics.MechanicsContext));
                AccessTools.Field(targetContext.GetType(), "m_Params").SetValue(targetContext,
                    new Kingmaker.UnitLogic.Abilities.AbilityParams { CasterLevel = 9 });
                AccessTools.Field(sourceContext.GetType(), "m_Params").SetValue(sourceContext,
                    new Kingmaker.UnitLogic.Abilities.AbilityParams { CasterLevel = 7 });
                var detached = new Kingmaker.UnitLogic.Buffs.Buff(blueprints.OfType<BlueprintBuff>().First(), targetContext, null);
                var rule = new Kingmaker.RuleSystem.Rules.RuleDispelMagic(null, null, detached,
                    Kingmaker.RuleSystem.Rules.RuleDispelMagic.CheckType.CasterLevel, Kingmaker.EntitySystem.Stats.StatType.Unknown, null)
                    { Reason = sourceContext };
                var preparing = dispelType.GetField("preparing", BindingFlags.NonPublic | BindingFlags.Static);
                try
                {
                    preparing.SetValue(null, rule);
                    rule.OnTrigger(null);
                    Require(rule.CheckRoll == null && rule.DC > 0 && rule.CasterLevel == 7,
                        "native dispel preview failed to prepare DC/CL or reached dice execution");
                }
                finally { preparing.SetValue(null, null); }
                Require((bool)type.GetField("CostHookInstalled", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null),
                    "native cost layout did not accept the read-only preview hook");
                int after = component.CalculateBurnCost((UnitDescriptor)null, blast).Total;
                Require(before == after, "preview hooks changed native cost outside a preview scope");
            }
            finally { harmony.UnpatchAll(harmony.Id); }
            Console.WriteLine($"kinetic checks passed: {triggers} triggers, {maneuvers} direct maneuvers, {saves} direct saves, {wrapperSaves} carrier saves, {periodic} ongoing effects, {areaBlasts.Count} lasting forms, {selectable} selectable modifiers; native cost hook inert outside a scope; native dispel preparation stopped before dice");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Kineticist integration check: " + message);
        }
    }
}
