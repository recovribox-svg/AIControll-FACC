# coverage2 playtest: review and fixes

20 November 2026, Claude. This page reviews the author's coverage2 logs and describes the fixes, which are in the
coverage3 test package. Both modes are covered. Not played.

**Package:** [coverage3](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage3-1ed420b-UMM-test.zip)
- startup MVID `60673199-52d7-4e60-8078-c91096ac9242`;
- UMM 0.3.60;
- checks: [COVERAGE3_CHECKS.json](competence/COVERAGE3_CHECKS.json);
- compiler and references: [COVERAGE3_BUILD_PROVENANCE.json](competence/COVERAGE3_BUILD_PROVENANCE.json).

It is coverage2 plus these fixes, on `claude/coverage2-review` (stacked on `claude/fusion-coverage-c2-c5`). Nothing is
merged.

## The log

- **Build:** startup MVID `5cfd7765`, which is coverage2.
- **Fights:** 14 in total, 5 turn-based and 9 real-time.
- **Commands:**
  - 649 issued, and each has its end row.
  - No AI Control exception.
  - The two WrathPatches errors belong to another mod.
- **Raw logs:** not committed. Their hashes are in the receipt.

### What coverage2 was meant to show, and what it showed

| Item | Result |
| --- | --- |
| C2: Explosive Runes / Hideous Laughter cast | Yes. Cast twice turn-based (4 enemies counted each time) and once real-time; that cast was cancelled before it went off because an allied Babau walked in. |
| C1: Molten Orb and Acid Arrow, damage over time | Valued with their ticks; the joint value lines show "damage over time fail/pass". Molten Orb on its own was ordered three times and went off once. |
| Bursts in real time (R5) | 12 walks read, 2 of them started attacks chasing their target; the one cancel above. |
| No change elsewhere | Nothing found that the coverage2 changes broke. |

### R1, R3 and R4: decided from the summary lines

| Item | Evidence | Decision |
| --- | --- | --- |
| **R1**: origin-aware engagement quote | 63 melee attacks compared at the roll: 56 forecast right, **0 forecast flanked wrongly**, 7 flanked but not forecast. All 8 second-engager forecasts were right. | **Not built.** The geometry quote guards against a wrong "flanked" forecast, and none occurred. The 7 misses go the other way, so the forecast is cautious. The next build logs each miss (below). |
| **R3**: fresh control price in the burst watch | No ally walked into a burst without a control price from its decision. | **Not built.** |
| **R4**: timing constants | First melee swing **0.70 s** on average (n = 72; estimate 0.6 s). Routine spacing matches 6 s / N: 2-attack 2.7–3.9 s, 3-attack 1.8–2.1 s, 4-attack 1.5–1.6 s, 5-attack 1.2 s. **Bow shots 1.81 s apart** (n = 34; estimate 0.7 s). First ranged shot falls in two groups (about 0.07 s and about 1.3 s). | **Calibrated:** first melee swing 0.7 s; bow shots one animation (1.8 s) apart, at most 6 s / N. The first ranged shot keeps 0.6 s, because its mean (0.52 s) describes neither group. 6 s / N and arrow speed unchanged. |

Cast disruption: three attacks were issued on a casting enemy. All three casts went off, and no attack resolved in
time. One had been credited with a 4 % break. Three is too few to calibrate the cast-time forecast; it is left as is.

## Findings and fixes

### 1. Real-time area spells with a cast time caught allies (fixed)

**Evidence.**
- arcan's Incendiary Runes / Frigid Touch fusion was cast five times in real time.
- Four of those casts hit allies: Orvath (four times), Wenduag, Bandit and the summoned Babau, **76 HP** in total (combat log).
- No decision row priced or vetoed friendly fire, even though Avoid Friendly Fire is on.
- The casts went off 2–6 s after their decisions: a walk of up to about 4 s (12.5 m), then the 2 s cast.

**Cause.**
- Native `AbilityTargetsAround` picks the victims around the clicked unit when the cast goes off.
- Ground aim and the pre-cast re-check (`BurstWatch`) were only used for bursts with a delivery delay or a projectile
  flight.
- Real time, the cast itself is such a lag (`UnitUseAbility.m_CastTime`, 2 s). In that time the target moved and melee
  allies closed in, and nothing re-checked the cast.

**Change** (`TacticalEngine.GroundAimBurst`, `BurstRules.LandsLater`).
- In real time, a burst that picks its victims around its anchor gets ground aim when its cast takes time. It must also
  be able to hurt allies, make no attack roll, and be able to target a point.
- It then gets what Explosive Runes already has:
  - it is aimed where the enemy will be when the cast goes off;
  - enemies count only when they are surely inside;
  - allies walking in are counted;
  - the cast is re-checked until it goes off, and cancelled for free if an ally walks in;
  - the landing spot is shared, so the other AI units do not walk into it.
- Turn-based is unchanged: nobody else moves during the caster's turn.

**Scope.** 83 exported around-bursts with neither delay nor flight now get ground aim in real time. Examples: Incendiary
Runes and its fusions, Glitterdust and Sound Burst. Enemy-only bursts, Selective casts, attack-roll bursts,
wands and Immediate-animation abilities keep unit aim. A burst on enemies that keep walking is valued lower, because an
enemy that may walk out is not counted.

### 2. A touch heal on a moving ally chased it round in circles (fixed)

**Evidence.**
- Ten real-time walks to touch range ended "in reach".
- In fight 12, Seelah walked twice to touch her summoned Schir with Cure Light Wounds. Each walk ended in reach, the next
  decision found Schir a step out of reach and walked again, and her third order (the cast) ended without acting.
- The same happened to Seelah with Tripper, Babau and Brimorak, and to Camellia with Tripper and Babau.

**Cause.**
- The walk ends as soon as the game's reach test reaches the recipient (`ShouldEndArrivedWalk`).
- The recipient keeps moving, so a moment later the decision's own reach test fails and it walks again.

**Change** (`CombatMemory.NoteTouchArrived`, `TacticalEngine.TouchFromHere`).
- For 1.5 s after such a walk, the same touch on the same recipient is cast from where the caster stands, if the
  recipient is within one 5-foot step beyond the touch's reach. The game's own distance and sight test decides.
- The game's touch delivery walks the last step (`UnitUseAbility` queues it after the cast).
- Real time only. A `[Walk] … casts … from here` line is logged once per fight and recipient.

### 3. R4 timing (fixed)

See the table above (`DisruptRules.FirstSwingSeconds`, `BowSwingSeconds`, `SwingSpacing(…, bow)`).
- A bow's second shot is now forecast 2.5 s after the decision. Before, it was 1.4 s, which beat a 2 s enemy cast it
  could not reach in time.
- This applies to this unit's attacks and to allies' ordered attacks (F2).

### 4. A stray timing summary at the start of a fight (fixed)

- The start of a fight reported what was recorded since the last report. Attacks that resolved between fights 13 and 14
  printed a "fight summary" (n = 1) at the start of fight 14.
- The start of a fight now resets without a line (`TimelineObserver.Clear` → `Reset`).

### 5. Flank misses: evidence for the next round (logging only)

- Each of the first four "flanked but not forecast" attacks per fight now logs a line. It shows:
  - the attacker and the target;
  - the seconds since the decision;
  - the other engagers the decision counted;
  - who engaged the target at the roll, and whether each one's command targeted it (the real-time rule).
- This shows whether the misses are allies arriving during the attack or a rule the forecast does not read.

### Found, not changed

- **Turn-based: two idle rounds against a helpless enemy on a ledge (fight 4, R3–R4).**
  - The last enemy, a helpless Brimorak, stood 4.4 m above the party.
  - Every path to it ended under the ledge, out of reach or out of sight.
  - Every spot around it lay in the party's own Corrosive Bomb area.
  - Once the rat swarm fell in R3, the rest of the party ended its turns (Lann only advanced). In R4 three units could
    act, and Orvath's blast ended the fight in R5.
  - A fix would need a search for a reachable spot with sight (a movement change), and the enemy was no threat. Recorded,
    not changed.
- **Tripper (wolf, about 30 HP) went down in fights 9, 13 and 14.**
  - Each time it advanced into a crowded fight at full HP for low value (about 1–2).
  - In fight 14 it was nauseated inside a Locust Swarm and in reach of two enemies. Leaving would have drawn their attacks
    of opportunity while it stayed in their reach, so the way out was priced at −19.9.
  - The model did what it is built to do. No rule was shown wrong.
- **Interrupted real-time orders that never acted (92 of 435).**
  - About half carry a logged re-decision reason: an engager on the way, in reach, HP lost while waiting, or a new enemy.
  - Most of the others end as their target dies or the fight ends ("no reachable enemies").
  - No new pattern beyond finding 2.
- **Performance:**
  - Real-time median 4.8–13.2 ms; turn-based 16.6–31.3 ms.
  - The nine decisions slower than 800 ms all ran a memory clean-up (heap −209 to −260 MB).
  - The heaviest single decision allocated 21 MB (arcan, abilities stage); its costliest abilities were Flare, Haunting
    Mists and Demoralize, not the fusions.
  - Unchanged from earlier builds. No speed-up is claimed.

## Checks

On `1ed420b`:
- Release build: 0 warnings.
- Tests: 1,059 unit (7 new) + 27/21/3 script tests.
- ProfileAudit exit 0:
  - `BURST-AIM` 73, `EVIDENCE` 14, `CAST-DISRUPT` 50.
  - Every other line, the Fusion coverage and the summon detail are identical to coverage2 on the same inputs.
- Native harnesses: 98/14/10.
- 0 added or removed game references (2,050 signatures).

## Replay (coverage3)

Record the startup MVID `60673199`. Send `Log.txt` and `combatLog.txt`, and say where you spawned enemies, rested, or
gave manual orders.

1. **Real time, arcan with allies in melee.**
   - Incendiary Runes / Frigid Touch should be logged as `[Burst] … aimed at the ground …`.
   - It should be cancelled ("… walks into it") or not cast when an ally closes in.
   - The combat log should show no friendly hits from it.
2. **Real time, a touch heal on a moving summon or companion.** It should be cast within a walk or two. A
   `[Walk] … casts … from here` line may appear.
3. **The fight summaries.**
   - `[Timing]` now prints the bow forecast (1.80 s).
   - `[Flank] … flanked at the roll but not forecast …` lines should appear for the misses.

## Not in this build

- C3 (Disintegrate) still waits for the author's policy on a permanent ally death.
- R6, R7 and the death-trigger proposal remain set aside.
