# AI Control v0.3.24 — UMM package

**[Download CompanionAI_Pathfinder_v0.3.24.zip](CompanionAI_Pathfinder_v0.3.24.zip?raw=true)**

The author asked for this version to be released (1 November 2026). It is development commit `0a203b4`; the ZIP is
byte-identical to `test-builds/AI-Control-FACC-v0.3.24-dev-build3-0a203b4-UMM-test.zip`. [Release notes](CHANGELOG_v0.3.24.md).

## Install

1. Fully exit the game.
2. Install/update the ZIP through Unity Mod Manager's **Mods** tab.
3. UMM should display **0.3.24**. Existing settings remain under `CompanionAI_Pathfinder`.
4. The complete startup log identifies the package:

```text
[Build] v0.3.24 module=0ea24da4-70d0-4c77-9046-58f05061174b
```

The flat ZIP contains exactly `CompanionAI_Pathfinder.dll` and `Info.json`.
- ZIP SHA-256: `764553b38f35bac00769115542f2db49718141a7cdafe1e7b5a0334d186a5062`
- DLL SHA-256: `d16c50e98e78806f82e7e98e76703b974315763ebeeace0450911ff0a8a3e104`
- [Identity](CompanionAI_Pathfinder_v0.3.24.identity.json) · [Checksums](SHA256SUMS.txt)

## Publish on GitHub — author action

Claude prepared the package and brought `main` to the release revision; the author creates the GitHub release and tag
(tag pushes from Claude are refused).
- **Releases → Draft a new release.** Tag: **v0.3.24** (new tag). Target: **`main`**.
- Title: `AI Control - Fully Autonomous Combat Companions v0.3.24`.
- Description: paste [CHANGELOG_v0.3.24.md](CHANGELOG_v0.3.24.md).
- Attach **this ZIP** (download it above) as the release asset, then publish.

## Previous releases

- [v0.3.23](CompanionAI_Pathfinder_v0.3.23.zip?raw=true) (tag `v0.3.23`, startup
  `[Build] v0.3.23 module=09a7ab12-2cc2-477c-a688-74765960ea97`, [notes](CHANGELOG_v0.3.23.md)).
- [v0.3.22](CompanionAI_Pathfinder_v0.3.22.zip?raw=true) (tag `v0.3.22`, [notes](CHANGELOG_v0.3.22.md)).
