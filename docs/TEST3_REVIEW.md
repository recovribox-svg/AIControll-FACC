# Test[3]: infusion execution, damage evidence and real-time movement

Reviewed all 1,930 TB AI lines, 1,327 RT AI lines and 15,346 native CombatLog
lines. The combat record contains 11 numbered encounters: six TB followed by five
RT. Neither AI excerpt includes a startup MVID; source-specific messages match
the new selector, but exact binary identity is not independently proven.

The native CombatLog is useful and should continue accompanying the AI logs.
It shows actual attack misses, saves/DCs, damage and resistance sources, opportunity
attacks, healing and actor IDs. A command saying `acted=True` is insufficient to
reconstruct those outcomes. It does not supply the live positions/path queries
needed for frame-exact movement attribution.

## 1. Infusion activation: confirmed execution bug, corrected

The AI did consider Burning. TB568/686/721/1060/1877 issue five activation commands;
every one ends immediately `Interrupt`, `acted=False`, followed by
`native activation request was refused`. No successful AI infusion activation is
demonstrated. The fallback actions continue, so no exception is required for this
defect to disable the feature. RT284/587 list positive plans but no activation issue.

The previous implementation queued `UnitActivateAbility` through the dispatcher,
then set `IsOn=true`, assuming native `OnDidTurnOn` would recognize its own queued
command. That assumption was incorrect. The complete native predicate calls
`IsBothAbilitiesIsJudgment`: it only suppresses duplicate **Judgment** commands.
For SubstanceInfusion it creates a second command. Native replacement interrupts
the first unacted matching activation; its end handler clears the toggle request.
This exactly matches the immediate failure trace.

The dedicated `CommandDispatcher.Run` overload now lets the actual native `IsOn`
request create its command **once**. It adopts the matching unfinished command
from `Raw` or `Queue`, checks native acceptance and tracks it without running it
again. No command is manually constructed here. These validated free, resource-free,
untargeted activations require no movement path. A native immediate-start case is
accepted only with an actual active, turned-on, caster-owned buff. A request with
neither a pending command nor installed buff is cleared and reported as failure.
Existing drivers still wait for the native command/DidAct/buff lifecycle and
redecide from actual costs. No force-start scope, private flag write, global patch
or hypothetical fact is introduced. The native internal command creation occurs
inside this dispatcher execution entry; it is not reissued through the ordinary
command overload. Ownership, group locks and restoration are retained.

### Costless quick-action classification

The generic quick threshold passed `null` ability for this new Toggle candidate,
classifying it as a paid quick action (bar 2). RT587's net gain 1.4 is positive
but never issues; TB1430's 1.5 similarly remains below that bar. Validated resource-free
infusion changes now use the **existing costless Free bar (1)**. Ordinary toggles
and Swift actions retain their existing threshold. Future blast burn is already
deducted in the marginal plan; this does not make all positive changes mandatory.
RT284's 0.7 still legitimately stays below the bar, and other quick actions may win.

Bowling/Pushing activatables appearing available does not mean they work with
every blast. TB76 reports Fire Blast and Searing Flesh as the two burn abilities;
no matching own Earth/Air blast is established in this excerpt. Their native
applicability excludes Fire. Comparing only Burning here is not proof the other
recognized effect layouts are broken or have been exercised.

## 2. Capped damage observation overriding known resistance: corrected

TB224 values Fire Blast against Dretch at 12.6 versus crossbow 7.6. CombatLog531–555
shows the blast hits but deals **zero**: `1d6 + 4 - 6`, with **Fire Resistance 10**.
TB226 learns `x1 - 6`. RT595 similarly learns 6 from a zero-damage blast.

Native `UnitPartDamageReduction.ApplyChunkReduction` limits the reduction recorded
on a hit to that packet's remaining damage. Six absorbed does not establish a
six-point resistance. `DamageObserver.Seen` is therefore evidence of an absorbed
amount, potentially a lower bound. `AfterTargetReduction` previously returned
that observation immediately and skipped known current native resistance/immunity.

It now preserves the observed factor/custom reduction while also asking the existing
native resistance query, taking the stricter remaining-damage bound. The two
reductions are not subtracted twice. Observed or native immunity returns zero;
an unavailable/failed native query retains the observed fallback. No resistance
table, new native damage roll, enemy scan or copied burn/game rule is introduced.

This corrects the source-confirmed **later observation override**. It does not
establish why that first pre-observation Fire Blast had an optimistic estimate:
its live parameters, active handlers and native query result are absent. That
first-hit discrepancy remains an explicit replay lead, not a claimed complete fix.
Existing observations can still become stale after defence changes within a fight.

## 3. Seelah movement: findings and approved diagnostics only

The author's reported tenth encounter matches CombatLog #10 / RT encounter 4:
RT697–1049, CombatLog11223–13439, with necromancer/Cambion/Hand of Hosilla.

- RT726–770: Seelah chooses a Sharpshooter approach, travels **13.5 m / 6.8 s**
  and **does attack**. CombatLog11467 confirms a miss; 11312–11330 shows an 8 HP
  opportunity hit during the approach. Missing damage is not a missing attack.
- RT798–815: she chooses to heal Camellia **6.9** over an in-reach attack **6.3**,
  walks 5.8 m into a route labelled Grease, loses **10 HP**, and AI Control explicitly
  cancels that approach to reconsider. CombatLog11699–11743 confirms the opportunity
  attacks, including Hand of Hosilla's 10 damage. This is a mod cancellation, not
  evidence of a native pathfinding refusal.
- RT818–852: she self-heals, then completes a 6.6 m approach and heals Camellia
  (CombatLog11939: 7 HP). These are support choices, not enemy retargeting.
- RT875–878: she attacks Cambion; CombatLog12039 confirms a miss. RT937–942's late
  Hand approach is interrupted after 1.2 m as damage incapacitates her; the combat
  record shows the Hand's 12-damage hit at 12357–12374.

RT encounter 5 additionally cancels a 4.1 m approach after 12 HP loss, then an
in-reach alternative interrupts almost immediately as she goes down. A distinct
native arrival/stuck problem is visible for Aeiluvetar at RT950–952. It should not
be conflated with all Seelah movement.

AI Control contributes through the long approaches, healing detours and emergency
cancellations it chooses. The logs do **not** demonstrate continuous score-based
target switching during a normal approach: `RealTimeController` waits while commands
are pending. Nor do they prove whether each opportunity attacker was visible in
the original path-risk query or moved into position later.

The author approved **selected-walk diagnostics only**. Added `[Walk]` lines record
the chosen score, best in-reach main action, existing risk cost/stop/drop/AoO/area
appraisal, and status/length/travel estimate from the already-requested native path.
An absent/skipped/failed path answer is distinguished from a returned one. These
lines add **no pathfinding calls**. Existing command/cancellation logs complete the
trace. Native navigation, scores, target choice, danger rules and emergency
interruption remain unchanged. No target-lock timer or switch penalty is added.

Replay first; only then discuss a specific risk/distance/support correction if
the new evidence establishes a gap. Do not disable emergency replanning: that
could force a critically hurt character to finish a lethal approach.

## Performance and other outcomes

| Mode/fight | Decisions | Median ms | p95 ms | Maximum ms |
| --- | ---: | ---: | ---: | ---: |
| TB1 | 29 | 33.4 | 200.7 | 572.2 |
| TB2 | 25 | 30.8 | 162.8 | 176.4 |
| TB3 | 53 | 13.7 | 146.6 | 168.2 |
| TB4 | 48 | 20.6 | 123.9 | 163.6 |
| TB5 | 56 | 26.4 | 174.3 | 252.5 |
| TB6 | 65 | 31.1 | 203.7 | 268.9 |
| RT1 | 97 | 23.3 | 168.8 | 758.7 |
| RT2 | 43 | 19.4 | 122.7 | 163.3 |
| RT3 | 76 | 27.1 | 96.0 | 117.6 |
| RT4 | 114 | 32.1 | 130.6 | 700.0 |
| RT5 | 122 | 13.0 | 101.4 | 762.7 |

TB: 276 decisions / 13,579 ms; RT: 452 / 19,066 ms plus 304 cooldown skips.
These are summed decision work, not FPS or whole-session speed. The sample includes
waits and failed activations; lower medians do not prove functional improvement.
RT160/1032/1197's long decisions overlap reported collection, but the logs do not
allocate the time to collection versus native rule work. TB23 is a cold-profile
Grease decision. No new cache, scan or broad scoring cut is justified from these
aggregate timings alone.

TB has 180 issues/180 ends, with 10 unacted outcomes (including five failed infusion
activations); RT has 189/189, with 40 unacted outcomes. Many interrupted attacks
acted successfully and missed; do not count every Interrupt as failure. No AI Control
exception is recorded. External blueprint-owner warnings remain external evidence.

## Validation and handover

- All **154 pure tests pass**, zero failed/skipped; Release against supplied real
  game/UMM references compiles with zero warnings/errors.
- Actual native metadata fixture: all 20 supplied substance activatables reject
  Judgment-only command matching even with themselves. No actor or installed fact
  is created and no command/gameplay action executes in this check.
- Compiled-route guard: the infusion dispatcher has native state request, current
  plus queued command adoption and trace, with **zero manual activation construction
  or command reissue**. Plain mod-candidate fixtures verify Free infusion bar 1,
  ordinary toggle bar 2, Swift bar 2. These are not live activation tests.
- Existing 18-recognized/two-excluded data checks, 486 native cost/quiver vectors,
  actual modifier branch and cone bindings pass. Ordinary audit remains partial:
  4,097 exports loaded, one known failed export, 1,220 rows / zero reader errors;
  normalized detail/verdict rows match the previous test baseline.
- Scope review verifies movement changes are logging only. Main/development remain
  unchanged; the draft task branch includes PR #8/#7/#6 as explicit dependencies.

Both-mode acceptance remains pending: verify exact new MVID, actual infusion buff
installation, native burn and blast outcomes, replacement/off/player ownership and
restoration, low-gain Free choices, Dretch fire resistance before/after observation,
and ordinary companions. Replay Seelah #10 with complete AI and CombatLog files to
inspect the approved diagnostics. Keep the same save/settings; report performance
and idle portrait selection too. No offline check establishes live frame times,
third-party command handlers or complete class competence.
