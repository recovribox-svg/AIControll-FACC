# 0.3.22 follow-up: condition work during scored casts

Branch `codex/decision-context-cost`, based on development `3e5e37dd1e3bb1e8fa54180853877a8572721e3e`.
Code revision: `8043a34028254ec7116fa389493aa356b9937b0b`. This includes and preserves Claude's deferred-scoring
correction after PR #3: cooldown positioning and next-round Gather Power still receive otherwise-unaffordable options.

## Supplied gameplay evidence

The author reports improved but remaining stalls on the low-end laptop, and noticeable stalls on a mid-end CPU,
especially during Orvath's turn in TB. Only mid-end-labelled logs were attached in this follow-up. Their version label
does not establish an exact source/binary checksum. Encounters differ, so these are not a controlled CPU comparison.

| Log | Orvath timed summaries | Median Orvath summary | Slowest full decision |
| --- | --- | --- | --- |
| `Turn Based Log v0.3.22-MidEndCPU.txt` | 26 | 155.0 ms | 429.1 ms |
| `Turn Based Log v0.3.22-MidEndCPU-2.txt` | 23 | 190.5 ms | 249.2 ms |
| `Realtime Log v0.3.22-MidEndCPU.txt` | 90 | 164.0 ms | 977.1 ms |

Whole-fight medians across all companions are much lower (TB 10.2 / 6.0 ms; RT 2.6–7.1 ms). Those averages hide
Orvath's recurring long decisions. The old 250 ms detail threshold also hid most of them.

The RT outlier reports **abilities 893.7 ms**, **Sleep 773.4 ms**, **439 native parameter queries / 12 reused** and
**282 execution contexts**, with garbage collection during that decision. This establishes the expensive stage and
allocation pressure, not the exact share caused by GC. Normal modifier searches are generally short. Four TB
movement-only/nauseated decisions nonetheless spend 54–59 ms in modifier search without scoring any aim.

The logs now show Burning Infusion comparisons and native free activation, providing gameplay evidence that the
previous calculator compatibility change reaches this installed setup. The continuing WrathPatches/Isekai channel
owner-blueprint warning remains separate. One Stinking Cloud path refusal is the dispatcher's intended veto.

## Changes and correctness boundaries

- **One detached condition context per scored cast/aim.** Safe condition readers visit recipients with native
  `GetDataScope`; they no longer rebuild the same cast, rerun parameter/rank queries and copy native rulebook history
  for every guard/recipient. Native parameter calculation still builds the snapshot. This cache is private to
  condition evaluation: damage providers and general execution-context callers continue receiving fresh contexts.
  Different ability objects, ambient source/target/event, nested aims and infusion contexts remain distinct.
- **Allocation-free guard-result lookup within an aim.** Reference keys replace formatted strings, GUID/item/book
  formatting and repeated auto-metamagic discovery just to find a cached condition result. A new scored aim owns a
  new dictionary, including different heights/orientations. Infusion source identity participates in the key.
  Legacy out-of-aim lookups retain the existing frame cache, with exact point/height/orientation formatting.
- **Restore temporary shared values.** `WithSharedValues` used to leave its high or average Hit Dice test values
  written into the context, including an infusion's supplied fact context. It now restores every touched value in
  `finally`, covering equal low/high early returns, average evaluation and exceptions. This is a correctness fix
  found while tracing the repeated condition work, not an effect visibly proven by these logs.
- **Invalidate all condition contexts/results on modifier changes.** Existing `ClearConditionCache` also clears
  every active nested aim scope. Disposing an aim releases its references and restores the caller's scope.
  Nothing survives into a later command/decision. Native ambient parameter-cache bypass remains conservative.
- **Skip impossible modifier work.** Movement-only nausea returns before native kinetic queries. Immediate modifier
  comparisons also skip decisions with no usable blast action or actionable projection. Current free/swift blasts,
  learned Quicken and TB next-round gathering remain eligible. Gather preparation still runs before the immediate
  action gate; Claude's deferred scoring remains. A neighborhood containing only the current configuration skips
  parity/projection setup. RT non-gather modifiers avoid the irrelevant native gather-timing safety query.
- **Measure the remaining tail.** Slow-decision detail starts at 100 ms. Logs include ambient parameter bypasses,
  reused condition contexts and guard-cache hits. Per-ability timing uses timestamps without allocating a Stopwatch
  for every ability. Reuse the same verbosity/settings when comparing builds.

No scoring weights, preference trees, target-count cuts, modifier-search limits or native game formulas were added
or changed. Re-evaluation after real commands is retained. Candidate pruning applies only where commands/options
cannot be used, preserving preparation and action-changing alternatives.

## Verification

- **143 unit tests pass**, zero failed/skipped.
- Mod and ProfileAudit builds against supplied real game references: **zero warnings/errors**.
- `ProfileAudit --kinetic-checks`: **4,098 loaded, zero failed; 1,220 rows, zero reader errors**.
- Native integration: 201 condition-context requests reuse one intercepted cast-context build; separate ability,
  nested aim, recipient data scope, infusion source and actual modifier entry/restoration stay isolated.
  Main-target predicates match a fresh native context, including native point-wrapper equality semantics.
- Guard-hit tests seed scoped results and exercise the actual lookup with distinct reference keys and nested aims.
  Full guard-miss evaluation on initialized actors remains a gameplay check; the offline process has no entity registry.
- Shared values are restored on early, average and thrown-checker paths. Movement-only kinetic entry points return
  before any unit query. Deferred-slot tests verify an explicit deferred request still revisits native availability.
- Prior hook rollback, inactive fast exit, query isolation, native calculator dispatch, inert burn cost and pre-dice
  dispel checks pass. The harness uses isolated engine-bound rewrites, including a fixed frame-count getter for the
  offline cache checks, and intercepted returns where live actors are unavailable. None are shipped.

These checks establish reuse/isolation and reader behavior, not a frame-time measurement. Cold profile construction
(the first TB decision built 60 profiles), enemy assessment, native single-call cost and other scoring remain possible
sources of delay. There is still no whole-decision deadline. No post-fix gameplay speedup is claimed.

## Replacement package and replay

Test package: `AIControll-FACC-context-8043a34-test.zip`, provided in the task chat.

- ZIP SHA-256: `c63e64043be1981e7dcf79ddcc3cac14ec19a9ef340c9a0bc8565ce9b156138e`
- DLL SHA-256: `a453df7314bdd7260312cfc6be0878b9bf4606760f684fec07e65aa5c0749558`
- Contains only mod DLL, existing Info.json and test identity note. Info.json remains repository version `0.3.21`;
  identify this experimental build by filename/commit/checksum. No release or main update.

After Claude's review, replay the same fights/settings/party/mods in TB and RT, ideally on both CPUs:

1. Orvath's Sleep/Color Spray/Entangle choices, eligible vs excluded recipients, main-target branches and party safety.
   Compare `[Perf]` ability times, native parameter/context counts and condition/guard reuse. Keep complete logs.
2. Consecutive Arcane Reservoir boost then cast: the second decision must read the new DC rather than retain the
   preceding command's snapshot. Check Burning Infusion effects, burn, free activation and post-fight restoration.
3. Nauseated movement-only turns: retain cloud escape/withdraw, with no kinetic search. After nausea ends, normal
   blast and modifier comparisons must return.
4. TB next-round Gather Power after spending Standard; current Free/Swift blasts and projected Quicken with Swift
   available; RT cooldown positioning. Verify Claude's deferred-scoring behavior remains.
5. Player-owned toggles / AI toggle management off; a non-kinetic caster; cold first decision vs later turns. Record
   exact build identity and return full `[Perf]` / `[Kinetic]` / `[Tactics]` / `[Toggle]` logs if any stutter remains.
