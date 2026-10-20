# Active-ability audit reports (read-only, 0.3.17 cycle)

These are the three audits behind docs/ACTIVE_ABILITY_PLAN.md. Line numbers refer to commit ed7d098.

## 1. The mod's active-ability lifecycle


## Summary (under 1500 words)

**Biggest finding:** the live mod has no support at all for activatable abilities (`ActivatableAbility` toggles). It never reads, scores, switches on or switches off any toggle (Power Attack, Rage, bardic performance, Spell Combat, kineticist infusions, metamagic rods…).
- The only places that touch them: `AbilityDiscovery.cs:130-131` reads toggles only to find and exclude kinetic blade internals, and `TurnBasedController.cs:235` refuses to clear a queued `UnitActivateAbility`.
- `CandidateKind` has only `{WeaponAttack, Ability, Move}` (`TacticalEngine.cs:32`).
- The "toggles" added in 0.3.3 are *abilities* that apply and remove the same buff (`AbilityProfile.IsToggle`, `AbilityProfile.cs:130`, e.g. Shift Form). In combat they get switched on when worth it and never back off; the out-of-combat routine skips them.

From the game code:
- Bardic performances start through a `UnitActivateAbility` command (Standard action by default, `UnitActivateAbility.GetCommandType`). `Stop()` clears `IsOn` for that kind of toggle at combat end. So **performances are off in every AI-driven fight unless the player restarts them by hand each fight** (strongly supported; I did not check each blueprint's flags).
- The AI never switches anything on, so it cannot rage a caster. It never switches anything off either: Fighting Defensively, Power Attack and Deadly Aim stay as the player left them. Its estimates do reflect toggles that are on, because they go through the game's own rule calls.
- The AI's own commands can switch toggles off through game rules: `ActivatableAbility.HandleUnitRunCommand` / `HandleUnitCommandDidAct`, plus the mod's `InterruptAll` calls in RT stuck/nausea handling, the TB end-turn unblock, and leaving combat.

**Discovery** (`AbilityDiscovery.Gather`, 26-112):
- Reads `unit.Abilities` (class/feat abilities, items, held touch charges) and per spellbook `GetAllKnownSpells`, `GetAllCustomSpells` and `GetSpecialSpells(0..10)`. Dedup key: blueprint, spellbook, item, metamagic.
- Leaves out:
  - Hidden abilities. This uses the game's `Ability.Hidden`, which matches the action bar.
  - Kinetic blade internals.
  - Variant parents, which are replaced only by their visible true variants.
- **Every other conversion from `GetConversions` is dropped:** cleric/druid spontaneous conversions, shadow spells, Combat Healer, AbilityResourceOverride, RestoreSpellSlot, ConvertSpellLevelToResource.

**Early filters:**
- `IsAvailable` (game legality, silent).
- `IsBlacklistedForCombat` (`AbilityClassifier.cs:2154-2252`):
  - variant parents;
  - a top-level `Demoralize` action (Demoralize, Dazzling Display);
  - enemy touch spells that only fatigue;
  - level-0 enemy touch abilities with no damage in the parent's own action list. Class abilities read as level 0 at character level 0-1 (`SpellLevel` falls back to CL/2), and damage inside a sticky-touch delivery is not seen;
  - a Daze-cantrip rule that never fires for vanilla Daze (the logs show Daze cast).
  - The fatigue and cantrip rules log at normal level on every decision.
- `ConsumableFactor`: non-heal potions are worth 0 unless `Difficulty ≥ 0.8`.
- Target sides by intent (`TacticalEngine.cs:1231-1235`): **point-only abilities that are neither harmful nor damaging get no target at all**, likely including vanilla Summon Monster (strongly supported).
- `TargetOn`/`CanTarget`; range (only Standard-action abilities may walk up).
- `ScoreAbility ≤ 0` drops the option, and its "why" is thrown away.
- In `Decide`: Affordable / `memory.Failed` / `SilentlyFailing` (`CastOutcome`: two silent failures on a target block it for the fight; replacing a held touch charge) / Progressive / `CoupleOwnAttackMarks`.
- Thresholds: quick ≥ 2.0, main ≥ 0.5. Swift/free options worth less than 2 are never chosen; the logs show `Commune with Constellations=1.0` at the top and then END TURN.

**AbilityProfile:**
- Walks only `AbilityEffectRunAction`, sticky-touch delivery, nested action lists found by reflection, referenced abilities, spawned areas (enter/round; exit never walked) and `AbilityTargetsAround`.
- Recognises damage, heal, target/caster buffs (control from descriptors and conditions), summon, area, dispel, removal/cleanse, enchant (partly by an "Enchant"/"EnhanceWeapon" type-name heuristic), burn relief, ability toggle, save, attack roll, charge, own-attack mark.
- **Not understood:** every other custom-logic ability (Vital Strike, Cleave, Overrun, teleports, Fly, shadow spells, weapon-delivered abilities), the effect of combat maneuvers, Demoralize's buff (stored as a buff reference, not an apply action), `AbilityApplyFact`, clicked-point effects, stat damage/drain, kill, restore resource, custom mod actions.
- These fall to a fallback (`TacticalEngine.cs:1700-1746`): summon / enchant / dispel (only with a target to dispel) / otherwise "utility 1.0" once per combat per blueprint.
- The fallback is logged once per session ("no recognised effect – components=[…]"), and only on the utility path.

**Special actions:**
- Weapon attacks use `UnitAttack`; TB forces full or single attack, RT lets the game decide.
- Move+Attack uses a dispatcher path plus a reach check.
- Charge is the game's ability (`AbilityCustomCharge`), offered only when a normal move cannot reach.
- Coup de Grace is the game's ability, valued by `CombatMath.EstimateCoupDeGrace`, only when already in reach.
- Emulated with `UnitMoveTo`: 5-foot step (the dispatcher sets the game's FiveFootStep movement limit), Withdraw, Leave area, Advance, and stand up (a short walk the game turns into getting up; TB only — in RT a prone unit is treated as "cannot act").

**Metamagic:**
- The AI never builds a metamagic variant. It uses the metamagic copies the game already has and auto/rod metamagic through `HasMetamagic`.
- Modelled: Empower/Maximize in estimates, Quicken through `RuntimeActionType`, Selective, Reach.
- Not modelled: Extend, Bolster, Persistent and the rest.
- Cast counters are keyed by blueprint only, so all copies of a spell share them.

**Ability-plus-attack combos:**
- Smite-like marks are kept only on the enemy of the best main action.
- Next-attack buffs are worth about 2 rounds.
- Vital Strike, Cleave and maneuvers are effectively unused (utility once per combat).

**Feedback and state:**
- Every engine command goes through `CommandDispatcher.Run`. PreBuff bypasses it with a direct `Commands.Run`, against the CLAUDE.md rule.
- `RecordCast` counts issued commands, not effects.
- `CastOutcome` judges only verifiable profiles; it counts any rule rolled by the caster as evidence.
- `LearnFromLastCommand` fills `memory.Failed`. That memory lasts one round, which in RT is 6 seconds.
- Reset points:

| State | Reset |
|---|---|
| CombatMemory | party combat start/end, or whenever the round number goes backwards — which can also happen on a TB↔RT switch mid-fight |
| CastOutcome, DamageObserver, SaveObserver | combat end only |
| Profile cache | never; `AbilityProfile.ClearCache` has no caller |

**Defects, labelled:**
- **Confirmed:**
  - No toggle support.
  - Conversions dropped.
  - The RT log says "Blacklisted stuck ability", but that goes to the legacy `FailedAbilityTracker`, which the new engine never reads.
  - Swift/free threshold 2.0 suppresses small swift/free options.
  - Blacklist log spam; the Daze rule is dead.
  - The out-of-combat buff routine and PreBuff ignore `ShouldControlUnit`, and leaving combat interrupts every party member's commands.
  - The out-of-combat routine and PreBuff read only `Abilities.Visible`, so no spellbook spell is ever pre-buffed or kept up.
  - Point casts are recorded under target "-" but the summon fallback looks them up by unit id.
- **Strongly supported:** performances off in every fight; vanilla summons never get a target; no cross-unit claims in RT (two allies can cast the same buff or heal on one target); Move-action abilities always count as affordable in RT; memory reset on a mode switch.
- **Hypothesis:** a rod left on gets wasted on whatever spell comes first; `CastOutcome` misses some silent failures.
- **Unknown:** how Spell Combat interacts with a plain cast.

**Diagnostics missing:** reasons for silent drops (`IsAvailable`, consumable 0, no target side, `CanTarget`, range, and the discarded `ScoreAbility` "why"); RT has no "nothing worth doing" line; the decision line shows only the top 5 usable options; nothing at all about toggle state; no log of conversions not offered; no marker when memory resets mid-fight.

---

# FULL REPORT — Active-ability lifecycle in AI Control 0.3.17 WIP (read-only audit)

Labels: **confirmed** = read in mod and game code (or seen in a log); **strongly supported** = the code path is clear but blueprint data was not checked; **hypothesis** = plausible but not traced to the end; **unknown** = could not be decided here. Game references are to the decompiled source under scratchpad/game.

## A. Architecture map (live paths)

### A.1 Entry points and drivers

**Turn-based**
- Hook: `TurnBasedPatches.TurnControllerTick_Postfix` (CustomBrainPatch.cs:284-350). Runs only in Preparing/Acting, with `ShouldControlUnit` (139-185) and `EnableInTurnBased`.
- `TurnBasedController.ProcessTurn` (47-110):
  - stand up (61-62, 198-228);
  - waiting and timeouts (116-188);
  - `IsClearableLeftover` keeps `UnitActivateAbility` and AoO commands (230-236);
  - attempt loop of 3 (83-101).
- When the turn should end but cannot, `TurnControllerTick_Postfix` calls `InterruptAll` (336-342).

**Real time**
- Hook: `TickBrain_Prefix` (CustomBrainPatch.cs:43-127). Returns false so native AI is suppressed; gates on `NextCommandTime` and on a `UnitUseAbility` that started but has not acted.
- `RealTimeController.ProcessUnit` (119-336):
  - throttle and cannot-act checks (148-197; `GetCannotActReason` 537-552 treats Prone as cannot act);
  - nausea drop (204-205, 515-531);
  - AoE escape (211-223);
  - out of combat → `ApplyPermanentBuffsOutOfCombat` (233-239);
  - while `!Commands.Empty`: re-plan after damage (247-253), then stuck check → `FailedAbilityTracker` + `InterruptAll` (255-320);
  - then `RealTimeTactics.Act` (326-329; TacticalDriver.cs:154-186), or the legacy engine when the setting is on.

**Shared driver**
- `TacticalDriver.LearnFromLastCommand` (23-58), `RecordIssued` (61-69), `RecordRejected` (72-79), `ActionWasSpent` (82-101).

**Out of combat**
- `HandleObjectLeaveCombat` (CustomBrainPatch.cs:484-512): InterruptAll, then permanent buffs.
- `OnPartyCombatEnd` → `ApplyPermanentBuffsToParty` (376-410; RealTimeController.cs:2167-2185).
- `ApplyPermanentBuffsOutOfCombat` (2058-2130) with `IsWorthKeepingUp` (2139-2162).
- PreBuff button: `PreBuffController.StartPartyBuff`/`ExecuteBuff` (135, 333-355; direct `Commands.Run` at 341).

### A.2 `TacticalEngine.Decide` (117-190)
1. `BuildContext` (327-427). RT: `HasMove = CanMove`, range = speed×6 (369-376).
2. `CastOutcome.Settle` (137).
3. Candidates (139-143): weapons (914-1004), abilities (1020-1038), movement (1760-1911); then `PriceHeldCharge` (197-212).
4. Filter (145-148): Value>0, Affordable (283-303), not in `memory.Failed`, not `SilentlyFailing` (220-224).
5. Progressive gate (151-155, 264-281).
6. `CoupleOwnAttackMarks` (157-158, 233-258).
7. Choice (160-165): quick ≥ 2.0, otherwise main ≥ 0.5.
8. Logging: `LogDecision` (305-321); TB-only "nothing worth doing" (173-183) and "no option at all" (185-188).

### A.3 Ability pipeline
1. `Gather` → `IsAvailable` (1026) → `IsBlacklistedForCombat` (1027).
2. `EvaluateAbility` (1193-1327):
   - profile, `ConsumableFactor` (1170-1186), `SlotOf` (1156-1167);
   - charge branch (1208-1212 → `EvaluateCharge` 1347-1377);
   - target sides (1220-1238), `TargetOn` (AbilityProfile.cs:961-972), range/approach (1249-1268);
   - `ScoreAbility` (1379-1754), then multipliers: consumable, ×0.9 when moving, `CastProvokeRisk`, concentration, archer touch, `MoveRisk` (1272-1297);
   - Progressive success chance (1315-1324).
3. `Execute` (2076-2152): `UnitUseAbility`, or a TB touch approach first (2109-2121) → `CommandDispatcher.Run` → `RecordCast` + `CastOutcome.Begin` (2140-2146).

## B. Per-category findings

### B.1 Abilities

**Discovery (AbilityDiscovery.cs:26-112)**
- Reads `unit.Abilities.Enumerable` (70-86) plus per book `GetAllKnownSpells` (97), `GetAllCustomSpells` (99) and `GetSpecialSpells` 0-10 (101-105).
- Dedup key: blueprint|book|item|metamagic (143-152).

Left out:

| What | Where | Game reason | Can it hide something useful? |
|---|---|---|---|
| `Ability.Hidden` (non-items) | 77-84 | fact hidden ∨ `Hide()` ∨ `Blueprint.Hidden` ∨ `!IsVisible` (game Ability.cs:33-43), the same test as ActionBarVM.CollectAbilities (491-499) | low risk |
| Items: `Blueprint.Hidden ∨ !IsVisible` | 77-79 | — | low |
| Kinetic blade internals | 40-44, 119-140 | game-internal | low |
| Variant parents | 46-64 | only visible true variants kept (`HasVariant`, `IsVisible`) | **yes**: other `GetConversions` results (game AbilityData.cs:1978-2120) are dropped — spontaneous conversions, `AbilityApplyFact` facts, ResourceOverride, RestoreSpellSlot, ConvertSpellLevelToResource, `AbilityShadowSpell` spells, Combat Healer quicken. Non-variant spells are never expanded. |

Read failures are reported once (`ReportOnce`, normal log).

**Early filters**
- `IsAvailable`: game legality (AbilityData.cs:720-860), silent.
- `ConsumableFactor`: policy; non-heal potions 0 unless `Difficulty ≥ 0.8` (1179); scrolls/wands 0.7×UMD; other items 0.85×UMD. Silent.
- Target sides (1231-1235): enemies need `CanTargetEnemies` or (point ∧ harmful/damage); allies need `CanTargetFriends/Self`. So point-only non-harmful abilities get no target.
- `TargetOn`: `CanTarget` on the unit, then on its point.
- Range: only Standard abilities may approach (1262-1263); a touch blocked by height is skipped (1260-1261).
- `ScoreAbility ≤ 0` (1270-1271): the "why" is dropped.
- `Decide` filters as in A.2; thresholds as in A.2. Log evidence: `Commune with Constellations(utility)=1.0` then END TURN.

**`IsBlacklistedForCombat` (AbilityClassifier.cs:2154-2252)** — no cache, runs every decision.
1. HasVariants (2166; Verbose).
2. Top-level `Demoralize` action in RunAction (2172-2184; Verbose). Removes Demoralize and Dazzling Display. A nested Demoralize is missed and becomes "utility", because Demoralize's buff is a `BlueprintBuffReference` field (game Demoralize.cs:22-58).
3. Enemy touch (DeliverTouch or StickyTouch) that applies only Fatigued/Exhausted or has the Fatigue descriptor, with no damage (2186-2216; `Main.Log` on every decision).
4. SpellLevel 0, enemy touch, no damage in the parent's own RunAction (2219-2235; `Main.Log` on every decision). Misses damage inside the sticky delivery ability. `SpellLevel` for class abilities = PretendSpellLevel / SpellList / min(9, CL/2) (AbilityData.cs:412-452), so class touch powers of level-1 characters count as level 0.
5. SpellLevel 0 with the Daze descriptor on the blueprint (2237-2242). Does not fire for vanilla Daze: Camellia casts `Daze->Goblin Stirrer(cc)` in the scratchpad logs. Dead rule.

### B.2 Activatable abilities

**Confirmed: the mod never reads or writes toggle state.**
- Only two references: `KineticBladeInternals` (AbilityDiscovery.cs:130-131) and `IsClearableLeftover` (TurnBasedController.cs:235).
- No candidate kind, scoring, hysteresis, group logic or `UnitActivateAbility` command anywhere.
- Applies to Power Attack, Deadly Aim, Fighting Defensively, Combat Expertise, Rage, Bloodrage, performances, Inspire Rage acceptance, Spell Combat/Spellstrike, kineticist substance infusions and Kinetic Blade, Arcane Pool properties, metamagic rods, judgments, mobility.

**Ability-based "toggles" (0.3.3)**
- Definition: `IsToggle` (AbilityProfile.cs:130).
- Scored as buffs. `BeneficialBuffsRemoved` allows a switch only when the new buff is better (TacticalEngine.cs:1658-1665), so they are effectively "on when worth it, never off".
- Not verifiable (2071), so CastOutcome never judges them.
- Excluded from the out-of-combat routine (RealTimeController.cs:2145).
- **Not** excluded by PreBuff (strongly supported).

**Game behaviour (decompiled)**
- `SetIsOn` (ActivatableAbility.cs:298-325) → `OnDidTurnOn` (737-812): `Immediately` toggles start at once; `WithUnitCommand` toggles issue `Commands.Run(new UnitActivateAbility)` (809).
- `UnitActivateAbility.GetCommandType` (34-60): BardicPerformance with Standard type → Standard / Move / Swift / Free depending on Quicken Performance and Singing Steel.
- `Stop()` clears `m_IsOn` for WithUnitCommand toggles. `OnNewRound` and `StopOutOfCombat` stop `DeactivateIfCombatEnded` toggles after combat (379-399; controller line 97).
- `ActivateOnCombatStarts` (Immediately + DeactivateIfCombatEnded) toggles restart by themselves on joining combat (401-407).
- ⇒ **WithUnitCommand performances are off at the start of every fight; the mod never restarts them** (strongly supported).
- Ways the AI's own commands switch toggles off:
  - `HandleUnitRunCommand` (413-423): a toggle with ActivatableAbilityUnitCommand of type X turns off when another command of type X runs.
  - `HandleUnitCommandDidAct` (425-458): an on-but-not-started WithUnitCommand toggle turns off on another acted command.
  - Mod `InterruptAll` calls: RealTimeController.cs:301 and 525; CustomBrainPatch.cs:336-342 and 499-503.
- `OnNewRound` spends the action of an ActivatableAbilityUnitCommand toggle each round (393-397). The engine sees this only as HasStandard/HasSwift being false.

**Valuation vs. state**
- Estimates reflect toggles that are on: `RuleCalculateAttackBonus` / `RuleCalculateWeaponStats` (CombatMath.cs:125-133); rod and auto metamagic via `UnitPartSpecialMetamagic` (AbilityData.cs:1506-1520).
- They are never optimised. The AI cannot rage a caster, and it never turns off Fighting Defensively, Combat Expertise or Power Attack.
- A rod left on is spent by the first eligible AI spell (MetamagicRodMechanics.cs:75-100). How often that hurts is a hypothesis.

### B.3 Special actions

| Action | Execution | Notes |
|---|---|---|
| Single attack | `UnitAttack`, `IsSingleAttack` in TB (2084-2094) | — |
| Full attack | `UnitAttack`, `ForceFullAttack` in TB; RT lets the game decide | `CanFullAttack` (343-344); in reach or within a 5-ft step (945-963); dispatcher refuses paths longer than the step (CommandDispatcher.cs:100-107) |
| Move+Attack | `UnitAttack` + TB `ForcedPath`, path-end reach check (CommandDispatcher.cs:90-96) | `MoveRisk` pricing |
| Charge | game ability (`AbilityCustomCharge`) via `UnitUseAbility` | `IsCharge` (AbilityProfile.cs:317); `EvaluateCharge` (1347-1377): CanFullAttack, CanTarget min/max range, only when a normal move cannot reach; FullRound slot |
| Coup de Grace | game ability (GUID 32280b13…) | eligibility = `CanTarget`; value = `EstimateCoupDeGrace` (CombatMath.cs:173-208) via `CoupDeGraceValue` (445-465); only already in reach; not verifiable |
| 5-foot step | `UnitMoveTo` + dispatcher `SetMovementLimit(FiveFootStep)` (CommandDispatcher.cs:320-335) | TB only (1790-1835); RT "Back off" 4 m, priced |
| Withdraw | plain `UnitMoveTo` (1841-1866) | not the Withdraw action rules; provokes, priced |
| Leave area | `UnitMoveTo` (1921-1980) | — |
| Advance | `UnitMoveTo` (1871-1910) | — |
| Stand up | short `UnitMoveTo` (`IssueStandUp` 2025-2061); the game converts it | TB only; RT prone = "cannot act" (RealTimeController.cs:550) |
| Touch delivery | held StickyTouch fact; TB approach first | `PriceHeldCharge`, `WouldReplaceHeldCharge`, stuck-charge removal (CastOutcome.cs:274-290) |
| Aid Another, maneuvers, Vital Strike, Cleave | game abilities | the last three fall to "utility" |

### B.4 Metamagic
- No AI-built variants. It uses only existing `AbilityData`: prepared metamagic slots and spontaneous presets via `GetAllCustomSpells`, item metamagic, and auto/rod metamagic.
- Modelled:
  - Empower ×1.5 and Maximize (CombatMath.cs:234-261, 279-375);
  - Quicken via `RuntimeActionType` (SlotOf 1156-1167; AbilityData.cs:926-945);
  - spontaneous metamagic full-round via `RequireFullRoundAction`;
  - Reach via `GetApproachDistance`;
  - Selective (AbilityProfile.cs:980-987).
- Not modelled: Extend, Bolster, Persistent, Dazing, Intensified (CL capped at 10 in `EvaluateDice`), mythic metamagic.
- Conversions are not offered.
- Rods are toggles, never used deliberately.
- `CombatMemory` keys by blueprint GUID only (CombatMemory.cs:72-95), so copies share `TimesUsed` and `LastCastRound`.

### B.5 Abilities coupled to other actions
- Own-attack marks: `MarksForOwnAttacks` (AbilityProfile.cs:1014-1030) plus `CoupleOwnAttackMarks` (233-258). Kept only on the best main option's enemy, re-aimed there, or dropped.
- Next-attack buffs: `IsSpentByUse` (628-639), worth about 2 rounds. A one-round self buff is worth 0 when no attack is left (1635-1651).
- Vital Strike, Cleave, Overrun, teleports, Fly, CustomAttack/MeleeAttack, shadow spells: `AnalyzeAbility` reads only RunAction, Projectile, Touch and StickyTouch (393-415). No effect → utility 1.0 once per combat (1739-1744). Unverifiable.
- Demoralize / Dazzling Display: blacklisted. Cornugon Smash and Shatter Defenses are hit-trigger features the game applies itself.
- Maneuvers: only `OnSuccess` is walked; the maneuver effect is not modelled.

### B.6 AbilityProfile

**Walks:**
- RunAction (397-403); StickyTouch delivery (409-414);
- action fields by reflection (1103-1132); `BlueprintAbilityReference` fields (1134-1149, depth ≤ 4);
- spawned areas: enter + round, move damage dropped, exit not walked; plus AbilityAreaEffectBuff (539-592);
- `AbilityTargetsAround` (319-335);
- subjects (595-613); Conditional guards with per-target safe-condition evaluation (743-758, 814-866).

**Recognises:** damage (half on save); heals (target / caster); apply-buff (duration, permanent, day-long, persistent, one-round, spent-by-use); area buffs; control from descriptors and AddCondition/BuffStatusCondition (1162-1200); summon; area; dispel; RemoveBuff / RemoveBuffsByDescriptor; enchant (EnchantWornItem plus the type-name heuristic at 521-523); HealBurn; toggle; save; attack roll; charge; own-attack mark.

**Not understood:**
- Other custom logic: Vital Strike, Cleave, Overrun, Dimension Door, Fly, Teleportation, Flash Step, Dweomer Leap, Tongue Grab, Custom/MeleeAttack, DeliverAttackWithWeapon, DeliveredByWeapon, Chain, Ricochet, Delay, ShadowSpell.
- Other components: AbilityApplyFact, RunActionOnClickedPoint/Unit, EffectMiss, ExecuteActionOnCast.
- Other actions: CombatManeuver's own effect, Demoralize, Kill, stat damage/drain, RestoreResource, HealStatDamage, temporary-HP actions, AddFeature, custom mod actions.
- Unsafe branch conditions → both branches counted, logged once (835).

**Fallback (1700-1746)** — when value ≤ 0, the effect is not verifiable, and the ability enchants / summons / dispels / removes / has no detectable effect, and it was not cast on this target this combat:

| Kind | Value |
|---|---|
| summon | (3+3L)·min(rounds,4)·0.5·(≥2 enemies ? 1 : 0.6) |
| enchant | 0 if active, else (1+1.5L)·rounds·0.5 |
| item | 0 |
| dispel/remove | 2 only when there is something to dispel |
| otherwise | utility 1.0 if `TimesUsed == 0` |

Logging: `DescribeStructureOnce` once per session, only on the utility path (1052-1067); `DescribeOnce` on issue.

### B.7 Execution, feedback, reset

- **Dispatcher:** `CommandDispatcher.Run` (38-132) attaches the TB path and refuses: no movement, no path, step taken, path ends out of reach, full attack too long. It verifies the command is in a slot and logs ISSUED/REJECTED. No `UnitActivateAbility` is ever issued.
- **After issue:** `RecordCast` on issue (2142). `CastOutcome.Begin` only for verifiable profiles (2069-2073, 2145):
  - evidence = attack/damage/heal/save/SR rules, a buff added, or a rule-failed cast (CastOutcome.cs:348-436);
  - verdict at execution end or at `Settle` (6 s timeout) (176-208, 326-346);
  - 2 failures → blocked for the fight (139-145, 292-308).
- **Learning:** `LearnFromLastCommand` marks a failure when the command did not act, was force-finished unstarted, left its action unused, or was a move that went nowhere. Exempt: a move with ≥ 1 m of progress, `IsIgnoreCooldown` commands. `RecordRejected` adds to `Failed`, and to `Unreachable` for "path ends out of reach".
- **CommandTrace:** ISSUED / ENDED (result, acted, moved); map capped at 64.

Reset points:

| State | Reset |
|---|---|
| TurnMemory | per unit per round; RT round = 6 s since the first decision (CombatMemory.cs:32-42, 97-106) |
| CombatMemory (Casts/Uses/Progressive/Bosses/condition cache) | `ResetCombat` at party combat start/end (CustomBrainPatch.cs:364, 380), or `Sync` when the round goes backwards (51-57) — which a mid-fight TB↔RT switch can trigger (strongly supported) |
| CastOutcome, DamageObserver, SaveObserver | combat end only (386-389) |
| FailedAbilityTracker | start and end |
| Profile caches, Described sets, ReportedUnknown | session; `AbilityProfile.ClearCache` has no caller |

TB vs RT:
- RT lists weapon attacks during cooldown (919).
- RT `HasMove = CanMove` (374), so Move-slot abilities are always affordable there.
- RT: no 5-foot step ("Back off" instead), no forced full/single attack, no touch approach split.
- RT has no "nothing worth doing" line; its waits are logged only at Verbose.
- RT adds re-plan after damage, the stuck interrupt, the AoE escape outside the engine, and the nausea drop.

### B.8 Out of combat
- **Permanent-buff routine:**
  - pool = `ClassifyAllAbilities` = `unit.Abilities.Visible` only (AbilityClassifier.cs:1512-1529; game UnitLogicCollection.cs:13): no spellbook spells, no items;
  - legacy `PermanentBuff ∧ !IsAlreadyApplied` (1534-1539), then `IsWorthKeepingUp`: no item, toggle, one-shot, burn, damage, heal, summon or harmful effect; a buff of hours or permanent, recognised value ≥ 0.5, absent;
  - casts only the first candidate, on self, via the dispatcher;
  - leave-combat and combat-end triggers skip `ShouldControlUnit`, and leaving combat runs InterruptAll on every party member.
- **PreBuff:** `Abilities.Visible` via `PreBuffAnalyzer` (no spellbook spells); no `ShouldControlUnit`, no toggle exclusion; direct `Commands.Run`; includes pets and summons.

### B.9 Legacy RT engine
- Same discovery, `ConsumablePolicy`, `IsAvailable` and blacklist (SituationAnalyzer.cs:583-640, 997-1014).
- Legacy timing buckets plus geometric-mean scoring (BuffScorer borrows `BuffWorth`).
- Uses `FailedAbilityTracker`, `PendingActionTracker` and `_abilityCooldowns`.
- No toggle handling either.

## C. Exclusions (where / reason / logged / risk)

| # | Where | Exclusion | Logged | Risk |
|---|---|---|---|---|
| C1 | — | control policy | at turn start | none |
| C2 | 128-134 | no enemies in context → no buffs or heals at all | yes | medium |
| C3 | — | Hidden | once | low |
| C4 | — | kinetic blade internals | once | low |
| C5 | — | conversions | not logged | **medium** |
| C6 | — | `!IsAvailable` | silent | low |
| C7 | blacklist | HasVariants | Verbose | — |
| C8 | blacklist | Demoralize | Verbose | **medium** |
| C9 | blacklist | fatigue touch | every-decision log | low-medium |
| C10 | blacklist | level-0 enemy touch | every-decision log | **medium at CL ≤ 1** |
| C11 | blacklist | Daze rule | Verbose | dead rule |
| C12 | — | evaluation exception | once | — |
| C13 | — | consumable settings | — | none |
| C14 | 1179 | non-heal potions at Difficulty < 0.8 | silent | **medium** |
| C15 | — | UMD 0 | silent | low |
| C16 | — | charge conditions | — | low |
| C17 | — | no target side | silent | **point-only helpful abilities / summons** |
| C18 | — | `CanTarget` | silent | low |
| C19 | — | range / slot | silent | low |
| C20 | — | `ScoreAbility = 0`: heals enemy, no effect on target, nothing to gain, friendly fire, already active, buff present, cast this round, no quality, mode switch not better, nothing to remove, utility used | silent | diagnostics gap |
| C21 | — | not affordable | only in TB "nothing worth doing" | low |
| C22 | — | `memory.Failed` (6 s in RT) | logged | medium in RT |
| C23 | — | CastOutcome block / held charge | logged | low-medium |
| C24 | — | Progressive | logged | by design |
| C25 | — | mark decoupled | silent | low |
| C26 | — | quick threshold 2.0 | — | **small swift options never used** |
| C27 | — | utility once (counts issues, not effects) | — | low |
| C28 | — | RT prone | Verbose | relies on the game |
| C29 | — | out-of-combat Visible-only pool | — | spells and items never kept up |

## D. Defects / gaps

| # | Finding | Label |
|---|---|---|
| D1 | No ActivatableAbility support | confirmed |
| D2 | Performances off every fight | strongly supported |
| D3 | Conversions dropped | confirmed |
| D4 | Point-only helpful abilities and summons get no target | strongly supported; the "-" key mismatch is confirmed |
| D5 | Level-0 touch rule reads the wrong level and the parent's damage only; log spam | confirmed |
| D6 | Daze rule dead | confirmed |
| D7 | RT "Blacklisted stuck ability" writes to `FailedAbilityTracker`, which the tactical engine never reads | confirmed |
| D8 | No cross-unit claims in RT | strongly supported |
| D9 | Quick threshold 2.0 suppresses small swift/free options | confirmed (log) |
| D10 | `RecordCast` / `TimesUsed` count issues, not effects | confirmed |
| D11 | Memory keys ignore metamagic, book and item | confirmed |
| D12 | Custom-logic abilities unmodelled | confirmed |
| D13 | Non-heal potions ≈ 0 | confirmed |
| D14 | Out-of-combat routine and PreBuff ignore `ShouldControlUnit`; leave-combat InterruptAll; PreBuff uses direct `Run` and has no toggle exclusion | confirmed |
| D15 | Out-of-combat and PreBuff pools never include spellbook spells | confirmed |
| D16 | Reset on a mid-fight TB↔RT switch | strongly supported |
| D17 | RT Move-slot abilities always affordable | strongly supported |
| D18 | AI commands switch toggles off through game rules | confirmed in game code; frequency unknown |
| D19 | Spell Combat interplay with a plain cast | unknown |
| D20 | Rod wasted on the first eligible spell | hypothesis |
| D21 | CastOutcome evidence is too broad | hypothesis |
| D22 | No-enemy context skips buffs and heals | confirmed |

## E. Diagnostics

**Exists:**
- `[Tactics]` decision line (top 5 usable options).
- TB "nothing worth doing" / "no option at all".
- `[Abilities] left out…`; once-only errors.
- `[Profile]`: analysis error, unevaluated condition, area filter, no recognised effect, buff analysis.
- `[AbilityClassifier] BLACKLISTED`.
- `[Cmd] ISSUED/REJECTED/ENDED`.
- `not retrying this turn`, `could not be issued`.
- `[Cast] did nothing…` and touch diagnostics.
- `[Progressive]`; RT stuck-command messages.

**Missing:**
1. A per-ability rejection reason: `IsAvailable`, consumable 0, no target side, `CanTarget`, range, and the discarded "why" from `ScoreAbility`.
2. A full option dump beyond the top 5, and an RT equivalent of "nothing worth doing".
3. Any toggle state or toggle change logs.
4. Conversions not offered.
5. A once-per-ability blacklist summary, instead of spam or Verbose.
6. Structure logs for abilities that end at 0 for reasons other than the utility path.
7. A correction to the misleading RT "Blacklisted" message.
8. A marker when memory resets mid-fight.
## 2. Game mechanics against the claims


Source labels:
- **[CODE]** – Assembly-CSharp. Paths are relative to `.../scratchpad/game/`.
- **[TTT]** – what Tabletop Tweaks shows about vanilla blueprints.
- **[BP?]** – blueprint data, not in code. GUIDs come from `dec_firebird/BlueprintCore.Blueprints.References`, which holds names and GUIDs only.
- **[LOG]** – the mod's own runtime logs in `scratchpad/log*_main.txt`.
- **[WEB]** – Steam forum posts. The fextralife wiki host did not resolve from the sandbox and no blueprint dump was found online, so web evidence is weak.

## Claims vs code

| # | Claim | Verdict / evidence |
|---|---|---|
| 1a | Charge is full-round | Indirectly confirmed. The UI exempts charge, demon charge and overrun from "no full-round in surprise round" (`MechanicActionBarSlot.cs:105`). Seen in TB in [LOG]. The blueprint ActionType itself is not readable. |
| 1b | Charge needs ≥10 ft | RT: 10 ft plus both corpulences. TB: one 5-ft step + MinWeaponRange + corpulences (`AbilityCustomCharge.cs:249-257`). |
| 1c | Clear path, no units or obstacles | Only partly true. A navmesh trace from caster to target must be clear (:331). Other units are checked only at the end spot in front of the target, not along the whole line (:336-356). |
| 1d | 2× speed, straight line | Max range = `CombatSpeedMps*6` (:264-267). The path is a two-point ForcedPath, a straight line (:45). Speed is forced to 2× (:121 TB, :167 RT). In TB you cannot charge after moving this turn (`TBM.TimeMoved>0`, :357-363). Mounted charges are delegated to the mount (`AbilityData.cs:1173-1186`). |
| 1e | +2 attack / −2 AC | `SystemMechanics.ChargeBuff` (f36da144a379d534cad8e21667079066) is added for 1 round (:46). Its contents are [BP?]. On-charge components check `RuleAttackWithWeapon.IsCharge` (ChargeAttackBonus, AdditionalDiceOnAttack:383, AbstractWeaponTrigger:382). |
| 1f | In RT, charge waits for initiative | Not specific to charge. Every command waits for `CombatState.CanActInCombat` and a free cooldown (`UnitCommandController.ShouldStartCommand:240-244`, `UnitCombatState.cs:254-268`). |
| 1g | Pounce | `UnitAttack.InitAttacks` (`UnitAttack.cs:225-226`): a charge is a single attack unless the unit has `Features.Pounce`. A melee weapon is required (:36). |
| 2a-b | CdG: auto crit, Fort DC 10+damage or die | No vanilla CdG class. The blueprint (32280b137ca642c45be17e2d92898758) runs an AbilityEffectRunAction whose contents are unknown. TTT replaces it with `CoupDeGraceTTT` (`tttdec_core/.../CoupDeGraceTTT.cs:96-113`): AutoHit + AutoCrit + ForceSneak, then Fort DC damage+10, then MarkedForDeath. [LOG] shows CdG taking 6.0 s in TB (full round). |
| 2c | CdG provokes | Vanilla probably does not. TTT adds `ContextActionProvokeAttackOfOpportunity(ApplyToCaster)` as a fix (`tttdec_base/.../Bugfixes.Abilities/BaseAbilities.cs:33-39`). The generic provoke path only covers spells cast as Standard commands (`AbilityData.cs:662-672`, `UnitUseAbility.cs:735`). |
| 2d | Helpless predicate | `UnitState.IsHelpless` (`UnitState.cs:219-229`) is true when: not conscious; Sleeping; the Unconscious condition; or not animating, which means Paralyzed, Petrified, or cutscene DisableAnimation (:231-245). Stunned, dazed, prone, entangled, pinned and nauseated are **not** helpless. Stunned only makes the target flat-footed (`RuleCheckTargetFlatFooted.cs:29`). Helpless gives −4 melee AC (`RuleCalculateAC.cs:45`); `UnitStatusEffectHelpless` (:28-35) sets effective Dex to 0 and removes dodge from AC and CMD. |
| 3 | Treat Affliction | No code class. It is `LoreReligionUseAbility` 4843cb4c23951f54290c5149a4907f54, probably granted by `SkillAbilities` e4c33ff9…. The mod's profiler sees a dispel/remove-buff action in it [LOG] (`/home/user/CombatAIPWOTR/Tactics/TacticalEngine.cs:1746-1754`) and logged its buff profile as "NoComponents", which may be a per-day marker buff (inference). DC, action type and the limit are [BP?]. "Once per person per 24 h" is [WEB] only. |
| 4a | Demoralize DC 10+HD+Wis | The DC uses the target's **CharacterLevel** and Wisdom **bonus**: `10 + CharacterLevel + Wis bonus` (`Mechanics.Actions/Demoralize.cs:75`). |
| 4b | Shaken 1 round +1 per 5 over | Confirmed: `1 + (roll−DC)/5`, +1 with Frightening Thug (:99). |
| 4c | Repeating extends duration | Confirmed: `IncreaseDuration` on the existing buff (:108-116). |
| 4d | 30 ft, see/hear, no SR, fear immunity | None of this is in the action. It comes from the ability blueprint (PersuasionUseAbility 7d2233c3…, plus a Move variant 453af215…) and the buff's descriptors [BP?]. |
| 4e | Frightened upgrade | Frightening Thug with duration ≥4 adds GreaterBuff for 1 round. Hellknight Fearsomeness within 3 m gives GreaterBuff for the full duration (:100-121). Shatter Confidence and Trickster paralysis hooks are at :122-138. |
| 4f | Dazzling Display, Cornugon Smash, Dreadful Carnage | Dazzling Display uses `Demoralize{DazzlingDisplay=true}` and has four abilities: Action, Move, Standard, Swift (5f3126d4…, 08c10a39…, ba2951ff…, 3de922e9…). Cornugon Smash (ceea5355…) and Dreadful Carnage (fc37b70e…) are features whose triggers are [BP?]. They are presumably automatic rulebook triggers, not commands (inference). |
| 4g | Shatter Defenses | Vanilla: a Shaken or Frightened target is flat-footed against an attacker with `Features.ShatterDefenses`, with no "hit first" requirement (`RuleCheckTargetFlatFooted.cs:29`). TTT changes it to "after a hit". |
| 5a | Fighting Defensively +2 dodge AC (+3 with 3+ Mobility) | Confirmed. `FightingDefensivelyACBonusProperty.cs:29-46` gives 3 if Mobility **ranks** (BaseValue) ≥3, else 2, plus feature ranks. |
| 5b | −4 attack and CMB | The attack penalty is 4 minus feature ranks, halved with a HalfBuff (`FightingDefensivelyAttackPenaltyProperty.cs:85-106`). Whether it also hits CMB is [BP?]. |
| 5c | AC only after attacking | Not supported by code. The AC comes from FightDefensivelyBuff (6ffd9335…), which is applied while the toggle is *started*. TTT treats "has the buff" as "fighting defensively". Whether the buff starts immediately or on the first attack depends on the toggle's `ActivationType` (Immediately vs OnUnitAction=Attack, `ActivatableAbility.cs:603-611`), which is [BP?]. Read it at runtime. Crane Wing and Riposte are [BP?]. |
| 6a | Mobility toggle halves speed | Confirmed: ×0.5, or ×1.5 with Trickster (`UnitEntityData.cs:1961-1964`). The component is `AcrobaticMovement.cs:19-36`. |
| 6b | Mobility check to avoid AoO | Confirmed. The roll is Mobility vs the **attacker's** CMD, made once per AoO that would actually fire on disengage, never in simulation (`UnitCombatState.cs:640-668`). The vanilla toggle has DeactivateIfCombatEnded=true; TTT sets it false. |
| 6c | Two concentration checks for casting defensively | **Wrong.** A failed defensive cast (DC 15+2×level, Standard-action spells only; `RuleCheckCastingDefensively.cs:42`, `UnitUseAbility.cs:735-746`) provokes from every engaged enemy. Each of those AoOs can be negated by a Mobility vs CMD check, but only if the caster has an extra fact (`m_FactForDefensivelyAcrobatic`, which fact is [BP?]). |
| 7 | Inspire Rage "Accept Effect" | The toggle InspiredRageAllyToggleSwitch (180e06c0…, buff 9fd2dd27…) has no code references. How the song checks it and its IsOnByDefault are [BP?]; [WEB] says it is on by default. `m_IsOn` is saved. Spellcasting is blocked by the generic `ForbidSpellCasting` → SpellcastingForbidden (with an IgnoreFeature; `ForbidSpellCasting.cs:21-31`), enforced in `AbilityData.cs:791-796` for Spell/SpellLike abilities. Items are still allowed unless they need UMD or MagicItemsForbidden is set. |
| 8 | Power Attack / Deadly Aim / Piranha Strike | Attack penalty: `WeaponParametersAttackBonus` scales by 1+BAB/4 (:124). Damage: `WeaponParametersDamageBonus` uses 1+BAB/4 and `PowerAttackScaling` (:137-160): secondary attacks ½; two-handed grip ×1.5, or ×2 with Greater Power Attack, or ×5/3 with the mythic fact. While the buff is on it applies to every attack, including AoOs. A `PowerAttack` group exists; membership and base values are [BP?]. `PowerAttackWatcher.cs:28-63` is the native AI's expected-value rule for toggling PA, used for non-party units only. |
| 9 | Cleave / Great Cleave / Cleaving Finish | Cleave is an ability (`AbilityCustomCleave.cs:28-76`). It attacks the primary target, then every living enemy in reach sorted by distance to the primary, stopping at the first miss. Cleave caps at 2 hits; Great Cleave has no cap. There is no "adjacent to previous" rule and no −2 AC in code. Its action type is [BP?]. Cleaving Finish is an automatic on-kill feature with a once-per-round cooldown buff (d8685468… is vanilla). TTT rebuilds it, so the exact vanilla components are unknown. |
| 10 | Rage / Bloodrage / Raging Song | Action types, resources, fatigue and which rage buff carries ForbidSpellCasting vs ForbidSpellbook/IgnoreFeature are all [BP?]. The code mechanics are in item 13. Raging Song is in the bardic group, so its command steps down Standard → Move → Swift with Quicken Performance / Singing Steel (`UnitActivateAbility.cs:36-59`). Lingering Performance keeps the buff 2 more rounds. |
| 11 | Smite / Mark of Justice / Lay on Hands | [LOG]: Smite (0 s) then Charge in the same TB turn, so Smite is swift or free. Logged Smite buff components: AttackBonusAgainstTarget, DamageBonusAgainstTarget, IgnoreTargetDR, AddFactContextActions, RemoveBuffOnLoad. Evil-only targeting, Mark of Justice and Lay on Hands action types are [BP?]. |
| 12 | Metamagic | `AbilityData.MetamagicData` (:149). Metamagic variants are spellbook custom spells built with `MetamagicBuilder` (`Spellbook.cs:1226-1290`). Quicken makes a spell swift, falling back to Standard if swift is spent. Spontaneous (non-Arcanist) casters cast any metamagic spell as full-round (`AbilityData.cs:946-975`, `:2193+`). Rods are activatables in the MetamagicRod group with `MetamagicRodMechanics`: they apply to the next suitable spell while on, then spend a charge. Auto metamagic is `AutoMetamagic`. A command can cast any AbilityData carrying MetamagicData, such as an existing custom spell. |
| 14 | Combat maneuvers | `ContextActionCombatManeuver` → `RuleCombatManeuver`: natural 1 fails, natural 20 succeeds, otherwise d20+CMB ≥ CMD; immunity or a concealment miss cancels. Duration is margin/5+1 rounds. Sunder only applies a SunderArmor AC debuff. Bull rush pushes 5 ft + margin. Maneuvers never provoke in code. `ManeuverProvokeAttack` (the Greater feats) makes the **target** provoke on success. Action types are [BP?]. |

## 13. How ActivatableAbility works [CODE]
Files: `UnitLogic.ActivatableAbilities/*`, `UnitLogic.Commands/UnitActivateAbility.cs`, `Controllers.Units/UnitActivatableAbilitiesController.cs`.

- **Two flags.** `IsOn` (serialized, :87-88) is the player's switch. `IsStarted` (serialized, :114) means the effect is live and the buff is applied (not dispellable, :565-601). There is no `IsRuntimeOn`; `IsRuntimeOnly` is only a UI flag.
- **What turning on does.** The UI just flips `IsOn`. `OnDidTurnOn` (:737-812) then runs, in this order:
  - **Group exclusivity:** the turn-on weights in the group are summed and the oldest are switched off while the sum exceeds the group size (1 + increases).
  - **Immediately** abilities call `TryStart` at once.
  - **WithUnitCommand** abilities run `new UnitActivateAbility(this)`. Its command type comes from `GetCommandType` (:34-72): the blueprint type, a TB override via `AbilityActivateWithUnitCommandInTurnBased`, bardic quickening, a free Judgment multi-activation, and modifiers. The command succeeds instantly; `TryStart` follows on act (:425-458), and a failed or replaced command sets IsOn false.
  - **OnUnitAction** abilities start on the next `RuleAttackWithWeapon` or `RuleCastSpell` (:603-657).
- **TryStart (:481-526) fails** when:
  - the ability is unavailable: resources, `OnlyInCombat`, restriction components or polymorph (:171-247);
  - it starts with combat (`DeactivateIfCombatEnded`+Immediately) and the unit is not in combat; it then waits as `ReadyToStart` and starts when combat begins (:401-407);
  - its per-round command is on cooldown;
  - the group is over capacity.
- **Round ticks** (controller :56-74): every 6 s in RT; in TB only while the unit is the current unit. `OnNewRound` (:379-399) stops the ability if it is off, unavailable, `DeactivateAfterFirstRound`, or combat has ended with `DeactivateIfCombatEnded`. Otherwise it spends resources and spends the `ActivatableAbilityUnitCommand` action.
- **Resources** (`ActivatableAbilityResourceLogic.cs:28-240`): spend types TurnOn/Start (spent at start), NewRound, Attack/Hit/Crit/SpecificWeapon, Never (manual), OncePerMinute/TenMinutes/Hour (round counters). Cost is 1 ± facts. A free fact skips the cost. Running short calls `TurnOffImmediately`.
- **Turning off** (:823-842):
  - `DeactivateImmediately` stops it at once.
  - In TB, `DeactivateImmediatelyIfNoAttacksThisRound` refunds the swift or standard action if no attack was made this round.
  - Otherwise it runs until the next tick. `Stop()` (:528-563) removes the buff, or after the rest of the round for command-type toggles.
  - `TurnOffImmediatelyWithUnitCommand` makes switching off cost that action.
- **Other stops:**
  - Owner disabled or unconscious, with the matching flags.
  - Rest, unless `DoNotTurnOffOnRest` (:82-95).
  - `StopOutOfCombat` (:97-103).
- **Saves.** IsOn, IsStarted, the applied buff, TimeToNextRound and ReadyToStart are saved. `OnPostLoad` resets IsOn for inactive WithUnitCommand toggles. Immediately-type toggles stay on across fights.
- **AI advice.** Set `IsOn = true`, as the UI does, and check `IsAvailable` and `UnitActivateAbility.GetCommandType(a)` against the action economy first.

## Other AI-relevant facts
- Only spells provoke on cast, and only when cast as a Standard command while engaged, not from wands or sticky touch, and only when the automatic defensive check fails (`UnitUseAbility.cs:735-746`).
- Ranged attacks, unarmed attacks without Improved Unarmed Strike, and standing up also provoke (`UnitCombatEngagementController.cs:124-171`).
- In TB, disengaging always provokes unless the move is a 5-foot step (`ImmuneAttackOfOpportunityOnDisengage`) or the unit has total concealment (`UnitCombatState.cs:676-720`).

## Still unknown (blueprint data; dump at runtime with `ResourcesLibrary.TryGetBlueprint`)
- Charge: ChargeBuff contents.
- ActionType of Charge, Coup de Grace, Smite, Cleave, the maneuvers, Rage, Lay on Hands, Treat Affliction and Mark of Justice.
- Vanilla Coup de Grace actions and target restriction.
- Treat Affliction: DC, which afflictions it removes, and the marker buff.
- Demoralize: range, SR and immunity settings.
- Triggers of Cornugon Smash and Dreadful Carnage.
- Fight Defensively: ActivationType and whether the penalty applies to CMB.
- Inspire Rage accept toggle: default state and how the song checks it.
- PA/DA/Piranha: group membership and base values.
- Which rage buff forbids which spellbook.
- The fact that enables the Mobility-after-failed-cast check.

Web sources (low weight): https://steamcommunity.com/app/1184370/discussions/0/5086242673972554335 and https://steamcommunity.com/app/1184370/discussions/0/4526764179303472027
## 3. Content from other mods


**Biggest risks across all four mods**
- **Toggles are never handled.** Neither engine finds or switches activatable abilities [C].
- **Kill, resurrect and custom damage actions are invisible.** `ContextActionKill`, `ContextActionResurrect` and custom damage actions (TTT `ContextActionDealDamageTTT`, MM `ContextActionCorruptingRayDamage`) are not recognised [C].
- **The "Enchant" name rule misfires.** Any action whose type name contains "Enchant" counts as an enchantment buff. That catches TTT Mage's Disjunction and MM Enchant Item, and the enchant fallback is checked before the dispel fallback [C].
- **Non-combat abilities can get the "utility" fallback** (value 1.0, once per combat). Examples: Isekai Planar Coliseum Return (given to the party inside the arena), Open Cosmic Store (free action), Summon Rift Boss [I].
- **TTT action-type conversions are dropped.** Discovery keeps only variants, so Quick Channel, Quicken Blessing, Meta-Rage and Quick Study are never found [C].
- **TTT toggle-like abilities may be switched off.** `AbilityEffectToggleBuff` abilities (Warrior Spirit, Black Blade attunements) show no effect, fall to the free-action utility fallback, and casting one can remove the buff [C structure, I outcome].

---

## 0. What the AI understands now (from its own code)

**Discovery (`AbilityDiscovery.Gather`)**
- It reads `unit.Abilities` (non-hidden) plus each spellbook's known, metamagic and special spells.
- Variant parents are replaced by their visible variants: `GetConversions()` filtered by `HasVariant`. Conversions that are not variants are dropped [C].
- The only use of `unit.ActivatableAbilities` is the kinetic-blade lookup, so no toggle is ever discovered or switched [C].
- Filters before scoring:
  - `AbilityData.IsAvailable`. In the game this covers resources (`GetAvailableForCastCount`), `IAbilityCasterRestriction`, and `IAbilityRestriction` inside `CanBeCastByCaster` (game AbilityData.cs:833) [C].
  - `IsBlacklistedForCombat`: variant parents, Demoralize actions, fatigue-only touch spells [C].
- Action cost comes from the game getters `RequireFullRoundAction` and `RuntimeActionType`; the latter reads `ActionType`. Patches on those getters by other mods therefore apply [C].

**Effect model (`AbilityProfile`)**
- It walks:
  - only the first `AbilityEffectRunAction`;
  - sticky touch, touch and projectile delivery (attack roll);
  - `AbilityTargetsAround`;
  - spawned areas: `AbilityAreaEffectRunAction` enter/round/move lists and `AbilityAreaEffectBuff`;
  - child lists of any action, found by reflection over `ActionList` / `GameAction` fields;
  - any `BlueprintAbilityReference` field, so `ContextActionCastSpell` is followed.
- Recognised (subclasses match too): DealDamage, HealTarget, ApplyBuff, SpawnMonster, SpawnAreaEffect, DispelMagic, EnchantWornItem, RemoveBuff, RemoveBuffsByDescriptor, SavingThrow, ConditionalSaved, Conditional, HealBurn, OnContextCaster and other subject changers [C].
- Name rule: a type name containing "Enchant" or "EnhanceWeapon" sets `Enchants=true` [C].
- Not understood:
  - actions: Kill, Resurrect, CombatManeuver, MeleeAttack, RestoreResource / RestoreAllSpellSlots;
  - custom ability logic: any `AbilityCustomLogic` (only Coup de Grace by GUID and Charge are special-cased) and any `AbilityApplyEffect`;
  - components: `AbilityExecuteActionOnCast`, `*OnClickedTarget`, the extra targets of `AbilityDeliverChain`;
  - buffs referenced through custom `BlueprintBuffReference` fields [C].
- Conditional guards are evaluated per target only for a whitelist of vanilla conditions. A custom condition makes the branch count for everyone [C].

**Buff worth (`BuffEffectAnalyzer`)**
- It matches components by exact type name against about 70 vanilla names. Unknown components give 0.5 for renewable abilities and 0 for items [C].
- Not valued: AddFacts, Polymorph, BuffExtraAttack, attack triggers, AutoMetamagic, IncreaseSpellDC, ChangeUnitSize [C].

**Scoring**
- Debuffs on enemies: hard control 1.0, soft 0.4, anything else 0.25 with no look at what it does [C].
- When an ability scores 0 and its effect cannot be checked, the fallback order is: summon, then enchant, then (items get nothing) dispel, then utility 1.0 once per combat [C].
- A point-only ability that is neither harmful nor damaging gets no target candidates, so it is never cast [C structure, I effect].

**Game rules the AI calls, so other mods' patches apply** [C]
- `RuleCalculateAttackBonus`, `RuleCalculateAC`, `RuleCalculateWeaponStats`
- `CalculateParams`, `RuleCheckCastingDefensively`
- `ModifierDescriptorHelper.IsStackable`
- `CanTarget`, `GetApproachDistance`, `HasMetamagic`
- `UnitPartDamageReduction`
- Empower/Maximize and dice math are done by the AI itself in `CombatMath`, not through `RuleCalculateDamage`.

---

## 1. Isekai Mod

**1.1 Inventory**
- The blueprint config holds 4097 new blueprints: 292 named `*Ability` and 69 resources [C].
- Code: 124 direct ability constructions plus loops (26 summon-helper call sites, 18 copies) [C].
- Toggles: 14 direct, plus 31 + 22 + 13 toggle-helper call sites [C].
- Rough total: about 200 active abilities and about 80 toggles [I].
- Families of active abilities:
  - **Overpowered Abilities:** Instakill, World Break, Super Buff and Dismiss, Unlimited Power, True Resurrection, Mind Control, Summon Calamity, Chrono Domain, Chrono-Surge, Absolute Defense, Singularity Vortex, Merchant's Gamble, Predator Maw.
  - **Archetypes:**
    - Overlord: Grasp Heart, Fallen Down, Ia Shub-Niggurath, Create Death Knight, Skeletal Form toggle-ability.
    - Devourer: Devour Prey, Devour Magic, Beelzebub, Predator Arts, Slime Form toggle-ability.
    - Shadow Monarch: Extraction, Ruler's Authority, Step, Exchange, Cloak.
    - God Emperor, Hero, Mastermind, Phantom Blade, Martial God.
    - Prestige: Plot Twist, Reality Rewrite, Anime Final Form.
  - **Heritages:** Vampiric Mist, Blood Drain Bite, Drow Poison, Succubus Charm, Earthquake Stomp, Angelic Bolt, Abyssal Hellfire, Howl of Fenrir, Dragon Lord Breath.
  - **Special powers:** Isekai Channel Positive/Negative (vanilla channel components copied); signature cantrips.
  - **Summons:** about 20.
  - **Consumables:** about 45 `Item*` / `Wish*`.
  - **Non-combat facts:** Open Cosmic Store, Wish Wealth, Codex of Reincarnation, Planar Coliseum Return, Summon Rift Boss / Summon Barren Boss Act 1-5.
- Toggles:
  - auto-metamagic (Empower, Maximize, Extend, Bolster…);
  - auras (Dark, Divine, Corrupt, Killing Intent, Sovereign's Dread, Domain Expansion…);
  - self buffs (Excalibur, Extreme Speed, Reflect, Unreactable, Spell Negation…);
  - Wings; Spriggan Size Alteration.

**1.2 Custom components** — mostly vanilla. 91 AbilityEffectRunAction, 44 ApplyBuff, 25 DealDamage, 9 SpawnMonster, all recognised [C].

| Component | Base | AI view |
|---|---|---|
| `ContextActionOnNearbyPoint` (holds an ActionList) | ContextAction | Understood: the summon inside is found by reflection [C] |
| `ContextActionChronoSurge` (resets Standard/Move/Swift cooldowns) | ContextAction | Utility [C]; the mid-turn action refund is not modelled [I] |
| `ContextActionRestoreAllSpellSlots` (Unlimited Power) | vanilla | Utility [C] |
| `CustomRollLogic` (Merchant's Gamble) | ContextAction | Utility [C] |
| Store, wish, return, boss and skill-book actions | ContextAction | Utility fallback [C], so possible mid-fight casts [I] |
| `ContextConditionImmuneToDeathEffect`, `ContextConditionCasterLevel`, HP-percent and is-living conditions | ContextCondition | Not whitelisted, so branches count for everyone [C] |
| `AbilityTargetHPBelowThresholdScaling` | `IAbilityTargetRestriction` | Respected through `CanTarget` [C] |
| `AbilityCasterFeatureBelowRank` | `IAbilityCasterRestriction` | Respected through `IsAvailable` [C] |
| Teleports (vanilla `AbilityCustomDimensionDoor`): Shadow Step, Exchange, Blink | AbilityCustomLogic | No effect seen [C] |
| Vanilla Kill / Resurrect / `AbilityExecuteActionOnCast` | — | Unrecognised [C] |

On buffs:
- Custom `AddStackingStatBonus` (33 uses) and `AddContextStackingStatBonus` (7), both `UnitFactComponentDelegate`, are unrecognised [C].
- Vanilla BuffExtraAttack (25), attack triggers (14), IncreaseAllSpellsDC (24) and AutoMetamagic (16) are unrecognised too [C].

**1.3 Resources**
- Vanilla `AbilityResourceLogic` and `AddAbilityResources` everywhere, so `IsAvailable` is correct [C].
- The Cosmic Wish patch on `UnitAbilityResourceCollection.Restore` (no restore on rest) is still read correctly [C].
- Helper toggles are on by default, free, and activate immediately [C]. Exceptions:
  - Spriggan Size Alteration: needs a Standard action, spends a resource each round, switches off after combat. It is never re-activated [C].
  - Domain Expansion and the Slime / Skeletal forms are off by default [C].
- The patch on spontaneous metamagic action time reads through `RequireFullRoundAction` the same way the AI does [C].

**1.4 What breaks or misleads**
- Instakill:
  - The AI sees 1d6 per 2 levels of damage on the death-immune branch (counted for everyone) plus Stunned on the successful-save branch.
  - The kill on a failed save is invisible [C].
- Devour Prey, Beelzebub, Ia Shub-Niggurath: their kills are invisible [C].
- True Resurrection and Reality Rewrite are never used, because dead allies are not targets [I].
- Chrono-Surge and Unlimited Power are valued at utility 1.0 [C].
- Super Buff:
  - Its 63 vanilla buffs sit behind custom caster-level conditions, so all tiers are counted at any level [C].
  - Dismiss is correctly valued 0 [C].
- The form toggle-abilities register as toggles; real time already refuses to flip them out of combat, but turn-based combat may still flip them [C].
- Auto-metamagic toggles are correctly picked up through `HasMetamagic` [C].

---

## 2. Tome of the Firebird 1.4.18

**2.1 Inventory**
- Only content wired in `BuildContent.cs` counts.
- Spells (about 22 blueprints) [C]:
  - Chains of Fire, Bone Fists, Telekinetic Strikes (with its Cast companion), Spear of Purity, Heal Mount (with Cast);
  - Keen Edge (with variants), Freezing Sphere (with variants), Gloomblind Bolts;
  - Fire Shield Warm/Cold, Vitriolic Mist, Entropic Shield, Burst of Radiance, Fly / Fly touch / Mass Fly.
- Other abilities: Shimmering Mirage wild talent; Vanguard share abilities (TeamworkFeatFactory); changed Lay on Hands mercies (Ensorcelled adds a dispel, Injured adds fast healing) [C].
- Toggles:
  - Elemental Stance ×4 (barbarian stance, on by default); Sundering Strike; Kineticist Internal Buffer (resource never spent, plus a resource restriction).
  - Double Metakinesis ×2, Draconic Claws T1–T4, Tiefling claws, adopted-pet toggles.
  - Abyssal claws ×4 changed to free and unlimited.
- No call site found for: Fiery Runes, Incendiary Cloud, Earth Tremor, all `Spells/TODO`, most kinetic feats and wild talents [I].

**2.2 Components**
- Active spells use vanilla actions.
- Custom buff parts are unrecognised and get 0.5 [C]: `ContextWeaponCategoryFlatDamageBonus`, `ContextWeaponCategoryExtraDamageDice`, `ElementalStanceComponent`, and vanilla `AddTargetAttackRollTrigger` in Fire Shield.
- `AbilityDeliverChain` (Chains of Fire): only the primary target is valued [C].
- Twin Spell / Double Blast (extra casts on `RuleCastSpell`) are invisible [I].

**2.3 Rule changes**
- `AddIFFToDispels` sets "enemy buffs only" on the Dispel family; this fits the AI's `HasSomethingToDispel` [C].
- Good Hope is changed to helpful toward allies [C].

---

## 3. Mystical Mayhem 0.7.0h

**Inventory** — content is JSON blueprints plus C#. KRXGenerator only generates GUID repositories, and the Unity assets are icons [C].
- 60 abilities, 5 toggles, 77 buffs, 17 resources [C].
- Spells: Bloody Tears, Borrowed Time, Draconic Malice, Enchant Item, Heart of the Mammoth, Mighty Strength, Paranoia, Recharge Item, Spell Turning, Sure Casting; deprecated Meteor Swarm.
- Warlock: Corrupting Ray, Eternal Torment, Babble, capstones, Daemonic Fury, Tasty Morsel.
- Archetypes and classes:
  - Draconic Warlock: 15 dragon-form variants, Servants, Dungeon Master.
  - Mortified Casting (with variants), Arcane Smite Evil, Invoker Eldritch Blast / Eldritch Chain.
  - Cyphermage scroll abilities, Circle of Order, Spell Synthesis, Theurgy (hidden), Trickster Booze, Elixir of Life.
- Toggles: Eternal Potion, Pact Wizard Reroll, Razmiran Channel, Life Tap, Tormenting.

**Custom components**

| Component | Base | AI view |
|---|---|---|
| `ContextActionCorruptingRayDamage` (deals damage by triggering its own `RuleDealDamage`) | ContextAction | No damage seen, so the warlock's main attack is under-used [C] |
| `ContextActionDrainHP` | ContextAction | Invisible [C] |
| `MaliceContextAction` | subclass of `ContextActionDealDamage` | Counted as damage even against targets without the caster's hexes [C/I] |
| `ContextActionEnchantRandomItem` (Enchant Item) | ContextAction | Name rule turns it into the enchant fallback, a full-round cast in combat [C rule / I] |
| `ContextActionRechargeItem`, `ContextActionIncreaseTormentingDuration` | ContextAction | Utility [C] |
| `AbilityApplyTheurgyBuff` (Theurgy, hidden) | AbilityApplyEffect | Not discovered [C] |
| Paranoia: vanilla `ChangeFaction` + `ParanoiaPenalties` | — | Generic 0.25 debuff [C] |

- Buff checks on the JSON: Mighty Strength and Mammoth Heart are recognised. Spell Turning, Sure Casting, Bloody Tears, Borrowed Time, Mortified Casting, Circle of Order, Daemonic Fury and the capstones are not [C].
- Meteor Swarm uses `AdditionalAbilityEffectRunActionOnClickedTarget`, which is not walked [C].

**Patches the AI respects** [C]
- `AbilityData.get_ActionType`: Spell Synthesis makes the second book free; Invoker blasts become move actions.
- `CanBeCastByCaster`, `RequireFullRoundAction`.
- Half-step `GetMaxAmount`; Tasty Morsel is not restored on rest.

---

## 4. Tabletop Tweaks Core / Base

**4.1 New content** [C]
- 1427 new blueprints; 118 named `*Ability`; 126 ability and 19 activatable constructions in code.
- Spells: Accursed Glare, Age Resistance, Cloak of Winds, Inflict Pain, Long Arms, Mage's Disjunction, Sands of Time, Shadow Enchantment, Spell Curse, Stunning Barrier Greater, Web Bolt, new hexes.
- Abilities: Stunning Fist variants, template smites, Black Blade abilities, 26 Arcane Metamastery abilities, mythic abilities.
- Toggles: 30 new metamagic rods, Split Hex, Stalwart, Lunging Spell Touch, Toppling Bash, Focused Weapon, One-Handed, Mantis Style, Energy Conversion; pseudo-toggles.
- New metamagics: Burning, Elemental, Encouraging, Flaring, Intensified, Piercing, Rime, Solid Shadows.

**4.2 Components in active abilities** [C]
- `ContextActionDealDamageTTT` is a plain ContextAction, not a DealDamage subclass. Damage is invisible (Oracle, Lich, mythic fixes).
- `ContextActionApplyBuffRanks` (Stunning Barrier Greater): its buff is invisible.
- Mage's Disjunction combines `ContextActionDisjointEnchantments` with Kill and DispelMagic. The name rule flags it as an enchantment, and the enchant fallback comes before dispel.
- Cleave now uses `AbilityCustomCleaveTTT` (custom logic); it is utility either way.
- `CoupDeGraceTTT` is handled by the AI's GUID check.
- `AbilityEffectToggleBuff` + `PseudoActivatable` (Warrior Spirit, Black Blade attunements): utility, and casting may toggle the buff off.
- Conversions that are not variants (`AbilityActionTypeConversion` for Quick Channel / Quicken Blessing, Meta-Rage, Quick Study, Greater Spell Specialization) are never discovered.
- Restrictions (`AbilityRequirementHasBuff`, `HasResource`) are respected.
- Buff components with renamed versions (`AttackBonusAgainstTargetTTT`, `AddOutgoingWeaponDamageBonus`, `BonusDamagePerDie`, `ForceFlatFooted`) are unrecognised.
- `BuffEnchantAnyWeaponFix` and `DRRework` swap components only in `CollectComponents` at runtime. The blueprints the analyzer reads still show the vanilla names, so recognition is unaffected.

**4.3 Patched rules the AI reads** [C]
- Respected: `IsStackable` / `ModifierDescriptorComparer`, `CanTarget`, `GetApproachDistance` (melee touch reach), `CheckSelective` (already handled by `SelectiveRule`).
- `GetConversions`: patched, but the AI uses it only for variants.
- `RuleCalculateDamage` postfix: Empower adds half of the *unmaximized* roll. The AI applies 1.5× to the maximized total, so it overestimates Empower+Maximize [C/I].
- Partly mirrored by the AI's own estimates [I]: the ContextRankConfig change (Intensified), the saving-throw, dispel, SR and concealment patches, `RuleAttackRoll`.
- Not patched: casting defensively, concentration, attacks of opportunity, attack count.
- `ActionBarVM.CollectAbilities` is replaced: it hides cantrip facts and shows pseudo-toggles. Discovery mirrors only the vanilla rule.
- Demoralize abilities remain blacklisted.

---

## 5. Test cases for a coverage audit

| # | Mod | Blueprint (GUID) | What to check |
|---|---|---|---|
| 1 | Isekai | InstakillAbility (55e06f68-8f56-4d4b-824f-f6bbaa46899d) | The kill is missed; only damage and stun are seen |
| 2 | Isekai | DevourPreyAbility (f7123ea3-e0fd-2ded-3859-9b26dda4cf5d) | The kill is missed |
| 3 | Isekai | ChronoSurgeAbility (192c300c-21de-438c-1fe5-bbde6be123bd) | Utility only; refunds actions mid-turn |
| 4 | Isekai | UnlimitedPowerAbility (297c0daf-9cb3-49e8-aa50-9b4c711468c3) | Utility; may be wasted |
| 5 | Isekai | SuperBuffAbility (a75ce3b1-221e-4a4c-bc8f-0c5235938705) and Dismiss (731cd00a-7dee-4256-8209-7b4ecac0aa66) | Unknown-condition tiers counted at any level; Dismiss must stay at 0 |
| 6 | Isekai | TrueResurrectionAbility (25c39cdd-c81f-43cc-8d14-c52c2adb3858) | Resurrect not understood |
| 7 | Isekai | PlanarColiseumReturnAbility / OpenCosmicStoreAbility | Non-combat utility cast in a fight |
| 8 | Isekai | SkeletalOverlordFormToggleAbility (658eaa79-f6df-f8d1-42b9-e7b12491d1b6) | Toggle flipped in combat |
| 9 | Isekai | ShadowExtractionAbility (964949c0-343f-f632-c399-47311f637881) | Summon through a wrapper action |
| 10 | Isekai | ShadowStepAbility (7d17e5a4-4075-d8b9-1dbe-d87e9dd96a7c) | Teleport custom logic |
| 11 | Isekai | AutoEmpowerAbility (1291d796-c847-4f6e-a674-aaa8c472da6c) | Auto metamagic included in damage estimates |
| 12 | Isekai | SizeAlterationAbility (1cfcc60f-ce65-406e-8325-2d1b91fc8707) | Resource toggle never activated |
| 13 | Firebird | ChainsOfFire (d982739b-77e4-44c3-96ad-b063de013255) | Chain targets ignored |
| 14 | Firebird | TelekineticStrikesCast (9c2f011a-4c66-4dcc-b73a-1426fb55050e) | Custom weapon-dice buff |
| 15 | Firebird | FireShieldWarm (54ab6ea1-a66d-45e3-adb5-a9afaa723b43) | Retaliation buff unrecognised |
| 16 | Firebird | KeenEdgePrimary (02a7c19b-b775-44cf-8a4b-3c6522f1c0c6) | Enchant path and "already active" check |
| 17 | MM | WarlockCorruptingRayAbility (c312b0a1-f5ef-4269-ac29-dee4da8edc70) | Custom damage invisible |
| 18 | MM | EnchantItem (735bd679-3881-472c-97d2-4d5aac8b2206) | Name rule triggers the enchant fallback |
| 19 | MM | Paranoia (02e96ca3-a020-4652-b6b5-2bae063dadaa) | ChangeFaction debuff valued as generic |
| 20 | MM | InvokerEldritchBlastAbility (6ccc0707-0c2b-4652-b789-705b7d9be969) | Action type patched to Move |
| 21 | TTT | MagesDisjunctionAbility (85f616bb-9d05-4f66-9f35-7636c6c06338) | Treated as an enchantment |
| 22 | TTT | WarriorSpiritToggleAbility (55929e35-04f8-4f50-95dd-60ad0fbb1c29) / BlackBladeEnergyAttunementFireAbility (0bb928b7-5a8f-428c-9fad-ce4e936d7c76) | Toggle-buff ability |
| 23 | TTT | StunningBarrierGreaterAbility (9e33ed47-7ddd-4fad-9d90-fb64b1a6f732) | Buff-ranks action invisible |
| 24 | TTT | Cleave (vanilla 6447d104a2222c14d9c9b8a36e4eb242) | Custom cleave logic, utility only |
| 25 | TTT | Quick Channel conversion | Conversion not discovered |
| 26 | TTT | CloakOfWindsAbility (6fb10fff-27b8-4bff-8174-6194bf8e38a8) | `*TTT` buff component unrecognised |

---

## 6. Suggested fixes [I]
- Value actions implementing `IDealDamageProvider` as damage, using their `GetDamageRule` for the estimate.
- Add `ContextActionKill` (worth the target's HP and threat, weighted by the save chance) and `ContextActionResurrect`.
- Narrow the "Enchant" name rule, or check dispel before enchant.
- Keep Personal abilities whose actions are all non-combat custom actions out of the utility fallback.
- Treat `AbilityEffectToggleBuff` as a toggle and read its buff.
- List conversions that are not variants.
- Follow `BlueprintBuffReference` fields on unknown actions.
- Map `*TTT` and Isekai `*StackingStatBonus` component names to their vanilla equivalents.
- Use the TTT empower rule when both Empower and Maximize apply.
- Decide whether the AI should manage toggles at all.

What already works: wrapper actions that hold an ActionList, spells built from vanilla components, and every action-type, availability, targeting and stacking patch that goes through game getters, because the AI calls those getters.