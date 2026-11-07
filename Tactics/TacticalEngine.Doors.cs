// ★ v0.3.29 test 7 (v0.3.29 test 6 turn-based log, the author's note): a closed door between the party and an enemy. A
// Neophyte Wizard stood in the next room behind a closed door for rounds 3-6 of the second fight; every walk and spell at
// him was refused before issue ("path ends without line of sight to the target"), the advances ended at the door, and the
// companions ended their turns with Aid Another or nothing until the game ended the fight. The engine knew no way to open
// a door. It now offers "Open door" - the game's own interaction (UnitInteractWithObject, a standard action that walks
// within the door's proximity radius first, as the player's click does) - when a closed door stands on the way to an
// enemy the unit cannot see. Only doors the game lets anyone open now: enabled, openable in combat, no lock, key or skill
// check left (InteractionRestrictionPart unless already unlocked), no trap seen on them, revealed.
// ★ v0.3.29 test 8 (v0.3.29 test 7 log, turn-based and real time: "companions still could not open unlocked doors"): no
// door was ever offered. The test-7 filter asked for EntityDataBase.IsRevealed, which the game sets for a map object only
// with the fog of war off (FogOfWarController.Tick), for area exits (SceneLoader) and for traps - never for a door the
// party sees, so every door was dropped. A door is now seen by the game's own rule (EntityVisibilityForPlayerController:
// perception check passed, and revealed or out of the fog). Also: a lock the party has the key for, a story-unlocked one
// or one already picked passes (the restriction's own IsPassed; an unpicked lock is never tried - its Trickery roll can jam
// it); the door counts for an enemy out of sight or one this turn's walks could not reach (TurnMemory.Unreachable: the same
// fight's real-time walks ended at the door, 113.0/71.2, for 11 s); "on the way" is the detour through the door
// (DoorRules.OnTheWay), the least detour per enemy; no walk is needed when the unit stands within the door's radius already
// (so a unit boxed in at the door may still open it); and a once-a-fight line names why no door was offered.
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
        /// <summary>A closed door in the area and why the AI may not open it now (<see cref="Refusal"/> null: it may).</summary>
        private struct ClosedDoor
        {
            public InteractionDoorPart Door;
            public Vector3 At;
            public string Refusal;
        }

        private static List<ClosedDoor> s_ClosedDoors;
        private static double s_ClosedDoorsAt = -10d;

        /// <summary>The area's closed doors, each with why it may not be opened, read at most once a second.</summary>
        private static List<ClosedDoor> ClosedDoors()
        {
            double now = Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;
            if (s_ClosedDoors != null && Math.Abs(now - s_ClosedDoorsAt) < 1d) return s_ClosedDoors;
            var doors = new List<ClosedDoor>();
            try
            {
                foreach (var mapObject in Game.Instance.State.MapObjects)
                {
                    if (mapObject == null || !mapObject.IsInGame) continue;
                    var door = mapObject.Get<InteractionDoorPart>();
                    if (door == null || door.IsOpen || door.View == null) continue;
                    doors.Add(new ClosedDoor { Door = door, At = door.View.transform.position, Refusal = WhyNotOpenable(door) });
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

        /// <summary>
        /// Why a party member may not open <paramref name="door"/> now with a plain interaction (UnitInteractWithObject);
        /// null when it may.
        /// </summary>
        private static string WhyNotOpenable(InteractionDoorPart door)
        {
            try
            {
                if (door.IsOpen) return "open";
                if (door.Type != Kingmaker.View.MapObjects.InteractionType.Approach) return "not opened by walking up to it";
                if (!door.Enabled) return "disabled";
                if (!door.CanInteract()) return "not usable in combat";
                if (door.HasVisibleTrap()) return "trapped";
                // The game's own rule for showing a map object (EntityVisibilityForPlayerController.IsVisible).
                var owner = door.Owner;
                if (!owner.IsPerceptionCheckPassed) return "not found yet";
                if (!owner.IsRevealed && owner.IsInFogOfWar) return "in the fog of war";
                return door.AlreadyUnlocked ? null : LockRefusal(owner);
            }
            catch (Exception ex)
            {
                return "unreadable (" + ex.GetType().Name + ")";
            }
        }

        /// <summary>
        /// ★ v0.3.29 test 8: why the door's restrictions stop a plain opening, or null. A restriction the game counts as
        /// passed (InteractionRestrictionPart.IsPassed: the key in the party's inventory, a story-unlocked flag, a lock
        /// already picked) opens it. A lock not yet picked is never tried: its check (DisableDeviceRestrictionPart) rolls
        /// Trickery, can jam the lock and spends the action for nothing on a failure.
        /// </summary>
        private static string LockRefusal(MapObjectEntityData owner)
        {
            var restrictions = owner.Parts.GetAll<InteractionRestrictionPart>();
            if (restrictions == null || restrictions.Count == 0) return null;
            bool passed = false;
            string refusal = null;
            foreach (var restriction in restrictions)
            {
                if (restriction == null) continue;
                if (restriction.IsPassed) passed = true;
                else if (restriction is DisableDeviceRestrictionPart) return "locked (needs a Trickery check)";
                else refusal = refusal ?? (restriction is KeyRestrictionPart ? "locked (needs a key)" : $"restricted ({restriction.GetType().Name})");
            }
            return passed ? null : refusal ?? "restricted";
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

        /// <summary>Metres from a door within which the game's interaction starts without a walk (its proximity radius).</summary>
        private static float DoorRadius(InteractionDoorPart door)
        {
            try { return door.Settings.ProximityRadius > 0f ? door.Settings.ProximityRadius : 4f; }
            catch { return 4f; }
        }

        /// <summary>
        /// "Open door" options: for each enemy out of the unit's sight, or out of reach of this turn's walks
        /// (<see cref="TacticalContext.Unreachable"/>), the closed door the game lets the party open with the least detour on
        /// the way to it (DoorRules.OnTheWay), worth what an advance on that enemy is worth, a little more
        /// (DoorRules.OverAdvance: the advance cannot get past the door, the opening can, for the whole party); the most
        /// valuable enemy behind each door counts. The real path to the door is checked before issue like any walk
        /// (NeedsMove), unless the unit stands within the door's radius already.
        /// </summary>
        private static void AddOpenDoorCandidates(TacticalContext ctx, List<Candidate> list)
        {
            if (!ctx.Permissions.CanWalk || ctx.Permissions.MovementOnly || !ctx.HasStandard) return;
            var unit = ctx.Unit;
            List<ClosedDoor> doors = null;
            Dictionary<InteractionDoorPart, Candidate> best = null;
            foreach (var enemy in ctx.Enemies)
            {
                if (enemy == null || enemy.Descriptor?.State?.IsConscious != true) continue;
                bool unreachable = ctx.Unreachable != null && ctx.Unreachable.Contains(enemy.UniqueId);
                bool hidden = false;
                if (!unreachable)
                {
                    try
                    {
                        hidden = Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar.LineOfSightGeometry.Instance
                            .HasObstacle(unit.EyePosition, enemy.EyePosition, 0f);
                    }
                    catch { continue; }
                    if (!hidden) continue;
                }
                doors = doors ?? ClosedDoors();
                if (doors.Count == 0)
                {
                    if (unreachable) NoDoorNote(ctx, enemy, hidden, "no closed door in the area");
                    return;
                }
                var est = CombatMath.EstimateWeaponAttack(unit, enemy, false);
                float worth = est != null ? DamageValue(enemy, est) : 0f;
                if (!(worth > 0f)) continue;
                float value = worth * AdvanceShare + DoorRules.OverAdvance;

                // The door with the least detour; for the log, the nearest one refused and why.
                InteractionDoorPart chosen = null;
                Vector3 chosenAt = default;
                float least = float.MaxValue, refusedAt = float.MaxValue;
                string refused = null;
                foreach (var closed in doors)
                {
                    var door = closed.Door;
                    if (door.IsOpen) continue;   // opened since the list was read
                    Vector3 at = closed.At;
                    float near = HorizontalDistance(unit.Position, at);
                    string why = closed.Refusal;
                    float detour = float.NaN;
                    if (why == null && Math.Abs(at.y - unit.Position.y) > DoorRules.SameFloor && Math.Abs(at.y - enemy.Position.y) > DoorRules.SameFloor)
                        why = "on another floor";
                    if (why == null && !DoorRules.OnTheWay(unit.Position.x, unit.Position.z, enemy.Position.x, enemy.Position.z, at.x, at.z, out detour))
                        why = float.IsNaN(detour) ? "not on the way" : $"off the way ({detour:F1} m longer)";
                    if (why == null && AllyOpening(ctx, door)) why = "an ally is opening it";
                    bool walk = near > DoorRadius(door) - 0.3f;
                    if (why == null && walk && ctx.NoMovementLeft) why = "no movement left to walk to it";
                    if (why != null)
                    {
                        if (near < refusedAt) { refusedAt = near; refused = why; }
                        continue;
                    }
                    if (detour < least)
                    {
                        least = detour;
                        chosen = door;
                        chosenAt = at;
                    }
                }
                if (chosen == null)
                {
                    // Said when it matters: the walks to the enemy failed, or a door it might be stands near.
                    if (unreachable || refusedAt <= NoDoorNoteRange)
                        NoDoorNote(ctx, enemy, hidden, refused == null ? "no closed door on the way"
                                                                       : $"nearest closed door {refusedAt:F1} m away: {refused}");
                    continue;
                }
                if (best != null && best.TryGetValue(chosen, out var known) && known.Value >= value) continue;
                bool needsWalk = HorizontalDistance(unit.Position, chosenAt) > DoorRadius(chosen) - 0.3f;
                (best ?? (best = new Dictionary<InteractionDoorPart, Candidate>()))[chosen] = new Candidate
                {
                    Kind = CandidateKind.Interact,
                    Slot = ActionSlot.Standard,
                    Door = chosen,
                    Target = enemy,
                    NeedsMove = needsWalk,
                    CheckPath = needsWalk,
                    Value = value,
                    Label = $"Open door to {enemy.CharacterName}",
                    Key = $"door:{chosen.Owner.UniqueId}"
                };
            }
            if (best != null) list.AddRange(best.Values);
        }

        /// <summary>Metres within which a closed door the unit may not open is named for an enemy merely out of sight.</summary>
        private const float NoDoorNoteRange = 8f;

        /// <summary>For the log, once a fight per unit and enemy: an enemy out of sight or reach with no door to open.</summary>
        private static void NoDoorNote(TacticalContext ctx, UnitEntityData enemy, bool hidden, string why) =>
            ReportOnce($"no-door|{CombatMemory.Fight}|{ctx.Unit.UniqueId}|{enemy.UniqueId}",
                () => $"[Door] {ctx.Unit.CharacterName}: {enemy.CharacterName} is {(hidden ? "out of sight" : "out of reach")} - " +
                      $"no door to open ({why})");

        /// <summary>Open the door: the game's own interaction, walking within its proximity radius first.</summary>
        private static bool ExecuteOpenDoor(UnitEntityData unit, Candidate c, out Kingmaker.UnitLogic.Commands.Base.UnitCommand command,
                                            out string failure)
        {
            command = null;
            // Interacting with an open door closes it (InteractionDoorPart.Open switches it).
            string refusal = c.Door == null ? "no door" : WhyNotOpenable(c.Door);
            if (refusal != null)
            {
                failure = $"the door cannot be opened now ({refusal})";
                return false;
            }
            var open = new UnitInteractWithObject(c.Door);
            if (!GameInterface.CommandDispatcher.Run(unit, open, c.Label, out failure, avoidAreas: true)) return false;
            command = open;
            return true;
        }
    }
}
