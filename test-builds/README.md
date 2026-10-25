# Development test build download

**[Download AI-Control-FACC-f231e66-UMM-test.zip](https://github.com/recovribox-svg/AIControll-FACC/raw/refs/heads/codex/orvath-summon-pacing/test-builds/AI-Control-FACC-f231e66-UMM-test.zip)**

This is the next **task-branch test build**, awaiting development integration. It includes native parameter-cache
and kinetic group-allocation fixes, Sleep shared-target-budget forecasting, corrected combined-descriptor matching,
and read-only build/pacing diagnostics. It does not override animation speed.

If the browser opens a GitHub file page, click **Download raw file**. Download this ZIP rather than GitHub's whole-repository source ZIP.

## Install with Unity Mod Manager

1. Close Pathfinder: Wrath of the Righteous.
2. Open Unity Mod Manager and select the game.
3. Drag the downloaded ZIP into the **Mods** tab and install/update AI Control.
4. Launch the game and enable AI Control if needed.

The supplied test3 logs use the earlier code's markers despite the author downloading the prior ZIP. **Confirm the
replacement before another comparison:** at combat start, this build prints `[Build] loaded module ...; file SHA-256
de97f4bbb24cb241b278c6c52deb66408e92bf45387007a558f0ddf0f43b26a5` and its feature names. If UMM reports the same
version already installed, ensure it actually replaces the files. With the game closed, copying just `Info.json` and
`CompanionAI_Pathfinder.dll` from this ZIP over the existing mod files is a fallback; leave settings files untouched.
The normal mod folder is `<game>/Mods/CompanionAI_Pathfinder`. An absent new `[Build]` line means the installed/logged
binary still needs checking. Do not uninstall/delete the settings folder merely to replace the DLL.

UMM displays version **0.3.21** because the repository's release metadata is unchanged. This package contains the
fixes from **f231e66**, including the previous development fixes. The mod ID remains `CompanionAI_Pathfinder`.

## Build identity

- Source: `f231e66286c7c5b45f0f64e661978702b27dddac` on `codex/orvath-summon-pacing`.
- ZIP SHA-256: `913ca69d32d9d99190e96d41b19a1c8a5ba88683d37d2e789112baa36cbf94a5`.
- DLL SHA-256: `de97f4bbb24cb241b278c6c52deb66408e92bf45387007a558f0ddf0f43b26a5`.
- Contents: only `Info.json` and `CompanionAI_Pathfinder.dll`, at the archive root as expected by UMM.
- Release build against supplied real game references: zero warnings/errors.
- 143 unit tests; 160 release action-entry comparisons and 2,560 walking/status/weapon affordability comparisons;
  native cache, Sleep-budget, descriptor, hook and runtime build-identity checks pass.
- Blueprint audit is explicitly partial: 4,097 loaded, one known failed export; 1,220 rows, zero reader errors.
  The 42 reader differences and remaining defensive-talent coverage limits are documented; this is not a claim of
  full gameplay equivalence. Live validation remains pending.

[Checksum file](SHA256SUMS.txt) · [Build identity](build-identity.json) ·
[Latest comparison, correctness audit and replay checklist](../docs/TEST3_COMPARISON.md) ·
[All reader differences](../docs/TEST3_READER_DIFF.md) ·
[Earlier integration handover](../docs/INTEGRATION_HANDOVER.md)

The earlier [3230ba4 package](AI-Control-FACC-3230ba4-UMM-test.zip) and
[fcc64b1 package](AI-Control-FACC-fcc64b1-UMM-test.zip) are retained for comparison.

This file was added to development at the author's request because conversation artifact downloads were unavailable.
It is an experimental test download, not a published GitHub release. Main remains the stable-release branch.
