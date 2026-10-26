# Kineticist infusion-value playtest

**[Download the UMM test ZIP — infusion-value 0a7ed97](AI-Control-FACC-infusion-value-0a7ed97-UMM-test.zip?raw=true)**

This is the new restricted task in [draft PR #6](https://github.com/recovribox-svg/AIControll-FACC/pull/6), on
`codex/kineticist-infusion-value`. The author requested this package after the implementation review.
Main and shared development remain unchanged. This is a test download, not a stable release or a revival of the
withdrawn broad kineticist attempt. The intended next development version is v0.3.22.

## Install

1. Fully exit the game.
2. Download the ZIP above and drag it into Unity Mod Manager's **Mods** tab, then install/update AI Control.
3. UMM will display **0.3.21**, because release metadata remains unchanged. If UMM does not replace a same-version
   install, extract the ZIP's two files into `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only.
   Keep your existing settings.
4. Launch the game and check its log for this startup marker:

```text
[Build] kinetic-infusion-value module=4abd89c4-7b64-4e6f-8ae5-5d96f61ed556 on v0.3.21
```

If that marker differs or is absent, the installed DLL is not this test. A game restart is required when replacing it.
To return to normal play, fully exit and reinstall the existing stable v0.3.21 release.

## What to test

This slice values **already-active, supported direct trip/push infusions**, using the actual game configuration.
It does not turn them on or choose another infusion. Burning and Entangling are outside its scope. If Orvath knows
only Burning, these sessions can check ordinary decisions/performance but cannot demonstrate the new infusion value.

In both turn-based and real-time modes:

- Compare idle portrait selection, cold/warm turns, ordinary companions and summons with stable v0.3.21, using the
  same save, settings and other mods. Report any new stutter.
- With Bowling/Pushing already active and the native burn affordable, compare eligible and immune targets,
  already-prone/getting-up targets, applicable blast families and native reductions making burn zero.
- Check the chosen-plan `[Kinetic]` line: infusion, raw contribution, target and existing burn penalty. No added-value
  line is expected for an off/unsupported infusion or one that contributes zero.
- Check that target choice and walking/casting risks make sense, and watch the actual maneuver outcomes.
- Check player toggle choices, AI toggle management off/on and setup after combat. This slice adds no switcher;
  the original burn-blocked recovery behavior still exists.

Send the full game log and this ZIP filename, plus what you observed. The detailed scope and remaining limits are in
[TACTICAL_AI §65](../docs/TACTICAL_AI.md#65-kineticist-infusion-value-a-new-restricted-slice-on-the-restored-v0321-base).

## Identity and verification

The archive contains exactly `CompanionAI_Pathfinder.dll` and `Info.json` at its root, following Build.ps1's UMM
layout. It uses the exact previously checked DLL; packaging changes no runtime code. It contains no game assemblies,
offline tools, stand-ins or settings. [SHA256SUMS.txt](SHA256SUMS.txt) identifies the ZIP;
[build-identity.json](build-identity.json) records source revisions, DLL hash and module identity.

- Runtime source: `0a7ed97e60bda45c5a9452734fc3a278b879b6b2`.
- Verified source plus documentation: `48029f58e497965e9b44ef2e2d7d5070cee96e99`.
- DLL SHA-256: `cf0fb2a3746e1f27d782afd8d3b2bb0269d614d80ca58b677b8d48f93d202a66`.
- Release build: zero warnings/errors. Tests: 110 passed, zero failed/skipped.
- Ordinary baseline/current audit reports match all 1,220 rows with zero reader errors. Coverage is partial:
  4,097 loaded exports and the same one known failed export on both runs.
- In-game behavior and performance: awaiting playtest. These checks do not establish gameplay stability.

Future test downloads should use a new, clearly named ZIP and matching identity/checksums, linked from the task
branch README and PR. Keep earlier filenames immutable so submitted logs can be tied to their exact build.
