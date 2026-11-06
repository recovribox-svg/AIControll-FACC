# AI Control v0.3.28 — UMM package

**[Download CompanionAI_Pathfinder_v0.3.28.zip](CompanionAI_Pathfinder_v0.3.28.zip?raw=true)**

The author asked for this version to be released once the test 15 logs showed it stable (6 November 2026). It is
development commit `2415cf8`, byte-identical to the v0.3.28 test 15 package: positioning with the whole party in mind,
the kineticist and archer engine, cones that spare the party, and drain, poison and swarms by the game's rules, with
`Info.json` 0.3.28. [Release notes](CHANGELOG_v0.3.28.md).

## Install

1. Fully exit the game.
2. Install/update the ZIP through Unity Mod Manager's **Mods** tab.
3. UMM should display **0.3.28**. Existing settings remain under `CompanionAI_Pathfinder`.
4. The complete startup log identifies the package:

```text
[Build] v0.3.28 module=794e6682-f29b-426a-a940-680ed813c83c
```

The flat ZIP contains exactly `CompanionAI_Pathfinder.dll` and `Info.json`.
- ZIP SHA-256: `eaf819611a872275f61964a89f154b7f03eb971aaabab5f29a3893c52f532589`
- DLL SHA-256: `a3461a0e67ba1bb60679148eb4d32f467d4e9eaf2c0e6562d98296743e2d75e3`
- [Identity](CompanionAI_Pathfinder_v0.3.28.identity.json) · [Checksums](SHA256SUMS.txt)

## Publish on GitHub — author action

Claude prepared the package and brought `main` to the release revision; the author creates the GitHub release and tag
(tag pushes from Claude are refused).
- **Releases → Draft a new release.** Tag: **v0.3.28** (new tag). Target: **`main`**.
- Title: `AI Control - Fully Autonomous Combat Companions v0.3.28`.
- Description: paste [CHANGELOG_v0.3.28.md](CHANGELOG_v0.3.28.md).
- Attach **this ZIP** (download it above) as the release asset, then publish.

## Previous releases

- [v0.3.27](CompanionAI_Pathfinder_v0.3.27.zip?raw=true) (tag `v0.3.27`, startup
  `[Build] v0.3.27 module=ae5d4340-5d51-454f-bec5-2888b74cbed6`, [notes](CHANGELOG_v0.3.27.md)).

- [v0.3.26](CompanionAI_Pathfinder_v0.3.26.zip?raw=true) (tag `v0.3.26`, startup
  `[Build] v0.3.26 module=24ddcf26-2006-43f5-ade7-b326abb2d62c`, [notes](CHANGELOG_v0.3.26.md)).

- [v0.3.25](CompanionAI_Pathfinder_v0.3.25.zip?raw=true) (tag `v0.3.25`, startup
  `[Build] v0.3.25 module=b1f83486-ff07-4245-b5d2-f4b0998b5d1a`, [notes](CHANGELOG_v0.3.25.md)).

- [v0.3.24](CompanionAI_Pathfinder_v0.3.24.zip?raw=true) (tag `v0.3.24`, startup
  `[Build] v0.3.24 module=0ea24da4-70d0-4c77-9046-58f05061174b`, [notes](CHANGELOG_v0.3.24.md)).

- [v0.3.23](CompanionAI_Pathfinder_v0.3.23.zip?raw=true) (tag `v0.3.23`, startup
  `[Build] v0.3.23 module=09a7ab12-2cc2-477c-a688-74765960ea97`, [notes](CHANGELOG_v0.3.23.md)).
- [v0.3.22](CompanionAI_Pathfinder_v0.3.22.zip?raw=true) (tag `v0.3.22`, [notes](CHANGELOG_v0.3.22.md)).
