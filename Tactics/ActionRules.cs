// ★ v0.3.14: which options a unit's current statuses still let the game start (no game types, unit-tested in tests/).
//
// The tactical engine reads its action economy from the game (HasStandardAction etc. already include Nauseated,
// Staggered and the surprise round), but three game rules decide whether a command *starts* at all, and the engine
// kept offering options they refuse (docs/STATUS_AWARE_PLAN.md, finding F4):
//   - Nauseated: UnitCommandController.ShouldStartCommand starts only UnitMoveTo; swift actions, move-action
//     abilities, toggles and attacks wait (real time) or are force-finished unstarted (turn-based).
//   - Staggered (UnitEntityData.IsMoveActionRestricted): one move OR one standard action, so "walk there and act"
//     spends the only action on the walk.
//   - CantMove / MovementBan / helpless (UnitState.CanMove false): nothing that needs walking.
// Tactics/UnitStatus.cs fills ActionPermissions from the game; this file is the rule applied to one option.
namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>What the unit's statuses allow right now, as read from the game (Tactics/UnitStatus.cs).</summary>
    public sealed class ActionPermissions
    {
        /// <summary>The unit can walk (UnitState.CanMove).</summary>
        public bool CanWalk = true;
        /// <summary>Only plain movement starts (Nauseated).</summary>
        public bool MovementOnly;
        /// <summary>Turn-based: one move or one standard action, never both (Staggered).</summary>
        public bool OneActionOnly;
        /// <summary>For the log, e.g. "nauseated: move only". Empty when nothing restricts the unit.</summary>
        public string Notes = "";

        public static readonly ActionPermissions None = new ActionPermissions();
    }

    public static class ActionRules
    {
        /// <summary>
        /// Why the game would not start an option of this shape, or null when it would.
        /// </summary>
        /// <param name="plainMove">The option is a plain UnitMoveTo (a move, step, withdraw, advance).</param>
        /// <param name="walks">The option walks: a plain move, or an attack/ability that has to get closer first.</param>
        /// <param name="mainAction">The option spends a standard or full-round action.</param>
        public static string Refusal(ActionPermissions p, bool plainMove, bool walks, bool mainAction)
        {
            if (p == null) return null;
            if (p.MovementOnly && !plainMove) return "only movement starts (nauseated)";
            if (!p.CanWalk && walks) return "cannot move";
            if (p.OneActionOnly && walks && mainAction) return "one action only (staggered): cannot move and act";
            return null;
        }
    }
}
