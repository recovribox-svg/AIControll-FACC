# AI Control v0.3.29 — UMM package

**[Download CompanionAI_Pathfinder_v0.3.29.zip](CompanionAI_Pathfinder_v0.3.29.zip?raw=true)**

The author asked for the door-opening work to be taken out, kept for a later version, and this version released
(7 November 2026). It is development commit `811fae2`: v0.3.29 test 8 without the door work. Its highlights are fewer
crowd blocks, real-time walks that end on time, cones that spare companions walking in, and no turns lost to walks that
cannot happen. `Info.json` is 0.3.29. [Release notes](CHANGELOG_v0.3.29.md).

## Install

1. Fully exit the game.
2. Install/update the ZIP through Unity Mod Manager's **Mods** tab.
3. UMM should display **0.3.29**. Existing settings remain under `CompanionAI_Pathfinder`.
4. The complete startup log identifies the package:

```text
[Build] v0.3.29 module=b1371c3f-bb68-4d1c-b881-9753eb663685
```

The flat ZIP contains exactly `CompanionAI_Pathfinder.dll` and `Info.json`.
- ZIP SHA-256: `c9b8ff16c5864e121c0fd9c42b1c6899778c1c121ead972fd748094a7c345a7c`
- DLL SHA-256: `d9213172ccb30d4d34fe617e8d67665c9f2bcd71fd77ebba45644e31b655938f`
- [Identity](CompanionAI_Pathfinder_v0.3.29.identity.json) · [Checksums](SHA256SUMS.txt)

## Publish on GitHub — author action

Claude prepared the package and brought `main` to the release revision; the author creates the GitHub release and tag
(tag pushes from Claude are refused).
- **Releases → Draft a new release.** Tag: **v0.3.29** (new tag). Target: **`main`**.
- Title: `AI Control - Fully Autonomous Combat Companions v0.3.29`.
- Description: paste [CHANGELOG_v0.3.29.md](CHANGELOG_v0.3.29.md).
- Attach **this ZIP** (download it above) as the release asset, then publish.

## Previous releases

- [v0.3.28](CompanionAI_Pathfinder_v0.3.28.zip?raw=true) (tag `v0.3.28`, startup
  `[Build] v0.3.28 module=794e6682-f29b-426a-a940-680ed813c83c`, [notes](CHANGELOG_v0.3.28.md)).

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


## v0.3.50 stable: Author-requested stable v0.3.50: review1 playtest corrections and native griffon leap (stable package; corrected gameplay not yet replayed)

[UMM test ZIP](CompanionAI_Pathfinder_v0.3.50.zip) · [identity](CompanionAI_Pathfinder_v0.3.50.identity.json) · [checksums](CompanionAI_Pathfinder_v0.3.50.sha256). Source `8efbb0e`, startup MVID `45ab748a-b0e9-4db9-b62a-3de2f4eff6fb`, manifest 0.3.50. ZIP SHA-256 `b0ea48b87086f94479c4884c7416a658fd9634570e3da31f2ef96d80a9fbdb4d`.

Stable v0.3.50 package requested by the author. Corrects raw native AC thresholds and held-protection removal from current danger; adds strictly known direct-HP reserve value to swarm exits with existing path/risk/opportunity gates; supports the inspected native Death from Above leap and queued weapon attack with bounded endpoint/quote work and final issue checks. Prospective charge AB/proc damage is uncredited. Grabber remains unsupported pending a paired maintenance/release model. Includes merged PR53 and previous integrated development. New corrected behavior and matched Unity performance remain unplayed; automated validation is recorded separately. [Design/replay](../docs/REVIEW1_SWARM_PLAYTEST.md) · [receipt](../docs/competence/REVIEW1_SWARM_CHECKS.json). 760 unit + 27 inventory, native step 13 and inherited checks pass; real-reference Release 0 warnings; 16/16 added signatures resolve. Compatible/supplementary audits pass, original full-corpus enum failure remains separate. Gameplay/matched timing pending.
