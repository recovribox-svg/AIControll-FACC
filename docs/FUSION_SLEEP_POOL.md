# Fusion F7: Sleep fusions and the shared Hit Dice pool

16 November 2026, Claude. Author-requested ("Do the Sleep fusions next"). Branch `claude/fusion-sleep-pool`, stacked on
`claude/fusion-outcome-effects` (PR #76); development base `980cf26`. **Not played.** Manifest stays 0.3.52; no `main`,
tag or release change.

Checks: [FUSION_F7_CHECKS.json](competence/FUSION_F7_CHECKS.json). Package:
[sleep1](../test-builds/AI-Control-FACC-v0.3.52-dev-sleep1-cf41e4b-UMM-test.zip), startup MVID
`61f28efb-85ea-4900-97d6-e9f1328da60e`. It contains the F4 work of PR #76 as well.

## What changed for the player

arcan's two withheld fusions, **Snowball/Sleep** and **Explosive Runes/Sleep**, are now offered. Before, every fused
spell containing Sleep was refused with "shared Hit Dice selection; unsupported forecast, cast withheld".

- 498 more fused configurations are admitted: 8,709 of 15,494 (8,211 after PR #76). They are Sleep and Rainbow Pattern
  fusions.
- No configuration lost admission, and no already-admitted fusion changed.

## What the game does in a fused Sleep

Read from the supplied assembly and the author's blueprint exports.

1. **The pool is not Sleep's.** Sleep alone puts creatures to sleep out of a fixed pool of **4 Hit Dice**. A fused cast
   builds its context on the fusion's holder ability (`MagicHackDefaultSlot1` to `10`), and the game calculates shared
   values from that ability (`MechanicsContext.RecalculateSharedValues`). The holder's value for the same key is
   **2d6, rolled once per cast**. A fused Sleep therefore has a pool of 2 to 12 Hit Dice, 7 on average.
2. **Who is affected.** The deliver spell decides (`AbilityExecutionProcess`): its own area selector, or the one target.
   Explosive Runes' burst takes everyone within 10 feet, allies included.
3. **Order.** For each recipient in the game's order, the first spell's actions run, then the second's. Sleep tests
   "level ≤ pool left", applies its save branch and then takes the recipient's level off the pool, whether the save
   passed or not.
4. **The save.** With a root save on the fusion, Sleep's own Will save rolls nothing (`ContextActionSavingThrow`): the
   sleep lands on a failed root save, whatever type that is. Without a root save it would be a second roll; that form
   stays refused ("independent nested saving throw").
5. **Damage and sleep in one cast.** Damage dealt before the sleep exists does not wake the target. Damage after it
   does. So "Explosive Runes/Sleep" (damage first) keeps its sleepers, and "Sleep/Explosive Runes" would wake them at
   once.

## Implementation

- **`SharedHdControl`** (the existing Sleep pool reader) gained a fused plan, built once per fusion from metadata:
  - exactly one of the two source spells uses the pool; the other neither reads nor writes a shared value;
  - the pool is the holder's own dice expression, as an exact distribution (2d6: eleven values);
  - delivery is the deliver spell's around-selector, or one recipient reached directly or by one simple projectile.
- **Forecast per aim.** The existing ordered forecast (`SharedHdBudget.TryForecast`) runs once per possible pool roll and
  the results are averaged by the roll's chance. Each recipient's "does it reach the sleep, does it spend" answer is
  read once per pool value and reused across rolls. A maximized cast uses the full 12, as the game does.
- **Admission.** `TacticalEngine.FusionWithheld` offers a pooled fusion only when the plan is supported and the joint
  outcome value qualifies. Otherwise it stays withheld, with the reason in the log:
  `[Fusion] <name>: shared Hit Dice selection (<reason>); cast withheld`.
- **Value.** `TryFusionControlRounds` adds the sleep to the control envelope with the forecast chance
  (`ValueModel.PooledRounds`: the envelope with it and without it, blended). It is credited only when all of the
  cast's own damage belongs to the earlier source spell, and for one round at most beside a periodic damage carrier.
- **Allies.** An ally in the area is counted by the same forecast. An unread pool never proves an ally safe.
- **The older forecast** never credits a pooled fusion's control.
- **Log.** The first `[FusionValue]` row of such a fusion ends with
  `rolled Hit Dice pool covers this recipient N%`.

Not changed: ordinary Sleep (still its fixed 4), one native cast and one slot, spell policies, the blocklist, the cone
re-aim, movement.

## What still stays withheld, and why

Of 1,754 pooled configurations, 498 are admitted, 222 sit behind another structural refusal, and 1,034 are withheld
by the pool gate, each for one stated reason:

| Reason | Count |
| --- | --- |
| The pool spell's save branches have another shape (Deep Slumber, Circle of Death and others) | 472 |
| The joint outcome value does not qualify the partner spell | 340 |
| Cone, line, chain or repeated delivery (the order of recipients is not read) | 94 |
| Touch or additional-area delivery | 68 |
| Two pool spells in one cast | 32 |
| Spread or custom delivery | 28 |

## Bounds and performance

- Per aim: one read-only context (as for ordinary Sleep), the native selector once, and at most
  `recipients × (highest pool + 1)` condition reads (13 per recipient for 2d6). The eleven forecasts are scalar.
- No new rule trigger, save, path or world query; no actor or context cache. The plan holds blueprint metadata only.
- Limits unchanged: 128 recipients, pool up to 64, 8,192 transitions per forecast.
- Two more fused spells are now scored on arcan's turns. Baseline from the gaps3 log: 8 slow rows, 286 to 539 ms. The
  replay should compare the abilities stage with that.

## Checks

On checked source `cf41e4b` (DLL SHA-256 `5d947ac2…`), real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 887 pass (6 new) |
| Inventory, supplement and ledger script tests | 27 + 3 + 18 pass |
| Added game references against the F4 build | 0 |
| ProfileAudit | exit 0; two runs identical; `FUSION-POOL` 25 checks; every other line equal to the F4 run |
| Fusion coverage | admitted 8,211 → 8,709; 0 lost; 0 changed; 0 reader errors |
| Native harnesses | 98 / 14 / 10 |

## Replay (turn-based and real time)

Record the startup MVID `61f28efb` and keep both logs.

1. Keep arcan's Snowball/Sleep and Explosive Runes/Sleep. At the first fight neither should log "cast withheld". Each
   should log `[Fusion] … shared save …` and, once valued, a `[FusionValue]` row ending with
   `rolled Hit Dice pool covers this recipient N%`.
2. Against low-level enemies (4 Hit Dice or fewer) the pool chance should be high and the fusion should often be the
   best action. Against one enemy of 8 Hit Dice or more it should be low, and against 13 or more it should be 0%.
3. When Explosive Runes/Sleep is cast, the combat log should show the damage and then Sleep on targets that failed the
   Will save, as far as the pool reached. The targets must stay asleep after the cast's own damage.
4. Put an ally in the burst with Avoid Friendly Fire on: expect another aim or no cast.
5. Block one of the two fusions in "Abilities AI must not use": it must not be cast.
6. Repeat with four to eight driven summons in melee with the targets (they take pool as recipients of the burst).
7. Regression: ordinary Sleep behaves as before; the other fusions keep their `[FusionValue]` numbers; a Sleep fusion
   built with no root save, if the window offers one, stays withheld.
8. Note arcan's slow-decision rows against the baseline above.

**One thing to check in the Magic Fusion window:** which spell is first in each of the two fusions. With Sleep first
and the damage spell second, the damage wakes the sleepers; the engine then gives the sleep no credit and values the
spell by its damage alone.

## Limits

- Not played. Offline checks run no actor, selection, roll, cast or Unity timing.
- The forecast assumes each recipient is still standing when Sleep checks it. A target the partner spell drops first
  would not spend pool; later recipients are then slightly under-valued.
- Rainbow Pattern fusions are admitted by the same reader, because their graph has the same shape. No played case.
- Coverage numbers are native UI templates, not a character's spells.
- A holder slot another mod has changed is read as it is; a slot without a single fixed dice source keeps the fusion
  withheld.
