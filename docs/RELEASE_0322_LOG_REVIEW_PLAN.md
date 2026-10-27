# Released v0.3.22 log review

The author supplied TB/RT AI logs, the native CombatLog and a reach-weapon/Grease
observation, requesting review and bug fixes. Branch
`codex/release-0322-log-fixes` starts at current development and released tag
`0a8b8f22852ca13a30069b7283ad11ea964e3bb0`. No overlapping open PR exists.

Read AGENTS/CONTRIBUTING/CLAUDE, the release handover, TACTICAL_AI §69–70 and
the existing Bugfixer protocol before edits. The earlier integration permission
completed PR #6–#12; this task uses an isolated development PR. No release/tag,
main change, version bump or replacement of the published v0.3.22 ZIP.

## Evidence and scope

- Match native encounters by IDs, using raw LF physical line references because
  mixed CR formatting changes universal-newline numbering. This corpus is eight
  TB encounters then seven RT encounters. The reported thirteenth fight is RT5.
- New energy traces show the native damage-reduction part is absent while actual
  damage is resisted. Pinned public Tabletop Tweaks source registers its reworked
  resistance in a separate TTUnitPartDamageReduction and supplies its own preview.
  Investigate a strict optional adapter to the actual existing replacement part,
  only when the native part is absent. No guessed resistance math, fabricated
  facts, added damage rules, pool consumption or shared calculation writes.
  The installed Core DLL was requested for exact API validation; unverified
  versions must retain the existing fallback with a bounded diagnostic.
- Review existing concentration/silent-outcome behavior, healing, buffs, infusions
  and the new HD-aim evidence against actual outcomes. Random saves/misses and
  normal acted TB interruptions are not silent failures. Preserve legitimate
  group/immune-centred Sleep choices and accumulating partial heals.
- The reach/Grease situation is a missing tactical response requiring a concrete
  proposal. The prior author boundary remains: discuss movement changes before
  coding. Investigate native reach/risk and propose a narrow shared-scale option;
  no movement/navigation/target-lock/cancellation edit without that approval.
- Preserve performance and scoring quality. These samples do not establish an
  infusion bottleneck or justify broad pruning. No current-session smoothness
  rating was supplied; the prior 8/10 feedback is historical.

## Verification and delivery

Use meaningful pure tests where possible, full unit suite, Release with supplied
game references, strict optional binding/provider cases, native compiled guards
and ordinary-reader parity. No initialized game/Unity profiler is available.
Distinguish source, metadata/value fixtures and actual gameplay verification.

Publish a draft PR into development and a uniquely identified UMM test ZIP on
the task branch if code changes are made. Keep Info.json at0.3.22, retain all old
ZIP bytes/checksums, and document unresolved limits and focused replay steps.
