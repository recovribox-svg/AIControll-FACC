// ★ v0.3.22: scoped condition-context reuse and temporary shared-value restoration, offline only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.Enums;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.Utility;
using UnityEngine;

namespace ProfileAudit
{
    internal static class ConditionContextChecks
    {
        private static int builds, runs, runMode;
        private static bool RuleContext(ref RulebookEventContext __result) { __result = null; return false; }
        private static bool Context(AbilityData ability, TargetWrapper target, ref AbilityExecutionContext __result)
        {
            builds++;
            var context = (AbilityExecutionContext)RuntimeHelpers.GetUninitializedObject(typeof(AbilityExecutionContext));
            AccessTools.Field(typeof(AbilityExecutionContext), "Ability").SetValue(context, ability);
            AccessTools.Field(typeof(AbilityExecutionContext), "<ClickedTarget>k__BackingField").SetValue(context, target);
            AccessTools.Field(typeof(MechanicsContext), "m_MainTarget").SetValue(context, target);
            AccessTools.Field(typeof(MechanicsContext), "m_SharedValues").SetValue(context,
                new int[Enum.GetValues(typeof(AbilitySharedValue)).Cast<int>().Max() + 1]);
            AccessTools.Field(typeof(MechanicsContext), "m_Ranks").SetValue(context,
                new int[Enum.GetValues(typeof(AbilityRankType)).Cast<int>().Max() + 1]);
            AccessTools.Field(typeof(MechanicsContext), "m_Params").SetValue(context, new AbilityParams { DC = 19, CasterLevel = 2 });
            __result = context;
            return false;
        }
        private static bool Run(ConditionsChecker checker, ref bool __result)
        {
            runs++;
            if (runMode == 2) throw new InvalidOperationException("injected checker failure");
            if (runMode == 3)
            {
                __result = (bool)AccessTools.Method(typeof(ContextConditionIsMainTarget), "CheckCondition")
                    .Invoke(checker.Conditions[0], null);
                return false;
            }
            __result = runMode == 0 || runs != 1;
            return false;
        }

        internal static void Check(List<SimpleBlueprint> blueprints)
        {
            var native = new Harmony("CompanionAI.ProfileAudit.ConditionContexts");
            var type = typeof(AbilityProfile);
            var aimAt = type.GetMethod("AimedAt", BindingFlags.NonPublic | BindingFlags.Static);
            var readContext = type.GetMethod("ConditionContext", BindingFlags.NonPublic | BindingFlags.Static);
            var withShared = type.GetMethod("WithSharedValues", BindingFlags.NonPublic | BindingFlags.Static);
            var bp = blueprints.OfType<BlueprintAbility>().First(b => b.GetComponents<ContextCalculateSharedValue>().Any());
            AbilityData NewAbility()
            {
                var a = (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                AccessTools.Field(typeof(AbilityData), "Blueprint").SetValue(a, bp);
                return a;
            }
            var ability = NewAbility();
            var other = NewAbility();
            var aim = new TargetWrapper(new Vector3(1f, 0f, 2f));
            IDisposable Aimed(TargetWrapper target) => (IDisposable)aimAt.Invoke(null, new object[] { target });
            MechanicsContext Read(AbilityData a, TargetWrapper target) => (MechanicsContext)readContext.Invoke(null, new object[] { a, target });
            try
            {
                native.Patch(AccessTools.Method(typeof(CombatMath), nameof(CombatMath.CreateContext)),
                    prefix: new HarmonyMethod(typeof(ConditionContextChecks), nameof(Context)));
                native.Patch(AccessTools.PropertyGetter(typeof(Rulebook), "CurrentContext"),
                    prefix: new HarmonyMethod(typeof(ConditionContextChecks), nameof(RuleContext)));
                builds = 0;
                using (Aimed(aim))
                {
                    var first = Read(ability, aim);
                    for (int i = 0; i < 200; i++) Require(ReferenceEquals(first, Read(ability, aim)), "same cast did not reuse its context");
                    Require(builds == 1, "condition readers rebuilt the cast context");
                    Require(((AbilityExecutionContext)first).ClickedTarget == aim, "main aim was lost");
                    var mainTarget = new ContextConditionIsMainTarget();
                    var nativeMainCheck = AccessTools.Method(typeof(ContextConditionIsMainTarget), "CheckCondition");
                    using (first.GetDataScope(aim))
                        Require((bool)nativeMainCheck.Invoke(mainTarget, null), "native main-target condition rejected the aimed recipient");
                    // Point-wrapper equality is the game's rule too; compare a fresh native cast rather than
                    // assuming different point coordinates imply a different "main target" predicate.
                    var recipient = new TargetWrapper(new Vector3(3f, 0f, 4f));
                    AbilityExecutionContext freshNative = null;
                    Context(ability, aim, ref freshNative);
                    bool expected;
                    using (freshNative.GetDataScope(recipient)) expected = (bool)nativeMainCheck.Invoke(mainTarget, null);
                    using (first.GetDataScope(recipient))
                    {
                        Require(ContextData<MechanicsContext.Data>.Current.CurrentTarget.Point.x == 3f,
                            "recipient scope did not reach the native context");
                        Require((bool)nativeMainCheck.Invoke(mainTarget, null) == expected, "reused context changed the native main-target predicate");
                    }
                    Require(((AbilityExecutionContext)first).ClickedTarget == aim, "recipient scope changed the main aim");
                    Require(!ReferenceEquals(first, Read(other, aim)), "different ability objects shared contexts");
                    using (Aimed(new TargetWrapper(new Vector3(1f, 0f, 2f), 90f, null)))
                        Require(!ReferenceEquals(first, Read(ability, aim)), "nested oriented aim reused the outer cast");
                    Require(ReferenceEquals(first, Read(ability, aim)), "nested aim did not restore outer context");
                    AbilityProfile.ClearConditionCache();
                    var fresh = Read(ability, aim);
                    Require(!ReferenceEquals(first, fresh), "modifier/cache invalidation retained a detached context");
                    var previewType = typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics.KineticPreview", true);
                    var constructor = previewType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                        new[] { typeof(Kingmaker.EntitySystem.Entities.UnitEntityData), typeof(IEnumerable<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff>),
                            typeof(IEnumerable<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff>) }, null);
                    var empty = Array.Empty<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff>();
                    MechanicsContext projected;
                    using ((IDisposable)constructor.Invoke(new object[] { null, empty, empty }))
                    {
                        projected = Read(ability, aim);
                        Require(!ReferenceEquals(projected, fresh), "modifier entry retained the old cast context");
                    }
                    Require(!ReferenceEquals(projected, Read(ability, aim)), "modifier restoration retained a projected context");

                    // Infusion effects use their supplied fact context; they must never be replaced by the cached cast.
                    var effectType = typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics.KineticEffectContext", true);
                    using ((IDisposable)Activator.CreateInstance(effectType, BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new object[] { ability, fresh, "fixture", AbilityProfile.Get(bp), null, null, null }, null))
                        Require(ReferenceEquals(fresh, Read(ability, aim)), "infusion source context was replaced");

                    var shared = bp.GetComponents<ContextCalculateSharedValue>().First().ValueType;
                    var checker = new ConditionsChecker { Conditions = new Condition[]
                        { new ContextConditionHitDice { AddSharedValue = true, SharedValue = shared } } };
                    native.Patch(AccessTools.Method(type, "Run"), prefix: new HarmonyMethod(typeof(ConditionContextChecks), nameof(Run)));
                    for (runMode = 0; runMode < 3; runMode++)
                    {
                        fresh[shared] = 73; runs = 0;
                        bool threw = false;
                        try { withShared.Invoke(null, new object[] { checker, ability, fresh }); }
                        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { threw = true; }
                        Require(threw == (runMode == 2), "injected checker failure was not exercised");
                        Require(fresh[shared] == 73, "temporary low/high/average shared value was not restored");
                        Require(runs == (runMode == 0 ? 2 : runMode == 1 ? 3 : 1), "wrong shared-value control-flow branch exercised");
                    }
                }
                var outside = Read(ability, aim);
                Require(!ReferenceEquals(outside, Read(ability, aim)), "condition contexts survived outside an aim scope");

                // Exercise CheckFor's actual cache-hit path without a live unit. Miss evaluation needs the
                // game's fully initialized entity reference registry; native point-predicate parity is tested above.
                var checkFor = type.GetMethod("CheckFor", BindingFlags.NonPublic | BindingFlags.Static);
                var guard = new ConditionsChecker { Conditions = new Condition[] { new ContextConditionIsMainTarget() } };
                var target1 = RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.EntitySystem.Entities.UnitEntityData));
                var target2 = RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.EntitySystem.Entities.UnitEntityData));
                var keyType = type.GetNestedType("ConditionKey", BindingFlags.NonPublic);
                object Key(AbilityData a, object target) => Activator.CreateInstance(keyType, BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { guard, a, target, null, null }, null);
                System.Collections.IDictionary Results() => (System.Collections.IDictionary)AccessTools.Field(
                    type.GetNestedType("AimScope", BindingFlags.NonPublic), "Results").GetValue(AccessTools.Field(type, "s_AimScope").GetValue(null));
                bool? Evaluate(AbilityData a, object target) => (bool?)checkFor.Invoke(null, new object[] { guard, a, target, null });
                using (Aimed(aim))
                {
                    var results = Results();
                    results.Add(Key(ability, target1), false);
                    for (int i = 0; i < 20; i++) Require(Evaluate(ability, target1) == false, "guard result changed on reuse");
                    Require(!results.Contains(Key(ability, target2)), "different recipients shared guard results");
                    Require(!results.Contains(Key(other, target1)), "equal-valued ability objects shared guard results");
                    results.Add(Key(other, target1), true);
                    Require(Evaluate(other, target1) == true, "guard cache ignored ability identity");
                    using (Aimed(new TargetWrapper(new Vector3(1f, 0f, 2f), 90f, null)))
                    {
                        Require(Results().Count == 0, "oriented nested aim shared guard results");
                        Results().Add(Key(ability, target1), true);
                        Require(Evaluate(ability, target1) == true, "nested guard result was not used");
                    }
                    Require(Evaluate(ability, target1) == false, "nested aim discarded the caller's guard results");
                    AbilityProfile.ClearConditionCache();
                    Require(results.Count == 0, "modifier invalidation retained guard results");
                }

                var movementOnly = new TacticalContext { Permissions = new ActionPermissions { MovementOnly = true } };
                foreach (var method in new[] { "AddKineticPlanCandidates", "AddKineticCandidates" })
                    AccessTools.Method(typeof(TacticalEngine), method).Invoke(null, new object[] { movementOnly, new List<Candidate>(), false });
                // Unit is deliberately null: these movement-only decisions must return before any game query.
                Console.WriteLine("CONDITION CONTEXT CHECKS: 201 reads/1 cast context; native main-target parity and scoped guard-cache isolation; ability/aim/source/recipient/modifier isolation; shared values restored on early/average/throw paths; nausea skips PASS");
            }
            finally { native.UnpatchAll(native.Id); AbilityProfile.ClearConditionCache(); }
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Condition context check: " + message); }
    }
}
