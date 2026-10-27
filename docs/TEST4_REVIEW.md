# Test[4]: successful infusions, spell coordination and measured performance work

Reviewed the 2,857-line TB AI log, 2,073-line RT AI log and combined native
CombatLog. Native references below use **LF-delimited physical lines**: the file
has mixed CR/CRLF formatting, and universal-newline readers produce different
numbers. Its twelve encounters are **seven RT followed by five TB**, unlike
Test[3]. Neither AI excerpt contains its startup MVID; the messages match the
previous fixes, but exact installed binary identity is not independently proven.

Both Test[3] and Test[4] were played on the same budget laptop. The author reports
Test[4] as more demanding across several actors, with occasional stutter through
more frequent slowdowns, but much milder than the withdrawn preview. Hardware
limits matter; neither the feedback nor aggregate timings establish that the mod
is generally poorly optimized, that the CPU alone explains the difference, or
that one actor or subsystem causes every pause.

## 1. Automatic Burning now executes and produces real effects

All **eight** logged infusion activations complete `Success`, `acted=True`:
TB398/1092/2146 and RT223/486/899/1132/1795. The following blasts use the actual
installed Burning buff. RT1791–1805 additionally exercises the corrected costless
Free threshold: a **1.9** improvement is selected and the subsequent blast completes.
The Test[3] immediate-activation failure is not repeated in these traces.

The native record supplies independent outcome evidence:

- CombatLog728–734: Zombie Fighter fails Burning's Reflex save against **DC19**.
- CombatLog19113–19120: Wandering Conjurer fails the initial Burning save.
- CombatLog19267–19279: the Conjurer fails a recurring save and takes **6 Fire**,
  explicitly sourced to **Burning Infusion**.

The native DC agrees with the AI's effect-context forecast. Passed saves elsewhere
are not activation failures. Only Burning is exercised in these sessions; other
recognized substance families are not thereby gameplay-verified. Bowling/Pushing
availability does not establish applicability to the observed Fire Blast.

Actual burn rises to 1/3, 2/3 and 3/3 at TB403/434/2151. Burning is restored off
at TB477/1229 and RT340/550/944/1217. TB2297–2303 and RT2024 demonstrate stopping
it when burn-block recovery makes an ordinary blast preferable. Keeping it on
while a stronger ordinary action exists is not itself a recovery failure. Tiny
off gains may legitimately remain below the existing Free threshold. No toggle
thrashing or exceeded daily limit is established. Player-owned groups and
management-disabled interactions still need focused replay.

## 2. Confirmed spell/movement coordination gap; narrow approved guard

RT encounter 7 contains the clearest avoidable coordination failure:

1. RT1508–1511: Seelah begins an elemental attack approach. The existing native
   query returns an **8.1m** path, **2.2m** arrival radius and no current area hazard.
2. RT1514–1521: Camellia chooses Grease on the elemental; Orvath chooses and
   completes Grease on a nearby Conjurer.
3. RT1531–1532: Seelah's approach interrupts after **4.1m**, unacted, and she is
   prone. CombatLog6215–6223 explicitly records her failed **Grease DC19** save,
   consistent with Orvath's cast.

The initial no-area result was temporally correct: allied spell choices created
the hazard afterward. Existing spell scoring checks current recipients and adds
a generic half-cost for possible future melee exposure; it did not inspect an
ally's already accepted attack approach.

The author approved a spell-safety correction using existing command/range data,
with no new navigation work. `CommittedApproachCaught` now feeds the existing
control-area friendly-fire decision in `ScoreAbility`. With **Avoid friendly fire**
enabled, it withholds a new harmful Soft/Hard control circle when a conscious,
directly controllable party/pet ally is demonstrably approaching into it. Existing
Selective, side, recipient-condition, immunity and present-buff checks remain.

The proof is deliberately narrow:

- RT combat only; one exact direct native fixed-position area spawn, matching one
  cylindrical spawned area. Attached, conditional, multiple, custom and unknown
  spawns retain existing appraisal. Literal zero duration is excluded; native
  variable duration/rank expressions are not recalculated here.
- Inspect at most eight current native command slots per ally. Only an accepted,
  unfinished ordinary `UnitAttack`, not started/acted/cutscene, with a live enemy
  target qualifies. Queued-only future orders, summons and the caster are excluded.
- Native distance and `ShouldUnitApproach` must confirm a remaining approach.
  Destination and proposed centre must have equal elevation. The entire possible
  arrival/body disk must fit strictly inside the smaller of native area size and
  existing profile radius: centre distance + native approach radius + corpulence
  must be less than that radius. No attack reach formula is recreated.
- Mere overlap, boundaries, slopes, unknown geometry and many ranged/large-target
  approaches do not establish a veto. Already-inside allies retain the existing
  current-position check. An unavailable check retains existing appraisal.

`[Walk]` explains a withheld cast once per caster/ability/ally/round. No commands
are changed or cancelled; no new path queries, target locks, movement scores or
emergency-cancellation policy changes are introduced. This is not a complete
future-route forecast. The supplied logs lack exact positions/elevations, so the
particular Test[4] incident being prevented is **not proven** by geometry tests.

Other Seelah traces do not establish continuous target-score thrashing. Native
paths are returned for her logged checks. RT606's emergency cancellation follows
incoming damage, while CombatLog1818–1844 identifies an ordinary Brute attack,
not an opportunity attack contradicting the zero-AoO forecast. Camellia's RT859
arrival/stuck event remains a separate lead.

## 3. Decisions preserved after checking actual outcomes

RT1890–1914's Sleep aimed at an immune/high-HD elemental is not necessarily wasted:
the nearby Vermlek actually fails its Sleep save at RT1912. The aim identifies the
area centre; it need not receive the benefit. Rejecting such centres by name or
immunity would remove legitimate area choices.

TB2521–2564's partial cures on downed Camellia show progress: needed HP changes
from **8 to 5** after the first successful heal. These are not repeated unchanged
failed casts. No arbitrary revive-attempt cap is added. Native TB attacks marked
`Interrupt` with `acted=True` can have attacked and missed; the status alone is
not a failure count. No AI Control exception is recorded in these excerpts.

## 4. Small local deduplication; no reduction in decision scope

Source review found repeated reads within otherwise unchanged forecasts:

- Spell-boost coupling asks for the same actual ability's description in several
  passes/aims. A function-local identity-keyed memo reuses the description, including
  null results. The original structural fast exit remains, so ordinary nonboost
  spells do not populate it. Variants/metamagic are not merged by blueprint name.
- Periodic infusion forecasts can reread the same damage reduction on successive
  predicted ticks. A memo local to one effect appraisal reuses only the resulting
  float, keyed by action, source-context and recipient identities plus the freshly
  calculated amount. No context or result survives that appraisal.

Every native child context is still constructed and recalculated. Dice, saves,
branches, survival weighting, duration, effect work/depth limits and HP capping
remain. No candidates or supported effects are skipped to lower timings, and no
new score weights or broader cross-decision cache is introduced. These are modest
deduplications, not evidence of a large frame-time improvement. The infusion memo
can add a small allocation when no repeated read occurs; live impact needs replay.

## 5. Performance evidence and new attribution

| Mode/fight | Decisions | Median ms | p95 ms | Maximum ms |
| --- | ---: | ---: | ---: | ---: |
| TB1 | 72 | 45.9 | 197.6 | 281.8 |
| TB2 | 57 | 27.5 | 111.2 | 135.1 |
| TB3 | 70 | 44.2 | 200.3 | 260.4 |
| TB4 | 28 | 31.3 | 102.9 | 117.5 |
| TB5 | 216 | 47.9 | 259.5 | 989.3 |
| RT1 | 91 | 27.9 | 170.4 | 748.4 |
| RT2 | 83 | 22.5 | 144.8 | 1335.2 |
| RT3 | 62 | 32.3 | 138.7 | 754.8 |
| RT4 | 71 | 21.5 | 180.6 | 233.6 |
| RT5 | 127 | 30.7 | 162.3 | 632.9 |
| RT6 | 105 | 18.5 | 178.8 | 737.8 |
| RT7 | 238 | 40.0 | 229.9 | 817.0 |

TB totals **443 decisions / 28,837ms**; RT **777 / 48,521ms**, plus 606 cooldown
skips. These are decision-work samples, not FPS or whole-session speed. Encounters
and successful infusion work differ from Test[3], so this is not a matched benchmark.
Lower medians would not establish correctness either.

RT35's cold 748ms decision builds 55 profiles and assesses enemies without a
collection flag. Seven other RT events above 600ms overlap collection, including
Camellia and Aeiluvetar decisions. Collection overlap does not allocate the pause
to GC. Warm TB control/boost decisions also cost 250–352ms with zero new profiles
(TB1641/1645/1797/1801/1873/1877). Long work exists beyond cold loading and active
infusions; no sole bottleneck is established.

The existing >250ms decision diagnostic now includes scalar stage timings for
context/threat, weapons/modes, abilities, kinetic/recovery, movement/toggles,
path/policy preparation, marks/boosts, infusion search and finish. Nested active
infusion appraisal time/call count is explicitly included in its enclosing stage,
not additional time. Expensive TB stand-up previews are reported separately,
because their private appraisal was outside normal decision summaries. There is
no per-frame trace, new global hook, animation-speed change or scoring budget cut.

## 6. Fire-resistance discrepancy remains open

RT1938–1945 chooses an infused Fire Blast on Vermlek; CombatLog9941–9956 shows
zero direct Fire against **Fire Resistance 10**. RT2025 similarly favors Fire
Blast on Dretch before RT2028 learns six absorbed; CombatLog11143–11157 shows
another zero-fire packet. Periodic damage uses the same reduction helper, so an
optimistic shared defence read can affect both parts.

The earlier censored-observation fix is preserved, but it does not establish the
root of this initial optimism. Native inspection rejects an uninitialized-chunk
explanation: `AllSources` initializes chunks, whose constructor resets reduction
from the runtime component's current value. The supplied logs do not expose the
actual native query, bypass state or failure at appraisal time.
Both supplied Fire Blast damage branches are native Energy/Fire actions and reach
this helper; the provider and physical-bypass paths do not skip it. Final blast
calibration cannot turn a zero reduced value positive. No missing-helper-call fix
is supported by source inspection.

`AfterTargetReduction` now reports an unavailable native estimate once for a
target/type/exception combination, then preserves the existing observed/raw
fallback. No bespoke resistance table, damage roll, defence write or unproven
math correction is added. A silent exception is now visible; the discrepancy is
**not claimed fixed**. The older gross-cost subtraction in burn-block recovery
also remains an unexercised category-reducer limitation, not a new observed failure
in these Fire-base-zero/Burning-plus-one cases.

## Verification and handover

Branch `codex/test4-logic-fixes` starts from development `f51189f` and includes
pending PR #9 head `10d2b96`, including PR #8/#7/#6, as explicit dependencies.
Main/development are unchanged by this work. This remains an unmerged test for
intended v0.3.22; UMM release metadata stays 0.3.21. See the
[test download and exact identity](../test-builds/README.md).

- **164 pure tests pass**, zero failed/skipped, including ten new geometric cases
  for whole-arrival containment, overlap/boundary uncertainty and invalid data.
- Full Release against supplied real game/UMM references: **zero warnings/errors**.
- Static/native inspection checks command lifecycle, read-only native approach
  APIs, Grease's supported spawn shape and memo identity/scope. No live actor
  command, save, path or frame-time fixture is implied.
- Compiled memo fixtures pass **13 identity/hash assertions**; five supplied Grease
  metadata gates pass. These check cache separation and supported structure, not
  native actor execution or actual veto outcomes.
- Existing native checks pass: all twenty substance activation predicates,
  Free/Swift thresholds, eighteen recognized/two withheld layouts, **486** native
  cost/quiver vectors, Rare Metal and cone metadata. Compiled activation-adoption,
  censored-resistance guards and cone bindings pass.
- Ordinary reader audit remains partial: **4,097 exports loaded**, one known
  unrelated Achaekek export failure, **1,220 ability rows / zero reader errors**.
  All normalized verdict/detail rows match the Test[3] baseline.
- Validated module MVID: `d98eb9f4-8bb4-448f-bfcb-4a701fb87368`.
- **No in-game test of this new build has run.** Prior Test[4] outcomes verify
  Burning execution in the prior build, not live behavior of this new guard.

Focused replay: verify the exact startup marker, then test useful/off/replacement
infusions, native burn, player ownership, management off and restoration in both
modes. For RT spell safety, enable Avoid friendly fire and use a flat-ground
compact melee approach inside a proposed Grease circle; inspect `[Walk]` and native
ally saves. Compare setting off, Selective/immunity, an already-in-reach attack,
boundary overlap, ranged and sloped cases. Movement/target commitment should retain
its existing behavior; uncertain cases are not guaranteed protection.

Compare cold/warm Orvath, summons and ordinary companions with the same save,
settings and mods. Include idle portrait selection and any slow stand-up turns.
Send complete AI logs plus the **native CombatLog**, ZIP filename/startup marker
and observations: native saves, resistance sources and actual damage continue to
provide essential evidence unavailable from command outcomes alone. Fire Blast
before/after resisted hits remains a specific replay lead.
