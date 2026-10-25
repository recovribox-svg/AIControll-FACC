// ★ v0.3.22: native Sleep conditions/SubHD, controlled recipients and SR outcomes; never execute an effect.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.Utility;

namespace ProfileAudit
{
    internal static class SharedAreaChecks
    {
        private static readonly Dictionary<UnitEntityData, UnitDescriptor> Descriptors = new Dictionary<UnitEntityData, UnitDescriptor>(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<UnitProgressionData, int> Levels = new Dictionary<UnitProgressionData, int>(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<TargetWrapper, UnitEntityData> Recipients = new Dictionary<TargetWrapper, UnitEntityData>(ReferenceEqualityComparer.Instance);
        private static bool Descriptor(UnitEntityData __instance, ref UnitDescriptor __result)
        { __result = Descriptors[__instance]; return false; }
        private static bool Level(UnitProgressionData __instance, ref int __result)
        { __result = Levels[__instance]; return false; }
        private static bool NoFact(ref bool __result) { __result = false; return false; }
        private static bool Recipient(TargetWrapper __instance, ref UnitEntityData __result)
        { __result = Recipients[__instance]; return false; }
        private static void InjectedPlan() { throw new InvalidOperationException("injected plan setup failure"); }

        internal static void Check(List<SimpleBlueprint> blueprints)
        {
            var harmony = new Harmony("CompanionAI.ProfileAudit.SharedArea");
            var type = typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics.SharedAreaPreview");
            var planType = type.GetNestedType("Plan", BindingFlags.NonPublic);
            var create = planType.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic);
            var forecast = type.GetMethod("Forecast", BindingFlags.Static | BindingFlags.NonPublic);
            var sleep = blueprints.OfType<BlueprintAbility>().First(b => b.name == "Sleep");
            var profile = AbilityProfile.Get(sleep);
            var plan = create.Invoke(null, new object[] { profile });
            Require(plan != null, $"supplied Sleep action graph was not recognized (AoE={profile.IsAoE}, selects={profile.SelectsTargets}, " +
                $"buffs={string.Join(",", profile.TargetBuffs.Select(b => b.name))}, heals={profile.Heals.Count}, caster buffs={profile.CasterBuffs.Count}, areas={profile.SpawnedAreas.Count()})");
            var pool = (AbilitySharedValue)AccessTools.Field(planType, "Pool").GetValue(plan);
            var context = (AbilityExecutionContext)RuntimeHelpers.GetUninitializedObject(typeof(AbilityExecutionContext));
            AccessTools.Field(typeof(MechanicsContext), "m_SharedValues").SetValue(context,
                new int[Enum.GetValues(typeof(AbilitySharedValue)).Cast<int>().Max() + 1]);
            UnitEntityData Unit(int level)
            {
                var unit = (UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(UnitEntityData));
                var descriptor = (UnitDescriptor)RuntimeHelpers.GetUninitializedObject(typeof(UnitDescriptor));
                var progression = (UnitProgressionData)RuntimeHelpers.GetUninitializedObject(typeof(UnitProgressionData));
                AccessTools.Field(typeof(UnitDescriptor), "Progression").SetValue(descriptor, progression);
                Descriptors[unit] = descriptor; Levels[progression] = level;
                return unit;
            }
            try
            {
                harmony.Patch(AccessTools.PropertyGetter(typeof(UnitEntityData), "Descriptor"),
                    prefix: new HarmonyMethod(typeof(SharedAreaChecks), nameof(Descriptor)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(UnitProgressionData), "CharacterLevel"),
                    prefix: new HarmonyMethod(typeof(SharedAreaChecks), nameof(Level)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(TargetWrapper), "Unit"),
                    prefix: new HarmonyMethod(typeof(SharedAreaChecks), nameof(Recipient)));
                foreach (var condition in new[] { typeof(ContextConditionHasFact), typeof(ContextConditionCasterHasFact), typeof(ContextConditionIsUnconscious) })
                    harmony.Patch(AccessTools.Method(condition, "CheckCondition"), prefix: new HarmonyMethod(typeof(SharedAreaChecks), nameof(NoFact)));
                var first = Unit(2); var second = Unit(3); var third = Unit(2);
                TargetWrapper Wrap(UnitEntityData unit)
                {
                    // Offline actors are not registered with the live game's EntityRef resolver.
                    var wrapper = new TargetWrapper(UnityEngine.Vector3.zero);
                    Recipients[wrapper] = unit;
                    return wrapper;
                }
                IDictionary Run(UnitEntityData[] order, Func<UnitEntityData, float> pass)
                {
                    context[pool] = 73;
                    var result = (IDictionary)forecast.Invoke(null, new object[] { plan, context,
                        order.Select(Wrap), 4, pass });
                    Require(context[pool] == 73, "projection leaked its remaining pool into the source context");
                    return result;
                }
                float Share(IDictionary result, UnitEntityData unit) => (float)result[unit];
                var normal = Run(new[] { first, second, third }, _ => 1f);
                Require(Share(normal, first) == 1f && Share(normal, second) == 0f && Share(normal, third) == 1f,
                    "each recipient was incorrectly credited the entire native HD pool");
                var reversed = Run(new[] { second, first, third }, _ => 1f);
                Require(Share(reversed, second) == 1f && Share(reversed, first) == 0f && Share(reversed, third) == 0f,
                    "projection ignored recipient order");
                var resisted = Run(new[] { first, second }, u => ReferenceEquals(u, first) ? 0f : 1f);
                Require(Share(resisted, second) == 1f, "a pre-action resistance rejection spent the shared pool");
                var mixed = Run(new[] { first, second }, u => ReferenceEquals(u, first) ? 0.25f : 1f);
                Require(Math.Abs(Share(mixed, second) - 0.75f) < 0.0001f,
                    "earlier SR uncertainty was ignored or current-recipient resistance was counted twice");
                var ability = (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                AccessTools.Field(typeof(AbilityData), "Blueprint").SetValue(ability, sleep);
                var buff = profile.TargetBuffs[0];
                var aim = new TargetWrapper(new UnityEngine.Vector3(1f, 0f, 2f));
                var aimed = AccessTools.Method(typeof(AbilityProfile), "AimedAt");
                var constructor = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
                IDisposable Project(IDictionary result) => (IDisposable)constructor.Invoke(new object[] { ability, buff, result });
                using ((IDisposable)aimed.Invoke(null, new object[] { aim }))
                using (Project(normal))
                {
                    Require(profile.BuffAppliesTo(buff, ability, second) == EffectApplies.No,
                        "the scorer still credited a recipient after the shared pool was spent");
                    Require(profile.BuffAppliesTo(buff, ability, first) == EffectApplies.Yes, "the first eligible recipient was lost");
                    using (Project(reversed))
                        Require(profile.BuffAppliesTo(buff, ability, first) == EffectApplies.No, "nested projection reused its caller's pool");
                    Require(profile.BuffAppliesTo(buff, ability, first) == EffectApplies.Yes, "nested disposal lost the caller's projection");
                    var tryShare = AccessTools.Method(type, "TryShare");
                    using ((IDisposable)aimed.Invoke(null, new object[] { new TargetWrapper(UnityEngine.Vector3.zero) }))
                        Require(!(bool)tryShare.Invoke(null, new object[] { ability, buff, first, 0f }), "a different aim inherited a pool");
                }
                Require(!(bool)AccessTools.Method(type, "TryShare").Invoke(null, new object[] { ability, buff, first, 0f }),
                    "a projection escaped its scoring scope");

                // A save-dependent debit needs a different forecast: reject it without executing any effect.
                var run = sleep.GetComponent<AbilityEffectRunAction>();
                var original = run.Actions.Actions;
                try
                {
                    run.Actions.Actions = original.Concat(new GameAction[] { new ContextActionSavingThrow {
                        Actions = new ActionList { Actions = new GameAction[] { new ContextActionChangeSharedValue {
                            SharedValue = pool, Type = SharedValueChangeType.SubHD } } } } }).ToArray();
                    Require(create.Invoke(null, new object[] { profile }) == null, "save-dependent budget writes were accepted");
                }
                finally { run.Actions.Actions = original; }
                context[pool] = 81;
                try
                {
                    forecast.Invoke(null, new object[] { plan, context, new[] { Wrap(first), Wrap(second) },
                        4, new Func<UnitEntityData, float>(u => ReferenceEquals(u, second) ? throw new InvalidOperationException("injected") : 1f) });
                    throw new InvalidOperationException("injected forecast failure did not propagate");
                }
                catch (TargetInvocationException ex) when (ex.InnerException?.Message == "injected") { }
                Require(context[pool] == 81 && Kingmaker.ElementsSystem.ContextData<MechanicsContext.Data>.Current == null,
                    "failure leaked native context/shared-value state");
                var readPlan = AccessTools.Field(typeof(AbilityProfile), "m_ReadSharedAreaPlan");
                var cachedPlan = AccessTools.Field(typeof(AbilityProfile), "m_SharedAreaPlan");
                object wasRead = readPlan.GetValue(profile), wasPlan = cachedPlan.GetValue(profile);
                try
                {
                    readPlan.SetValue(profile, false);
                    harmony.Patch(create, prefix: new HarmonyMethod(typeof(SharedAreaChecks), nameof(InjectedPlan)));
                    Require(AccessTools.Method(type, "Begin").Invoke(null, new object[] { ability, profile, aim }) == null,
                        "optional plan setup failure escaped instead of retaining ordinary ability scoring");
                }
                finally { readPlan.SetValue(profile, wasRead); cachedPlan.SetValue(profile, wasPlan); }
            }
            finally { harmony.UnpatchAll(harmony.Id); Descriptors.Clear(); Levels.Clear(); Recipients.Clear(); }
            Console.WriteLine("SHARED AREA CHECKS: supplied Sleep graph; native HD predicate/SubHD; order/SR branches; scorer/aim/nested isolation; unsafe-graph fallback; context restoration PASS");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("shared area: " + message); }
    }
}
