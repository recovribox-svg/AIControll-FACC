# Development test build download

**[Download AI-Control-FACC-fcc64b1-UMM-test.zip](https://github.com/recovribox-svg/AIControll-FACC/raw/refs/heads/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-fcc64b1-UMM-test.zip)**

If the browser opens a GitHub file page, click **Download raw file**. Download this ZIP rather than GitHub's whole-repository source ZIP.

## Install with Unity Mod Manager

1. Close Pathfinder: Wrath of the Righteous.
2. Open Unity Mod Manager and select the game.
3. Drag the downloaded ZIP into the **Mods** tab and install/update AI Control.
4. Launch the game and enable AI Control if needed.

UMM displays version **0.3.21** because the repository's release metadata is unchanged. This package contains the
merged development fixes from **fcc64b1**, not the older stable release. The mod ID remains `CompanionAI_Pathfinder`.

## Build identity

- Source: `fcc64b19c031db625c4da70b9fa942afc71cc222` (merged PR #4).
- ZIP SHA-256: `f096660e76150b9a71a7245725196f9142c82d6a0c10d46b688aadfd0d36edd5`.
- DLL SHA-256: `5e03ed141dfe42242d758795692bc5329482358f81d2f020dc6920132fcfeb50`.
- Contents: only `Info.json` and `CompanionAI_Pathfinder.dll`, at the archive root as expected by UMM.
- Release build against supplied real game references: zero warnings/errors.
- 143 unit tests and kinetic self-checks passed. The fresh offline blueprint audit is explicitly partial due to one
  known unrelated export error; live performance/compatibility validation remains pending.

[Checksum file](SHA256SUMS.txt) · [Build identity](build-identity.json) ·
[Gameplay replay checklist](../docs/DECISION_CONTEXT_PLAYTEST.md) · [Integration handover](../docs/INTEGRATION_HANDOVER.md)

This file was added to development at the author's request because conversation artifact downloads were unavailable.
It is an experimental test download, not a published GitHub release. Main remains the stable-release branch.
