# Opening doors in combat - saved for a future version

Status: **taken out of v0.3.29 at the author's request (7 November 2026)**. The code is saved here, not lost. It
shipped only in development test builds 7 and 8 (`docs/TACTICAL_AI.md` §119-§120).

## What it did

When an enemy stood behind a closed door, out of sight or out of reach of the companions' walks, a companion could
walk to the door and open it with the game's own interaction (`UnitInteractWithObject`, a standard action, the same
as the player's click). This worked in both turn-based and real-time combat. The log showed `Open door to <enemy>`;
when no door was offered, a `[Door]` line said why.

## Where the code is

- `door-opening.patch` (this folder): the whole feature as one patch, made against the v0.3.29 release source.
  To bring it back, run `git apply docs/future/door-opening.patch` from the repository root, then build and run the
  tests. It restores:
  - `Tactics/TacticalEngine.Doors.cs`: the option, the door filter and the `[Door]` reasons.
  - `Tactics/DoorRules.cs` and `tests/CompanionAI.Tests/DoorRulesTests.cs`: the pure "on the way" rule (detour
    through the door) and its 10 tests.
  - The hooks in `TacticalEngine.cs`: `CandidateKind.Interact`, `Candidate.Door`, the candidate call, `Execute`, and
    the no-sight filter exemption in `SilentlyFailing`.
  - `TacticalEngine.ApproachValidation.cs`: the path check for the door's walk.
  - `TacticalDriver.cs` / `RealTimeController.cs`: `ShouldDropOpenedDoor`. In real time, a pending opening is
    dropped when someone else opened the door, because interacting with an open door closes it again.
- The development history also has it: commits `ed5cb78` (test 7) and `d2ed55e` (test 8) on
  `claude/dreamy-pasteur-b9h3rn`. Those commits also carry fixes that stay in the release, so restore from the patch,
  not by reverting the rollback.

## What the three play-test logs showed

The same room each time: the Neophyte Wizard (turn-based) and a Dretch (real time) behind a closed door in the
prologue area.

1. **v0.3.29 test 6 log, before the feature:** walks and spells at the wizard were refused ("path ends without line of
   sight") and the companions ended their turns.
2. **Test 7 log:** no `Open door` option at all. The filter asked for `EntityDataBase.IsRevealed`. The game sets that
   for a map object only with the fog of war off, for area exits and for traps; never for a door the party sees.
3. **Test 8 log (game visibility rule instead):** the new `[Door]` lines named the real reason. Every closed door
   near those enemies was **"not usable in combat"**. That door's interaction has `InteractionSettings.NotInCombat`
   set, so `InteractionPart.CanInteract()` is false while the party is in combat, and
   `UnitInteractWithObject.OnAction` fails it. The game forbids opening it during a fight, for the player as well.
   The AI was right not to open it.

So in the room that prompted the feature, no rule-abiding mod can open the door during combat. Opening it would mean
calling `InteractionDoorPart.Open()` directly, past the game's own rule, which this mod does not do (it uses the
game's rules).

## Before bringing it back

- Find a fight where a door that is usable in combat (`NotInCombat` off) stands between the party and an enemy. Only
  there can the feature be judged in play.
- The `[Door]` reason line is the first thing to read in the log.
- The rest still needs play-testing: locks the game counts as passed (key held, story unlock, already picked), the
  detour rule, opening from the door's radius without walking, and real-time dropping of a door opened meanwhile.
- A smaller alternative, if that never comes up: keep only the `[Door]` note, so the log explains why companions wait
  at a door the game will not let them open.
