# Listing on ModFinder

[ModFinder](https://github.com/Pathfinder-WOTR-Modding-Community/ModFinder) lists mods from
`ManifestUpdater/Resources/internal_manifest.json` in its repository (pull request or issue there). Version and
description are filled in automatically from the host (GitHub releases) about every two hours, so the entry carries
neither.

## Entry for `internal_manifest.json`

The file is kept sorted by `Name`: this goes after "Advanced Horse" and before "Allow Modded Achievements".

```json
    {
        "Name": "AI Control - Fully Autonomous Combat Companions",
        "Author": "MonsieurAndroid",
        "Id": {
            "Id": "CompanionAI_Pathfinder",
            "Type": "UMM"
        },
        "Service": {
            "GitHub": {
                "Owner": "recovribox-svg",
                "RepoName": "CombatAIPWOTR"
            }
        },
        "About": "(AI coded) Replaces companion combat AI with a tactical engine, built for turn-based combat and also working in real time: weapon attacks, spells, heals, buffs, control and positioning chosen by expected value using the game's own rules.",
        "HomepageUrl": "https://github.com/recovribox-svg/CombatAIPWOTR",
        "Tags": [ "Gameplay", "AI_Coded" ]
    },
```

- `Id.Id` must equal `Id` in `Info.json` (`CompanionAI_Pathfinder`), or ModFinder will not recognise the installed mod.
- Tags are the values of ModFinder's `Tag` enum (`ModFinderClient/Mod/Tag.cs`): Audio, Bugfix, Content, Gameplay,
  Homebrew, Miscellaneous, Portraits, Romance, Story, UserInterface, Utilities, Visuals, AI_Coded. `Gameplay` because
  the mod changes how companions fight; `AI_Coded` because the code is AI-written. The other listed AI-written mods
  use the same tag and an "(AI coded)" note in `About`.

## What ModFinder needs from this repository

1. **A public repository.** ModFinder reads releases through the public GitHub API. A private repository cannot be
   listed.
2. **A published (not draft) GitHub release per version**, tagged with the version (`v0.3.12`; the `v` prefix is
   ignored). The tag version must equal `Version` in `Info.json`, otherwise ModFinder always reports the mod as out of
   date.
3. **The first release asset is the mod zip**: what a user drops into Unity Mod Manager. That is
   `release/CompanionAI_Pathfinder_v<version>.zip` (`CompanionAI_Pathfinder.dll` + `Info.json` at the root), made by
   `Build.ps1`.
