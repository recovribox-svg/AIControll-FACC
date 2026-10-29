# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Collaboration workflow

Read [CONTRIBUTING.md](CONTRIBUTING.md) for shared branch ownership, Codex pull requests, handovers, verification, and releases.
Codex works on task branches and targets pull requests at `claude/dreamy-pasteur-b9h3rn`; Claude reviews and integrates them there.
Keep `main` for author-authorized releases. Read [AGENTS.md](AGENTS.md) when coordinating agent work.

Current handover (27 October 2026): the author explicitly authorized Codex to integrate the scoped PR #6–#11
chain and prepare the v0.3.22 UMM package on development. Read
[docs/RELEASE_0_3_22_HANDOVER.md](docs/RELEASE_0_3_22_HANDOVER.md) before resuming.
The author will create the GitHub release personally; Codex must not publish it or create/move release tags.
The earlier [PR #4 integration handover](docs/INTEGRATION_HANDOVER.md) is historical and concerns work later withdrawn.

Current follow-up (28 October 2026): released v0.3.22 log review is isolated on
`codex/release-0322-log-fixes`; read [the evidence and replay handover](docs/RELEASE_0322_LOG_REVIEW.md).
It adds optional TTT resistance-provider appraisal and author-approved reach/Grease
**diagnostics only**. Movement behavior is unchanged. [Draft PR #13](https://github.com/recovribox-svg/AIControll-FACC/pull/13) and the
[test ZIP identity](test-builds/README.md) carry the handover; live acceptance is pending. The prior integration
authorization is completed and does not authorize merging this follow-up or replacing
the published v0.3.22 package/tag.

Latest playtest follow-up (28 October 2026): `codex/release0322-log2-fixes`
is [draft PR #14](https://github.com/recovribox-svg/AIControll-FACC/pull/14), includes pending PR #13, and narrowly fixes uncast-command failure learning and
explicit missed-roll calibration. Read [the second-log handover](docs/RELEASE_0322_LOG2_REVIEW.md).
New logs confirm live TTT reads and Burning effects; the new observer DLL still
needs replay. Movement remains diagnostic-only, with no performance quality cut.
Shared development/main, stable ZIP/tag and all earlier test archives are preserved.

Current review (29 October 2026): [draft PR #15](https://github.com/recovribox-svg/AIControll-FACC/pull/15)
on `codex/release0322-fixes-review` includes
pending PR #14/#13. Read [the fixes-playtest handover](docs/RELEASE_0322_FIXES_REVIEW.md).
It adds native arcane-failure probability to expected spell benefit and navigation
DIAGNOSTICS ONLY for Seelah's combat20. Native failures remain legitimate;
movement/cancellation policies and performance budgets remain unchanged. The
author deferred Ardub/body-blocking integration; do not resume it implicitly.
New build replay/review is pending; no integration/release permission is inferred.

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
  estimates, AddContextStatBonus stacking, optional pet belt items (`GameInterface/PetItemsPatch`); 0.3.14: real time on the
  tactical engine, status-aware actions (`Tactics/ActionRules`, `UnitStatus`; `docs/STATUS_AWARE_PLAN.md`), path attacks of
  opportunity and drop chance (`Tactics/RiskMath`), area exits, SR/energy/observed damage (`Tactics/DamageObserver`); 0.3.15:
  only action-bar abilities (`AbilityDiscovery`), silent cast failures and touch charges (`Tactics/CastOutcome`), TB touch
  spells walk into reach first, per-round area cost, per-target control duration, observed saves (`Tactics/SaveObserver`);
  0.3.16: touch reach by the game's `MechanicsDistance`, provoking casts priced by the defensive-casting rule
  (`CastProvokeRisk`), unfiltered grid-cell lookups (`BattlefieldGrid.CellAt`); 0.3.17: approach around harmful areas,
  Hit Dice conditions with shared values, per-target "reached nothing" safety net, diagnostics (`FightStats`,
  `CoverageReport`), v0.2 planner removed; 0.3.18: calibration - shared formulas in `Tactics/ValueModel` (unit-tested),
  expected rounds and enemy danger from the fight's observed damage, kill-chance bonus, continuous heal curve, ranged
  attack rolls provoke (`Tactics/ProvokeObserver`, `ProvokeRisk`), real-time area escape through the engine; one
  resistance path - spell immunity in the game's order (`Tactics/ResistanceRule`), buff immunity (`CombatMath.BuffImmune`),
  spell attack rolls by the game's attack rule; real-time cannot-act rules from the game code; active abilities AA-1 -
  damage/heals by the game's preview (`IDealDamageProvider`), kill actions, conversions, point-target summons, toggle-buff
  effects, caster-provoke detection, no "utility" fallback, more read-only conditions; AA-2 - command-activated toggles
  (performances, Rage family, spirits) as candidates, `Tactics/TacticalEngine.Toggles.cs`; AA-3 - attack modes (Power
  Attack, Deadly Aim, Fighting Defensively, Combat Expertise) chosen per attack and Mobility per move,
  `Tactics/TacticalEngine.Modes.cs`; AA-4 - Demoralize by the Intimidate rule, combat maneuvers by CMB vs CMD, Vital
  Strike / Cleave / Pounce, Persistent and Extend, chain targets, `Tactics/TacticalEngine.Martial.cs`; play-test fixes
  test10-11 in `docs/TACTICAL_AI.md` §33-34: real-time path check on the game's funnelled walk, pending ally areas,
  Sleep Hit Dice pool, summons with ally-catching areas; released as 0.3.18 with `release/CHANGELOG_v0.3.18.md`; 0.3.19: buffs valued by what they do for the recipient
  (`Analysis/BuffParts`, `Tactics/TacticalEngine.Buffs.cs`, `docs/SHORT_BUFFS_AUDIT.md`), area toggles read by their
  actions with per-recipient conditions (`TacticalEngine.Toggles.cs`), class competence audit
  (`docs/CLASS_COMPETENCE_AUDIT.md`: Fervor conversions, Cackle/Chant extensions, rage parts), kineticist burn / quiver /
  Overflow (`TacticalEngine.Kinetic.cs`), revives, next-spell boosters (`TacticalEngine.Boosters.cs`), caster-side marks
  (`TacticalEngine.Marks.cs`), RT no-sight memory, one toggle owner + self-buff switch (`GameInterface/ModInterop`, Buff It
  2 The Limit), log trimming; `release/CHANGELOG_v0.3.19.md`; 0.3.20: weapon damage by the game's rule, enemy threat
  by offence (`[Threat]`), one-move walk-then-act, casts counted when performed (`Tactics/CastLedger`), real-time quiet
  waits (`Tactics/RealTimePacing`), attacks of opportunity avoided by approach spot and counted on the real walk
  (`PlanApproach`, `CommandDispatcher.Run` pathAttackers, `TurnMemory.PathAttackers`), live-code audit
  `docs/LIVE_CODE_AUDIT.md`; `release/CHANGELOG_v0.3.20.md`; 0.3.21: attack modes in the
  base game's trigger form, Wrath Tactics first-refusal handoff in real time (`GameInterface/ModInterop`,
  `Tactics/WrathHandoff`), coverage and verification audit with the real readers (`tools/ProfileAudit`,
  `docs/COVERAGE_AUDIT.md`), auras on buffs (`Tactics/TacticalEngine.Auras.cs`), immunities, sight, claws, pits,
  regeneration, spellcasting boosts, on-hit effects, Quicken / Extend boosters, shadow spells, caster-only granted facts - `docs/TACTICAL_AI.md` §53-64; `release/CHANGELOG_v0.3.21.md`)

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
- **Tactics/**: the live decision engine (both modes) - `TacticalEngine`, `AbilityProfile`, `AbilityDiscovery`,
  `CombatMath`, `RiskMath`, `ValueModel` (shared value formulas, unit-tested), `ResistanceRule` (spell resistance order,
  unit-tested), `CombatMemory`, `TacticalDriver` (RT),
  observers (`CastOutcome`, `DamageObserver`, `SaveObserver`, `ProvokeObserver`), per-fight numbers (`FightStats`: also
  the calibration input), `CoverageReport`, `ConsumablePolicy`, `SpellTierPolicy` / `ProgressiveGate`
- **Core/TurnBased/**: `TurnBasedController` (TB driver); **Core/**: `ExecutionResult`; legacy-engine state
  (`TurnState`, `TurnPlan`, `PlannedAction`, `TeamBlackboard`, `CombatDataCache`, `FailedAbilityTracker`)
- **Analysis/**: shared analyzers used by the engine (`AbilityClassifier` blacklist, `BuffEffectAnalyzer`,
  `BuffStackingAnalyzer`, `AoeDangerAnalyzer`, `CombatRulesAnalyzer`, `HitChanceCalculator`, `BattlefieldGrid`) and the
  legacy engine's (`SituationAnalyzer`, `TargetScorer`, `Situation`)
- **Core/DecisionEngine/, Scoring/, Planning/MovementPlanner**: the old real-time engine (setting "Real time: use the old
  decision engine"). Not maintained: to be removed entirely; do not adapt it or spend changes on it. The v0.2 planner
  (TurnPlanner, role plans, TurnOrchestrator, ActionExecutor) was removed in 0.3.17.
- **tools/**: offline verification - `ProfileAudit` (the mod's readers on blueprint dumps), `RunnableStubs`, `SigDump`,
  `RefStubGen`, `CoverageAudit` (`tools/README.md`); none of them ship with the mod.
- **docs/archive/**: superseded documents (the v0.2.57 architecture, early research reports).
- **Abstraction/**: `PathfinderCombatStateAdapter` (mode display in the UI)
- **GameInterface/**: Harmony patches and command issuing - `CustomBrainPatch`, `RealTimeController`, `CommandDispatcher`
- **Settings/**: Per-character configuration with bilingual support (EN/KR)
- **UI/**: Unity Mod Manager GUI

### Action Economy
Pathfinder action types: Standard (1), Move (1), Swift (variable), Full-Round (2)
Read from the game at each decision (`TacticalContext`: `HasStandardAction` etc.; real time: the game's cooldowns);
`TurnState` is the old real-time engine's only.

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
| AI Entry | `PartUnitBrain.Tick()` Behaviour Tree replacement | `AiBrainController.TickBrain()` prefix (real time), `TurnController.Tick` postfix (turn-based) |
| Basic Attacks | Completely excluded from TurnPlanner | Weapon attacks valued by the game's rules (v0.3.0) |
| Decision Point | Single `CompanionAIDecisionNode` | One: `TacticalEngine.Decide` for both modes (v0.3.14) |
| Command Queue | `Commands.Empty` strict check | No decision while any command or queued command is pending (`GameAPI.HasPendingCommands`) |

Live-code map, findings register and audit backlog: `docs/LIVE_CODE_AUDIT.md`.

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
  damage-equivalent points so options stay comparable. Shared formulas (worth per round, horizons, kill bonus, heal
  curve, fight-length and danger calibration) live in `Tactics/ValueModel.cs` with unit tests: change them there.
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
