# Test[5] review scope

The author supplied ordinary TB, RT, miniboss and native CombatLog traces plus
one cautious Sleep-targeting observation. They report smoothness **8/10** and a
definite improvement over Test[4]. Preserve that gain and the working decision
features; do not introduce speculative weights or broad performance pruning.

Branch `codex/test5-logic-fixes` starts at current remote development
`f51189fa93ffebc791489c6761e05ccbbdea0014`, then includes pending PR #10 head
`09249af763b1a2f7b5df22fe089b17557952cc29` as an explicit dependency (including
PR #9/#8/#7/#6). Main and shared development stay unchanged. Repository rules,
the existing Bugfixer protocol and TACTICAL_AI §65–69 were read before edits.

## Investigation

- Correlate all three AI logs against native outcomes, matching encounter IDs.
  Native physical references use raw LF lines because mixed CR formatting changes
  universal-newline numbering. Do not reuse Test[4]'s encounter order blindly.
- Investigate the reported two Sleep casts on a Quasit in TB7. Check the shared
  HD budget, native save/SR, reach, friendly fire, actual recipients and competing
  target values. Passed saves alone do not prove a poor choice. Change scoring
  only if a defect can be established without losing legitimate area choices.
- Verify infusion activation, cost, native saves/damage, recovery and restoration;
  investigate continuing optimistic fire estimates without inventing game math.
- Check new committed-approach safety and stage diagnostics. Existing movement,
  target commitment, navigation and cancellation boundaries remain: the prior
  approved new-area safety check does not authorize wider movement changes.
- Fix only source-supported defects, explaining the hypothesis before runtime
  edits. Keep native rule/context boundaries, command dispatcher and toggle
  ownership/restoration intact. Unknowns remain explicit investigation limits.

## Verification and handover

Use focused pure tests where appropriate, the existing complete test suite,
Release against supplied game references and relevant native-data/compiled-route
checks. Distinguish metadata fixtures from live actor behavior and decision
timings from Unity/Mono frame performance. No live game runs here.

Submit a draft development PR with evidence and remaining limits. If runtime
changes are warranted, publish a new immutable flat UMM test ZIP with DLL/source/
module identity and keep prior downloads unchanged. Retain Info.json 0.3.21 for
intended v0.3.22; no stable release, integration or version bump is authorized.
