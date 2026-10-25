// ★ v0.3.22: scoped, read-only projection of learned blast modifiers for the game's own cost/metamagic queries.
// No fact is activated, registered, subscribed, or added to the unit. Never use this to execute a command.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Mechanics;

namespace CompanionAI_Pathfinder.Tactics
{
    internal sealed class KineticPreview : IDisposable
    {
        [ThreadStatic] internal static KineticPreview Current;
        private readonly KineticPreview previous;
        internal readonly UnitEntityData Unit;
        internal readonly HashSet<BlueprintBuff> Controlled;
        internal readonly List<BlueprintBuff> Selected;
        internal readonly Dictionary<BlueprintBuff, MechanicsContext> Contexts = new Dictionary<BlueprintBuff, MechanicsContext>();
        private readonly Dictionary<BlueprintBuff, EntityFact> detachedFacts = new Dictionary<BlueprintBuff, EntityFact>();
        private readonly List<(AddKineticistBurnModifier Modifier, EntityFactComponent Runtime)> handlers =
            new List<(AddKineticistBurnModifier, EntityFactComponent)>();
        internal static bool CostHookInstalled;
        internal static bool PreviewActive; // plain fast exit before TLS/property access on global hot paths
        internal const string PatchOwner = "CompanionAI_Pathfinder.KineticPreview";
        private static bool costLayoutMatched;

        internal static bool InstallPatches(Harmony harmony, Action beforeEachPatch = null)
        {
            CostHookInstalled = false;
            costLayoutMatched = false;
            bool success = OptionalPatchInstallation.TryInstall(() =>
            {
                NativeDispelPreview.Disable();
                harmony.UnpatchAll(harmony.Id); // reload/retry cannot duplicate native cost injection
                Patch(harmony, AccessTools.Method(typeof(UnitHelper), nameof(UnitHelper.HasFact),
                    new[] { typeof(UnitDescriptor), typeof(Kingmaker.Blueprints.Facts.BlueprintFact) }), typeof(FactExistence), "Prefix", beforeEachPatch);
                Patch(harmony, AccessTools.Method(typeof(EntityFactsProcessor<Buff>), "HasFact",
                    new[] { typeof(Kingmaker.Blueprints.Facts.BlueprintFact) }), typeof(BuffExistence), "Prefix", beforeEachPatch);
                Patch(harmony, AccessTools.Method(typeof(AddKineticistBurnModifier), nameof(AddKineticistBurnModifier.HandleKineticistCalculateAbilityCost)), typeof(ExistingCostModifier), "Prefix", beforeEachPatch);
                Patch(harmony, AccessTools.Method(typeof(AutoMetamagic), nameof(AutoMetamagic.ShouldApplyTo)), typeof(ExistingMetamagic), "Prefix", beforeEachPatch);
                Patch(harmony, AccessTools.Method(typeof(AbilityData), nameof(AbilityData.HasMetamagic)), typeof(SelectedMetamagic), "Postfix", beforeEachPatch);
                Patch(harmony, AccessTools.PropertyGetter(typeof(UnitPartKineticist), nameof(UnitPartKineticist.GatherPowerMode)), typeof(GatherProjection), "Postfix", beforeEachPatch);
                Patch(harmony, AccessTools.Method(typeof(AbilityKineticist), "CalculateBurnCost", new[] { typeof(UnitDescriptor), typeof(BlueprintAbility) }), typeof(CostProjection), "Transpiler", beforeEachPatch);
                if (!costLayoutMatched) throw new InvalidOperationException("native cost layout not recognized");
                NativeDispelPreview.InstallPatches(harmony, beforeEachPatch);
                CostHookInstalled = true; // publish readiness only after every optional hook succeeds
            }, () => harmony.UnpatchAll(harmony.Id), out var failure);
            if (!success)
            {
                CostHookInstalled = false;
                NativeDispelPreview.Disable();
                Main.Log($"[Kinetic] optional preview patches unavailable; current setup retained: {failure.GetBaseException().Message}");
            }
            if (success) Main.Log("[Kinetic] optional preview hooks installed");
            return success;
        }

        internal static void Patch(Harmony harmony, MethodBase original, Type patch, string kind, Action before)
        {
            before?.Invoke(); // offline failure-injection seam; never supplied by the game load path
            if (original == null) throw new MissingMethodException($"optional kinetic patch target for {patch.Name}");
            var method = new HarmonyMethod(AccessTools.Method(patch, kind));
            harmony.Patch(original, prefix: kind == "Prefix" ? method : null,
                postfix: kind == "Postfix" ? method : null, transpiler: kind == "Transpiler" ? method : null);
        }
        internal KineticistGatherPowerMode? GatherMode;

        internal static IEnumerable<BlueprintScriptableObject> ActiveBlueprints(UnitEntityData unit)
        {
            var live = unit.Facts.List.Where(f => f?.Active == true && f.Blueprint != null)
                .Select(f => (BlueprintScriptableObject)f.Blueprint);
            var scope = Current;
            return scope?.Unit == unit
                ? live.Where(b => !scope.Controlled.Contains(b as BlueprintBuff)).Concat(scope.Selected)
                : live;
        }

        internal static IEnumerable<(T Component, EntityFact Fact)> ActiveComponents<T>(UnitEntityData unit) where T : BlueprintComponent
        {
            var scope = Current;
            foreach (var fact in unit.Facts.List)
            {
                if (fact?.Active != true || fact.Blueprint == null ||
                    (scope?.Unit == unit && scope.Controlled.Contains(fact.Blueprint as BlueprintBuff))) continue;
                foreach (var component in fact.Blueprint.GetComponents<T>()) yield return (component, fact);
            }
            if (scope?.Unit == unit)
                foreach (var bp in scope.Selected)
                    foreach (var component in bp.GetComponents<T>()) yield return (component, scope.detachedFacts[bp]);
        }

        internal static void QueryComponent<T>(T prototype, EntityFact fact, Action<T> query) where T : UnitFactComponentDelegate
        {
            var clone = (T)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(prototype, null);
            var runtime = clone.CreateRuntimeFactComponent();
            runtime.Setup(clone);
            AccessTools.Property(typeof(EntityFactComponent), "Fact").SetValue(runtime, fact);
            using (runtime.RequestEventContext()) query(clone);
        }

        internal KineticPreview(UnitEntityData unit, IEnumerable<ActivatableAbility> controlled, IEnumerable<ActivatableAbility> selected)
            : this(unit, controlled.Select(t => t.Blueprint.Buff), selected.Select(t => t.Blueprint.Buff)) { }

        internal static KineticPreview WithAdditionalBuff(UnitEntityData unit, BlueprintBuff buff)
        {
            var prior = Current?.Unit == unit ? Current : null;
            var preview = new KineticPreview(unit, (prior?.Controlled ?? new HashSet<BlueprintBuff>()).Concat(new[] { buff }),
                (prior?.Selected ?? new List<BlueprintBuff>()).Concat(new[] { buff }));
            preview.GatherMode = prior?.GatherMode;
            return preview;
        }

        private KineticPreview(UnitEntityData unit, IEnumerable<BlueprintBuff> controlled, IEnumerable<BlueprintBuff> selected)
        {
            if (!CostHookInstalled) throw new InvalidOperationException("Native kineticist cost projection is unavailable");
            Unit = unit;
            Controlled = new HashSet<BlueprintBuff>(controlled);
            Selected = selected.Distinct().ToList();
            foreach (var bp in Selected)
            {
                KineticDecisionScope.Check();
                var live = unit.Buffs.GetBuff(bp);
                var context = live?.MaybeContext ?? new MechanicsContext(unit, unit.Descriptor, bp);
                if (live == null) context.Recalculate();
                Contexts[bp] = context;
                var detached = new Buff(bp, context, null);
                // Give the detached fact access to its owner without attaching it or executing any lifecycle action.
                AccessTools.Property(typeof(EntityFact), "Manager").SetValue(detached, unit.Facts);
                detachedFacts[bp] = detached;
                foreach (var prototype in bp.GetComponents<AddKineticistBurnModifier>())
                {
                    // CalculateBurnCost may update Value for a ContextValue; do not write to the live blueprint.
                    var clone = (AddKineticistBurnModifier)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(prototype, null);
                    var runtime = clone.CreateRuntimeFactComponent();
                    runtime.Setup(clone);
                    AccessTools.Property(typeof(EntityFactComponent), "Fact").SetValue(runtime, detached);
                    handlers.Add((clone, runtime));
                }
            }
            previous = Current;
            Current = this;
            PreviewActive = true;
            AbilityProfile.ClearConditionCache();
            CombatMath.ClearAttackCache();
        }

        internal static bool Suppress(AddKineticistBurnModifier modifier, UnitDescriptor caster) =>
            Current != null && caster?.Unit == Current.Unit &&
            !Current.handlers.Any(h => ReferenceEquals(h.Modifier, modifier)) &&
            Current.Controlled.Contains(((EntityFact)AccessTools.Property(modifier.GetType(), "Fact").GetValue(modifier)).Blueprint as BlueprintBuff);

        // Called after the game creates its cost, BEFORE its normal cost-handler event. Every selected modifier
        // runs its actual game method in a detached component context, and all other native/mod handlers still run.
        internal static void Inject(ref KineticistAbilityBurnCost cost, UnitDescriptor caster, BlueprintAbility ability)
        {
            if (!PreviewActive) return;
            var scope = Current;
            if (scope == null || caster?.Unit != scope.Unit) return;
            foreach (var entry in scope.handlers)
                using (entry.Runtime.RequestEventContext())
                    entry.Modifier.HandleKineticistCalculateAbilityCost(caster, ability, ref cost);
        }

        internal static bool Suppress(AutoMetamagic component, AbilityData ability) =>
            Current != null && ability?.Caster?.Unit == Current.Unit &&
            Current.Controlled.Any(b => b.GetComponents<AutoMetamagic>().Contains(component));

        public void Dispose()
        {
            Current = previous;
            PreviewActive = previous != null;
            AbilityProfile.ClearConditionCache();
            CombatMath.ClearAttackCache();
        }

        // ★ v0.3.22: blast delivery and native custom properties can inspect their modifier buffs directly
        // (e.g. ricochet). Shadow only existence queries on this unit; no fact is attached or removed.
        private static bool ProjectFact(Kingmaker.Blueprints.Facts.BlueprintFact fact, out bool result)
        {
            result = false;
            if (Current == null || !(fact is BlueprintBuff buff) || !Current.Controlled.Contains(buff)) return false;
            result = Current.Selected.Contains(buff);
            return true;
        }

        private static class FactExistence
        {
            private static bool Prefix(UnitDescriptor __0, Kingmaker.Blueprints.Facts.BlueprintFact __1, ref bool __result)
            {
                if (!PreviewActive) return true;
                var scope = Current;
                if (scope == null || scope.Unit != __0?.Unit || !ProjectFact(__1, out var projected)) return true;
                __result = projected;
                return false;
            }
        }

        private static class BuffExistence
        {
            private static bool Prefix(EntityFactsProcessor __instance, Kingmaker.Blueprints.Facts.BlueprintFact __0, ref bool __result)
            {
                if (!PreviewActive) return true;
                // Generic processor code may be shared on Mono. Accept the base type and affect only this buff collection.
                if (Current == null || !ReferenceEquals(__instance, Current.Unit.Buffs) || !ProjectFact(__0, out var projected)) return true;
                __result = projected;
                return false;
            }
        }

        private static class CostProjection
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                // Verified game layout: a captured local holds the new cost immediately before RaiseEvent.
                // Fail closed on a different method layout; do not replace the native calculation with copied math.
                var stores = code.Select((c, i) => (c, i)).Where(x => x.c.opcode == OpCodes.Stfld &&
                    x.c.operand is FieldInfo f && f.FieldType == typeof(KineticistAbilityBurnCost)).ToList();
                if (stores.Count != 1 || code.Count < 2 || code[1].opcode != OpCodes.Stloc_0)
                {
                    return code;
                }
                var field = (FieldInfo)stores[0].c.operand;
                code.InsertRange(stores[0].i + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldloc_0), new CodeInstruction(OpCodes.Ldflda, field),
                    new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(KineticPreview), nameof(Inject)))
                });
                costLayoutMatched = true;
                return code;
            }
        }

        private static class ExistingCostModifier
        {
            private static bool Prefix(AddKineticistBurnModifier __instance, UnitDescriptor __0) => !PreviewActive || !Suppress(__instance, __0);
        }

        private static class ExistingMetamagic
        {
            private static bool Prefix(AutoMetamagic __0, AbilityData __2, ref bool __result)
            {
                if (!PreviewActive) return true;
                if (!Suppress(__0, __2)) return true;
                __result = false;
                return false;
            }
        }

        private static class SelectedMetamagic
        {
            private static void Postfix(AbilityData __instance, Metamagic __0, ref bool __result)
            {
                if (!PreviewActive) return;
                var scope = Current;
                if (__result || scope == null || __instance.Caster?.Unit != scope.Unit) return;
                foreach (var bp in scope.Selected)
                    foreach (var component in bp.GetComponents<AutoMetamagic>())
                    {
                        // Bypass only our suppression while asking the game's suitability function. No state changes.
                        bool applies;
                        try { Current = scope.previous; applies = AutoMetamagic.ShouldApplyTo(component, __instance.Blueprint, __instance); }
                        finally { Current = scope; }
                        if (applies && (component.Metamagic & __0) == __0) { __result = true; return; }
                    }
            }
        }

        private static class GatherProjection
        {
            private static void Postfix(UnitPartKineticist __instance, ref KineticistGatherPowerMode __result)
            {
                if (!PreviewActive) return;
                var scope = Current;
                if (scope != null && scope.Unit == __instance.Owner?.Unit && scope.GatherMode.HasValue) __result = scope.GatherMode.Value;
            }
        }
    }
}
