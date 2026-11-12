using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.UnitLogic.ActivatableAbilities;

namespace ToyBoxSpendNative
{
    internal static class Program
    {
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        private static bool UnknownPrefix() => true;
        private static int Main(string[] args)
        {
            if (args.Length < 3 || !File.Exists(args[0]))
            { Console.Error.WriteLine("ToyBoxSpend.Native <supplied ToyBox.dll> <export roots...> <report.txt>"); return 2; }
            int audit = (int)typeof(ProfileAudit.Program).GetMethod("Main", Static).Invoke(null, new object[] { args.Skip(1).ToArray() });
            if (audit != 0) return audit;
            var provider = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var prefix = provider.GetType("ToyBox.BagOfPatches.Infinites+ActivatableAbilityResourceLogic_SpendResource_Patch", true).GetMethod("Prefix");
            var type = typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics.ToggleSpendOverride", true);
            var read = type.GetMethod("Read", Static);
            var quote = type.GetMethod("TryRead", BindingFlags.Instance | BindingFlags.NonPublic);
            var model = read.Invoke(null, new object[] { prefix });
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string label) { checks++; if (!ok) { bad++; lines.Add("TOYBOX-SPEND FAILED: " + label); } }
            Check(model != null, "actual supplied prefix/getter IL and resolved fields bind");
            if (model == null) { foreach (var l in lines) Console.WriteLine(l); return 1; }
            var field = (FieldInfo)type.GetField("Infinite", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(model);
            var settings = RuntimeHelpers.GetUninitializedObject(field.DeclaringType);
            bool Quote(object value, out bool skip)
            {
                var a = new object[] { false, new Func<FieldInfo, object>(_ => value) };
                bool known = (bool)quote.Invoke(model, a); skip = (bool)a[0]; return known;
            }
            Check(Quote(settings, out bool off) && !off, "disabled infinite switch leaves native spend");
            field.SetValue(settings, true);
            Check(Quote(settings, out bool on) && on, "enabled infinite switch cancels native spend");
            field.SetValue(settings, false);
            Check(Quote(settings, out bool fresh) && !fresh, "same contract reads changed setting afresh");
            Check(!Quote(null, out _), "missing live settings is unknown");
            Check(!Quote(new object(), out _), "wrong settings object is unknown");
            Check(read.Invoke(null, new object[] { typeof(Program).GetMethod("UnknownPrefix", Static) }) == null,
                "uninspected callback is rejected");

            // Metadata only: do not apply a detour, invoke a plugin getter, read Main.Settings or run its initializer.
            var harmony = typeof(HarmonyLib.Harmony).Assembly;
            var shared = harmony.GetType("HarmonyLib.HarmonySharedState", true);
            var state = (Dictionary<MethodBase, byte[]>)HarmonyLib.AccessTools.Field(shared, "state").GetValue(null);
            var serialize = HarmonyLib.AccessTools.Method(harmony.GetType("HarmonyLib.PatchInfoSerialization", true), "Serialize");
            var native = HarmonyLib.AccessTools.Method(typeof(ActivatableAbilityResourceLogic), "SpendResource");
            var wrong = HarmonyLib.AccessTools.Method(typeof(ActivatableAbilityResourceLogic),
                "Kingmaker.UnitLogic.ActivatableAbilities.IActivatableAbilitySpendResourceLogic.OnNewRound");
            byte[] saved, savedWrong;
            lock (state) { state.TryGetValue(native, out saved); state.TryGetValue(wrong, out savedWrong); }
            var metadata = new HarmonyLib.PatchInfo();
            HarmonyLib.AccessTools.Method(typeof(HarmonyLib.PatchInfo), "AddPrefixes").Invoke(metadata, new object[] {
                "CompanionAI.ToyBox.NativeFixture", new[] { new HarmonyLib.HarmonyMethod(prefix) } });
            var contract = typeof(TacticalEngine).GetMethod("ToggleSpendContractFor", Static);
            bool Known()
            {
                var c = contract.Invoke(null, new object[] { new TacticalContext() });
                return (bool)c.GetType().GetField("Known").GetValue(c);
            }
            try
            {
                var bytes = (byte[])serialize.Invoke(null, new object[] { metadata });
                lock (state) state[native] = bytes;
                Check(Known(), "actual spend prefix metadata is scoped to the native spend method");
                lock (state) { if (saved == null) state.Remove(native); else state[native] = saved; state[wrong] = bytes; }
                Check(!Known(), "same callback on a different native method is not accepted");
            }
            finally
            {
                lock (state)
                { if (saved == null) state.Remove(native); else state[native] = saved;
                  if (savedWrong == null) state.Remove(wrong); else state[wrong] = savedWrong; }
            }
            Check(Known(), "native hook metadata is restored");
            lines.Insert(0, $"TOYBOX-SPEND {checks} checks, failures {bad}; exact supplied IL/fields and injected settings data; no plugin getter/setup, static Main settings, actor or patch wrapper executed");
            foreach (var l in lines) Console.WriteLine(l);
            File.AppendAllText(args[args.Length - 1], Environment.NewLine + string.Join(Environment.NewLine, lines));
            return bad == 0 ? 0 : 1;
        }
    }
}
