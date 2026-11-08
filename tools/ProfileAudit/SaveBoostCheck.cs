// ★ v0.3.30 competence programme: compiled production call-path checks, without initialized actors/native casts.
// These complement the independent roll-enumeration tests; they do not prove native actor state or gameplay behavior.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CompanionAI_Pathfinder.Tactics;

namespace ProfileAudit
{
    internal static class SaveBoostCheck
    {
        internal static List<string> Run(out int failures)
        {
            var lines = new List<string>();
            int bad = 0, checks = 0;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo Method(Type type, string name, int count) => type.GetMethods(flags)
                .SingleOrDefault(m => m.Name == name && m.GetParameters().Length == count);
            void Calls(MethodInfo caller, MethodInfo callee, string route)
            {
                checks++;
                try
                {
                    if (caller == null || callee == null || !CalledMethods(caller).Any(m =>
                        m.Module == callee.Module && m.MetadataToken == callee.MetadataToken))
                    {
                        bad++;
                        lines.Add($"SAVE-BOOST FAILED: compiled route {route} missing");
                    }
                }
                catch (Exception ex)
                {
                    bad++;
                    lines.Add($"SAVE-BOOST FAILED: inspect {route}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            var boost = Method(typeof(TacticalEngine), "BoostGain", 5);
            var marginal = Method(typeof(CombatMath), "DcBoostShare", 7);
            var effect = Method(typeof(CombatMath), "EffectChanceCore", 6);
            var factor = Method(typeof(CombatMath), "SaveEffectFactor", 5);
            // There is also a four-argument MechanicsContext save overload; select the actual DC-offset ability route.
            // ★ v0.3.30 competence2: five parameters since the disbelief save opts out of Persistent.
            var dcSave = typeof(CombatMath).GetMethods(flags).SingleOrDefault(m => m.Name == "SaveFailChance" &&
                m.GetParameters().Length == 5 && m.GetParameters()[0].ParameterType.Name == "AbilityData");
            var pureFactor = Method(typeof(ValueModel), "SaveEffectFactor", 4);
            var pureSave = Method(typeof(ValueModel), "SpellSaveFailChance", 4);
            Calls(boost, marginal, "BoostGain -> DcBoostShare");
            Calls(marginal, dcSave, "DcBoostShare -> actual-DC-offset save forecast");
            Calls(marginal, pureFactor, "DcBoostShare -> shared save outcome factor");
            Calls(marginal, Method(typeof(ValueModel), "SaveBoostShare", 2), "DcBoostShare -> marginal share");
            Calls(effect, factor, "ordinary EffectChance -> save factor");
            Calls(factor, pureFactor, "ordinary save factor -> shared outcome factor");
            Calls(dcSave, pureSave, "actual DC -> d20/Persistent math");
            Calls(pureSave, Method(typeof(ValueModel), "SaveFailChance", 2), "spell save -> existing d20 limits");
            var shadow = Method(typeof(CombatMath), "ShadowShare", 4);
            Calls(marginal, shadow, "DcBoostShare -> disbelief before/after factors");
            Calls(shadow, dcSave, "shadow disbelief -> actual-DC-offset Will save");
            Calls(shadow, Method(typeof(ValueModel), "ShadowEffectFactor", 2), "shadow disbelief -> shared reality math");
            Calls(effect, Method(typeof(CombatMath), "ShadowShare", 2), "ordinary EffectChance -> shadow share");
            Calls(Method(typeof(CombatMath), "ShadowShare", 3), shadow, "ordinary shadow route -> shared disbelief forecast");
            // ★ v0.3.30 competence2 (PR #20 review): free Persistent on the next spell uses the same save-effect factor.
            var persistent = Method(typeof(CombatMath), "PersistentBoostShare", 6);
            Calls(Method(typeof(TacticalEngine), "MetamagicGain", 5), persistent, "MetamagicGain -> Persistent save-factor share");
            Calls(persistent, Method(typeof(ValueModel), "PersistentBoostShare", 4), "Persistent share -> shared pure math");
            Calls(persistent, pureFactor, "Persistent share -> shared save outcome factor");
            lines.Insert(0, $"SAVE-BOOST: {checks} compiled call-path checks, failures {bad}; actor/native outcome NOT TESTED");
            failures = bad;
            return lines;
        }

        private static IEnumerable<MethodBase> CalledMethods(MethodInfo method)
        {
            var bytes = method.GetMethodBody()?.GetILAsByteArray() ?? throw new InvalidOperationException("No IL body");
            var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
                .GroupBy(c => unchecked((ushort)c.Value)).ToDictionary(g => g.Key, g => g.First());
            for (int offset = 0; offset < bytes.Length;)
            {
                ushort value = bytes[offset++];
                if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
                var op = codes[value];
                if (op.OperandType == OperandType.InlineMethod)
                    yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset));
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: offset++; break;
                    case OperandType.InlineVar: offset += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: offset += 8; break;
                    case OperandType.InlineSwitch: offset += 4 + 4 * BitConverter.ToInt32(bytes, offset); break;
                    default: offset += 4; break;
                }
            }
        }
    }
}
