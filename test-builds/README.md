# Archived test builds

**For v0.3.22, use the [versioned UMM package](../release/CompanionAI_Pathfinder_v0.3.22.zip?raw=true).**
[Install and release identity](../release/README.md). The older test ZIPs below retain their original
0.3.21 metadata and checksums for reproducing historical logs.

## Test[5] fixes playtest — 25434a2

**[Download the new UMM test ZIP — 25434a2](AI-Control-FACC-test5-fixes-25434a2-UMM-test.zip?raw=true)**

Corrects a confirmed outcome bug: a visible failed concentration check could teach a silent/no-reach strike
against a useful spell/target. Matching native failures now supply refusal evidence; genuine silent-effect
protection remains. No casting-risk, resource, movement, target-commitment or cancellation policy change.

The author rated the preceding Test[5] **8/10 smoothness**, improved over Test[4]. This preserves prior
optimizations and decision scope. Sleep scoring stays unchanged: the reported Quasit-versus-human-cluster
choice lacks enough competing-aim evidence to justify new weights. Bounded diagnostics now expose existing
Sleep aim values/rejections and energy-estimate return routes. **No resistance fix or new speedup is claimed.**

[Draft PR #11](https://github.com/recovribox-svg/AIControll-FACC/pull/11) targets development and includes pending
PR #10/#9/#8/#7/#6. Main/shared development are unchanged. This is an unmerged test for intended v0.3.22;
release metadata remains 0.3.21. The withdrawn broad implementation remains withdrawn.

## Install and identify

1. Fully exit the game.
2. Install/update the ZIP through Unity Mod Manager's **Mods** tab.
3. UMM still shows **0.3.21**. If it will not replace a same-version install, extract these two files into
   `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only. Keep settings.
4. Restart and verify the complete log contains:

```text
[Build] test5-logic-fixes module=5bc34728-8614-4cd9-a9ef-046868de2d4f on v0.3.21
```

A different/missing marker means another DLL loaded. For normal play, fully exit and reinstall stable
v0.3.21. Send complete AI logs and native **CombatLog**, this ZIP filename and observations.

## Focused playtest

- Both modes: an ordinary failed concentration check should produce `[Cast] ... native failure, not counted
  as silent`, with no silent/no-reach strike for that event. Genuine silent-effect cases should still be detected.
  Live native/third-party event ordering needs replay; offline checks do not prove it.
- Replay TB7's Quasit/human cluster. `[HD-Aim]` shows up to 12 already-scored centres, existing values/rejections,
  scored risks and up to 3 HD-eligible recipient IDs each. Eligibility is not guaranteed positive benefit or
  a failed save. Rows precede selected-path preparation/policy; chosen current value is printed separately.
  All remaining centres still score; no new cluster bonus, priority, repeated-save blacklist or extra query.
- Fire/Burning against resistant targets: include `[Math] energy estimate` and native damage packets.
  The existing raw/native/observed/final values and return route are logged separately for direct/infusion reads,
  at most 24 rows/fight. Other energy reads can exhaust that sample. Initial resistance optimism is **unresolved**;
  actual criticals sometimes penetrate, so there is no blanket Fire blacklist.
- Recheck native infusion activation/burn/off/restoration, player ownership and management off. The prior
  logs confirm five successful Burning activations; they do not gameplay-validate every supported infusion.
- The committed-approach Grease safety guard remains narrow and respects actual Selective protection.
  Vanilla Selective Grease that spares allies bypasses the veto. A TTT setting that makes a lasting area
  affect allies remains guarded. No broader path/movement guarantee; no discovery change for the second
  blast the author confirmed was not actually granted.
- Same save/settings/mods: compare ordinary companions, Orvath/summons and idle selection in both modes.
  New bounded diagnostics allocate small notes/keys; live performance parity still needs replay.

## Verification and limits

**164 tests pass**, zero failed/skipped. Release builds against supplied game/UMM references with zero
warnings/errors. Compiled concentration/native bindings, Selective chain and HD diagnostic boundaries pass;
six detached vanilla Grease off/on/off assertions yield affects/spares/affects allies. No initialized actor,
TTT/AutoMetamagic handler, gameplay rule or spawn is run by these fixtures. Energy trace return/query scope
has an independent source review.

Retained checks pass: 13 memo assertions, 5 Grease gates, 20 activation metadata cases, Free/Swift thresholds,
18 recognized/two withheld layouts, 486 native cost/quiver vectors, Rare Metal,cone bindings/metadata and
compiled activation/resistance routes. Partial ordinary reader: 4,097 loaded exports, one known failed export;
1,220 rows, zero reader errors, all normalized details unchanged from the preceding build.

**No in-game test of this new build.** Concentration settlement and diagnostics need live acceptance.
Unraveling/Chain Arrows remain withheld; Deep Slumber keeps its spreading restriction. No new Gather,
metakinesis or form optimizer. Separate unacted Aid Another/no-reach ordering remains an investigation lead.
[Full review](../docs/TEST5_REVIEW.md) · [Infusion support](../docs/INFUSION_SELECTION_PLAN.md).

## Identity

- Runtime source: `25434a2332a467992a3385e4b1722049c1e54fbb`.
- MVID: `5bc34728-8614-4cd9-a9ef-046868de2d4f`.
- ZIP SHA-256: `bf91fc8ad1dabdbe4645071a54b1581c4f8e6018ec24b89f2c654ee5482bf6fd`.
- DLL SHA-256: `74734f45efed6f91930d3b2019395dec28a7d58749b481523734055de2576bee`.
- Flat archive: exactly checked `CompanionAI_Pathfinder.dll` and unchanged `Info.json`.
- [Versioned identity](AI-Control-FACC-test5-fixes-25434a2-UMM-test.identity.json) · [SHA256SUMS](SHA256SUMS.txt).

## Earlier downloads retained

- [Test[4] fixes — c6af593](AI-Control-FACC-test4-fixes-c6af593-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test4-fixes-c6af593-UMM-test.identity.json).
- [Native command fixes — aa9fe94](AI-Control-FACC-test3-fixes-aa9fe94-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test3-fixes-aa9fe94-UMM-test.identity.json).
- [Infusion selection — 05d1959](AI-Control-FACC-infusion-selection-05d1959-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-infusion-selection-05d1959-UMM-test.identity.json).
  **Known failed activation path; use the newest build for further testing.**
- [Test[1] scoring fixes — 8fb0257](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.identity.json).
- [Original Test[1] — 0a7ed97](AI-Control-FACC-infusion-value-0a7ed97-UMM-test.zip?raw=true)
  and [identity](build-identity.json).
- [Optional diagnostics — 664815b](AI-Control-FACC-infusion-value-664815b-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-infusion-value-664815b-UMM-test.identity.json).

Prior filenames/checksums remain unchanged for exact log-to-build matching.
