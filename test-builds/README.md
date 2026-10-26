# Infusion-selection playtest — 05d1959

**[Download the new UMM test ZIP — 05d1959](AI-Control-FACC-infusion-selection-05d1959-UMM-test.zip?raw=true)**

This test adds automatic selection and activation of useful learned substance infusions,
including damage over time, debuffs and supported blast modifiers. It also addresses the
Test[2] touch-heal retry, unnecessary cooldown scoring and low-HP withdraw/advance loop.
It includes the previous Sleep/path/energy/cone fixes.

Work is isolated in [draft PR #8](https://github.com/recovribox-svg/AIControll-FACC/pull/8)
into development, with pending PR #7/#6 included as dependencies. Main and shared development
are unchanged. This is an unmerged test for intended v0.3.22; release metadata remains 0.3.21.
The withdrawn earlier broad implementation remains withdrawn.

## Install and identify

1. Fully exit the game.
2. Download the ZIP above, then drag it into Unity Mod Manager's **Mods** tab and install/update AI Control.
3. UMM still displays **0.3.21**. If UMM does not replace the same-version install, extract the ZIP's
   two files into `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only. Keep your settings.
4. Restart and verify this exact line in the complete log:

```text
[Build] infusion-selection module=f4bc5e75-a6e9-4da8-ac81-73e105c47aa7 on v0.3.21
```

A different/missing marker means a different DLL is loaded. To return to stable play, fully exit and
reinstall stable v0.3.21. Send the complete log, this ZIP filename and your observations after testing.

## What to test

Use both turn-based and real-time modes with the same save/settings/other mods as smooth Test[2]:

- Enable **AI manages toggles**, leave learned substance infusions off, then let the AI choose in combat.
  `[Kinetic]` lines explain comparisons, changes, target, burn and withheld types. A stronger spell/attack,
  immunity, extra burn or a work limit can legitimately leave an infusion off.
- Check Burning against susceptible/resistant targets, Bowling against eligible/prone/immune targets,
  debuffs already present, and burn/quiver limits. Verify actual buff installation and blast outcomes.
- Check replacing/stopping infusions, duplicate entries, one group switch per round, player changes,
  management disabled and restoration of pre-fight choices after combat. Player choices during a fight
  protect the group from AI changes for that fight when the existing respect-player setting is enabled.
- Repeat the touch-heal/revive approach and low-HP archer retreat situations. Check ordinary companions,
  future movement while actions cool down, and spell-forbidding modes.
- Compare Orvath/summons' turns and idle portrait selection on the budget laptop. Report any new stutter
  or animation-speed change. This code changes no combat-speed setting; offline checks cannot establish FPS.

## Known limits

The supplied data contains 20 substance buff layouts; 18 are recognized. **Unraveling and Chain Arrows
are withheld from automatic selection** because safe read-only dispel/secondary-delivery valuation was
not established. Exposed blast forms retain ordinary scoring; there is no new Gather/metakinesis/form
optimizer or weapon/blade/shadow forecast. Deep Slumber's inherited spreading-delivery control restriction
remains. [Detailed support matrix and limitations](../docs/INFUSION_SELECTION_PLAN.md).

The four-aim/eight-recipient/24-comparison/8 ms limit is a cooperative **selection search** limit,
not a bound on ordinary decisions or active-effect appraisal. Native calls can overrun it. Native
post-switch costs remain authoritative; other mods' handlers and actual activation need playtesting.

## Identity and checks

- Runtime source: `05d1959bdffae641d751c1d8a5daba56142391b5`.
- ZIP SHA-256: `447c8e2937b409c72d7af31bc0dd4bfa888ddc52c088ac881339bf52276738ad`.
- DLL SHA-256: `7e2f5ddfe5b5721a3f41c9efa1fd0d49e5c5f5434bf44ee8320ccaf4055f0948`.
- MVID: `f4bc5e75-a6e9-4da8-ac81-73e105c47aa7`.
- Flat archive: exactly `CompanionAI_Pathfinder.dll` and `Info.json`; no game DLLs or harness files.
- [Versioned identity](AI-Control-FACC-infusion-selection-05d1959-UMM-test.identity.json) and
  [SHA256SUMS.txt](SHA256SUMS.txt) preserve package identity.
- 154 tests pass, zero failed/skipped; full Release build has zero warnings/errors. Native-data checks
  cover the layouts, 486 native cost/quiver cases, real modifier branches and cone bindings. Ordinary
  audit: 1,220 unchanged rows / zero reader errors; **partial** export coverage (4,097 loaded, one failed).
- **This new build has not been tested in game.** The author's smooth-overall Test[2] report applies to
  the previous build. [Test[2] review and bug evidence](../docs/INFUSION_SELECTION_TEST2_REVIEW.md).

## Earlier downloads retained

- [Previous Test[1] scoring-fix build — 8fb0257](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.identity.json).


- [Original Test[1] — 0a7ed97](AI-Control-FACC-infusion-value-0a7ed97-UMM-test.zip?raw=true): the supplied Test[1] logs
  and smooth-overall feedback refer to this build. [Original identity](build-identity.json).
- [Optional diagnostics only — 664815b](AI-Control-FACC-infusion-value-664815b-UMM-test.zip?raw=true): corrects RT/TB
  labels, with the same tactical calculations as original Test[1].
  [Identity](AI-Control-FACC-infusion-value-664815b-UMM-test.identity.json).

Old filenames/checksums remain unchanged so logs can be tied to the exact installed binary.
