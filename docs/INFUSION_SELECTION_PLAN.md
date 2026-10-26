# Infusion selection and Test[2] review

The author widened the task after Test[2]: choose and activate useful infusions,
cover substance effects beyond direct maneuvers, retain budget-laptop performance,
and repair evidenced log defects. Test[2] felt **smooth overall**. Its excerpts
begin mid-session without a startup marker, so binary identity is not independently
proven by these logs.

Branch `codex/kineticist-infusion-selection` starts at remote development
`f51189f` and includes pending PR #7 (`6f9f021`) as an explicit dependency.
PR #7 itself includes PR #6. No integration into main/development is authorized.
This is new work, not restoration of the withdrawn configuration-preview system.

## Evidence and approach

- Previous scope intentionally excluded selecting/starting an infusion. General
  toggle discovery excludes blast modifiers; the kineticist helper only recovers
  when burn blocks all blasts. That explains inactive infusions remaining off.
- Stock substance infusions are native free-command activatables. Effect/cost
  installation is proved by their actual active buff, not an IsOn request alone.
  Duplicated activatable entries must not duplicate scoring or benefits.
- Use the actual buff/trigger blueprint and native context/parameter/rank queries
  for its damage, saves and control. Read hidden wrapper activation actions and
  periodic effects; never run a gameplay action while appraising it. Reuse shared
  condition, reduction, buff/debuff and damage-equivalent valuation helpers.
- Compare individual legal substance changes against already-scored blast plans,
  their delivery risk and the best ordinary alternative. Include retaining/off
  configurations. Bound complete effect comparisons and native cost queries; do
  not multiply all spells, targets, infusions and metakinesis combinations.
- Copy one actual native burn-cost value per sampled blast and adjust only exact,
  constant, recognized infusion modifiers. The native cost object's methods and
  Total own reduction/gather arithmetic. This is a restricted marginal estimate,
  not an arbitrary-fact native preview. Unknown modifiers fail closed; native
  current availability/cost is checked again after the real transition. No fake
  facts, global patches, actor mutation during scoring or cross-decision native
  context/rule cache is introduced.
- Issue the native activation command through CommandDispatcher.Run, then request
  the real toggle state in the same execution path. Native OnDidTurnOn detects
  the existing command rather than issuing a duplicate. Wait for completion and
  actual buff installation before considering a blast. Respect global management,
  player-owned competing toggles, native group capacity and per-round switch locks;
  record displaced states for ordered end-of-fight restoration.
- Form infusions mainly grant already exposed blast variants. Continue valuing
  those variants by the shared engine; do not invent a second form search. Record
  unsupported delivery/effect shapes explicitly rather than assigning generic
  utility credit or assuming full content coverage.

## Log defects and performance

- Camellia's separate touch approach can arrive in range yet be cancelled as stuck;
  recording the parent spell as refused then blocks the useful revive. Verify
  current native follow-up reach/LoS before recording that movement failure.
- Warm RT decisions while standard actions cool down still score unavailable
  healing/buffs. Investigate a narrow early gate, preserving offensive movement,
  held charges and deferred preparation. Do not restore the withdrawn broad slot
  pruning that lost legitimate future actions.
- Investigate low-HP withdraw/advance loops with consistent risk appraisal; only
  change behavior supported by both log and source evidence. Logged GC overlap
  does not establish GC as the sole cause of a long decision.

## Verification and handover

Build Release against supplied real game/UMM references; run all unit tests and
focused pure policy/valuation tests. Validate real infusion blueprints, native API
signatures and supported/unsupported paths with isolated offline tools. Keep game
assemblies, rewritten dependencies and fixtures out of commits/packages. Inspect
actual toggle lifecycle, group ownership, no-thrashing, burn affordability and both
combat modes. Publish a new immutable flat UMM ZIP, MVID/checksums, clear support
matrix and gameplay checklist in a draft PR into development. Compilation and
offline checks are not live gameplay or a performance guarantee.
