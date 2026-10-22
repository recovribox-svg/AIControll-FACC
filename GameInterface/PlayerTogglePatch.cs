// ★ v0.3.19: the player's own toggle clicks during a fight (setting "Leave a toggle alone for the rest of the fight once
// you switch it yourself").
//
// The action bar switches a toggle in MechanicActionBarSlotActivableAbility.OnClick (and, for an item's toggle,
// MechanicActionBarSlotItem.OnClick): ActivatableAbility.IsOn = !IsOn. Those are the player's clicks and nothing else;
// the AI sets IsOn directly. A click that changed the toggle while its owner is in combat is noted in CombatMemory, and
// the engine leaves that toggle alone until the fight ends. Only noted - the click itself is never changed.
using System;
using HarmonyLib;
using Kingmaker.UI.UnitSettings;
using Kingmaker.UnitLogic.ActivatableAbilities;

namespace CompanionAI_Pathfinder.GameInterface
{
    [HarmonyPatch(typeof(MechanicActionBarSlotActivableAbility), nameof(MechanicActionBarSlotActivableAbility.OnClick))]
    public static class PlayerToggleSlotPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MechanicActionBarSlotActivableAbility __instance, out bool __state) =>
            __state = PlayerToggles.Before(__instance?.ActivatableAbility);

        [HarmonyPostfix]
        public static void Postfix(MechanicActionBarSlotActivableAbility __instance, bool __state) =>
            PlayerToggles.After(__instance?.ActivatableAbility, __state);
    }

    [HarmonyPatch(typeof(MechanicActionBarSlotItem), nameof(MechanicActionBarSlotItem.OnClick))]
    public static class PlayerToggleItemPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MechanicActionBarSlotItem __instance, out bool __state) =>
            __state = PlayerToggles.Before(__instance?.Item?.ActivatableAbility);

        [HarmonyPostfix]
        public static void Postfix(MechanicActionBarSlotItem __instance, bool __state) =>
            PlayerToggles.After(__instance?.Item?.ActivatableAbility, __state);
    }

    internal static class PlayerToggles
    {
        public static bool Before(ActivatableAbility toggle)
        {
            try { return toggle?.IsOn ?? false; } catch { return false; }
        }

        public static void After(ActivatableAbility toggle, bool wasOn)
        {
            try
            {
                if (toggle?.Blueprint == null || !Main.Enabled || toggle.IsOn == wasOn) return;
                var unit = toggle.Owner?.Unit;
                // The party's fight (a companion that has not joined it yet in real time is in it too).
                if (unit == null || !(unit.IsInCombat || (Kingmaker.Game.Instance?.Player?.IsInCombat ?? false))) return;
                Tactics.CombatMemory.NotePlayerToggle(unit, toggle);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Toggle] player click: {ex.Message}");
            }
        }
    }
}
