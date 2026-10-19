# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview
- **Mod name**: AI Control - Fully Autonomous Combat Companions (author MonsieurAndroid); UMM `Id` stays `CompanionAI_Pathfinder` (settings path, installs)
- **Game**: Pathfinder: Wrath of the Righteous
- **Type**: Unity Mod Manager mod (C# .NET 4.8)
- **Purpose**: Complete replacement of companion AI with tactical decision-making system
- **Version**: tracked in `Info.json` (0.3.0: step-wise turn-based tactical engine in `Tactics/`; 0.3.1: per-target
  Conditional branches, next-attack buffs, cleanses, time-based command wait; 0.3.2: buff quality from components,
  harmful-only cleanses, mode switches; 0.3.3: Soul Power/burn, day-long markers, summon after-spawn effects, toggles,
  game stacking rule; compatibility notes in README; 0.3.4: enchant carrier buffs, partial-stacking, dispel targets; 0.3.5: out-of-combat buff routine
  judged by AbilityProfile, cannot-act turns, no utility fallback for removal abilities; 0.3.6: self-dispelling buffs, casting in melee; 0.3.7: Progressive spell policy, area-effect spell analysis; 0.3.8: enchantments seen as active,
  own-attack marks on the attack target, area target filters, surprise round / no-move turns; 0.3.9: ranged 5-foot-step full attacks,
  danger-aware healing and downed allies, Coup de Grace by the rules, sleeping enemies; 0.3.10: 5-foot-step movement rule,
  path-end reach check, danger-aware withdraw; 0.3.11: only companions/pets revivable, summons
  worth half, no advance towards unreachable enemies; 0.3.12: harmful-content buffs never valued as helpful, CC
  immunity for combined flags, real-time friendly-fire veto / queue gates / classifier cache key, visible analysis
  failures; audit in `docs/PROGRESSIVE_SPELLS.md` §A; 0.3.13: attacks of opportunity priced into moves (game's own
  disengage checks), no second copy of an own active control area, Empower/Maximize (incl. auto metamagic) in
  estimates, AddContextStatBonus stacking, optional pet belt items (`GameInterface/PetItemsPatch`))

## Reference Resources
- **Rogue Trader CompanionAI v3.5.7**: `C:\Users\veria\Downloads\CompanionAI_v3-master - v3.5.7`
  - Reference for complete AI replacement architecture
  - Key patterns: Behaviour Tree replacement, single decision point, no basic attacks
- **Pathfinder Decompiled Source**: `c:\Users\veria\Downloads\pathfinder_decompile`
  - Game internals reference for accurate API usage

## Build Command
```powershell
.\Build.ps1 [-GamePath "<...\Pathfinder Second Adventure>"] [-Deploy]
# or: dotnet build CompanionAI_Pathfinder.csproj -c Release -p:GamePath="<game folder>"
```
Output: `bin\Release\` and `dist\CompanionAI_Pathfinder_v<version>.zip`. `-Deploy` / `-p:DeployToGame=true` copies to `<game>\Mods\CompanionAI_Pathfinder\`.

## Turn-based command rule (v0.2.119)
Issue every unit command through `GameInterface/CommandDispatcher.Run`. In turn-based combat the game's
`UnitCommand.TickApproaching` only follows `ForcedPath` (else the mouse-cursor prediction path, else interrupt);
the dispatcher computes it like native AI (`AiAction.ScorePath`). Never write `TurnController.Status`.
See `docs/INVESTIGATION.md`.

## Architecture

### AI Decision Pipeline (v0.3.0)
```
Turn-based:  TurnController.Tick (Harmony postfix, CustomBrainPatch.TurnControllerTick_Postfix)
                 → Core/TurnBased/TurnBasedController   (waits for commands, per-turn memory)
                 → Tactics/TacticalEngine.Decide        (ONE best next action from current action economy)
                 → Tactics/TacticalEngine.Execute → GameInterface/CommandDispatcher.Run
Real-time:   AiBrainController.TickBrain (prefix) → RealTimeController (waits: commands, stuck check, AoE escape)
                 → Tactics/RealTimeTactics.Act → TacticalEngine.Decide / Execute   (v0.3.14: same engine as turn-based;
                   actions left = the game's real-time cooldowns, round = 6 s, no 5-foot step; old UnifiedDecisionEngine
                   only with the "Real time: use the old decision engine" setting)
```
Design and value formulas: `docs/TACTICAL_AI.md`. Root-cause evidence: `docs/INVESTIGATION.md`.

### Key Folders
- **Core/**: Turn orchestration - `TurnOrchestrator`, `TurnState`, `TurnPlan`, `PlannedAction`, `ExecutionResult`
- **Abstraction/**: Game API isolation - `IGameUnit`, `IAbilityData`, `ICombatState` with Pathfinder adapters
- **Analysis/**: Combat analysis - `SituationAnalyzer`, `AbilityClassifier`, `TargetScorer`, `Situation`
- **Planning/**: Role-based strategies - `TurnPlanner`, `BasePlan` (abstract), `DPSPlan`, `TankPlan`, `SupportPlan`
- **Execution/**: Action execution - `ActionExecutor`
- **GameInterface/**: Harmony patches - `CustomBrainPatch`, `RealTimeController`
- **Settings/**: Per-character configuration with bilingual support (EN/KR)
- **UI/**: Unity Mod Manager GUI

### Action Economy
Pathfinder action types: Standard (1), Move (1), Swift (variable), Full-Round (2)
Tracked per turn in `TurnState`.

### Critical Design Decisions
- **GUID-based ability identification** for multilingual compatibility (not string matching)
- **Type-based blacklisting** for abilities (not name-based)
- **Adapter pattern** isolates game API dependencies
- **Dual combat modes**: Turn-based and real-time handled separately

## Critical Architecture Notes

### Weapon attacks (v0.3.0 - supersedes the v0.2.21 "no basic attack" rule)
In Pathfinder a weapon attack IS the `UnitAttack` command, and it is the main damage source of martial characters,
archers and animal companions. Excluding it (v0.2.21 rule, borrowed from Rogue Trader) left Lann with `Attacks=0` and
made him retreat every turn (see docs/TACTICAL_AI.md §1). The turn-based engine values weapon attacks with the game's
own rules and competes them against spells by expected value. Always set `ForceFullAttack` or `IsSingleAttack`:
otherwise the UI smart-cursor mode decides.

### RT v3.5.7 vs PF Differences
| Aspect | Rogue Trader v3.5.7 | Pathfinder Current |
|--------|---------------------|-------------------|
| AI Entry | `PartUnitBrain.Tick()` Behaviour Tree replacement | `AiBrainController.TickBrain()` Prefix patch |
| Basic Attacks | Completely excluded from TurnPlanner | Used as fallback (PROBLEM) |
| Decision Point | Single `CompanionAIDecisionNode` | Scattered in RealTimeController |
| Command Queue | `Commands.Empty` strict check | Partial check |

### Pathfinder AI System (from decompile)
- `AiBrainController.Tick()` → `ForceTick()` → `TickBrain()` (static)
- `TickBrain()` checks `NextCommandTime` before issuing commands
- `UnitCommands.Run()` replaces same-type commands (Standard replaces Standard)
- Action cooldown: `CombatState.HasCooldownForCommand(ActionType)`

## Development Guidelines

### Code Changes
- Update `Info.json` version before release
- Mark changes with version comments: `// ★ v0.2.22:`
- Entry point: `Main.cs` → `Main.Load()` initializes Harmony patches

### Harmony Patching
- Turn-based entry: `TurnBasedPatches.TurnControllerTick_Postfix` (postfix on `TurnController.Tick`)
- Real-time entry: `CustomBrainPatch` prefixes `AiBrainController.TickBrain`
- EventBus (no Harmony): `CommandTrace` (command outcome log), `MountPathHandler` (mount ForcedPath)
- Uses manual patching via `AccessTools` for private methods
- Harmony instance ID: `CompanionAI_Pathfinder`

### Changing turn-based behaviour
- Add or retune options in `Tactics/TacticalEngine.cs` (candidates and `ScoreAbility`); keep values in
  damage-equivalent points so options stay comparable.
- What an ability does: `Tactics/AbilityProfile.cs`. Game-rule math: `Tactics/CombatMath.cs`.
- Item-use policy must go through `Tactics/ConsumablePolicy.IsAllowed` (shared by turn-based and real-time).
- Turn/combat facts that must survive re-decisions live in `Tactics/CombatMemory.cs`, never in a candidate.
- Effects behind a `Conditional` (Cure/Inflict, Lay On Hands) must be checked per target with
  `AbilityProfile.AppliesTo` / `BuffAppliesTo`; never count both branches for everyone.
- A buff's worth comes from what it does (`Analysis/BuffEffectAnalyzer`, shared with real-time) and whether it stacks
  on the recipient (`BuffStackingAnalyzer.Analyze`, descriptor-based); only harmful buffs count as something to cleanse.
- Use the game's own rule functions (e.g. `ModifierDescriptorHelper.IsStackable`), never copied tables: other mods
  (Tabletop Tweaks) patch those functions, and the mod must stay independent of them.
- Spell-resource policy (Progressive): pure rule in `Tactics/SpellTierPolicy.cs` (unit-tested, `dotnet test
  tests/CompanionAI.Tests`), game side in `Tactics/ProgressiveGate.cs`, shared by both engines; slot level always from
  `Spellbook.GetSpellLevel`, never a stored counter. Design: `docs/PROGRESSIVE_SPELLS.md`.
- A value of 0 for a verifiable effect (damage, heal, cleanse) means "not useful now": never let it fall through to
  the once-per-combat fallback for unverifiable effects. Consumables never use that fallback.

### Claude Behavior Guidelines
- **Investigate before concluding** - use decompiled sources when game behavior is unclear
- **Complete solutions** - avoid partial fixes or "implement later" suggestions
- **Refactor proactively** - eliminate duplication when found
- **Side effects matter** - consider impact across turn-based and real-time modes
