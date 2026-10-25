// ★ v0.3.22: ask SpendAction on isolated cooldown copies. Never spend an action on the real unit to preview it.
using System;
using HarmonyLib;
using Kingmaker.Controllers.Combat;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands.Base;

namespace CompanionAI_Pathfinder.Tactics
{
    internal static class NativeActionCost
    {
        private static readonly System.Reflection.MethodInfo Spend = AccessTools.Method(typeof(UnitEntityData), nameof(UnitEntityData.SpendAction));
        private static readonly System.Reflection.MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static readonly System.Reflection.FieldInfo State = AccessTools.Field(typeof(UnitEntityData), "<CombatState>k__BackingField");

        internal static bool CanQuery => Spend != null && State != null &&
            !(Harmony.GetPatchInfo(Spend)?.Owners?.Count > 0);

        internal static bool TrySeconds(UnitEntityData unit, UnitCommand.CommandType type, bool fullRound, out float seconds)
        {
            seconds = 0f;
            if (!CanQuery || unit?.CombatState == null || !unit.IsInCombat) return false;
            // Verified native SpendAction only reads combat mode and writes this state's cooldown values.
            // A patched implementation may have other effects; refuse the projection instead of assuming purity.
            var shadow = (UnitEntityData)Clone.Invoke(unit, null);
            var state = (UnitCombatState)Clone.Invoke(unit.CombatState, null);
            AccessTools.Field(typeof(UnitCombatState), nameof(UnitCombatState.Cooldown)).SetValue(state, new UnitCombatState.Cooldowns());
            State.SetValue(shadow, state);
            shadow.SpendAction(type, fullRound, 0f);
            seconds = Math.Max(state.Cooldown.MoveAction, Math.Max(state.Cooldown.StandardAction, state.Cooldown.SwiftAction));
            return seconds > 0f || type == UnitCommand.CommandType.Free;
        }
    }
}
