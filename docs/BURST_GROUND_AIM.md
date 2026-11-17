# Burst ground aim (slice A)

17 November 2026, Claude. The author approved slice A of
[BURST_AIM_AND_TACTICS_PLAN.md](BURST_AIM_AND_TACTICS_PLAN.md), including A3-A5. Branch `claude/burst-ground-aim`, from
the plan branch over development `ad63ee8`. **Not played.** Manifest stays 0.3.60; no `main`, tag or release change.

Checks: [BURST_AIM_CHECKS.json](competence/BURST_AIM_CHECKS.json). Package:
[burst1](../test-builds/AI-Control-FACC-v0.3.60-dev-burst1-d7534ed-UMM-test.zip), startup MVID
`bc92b339-5bf5-4981-b7af-f641071cbb06`.

## What changed for the player

A burst that can hurt allies and lands after its cast goes off is now aimed at a point on the ground, not at the enemy.
Examples: Explosive Runes and its fusions, Wail of the Banshee, Ice Storm, Flame Strike and Fireball-like projectile
bursts. Before, the game picked the burst's victims around the enemy wherever it had walked by then, which in outcome2
hit Seelah, Wenduag, Bandit and Tripper.

- **The aim.** In real time, an enemy walking somewhere is led to where it will be when the burst lands. The caster
  must be able to cast at that spot without walking first; otherwise the aim is where the enemy stands. In turn-based
  mode it is always where the enemy stands.
- **Who counts.** An enemy counts as hit only if it will surely be inside then. An ally counts as caught if it may be
  inside: standing in the spot, or walking into it around the landing time. The caster counts at the spot it casts from.
- **Re-check (real time).** Until the cast goes off, it is re-checked every 0.25 s and at cast start, with fresh
  positions. It is cancelled, for free, if:
  - an ally the decision did not count will now be caught (with Avoid Friendly Fire on), or
  - such allies cost more than the cast is worth (with it off), or
  - no enemy will be left where it lands.
- **Landing spot (real time).** The other AI units' walks treat the spot as an area "about to land" until the burst lands.
- **Turn-based.** A unit's turn waits for its own burst to land before ending, within the existing 4 s cap.

Bursts that keep aiming at the unit:
- enemy-only bursts (Deep Slumber, Eye of the Sun): following the target is better and they cannot hurt allies;
- Selective casts;
- bursts with an attack roll (bombs);
- bursts with no delay and no projectile;
- bursts the game will not aim at a point.

## Native contracts

From the supplied assembly (decompiled locally, not committed):

| Fact | Source |
| --- | --- |
| Victims picked when the burst lands: around the anchor unit's eye position, or the fixed point | `AbilityTargetsAround.Select` |
| A delay delivers on the main target after `DelaySeconds` | `AbilityDeliverDelay.Deliver` |
| A projectile aimed at a unit follows it | `AbilityDeliverProjectile.Deliver` |
| A fused cast's point targeting follows its deliver spell | `AbilityData.CanTargetPoint` |
| Cast time 2 s (6 s full-round, 0 for wands and Immediate animations) | `UnitUseAbility` (`m_CastTime`) |
| Spell spent when the cast goes off | `UnitUseAbility.OnAction` |
| Action spent when the command goes off | `UnitCommandController.TickCommand` |
| The command-act event | `IUnitCommandActHandler.HandleUnitCommandDidAct` |
| The turn-based turn end does not wait for a delay | `TurnController`, `CustomBrainPatch.ReadyToEndTurn` |

## Implementation

| Part | Where |
| --- | --- |
| Pure timing, prediction and verdict rules | `Tactics/BurstRules.cs` (unit-tested) |
| Delay and first-projectile metadata, read from the cast's deliver blueprint | `AbilityProfile` (`SelectsAround`, `DeliverDelaySeconds`, `ProjectileSpeed`, `ProjectileMinTime`) |
| A1 classification, A2 plan, lead point and forecast | `Tactics/TacticalEngine.Burst.cs`; `EvaluateAbility` and `ScoreAbility` |
| A3 re-check, A5 landing time | `Tactics/BurstWatch.cs`; the hook in `RealTimeController.ProcessUnit` |
| A4 landing spot | `AoeDangerAnalyzer.RegisterPendingBurst` and `PendingBurstLandsIn` |
| A5 turn wait | `CustomBrainPatch.ReadyToEndTurn` |

**Bookkeeping.** A ground-aimed candidate has no target unit, so its anchor enemy (`Candidate.Aimed`) is used wherever
the unit aim was used before:
- the cast ledger;
- the "reached nothing twice" safety net;
- the no-sight, unreachable and too-far notes;
- focus re-aim and the step-out-before-casting rule.

**Limits against thrashing.**
- At most 2 cancels per caster and ability every 6 s.
- After a cancel, the same ability is not aimed within 1.5 m of that point for 1.5 s.

**Bounds.**
- Each unit's walk is read once per decision (`WalkClaim`, at most 8 command slots).
- For each anchor: one pass over the enemies, and five window samples per walking ally.
- The re-check runs at most 4 times a second per casting unit: one pass over the units in the scene, and a damage
  estimate only for a newly exposed ally.
- No path search, world scan, dice-rolling rule, actor or context preview, or persistent actor cache is added.

**Diagnostics.**
- One `[Burst]` line per issued burst: the point, the lead, the landing time and its parts, and the enemies and allies
  counted.
- One line per cancel.
- The lines are capped at 64 per fight.
- The decision label carries `[ground +X m, lands in Y s]`.

## Checks

On checked source `d7534ed`, real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 953 pass (18 new in `BurstRulesTests`) |
| Inventory, ledger and supplement script tests | 27 + 18 + 3 pass |
| ProfileAudit | exit 0; `BURST-AIM` 28 checks, 0 failures; every other line equal to the PR #80 run on the same inputs; Fusion coverage identical (8,709 / 15,494) |
| Native harnesses | 98 / 14 / 10 |
| Added game references | 9, all resolve (compiled against the real assemblies) |

The export pass found:
- 27 around-bursts with a delay;
- 93 with a simple projectile, of which 8 reference a projectile blueprint missing from these exports (the game loads it);
- 19 enemy-only bursts;
- 40 abilities that get ground aim here.

## Replay

Record the startup MVID `bc92b339` and keep `Log.txt` and `combatLog.txt`.

Real time:
1. Have Explosive Runes (or an Explosive Runes fusion) cast at an enemy that is walking towards an ally. Expect a
   `[Burst]` line with "ground +X m ahead of <enemy>", and no damage to the ally in the combat log.
2. Let an ally walk into the spot before the cast goes off. Expect `[Burst] ... cancelled before casting (<ally> walks
   into it ...) - deciding again`, and the spell slot still unspent.
3. Repeat with Avoid Friendly Fire off. It cancels only when the ally's cost beats the cast's value.
4. While a burst waits to land, the other AI units should not walk through the spot. A walk priced through it names
   the spell "(about to land)".
5. An enemy standing still in melee with an ally: as before, no cast unless Selective.

Turn-based:

6. Cast Explosive Runes. The turn ends only after the burst lands (verbose: "turn end waited ...").

Regression:

7. Deep Slumber and a Selective cast still aim at the enemy unit. Fireball is now aimed at the ground.

## Limits

- **Not played.** Offline checks run no actor, selection, command, cast or Unity timing.
- Predictions follow a straight walk to its end, as `WalkClaim` does; the game's path can curve.
- A player's new order after the cast has gone off can still walk an ally into the burst. A4 covers only the AI units.
- An enemy that changes course after the cast has gone off makes the burst land where it would have been. That costs
  value, not allies.
- The decision's cast time is the native rule's 2 s. The re-check reads the running command's real cast time.
