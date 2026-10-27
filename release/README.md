# AI Control v0.3.22 — UMM package

**[Download CompanionAI_Pathfinder_v0.3.22.zip](CompanionAI_Pathfinder_v0.3.22.zip?raw=true)**

The author authorized this version for public release. PRs #6–#11 are integrated into
`claude/dreamy-pasteur-b9h3rn`; this package adds the **0.3.22** UMM manifest and version-correct startup identity.
It contains the final Test[5] fixes. The only runtime-source change during packaging is that identity line;
decision calculations, movement, toggle handling and performance budgets are unchanged.

## Install

1. Fully exit the game.
2. Install/update the ZIP through Unity Mod Manager's **Mods** tab.
3. UMM should display **0.3.22**. Existing settings remain under `CompanionAI_Pathfinder`.
4. The complete startup log identifies the package:

```text
[Build] v0.3.22 module=52bb4a21-9f8f-459f-93db-5c942e531fa2
```

The flat ZIP contains exactly `CompanionAI_Pathfinder.dll` and `Info.json`. No game DLLs, settings, offline
verification files or raw playtest logs are included.

## Publish on GitHub — author action

Codex prepares and integrates the package; the author creates the GitHub release/tag.

- Create the release with tag **v0.3.22** and target the prepared revision on
  **`claude/dreamy-pasteur-b9h3rn`**. Main still represents v0.3.21 at this handover.
- Use the exact development merge revision recorded in the release-preparation PR timeline if development
  has since advanced. Do not select an older experimental v0.3.22 test tag.
- Download the ZIP above and attach **that same ZIP** as the release asset.
- Use [CHANGELOG_v0.3.22.md](CHANGELOG_v0.3.22.md) for the release description.

[Development/Claude handover](../docs/RELEASE_0_3_22_HANDOVER.md) records authorization, integration and remaining
leads. [Historical test packages](../test-builds/README.md) remain available for reproducing older reports.

## Identity and checks

- Runtime source: `a9efe55e9de5ae529e1889d5beb99f95f1bd29ce`.
- Module MVID: `52bb4a21-9f8f-459f-93db-5c942e531fa2`.
- ZIP SHA-256: `771fbeb360418f1b4d03ea83d101ec12a88cdcc1534a51ebcb17cee2cacb12d6`.
- DLL SHA-256: `3b372f48c6e080fe989182a2686ae96e464d68ac9e88c61ac9bff924e1373533`.
- [Identity sidecar](CompanionAI_Pathfinder_v0.3.22.identity.json) · [SHA256SUMS](SHA256SUMS.txt).
- **164 tests pass**, zero failed/skipped; Release against supplied game/UMM references has zero warnings/errors.
- Compiled concentration/Selective/HD, activation and resistance routes pass. Native metadata/bindings and
  486 burn/quiver vectors pass. Partial reader: 4,097 loaded, one known failed export; 1,220 rows, zero reader
  errors, all normalized details unchanged from Test[5].
- The author reports 8/10 smoothness in the preceding playtest. Latest concentration/diagnostic changes have
  automated/static verification; no fresh live game or third-party-handler test is claimed.

Initial fire-resistance optimism remains unresolved; the release notes list the other scope limits.
This package keeps all previous release and test ZIPs unchanged.
