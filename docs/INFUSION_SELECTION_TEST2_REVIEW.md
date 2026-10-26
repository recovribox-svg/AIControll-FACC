# Test[2]: infusion selection and log review

The author supplied `Turn Based Mode v0.3.22-Test[2].txt` (1,418 lines) and
`Realtime Mode v0.3.22-Test[2].txt` (1,139 lines), and reports **smooth overall**
play on the budget laptop. Physical line references below refer to those uploads.
Both excerpts start without a startup build marker. Their installed DLL identity
cannot be independently established from these excerpts.

## Infusions remaining off: explained and addressed

TB40/852 explicitly say Burning is left alone except when its burn blocks every
blast. TB854–857 show Bowling/Pushing off, with duplicate activatable entries.
RT1065 records a player-enabled Burning toggle. No added-value `[Kinetic]` line
appears in either excerpt. These facts match the old source: the previous task
valued already-active maneuvers only; it did not choose or activate infusions.
This was a capability boundary, not evidence that native activation was broken.

The new selector compares supported individual substance changes, including off,
against actual blast plans and the best ordinary main action. It issues native
activation through the dispatcher, waits for command completion and actual buff
installation, then decides again with actual cost/availability. Full behavior,
support matrix and remaining limits are in [the implementation plan](INFUSION_SELECTION_PLAN.md).
`DeactivateImmediately` was checked in native code: it makes stopping immediate;
it does **not** establish that successfully activated infusions immediately expire.

## Three source-supported corrections

### Arrived touch approach mistaken for a failed cast

RT239–245: Camellia's separate Cure Light Wounds approach moves 6.8 m, is cancelled
as stuck despite being in range, and records the parent spell key as refused.
The next appraisal values reviving Seelah at 49.2 but chooses a 3.5 attack instead.

The command's weak issue record now retains its exact follow-up ability/aim.
Before suppressing a stuck separate walk, the driver checks current native cast
reach and line of sight. An arrived approach clears only that command's pending
key and permits reconsideration. It does not erase unrelated failures or turn a
blocked path into a successful cast. Unknown reach/LoS keeps existing suppression.
Actual cast success remains unverified until replayed in game.

### Re-evaluating unavailable healing/buffs during cooldown

RT819–831 show three warm Orvath decisions of 301/338/343 ms, no new profiles, no
standard action, repeated revive appraisals and a final wait. Source scoring was
still walking through those unavailable healing/buff options.

A narrow action-slot gate omits unrelated unavailable options. It deliberately
retains offensive future positioning, held-touch opportunities, and spells/items
needed to price a spell-forbidding toggle's opportunity cost. Ability discovery is
shared within the decision rather than repeated by the kineticist helper. This
does not skip all unavailable actions or change action cooldowns. It reduces
known unnecessary work; the logs do not establish how many milliseconds it saves.

### Low-HP withdraw/advance loop

RT254–294 show Lann withdrawing, completing an 18.3 m retreat, then advancing for
0.6 points toward an attack worth about 0.4–0.5. The pattern repeats at RT391–409,
441–443 and 630–650. Fight 1 contains eight withdrawal and eight advance issues.

The source imposed a 0.6 minimum advance value, above the ordinary 0.5 action bar,
even when the prospective attack did not justify it. That floor is removed.
Advancing now uses its actual prospective attack/refused-action payoff multiplied
by the existing advance share, with the existing movement risk. No arbitrary
post-retreat timeout or target blacklist was added. This corrects this forced
low-value return; it does not prove all future retreat decisions optimal.

## Performance evidence

| Mode/fight | Decisions | Median ms | p95 ms | Maximum ms |
| --- | ---: | ---: | ---: | ---: |
| RT1 (line 717) | 297 | 36.6 | 127.3 | 1167.6 |
| RT2 (line 1127) | 182 | 49.0 | 232.5 | 365.2 |
| TB1 (line 730) | 102 | 56.7 | 180.4 | 590.6 |
| TB2 (line 1029) | 30 | 33.7 | 104.4 | 136.3 |
| TB3 (line 1256) | 39 | 36.5 | 186.0 | 187.9 |
| TB4 (line 1410) | 24 | 27.4 | 93.6 | 121.7 |

RT totals 479 decisions / 27,989 ms summed work, plus 396 cooldown skips. TB
totals 195 decisions / 11,295 ms. These are decision durations, not FPS/frame
timings. Do not average fight percentiles into an overall percentile. RT338's
1168 ms wait overlaps a reported memory collection; the log does not apportion
time between collection, rule handlers and other work. TB64's 591 ms first
Entangle decision builds profiles. Neither establishes a new infusion bottleneck:
the tested source did not search infusion configurations.

The new selector's four-aim/eight-recipient/24-comparison/8 ms cooperative limit
is a search limit, **not a bound on the entire decision or active-effect scoring**.
A native call already running cannot be interrupted. No animation/time-scale or
combat-speed setting is changed. Only a matched live replay can establish the
new build's performance; Test[2]'s smoothness is evidence for the previous build.

## Other findings and limits

- TB has 123 issued/123 ended commands: 50 abilities, 15 moves and 58 attacks;
  none ended with `acted=False`. RT has 205/205: 77 abilities, 50 moves and 78
  attacks, with 18 `acted=False` outcomes. An interrupted attack with
  `acted=True` is not a failed attack. Failed movement/casts must be read in context.
- No AI Control exception is recorded. TB's ChannelPositiveHarm/ClashingRocks
  owner warnings are emitted by WrathPatches about other blueprints. These logs
  do not establish their gameplay effect or justify editing another mod's data.
- Blasts, ordinary attacks, Sleep, Color Spray, DC boosters, heals, summons and
  area escape are issued. Command completion alone does not verify exact damage,
  saving throws, every cone recipient or the full Sleep forecast.
- The previous PR's Sleep/path/energy/cone fixes remain included. Its conservative
  Deep Slumber spreading-delivery restriction remains explicit; it is not new
  fallout from infusion selection.
- No live game is available in this environment. Native metadata/value-type tests,
  compilation and pure tests are separated from the gameplay acceptance checklist.

## Focused replay

Use both modes with AI toggle management enabled and the exact new build marker.
Start with learned infusions off and let AI choose. Check Burning versus susceptible
and resistant targets; Bowling versus prone/immune targets; burn-limited rounds;
replacement/off decisions; duplicate entries; player clicks; management off; and
end-of-fight restoration. Repeat the touch-revive and low-HP archer situations.
Compare ordinary companions, Orvath/summons, and idle portrait selection with the
same save/settings. Send the complete log and ZIP filename, including any cases
where an apparently useful infusion stays off. A stronger ordinary action, extra
burn, immunity or a logged unsupported shape may legitimately explain that choice.
