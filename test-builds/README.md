# Development test build download

**[Download AI-Control-FACC-3230ba4-UMM-test.zip](https://github.com/recovribox-svg/AIControll-FACC/raw/refs/heads/codex/orvath-summon-pacing/test-builds/AI-Control-FACC-3230ba4-UMM-test.zip)**

This is the next **task-branch test build**, awaiting development integration. It fixes repeated native parameter
queries across empty rulebook roots, reuses detached kinetic group defaults, and adds read-only timing diagnostics.
It does not override animation speed; that symptom still needs the new `[Pacing]` logs.

If the browser opens a GitHub file page, click **Download raw file**. Download this ZIP rather than GitHub's whole-repository source ZIP.

## Install with Unity Mod Manager

1. Close Pathfinder: Wrath of the Righteous.
2. Open Unity Mod Manager and select the game.
3. Drag the downloaded ZIP into the **Mods** tab and install/update AI Control.
4. Launch the game and enable AI Control if needed.

UMM displays version **0.3.21** because the repository's release metadata is unchanged. This package contains the
fixes from **3230ba4**, including the previous development fixes. The mod ID remains `CompanionAI_Pathfinder`.

## Build identity

- Source: `3230ba47a3055703a82a8cce7dfccc245056d099` on `codex/orvath-summon-pacing`.
- ZIP SHA-256: `5e10e7752d3673c6292d17873609925704a38bdf4bfe76269d2ed4f169ffce14`.
- DLL SHA-256: `cbdd80471d1a1dd46857fd5dea26446fa14815e9fa52a5432d972e923bf62c13`.
- Contents: only `Info.json` and `CompanionAI_Pathfinder.dll`, at the archive root as expected by UMM.
- Release build against supplied real game references: zero warnings/errors.
- 143 unit tests and kinetic self-checks passed. The fresh offline blueprint audit is explicitly partial due to one
  known unrelated export error; live performance/compatibility validation remains pending.

[Checksum file](SHA256SUMS.txt) · [Build identity](build-identity.json) ·
[Gameplay replay checklist and Claude handover](../docs/ORVATH_PACING_PLAYTEST.md) ·
[Earlier integration handover](../docs/INTEGRATION_HANDOVER.md)

The [previous fcc64b1 package](AI-Control-FACC-fcc64b1-UMM-test.zip) is retained for comparison.

This file was added to development at the author's request because conversation artifact downloads were unavailable.
It is an experimental test download, not a published GitHub release. Main remains the stable-release branch.
