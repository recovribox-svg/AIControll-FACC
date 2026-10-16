# CompanionAI - Pathfinder: Wrath of the Righteous

Unity Mod Manager mod that replaces party-companion combat AI with a utility-scoring tactical AI
(role-based DPS / Tank / Support plans, buff/heal/debuff selection, positioning). Works in turn-based
combat (primary) and real-time combat.

Version **0.2.119** fixes companions not physically moving in turn-based combat, and their movement
following the mouse cursor. The root cause and evidence are in [docs/INVESTIGATION.md](docs/INVESTIGATION.md).

---

## Install (players)

1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) into the game.
2. Take `CompanionAI_Pathfinder_v0.2.119.zip` (built as described below, found in `dist\`).
3. Install it either way:
   - Unity Mod Manager → *Mods* tab → drag the ZIP onto the window / *Install Mod*, **or**
   - extract it to `<game>\Mods\CompanionAI_Pathfinder\` so the folder contains
     `CompanionAI_Pathfinder.dll` and `Info.json`.
4. Start the game and open the mod settings with **Ctrl+F10**.

### Settings (Ctrl+F10)
- *Enable in Turn-Based Mode* / *Enable in Real-Time Mode* - both are honoured from 0.2.119.
- *Control Companions* / *Control Main Character* and per-character AI on/off, role and range preference.
- *Enable Debug Logging* - **off by default**; when on it writes several lines per frame. Leave it off unless
  you are debugging. Existing installs keep their old `settings.json`, so turn it off there if it was on.

Units the game is currently controlling itself (confused, panicked, dominated, cutscenes) are left to the game.

---

## Build (developers)

Requirements (Windows 10/11):
- The game installed with Unity Mod Manager applied (the build references the game's own DLLs).
- [.NET SDK 6 or newer](https://dotnet.microsoft.com/download) - e.g. `winget install Microsoft.DotNet.SDK.8`.
  Visual Studio is **not** required (the .NET Framework 4.8 reference assemblies come from NuGet).

```bat
Build.bat
```
or
```powershell
.\Build.ps1                                   # auto-detects the Steam install
.\Build.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure"
.\Build.ps1 -GamePath "<...>" -Deploy         # also copies the mod into <game>\Mods\CompanionAI_Pathfinder
```

The script:
1. finds `Wrath_Data\Managed` (`-GamePath`, `-ManagedPath`, `WRATH_PATH` env var, Steam registry +
   `libraryfolders.vdf`, or an existing `References\Managed`);
2. copies the referenced game assemblies into `References\Managed\` (git-ignored - they are the game's
   copyrighted files; afterwards you can rebuild without the game path);
3. runs `dotnet build -c Release`;
4. writes `dist\CompanionAI_Pathfinder_v<version>.zip` with exactly the layout of the original release
   (`CompanionAI_Pathfinder.dll` and `Info.json` at the archive root) and verifies its entries.

Plain `dotnet build -c Release -p:GamePath="<game folder>"` also works; output goes to `bin\Release\`.

Reference assemblies used (all from `<game>\Wrath_Data\Managed`): `Assembly-CSharp`, `Owlcat.Runtime.Core/UI/Visual/Validation`,
`Owlcat.SharedTypes`, `AstarPathfindingProject`, `UniRx`, `Newtonsoft.Json` (the game's own 8.0.0.0 build - the mod
must bind to it, not to the NuGet 13.0 package), `UnityEngine*.dll`, and `UnityModManager\UnityModManager.dll`.
Harmony comes from NuGet as compile-only; Unity Mod Manager provides it at runtime.

---

## In-game test checklist (turn-based first)

Every command the mod issues is logged, so one combat shows where a request stops. In the Unity Mod Manager log
(`<game>\Wrath_Data\Managed\UnityModManager\Log.txt`, next to `UnityModManager.dll`, or the in-game UMM *Logs* tab) look for:

```
[Cmd] Seelah: ISSUED UnitMoveTo 'Move (12.3,45.6)' mode=TB/Preparing forcedPath=7.4m approachR=0.3
[Cmd] Seelah: ENDED UnitMoveTo 'Move (12.3,45.6)' result=Success acted=True moved=7.3m after 1.6s
[Cmd] Seelah: REJECTED UnitUseAbility '... ' mode=TB/Acting - target out of reach and no movement left this turn
```
`forcedPath=none` on a turn-based command that needed to move, or `moved=0.0m` with `result=Interrupt`, means the
movement fix did not apply - please send that log section.

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Turn-based, companion far from enemies, **mouse resting over empty ground far away** | Companion walks toward the enemy/plan position, not toward the cursor; `ENDED ... moved>0` |
| 2 | Same, but **hover the mouse over an enemy next to the companion** during its turn | Movement distance unaffected (limit is chosen by the AI, not the smart cursor) |
| 3 | Turn-based melee companion: move then attack | One move action used, attack still performed in the same turn |
| 4 | Turn-based caster: move into range then cast | Spell cast after moving |
| 5 | Turn-based, companion adjacent to enemy | 5-foot step or no move; full attack possible |
| 6 | Player delays a companion's turn | Turn stays delayed (mod no longer forces it back to Acting) |
| 7 | Player-controlled character (AI disabled for it) | Normal click movement and path preview |
| 8 | Combat start → end transitions; next combat | Turns start/end normally, no stuck turn |
| 9 | Confused / panicked companion | Game handles it (mod skips the unit) |
| 10 | Real-time combat (if used) | Companions walk the whole way; no stop-start every ~0.5 s |

---

## Architecture

```
TurnController.Tick (postfix)  ──► TurnBasedController ─► TurnPlanBuilder / TurnPlanValidator
AiBrainController.TickBrain (prefix, real-time) ─► RealTimeController ─► UnifiedDecisionEngine
                                         │
                                         ▼
                     ActionExecutor / RealTimeController
                                         │
                                         ▼
          GameInterface/CommandDispatcher  (Init → ForcedPath in turn-based → UnitCommands.Run → verify)
                                         │
                                         ▼
                 GameInterface/CommandTrace (EventBus: ISSUED / REJECTED / ENDED log lines)
```

See `ARCHITECTURE.md` (Korean) for the decision/scoring pipeline.

## License / credits

MIT. Original mod by veria (seyelive5), based on patterns from CompanionAI for Rogue Trader.
