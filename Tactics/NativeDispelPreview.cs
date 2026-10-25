// ★ v0.3.22: let native dispel eligibility/bonus/DC preparation run, stopping BEFORE rolls, events, or removal.
// Layout changes and foreign patches disable this preview. Nothing is dispelled to ask its probability.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    internal static class NativeDispelPreview
    {
        [ThreadStatic] private static ContextActionDispelMagic action;
        [ThreadStatic] private static RuleDispelMagic captured;
        [ThreadStatic] private static RuleDispelMagic preparing;
        private static bool actionHook, ruleHook;
        internal static bool HooksReady => actionHook && ruleHook;
        private static readonly MethodInfo TryBuff = AccessTools.Method(typeof(ContextActionDispelMagic), "TryDispelBuff");
        private static readonly MethodInfo Prepare = AccessTools.Method(typeof(RuleDispelMagic), nameof(RuleDispelMagic.OnTrigger));

        internal static void Disable() { actionHook = ruleHook = false; }
        internal static void InstallPatches(Harmony harmony, Action before)
        {
            Disable();
            KineticPreview.Patch(harmony, TryBuff, typeof(EligibilityGate), "Transpiler", before);
            KineticPreview.Patch(harmony, Prepare, typeof(PreparationGate), "Transpiler", before);
            if (!HooksReady) throw new InvalidOperationException("native dispel layout not recognized");
        }

        private static bool NoForeignPatches(MethodInfo method) => method != null &&
            !(Harmony.GetPatchInfo(method)?.Owners.Any(id => id != KineticPreview.PatchOwner) ?? false);
        internal static bool CanQuery => HooksReady && NoForeignPatches(TryBuff) && NoForeignPatches(Prepare);

        internal static List<Buff> OrderedBuffs(UnitEntityData target)
        {
            var buffs = target.Buffs.Enumerable.ToList();
            // Use the game's own comparison from RunAction, rather than copying its ordering rule.
            var comparison = typeof(ContextActionDispelMagic).GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .SingleOrDefault(m => m.ReturnType == typeof(int) && m.Name.Contains("<RunAction>") &&
                    m.GetParameters().Length == 2 && m.GetParameters().All(p => p.ParameterType == typeof(Buff)));
            if (comparison == null) return null;
            object receiver = comparison.IsStatic ? null : AccessTools.Field(comparison.DeclaringType, "<>9")?.GetValue(null);
            if (!comparison.IsStatic && receiver == null) return null;
            buffs.Sort((Comparison<Buff>)Delegate.CreateDelegate(typeof(Comparison<Buff>), receiver, comparison));
            return buffs;
        }

        internal static bool TryChance(ContextActionDispelMagic source, MechanicsContext context, UnitEntityData target,
                                       Buff buff, out float chance)
        {
            chance = 0f;
            if (!CanQuery || context?.MaybeCaster == null || target == null || buff == null) return false;
            var previousAction = action;
            var previousCaptured = captured;
            var previousPreparing = preparing;
            try
            {
                action = source;
                captured = null;
                // TryDispelBuff owns all native eligibility checks, limits, schools and bonuses. Our gate returns
                // before TriggerRule and before either OnSuccess/OnFail can run, including failed-dispel triggers.
                using (context.GetDataScope(new TargetWrapper(target)))
                    TryBuff.Invoke(source, new object[] { buff, 0 });
                if (captured == null) return true; // native eligibility rejected this buff
                if (captured.Check != RuleDispelMagic.CheckType.CasterLevel) return false;
                captured.Reason = context;
                captured.DisableBattleLogSelf = true;
                preparing = captured;
                captured.OnTrigger(null); // native DC/CL preparation; gate returns before Dice.D20
                if (captured.CheckRoll != null) throw new InvalidOperationException("Dispel preview reached a roll");
                int successes = 0;
                for (int roll = 1; roll <= 20; roll++) if (captured.IsSuccessRoll(roll)) successes++;
                chance = successes / 20f;
                return true;
            }
            finally { action = previousAction; captured = previousCaptured; preparing = previousPreparing; }
        }

        internal static bool CanQueryFor(UnitEntityData caster, UnitEntityData target)
        {
            if (!CanQuery || caster == null || target == null) return false;
            // This rule has no IsFake guard. Do not run unknown rulebook handlers just to appraise a dispel.
            if (!HasRuleHandlers(caster) && !HasRuleHandlers(target)) return true;
            AbilityDiscovery.ReportOnce($"kinetic-dispel-handlers|{caster.UniqueId}|{target.UniqueId}",
                $"[Kinetic] {caster.CharacterName}: dispel contribution 0 against {target.CharacterName}; rulebook handlers cannot safely be previewed");
            return false;
        }

        private static bool HasRuleHandlers(UnitEntityData unit) => unit.Facts.List.Any(f => f?.Active == true &&
            f.Blueprint.ComponentsArray.Any(c => c != null && c.GetType().GetInterfaces().Any(i => i.IsGenericType &&
                i.GetGenericArguments().Contains(typeof(RuleDispelMagic)))));

        private static bool Capturing(ContextActionDispelMagic source) => action != null && action == source;
        private static void Capture(RuleDispelMagic rule) { captured = rule; }
        private static bool Prepared(RuleDispelMagic rule) => preparing != null && preparing == rule;

        private static void MoveLabels(CodeInstruction from, CodeInstruction to)
        {
            to.labels.AddRange(from.labels);
            from.labels.Clear();
        }

        private static class EligibilityGate
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var code = instructions.ToList();
                int call = code.FindIndex(c => c.operand is MethodInfo m && m.Name == "TriggerRule" &&
                    m.IsGenericMethod && m.GetGenericArguments().SequenceEqual(new[] { typeof(RuleDispelMagic) }));
                if (call < 3 || code[call - 3].opcode != OpCodes.Ldarg_0 ||
                    !(code[call - 2].operand is MethodInfo getter) || getter.Name != "get_Context" ||
                    !code[call - 1].opcode.Name.StartsWith("ldloc")) return code;
                int at = call - 3;
                var continuation = generator.DefineLabel();
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                MoveLabels(code[at], first);
                code[at].labels.Add(continuation);
                code.InsertRange(at, new[]
                {
                    first, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeDispelPreview), nameof(Capturing))),
                    new CodeInstruction(OpCodes.Brfalse, continuation),
                    new CodeInstruction(code[call - 1].opcode, code[call - 1].operand),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeDispelPreview), nameof(Capture))),
                    new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Ret)
                });
                actionHook = true;
                return code;
            }
        }

        private static class PreparationGate
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var code = instructions.ToList();
                int call = code.FindIndex(c => c.operand is MethodInfo m && m.Name == "get_D20" && m.DeclaringType == typeof(RulebookEvent.Dice));
                if (call < 1 || code[call - 1].opcode != OpCodes.Ldarg_0) return code;
                int at = call - 1;
                var continuation = generator.DefineLabel();
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                MoveLabels(code[at], first);
                code[at].labels.Add(continuation);
                code.InsertRange(at, new[]
                {
                    first, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeDispelPreview), nameof(Prepared))),
                    new CodeInstruction(OpCodes.Brfalse, continuation), new CodeInstruction(OpCodes.Ret)
                });
                ruleHook = true;
                return code;
            }
        }
    }
}
