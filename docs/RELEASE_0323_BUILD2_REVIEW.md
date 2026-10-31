# Review of the "v0.3.23-dev-build2" logs (31 October 2026, Claude)

Inputs supplied by the author:
- `Turn_Based_Mode_v0.3.23-dev-build2_log.txt`: 12 fights;
- `Real_Time_Mode_v0.3.23-dev-build2_log.txt`: 14 fights;
- the game's `combatLog.txt`: 26 combats; 1-12 are turn-based, 13-26 real time.

Build: `[Build] v0.3.22 module=ba81d8ad-acb4-4dfc-80e1-1a46c8acd506`, the movement-0c2e0f5 test ZIP renamed by the author
for 0.3.23 development. UMM still reads 0.3.22 from `Info.json`.

## Previous fixes, seen in play

- **Sleep loop: gone.** Every Sleep in the 26 combats landed once.
  - No target was put back to sleep after being shot awake.
  - Archers left sleepers alone while another enemy fought: Lann shot the awake Corrupted Mongrel Ranger, not the
    sleeping Guard (TB fight 11, R4).
- **Step out of melee to heal: works.**
  - Camellia (32 %) and Seelah (29 %) each took "5ft step out of melee" and then cast Cure Light Wounds on themselves.
  - Camellia at 21 % stepped out and then cast Grease (64) rather than heal (20): a value choice.
- **Coup de Grace in turn-based:** three, all landed.
- **Arcane failure (PR #15): first gameplay evidence.** Seelah casts with 10 % native arcane failure, and her spells are
  valued ×0.90 as intended.
- **TabletopTweaks preview: active.** 255 `route=TTT estimate` rows, no errors.

## Fixed

### 1. Coup de Grace was never offered in real time when it needed a walk

**Real-time fight 7 (combat 19).** Seelah and Camellia stood beside a sleeping Neophyte Fighter for about a minute,
waiting with nothing better than 0.3 (15 and 12 "wait" decisions with every action available).
- Neither was offered a Coup de Grace.
- When the last other enemy died, the archers and Seelah's Divine Zap woke the sleeper instead.

**Cause.** An ability that needs a walk first was considered only as a Standard action. Coup de Grace is full-round.
That is right turn-based (no move and full-round action together), but in real time walking is free: the game walks a
unit to its target before a full-round action too.

**Fix.** In real time a full-round ability may walk to its target like a Standard one. Turn-based is unchanged.

### 2. The last sleeping enemy was shot awake beside a melee ally who could finish it

**Turn-based fight 11, R5.**
- The sleeping Corrupted Mongrel Guard was the last enemy, so `WakeFactor` gave half value ("then it has to be
  fought").
- Lann shot it awake for 12. It got up and hit Seelah, who could have finished it with a Coup de Grace.
- Real-time fight 7 ended the same way.

**Fix.** When every enemy is disabled and a conscious party member who can act wields a melee weapon and has
Coup de Grace, a non-lethal hit on a sleeper is worth nothing (it no longer has to be fought awake). Without such an
ally the half value stays.

### 3. A false "did nothing" strike for a cast that was never started

**Turn-based fight 4.**
- Orvath's Color Spray "into Entangle" walked 5.6 m, and the game force-finished the command before it started (ends
  "Success", acted, never started).
- The turn controller and the cast ledger both counted it as not performed.
- `CastOutcome.Settle` still struck Color Spray "did nothing to its target (1/2)", because its gate looked at "acted"
  only. Two strikes block the ability on that target.

**Fix.** The gate uses the same rule as `CastLedger.Performed`, started and acted. A genuine silent failure (the game
executed the cast and nothing happened) is still caught. Four unit tests were added; 222 pass.

## Looked at, no change

- **Orvath stuck walking from one spot (RT fight 6).** Three walks in a row (Color Spray, then two Move+Attacks) were
  cancelled as stuck from the same position, about (78, 40, 91).
  - The fixes2 log's stuck snapshots were at the same place (Lann and Orvath).
  - The native path ends short of the goal there (endpoint 5-9 m from it) although the inspection path reaches it.
  - That is pathfinding, kept diagnostics-only: evidence for recommendation E (the `[Walk]` snapshots name the spot).
- **A boost spent before a walk that got stuck.** An Eldritch Surge "for Color Spray" went to waste in that episode.
  It is 1 of 22 boosts in real time; the second boosted Color Spray landed. Not changed.
- **Camellia with nothing to do against the Hand of Hosilla (TB fight 10, R6-R7).** No approach option was offered
  against that distant caster, and the log does not say why. To check in the next logs.
- **Seelah's orders dropped at fight start (RT).** She lost 12-13 of her 17 HP while waiting to cast, and the guard
  dropped her unstarted orders as designed.

## Performance (recommendation B evidence; nothing changed)

- 127 slow decisions, median 296 ms. All 10 over 600 ms coincided with a garbage collection.
- Orvath's real-time abilities stage alone is usually 230-350 ms, with 79 abilities on his bar.

## Checks run

- Release build against the game's references: 0 warnings, 0 errors.
- Unit tests: 222 passed.
- `tools/SigDump`: no external reference added or removed.
- Not yet run in the game.
