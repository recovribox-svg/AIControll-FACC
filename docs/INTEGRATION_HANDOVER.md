# Integration handover — 25 October 2026

## Why Codex completed this integration

MonsieurAndroid explicitly asked Codex to finish Claude's interrupted verification, merge the changes into
`claude/dreamy-pasteur-b9h3rn`, and leave a durable handover while Claude was unavailable. This is authorization
for the development integration of [PR #4](https://github.com/recovribox-svg/AIControll-FACC/pull/4), not a stable
release or a change to `main`. The normal collaboration/release rules in CONTRIBUTING.md remain in place.

The author relayed Claude's preliminary source-review conclusion: the changes looked correct, the shared-value
restoration fixed a real bug, and Claude's earlier deferred-scoring correction was preserved. Claude had started
build/offline verification, found the missing Unity frame-counter stand-in, and stopped before completing that fix.
This is author-relayed review context, not a claim of a completed GitHub approval or completed Claude test run.

## What Codex finished

- Runtime changes: `8043a34028254ec7116fa389493aa356b9937b0b`, documented at `cdb0c876790def6e795bdf0be95b95982f4f3264`.
  Scoped condition-context/result reuse, temporary shared-value restoration and impossible-search skips remain as
  reviewed. Claude's `3e5e37d` deferred scoring for RT cooldown positioning and TB Gather Power preparation remains.
- Verification-tool fix: `1fd1b980c857e1e8335bae397bd09e3f839512a4`.
  `run.sh` now creates and verifies its `Time.frameCount` stand-in. `RunnableStubs` can generate a managed body for
  an explicitly selected bodyless internal call; default rewriting still leaves externs alone. Optional finalizers
  missing in some Unity versions remain optional. The runner also handles spaces in tool paths and propagates build
  pipeline failures. No extra gameplay-code change was required for this interrupted verification issue.
- This handover is included in PR #4. Its PR timeline records the final checked head and merge result; use that
  record for the integration SHA/status rather than repeating the integration from Claude's previous checkout.

## Verification completed by Codex

- `dotnet test tests/CompanionAI.Tests --no-restore`: **143 passed**, zero failed/skipped.
- Full mod build against supplied real game reference DLLs: **zero warnings/errors**.
- Verification-tool builds: **zero warnings/errors**. Bash syntax check passes.
- Shared `tools/ProfileAudit/run.sh` was run from a **fresh output directory**, including a path containing spaces,
  with no manually pre-applied frame-counter patch. Both the decision-read and condition-context checks pass,
  along with existing kinetic profile checks, optional-hook failure/rollback, native cost and pre-dice dispel checks.
- Rewriter checks confirm default mode retains the extern getter, while the runner's explicit selection supplies
  a managed zero-return body. All 21 staged input DLL hashes were unchanged after the runner executions.

The strict fresh audit correctly failed on the known unrelated export/runtime error in
`Classes/Warpriest/DeityFavoriteWeapon/AchaekekSacredWeaponFeature.jbp` (SecurityException in an engine-bound path).
The documented **explicit** `--allow-failures 1` run passed its kinetic checks and produced a **PARTIAL** report:
**4,097 loaded, 1 failed; 1,220 ability rows, zero reader errors**. The failure is listed in the report. Do not describe
this new clean-run result as a complete 4,098/zero-failure audit or the author's broader 4,801-row dataset.

The repeatable invocation is:

```sh
tools/ProfileAudit/run.sh "<offline Managed staging>" "<Release mod DLL>" "<fresh harness directory>" \
  "<report path>" --kinetic-checks --allow-failures 1 <blueprint dump folders...>
```

Host-specific offline staging still includes the previously documented reference-only Unity.Mathematics shim
(absent from the upload) and a compatible Harmony native-helper copy for this Linux sandbox. Those are separate
from the frame-counter fix now provided by the shared runner. Neither those files nor any rewritten game DLL,
decompiled source or harness binary is committed or shipped. These checks do not establish live Unity/Mono parity.

## What remains for Claude / the author

1. Pull current development before making further edits; PR #4 and its tool fix are the integration being completed
   by Codex under the author's delegation. Check its merge result in the PR timeline.
2. Replay the TB/RT performance checklist in [DECISION_CONTEXT_PLAYTEST.md](DECISION_CONTEXT_PLAYTEST.md), preferably
   using the same encounters/settings on both CPUs. Live speedup, fully initialized actor guard-miss behavior and
   third-party-mod parity remain unverified. No whole-decision deadline or absence of remaining stutters is claimed.
3. Record the exact test package/checksum and collect complete `[Perf]`, `[Kinetic]`, `[Tactics]` and `[Toggle]` logs.
   The original `context-8043a34` experimental package identity remains recorded in that checklist; later rebuilt
   binaries must use their own identity/checksum even when runtime source is unchanged.
4. No release/version bump or update of `main` is authorized by this integration. Release only after the author's
   gameplay acceptance and separate release instruction. Do not silently treat integration as gameplay approval.
