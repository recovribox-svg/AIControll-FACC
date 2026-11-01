# AI Control v0.3.23 — UMM package

**[Download CompanionAI_Pathfinder_v0.3.23.zip](CompanionAI_Pathfinder_v0.3.23.zip?raw=true)**

The author asked for this version to be released (1 November 2026). It is the development build tested as
`v0.3.23-dev-build6` (runtime commit `967050c`); the ZIP is byte-identical to that test ZIP.
[Release notes](CHANGELOG_v0.3.23.md).

## Install

1. Fully exit the game.
2. Install/update the ZIP through Unity Mod Manager's **Mods** tab.
3. UMM should display **0.3.23**. Existing settings remain under `CompanionAI_Pathfinder`.
4. The complete startup log identifies the package:

```text
[Build] v0.3.23 module=09a7ab12-2cc2-477c-a688-74765960ea97
```

The flat ZIP contains exactly `CompanionAI_Pathfinder.dll` and `Info.json`.
- ZIP SHA-256: `8843109b813370dc60f8df5efc58d72879f1b3c2af6a385ebd436229903b2c7f`
- DLL SHA-256: `b4efd3f509247207fa0366dc9a96c40ddab2c5a9834744682c831cdffde77820`
- [Identity](CompanionAI_Pathfinder_v0.3.23.identity.json) · [Checksums](SHA256SUMS.txt)

## Publish on GitHub — author action

Claude prepared the package and brought `main` to the release revision; the author creates the GitHub release and tag
(tag pushes from Claude are refused).
- **Releases → Draft a new release.** Tag: **v0.3.23** (new tag). Target: **`main`**.
- Title: `AI Control - Fully Autonomous Combat Companions v0.3.23`.
- Description: paste [CHANGELOG_v0.3.23.md](CHANGELOG_v0.3.23.md).
- Attach **this ZIP** (download it above) as the release asset, then publish.

## Previous release

[v0.3.22](CompanionAI_Pathfinder_v0.3.22.zip?raw=true) (tag `v0.3.22`, startup
`[Build] v0.3.22 module=52bb4a21-9f8f-459f-93db-5c942e531fa2`, [notes](CHANGELOG_v0.3.22.md)), unchanged.
