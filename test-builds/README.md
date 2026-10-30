# Test builds

**[Download the current UMM test ZIP — movement 0c2e0f5](AI-Control-FACC-dev-movement-0c2e0f5-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-dev-movement-0c2e0f5-UMM-test.zip>

The fixes2 build below plus the three movement follow-ups the author approved
([review](../docs/RELEASE_0322_FIXES2_REVIEW.md)):
- when acting from where a companion stands is worth as much as walking somewhere first, it stays (no walking into
  its own Entangle for nothing);
- a hurt caster next to an enemy can step away to heal herself instead of using a weak action;
- charges and an archer's "step back, then shoot" no longer aim at sleeping enemies.

Not the published v0.3.22 release. Startup line:

```text
[Build] v0.3.22 module=ba81d8ad-acb4-4dfc-80e1-1a46c8acd506
```

- Source: `claude/dreamy-pasteur-b9h3rn` at runtime commit `0c2e0f5`.
- ZIP SHA-256: `b2b766394fb696612306c9efbb25b3dbc4d099919445d8fb2412892f49d52998`.
- DLL SHA-256: `5f053fcfcfb26338e03ad8a71c735bb4679cb9c605cc62069dbddccb1253aa8d`.
- Archive: the mod DLL and the unchanged `Info.json` only.
- [Identity](AI-Control-FACC-dev-movement-0c2e0f5-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).

Checks: 218 unit tests pass; Release build with 0 warnings; no new game references. **No in-game run yet.**

What to look for, besides the fixes2 checks below:
- a hurt caster in melee takes a "5ft step out of melee" (turn-based) or "Back off" (real time), then heals;
- casters cast from where they stand when a walk would gain nothing;
- nobody charges a sleeping enemy.

## Previous test: fixes2 c3f1636

**[Download the fixes2 UMM test ZIP — c3f1636](AI-Control-FACC-dev-fixes2-c3f1636-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-dev-fixes2-c3f1636-UMM-test.zip>

The integrated ecabeb4 build below plus the fixes from the fixes2 logs ([review](../docs/RELEASE_0322_FIXES2_REVIEW.md)):
- archers no longer shoot sleeping enemies awake while another enemy is still fighting (the Sleep loop);
- Holy Water-style "only against undead or evil outsiders" branches are read per target.

It also carries Recommendation A's behaviour-preserving helpers. Movement is unchanged. It is **not** the published
v0.3.22 release.

UMM still displays **0.3.22**. Identify this build by its startup line, and please keep the startup part in the log you
send:

```text
[Build] v0.3.22 module=bd00b45e-bce8-4213-91e1-74425d708574
```

- Source: `claude/dreamy-pasteur-b9h3rn` at runtime commit `c3f1636`.
- Module MVID: `bd00b45e-bce8-4213-91e1-74425d708574`.
- ZIP SHA-256: `176aa2343601fe3ae0ad603c8e25b040c2b0014a75cf26538ab7d8a88e7be91a`.
- DLL SHA-256: `02ce9189fda9d47998d1215bdce62cf2bf381edefed9f8de118fca76d5f6037b`.
- Archive: exactly the mod DLL and the unchanged `Info.json`.
- [Identity](AI-Control-FACC-dev-fixes2-c3f1636-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).

Checks: 218 unit tests pass; Release build with 0 warnings; blueprint audit unchanged (4,801 rows). **No in-game run of
this DLL yet.**

What to look for:
- Sleep or Color Spray on one enemy while others still fight: archers shoot the awake enemies or wait, never the
  sleeper. Melee allies may Coup de Grace it.
- Holy Water is thrown only at undead or evil outsiders.
- Everything in the ecabeb4 checklist below.

## Previous test: ecabeb4 (integrated PR #13–#15)

**[Download the integrated UMM test ZIP — ecabeb4](AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.zip>

This is the **reviewed, integrated development revision**: published v0.3.22 plus PR #13 (optional Tabletop Tweaks
resistance preview, reach/Grease diagnostics), PR #14 (no false failure strike for uncast orders, missed-roll splash
kept out of damage calibration) and PR #15 (native arcane spell-failure chance in spell valuation, stalled-navigation
diagnostics). Its code is the same as Codex's last test (be0da2c), rebuilt from the merged revision, so it has its own
module ID. It is **not** the published v0.3.22 release: the [published stable ZIP](../release/CompanionAI_Pathfinder_v0.3.22.zip?raw=true)
does not contain these changes and is unchanged.

UMM still displays **0.3.22**. Identify this build by its startup line:

```text
[Build] v0.3.22 module=2cc6eb92-af79-4a1c-a65d-aad66d8ef315
```

Install as for the earlier tests below (fully exit the game, install through UMM or overwrite the two files in
`<game>/Mods/CompanionAI_Pathfinder/`, keep settings, restart).

- Source: `claude/dreamy-pasteur-b9h3rn` at runtime commit `ecabeb40141ede99c2f118d99f7ffa0667d746e4`.
- Module MVID: `2cc6eb92-af79-4a1c-a65d-aad66d8ef315`.
- ZIP SHA-256: `34618d191de35a16aa2a3006bad73bd5e6742c9fcc5e92d2d57c1da813126423`.
- DLL SHA-256: `06a274047b16a68841db613360848e31cfe4983fd6b9d33d5b5e5d73f21c70a0`.
- Archive: exactly the mod DLL and the unchanged `Info.json`; no game, TTT or verification files.
- [Identity](AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt) ·
  [Integration handover](../docs/INTEGRATION_0322_FOLLOWUPS.md).

Checks: Claude ran 164 unit tests (all pass), a Release build against the game's references (0 warnings, 0 errors) and
the v0.3.21-era blueprint reader comparison (4,801 rows identical to the v0.3.22 build). **No in-game run of this DLL
yet.** The focused replay is the be0da2c checklist below plus the earlier observer and TTT checks: arcane-failure
`[CastChance]` notes, uncast orders not counted, missed Holy Water splash not calibrated, TTT estimates, Burning
infusion, toggle ownership/restoration, group Sleep, healing, and the new `[Walk]`/`[Reach]` diagnostic rows.

## Previous test: be0da2c (Codex, PR #15)

*Historical handover text: PR #13–#15 have since been reviewed and merged (see the top of this page).*

**[Download the earlier UMM test ZIP — be0da2c](AI-Control-FACC-native-cast-chance-be0da2c-UMM-test.zip?raw=true)**

[Draft PR #15](https://github.com/recovribox-svg/AIControll-FACC/pull/15) targets development.
This **unmerged v0.3.22 follow-up** accounts for native arcane spell-failure
probability when valuing affected spells. It uses the game's own eligibility and
spellbook-UI calculator, lazily once per decision. A 20% failure chance reduces
expected benefit to 80%; it does not prevent legitimate random failures.
Independent movement/provoking costs retain their handling.

For Seelah's combat20 wall report, this adds **diagnostics only**: requested and
inspected endpoints, plus native movement data when existing stuck recovery fires.
There is no navigation, target-commitment, cancellation or exit-rule change.
Ardub/body-blocking integration is deferred; no such dependency is included.

Includes pending [PR #14](https://github.com/recovribox-svg/AIControll-FACC/pull/14)
and [PR #13](https://github.com/recovribox-svg/AIControll-FACC/pull/13). UMM remains
**0.3.22**. The [published stable ZIP](../release/CompanionAI_Pathfinder_v0.3.22.zip?raw=true)
and older test archives remain unchanged. New live acceptance is pending; there
is no performance quality cut or claimed speedup.

## Install and identify

1. Fully exit the game.
2. Install the ZIP through Unity Mod Manager's Mods tab.
3. If UMM refuses a same-version replacement, extract its two files into
   `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only; keep settings.
4. Restart and confirm complete startup contains:

```text
[Build] v0.3.22 module=bccf1618-8aa9-4a3d-9c3f-d68075ebafb8
```

A different/missing module means another DLL loaded. To return to stable, fully
exit, reinstall the linked stable ZIP and restart.

## Focused replay

- Check affected spells' `[CastChance]` notes against native failure percent and
  candidate values. Affected 20% sources should use benefit factor 0.8, while native
  unaffected items, sources and held-touch delivery keep factor 1. Chance changes
  should refresh on the next decision. Native failures must stay non-silent.
- Compare armor/shield reducers, no armor, granted abilities and TTT handlers in
  TB/RT. Spell names do not decide eligibility. Preserve useful spells rather
  than blacklisting unlucky failures.
- Preserve automatic Burning/DC/burn/off restoration, player ownership/disabled
  management, resistant-target avoidance, group Sleep, healing and cast safety.
- Replay the corridor/cloud escape. Existing Walk rows now label inspection goal/
  endpoint/gap; a stuck event adds native destination/path end/next waypoint.
  Include those rows; a queried path is not necessarily the execution route.
  Movement behavior is unchanged.
- Compare same-save/settings/mods ordinary actors, Orvath/summons and idle selection.
  Author reports preceding sessions unchanged 7.5/10; new build timing is untested.
  Send complete startup+AI logs, native CombatLog and the exact ZIP filename.

## Verification and identity

164 tests pass; Release has zero warnings/errors. 39 native/compiled chance guards
and retained observer/provider/infusion/cone checks pass. All 1,220 ordinary reader
details match release (4,097 exports loaded/one known failure, partial coverage).
No initialized actor, actual chance handler or Unity/Mono timing is run offline.

- Runtime source: `be0da2c5ff2d949ccf223c982dc2b01000c165c1`.
- Module: `bccf1618-8aa9-4a3d-9c3f-d68075ebafb8`.
- ZIP SHA-256: `018609c3e94c73fde180847c680ecc342d01687167ea1931bd1ebb641c068a2e`.
- DLL SHA-256: `4c20a8d6a8634b45d9d2240ab8ab5b5a5845a9e2187e40c803e3d9846225055c`.
- Archive: exactly checked mod DLL and unchanged manifest.
- [Identity](AI-Control-FACC-native-cast-chance-be0da2c-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- [Evidence and handover](../docs/RELEASE_0322_FIXES_REVIEW.md).
- Previous observer test: [649ed23](AI-Control-FACC-log2-fixes-649ed23-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-log2-fixes-649ed23-UMM-test.identity.json).
  [Its original handover](https://github.com/recovribox-svg/AIControll-FACC/blob/9301356fdd8405743f60958c8afe9678f4edcb81/test-builds/README.md).

## Earlier downloads

All prior ZIPs, identities and checksums remain unchanged. Their original
handover is retained below for reproducing earlier sessions; use the top link
for this test and its correct startup marker.

<details>
<summary>Previous follow-up and historical test instructions</summary>

# Test builds

**[Download the new UMM test ZIP — ea20a79](AI-Control-FACC-release0322-fixes-ea20a79-UMM-test.zip?raw=true)**

This **unmerged follow-up to released v0.3.22** fixes resistance appraisal when
Tabletop Tweaks' DR rework supplies a replacement component. It queries the
existing optional component's own preview; TTT is not required or included.
The Hand of Hosilla/Grease case gets **diagnostics only**. Movement behavior,
spell scoring weights, game speed and performance budgets are unchanged.

UMM still displays **0.3.22**. This is a separate test, not a replacement for the
[published stable ZIP](../release/CompanionAI_Pathfinder_v0.3.22.zip?raw=true).
No in-game acceptance or new performance improvement is claimed.
[Draft PR #13](https://github.com/recovribox-svg/AIControll-FACC/pull/13) targets development.

## Install and identify this test

1. Fully exit the game.
2. Install this ZIP through Unity Mod Manager's Mods tab.
3. If UMM will not replace a same-version install, extract the ZIP's two files to
   `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only; keep settings.
4. Restart and confirm this exact startup line in the complete AI log:

```text
[Build] v0.3.22 module=91661767-d748-41f6-8822-ec1492a08022
```

A different/missing module marker means another DLL loaded. To return to stable,
fully exit and reinstall the linked stable ZIP. Restart after every replacement.

## Focused replay

- With TTT DR rework active, meet a fresh resistant Dretch/Quasit. Before damage
  observations, look for `[Math] ... TTT estimate` or `TTT immunity` where its
  replacement part is installed. Compare direct Fire/Burning values with actual
  native damage/resistance; a zero-valued Burning infusion may correctly stay off.
- Check susceptible targets and automatic infusion activation, burn, off/restoration,
  player-owned toggles and disabled management in TB/RT. Compare TTT absent/rework
  off, immunity and changing pools. The provider preview is approximate; full
  stacking/bypass parity still requires replay.
- Replay the reach/Grease encounter. `[Reach]` records already-scored approaches,
  risk and bounded current foe attack/distance evidence. It does not make the
  party back away or guarantee pursuit. No movement behavior change is intended.
- Preserve group Sleep, heals and silent-cast safety. Check ordinary companions,
  Orvath/summons and idle selection on the same save/settings/mods.
- Send the complete AI log **including startup**, native **CombatLog**, exact ZIP
  filename and observations. CombatLog is useful: it confirms damage, resistance,
  saves, heals and ordinary versus opportunity attacks that AI logs cannot prove.
  Energy samples are capped at 24 rows/fight, so later cases may not print.

## Checks and identity

164 unit tests pass; Release has zero warnings/errors. Actual supplied Core 0.7.15
binding, optional absence and incompatible API fallbacks pass. Retained native/
compiled infusion, cost, memo, cone, Selective, concentration and HD checks pass.
All 1,220 ordinary reader details match release (4,097 exports loaded/one known
failure, partial coverage). No initialized actor, live resistance/pool/handler or
Unity/Mono timing test was run.

- Runtime source: `ea20a79082a56ef226446d31ef29d5b4c08277c5`.
- Module: `91661767-d748-41f6-8822-ec1492a08022`.
- ZIP SHA-256: `d1378472f5d711a7ed458b942f3db3677872910adbb870889b1580fb356351d5`.
- DLL SHA-256: `35594d2719c3858c54839f191363d18050587d893f59c56502c80d694f165c70`.
- Flat archive: exactly the checked mod DLL and unchanged `Info.json`.
- [Identity](AI-Control-FACC-release0322-fixes-ea20a79-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- [Evidence, limits and handover](../docs/RELEASE_0322_LOG_REVIEW.md).

## Archived earlier tests

The following is the historical Test[5] handover. Its PRs are now integrated in
released 0.3.22; its old 0.3.21 test metadata and checksums are retained unchanged.
Use the download at the top for this follow-up, or stable for ordinary play.


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

</details>
