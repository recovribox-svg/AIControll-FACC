# AI Control v0.3.26 — UMM package

**[Download CompanionAI_Pathfinder_v0.3.26.zip](CompanionAI_Pathfinder_v0.3.26.zip?raw=true)**

The author asked for this version to be released (2 November 2026), right after merging PR #17. It is development
commit `e10513b`: the "Abilities AI must not use" list and the log review, with `Info.json` 0.3.26.
[Release notes](CHANGELOG_v0.3.26.md).

## Install

1. Fully exit the game.
2. Install/update the ZIP through Unity Mod Manager's **Mods** tab.
3. UMM should display **0.3.26**. Existing settings remain under `CompanionAI_Pathfinder`.
4. The complete startup log identifies the package:

```text
[Build] v0.3.26 module=24ddcf26-2006-43f5-ade7-b326abb2d62c
```

The flat ZIP contains exactly `CompanionAI_Pathfinder.dll` and `Info.json`.
- ZIP SHA-256: `e81f92946d1a1110471f2f17590bc0e2963cb0d8b5c27545220e1f368194c2ea`
- DLL SHA-256: `56278f8c328f5bbb6336e2e401ad82b23b51b4343604c9769301786008a26979`
- [Identity](CompanionAI_Pathfinder_v0.3.26.identity.json) · [Checksums](SHA256SUMS.txt)

## Publish on GitHub — author action

Claude prepared the package and brought `main` to the release revision; the author creates the GitHub release and tag
(tag pushes from Claude are refused).
- **Releases → Draft a new release.** Tag: **v0.3.26** (new tag). Target: **`main`**.
- Title: `AI Control - Fully Autonomous Combat Companions v0.3.26`.
- Description: paste [CHANGELOG_v0.3.26.md](CHANGELOG_v0.3.26.md).
- Attach **this ZIP** (download it above) as the release asset, then publish.

## Previous releases

- [v0.3.25](CompanionAI_Pathfinder_v0.3.25.zip?raw=true) (tag `v0.3.25`, startup
  `[Build] v0.3.25 module=b1f83486-ff07-4245-b5d2-f4b0998b5d1a`, [notes](CHANGELOG_v0.3.25.md)).

- [v0.3.24](CompanionAI_Pathfinder_v0.3.24.zip?raw=true) (tag `v0.3.24`, startup
  `[Build] v0.3.24 module=0ea24da4-70d0-4c77-9046-58f05061174b`, [notes](CHANGELOG_v0.3.24.md)).

- [v0.3.23](CompanionAI_Pathfinder_v0.3.23.zip?raw=true) (tag `v0.3.23`, startup
  `[Build] v0.3.23 module=09a7ab12-2cc2-477c-a688-74765960ea97`, [notes](CHANGELOG_v0.3.23.md)).
- [v0.3.22](CompanionAI_Pathfinder_v0.3.22.zip?raw=true) (tag `v0.3.22`, [notes](CHANGELOG_v0.3.22.md)).
