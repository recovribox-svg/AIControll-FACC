// ★ v0.3.14: a unit's statuses as the game applies them, for both engines (docs/STATUS_AWARE_PLAN.md).
//
// Everything here is read from the game's own predicates (UnitState, UnitEntityData, UnitProneController), never from
// a table of conditions, so a mod that changes a condition's rules (Tabletop Tweaks' Staggered fix) is followed.
// The rule applied to an option is the game-independent Tactics/ActionRules.cs.
using System;
using System.Collections.Generic;
using Kingmaker.Controllers.Units;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class UnitStatus
    {
        /// <summary>Conditions that change what a unit can do this turn (named in the log).</summary>
        private static readonly UnitCondition[] Notable =
        {
            UnitCondition.Nauseated, UnitCondition.Staggered, UnitCondition.Dazed, UnitCondition.Stunned,
            UnitCondition.Paralyzed, UnitCondition.Petrified, UnitCondition.Helpless, UnitCondition.Sleeping,
            UnitCondition.Unconscious, UnitCondition.Prone, UnitCondition.CantMove, UnitCondition.MovementBan,
            UnitCondition.CantAct, UnitCondition.Confusion, UnitCondition.Frightened, UnitCondition.Cowering,
            UnitCondition.Entangled, UnitCondition.Blindness, UnitCondition.Slowed, UnitCondition.ForceMove,
            UnitCondition.CanNotAttack, UnitCondition.UseAbilityForbidden, UnitCondition.SpellcastingForbidden,
            UnitCondition.CantUseStandardActions
        };

        /// <summary>
        /// "nauseated, prone (can stand up)" - the unit's notable conditions and states, empty when there are none.
        /// Every "cannot act" line names them: 93 such lines in six turn-based logs could not be traced to a cause.
        /// </summary>
        public static string Describe(UnitEntityData unit)
        {
            var state = unit?.Descriptor?.State;
            if (state == null) return "";
            var parts = new List<string>();
            try
            {
                foreach (var condition in Notable)
                    if (state.HasCondition(condition)) parts.Add(condition.ToString().ToLowerInvariant());
                if (state.Prone.Active && !state.HasCondition(UnitCondition.Prone))
                    parts.Add(CanStandUpOnCommand(unit) ? "lying down (can stand up)" : "lying down");
                if (state.IsPanicked) parts.Add("panicked");
                if (unit.View != null && unit.View.IsGetUp) parts.Add("getting up");
                if (!state.CanAct && parts.Count == 0) parts.Add("busy (animation)");
            }
            catch (Exception ex)
            {
                parts.Add($"status unreadable: {ex.Message}");
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Turn-based: the unit is lying down, may stand up, and the game waits for an order to stand it up. This is
        /// the game's own test for the "get up" action (TurnController.UnitCanGetUpOnCommand), and like
        /// UnitEntityData.IsAbleToAct it counts such a unit as able to act although UnitState.CanAct is false while it
        /// lies down. UnitProneController only stands a directly controlled unit up while it has a command; in real time
        /// it stands up by itself after 3 seconds.
        /// </summary>
        public static bool CanStandUpOnCommand(UnitEntityData unit)
        {
            try
            {
                var state = unit?.Descriptor?.State;
                return state != null && unit.IsDirectlyControllable && state.Prone.Active &&
                       !UnitProneController.ShouldBeProne(unit) && state.CanStandUp &&
                       TurnBased.Controllers.CombatController.IsInTurnBasedCombat();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>What the unit's statuses allow its options to do now (see ActionRules.Refusal).</summary>
        public static ActionPermissions Permissions(UnitEntityData unit, bool turnBased)
        {
            var state = unit?.Descriptor?.State;
            if (state == null) return ActionPermissions.None;
            var p = new ActionPermissions();
            var notes = new List<string>();
            try
            {
                p.CanWalk = state.CanMove;
                // UnitCommandController.ShouldStartCommand: while nauseated only UnitMoveTo (and area transitions) start.
                p.MovementOnly = state.HasCondition(UnitCondition.Nauseated);
                // ★ v0.3.14 (test5): turn-based, the game says the unit cannot act (an animation such as nausea's holds it)
                // but may still walk: TurnBasedController only decides then after a second of waiting, so a short
                // animation after an attack never gets here. Real time waits for the animation instead.
                bool busyButMobile = turnBased && !state.CanAct && state.CanMove;
                if (busyButMobile) notes.Add(p.MovementOnly ? "nauseated: move only" : "busy: move only");
                p.MovementOnly |= busyButMobile;
                // Real time has no such limit on walking: UnitMoveTo ignores action cooldowns there.
                p.OneActionOnly = turnBased && unit.IsMoveActionRestricted();
                if (p.MovementOnly && !busyButMobile) notes.Add("nauseated: move only");
                if (!p.CanWalk) notes.Add("cannot move");
                if (p.OneActionOnly) notes.Add(unit.Descriptor.State.HasCondition(UnitCondition.Staggered)
                    ? "staggered: one action" : "one action");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Status] {unit.CharacterName}: {ex.Message}");
                return ActionPermissions.None;
            }
            p.Notes = string.Join(", ", notes);
            return p;
        }
    }
}
