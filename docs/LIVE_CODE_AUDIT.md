# AI Control - live-code audit (v0.3.20)

Audit of the code that runs in play, against `AI_Control_Live_Code_Audit_Master_Prompt.md`. Scope: the modern tactical
engine in both combat modes, its shared dependencies, and the boundaries to the optional old real-time engine (whose
own scoring is excluded, see A.4).

| | |
|---|---|
| Branch / base commit | `claude/dreamy-pasteur-b9h3rn` at `44a5354` (0.3.20-test5); audit changes on top |
| `Info.json` | `Id` CompanionAI_Pathfinder, `Version` 0.3.20 |
| Working tree before the audit | clean |
| Remote | `recovribox-svg/CombatAIPWOTR` |
| Build | `dotnet build CompanionAI_Pathfinder.csproj -c Release -p:ManagedPath=<game>/Wrath_Data/Managed`, against the real game assemblies (Assembly-CSharp 11.9 MB), 0 warnings |
| Tests | `dotnet test tests/CompanionAI.Tests`: 67 before, 90 after (all pass) |
| Game runtime | not available here: nothing below is "verified in game" unless it says so; log evidence is from the user's 0.3.20-test4 logs (4 turn-based, 4 real-time fights) |

Evidence grades: **Confirmed** (code proof or a test), **Strongly supported** (code path and cause clear, not yet seen in
play), **Hypothesis**, **Informational**.

---

## A. Live-code reachability map

### A.1 Entry points (`Main.Load`)

| Hook | Kind | Reaches |
|---|---|---|
| `AiBrainController.TickBrain` | Harmony prefix (manual, `Main.PatchTickBrainManually`) | `CustomBrainPatch.TickBrain_Prefix` → `RealTimeController.ProcessUnit` (real time and out of combat; the game does not call it in turn-based combat) |
| `CombatController.StartTurn` | postfix (manual) | `TurnBasedPatches.StartTurn_Postfix` - refusal logging only |
| `TurnController.Tick` | postfix (manual) | `TurnBasedPatches.TurnControllerTick_Postfix` → `TurnBasedController.ProcessTurn` |
| `UnitCommands.Run` | prefix (attribute) | `TrapAvoidancePatch` - retargets **player-issued** walks away from detected traps (real time / out of combat) |
| `MechanicActionBarSlotActivableAbility/Item.OnClick` | prefix/postfix (attribute) | `PlayerTogglePatch` → `CombatMemory.NotePlayerToggle` |
| `AbilityData.CanBeCastByCaster` | transpiler (attribute) | `PetItemsPatch` (setting `PetsUseItems`) |
| `IPartyCombatHandler`, `IInCombatHandler` | EventBus | `CombatEventHandler` → `CombatPatches.OnPartyCombatStart/End` (per-fight resets and reports), out-of-combat buffs |
| EventBus observers | EventBus | `CommandTrace`, `MountPathHandler`, `DamageObserver`, `CastOutcome`, `SaveObserver`, `FightStats`, `ProvokeObserver` |
| UMM `OnToggle` / `OnGUI` / `OnSaveGUI` | callbacks | (un)subscribe observers (idempotent), `UI.MainUI`, `ModSettings.Save` |

Harmony patches stay applied while the mod is switched off in the mod manager; every patch checks `Main.Enabled`
(`PreBuffController.Tick` runs first in `TickBrain_Prefix`, but does nothing unless the player started a "Buff party"
session).

### A.2 Turn-based pipeline

```
TurnController.Tick (postfix) ─ ShouldControlUnit, EnableInTurnBased, Status Preparing/Acting
 └ TurnBasedController.ProcessTurn
    ├ CombatMemory.Sync / ForTurn(unit)              per-round TurnMemory (Failed, Unreachable, TooFar, RefusedSteps)
    ├ stand-up order / wait for the last command (GameAPI.IsReadyForNextAction, idle and max-wait timeouts)
    ├ TacticalDriver.LearnFromLastCommand            no-op command → option Failed this round
    └ up to 3 attempts: TacticalEngine.Decide → TacticalEngine.Execute → CommandDispatcher.Run
        Decide: BuildContext (action economy from the game, enemies/allies/downed, EnemyThreat.Refresh, fight
                calibration) → CastOutcome.Settle, CombatMemory.SettleCast → candidates: weapons (+attack modes),
                abilities (AbilityDiscovery.Gather → EvaluateAbility → ScoreAbility), kineticist, movement, toggles,
                Mobility, held charge → filter (value > 0, Affordable, not Failed, not silently failing) →
                Progressive gate → own-attack marks → spell boosts → quick action first, else best main ≥ 0.5,
                buff restraint (15 %) → LogDecision
        Execute: approach walks, toggles, UnitAttack (ForceFullAttack / IsSingleAttack), UnitUseAbility, UnitMoveTo;
                 attack modes after dispatch; RecordCast, pending areas, CastOutcome.Begin
        Dispatch: ForcedPath by the game's pathfinder, movement left, 5-foot step, one move before a standard
                  action, reach and sight at the path end, harmful areas on the path, full attack ≤ 5-foot step
    rejected → TacticalDriver.RecordRejected (Failed, Unreachable, TooFar) → next attempt
 └ EndTurn → TurnController.ForceToEnd when CanEndTurnAndNoActing
```

### A.3 Real-time pipeline

```
AiBrainController.TickBrain (prefix) ─ ShouldControlUnit, EnableInRealTime, NextCommandTime, cast in progress
 └ RealTimeController.ProcessUnit
    ├ 3 ms per-frame budget (one heavy decision a frame; the rest queued), decision interval + per-unit phase
    ├ ridden mount / cannot act / nauseated (drop unstartable commands)
    ├ out of combat → ApplyPermanentBuffsOutOfCombat (AiRecastsSelfBuffs)
    ├ commands pending → replan after heavy damage / a new harmful area, stuck-command detection
    │                    (RealTimeTactics.RecordStuck) → return
    └ RealTimeTactics.Act → [quiet wait, audit PERF-001] → Decide / Execute / Dispatch (as turn-based; actions left =
      the game's cooldowns, round = 6 s, no 5-foot step, the real-time path checked against harmful areas)
```

Feedback into memory, both modes: `TurnMemory` (this round), `CombatMemory` (this fight: casts, uses, toggles,
refused walks, no-sight, prone stays, burn reliefs, pending casts), observers (`DamageObserver` resistances and blast
shares, `SaveObserver` saves, `CastOutcome` silent failures, `ProvokeObserver`, `FightStats` calibration), and the
per-fight `EnemyThreat` assessment. Resets: `CombatPatches.OnPartyCombatStart/End`, `CombatMemory.Sync` when the
round count goes back.

### A.4 Classification

| Bucket | Code |
|---|---|
| **A. Active core** | `Tactics/TacticalEngine*.cs` (all partials), `TacticalDriver`, `Core/TurnBased/TurnBasedController`, `GameInterface/RealTimeController` (`ProcessUnit` and its gates, stuck detection, out-of-combat buffs), `GameInterface/CommandDispatcher`, `CustomBrainPatch` (entry, control check, combat events) |
| **B. Shared active dependency** | `Tactics/`: `AbilityProfile`, `AbilityDiscovery`, `CombatMath`, `RiskMath`, `ValueModel`, `ResistanceRule`, `ActionRules`, `UnitStatus`, `CombatMemory`, `CastLedger`, `RealTimePacing`, `EnemyThreat`, `ThreatAssessment`, `ProgressiveGate`, `SpellTierPolicy`, `ConsumablePolicy`, `SelectiveRule`, observers, `FightStats`, `CoverageReport`. `Analysis/`: `AoeDangerAnalyzer`, `BuffEffectAnalyzer`, `BuffStackingAnalyzer`, `BuffParts`, `BattlefieldGrid` (dispatcher), `HitChanceCalculator` (DR, SR, ability hit chance only), `AbilityClassifier` (blacklist, CC immunity, undead, charge, descriptor map, permanent buffs). `GameInterface/`: `GameAPI`, `CommandTrace`, `MountPathHandler`, `ModInterop`, `PetItemsPatch`, `PlayerTogglePatch`, `PreBuffController` + `Analysis/PreBuffAnalyzer` ("Buff party" button), `TrapAvoidancePatch` + `Analysis/TrapAwarenessController`. `Settings/`, `UI/`. `Core/ExecutionResult` |
| **C. Boundary** | `RealTimeLegacyEngine` switch in `RealTimeController.ProcessUnit`; resets of `FailedAbilityTracker`, `PendingActionTracker`, `CombatPhaseDetector` at combat start/end; `CombatRulesAnalyzer.ClearCache`; `PathfinderCombatStateAdapter` (UI mode display) |
| **D. Legacy only** (excluded) | `Core/DecisionEngine/*` (`UnifiedDecisionEngine`, `CombatPhaseDetector`, `PendingActionTracker`), `Scoring/*`, `Planning/MovementPlanner`, `Analysis/SituationAnalyzer`, `Situation`, `TargetScorer`, `TargetAnalyzer`, `TargetImmunityChecker`, `CrowdControlAnalyzer`, `BattlefieldInfluenceMap`, `CombatRulesAnalyzer` (scoring use), `GameInterface/MovementAPI`, `Core/TeamBlackboard`, `CombatDataCache`, `TurnState`, `TurnPlan`, `PlannedAction`, `FailedAbilityTracker`, `RealTimeController.ExecuteAIDecision` and its helpers, `LineOfSightChecker` (only legacy callers) |
| **E. Unreachable** | `CombatModePatches.CheckModeChange` (no caller); `RealTimeController` statistics counters; `Abstraction/` interfaces beyond the UI mode display |
| **F. Uncertain** | none open; `TickBrain` in turn-based combat is guarded (returns false) though the game is not expected to call it |

Exclusion evidence for D: every reference to these types comes from other D files or from the legacy branch of
`RealTimeController` (`RealTimeLegacyEngine`); the legacy engine reads modern helpers (`AbilityProfile.Get`,
`CombatMath` estimates, `ProgressiveGate`, `ConsumablePolicy`, `TacticalEngine.BuffWorth`) but writes none of the
modern state (`CombatMemory`, `TurnMemory`, observers' learned values come from game events in both cases), so the
legacy option cannot contaminate turn-based decisions. Boundary checked: the per-frame unit lists and team blackboard
are built only when the legacy engine is selected (v0.3.17).

---

## B. Coverage ledger

| Subsystem | Depth | Result |
|---|---|---|
| Entry points, patches, subscriptions, enable/disable | full | no defect; subscriptions idempotent; patches gated by `Main.Enabled` |
| `TurnBasedController` (wait, stand up, attempts, end turn) | full | LIF-002 (informational) |
| `RealTimeController.ProcessUnit` and gates, budget, queue, stuck detection, out-of-combat buffs | full (legacy branch excluded) | PERF-001; LIF-003 (hypothesis) |
| `TacticalDriver` / `RealTimeTactics` | full | fixed with LIF-001, PERF-001 |
| `CommandDispatcher` | full | no defect |
| `TacticalEngine.Decide`, `BuildContext`, filters, quick/main choice, restraint, logging | full | no defect |
| `AddWeaponCandidates`, `EvaluateCharge`, attack-mode pricing (read) | critical paths | no defect |
| `EvaluateAbility`, `ProvokeLoss`, `ScoreAbility` (damage, friendly fire, heals, revives, control, marks, maneuvers, buffs, cleanses, extensions, fallback, slot discount) | full | VAL-001 (fixed); score contract in D |
| Movement (`AddMovementCandidates`: leave area, 5-foot step / back off, withdraw, advance) | critical paths | no defect (attack-of-opportunity stop rule corrected in test5) |
| `CombatMemory` lifecycle and keys | full | LIF-001 (fixed) |
| `CastOutcome` settle/block | critical paths | no defect (interrupted casts already not counted) |
| `ValueModel`, `RiskMath`, `ActionRules` | full | no defect; unit-tested |
| `AbilityDiscovery`, `AbilityProfile.Get` cache, control classification | critical paths | Compulsion/Charm fixed in test5; no new defect |
| `EnemyThreat`, `DamageObserver` | full (written in this release) | fixed in test5 |
| Settings, UI, migration, optional mods | full | SET-001, INT-001 |
| Diagnostics (`[Tactics]`, `[Perf]`, `[Calib]`, coverage, trace) | full | OBS-001 |
| Tests | full | TEST-001 |
| `AoeDangerAnalyzer`, `BuffParts`/`BuffEffectAnalyzer`, kineticist, toggles, boosters, marks | interfaces only (reviewed in the 0.3.19 audits) | no new finding; not re-audited line by line |
| Legacy engine internals | excluded (D) | - |

---

## C. Findings register

| ID | Subsystem | Finding | Type | Sev. | Confidence | Status |
|---|---|---|---|---|---|---|
| LIF-001 | `CombatMemory.RecordCast` (via `TacticalEngine.Execute`), both modes | A cast was recorded when its command was **issued**. A command that ended without acting still counted: an ability valued by the once-per-combat rule for effects the AI cannot see (an enchantment, a dispel) was never offered again on that target for the rest of the fight; a buff counted as "cast this round"; a debuff's repeat discount (x0.3 after two uses) grew; a mark the AI cannot see blocked re-marking for its duration. | Correctness / lifecycle | P2 | Confirmed (code) | **Implemented** |
| PERF-001 | `RealTimeTactics.Act` | Three quarters of real-time decisions end in "wait" while the standard action is on cooldown, each a full decision (every ability on every target). Measured in the 0.3.20-test4 real-time log: 92/125, 56/90, 100/132 and 74/97 decisions chose nothing; total decision time 6.1, 4.5, 5.6, 4.1 s per fight. | Performance | P2 | Confirmed (log) | **Implemented** (measurement pending) |
| VAL-001 | `TacticalEngine.EvaluateAbility` | The "worth spending an item" bar (5 for a used-up item, the quick bar otherwise) was checked before the walk (x0.9), attacks of opportunity, the concentration check and the walk's risk were taken off, so an item could be used for less than the bar. | Tactical value | P3 | Confirmed (code) | **Implemented** |
| INT-001 | `TrapAvoidancePatch` | Retargets the player's own walk orders (real time and out of combat) away from detected traps, for every party member, whatever the AI settings; no setting switches it off and the README does not mention it. Only perceived traps; turn-based combat untouched. | Integration / settings | P3 | Confirmed (code) | Needs a user decision (setting or note) |
| SET-001 | `CharacterSettings.HealAtHPPercent`, `AvoidFriendlyFire` | Live settings with no UI (settings file only): heal threshold 30 % (+20 % margin, at least 35 %), friendly-fire veto on. | Settings | Info | Confirmed | Documented here |
| OBS-001 | live value paths (`TacticalEngine` 24, `CombatMath` 21, `AbilityProfile` 15, `AoeDangerAnalyzer` 13, others) | About 140 `catch` blocks return a default without a trace. A game update or a mod component that throws makes the value silently fall back (often conservative: no value, no immunity). | Observability | P3 | Confirmed (count) | Deferred (proposal in F) |
| TEST-001 | `tests/CompanionAI.Tests` | Only game-independent formulas are tested (now 90 tests: value model, risk, resistance order, action rules, spell tiers, cast ledger, real-time pacing). Candidate generation, legality and final choice need the game and are verified by play logs only. | Maintainability | Info | Confirmed | Accepted (see H) |
| LIF-002 | `TurnBasedController.ProcessTurn` | Rejected attempts count towards the 16-decisions-per-turn safety limit. The most in one turn of the test4 turn-based log is 4; no turn has come near the limit. | Lifecycle | Info | Confirmed | No change |
| LIF-003 | `CombatEventHandler.HandleObjectLeaveCombat`, `ProcessUnit` out-of-combat branch | A party member out of combat while the rest still fight may cast an hours-long self buff (standard action) before it joins. | Lifecycle | P3 | Hypothesis (not seen in logs) | Watch |
| DOC-001 | `CLAUDE.md` | "Action economy tracked in TurnState", "basic attacks used as fallback (PROBLEM)", "decision point scattered" described the pre-0.3.14 design. | Maintainability | Info | Confirmed | **Implemented** |
| DEAD-001 | `CombatModePatches.CheckModeChange` | No caller. | Maintainability | Info | Confirmed | Left (no removal by default) |
| ARCH-001 | `Tactics/TacticalEngine.cs` (≈3 700 lines) | One file holds context, weapons, abilities, movement and execution; the partials split off newer families. Hard to test in isolation, but no defect traced to the size. | Maintainability | Info | Informational | No refactor (risk > benefit now) |

Found and fixed in the same session before this register (0.3.20-test5, `docs/TACTICAL_AI.md` §50): a hit from an
attack of opportunity counted as stopping a walk (the game lets the unit walk on); a flat half chance a hit spoils a
spell (the game's concentration rule now); Compulsion read as Charm (hard control: Bane); save bonuses against one kind
of effect counted against everything; blast learning from resisted hits.

### Details of the implemented findings

**LIF-001.** First divergence: `Execute` → `CombatMemory.RecordCast` right after `CommandDispatcher.Run` accepted the
command. Readers: `ScoreAbility` once-per-combat fallback (`LastCastRound(...).HasValue`: enchant, dispel, unknown
effects), buff "cast this round", debuff/mark repeat (`TimesUsed ≥ 2` → x0.3), summon (`TimesUsed > 0` with a living
summon), empty-component marks (`CastTargets`), the Foe Focus re-mark rule (`Marks.cs`). Commands end without acting
regularly: turn-based force-finishes, real-time replans after damage or a new area, stuck walks cancelled, the game
dropping the party's orders (real-time fight 1 of the test4 log: all five first orders). Remedy: `Tactics/CastLedger`
(pure) keeps each caster's last record open; `CombatMemory.SettleCast`, called before each of the caster's decisions
(both modes) and before a new record, confirms it once the command has ended having started and acted
(`CastLedger.Performed`), otherwise takes it back (round and use count before it). A command that never ends leaves
the record standing (the old behaviour). `CastOutcome` already ignored casts that ended before executing (v0.3.18); the
ledger is needed because `CastOutcome` follows only verifiable abilities, and the affected ones are the unverifiable.
Regression risk: an ability whose command acted without starting would be recounted - in the game's code only
`ForceFinishForTurnBased` produces "acted, not started", and it never runs the ability. Tests: `CastLedgerTests` (11).

**PERF-001.** Remedy: after a real-time decision that chose nothing while the unit's standard action is on cooldown
(`HasStandardAction` false, `CombatState.Cooldown.StandardAction` seconds left), the unit decides again when the cooldown
ends, at most 2 s later (`RealTimePacing.QuietSeconds`), or at once when its HP drops, its engagement count changes, it
stands in more harmful areas, the number of enemies in the fight changes, or an ally goes down (`RealTimePacing.ShouldWake`, a cheap
snapshot each check). Not when the standard action is available (an option may become worth taking at any moment), not
in turn-based combat. What a quiet wait can delay: a move during the cooldown that none of the triggers predicts (an
enemy walking away out of reach) - at most 2 s. Expected: most of the "wait" decisions skipped; the `[Perf] real-time`
line now ends with "N skipped while waiting for a cooldown". Tests: `RealTimePacingTests` (12).

**VAL-001.** The bar is applied again after every cost; the early check stays (costs only lower the value).

---

## D. Tactical score contract

**Scale.** Every option is in damage-equivalent points (`ValueModel`): damage counts as HP taken (capped at the target's
HP) plus the removal value `threat x (P(kill) + 0.25 (1 - P(kill)) x share of HP)`; a combatant's worth a round is
`4 + 3 x level` (CR for enemies), x its fight share (`EnemyThreat`, 0.5..2) for enemies, halved for summons.

| Family | Base | Chance | Horizon / count | Costs, gates | Notes |
|---|---|---|---|---|---|
| Weapon attack / full attack | `CombatMath.EstimateWeaponAttack` by the game's rules (bonus vs AC, damage after DR, crits, sneak, swarms) → `DamageValue` | per attack | one action | role/preference weight, wake factor (sleeping target), shot provokes, 5-foot step risk; full attack ≤ 5-foot step | modes (Power Attack...) priced per attack |
| Move+Attack / Charge | as above x0.9 / x1.1 (Charge) x0.95 | | | `MoveRisk` (attacks of opportunity: drop chance, damage; areas; exposure below half HP), one-move check in the dispatcher | |
| Damage spell / blast | `EstimateAbilityDamage` (game preview, resistances seen, learned blast share) | `EffectChance` (SR, attack roll, save, evasion) | per enemy in the area | friendly fire x2 (veto by default), slot discount in easy fights, provoke loss, concentration | kill actions: better of kill and damage |
| Hard / soft control, debuff | `ControlWorth` (threat x urgency) x strength 1 / 0.4 / 0.25 / 0.15 (minor) | effect chance, immunities, buff immunity | rounds `min(expected, 3)`, branch duration | allies caught (veto), melee allies heading in, downed allies under it, disabled targets x0.4, repeats x0.3 | |
| Mark | 0.15 x threat x rounds (0.1, one round, for one-shot) | | | only on the main attack's target (coupling) | caster-side marks by what they guard |
| Heal | HP restored x `HealPerHp` (0 at the threshold) + drop-chance reduction x ally worth | | | threshold (setting), potion threshold for one's own potions | revive: up chance x stays-up chance x `ReviveWorth` (6 rounds) |
| Buff | read parts (`BuffParts`) valued for the recipient: attack/damage gain, AC/saves/concealment kept off, temp HP, DR... over useful rounds, x role weight | | ≤ 5 recipients | already active, stacking, harmful area when growing; restraint: a standard-action pure buff must beat the best immediate action by 15 % | unread effects: quality x level x 60 % on a main action |
| Cleanse | `(2 + 2 x level) x min(rounds, 3)` per ally with something to remove | | | | |
| Toggles, Mobility, boosters, kineticist, maneuvers | own appraisals on the same scale | | | | see the partials |
| Fallback (unverifiable effect) | summon `(3 + 3 x level) x min(rounds, 4) x 0.5`; dispel 2; enchant `BuffPerRound x rounds x 0.5` | | once per combat per target (LIF-001: counted by performed casts) | never for items, never when a verifiable effect is 0 | |
| Movement | leave area: area cost avoided; withdraw: drop chance here - there; 5-foot step / back off: what provoking here would lose; advance: 0.35 x the attack (or the refused option) | | | `MoveRisk` | |

**Choice.** Quick (free/swift) action first when it clears its bar (2; 1 for a free action that spends nothing), else the
best main action ≥ 0.5; slots are not compared against each other, which is right (a swift action costs no standard).

**Checked for double counting.** Role weight applied once per family (damage in `AttackWeight` or `ScoreAbility`,
buff in the appraisal, heal once); attack-of-opportunity risk once per option (the stop share and the cost are
separate terms of the same risk, and since test5 the stop share is only drops and trips); the kill bonus is per
target; urgency only in the worth of taking an enemy down (not in the danger it poses). No double count found.

**Hard gates are gates.** `Affordable`, `memory.Failed`, `SilentlyFailing`, Progressive and the friendly-fire veto
remove options after scoring; couplings re-check affordability. No eligibility rule is expressed as a small penalty.

**Known heuristics** (calibrated on play logs, not derivable from the rules): worth a round, the control strengths,
the 3-round horizon, the 15 % buff margin, the 0.35 advance share, the fallback values. Unknown effects are never
given confident value: unread buffs at 60 %, unverifiable abilities once per combat, consumables never.

---

## E. Performance review

| Path | Rate | Measured | Note |
|---|---|---|---|
| Turn-based decision | ~3-6 a turn | median 20-57 ms, 95 % 87-190 ms (test4, 4 fights) | scales with abilities x targets (`EvaluateAbility` scores every target of every ability); acceptable for turn-based |
| Real-time decision | every 0.5-0.95 s per unit while nothing is pending | median 21-35 ms; ~75 % of them "wait" | PERF-001 |
| Rare 1 s decisions | 1-2 a fight | 986-1156 ms with 0 new profiles | most likely garbage collection on the user's laptop; the `[Perf] slow decision` line now says when one ran (test5) - hypothesis until the next log |
| `EnemyThreat.Refresh` | once a round, again when an enemy falls or joins | not measured apart | the slow-decision line says when it ran |
| `AbilityProfile` | built once per blueprint, cached for the session | "new ability profiles built" on slow decisions | structure cached, values per decision: right split |
| Per-frame caches (`CombatMath.FrameCached`, attack routines, revive answers, appraisals) | per frame | bounded (cleared at 256-512 entries) | correct keys; turn-based decisions happen on separate frames, so no stale value across a command |
| Per-frame real-time budget | 3 ms | one decision per frame in practice (a decision is 20-35 ms) | spreads units over frames; worst frame = one decision |

No other optimisation is proposed without a profile: the remaining costs are per decision and scale with real
content (abilities, targets), and caching tactical values across decisions would risk stale choices.

---

## F. Plan

Done in this pass (small, independent, tested):

1. LIF-001 - casts counted when performed (`CastLedger`, `CombatMemory.SettleCast`).
2. PERF-001 - quiet real-time waits during the standard-action cooldown (`RealTimePacing`).
3. VAL-001 - item bar after costs.
4. DOC-001 - `CLAUDE.md` architecture notes.

Proposed, not done:

| Item | Option A (minimal) | Option B | Recommendation |
|---|---|---|---|
| INT-001 trap avoidance | a line in the README | a setting "Steer my own walk orders around detected traps" (default on: today's behaviour) | B, needs the user's decision (a new UI option) |
| OBS-001 silent catches | none | one helper `Diagnostics.Swallowed(site, ex)` that logs once per site in verbose mode, applied to the value paths | B in a later maintenance pass; behaviour-neutral but touches many files |
| LIF-003 out-of-combat buff mid-fight | gate `ApplyPermanentBuffsOutOfCombat` on the party not being in combat | - | only if a log shows it |
| TEST-001 | keep extracting pure rules with tests when they change | decision-level fixtures need the game assemblies at test time | A |

Rollback: each batch is self-contained (`CastLedger` + two call sites; `RealTimePacing` + `RealTimeTactics.Act` and
one reset; one `continue` in `EvaluateAbility`).

---

## G. Change log (audit batches)

| File | Change |
|---|---|
| `Tactics/CastLedger.cs` (new) | cast records per caster: record, confirm, take back; `Performed(started, acted)` |
| `Tactics/CombatMemory.cs` | casts and uses through the ledger; `RecordCast(..., command)`, `SettleCast` |
| `Tactics/TacticalEngine.cs` | `SettleCast` before each decision; `RecordCast` with the command; item bar after costs |
| `Tactics/RealTimePacing.cs` (new) | quiet-wait rule |
| `Tactics/TacticalDriver.cs` | `RealTimeTactics`: quiet waits, snapshot, skip count, `ClearQuiet` |
| `Tactics/FightStats.cs` | `[Perf] real-time` line: skipped decisions |
| `GameInterface/CustomBrainPatch.cs` | quiet waits cleared at combat start |
| `tests/CompanionAI.Tests` | `CastLedgerTests`, `RealTimePacingTests` (23 tests) |
| `CLAUDE.md` | architecture notes; pointer to this document |

Deliberately unchanged: turn-based pacing, the score scale and coefficients, the legacy engine, settings defaults,
optional-mod handling.

---

## H. Validation report

| Check | Result |
|---|---|
| Build against the game assemblies | succeeded, 0 warnings, before and after |
| Unit tests | 67 passed before; 90 passed after (23 new) |
| In-game | **not run** (no game here). To check in the next test build: real time - `[Perf] real-time ... N skipped while waiting for a cooldown`, decision totals per fight against test4 (6.1 / 4.5 / 5.6 / 4.1 s), units still react at once to damage and new areas, no slower first attack after a cooldown; both modes - an ability whose command was interrupted is offered again (verbose: "its last cast ended without acting - not counted as cast") |
| Before traces | the user's 0.3.20-test4 logs (turn-based and real-time) |

---

## I. Backlog and stopping decision

Open: INT-001 (user decision), OBS-001 (maintenance pass), LIF-003 (watch), the slow-decision cause (next log). Legacy
engine: out of scope; kept until 0.4.0.

Stopping here: the remaining items are low-impact, need a user decision, or need in-game evidence first. The modern
pipeline's high-risk paths (decision, scoring, dispatch, memory) have a documented result; the larger refactors that a
cleaner architecture would suggest (splitting `TacticalEngine.cs`, decision-level test fixtures) have no defect behind
them that a smaller change does not address.
