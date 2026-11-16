# outcome1 review and integration of PRs #74-79

16 November 2026, Codex. The author explicitly authorized reviewing and merging satisfactory open PRs into
`claude/dreamy-pasteur-b9h3rn` after the outcome1 playtest review. Development started at `980cf26`.
The reviewed heads are preserved as merge parents; the combined checked source is **`04a7b9ff49a2d37f12ce35df97b758e7b5959148`**.
No `main`, tag or release change. Manifest remains **0.3.52**; this is a development test.

## Review conclusions

| PR | Assessment and integration |
| --- | --- |
| #74 | Research only. WorldCrawl and CompanionAI v3 supply design inspiration, not compatibility or dependencies. Historical claims remain distinguished from current implementation and measured results. |
| #75 | Native/TTT delivering-hand inspection supports the AI-only Coup reach correction. outcome1 adds a 13.2 m radius / Claw witness, but no exact separation; a distant hit is not established. Native target, melee reach, body size, action and path restrictions remain. Unsupported providers are conservatively withheld. |
| #76 | Shared admission and per-save-outcome control envelopes preserve one root save/delivery weighting and exclude already-downed recipients from duplicate control credit. outcome1 recognizes saved Frigid but does not cast it. |
| #77 | Holder-owned shared pool, native recipient order and source-action order are supported by the supplied code/graphs. Bounded immutable metadata and per-aim scratch retain the documented current-standing approximation. Review identified the metamagic gap corrected in #79. |
| #78 | All 666 issues match terminals; no AI exception or identified Invocation settling violation. Repeated path details were eagerly formatted and printed. The correction bounds only diagnostics; it does not alter reach/refusal/retry/path policy. |
| #79 | Effective context flags and exact normal/Empowered distributions match the native pool calculation. The proposed extra friendly-Sleep veto was withdrawn; the final source contains no such map/veto or native spell/faction patch. Historical broader-candidate receipts are explicitly superseded. |

Documentation conflicts were resolved by preserving every task handover and ordering TACTICAL_AI sections 181-185.
The one runtime conflict in `EvaluateAbility` retains **both** the actual Coup weapon guard and the shared fusion
admission/detail rule, after the user blocklist. The shared dispatcher retains Coup preparation and the independent
bounded path witness. Compiled-route audits pass for all of them; no unrelated runtime changes were introduced.

## Checked combined build

- Clean source **`04a7b9f`**, startup MVID **`2f9ef9ef-8d7a-4574-9b98-fc8383b45bee`**; frozen DLL SHA-256
  `4f9ce6aad72c6ef3cde353527b06debf2d3905ca40f9ee87efa2f95b6f3048da`. Later handover/receipt/package edits do not rebuild that DLL.
- **935 unit + 27 inventory + 3 supplement-script + 18 ledger tests**, no failures/skips.
- Real-reference Release **0 warnings / 0 errors**. SDK **8.0.426**, C# **4.11.0-3.25569.22 (3fb752d4)**,
  supplied UMM **0.33.2.0**. [Compiler/reference provenance](competence/OUTCOME1_INTEGRATION_BUILD_PROVENANCE.json).
- ProfileAudit **44,640 loaded / 4,808 abilities**, no load/reader failures or prerequisite skips;
  **FUSION-OUTCOME 34, FUSION-POOL 31, COUP-REACH 32, DIAGNOSTIC-LOG 22**, all inherited checks pass.
  Every other report row equals the checked gaps3 baseline on identical augmented inputs and the supplied TTT provider.
- Native provider harnesses **98/14/10** pass; supplementary Magus **44** passes. All **4 added native signatures** resolve.
- Fusion UI-template coverage: **8,709 / 15,494** admitted, **6,785** explicitly withheld; **498** shared-pool
  templates admitted, **0** reader/cone errors. These counts do not establish character availability, aim or utility.
- The original raw archive's known Achaekek enum mismatch remains a separate expected negative-input check.
  The compatible corpus is not the complete S1 stock summon supplement.

[Full checks and private-artifact hashes](competence/OUTCOME1_INTEGRATION_CHECKS.json).
[outcome2 UMM ZIP](../test-builds/AI-Control-FACC-v0.3.52-dev-outcome2-04a7b9f-UMM-test.zip?raw=true),
[identity](../test-builds/AI-Control-FACC-v0.3.52-dev-outcome2-04a7b9f-UMM-test.identity.json),
[checksum](../test-builds/AI-Control-FACC-v0.3.52-dev-outcome2-04a7b9f-UMM-test.sha256).
ZIP SHA-256 `9c84960e25ff22925462b53ee6ac1ec2dcaa42ff6544d637e0a2d77b8f517486`. ZIP contains only the checked mod DLL and unchanged Info.json, no native/verification dependencies.

## Replay and remaining work

Use this cumulative package for the next test. Keep the startup module ID and paired AI/combat logs; note spawning
and rest timing. Test both combat modes. Leave Grabber to the engine for its acceptance cases.

1. **Coup:** companion and controlled summon, bow plus melee limb, distant and legal melee targets, legitimate
   reach/natural/unarmed cases; retain native target/sight/path restrictions. See [Coup replay](COUP_DE_GRACE_REACH.md).
2. **Fusion:** [F4](FUSION_OUTCOME_EFFECTS.md) needs an actual saved Frigid cast, including a passed save.
   [F7](FUSION_SLEEP_POOL.md) needs normal Sleep-fusion coverage, recipient order and payload-order cases;
   a legitimate native automatic Empower/Maximize source is optional. Retain user blocks and damage friendly-fire checks.
3. **Ally protection:** AI Control issues native fused casts and does not redefine Sleep targeting. Ordinary Sleep
   and Sleep-delivered fusion have enemy-selector regression checks. A broader damage-delivered fusion's per-effect
   protection for companions, friendly summons and allied NPCs should be compared with the same manual native cast
   if available. The community descriptions and outcome1 logs do not establish that mixed-delivery case; no extra
   friendly-Sleep veto is introduced on that inference. Damage keeps its own friendly-fire rules.
4. **Diagnostics:** repeat the obstructed ledge case and a reachable target. First/materially changed path-refusal
   details should remain, repeats should be quieter, and native refusals/actual rejection/terminal records must remain.
   The rounded-field replay retained 33/1,060 old details; that is not a full-precision Unity timing measurement.
5. **Still open:** substantial warm ability/context work, the obstructed approach, holder alternative/safety and
   monster-grapple modeling. TB release and an actual leap still need acceptance. Invocation's 66 completed casts
   show no identified settling/same-stat violation; no new cap or assignment policy was justified.

The played outcome1 build predates F7, corrected Coup and the new diagnostics/metamagic correction.
Automated combined checks do not replace those gameplay tests or establish stable-release readiness.
