# outcome1 playtest review

16 November 2026. User-supplied AI and combat logs, module
`fbc220a5-e4af-42eb-b386-e72ec1b61247`, match PR #76's checked source `f5ccb40` and outcome1 package receipt.
The author confirms spawns and rests **between fights**. The log also identifies RandomReinforcementsPerEncounter
spawns; do not assume every new enemy was an ordinary encounter or a manually spawned one.

This review starts from development `980cf26`. The diagnostic correction is independent of PRs #74–77; the author
separately authorized reviewing and merging satisfactory open PRs after this review. A final combined package and
integration receipt must identify the exact integrated source, rather than relabelling outcome1 or rebuilding its DLL.

## Input identity and observed behavior

[Input receipt](competence/OUTCOME1_PLAYTEST_INPUTS.json), [Invocation ledger](competence/OUTCOME1_INVOCATION_LEDGER.json).
Rows refer to physical LF lines in the original files. Combat text contains embedded CR characters; splitting on all
Unicode line breaks would produce different row numbers. Raw logs are private.

| Observation | Evidence / limit |
| --- | --- |
| Nine encounters, 666 issued and 666 matching terminal commands | AI log fight boundaries 27–891, 950–1545, 1592–2056, 2096–2800, 2857–3451, 3496–4265, 4318–4759, 4810–7424, 7475–8101; 225 TB, 441 RT issues |
| No recorded AI Control exception | No `Exception`, `ERROR` or `FAILED` lines; this is not proof of complete gameplay correctness |
| 102 interruptions before DidAct | 46 moves, 46 ability commands, 10 attacks; interruption alone is not a mod failure or evidence of a spent spell |
| Three successful RT Release Grapple commands | Wenduag issue/terminal rows 3346–3347, 4643–4644, 7807–7808; no TB release or actual leap acceptance in this pair |
| Summon takeover and commands | Brimorak and Babau are explicitly driven; 17 issued/terminal commands, 10 with DidAct, including breath, dispel, attacks and approaches |
| Six issued Coups, all DidAct | Unlike gaps2; outcome1 does **not** include PR #75 |
| 23 issued fusion casts, 15 with DidAct | 17 Snowball/Shocking Grasp, five Incendiary Runes/Color Spray, one Explosive Runes/Hideous Laughter; no issued Frigid Touch fusion |

### Fusion outcome support

AI rows 537–539 recognize Incendiary Runes/Frigid Touch with root Reflex and report the same 0.40 control envelope
on the failed and passed outcomes for the first qualified recipient. This verifies discovery and forecast admission
in the played build; **it does not verify delivery or stagger on a passed save**, because no such cast was issued.
The two Sleep fusions are withheld, as expected before PR #77. Several old fusions act in both modes; a ray miss or
an interrupted cast is not a new spell-recognition failure. An incidental Skeleton immune to Color Spray is not,
by itself, proof that the whole burst had no useful recipients. No new fusion scoring multiplier is justified from
this pair alone. The pending F4/F7 code is reviewed separately before integration.
One native `Fail`/DidAct record is Orvath's Grease (AI 4939); combat row 26705 records his failed concentration
check, 16 versus DC 17. The engine already models concentration risk; a possible cast is not a guaranteed cast.

### Coup de Grace

Cambion Bard receives a **13.2 m** command radius at AI rows 1363 and 7863. The combat log shows its **Claw** dealing
2 damage and a DC 12 Coup save (physical rows 4178–4195 and 33123–33140). This is now a played witness of the range
supplier/delivering-weapon inconsistency identified in PR #75. The logs do not give the actual separation at that
moment, so they still cannot establish a distant hit. Do not call the unplayed PR #75 correction gameplay-accepted.
Brimorak's controlled-summon Coup uses a 1.6 m radius and acts (AI 7765/7876); this pair does not demonstrate a
summon's distant execution.

### Invocation

68 issues, **66 DidAct**, two interrupted: 17 first placements, 31 settled moves, eight early overrides and ten
holder-unseen cases. All eight identified overrides meet the logged 5-point override threshold; no identified
under-settle or same-stat stacking violation. Some casts take a buff from a recipient absent from the engine's
current ally list. The ledger marks uncertainty; it does not turn a recent earlier HP row into proof that the ally
was still available when the cast was chosen. Free casts can occur in a burst without spending the main action.
There is no evidence here for a new per-fight cap or a new joint assignment rule. The casts, tiers, current values,
source identities and replacement/return chains remain available in the derived ledger for further review.

### Performance and unreachable geometry

**1,060 verbose path-end refusal rows** all name goal `(93.6,45.1,48.9)`, the elevated Treasure-Hunting Necromancer.
The selected endpoints are roughly 4.2–4.3 m below it. Native reach/sight checks reject those paths; some plain
advances later hit the existing stuck guard. A static block at that coordinate is not evidence that teleportation,
a generic sight bypass or disabling safety is a valid repair. No movement, commitment, pathfinding, retreat,
retry-memory or stuck-cancellation policy is changed by this task.

The detailed witness was eagerly formatted inside `PathEndVerdict` even for a successful or quiet-mode check, and
every failed preview printed it again. The 1,060 lines contain 309,919 bytes in this log. The correction below
addresses that repeated diagnostic work, not the navmesh/height obstruction or the whole decision-time workload.

Slow-decision rows: Orvath 45 (252–1560 ms), arcan 15 (260–1409), Seelah seven (252–1338), Camellia four, Lann three,
Bandit one, Drow Magus one. Warm ability/context work remains material. `stage heap KB` is a `GC.GetTotalMemory(false)`
delta, **not an allocated-byte counter**; collections and other activity affect it. These data cannot establish a
Unity speedup from removing logging work. Drow Magus is explicitly in Pure Mage mode (AI 331/333), so not enabling
Spell Combat/Spellstrike for it is the configured policy, not proof of a missing melee-Magus implementation.

## Implemented diagnostic correction

`PathEndVerdict` still calls the native `IsUnitCloseEnough`, mechanics distance and existing `JudgePathEnd`, then
returns exactly the existing classification/shortfall. Its witness is now a scalar struct; successful and nonverbose
checks format no witness string. Failure text, refusal, native path checks and command outcomes are unchanged.

Detailed refusal formatting runs only in verbose mode, after a diagnostic-only gate keyed by fight, caster identity
and the exact logged query geometry (goal, radius, sight requirement and reason). First observations and material
changes remain visible: 0.25 m endpoint/distance, 0.5 m path length, or a changed trimmed-point count. Bounds are
128 casters, 16 query identities per caster and five reports per query per fight. At capacity old suppression state
is preserved; it is not cleared to produce a fresh burst. This state contains strings/scalars only, never an actor,
context, native path or a cached eligibility result. The native checks continue to execute when a detail is quiet.
Actual command rejection/terminal logs and stuck snapshots remain available.

The unchanged C# policy replayed over the **rounded logged fields and display names** retains 33 of the 1,060 old
rows (9,532 bytes of the old text, before the new short bound annotation). This is a log-field projection, not a live
native replay: full-precision geometry and native IDs may differ. Suppressed warmed policy checks allocate zero
bytes in the game-independent test. No claim about frame time or total decision allocation follows from that.

[Checks](competence/OUTCOME1_PLAYTEST_CHECKS.json) distinguish source/build verification from the played outcome1
observations. The final integrated package/receipt follows the separately authorized PR review.

## Follow-up replay and limits

- Keep the startup module ID and paired logs. In TB and RT, repeat the ledge/obstructed target case and a reachable
  enemy. Expect first/changed native refusal details, reduced repetition and unchanged refusal/ordinary command
  outcomes. The diagnostic gate must not make an unreachable path legal or suppress an actual rejection/terminal.
- Play the PR #75 Coup cases with a bow and melee limb and an ordinary summon; retain legitimate reach and body size.
- For F4, obtain an actual saved Frigid Touch fusion cast, including a passed save. For F7, play the combined package's
  specific pooled-fusion replay after its independent review/corrections. Do not use outcome1 as acceptance of F7.
- TB grapple release and actual leap remain unplayed. The broader holder-cost/safety and monster-grapple limits
  remain. Large-party warm ability work needs a matched, separately scoped performance investigation.
- A future approach improvement should distinguish an unsafe/blocked endpoint from a globally unreachable target,
  preserve native sight/height, and invalidate any remembered result on relevant geometry/state changes. A broad
  target blacklist inferred from one preview failure would be unjustified; no such cache is introduced here.
