# Review of the "v0.3.23-dev-build3" logs and play-test notes (31 October 2026, Claude)

Inputs supplied by the author:
- `Turn_Based_Mode_v0.3.23-dev-build3_log.txt`: 12 fights;
- `Real_Time_Mode_v0.3.23-dev-build3_log.txt`: 15 fights;
- the game's `combatLog.txt`: 27 combats; 1-12 are turn-based, 13-27 real time;
- the author's `Noticed_during_playtest.txt`.

Build: `[Build] v0.3.22 module=582c262e-656d-4da3-83d0-0d2f43f4711f` (v0.3.23-dev-build3, 089a402).

## The author's two notes (real time)

### 1. "A downed companion healed beside an enemy is struck down again at once" - fixed

**Real-time fight 3 (combat 15).** Orvath brought Seelah back four times beside the Cultist Champion (CR4 Cultist
Fighter Tank), at "stays up" estimates of 38-93 %.

Each time, the attack of opportunity for standing up hit at once:

| Heal | Attack of opportunity |
| --- | --- |
| 8 | 6 |
| 10 | 8 |
| 6 | 8 |
| 14 | 14 (critical) |

The Champion's next blow then dropped her again.

**Cause.** `ReviveStaysUp` judged the stand-up attack and the following round of attacks each at the full HP after
the heal. In reality they come one after the other, and the second meets what the first left.

**Fix.** Each step now meets the HP the step before it left:
- **Real time:** she stands at once, so the stand-up attacks come first and the round's danger meets the rest.
- **Turn-based:** she lies through the enemies' turns and then stands. The stand-up attacks meet the HP she is
  expected to have left if she survived the round (`RiskMath.ExpectedTakenIfStillUp`, the conditional mean of the
  engine's exponential damage model, unit-tested).

**Effect.** The four heals above come out at roughly 35-50 % instead of 80-93 %. A revive beside a heavy hitter now
competes with dealing with that enemy first (Color Spray, Sleep, an attack). The remaining value of reviving is
unchanged.

### 2. "Front-liners bypass the enemy front line to chase its archers and casters" - fixed for real time

**Real-time fight 5 (combat 17).**
- At the start Seelah chose Move+Attack on a Cultist Archer 13.6 m away (8.3) over an Earth Mephit (5.5) and an
  Abrikandilu (5.2) near her. Attacks of opportunity priced: 0.
- She was shot by both archers on the way (11 and 12) and clawed by the Abrikandilu (10).
- In another fight she walked 14 m to a Neophyte Sharpshooter (10.0) over a Neophyte Fighter nearby (9.6).

**Cause.** Real time, a walk-first option was priced only by the attacks of opportunity on its straight path at the
enemies' present positions:
- nothing for the walk's time: the action comes a whole round later;
- nothing for enemies who move into the way.

Turn-based has neither problem: Move+Attack happens within the turn, and enemies do not move during it.

**Fix (real time only, the same scale as everything else).** For Move+Attack, Charge and walk-then-cast:
- **Walk time.** The action comes later by the walk beyond its own cooldown and is worth
  `ValueModel.RealTimeWalkFactor` = round / (round + delay): nothing lost without delay, half after a whole round of
  walking. Unit-tested.
- **Interception.** A melee enemy (not the target, not engaging the unit already, not at the destination) that can
  reach the way within half the walk time is priced like an attack of opportunity on the walk (`DisengageRisk`,
  shown as `[intercepted N]`).

**Effect on the logged cases.**
- Archer: 8.3 × 0.58 = 4.8, less two interceptions; the Earth Mephit next to her stays about 5. She engages the front
  line.
- Sharpshooter: 10.0 × 0.57 = 5.7, against the Neophyte Fighter at about 9.

Staying with the nearer front-liners also leaves them engaged, which keeps them off the back line. No separate
"protect" rule was added; the walk's price does it.

## Other fixes

### 3. Orders issued during the get-up animation (real time)

After lying prone, a unit plays its get-up animation, and the game starts no command until it ends.
- The controller decided during it: 19 decisions, 9 of them cancelled "Interrupt" after 0.0 s (Aeiluvetar's Aid
  Another 8 times, Seelah's 3), the rest with the move blocked.
- **Fix:** real time now waits out the animation (`View.IsGetUp`), as the turn-based controller and the game's
  ContinueActing already do.

## Previous fixes, seen in play

- **Real-time Coup de Grace with a walk: works.**
  - Seelah walked 2.3 m and Camellia 3.7 m (into a Stinking Cloud) and both landed.
  - Three more landed in turn-based.
- **Force-finished casts:** no false failure strikes in these logs.

## Looked at, no change

- **Turn-based fight 10: Seelah and Camellia had no weapon option or advance all fight.**
  - No enemy melee reached them either; only ranged attacks and spells happened, both ways.
  - The real path check excluded these enemies, so it is map geometry, not a decision. (The `[Threat]` line estimates
    reach by distance only.)
- **Real-time waits with every action available.**
  - Aeiluvetar against a Vermlek: her arrows are worth 0.0.
  - Fight 13: no line of sight to anyone.
- **Performance.** 178 slow decisions; 10 of the 12 over 600 ms coincided with a garbage collection.

## Version

`Info.json` now reads **0.3.23**, as the author asked, so the startup line identifies the build as v0.3.23.

## Checks run

- Release build against the game's references: 0 warnings, 0 errors.
- Unit tests: 229 passed (new: walk factor, conditional damage).
- `tools/SigDump`: no external reference added or removed.
- Not yet run in the game.
