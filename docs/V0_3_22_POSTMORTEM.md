# v0.3.22 withdrawal, rollback and failure analysis

## Decision and current handover

The author cancelled the entire v0.3.22 attempt and explicitly authorized Codex to
revert development, delete its feature/test branches, close the outstanding work,
and preserve the Claude/Codex workflow. This supersedes earlier handover documents
that recommend testing or integrating these changes. No further v0.3.22 fixes or
feature development should be resumed from those instructions.

Target branch: `claude/dreamy-pasteur-b9h3rn`.
Stable baseline: tag `v0.3.21`, commit
`3d280bd3e3800f1db671112f1be1460ba05d7de5`.
Development before rollback: `ddd071ff797763bd0b2316e04c8d9c110efd5085`.
Unmerged PR #5 head: `3dc6463ac2d974a392c61faca371ac88d27b8075`.

This is a history-preserving revert, not a reset or force-push. Development's
commit ID differs from the release because it retains the collaboration setup,
verification tooling, historical evidence and this report. Its deployed source,
project, manifest, build scripts and unit-test sources match the stable tag.
`main` and release tags are not changed. Existing v0.3.22 test tags remain historical
references only; they must not be interpreted as recommended builds. In particular,
the remote `v0.3.22-test3` tag already pointed at the stable commit before rollback;
tag names alone do not identify the experimental DLL that a user installed.

Earlier documents under `docs/KINETICIST_*`, `DECISION_CONTEXT_PLAYTEST.md` and
`INTEGRATION_HANDOVER.md` are retained as historical evidence and to preserve the
workflow records. Their "current build", integration and replay recommendations
are withdrawn. `CLAUDE.md` is preserved byte-for-byte, including its historical
PR #4 handover paragraph; this report is the subsequent handover. PR #5 is closed
unmerged. Claude should start future work from the rolled-back development head
and the normal contribution rules, not restore the cancelled branches.

## What was attempted

| Stage | Commits / PR | Work and result |
| --- | --- | --- |
| Collaboration setup | PR #1, merge `cbc710a` | Added AGENTS.md, CONTRIBUTING.md, PR template and CLAUDE workflow link. Preserved. |
| Environment and baseline | `055c321`, `0f10b44`, `d1ffb32` | Inspected exported blueprints and supplied game assemblies; added stricter partial-audit reporting. Some blueprints and live Unity execution remained unavailable. |
| Kineticist expansion | `17cf71c`, PR #2 merge `d456882` | Added hypothetical infusion/metakinesis facts, native burn-cost previews, modifier search, Gather Power plans, area/delivery/effect readers, dispatcher toggle execution and restoration. Shared readers and scoring also changed. |
| Review corrections | `175fad3` | Isolated optional Harmony installation and rollback; introduced cooperative 40 ms / 64 aim / 128 state limits, native-query reuse and inactive-hook fast gates. Did not prove safe global generic detouring or smooth selection-time UI. |
| First playtest optimization | `ab0c9ea`, PR #3 merge `2d498ad` | Pruned unavailable action slots, cached decision-local parameters/discovery, broadened native calculator compatibility and added timing diagnostics. User reported improvement but persistent stutter. |
| Deferred-option correction | `3e5e37d` | Claude corrected over-broad slot pruning so RT positioning and TB preparation could still value otherwise-unaffordable options. |
| Second optimization | `8043a34`, PR #4 merge `fcc64b1` | Reused condition contexts/results, restored temporary shared values, skipped impossible modifier searches. `1fd1b98` repaired the shared offline frame-counter stand-in. Codex completed this integration under explicit author delegation while Claude was unavailable. |
| Download distribution | `ddd071f` | Added experimental ZIP and prominent development download link because chat downloads were inaccessible. Removed by rollback. |
| Unmerged follow-up | PR #5: `3230ba4`, `7110d48`, `f231e66`, `3dc6463` | Improved empty-root cache reuse, added native-default group reuse, a shared Sleep HD forecast, descriptor-mask correction and read-only pacing/build diagnostics; published test ZIPs. Never integrated into development. Withdrawn. |
| Final investigation | local `codex/live-code-audit` | Read the major audit brief, mapped live routes, analyzed test4 logs and reproduced a generic fact-lookup defect offline. No production changes from this investigation were committed or integrated. Cancelled by author. |

Initial/full dump counts and test totals in older handovers describe their specific
fixtures and revisions. They are not blanket acceptance of the feature. One
later fresh shared-runner check loaded 4,097 exports with one explicitly allowed
engine-bound failure and produced 1,220 ability rows; it was PARTIAL, not complete.

## Technical failures and evidence

### 1. Unsafe global closed-generic Harmony detour

**Confirmed offline semantic corruption; live Mono impact not directly measured.**
`KineticPreview.InstallPatches` patched
`EntityFactsProcessor<Buff>.HasFact(BlueprintFact)`. This belongs to a generic game
class whose code can be shared across reference-type instantiations. Restricting
the prefix's behavior to a selected actor did not restrict the detour itself.

A local probe used the supplied game classes, one active Buff and one active
Feature in the same EntityFactsManager, and the production `BuffExistence.Prefix`.
With preview inactive, the results were:

| Query | Before patch | After patch with preview inactive |
| --- | --- | --- |
| Buff collection contains its Buff | true | true |
| Buff collection contains the Feature | false | false |
| Feature collection contains its Feature | true | **false** |
| Feature collection contains the Buff | false | **true** |

The underlying manager still contained correctly typed Buff and Feature caches.
Unpatching in the warmed probe process did not restore the Feature answers.
The experiment isolated the single generic detour; it did not require an active
hypothetical preview. This defeats the assumption that an early inactive-prefix
return proves global safety. That test only verified the prefix branch, not the
semantics of other generic instantiations after Harmony installed the wrapper.

The probe ran on .NET 8 with staged game dependencies, not the game's Unity/Mono
runtime. It establishes a serious unsupported assumption and reproducible defect
in that environment. It does not establish exact live frame-time attribution,
which actors were affected in game, or that this was the sole stutter cause.
No game assembly, decompiled source or engine stand-in is committed in this report.

### 2. Global preview hooks escaped the decision/control boundary

**Confirmed source behavior and native UI reachability.** Optional kinetic hooks
were installed at Main.Load and remained installed outside TacticalEngine.Decide.
They intercepted fact checks, metamagic, native burn-cost calculation and gather
mode. Per-character AI deselection did not remove these hooks. Main.OnToggle also
did not unpatch this optional owner. An inactive preview flag cannot erase Harmony
wrapper overhead or guarantee generic-body safety.

Native action-bar resource refresh can call AbilityData.ResourceCost and native
kinetic burn-cost calculation without a combat decision. Thus a portrait-selection
regression can occur outside the only path measured by the tactical timers. No
explicit mod portrait-selection handler was found. Tightening the modifier-search
budget alone could not bound that native UI path.

The author's correction matters: the observed unsuccessful "off" test was
**Orvath's per-character AI deselection**, not global UMM disable. The settings
window was closed. A fully exited/uninstalled comparison was pending when the
rollback was requested; no result is invented here.

### 3. Remaining synchronous work was not covered by the advertised search budget

**Confirmed logs and source; exact internal call attribution incomplete.** In the
latest f231 DLL logs, 81 of 85 slow RT Orvath decisions reported a burn-recovery
stage (median 56.0 ms, maximum 103.8 ms). All 30 slow TB Orvath decisions did so
(median 69.45 ms, maximum 141.7 ms). The corresponding slow-decision medians were
139 ms RT and 187.5 ms TB; maxima were 512 and 382 ms.

`AddKineticCandidates` entered native blast availability through
`blasts.Any(a => a.IsAvailable)` before proving a recovery action was needed.
The named recovery stage could therefore be expensive without proposing recovery.
These measurements identify a recurring stage, not proof that this one call
accounts for its entire duration. Ordinary modifier-search stages were often
only 1–2 ms. A separate budgeted search took about 380 ms with one aim and no
published alternatives during a collection-marked interval. Cooperative checks
cannot interrupt a single native call or stop-the-world collection.

Initial feature work underestimated the cost of repeated native queries across
abilities, variants, targets, orientations and hypothetical states. Later caches
removed demonstrable repetition but did not bound the full decision or UI refresh.
Low whole-party medians hid the recurring actor-specific tail that bothered the
author. Describing a bounded sub-search must not imply a bounded frame.

### 4. Pruning and caching introduced correctness hazards

**Confirmed corrections in commit history.** Early slot pruning could omit options
needed for deferred RT positioning or TB Gather preparation; Claude corrected this
in `3e5e37d`. Native rulebook roots could rotate even when empty, defeating expected
cache reuse until PR #5. Condition/shared-value trials could leave temporary values
in their context; `8043a34` added restoration in finally. That latter issue was
found while extending shared code and is not claimed here to originate exclusively
in v0.3.22; the feature expanded the contexts on which it mattered.

These are distinct failures: a cache can be ineffective without returning wrong
values, while over-pruning can suppress useful candidates. Passing selected
availability/affordability equivalence tests did not prove equal full decisions,
native actor behavior, third-party calculator parity or correct UI fact semantics.
The rollback removes the entire attempted optimization layer, including its fixes,
instead of retaining selected portions on top of the requested stable baseline.

### 5. Effect/score coverage was broader than the acceptance evidence

**Confirmed reader differences and a corrected regression; other score findings
remain hypotheses unless stated otherwise.** PR #5's comparison found 42 changed
reader rows among 1,220: 29 modifier configurations, one Gather preparation,
three newly recognized debuffs and nine former guessed buffs now valued at zero.
The other 1,178 matching rows do not establish decision-score equivalence. Zeroing
unread effects reduced guesses but also left some defensive talents incompletely
valued. A combined cold-or-electricity descriptor check wrongly required both
flags; PR #5 corrected it to the native intersection semantics.

The unfinished audit also raised possible double charging of conditional save
coverage, repeated nonstacking buff applications, Gather opportunity pricing and
signed friendly-fire aggregation. Some appeared pre-existing. They were not fully
validated or repaired before cancellation and are not asserted to explain this
regression. Future work must reproduce any such issue independently on v0.3.21.

### 6. Validation and packaging did not provide an adequate safety gate

**Confirmed process failures.** Offline checks passed up to 143 unit tests and many
native fixtures, and real-reference builds compiled cleanly. None exercised live
Unity/Mono selection-frame behavior. Several native fixtures intercepted actor or
engine-dependent work; inactive-prefix checks missed cross-generic corruption.
Partial blueprint checks were at times presented too broadly before their failure
handling and fresh-run setup were corrected. The shared runner also initially
omitted a frame-counter stand-in used by an ad-hoc harness. Reproducibility had to
be repaired during PR #4 integration.

Experimental builds retained Info.json version 0.3.21 under the normal release
policy, which made UMM's displayed version insufficient to identify a test DLL.
Earlier test3 logs did not match the expected diagnostic markers despite the
author downloading the stated ZIP; the cause was not established. Later build
hash/MVID markers confirmed the latest test4 DLL. Future experimental identity
must be unambiguous before comparisons, rather than inferred from a display label.

The latest file named "Turn Based ... test4" actually records four RT fights;
Log.txt contains two TB fights after an initial RT transition. The report uses
logged mode markers. Changes in encounters, thresholds and warm caches prevent
using lower medians as proof of equivalent tactical quality or speedup.

## Unresolved attribution: do not invent a root cause

The new global generic hook is a concrete correctness defect and a plausible
selection-boundary contributor. Recurring synchronous kinetic availability work
is a separate demonstrated performance problem. Neither has been proven the sole
cause of Orvath's selection stutter or native summons' animation slowdown.

The logs contain no AI Control exception and no usable slow unmanaged-summon-brain
sample. WrathPatches reports a failed CurrentSelectedCharacter patch at startup;
this is relevant external-mod evidence, not proof of responsibility. TB logs also
show Unity timeScale x3 while configured TB multipliers remain x1. The read-only
pacing logger deduplicates unchanged values, so the actor label does not prove
an actor-specific speed setting or identify which mod/native path wrote it.
No intentional combat-animation acceleration was established in this investigation.

## Why the attempt failed and what must change

The architecture expanded from a class feature into global native hooks, detached
fact emulation, shared effect readers, caching and action planning in one attempt.
The tests validated individual projections and reuse rules more strongly than the
live system boundaries those projections depended on. Performance work then
continued inside decisions while the decisive idle-selection symptom could occur
outside those decisions. Repeated experimental improvements did not establish
acceptable end-to-end stability. Codex owns these implementation and validation
gaps; the rollback does not blame the author's CPU or supplied logs.

Future implementation rules:

1. Start from the rolled-back development head. Reintroduce no cancelled feature
   wholesale. Require a new focused task and an independently reviewable design.
2. Prefer narrow nongeneric integration points. Before any generic Harmony hook,
   verify all relevant closed instantiations on the actual supported Unity/Mono
   runtime. An inactive prefix is not a sufficient equivalence test.
3. Include idle selected/unselected actors, out-of-combat UI, per-character off,
   global off, fully restarted/unloaded mod, summons, save/load and both combat
   modes in the baseline. Keep saved burn/buff state distinct from loaded code.
4. Measure cold and warm actor-specific tails and allocations; include native
   entry points outside Decide. State clearly which work a budget can and cannot
   bound. Admit a smaller feature if the synchronous cost remains unacceptable.
5. Separate native rule parity, scoring coverage, execution/ownership and frame
   time into explicit acceptance checks. Do not infer them from reader counts,
   compilation or cache-hit tests.
6. Test caches against fresh calculations and real initialized actors, including
   nested/ambient scopes, mutation, roots, target changes and restored state. Test
   pruning against deferred and action-changing options, not only immediate casts.
7. Make the shared verification script reproduce the exact documented check in a
   fresh directory. Report partial exports and injected seams prominently. Never
   deploy rewritten game dependencies or offline stand-ins.
8. Identify experimental DLLs at startup and distribution by source/hash. Require
   targeted in-game acceptance before treating a feature as ready. Improvement
   reported for one symptom is not acceptance of every new behavior.

## Rollback operations and verification

Inverse changes were applied newest-first, without rewriting history:

| Commit undone | Meaning |
| --- | --- |
| `ddd071ff797763bd0b2316e04c8d9c110efd5085` | Development test ZIP, checksums/identity and download promotion |
| `fcc64b19c031db625c4da70b9fa942afc71cc222` (`-m 1`) | PR #4 integration |
| `3e5e37dd1e3bb1e8fa54180853877a8572721e3e` | Deferred-scoring follow-up to PR #3 |
| `2d498ad` (`-m 1`) | PR #3 integration |
| `d4568828d5dc04408ec3bf555e8bca46805c3bc4` (`-m 1`) | PR #2 integration |

The preserved-path exception restores `tools/`, CLAUDE.md and the historical
handover/evidence documents from pre-rollback development. AGENTS.md,
CONTRIBUTING.md, the PR template, build scripts and all existing configuration
remain intact. Runtime sources and tests are byte-for-byte identical to the
release; docs/TACTICAL_AI.md is also restored to that release. No merge conflicts
occurred. The root README now withdraws tests and points to stable v0.3.21.

The preserved offline tools include optional v0.3.22-specific reflective checks.
Those are historical tooling, excluded from the mod build, and must not be invoked
with `--kinetic-checks` against the rolled-back DLL: the removed feature types are
deliberately absent. Tool retention does not retain deployed v0.3.22 logic.

Branches deleted on GitHub and locally: codex/decision-context-cost,
codex/kineticist-competence, codex/kineticist-playtest-stutter and
codex/orvath-summon-pacing. Additional local-only test/task branches:
codex/test-build-download and codex/live-code-audit. The workflow-only remote
codex/setup-contributor-workflow is preserved. Detached historical worktrees and
Git history are not branches and are not rewritten or erased.

Pre-push verification:

- Exact Git blob/mode comparison: **121 runtime/build/configuration/test paths
  identical** to v0.3.21; no extra runtime path remains. This includes all deployed
  C# sources, project files, Info.json and the stable test suite.
- Preservation comparison: **25 workflow/tooling paths identical** to pre-rollback
  development, including every tracked file under tools/ and .github/, AGENTS.md,
  CONTRIBUTING.md, CLAUDE.md, Build.ps1, Build.bat and INTEGRATION_HANDOVER.md.
- No tracked test-build archive, identity sidecar or test-build path remains in
  development. Historical Git objects and historical release/tag references are
  retained; this is not an attempt to erase GitHub history or release assets.
- Full Release build against supplied real game references: **0 warnings,
  0 errors**.
- `dotnet test tests/CompanionAI.Tests`: **104 passed, 0 failed, 0 skipped**. The
  count is the restored stable suite; the cancelled feature's tests are removed.
- `git diff --cached --check`: passed.
- Live gameplay was not executed here. Exact source parity is established;
  reinstalling the existing stable release still requires a full game exit and
  replacement of the installed experimental DLL. Saved accepted burn or native
  buffs are game state, not undone automatically by reverting repository code.

Completion record:

- Rollback commit `faf8c76576290d91b59c0538df9a0d0240497d22` was pushed directly
  to development under the author's explicit delegation, together with all four
  remote deletions in one atomic Git push. No force-push was used.
- Remote branch enumeration after that push showed exactly development, main and
  codex/setup-contributor-workflow. All four requested test branches were absent.
  The two additional local-only branches were also deleted. Checked-out old
  worktrees were detached before deleting their branch names, preserving evidence.
- Remote main remained `3d280bd3e3800f1db671112f1be1460ba05d7de5`, identical to its
  pre-operation value. The full remote tag list was unchanged.
- PR #5 was closed unmerged with an explicit withdrawal/handover comment. PRs
  #2, #3 and #4 remain historical merged records; their runtime changes are undone
  by `faf8c76`. PR #1's collaboration setup remains.
- This completion record is a subsequent documentation-only commit; it does not
  change the restored runtime, tests or preserved workflow/tooling.
