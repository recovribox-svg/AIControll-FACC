using System;
using System.Reflection;

namespace CompanionAI_Pathfinder.Tactics
{
    // Optional hook contract, read without calling a plugin getter/patch/setup. UMM may rewrite the module identity;
    // verify the complete tiny IL bodies and resolved members instead of trusting a version, name or MVID alone.
    internal sealed class ToggleSpendOverride
    {
        internal readonly FieldInfo Settings, Infinite;
        private ToggleSpendOverride(FieldInfo settings, FieldInfo infinite) { Settings = settings; Infinite = infinite; }

        internal static ToggleSpendOverride Read(MethodInfo prefix)
        {
            try
            {
                if (prefix == null || !prefix.IsStatic || prefix.ReturnType != typeof(bool) || prefix.GetParameters().Length != 0 ||
                    prefix.Name != "Prefix" || prefix.DeclaringType.FullName !=
                    "ToyBox.BagOfPatches.Infinites+ActivatableAbilityResourceLogic_SpendResource_Patch") return null;
                if (HarmonyLib.Harmony.GetPatchInfo(prefix) != null) return null;
                var body = prefix.GetMethodBody(); var il = body?.GetILAsByteArray();
                if (body == null || body.LocalVariables.Count != 0 || body.ExceptionHandlingClauses.Count != 0 || il?.Length != 14 ||
                    il[0] != 0x28 || il[5] != 0x7b || il[10] != 0x16 || il[11] != 0xfe || il[12] != 0x01 || il[13] != 0x2a) return null;
                // call get_settings; ldfld toggleInfiniteAbilities; ldc.i4.0; ceq; ret
                var getter = prefix.Module.ResolveMethod(BitConverter.ToInt32(il, 1)) as MethodInfo;
                var flag = prefix.Module.ResolveField(BitConverter.ToInt32(il, 6));
                if (getter == null || !getter.IsStatic || getter.GetParameters().Length != 0 || getter.Name != "get_settings" ||
                    getter.DeclaringType.FullName != "ToyBox.BagOfPatches.Infinites" || getter.Module != prefix.Module ||
                    getter.ReturnType.FullName != "ToyBox.Settings" || flag.Name != "toggleInfiniteAbilities" || flag.IsStatic ||
                    flag.FieldType != typeof(bool) || flag.DeclaringType != getter.ReturnType) return null;
                if (HarmonyLib.Harmony.GetPatchInfo(getter) != null) return null;
                var getBody = getter.GetMethodBody(); var bytes = getBody?.GetILAsByteArray();
                if (getBody == null || getBody.LocalVariables.Count != 0 || getBody.ExceptionHandlingClauses.Count != 0 ||
                    bytes?.Length != 6 || bytes[0] != 0x7e || bytes[5] != 0x2a) return null;
                // ldsfld Main.Settings; ret
                var settings = getter.Module.ResolveField(BitConverter.ToInt32(bytes, 1));
                if (!settings.IsStatic || settings.Name != "Settings" || settings.DeclaringType.FullName != "ToyBox.Main" ||
                    settings.Module != prefix.Module || settings.FieldType != getter.ReturnType) return null;
                return new ToggleSpendOverride(settings, flag);
            }
            catch { return null; }
        }

        internal bool TryRead(out bool skipsSpend, Func<FieldInfo, object> readStatic = null)
        {
            skipsSpend = false;
            try
            {
                // Production reaches this only for an already applied hook in the initialized game. The offline fixture
                // supplies an uninitialized settings object through readStatic; it never runs ToyBox.Main's initializer.
                var settings = readStatic == null ? Settings.GetValue(null) : readStatic(Settings);
                if (settings == null || !Infinite.DeclaringType.IsInstanceOfType(settings)) return false;
                skipsSpend = (bool)Infinite.GetValue(settings);
                return true;
            }
            catch { return false; }
        }
    }
}
