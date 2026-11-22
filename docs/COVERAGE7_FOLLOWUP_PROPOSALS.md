# Coverage7 follow-up proposals: performance and movement

22 November 2026. Proposals only; movement changes require author approval. The coverage7 corrections include no movement or touch-pruning change; this document describes future work.

## Confirmed baseline; do not call this a before/after

Parsed the verified `Log.txt` using Python `splitlines()` (the same 15,240 numbered logical lines used by the review).
There are 69 `[Perf] slow decision` rows. All six rows over one second explicitly say that memory clean-up ran:

| Log row | decision ms |
| --- | ---: |
| 4880 | 1298 |
| 6854 | 1085 |
| 7207 | 1005 |
| 8339 | 1176 |
| 12381 | 1158 |
| 14369 | 1079 |

The measured `costliest:` duration is the whole ability callback, not a timer around free-side search. On rows without
that clean-up marker, Light is reported seven times (5.0–153.1 ms); Guidance three (113.9–182.0 ms); the single-recipient
Inflict Light Wounds three (67.0–187.5 ms); Cure Light Wounds once (124.7 ms). This list is censored: only the three
costliest abilities on decisions above the existing slow threshold are reported. It cannot establish an ability-wide
mean or percentile or a full allocation count.

Examples worth retaining in the public review:
- 8095: Drow Magus, Light 153.1 ms / 0 contexts, total decision 318 ms, 23 crowded approaches / 12 without a free side.
- 12674: Seelah, Guidance 182.0 ms, total 275 ms, 30 crowded approaches / 12 without a free side.
- 7076: Camellia, Inflict Light Wounds 187.5 ms, zero offered options, total 344 ms, 25 crowded approaches / 10 without a side.
- 12737: Lann, Cure Light Wounds 124.7 ms, zero offered options, total 253 ms, 24 crowded approaches / 10 without a side.

**Heap qualification:** `DecisionTimings.Mark` and `CombatMath.HeapMark/HeapSince` use `GC.GetTotalMemory(false)`.
These are changes in observed managed-heap occupancy, not bytes allocated by an actor, stage or decision. Other game
activity, deallocation and GC timing can affect them. Positive occupancy growth supports investigating allocation,
but a 10–40 MB positive delta does not prove that only, or exactly, 10–40 MB was allocated. A negative stage delta
concurrent with clean-up is not a negative allocation count. The review's unconditional allocation claims should be
reworded as heap growth / a likely allocation-pressure issue.

## Code confirms work ordering, not the whole attribution

`TacticalEngine.EvaluateAbility` computes range, then calls `PlanApproach` for an affordable out-of-range recipient,
then `RealTimeWalk` / `MergeRisk`, then `ScoreAbility`. `PlanApproach` is decision-local memoized by target, rounded
range, exposure flag and advance mode; a touch ability can be the first user of each recipient's entry, and its timer
therefore includes work later abilities reuse. It prices the direct spot, asks whether a body takes it, and may sample
16-direction rings with native cell/position/hazard checks. `SpotRules.PricedSpots` bounds expensive pricing when
only crowding is the reason; area/swarm cases have their existing ring bounds.

That makes low-value touch searches a credible optimisation candidate. It does **not** demonstrate that all 153 ms
of Light was its spot search. Zero context creations is supporting evidence, not an exact phase measurement.

## Recommendation: defer code under the author's measurement condition

The environment has the native DLLs, blueprints and saved actor data, but no runnable game scene / initialized Unity
world / native pathfinding replay. The ZIP save is not an executable headless world. Replaying pure scalar helpers or
mocking every path query would measure the mock, and cannot establish a before/after for this bottleneck. No speed-up
is claimed. No touch-pruning optimisation has been implemented.

A practical future slice is:
1. Add bounded decision-local scalar timings/counters around *cache-miss* `PlanApproach`, its spot-search portion, and
   recipient scoring, emitted only in the existing slow row/summary. Attribute cache hits separately. Do not log each
   recipient or hold actors beyond the existing context. If actual allocation throughput is wanted, first verify an
   appropriate supported Mono profiler/API; retain occupancy terminology until then.
2. Select a dense-party save encounter and record baseline with that exact instrumentation before changing pruning.
   Use the same game/mod settings, party, formation, spellbook and spawn batch; separate cold-profile and warm runs.
3. Extract a narrowly proven, recipient-intrinsic upper bound before path planning. Start with structurally read,
   single-recipient helpful touch buffs, no area/aura, no caster side effects or replacements, no unknown branches,
   no held-state effects and no geometry-dependent growth. Never identify Light/Guidance by name or GUID. Fail open
   to the existing planner on unread/custom cases. Preserve all candidates that could reach an action threshold,
   Progressive viability/boss alternative or the existing main/buff selection margin.
4. Keep the existing exact scoring/path/risk calculation for survivors, then compare candidate outcomes to the
   baseline on the same captured decisions. A pure proof test must establish that skipped upper bounds cannot beat
   their admissible alternatives; do not treat a score at the caster's current position as an upper bound for an
   approach spot (applicability, nearby effects and geometry can change).
5. Run the same encounter TB and RT, several warm repetitions; report decision/ability/plan cache-miss median/p95,
   maximum searches and rule/context counts, plus GC-separated wall time. Claim only the measured scope. Reject the
   change if selected actions change outside the intentionally skipped dominated options or emergency heals disappear.

The existing evaluator is not ready for a trivial `if (value < best) continue`: it needs the approach point to score,
can return best/viable/boss aims separately, creates infusion prospects, and carries scoring metadata. The bound
should be explicit and narrow rather than an optimistic duplicate `ScoreAbility` call with a fake origin.

## Refused advances: verified endpoint/path mismatch, corrected memory claim

The observed issue is real: chosen endpoints can be outside Corrosive Bomb while the **native path** enters it.
`PlanApproach` tests endpoint/straight-segment danger and native valid spots, while
`CommandDispatcher.RealTimeWalkRefused` obtains the native path and checks its traversed segments. A successful
endpoint test is not a proof of a safe actual route. The dispatch refusal remains valuable.

However, the original page says nothing remembers the refusal. **The code already does:**
- `TacticalDriver.RecordRejected` adds the exact candidate key to `TurnMemory.Failed` for the round.
- RT dispatch failures starting `WalksIntoAreaFailure` call `CombatMemory.NoteRefusedWalk`.
- `RefusedWalks` retains exact `unit.UniqueId|optionKey`, origin and game-time expiry for **12 seconds**, effective
  while the unit remains within **1 m** of that origin; `ResetCombat` clears it.
- `SilentlyFailing` consults it before selection; `IdleAdvanceCandidates` uses that same predicate.
- Selected preflight area failures are removed within that `SelectedApproachFilter.Apply` invocation; they do not
  enter `TurnMemory.Failed` or the 12-second dispatch-refusal map. The persistent preflight feedback is limited to
  the existing specific additional-AoO and too-far cases, not area refusals.

Rows 8656/8661 show two `Advance->Venomhodag` choices, and 8697/8702 two `Advance->Dire Bear` choices. The encounter
contains same-named enemies. At 8687 one Schir issues an attack, followed by another same-named Schir's refusal rows.
Therefore repeated display labels alone do not prove an identical actor/key ignored the existing memory. Later
RT2→RT3 rows also cross round boundaries. Exact actor/target identifiers and game time are not printed on these rows.
Do not add a second generic cooldown or promote an area refusal into global target-unreachable evidence.

## Movement proposal, requiring author approval

Preferred first slice: reuse native selected-route feedback to stop repeated *unproven* endpoint plans; retain the
existing hazardous-path refusal. Do not automatically accept a priced hazardous crossing yet.

- Restrict initial scope to RT plain offensive advances selected after useful offensive walks are refused. No retreat,
  swarm-exit, commitment or ordinary attack-walk rule changes.
- Reuse the same `CommandDispatcher.CanPrepare` path/area test and native danger predicates. Share a fixed work budget
  with selected-plan preparation and dispatch; do not ask for a path for every ring point or every enemy.
- A bounded alternative can try at most one next already-generated endpoint when the first selected native path crosses
  a harmful area. A candidate must pass the same actual path/arrival test. If the budget is spent, wait or retain an
  independently feasible action; do not certify an unchecked endpoint as safe.
- Retain only per-decision endpoint/route outcomes. Reuse existing cross-decision refusal memory; if extending its key
  or lifetime, establish position/goal/area-change invalidation first. A changed body position or an expired area can
  make a previously refused route valid, so a whole-fight ban is inappropriate.
- Optional bounded diagnostics: on a material refusal log exact short actor/target IDs, selected endpoint, game time,
  and whether a recent exact-key refusal was active or invalidated. This would resolve the same-name ambiguity without
  a per-frame log stream. Do not make the diagnostic itself a new path query.
- Tests: safe endpoint/unsafe path; second endpoint with safe path; no-answer retains existing policy; queries capped;
  same names/different IDs do not share failures; round/fight cleanup; newly valid route retried after invalidation;
  no identical unsafe dispatch retry inside one decision; ordinary priced attack crossing remains unchanged.
- TB regression: existing TB path trimming, one-move reach, touch delivery and selected-path budgets are unchanged.

The alternative of pricing every advance crossing is a broader policy choice: it would deliberately allow movement
through some currently forbidden harmful paths. It needs a separate comparison of residual area effects, remaining
walk/time and the future attack's gain, with fresh native path sampling. That is more work and carries greater risk
than bounded feasibility feedback, so it is not the recommended first change.
