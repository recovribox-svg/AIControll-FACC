// ★ v0.3.29 test 7 (v0.3.29 test 6 turn-based log, the author's note): a closed door between the party and an enemy. A
// Neophyte Wizard stood in the next room behind a closed door for rounds 3-6 of the second fight; every walk and spell at
// him was refused before issue ("path ends without line of sight to the target"), the advances ended at the door, and the
// companions ended their turns with Aid Another or nothing until the game ended the fight. The engine knew no way to open
// a door. It now offers "Open door" - the game's own interaction (UnitInteractWithObject, a standard action that walks
// within the door's proximity radius first, as the player's click does) - when a closed door stands on the way to an
// enemy the unit cannot see. Only doors the game lets anyone open now: enabled, openable in combat, no lock, key or skill
// check left (InteractionRestrictionPart unless already unlocked), no trap seen on them, revealed.
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.View.MapObjects;
using Kingmaker.View.MapObjects.InteractionRestrictions;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static List<InteractionDoorPart> s_ClosedDoors;
        private static double s_ClosedDoorsAt = -10d;

        /// <summary>The closed doors anyone may open now, read at most once a second (the area's map objects).</summary>
        private static List<InteractionDoorPart> ClosedDoors()
        {
            double now = Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;
            if (s_ClosedDoors != null && Math.Abs(now - s_ClosedDoorsAt) < 1d) return s_ClosedDoors;
            var doors = new List<InteractionDoorPart>();
            try
            {
                foreach (var mapObject in Game.Instance.State.MapObjects)
                {
                    if (mapObject == null || !mapObject.IsInGame || !mapObject.IsRevealed) continue;
                    var door = mapObject.Get<InteractionDoorPart>();
                    if (door == null || !Openable(door)) continue;
                    doors.Add(door);
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] doors: {ex.Message}");
            }
            s_ClosedDoors = doors;
            s_ClosedDoorsAt = now;
            return doors;
        }

        /// <summary>A closed door the game lets a party member open now with a plain interaction (UnitInteractWithObject).</summary>
        private static bool Openable(InteractionDoorPart door)
        {
            try
            {
                if (door.IsOpen || door.Type != Kingmaker.View.MapObjects.InteractionType.Approach || !door.CanInteract()) return false;
                if (door.View == null || door.HasVisibleTrap()) return false;
                // A lock, a key or a skill check: not opened by the AI (a failed check spends the action for nothing).
                return door.AlreadyUnlocked || door.Owner.Get<InteractionRestrictionPart>() == null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Another party member is already on its way to open <paramref name="door"/> (an interaction not finished).</summary>
        private static bool AllyOpening(TacticalContext ctx, InteractionDoorPart door)
        {
            foreach (var ally in ctx.Allies)
            {
                if (ally == null || ally == ctx.Unit || ally.Commands?.Raw == null) continue;
                foreach (var command in ally.Commands.Raw)
                    if (command is UnitInteractWithObject open && !open.IsFinished && open.Interaction == door) return true;
            }
            return false;
        }

        /// <summary>
        /// "Open door" options: for each closed door on the way (DoorRules.OnTheWay) to an enemy the unit has no sight of,
        /// worth what an advance on that enemy is worth, a little more (DoorRules.OverAdvance: the advance cannot get past the
        /// door, the opening can, for the whole party); the most valuable enemy behind each door counts. The real path to the
        /// door is checked before issue like any walk (NeedsMove).
        /// </summary>
        private static void AddOpenDoorCandidates(TacticalContext ctx, List<Candidate> list)
        {
            if (!ctx.Permissions.CanWalk || ctx.Permissions.MovementOnly || !ctx.HasStandard || ctx.NoMovementLeft) return;
            var unit = ctx.Unit;
            List<InteractionDoorPart> doors = null;
            Dictionary<InteractionDoorPart, Candidate> best = null;
            foreach (var enemy in ctx.Enemies)
            {
                if (enemy == null || enemy.Descriptor?.State?.IsConscious != true) continue;
                bool hidden;
                try
                {
                    hidden = Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar.LineOfSightGeometry.Instance
                        .HasObstacle(unit.EyePosition, enemy.EyePosition, 0f);
                }
                catch { continue; }
                if (!hidden) continue;
                doors = doors ?? ClosedDoors();
                if (doors.Count == 0) return;
                var est = CombatMath.EstimateWeaponAttack(unit, enemy, false);
                float worth = est != null ? DamageValue(enemy, est) : 0f;
                if (!(worth > 0f)) continue;
                float value = worth * AdvanceShare + DoorRules.OverAdvance;
                foreach (var door in doors)
                {
                    if (door.IsOpen) continue;   // opened since the list was read
                    Vector3 at = door.View.transform.position;
                    if (Math.Abs(at.y - unit.Position.y) > DoorRules.SameFloor && Math.Abs(at.y - enemy.Position.y) > DoorRules.SameFloor) continue;
                    if (!DoorRules.OnTheWay(unit.Position.x, unit.Position.z, enemy.Position.x, enemy.Position.z, at.x, at.z)) continue;
                    if (best != null && best.TryGetValue(door, out var known) && known.Value >= value) continue;
                    if (AllyOpening(ctx, door)) continue;
                    (best ?? (best = new Dictionary<InteractionDoorPart, Candidate>()))[door] = new Candidate
                    {
                        Kind = CandidateKind.Interact,
                        Slot = ActionSlot.Standard,
                        Door = door,
                        Target = enemy,
                        NeedsMove = true,
                        CheckPath = true,
                        Value = value,
                        Label = $"Open door to {enemy.CharacterName}",
                        Key = $"door:{door.Owner.UniqueId}"
                    };
                }
            }
            if (best != null) list.AddRange(best.Values);
        }

        /// <summary>Open the door: the game's own interaction, walking within its proximity radius first.</summary>
        private static bool ExecuteOpenDoor(UnitEntityData unit, Candidate c, out Kingmaker.UnitLogic.Commands.Base.UnitCommand command,
                                            out string failure)
        {
            command = null;
            // Interacting with an open door closes it (InteractionDoorPart.Open switches it).
            if (!Openable(c.Door))
            {
                failure = "the door is open already or cannot be opened now";
                return false;
            }
            var open = new UnitInteractWithObject(c.Door);
            if (!GameInterface.CommandDispatcher.Run(unit, open, c.Label, out failure, avoidAreas: c.CheckPath)) return false;
            command = open;
            return true;
        }
    }
}
