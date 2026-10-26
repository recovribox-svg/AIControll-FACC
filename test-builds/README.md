# Test[1] scoring-fix playtest

**[Download the UMM test ZIP — 8fb0257](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.zip?raw=true)**

This build addresses four findings: Sleep's shared-HD budget, blocked approaches and boost follow-ups,
energy-specific protection, and cone recipients/friendly fire. It includes the earlier narrow active
Bowling/Pushing valuation slice. [Draft PR #7](https://github.com/recovribox-svg/AIControll-FACC/pull/7)
targets development and explicitly includes pending PR #6. Neither main nor development has been changed.
This is an unmerged test for intended v0.3.22; the earlier broad attempt remains withdrawn.

**Known capability reduction:** Deep Slumber uses native spreading delivery whose recipient order depends on live
frames/positions. Its shared control credit is withheld, so the AI will not select that pure-control spell in this
test. Other unsupported shared graphs/custom cones also fail closed. This restriction is deliberate and logged.
[Full support boundaries and evidence](../docs/TEST1_SCORING_FIXES.md).

## Install

1. Fully exit the game.
2. Download the ZIP above and drag it into Unity Mod Manager's **Mods** tab, then install/update AI Control.
3. UMM displays **0.3.21**, because release metadata is unchanged. If it does not replace the same-version install,
   extract the ZIP's two files into `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only.
   Keep your existing settings.
4. Restart the game and check its log for this exact marker:

```text
[Build] test1-scoring-fixes module=c528dc54-5d32-4b01-8926-0eeb7f32f7e9 on v0.3.21
```

A missing/different marker means a different DLL is loaded. To return to normal play, fully exit and reinstall
stable v0.3.21. Send the complete game log, this ZIP filename and your observations after testing.

## What to test

Use both turn-based and real-time modes, the same save/settings/other mods as the smooth Test[1] comparison:

- Check Orvath portrait selection out of combat, cold/warm turns, ordinary companions and summons on the budget
  laptop. Report any new stutter or animation-speed change; offline checks cannot establish performance parity.
- Sleep several low-HD enemies with and without allies nearby. Record actual affected recipients and SR/save
  outcomes; the AI must not reuse the full HD pool for every creature. Unsupported forecasts explain `[HD]` once.
- Test blocked casts, long walks, reservoir boosts and touch approaches. Boosts should wait for a viable follow-up;
  a reachable higher-tier spell/in-reach action should remain available after a lower tier fails preparation.
  `[Approach]` distinguishes confirmed refusals from unchecked options omitted at the three-check work limit.
- Compare fire/cold/physical danger, matching/mismatching protection, resistance already active, pools and immunity.
  `[Buff]` explanations show matching incoming energy and estimated prevention.
- Try Color Spray with enemies and allies at the cone edges, beside/behind/in front, an unconscious ally, Selective,
  obstacles and walk-then-cast positions. Avoid-friendly-fire must use the native cone and conserve unsafe casts.
- Recheck the earlier active supported Bowling/Pushing path and toggle-management boundaries. It values an
  already-active infusion; it does not select infusions. Burning/Entangling remain outside that narrow slice.

## Identity and checks

Flat UMM archive: exactly `CompanionAI_Pathfinder.dll` and `Info.json`. No game DLL, settings, rewritten dependency
or offline tool. Packaging retains the exact checked Release DLL; it changes no runtime code.
[Versioned identity](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.identity.json) and
[SHA256SUMS.txt](SHA256SUMS.txt) record checksums.

- Runtime source: `8fb02579a04ee202889478c091878997b222ba4c`.
- ZIP SHA-256: `2263ef1bbaa14d8cb6f69127b6a279dc50b0af230359df4b3562865debd83791`.
- DLL SHA-256: `038af46606db672625661885eb01a4a0e43f4c8316e30b93e2c5c8906bd763a7`.
- Build against real supplied game/UMM references: **0 warnings/errors**. Unit tests: **142 passed, 0 failed/skipped**.
- Actual-data parser checks: Sleep and its equivalent durations, rejected unsupported forms, six nonconsuming
  threshold abilities, and Color Spray's completed metadata pass. Native cone/range delegates bind, with
  all-or-none collection checks. These do not test live actors, geometry, conditions or casts.
- Partial ordinary blueprint audit: **1,220 rows, 0 reader errors**, same classification counts; five detail rows
  now distinguish energy types/immunity. **4,097 exports loaded; one known export failed** on both comparisons.
- **In-game testing of this build is pending.** Smooth budget-laptop feedback applies to the earlier narrower
  Test[1]. Live pathfinding, third-party rules and CPU cost remain acceptance gates. Moving plans are checked again
  at issue; mixed/custom energy predictions and aggregate packet sizes can conservatively underprice protection.

## Earlier downloads retained

- [Original Test[1] — 0a7ed97](AI-Control-FACC-infusion-value-0a7ed97-UMM-test.zip?raw=true): the supplied Test[1] logs
  and smooth-overall feedback refer to this build. [Original identity](build-identity.json).
- [Optional diagnostics only — 664815b](AI-Control-FACC-infusion-value-664815b-UMM-test.zip?raw=true): corrects RT/TB
  labels, with the same tactical calculations as original Test[1].
  [Identity](AI-Control-FACC-infusion-value-664815b-UMM-test.identity.json).

Old filenames/checksums remain unchanged so logs can be tied to the exact installed binary.
