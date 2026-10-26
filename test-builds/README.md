# Kineticist infusion-value playtest

**[Download the UMM test ZIP — infusion-value 664815b](AI-Control-FACC-infusion-value-664815b-UMM-test.zip?raw=true)**

This optional update corrects Progressive RT/TB diagnostic labels and complete-list reporting. It changes no tactical
calculation or performance algorithm. The author reported smooth overall budget-laptop play on the first test;
[the detailed review](../docs/INFUSION_VALUE_TEST1_REVIEW.md) records both successes and unresolved model limits.

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
[Build] kinetic-infusion-value module=37bddec0-bec7-42ef-9418-c1f27a8903b1 on v0.3.21
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
layout. It uses the checked diagnostic-update DLL; packaging changes no runtime code. It contains no game assemblies,
offline tools, stand-ins or settings. [SHA256SUMS.txt](SHA256SUMS.txt) identifies the ZIP;
[versioned build identity](AI-Control-FACC-infusion-value-664815b-UMM-test.identity.json) records source revisions,
DLL hash and module identity.

- Runtime source: `664815b8b2562eedbd7d85525a50ad498811d495`.
- Original infusion implementation: `0a7ed97e60bda45c5a9452734fc3a278b879b6b2`.
- DLL SHA-256: `fd863abc853c6834e73bab62bfa29a40a2ee7c9a471e5d5d76fe12decb210ba3`.
- Release build: zero warnings/errors. Tests: 110 passed, zero failed/skipped.
- The original infusion build's baseline/current audit reports match all 1,220 rows with zero reader errors; this
  diagnostic update changes no reader. Coverage is partial:
  4,097 loaded exports and the same one known failed export on both runs.
- The original infusion build's ordinary gameplay was tested and reported smooth overall; the active Bowling/Pushing
  path was not exercised. The diagnostic update itself has not been tested in game. These checks do not establish
  complete gameplay stability or resolve the pre-existing scoring limits listed in the review.

## Original Test[1] retained

[Download original 0a7ed97 ZIP](AI-Control-FACC-infusion-value-0a7ed97-UMM-test.zip?raw=true).
Its unchanged [build identity](build-identity.json) has MVID `4abd89c4-7b64-4e6f-8ae5-5d96f61ed556`.
The supplied Test[1] logs relate to that build, not the later diagnostic update. Both ZIP hashes remain listed.

Future test downloads should use a new, clearly named ZIP and matching identity/checksums, linked from the task
branch README and PR. Keep earlier filenames immutable so submitted logs can be tied to their exact build.
