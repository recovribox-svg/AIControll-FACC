# Test[3] fixes playtest — 019686b

**[Download the new UMM test ZIP — 019686b](AI-Control-FACC-test3-fixes-019686b-UMM-test.zip?raw=true)**

This test corrects the failed native infusion activation seen in Test[3], classifies validated
resource-free switches under the existing costless Free threshold, and keeps capped damage observations
from replacing stronger known native resistance. It adds the **author-approved movement diagnostics only**:
Seelah's target choice, movement scores, native navigation and emergency cancellation are unchanged.

[Draft PR #9](https://github.com/recovribox-svg/AIControll-FACC/pull/9) targets development and includes pending
PR #8/#7/#6 as dependencies. Main and shared development are unchanged. This is an unmerged test for
intended v0.3.22; release metadata remains 0.3.21. The withdrawn broad implementation stays withdrawn.

## Install and identify

1. Fully exit the game.
2. Download the ZIP above and install/update it through Unity Mod Manager's **Mods** tab.
3. UMM still shows **0.3.21**. If it will not replace a same-version install, extract the two files into
   `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only. Keep existing settings.
4. Restart and verify the complete log contains:

```text
[Build] test3-infusion-fixes module=256233cf-7f95-4c40-babb-e461bdd26750 on v0.3.21
```

A different/missing marker means another DLL is loaded. To return to normal play, fully exit and reinstall
stable v0.3.21. Send both AI logs and the native **CombatLog**, this ZIP filename and your observations.

## Focused playtest, both TB and RT

- Enable **AI manages toggles** and start a fresh fight with learned infusions off. Confirm useful changes
  complete, actually install their buff and produce the expected blast effect/native burn. Test replacing/off,
  duplicate entries, player ownership, management disabled and end-of-fight restoration.
- A stronger ordinary action, immunity, extra burn or the normal value threshold can still leave an infusion
  off. Bowling/Pushing do not apply to Fire Blast merely because their toggles are available.
- Compare Fire Blast against Dretch before and after an observed resisted hit. The later-observation bug is
  corrected; the initial pre-observation optimistic estimate still needs investigation with live data.
- Replay Seelah's tenth encounter. `[Walk]` records selected action versus an in-reach alternative, existing
  movement risk and the existing path check's status/distance. Existing cancellation logs explain interruptions.
  These diagnostics add **no pathfinding calls or movement policy changes**.
- Compare Orvath/summons, ordinary companions, idle portrait selection and cold/warm turns with the same
  save/settings/mods. Report any new stutter or speed change. No combat-speed setting was changed.

## Limits and evidence

**This new build has not been tested in game.** Compilation and native metadata/control-flow checks cannot
prove live activation, restoration, third-party handlers or frame times. Seelah's behavioral remedy remains
undecided pending replay. The first-hit fire-estimate discrepancy is not claimed fixed.

Unraveling and Chain Arrows remain withheld from automatic selection; Deep Slumber retains its earlier
spreading-delivery restriction. No new Gather/metakinesis/form search, weapon/blade/shadow forecast or
secondary-recipient optimizer. Search limits remain cooperative and exclude ordinary decision/active-effect work.
[Full Test[3] review](../docs/TEST3_REVIEW.md) · [infusion support matrix](../docs/INFUSION_SELECTION_PLAN.md).

## Identity and checks

- Runtime source: `019686b037bf76a14a01cd0afde35796820351ba`.
- MVID: `256233cf-7f95-4c40-babb-e461bdd26750`.
- ZIP SHA-256: `f01dbfbf05ea99de9705f717552812af6b510a7675150a5cc8043655d39af45f`.
- DLL SHA-256: `86376ac278f44b62e0afb269d222f99e1aac208d85a055357ce21719e4a21c62`.
- Flat archive contains exactly `CompanionAI_Pathfinder.dll` and unchanged `Info.json`.
- [Versioned identity](AI-Control-FACC-test3-fixes-019686b-UMM-test.identity.json) · [SHA256SUMS](SHA256SUMS.txt).
- 154 unit tests pass, zero failed/skipped; Release builds with zero warnings/errors. Native fixtures expose
  Judgment-only matching; compiled-route guards exclude duplicate construction/reissue; Free/Swift thresholds,
  486 cost/quiver cases and cone bindings pass. Partial blueprint audit: 4,097 loaded / one failed export;
  all 1,220 ordinary verdict/detail rows match the previous test, zero reader errors.

## Earlier downloads retained

- [Infusion-selection Test[3] — 05d1959](AI-Control-FACC-infusion-selection-05d1959-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-infusion-selection-05d1959-UMM-test.identity.json). **Known failed activation
  path; use the new build above for further infusion testing.** Original artifact is preserved.


- [Previous Test[1] scoring-fix build — 8fb0257](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.identity.json).


- [Original Test[1] — 0a7ed97](AI-Control-FACC-infusion-value-0a7ed97-UMM-test.zip?raw=true): the supplied Test[1] logs
  and smooth-overall feedback refer to this build. [Original identity](build-identity.json).
- [Optional diagnostics only — 664815b](AI-Control-FACC-infusion-value-664815b-UMM-test.zip?raw=true): corrects RT/TB
  labels, with the same tactical calculations as original Test[1].
  [Identity](AI-Control-FACC-infusion-value-664815b-UMM-test.identity.json).

Old filenames/checksums remain unchanged so logs can be tied to the exact installed binary.
