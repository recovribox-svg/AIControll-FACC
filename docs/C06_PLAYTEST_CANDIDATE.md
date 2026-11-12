# C06R1 combined playtest candidate

Development-only candidate on `codex/c06-playtest-candidate`, assembled from the focused C06 native-view guard, Psionics cost-scope, Tome burn-property, drain-threat and passive playtest-diagnostic branches. All start at the author's permitted played C06 / PR #22 `ab810b3`, which includes current development `1f58150` / runtime `3443d94`. The manifest remains 0.3.30. This task branch packages a replay candidate; Claude still reviews and integrates each PR into development.

## Expected changes from played C06

- Removing or controlling a dangerous source retains its recognized stat/level-drain harm after ordinary threat scaling. Negative-level harm uses the victim's remaining native death limit, including levels already drained. Hit chance, existing disabled discount, urgency and competing action/resource costs still apply; a draining enemy is not a forced target.
- Psionics' inspected global cost patch no longer prevents otherwise native performance quotes for unrelated abilities. Its mapped Mind Blade enchantments and unknown providers keep conservative fallback; live dictionary changes are read afresh.
- The supplied Tome/TTT two-term burn property can be read for Orvath with actual feature ownership. Unsupported shapes still fall back, and every burn retains its daily resource price. This should remove the specific named unknown-property fallback when the installed graph matches; it does not promise more burn use.
- A target without a spawned native View no longer aborts the weapon-appraisal stage. The guard waits for the native reach data; valid targets continue through normal candidate handling.
- SwarmAudit lines expose current round facts, existing source-cache contents and live-area coverage. Movement and avoidance policies are unchanged. Read [the swarm proposal](C06_SWARM_PROPOSAL.md) before authorizing that separate work.

The exact source, DLL MVID, ZIP checksum and verification results are recorded in the accompanying identity JSON and `competence/C06_COMBINED_CHECKS.json`. Install only the two ZIP members through UMM; no native game/mod assemblies or rewritten audit dependencies are packaged. Check the startup `[Build]` module against the identity JSON before reporting results.

## Focused replay

1. In both TB and RT, face a mixture of draining and ordinary enemies, fresh/already-drained victims, Death Ward/immunity and disabled foes. Compare competing action values and actual native outcomes. Expected: recognized non-HP harm survives target-removal scaling, and another level drained is valued more as the native death limit approaches; useful alternatives may still win.
2. With the same mod setup, inspect paid Inspire Courage or Inspired Rage performance quotes, actual resource starts/stops, manual control and restoration. Expected: unrelated native performances can receive bounded cost/horizon quotes; mapped custom Mind Blade costs remain named fallback.
3. Check Orvath's named burn-property line and native nonlethal/accepted-burn quantities, with and without the actual Burn Resistance feat if available. Expected: the inspected graph is modeled with the native term rounding and ownership, while daily restraint remains. Unsupported provider revisions remain fallback.
4. Revisit reinforcements. Expected: no ReadRoutine/GetApproachRadius view-null episode; other valid enemy appraisals continue. The six offline regressions cover viewless inputs, not a live reinforcement simulation.
5. Meet Corrupted Rat and Thin Bhoga swarms in both modes. Capture SwarmAudit, decision and native damage lines inside/just outside/overlapping areas. This build gathers the missing coverage evidence; avoidance changes require the author's explicit approval.

For follow-up coverage, capture the final remembered invisible enemy and a Faerie Fire reveal, matched bomb estimates/actual outcomes, Profane Invocation casts per fight, and a melee Magus holding a missed touch charge. The present logs show eleven ordinary Faerie Fire selections, eleven bomb selections, no Profane Invocation and Magus toggle states; those observations do not establish the missing paths. C07 remains a separate native-command/action-budget/range/pricing task, because selecting a native weapon command without consistent appraisal would be an incomplete repair.

## Verification limits

All unit suites, real-reference Release build, strict explicitly native-compatible ProfileAudit, native toggle checks, supplied-provider burn fixtures, viewless regressions and added-member reference checks are run on the combined source before packaging. The strict scope excludes one fingerprinted malformed Achaekek enum spelling; the full original corpus remains INCOMPLETE and is reported separately. No input is normalized. Offline checks do not establish real actor/mod initialization, successful gameplay or improved Unity latency/allocations. The reviewed logs' stage heap figures are selected global heap deltas and include collections.

Read [the complete log review](C06_PLAYTEST_REVIEW.md) and [input fingerprints](C06_PLAYTEST_REVIEW_INPUTS.json). Main, development and releases are not updated by this packaging branch.

Packaged source `a19fb54`: [C06R1 ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-C06R1-a19fb54-UMM-test.zip), [identity](../test-builds/AI-Control-FACC-v0.3.30-dev-C06R1-a19fb54-UMM-test.identity.json), [checksums](../test-builds/AI-Control-FACC-v0.3.30-dev-C06R1-a19fb54-UMM-test.sha256). Startup MVID `1a2a9e3e-dd56-4849-b49e-7144ad3b7ed5`. [Combined verification](competence/C06_COMBINED_CHECKS.json): 551 unit + 11 inventory tests; Release 0 warnings; 98 toggle / 14 Tome / 6 viewless checks pass.
