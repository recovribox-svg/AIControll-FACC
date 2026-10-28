# Released v0.3.22 follow-up: second playtest review scope

The author supplied new TB/RT AI logs and native CombatLog, reporting 7.5/10
smoothness with subtle, infrequent slowdowns. Review decisions and functionality
as well as performance. Prior reach/Grease approval remains diagnostics only.

## Checkout and coordination

- Remote development remains `0a8b8f22852ca13a30069b7283ad11ea964e3bb0`.
- New branch `codex/release0322-log2-fixes` starts there and fast-forwards pending
  PR #13 at `8bb6a3cb3fd12e95e369d89d21f143fd6a04160c` for its tested TTT/diagnostic
  pathways. New work depends on #13; neither branch is merged into development.
- Main, stable package/tag and all earlier test ZIP bytes remain unchanged.
  Keep Info.json 0.3.22; provide a distinct test ZIP if runtime code changes.
- The excerpts omit startup/MVID. New TTT and Reach rows demonstrate those code
  pathways, not independent exact loaded-binary identity.

## Evidence before implementation

RT Color Spray approaches the Corrupted Mongrel Ranger while Orvath's earlier
Grease generates saving throws. The approach is then cancelled as stuck:
Interrupt, acted=False, moved0.1m. CastOutcome nevertheless reports no target
result and adds a (1/2) strike, whereas the cast ledger correctly says unacted.

Source hypothesis: CastOutcome.Setttle's pre-execution discard requires both
!Executed and !Evidence. Old-area saves set the broad Evidence flag, while the
separate target-reached filter correctly excludes other areas. That broad flag
must not convert a never-executed, unacted interrupted order into a tried cast.
Remove only its influence on the discard; preserve executed/acted, touch,
concentration/native refusal and genuine silent/no-reach protections. Validate
actual compiled branch behavior, not a copy of the condition in a test.

Continue correlating new TTT estimates/immunity, infusion activation, Sleep,
healing and native packets. Treat misleading diagnostics separately from scoring
bugs; never introduce guessed resistance tables or source-specific item rules.

## Performance and diagnostics boundaries

Attribute existing slow reports before proposing optimization. Three sampled
slow decisions include one cold profile build and a warm context/threat outlier
with GC overlap; no demonstrated infusion-search bottleneck. Keep decision
quality/budgets and game speed. Do not infer controlled speedup/regression from
unmatched fight aggregates.

All new Reach rows currently report unknown foe intent. Inspect native command
layout for a bounded observational improvement only if source supports it. Do
not change reach responses, movement, target commitment, pathfinding, retreat,
command cancellation or any candidate score.

## Verification and handover

Run the 164 existing unit tests and full Release build against supplied game/UMM
references after final edits. Exercise the compiled cast-settlement decision with
unrelated evidence, real execution/acted cases and retained native failure paths;
retain optional TTT/native/reader checks. Document limits honestly: no live actor
or Unity timing proof. Supply a draft PR into development with dependency #13,
exact runtime source/module/package identity and focused TB/RT replay steps.
