// ★ v0.3.22: native query reuse and invalidation checks; this code runs only in the offline audit process.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.RuleSystem;
using Kingmaker.Utility;
using UnityEngine;

namespace ProfileAudit
{
    internal static class DecisionReadChecks
    {
        private static int calls;
        private static int availabilityReads;
        private static AbilityData offered;
        private static ActionSlot offeredSlot;
        private static void BrainShape(Kingmaker.EntitySystem.Entities.UnitEntityData unit) { }
        private static RulebookEventContext activeRules;
        // ★ v0.3.22: the real game replaces an empty context after each top-level event. Returning null hid
        // the original cache-key bug; rotate real empty contexts on EVERY lookup in the regression fixture.
        private static bool RuleContext(ref RulebookEventContext __result)
        { __result = activeRules ?? new RulebookEventContext(); return false; }
        private static bool Discovery(ref List<AbilityData> __result) { __result = new List<AbilityData> { offered }; return false; }
        private static bool Slot(ref ActionSlot __result) { __result = offeredSlot; return false; }
        private static bool Available(ref bool __result) { availabilityReads++; __result = false; return false; }
        private static bool Parameters(ref AbilityParams __result)
        {
            calls++;
            __result = new AbilityParams { CasterLevel = 7, DC = 23 };
            return false;
        }

        private sealed class MultiClassCalculator : ContextAbilityParamsCalculator
        {
            public override AbilityParams Calculate(MechanicsContext context) => new AbilityParams { DC = 29, CasterLevel = 11 };
        }

        internal static void Run(List<SimpleBlueprint> blueprints)
        {
            var assembly = typeof(TacticalEngine).Assembly;
            var scopeType = assembly.GetType("CompanionAI_Pathfinder.Tactics.DecisionReadScope", true);
            var previewType = assembly.GetType("CompanionAI_Pathfinder.Tactics.KineticPreview", true);
            var query = typeof(CombatMath).GetMethod("AbilityParameters", BindingFlags.Static | BindingFlags.NonPublic);
            var harmony = new Harmony("CompanionAI.ProfileAudit.DecisionReads");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(AbilityData), "CalculateParams"),
                    prefix: new HarmonyMethod(typeof(DecisionReadChecks), nameof(Parameters)));
                // The native getter requires Game.Instance; the offline process has no live game singleton.
                harmony.Patch(AccessTools.PropertyGetter(typeof(Rulebook), "CurrentContext"),
                    prefix: new HarmonyMethod(typeof(DecisionReadChecks), nameof(RuleContext)));
                var ability = (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                var second = (AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                AbilityParams Read(AbilityData a) => (AbilityParams)query.Invoke(null, new object[] { a });
                IDisposable NewScope() => (IDisposable)Activator.CreateInstance(scopeType, true);
                calls = 0;
                using (var outer = NewScope())
                {
                    for (int i = 0; i < 200; i++)
                    {
                        var p = Read(ability);
                        Require(p.DC == 23, "cached parameters were changed by a consumer");
                        p.DC = -999;
                    }
                    Require(calls == 1, "200 repeated queries did not reuse a native parameter result");
                    Read(second);
                    Require(calls == 2, "different ability instances shared parameters");
                    using (NewScope()) { Read(ability); Require(calls == 3, "nested hypothetical state reused outer parameters"); }
                    Read(ability);
                    Require(calls == 3, "nested read scope did not restore the outer cache");
                    // Use the real modifier-scope constructor/disposal, with no detached facts needed.
                    var constructor = previewType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                        new[] { typeof(Kingmaker.EntitySystem.Entities.UnitEntityData), typeof(IEnumerable<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff>),
                            typeof(IEnumerable<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff>) }, null);
                    var empty = Array.Empty<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff>();
                    using ((IDisposable)constructor.Invoke(new object[] { null, empty, empty }))
                    { Read(ability); Require(calls == 4, "modifier entry retained old parameter results"); }
                    Read(ability);
                    Require(calls == 5, "modifier restoration retained projected parameter results");
                }
                using (NewScope()) { Read(ability); Require(calls == 6, "parameters leaked into the next decision"); }
                Read(ability); Read(ability);
                Require(calls == 8, "a query outside a decision was cached");
                using (NewScope())
                {
                    int start = calls;
                    activeRules = new RulebookEventContext();
                    activeRules.PushEvent((RulebookEvent)RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.RuleSystem.Rules.RuleCalculateWeaponStats)));
                    try { Read(ability); Read(ability); Require(calls == start + 2, "active rule context was incorrectly cached"); }
                    finally { activeRules = null; }
                    Read(ability); Read(ability);
                    Require(calls == start + 3, "fresh empty roots defeated parameter reuse");
                }
                using (NewScope())
                {
                    var aimedAt = typeof(AbilityProfile).GetMethod("AimedAt", BindingFlags.Static | BindingFlags.NonPublic);
                    IDisposable Aim(TargetWrapper target) => (IDisposable)aimedAt.Invoke(null, new object[] { target });
                    int start = calls;
                    Read(ability);
                    using (Aim(new TargetWrapper(new Vector3(0f, 0f, 0f)))) { Read(ability); Read(ability); }
                    using (Aim(new TargetWrapper(new Vector3(0f, 0f, 0f), 0f, null))) Read(ability);
                    using (Aim(new TargetWrapper(new Vector3(0f, 0f, 0f), 90f, null))) Read(ability);
                    Require(calls == start + 4, "point, absent aim or wall orientation shared parameter results");
                    var ambient = (MechanicsContext)RuntimeHelpers.GetUninitializedObject(typeof(MechanicsContext));
                    using (ambient.GetDataScope(new TargetWrapper(new Vector3(0f, 0f, 0f)))) { Read(ability); Read(ability); }
                    Require(calls == start + 6, "mutable ambient mechanics context was incorrectly memoized");
                    Read(ability);
                    Require(calls == start + 6, "ambient scope did not preserve the outside result");
                }

                // Exercise the actual candidate entry point: an unavailable slot must be rejected before native
                // availability/effect checks, while free and standard-as-move options still reach them.
                offered = ability;
                harmony.Patch(AccessTools.Method(typeof(AbilityDiscovery), "Gather"),
                    prefix: new HarmonyMethod(typeof(DecisionReadChecks), nameof(Discovery)));
                harmony.Patch(AccessTools.Method(typeof(TacticalEngine), "SlotOf"),
                    prefix: new HarmonyMethod(typeof(DecisionReadChecks), nameof(Slot)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(AbilityData), "IsAvailable"),
                    prefix: new HarmonyMethod(typeof(DecisionReadChecks), nameof(Available)));
                var candidates = typeof(TacticalEngine).GetMethod("AddAbilityCandidates", BindingFlags.Static | BindingFlags.NonPublic);
                var ctx = new TacticalContext();
                void CheckSlot(ActionSlot slot, bool shouldReach)
                {
                    offeredSlot = slot; availabilityReads = 0;
                    candidates.Invoke(null, new object[] { ctx, new List<Candidate>() });
                    Require(availabilityReads == (shouldReach ? 1 : 0), "ability-slot pruning touched an unaffordable ability or lost a usable one");
                }
                CheckSlot(ActionSlot.Standard, false); CheckSlot(ActionSlot.FullRound, false);
                CheckSlot(ActionSlot.Swift, false); CheckSlot(ActionSlot.Move, false); CheckSlot(ActionSlot.Free, true);
                // Claude's deferred scoring remains necessary for cooldown positioning / next-round gathering.
                var slotDeferred = (List<AbilityData>)AccessTools.Field(typeof(TacticalContext), "SlotDeferred").GetValue(ctx);
                slotDeferred.Clear();
                CheckSlot(ActionSlot.Standard, false);
                Require(slotDeferred.Count == 1, "slot-skipped ability was lost instead of deferred");
                availabilityReads = 0;
                var deferred = (IEnumerable<Candidate>)AccessTools.Method(typeof(TacticalEngine), "EvaluateDeferred")
                    .Invoke(null, new object[] { ctx, (Func<AbilityData, bool>)(_ => true) });
                Require(!deferred.Any() && availabilityReads == 1, "explicit deferred request did not revisit native availability");
                ctx.HasStandard = true;
                CheckSlot(ActionSlot.Move, true); CheckSlot(ActionSlot.Standard, true);
                ctx.Permissions = new ActionPermissions { MovementOnly = true };
                CheckSlot(ActionSlot.Free, false);

                // The native brain body has engine internal calls unavailable on .NET. Check its signature,
                // then verify Harmony binds our real diagnostic state pair to a stand-in with that signature.
                Require(AccessTools.Method(typeof(Kingmaker.AI.AiBrainController), "TickBrain",
                    new[] { typeof(Kingmaker.EntitySystem.Entities.UnitEntityData) }) != null, "native brain signature changed");
                harmony.Patch(AccessTools.Method(typeof(DecisionReadChecks), nameof(BrainShape)),
                    prefix: new HarmonyMethod(typeof(CompanionAI_Pathfinder.GameInterface.CustomBrainPatch), "TickBrain_Prefix"),
                    postfix: new HarmonyMethod(typeof(CompanionAI_Pathfinder.GameInterface.CustomBrainPatch), "TickBrain_Postfix"));
            }
            finally { activeRules = null; harmony.UnpatchAll(harmony.Id); }

            // A modded calculator uses the same native virtual dispatch as the single-class game calculator.
            var toggle = blueprints.OfType<BlueprintActivatableAbility>().First(b => b.Buff != null &&
                b.Buff.GetComponent<Kingmaker.UnitLogic.Class.Kineticist.AddKineticistBurnModifier>() != null &&
                b.Buff.GetComponent<ContextAbilityParamsCalculator>() != null);
            var original = toggle.Buff.ComponentsArray;
            try
            {
                var calculator = new MultiClassCalculator();
                toggle.Buff.ComponentsArray = original.Where(c => !(c is ContextAbilityParamsCalculator)).Concat(new[] { calculator }).ToArray();
                Require((bool)typeof(TacticalEngine).GetMethod("CanProjectModifier", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { toggle }), "modded native calculator was rejected");
                var context = (MechanicsContext)RuntimeHelpers.GetUninitializedObject(typeof(MechanicsContext));
                AccessTools.Field(typeof(MechanicsContext), "AssociatedBlueprint").SetValue(context, toggle.Buff);
                AccessTools.Method(typeof(MechanicsContext), "RecalculateAbilityParams").Invoke(context, new object[] { false });
                Require(context.Params.DC == 29 && context.Params.CasterLevel == 11, "native virtual calculator dispatch was bypassed");
            }
            finally { toggle.Buff.ComponentsArray = original; }
            var defaults = typeof(TacticalEngine).GetField("DefaultKineticGroups", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var value = defaults.GetType().GetProperty("Value");
            var firstDefault = (Kingmaker.UnitLogic.Parts.UnitPartActivatableAbility)value.GetValue(defaults);
            for (int i = 0; i < 100; i++) Require(ReferenceEquals(firstDefault, value.GetValue(defaults)), "default group array was reallocated");
            var liveGroups = new Kingmaker.UnitLogic.Parts.UnitPartActivatableAbility();
            var group = ActivatableAbilityGroup.SubstanceInfusion;
            int nativeDefault = firstDefault.GetGroupSize(group);
            liveGroups.IncreaseGroupSize(group);
            Require(liveGroups.GetGroupSize(group) != nativeDefault && firstDefault.GetGroupSize(group) == nativeDefault,
                "live group overrides leaked into default capacity");
            Console.WriteLine("DECISION READ CHECKS: rotating-empty-root 200 reads/1 native query; active-rule bypass; clone/aim/state isolation; shared native group defaults; brain state-pair binding; deferred scoring and calculator PASS");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Decision read check: " + message); }
    }
}
