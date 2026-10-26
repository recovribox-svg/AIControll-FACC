# Test[3] review and surgical bug fixes

The author supplied both AI logs and a separate native CombatLog, reported
occasional real-time Seelah roaming/indecision around encounter 10, and observed
that Orvath still did not seem to use infusions.

## Scope and boundaries

- Correlate actual decisions, issued/ended commands and native outcomes across
  all supplied encounters; distinguish evidence, hypotheses and verified fixes.
- Confirm infusion selection versus activation. The logs show selected Burning
  plans whose activation immediately ends Interrupt/acted=False and is refused.
  Inspect the complete native lifecycle, including compiler-generated command
  matching predicates, before changing dispatch. Do not alter scores to disguise
  an execution failure.
- Investigate other source-supported errors, including native damage/resistance
  discrepancies, preserving unrelated mechanics and both combat modes.
- **Seelah/real-time roaming is investigation and proposal only.** The author
  explicitly requires discussing a supported remedy before any behavioral code
  changes for that issue. No movement, target commitment, heal-diversion or
  emergency-interruption policy changes are authorized by this plan.
- Retain existing architecture and search/ownership boundaries. No global hooks,
  synthetic facts, gameplay actions during appraisal or speculative broad tuning.

## Coordination

`codex/test3-infusion-fixes` starts from remote development
`f51189fa93ffebc791489c6761e05ccbbdea0014` and explicitly includes pending PR #8 at
`7ee50365a0b6f01afbd1d0861e1cffb67edfe65d`, including PR #7/#6 dependencies.
Main and development remain unchanged. A new draft PR targets development.

## Verification and delivery

Use pure tests for meaningful isolated logic, full Release against the supplied
real game references, native binding/control-flow checks and a comparative reader
audit appropriate to each correction. An isolated harness is not live game proof.
Preserve all old ZIPs and publish a uniquely identified flat UMM test ZIP with MVID,
source/checksums and both-mode gameplay steps. Include exact log evidence, remaining
uncertainty and the unimplemented movement proposal in the durable PR handover.
