# AI Control v0.3.25 — UMM package

**[Download CompanionAI_Pathfinder_v0.3.25.zip](CompanionAI_Pathfinder_v0.3.25.zip?raw=true)**

The author asked for this version to be released (2 November 2026). It is development commit `ffed9ac`; the ZIP is
byte-identical to `test-builds/AI-Control-FACC-v0.3.25-dev-test3-ffed9ac-UMM-test.zip`. [Release notes](CHANGELOG_v0.3.25.md).

## Install

1. Fully exit the game.
2. Install/update the ZIP through Unity Mod Manager's **Mods** tab.
3. UMM should display **0.3.25**. Existing settings remain under `CompanionAI_Pathfinder`.
4. The complete startup log identifies the package:

```text
[Build] v0.3.25 module=b1f83486-ff07-4245-b5d2-f4b0998b5d1a
```

The flat ZIP contains exactly `CompanionAI_Pathfinder.dll` and `Info.json`.
- ZIP SHA-256: `376e6da898704ca418c41c08101957fd9530d83d076ac815759c6fd9a6bb6dba`
- DLL SHA-256: `07eae141ae6c631a36b8eb3af8872172ad3cceaf8f8ab60a8c74ac6acfc37bd9`
- [Identity](CompanionAI_Pathfinder_v0.3.25.identity.json) · [Checksums](SHA256SUMS.txt)

## Publish on GitHub — author action

Claude prepared the package and brought `main` to the release revision; the author creates the GitHub release and tag
(tag pushes from Claude are refused).
- **Releases → Draft a new release.** Tag: **v0.3.25** (new tag). Target: **`main`**.
- Title: `AI Control - Fully Autonomous Combat Companions v0.3.25`.
- Description: paste [CHANGELOG_v0.3.25.md](CHANGELOG_v0.3.25.md).
- Attach **this ZIP** (download it above) as the release asset, then publish.

## Previous releases

- [v0.3.24](CompanionAI_Pathfinder_v0.3.24.zip?raw=true) (tag `v0.3.24`, startup
  `[Build] v0.3.24 module=0ea24da4-70d0-4c77-9046-58f05061174b`, [notes](CHANGELOG_v0.3.24.md)).

- [v0.3.23](CompanionAI_Pathfinder_v0.3.23.zip?raw=true) (tag `v0.3.23`, startup
  `[Build] v0.3.23 module=09a7ab12-2cc2-477c-a688-74765960ea97`, [notes](CHANGELOG_v0.3.23.md)).
- [v0.3.22](CompanionAI_Pathfinder_v0.3.22.zip?raw=true) (tag `v0.3.22`, [notes](CHANGELOG_v0.3.22.md)).
