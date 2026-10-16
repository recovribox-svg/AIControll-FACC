# Investigation log - CompanionAI_Pathfinder

- **v0.3.1** (fixes from the first 0.3.0 play logs): evidence and causes in [TACTICAL_AI.md](TACTICAL_AI.md) §6. Game
  facts used: `Conditional {ConditionsChecker, IfTrue, IfFalse}` and `ContextCondition` reading
  `ContextData<MechanicsContext.Data>.Current` (evaluated via `MechanicsContext.GetDataScope`); `RemoveBuffOnAttack`;
  `ContextActionRemoveBuffsByDescriptor` matching `buff.MaybeContext.SpellDescriptor`; `UnitCommand.IsRunning` is
  false while a command is still approaching, so movement is tracked as progress separately.
- **v0.3.0** (decision making, weapon attacks, consumables, mount paths): see [TACTICAL_AI.md](TACTICAL_AI.md) for the
  design. The evidence came from a v0.2.119 play-session log (summary table in TACTICAL_AI.md §1), and the root causes
  were confirmed in the source and in Assembly-CSharp:
  - **No weapon attacks in turn-based mode.** `TurnPlanBuilder.FindBestAttack` used only `situation.AvailableAttacks`,
    and `SituationAnalyzer.AddWeaponAttacks` only adds *abilities* with `Range == Weapon`. The real-time engine did
    consider `BasicAttack`, which is why Lann shot in real time but retreated in turn-based combat.
  - **Self-invalidating plans.** `TurnPlanValidator` replanned on "Position changed", caused by the unit's own planned move.
  - **Mount commands.** `SaddledUnitController.TickDelegateRiderToMount` builds the mount's `UnitMoveTo`/`UnitAttack`
    without `ForcedPath`, unlike `TacticalCombatRider`, which copies it. In turn-based mode it then follows the cursor
    prediction, exactly as in §1 below.
  - **Full vs single attack** for the directly-controllable current unit comes from the UI smart-cursor mode
    (`UnitCombatState.IsFullAttackRestrictedBecauseOfMoveAction` → `TurnController.GetEnabledFullAttack` →
    `m_AttackMode`). The engine sets `UnitAttack.ForceFullAttack` / `IsSingleAttack` explicitly, as
    `AbilityDeliverAttackWithWeapon` and `MagusController` do.
  - **Variant spells.** `AbilityData.Cast` refuses abilities with variants, so the engine expands them with `GetConversions()`.
  - **Consumables in combat.** Only quick-slot items: `ItemEntity.OnDidEquipped` adds an ability with `SourceItem`, and
    `IsUsableFromInventory` is false while `Player.IsInCombat`.

# Movement investigation - CompanionAI_Pathfinder 0.2.118 → 0.2.119

Sources examined: the supplied 0.2.118 source archive, the shipped 0.2.117/0.2.118 DLL package (decompiled), the
supplied `Assembly-CSharp.dll` (decompiled with ILSpy 8.2) and `UnityModManager.dll` (0.33.0). No runtime log was
supplied, so the `Commands.Empty=True` observation from the handover brief is explained from code, not from a log.
Line numbers refer to the ILSpy output of the supplied Assembly-CSharp.

The later experimental builds described in the brief (prediction suppression, 0.2.123 pointer patches) were **not**
in the supplied material. The 0.2.118 source contains attempt 3 (forcing `TurnController.Status = Acting`) and
uses the `UnitMoveTo(Vector3)` constructor (attempt 2), but does not patch `UpdateActionPredictions`.

## 1. Root cause - turn-based movement (symptoms A and B)

They are the same bug.

**Pipeline of one `UnitMoveTo` issued by the mod in turn-based combat:**

| Step | Game code | Result for the mod's command |
|------|-----------|------------------------------|
| Submit | `UnitCommands.Run` (UnitCommands.cs:294) | `Init`, put in the Move slot, `OnRun` → `StartInTbm` adds it to `TurnController.m_RunningCommands`. "Command registered" is true at this point. |
| Turn state | `TurnController.Tick` (TurnController.cs:980-990) | `Preparing` → `Acting` on its own as soon as the rider's command slots are non-empty. |
| Tick gate | `UnitCommandController.TickCommandTurnBased` | Ticks only while `IsActing || IsEnding`. |
| Approach | `UnitCommand.TickApproaching` (UnitCommand.cs:728-753) | In TB, for an in-combat unit: `path = ForcedPath ?? PathVisualizer.Instance.CurrentPathForUnit(view)`. If null or empty, the command is **interrupted** ("forcedPath was null or empty"). Otherwise `AgentASP.FollowPrecomputedPath(path)`. |
| `CurrentPathForUnit` | PathVisualizer.cs:223 | Returns the visualizer's `m_CurrentPath` for the current unit. |
| Prediction | `TurnController.UpdateActionPredictions` (TurnController.cs:2204-2212) | For a directly-controllable unit over terrain: `ApproachPoint = DefaultPointerController.WorldPosition` → `PathVisualizer.CalculatePathForCommand`. So `m_CurrentPath` is **the path to the mouse cursor**. |

The mod never set `ForcedPath`, so its commands:
- walked along whatever the cursor-hover prediction path was (**symptom A**), or
- were interrupted on their first approach tick when there was no prediction path. The slot then empties, which is
  the `Commands.Empty=True` seen right after "Command registered" (**symptom B**).

**How native AI does it:** `AiBrainController.SelectAction` (AiBrainController.cs:186-190) sets
`unitCommand.ForcedPath = context.BestPath`. That path is computed in `AiAction.ScorePath` (AiAction.cs:255-409) by
`UnitMovementAgent.FindPath(destination, cb, approachRadius, needLOS)` + `AstarPath.BlockUntilCalculated`, then
`FixPathIfTooShort` and ally-blocked-endpoint trimming.

**The earlier attempts, explained:**
1. *Suppress `UpdateActionPredictions`* - this removes the cursor path, so the hover effect goes away. But
   `TickApproaching` then finds no path and interrupts every command, so movement breaks completely.
2. *Change the `UnitMoveTo` constructor* - irrelevant, because the path source is the problem. (The 1-argument
   constructor is the right one. Its default `ApproachRadius` is 0.3 m, while `(dest, 0f)` can never satisfy
   `IsUnitCloseEnough`.)
3. *Force `Status = Acting`* - unnecessary, because the game does this itself (row "Turn state"). It was also harmful:
   it ran in `Ending` and `Delayed` too.
5. *Neutralise pointer reads* - the prediction path still feeds `TickApproaching`, so commands still don't follow
   the mod's destination.

### Second cursor coupling: movement limit
For the directly-controllable current unit, `UnitMovementAgent` (UnitMovementAgent.cs:517-551) advances only while
`IsCurrentUnit && IsActing`. It clamps each step through `TurnController.TickMovement`, which uses `CurrentMovementLimit`.
For player-controllable units that value is UI state. `UpdateSmartCursorVariants` (inside `UpdateActionPredictions`)
switches it, for example to `FiveFootStep` while the cursor hovers an adjacent enemy. 0.2.119 sets the limit the way
native AI decides (five-foot step only if the whole path fits, otherwise `TwoActions`). `TickMovement(total:false)`
still spends only the move action while the standard action is unused.

## 2. Fix (0.2.119)

- `GameInterface/CommandDispatcher.cs`: every mod command goes through it. In TB combat, when the command has to move
  (`ShouldUnitApproach`), it does what native AI does: `Init` → `FindPath` + `BlockUntilCalculated` (path claimed while
  computing, like `DecisionContext`) → `FixPathIfTooShort` port → trim ally-occupied end → `ForcedPath`. It also sets the
  movement limit. It rejects the command (as a step failure) if no movement is left, mirroring `ScorePath`. Otherwise
  the game would force-finish the command as "Success" without acting. Real-time and out-of-combat commands pass
  through unchanged, because `TickApproaching` uses `view.MoveTo` there.
- `TurnController.Tick` postfix: acts only in `Preparing`/`Acting` and no longer writes `Status`. It ends the turn via
  `ForceToEnd` (`CanEndTurn` accepts `Preparing`), and clears an idle leftover command that would block ending the turn.
- `TrapAvoidancePatch` (`UnitCommands.Run` prefix): it used to replace every player-faction `UnitMoveTo` with a new one.
  That included cutscene, mount and fear moves, and moves created inside TB hover-prediction click simulation on every
  mouse move. The replacement dropped `ForcedPath`, rider/mount links and other properties. Now it touches only
  `CreatedByPlayer` moves outside TB combat and outside `PointerController.SimulatingClick`, and it retargets in place
  with `SetTargetForce`.
- `ShouldControlUnit`: skips units that are not `IsDirectlyControllable` (confused, panicked, `PreventDirectControl`)
  and crusade tactical combat. The "Enable in Turn-Based / Real-Time" toggles are now honoured.

## 3. Other defects fixed

- **Real-time, stuck detector** (`RealTimeController.ProcessUnit`): it interrupted any command with `IsStarted == false`.
  But `ShouldStartCommand` starts a `UnitMoveTo` or melee attack only when `IsUnitCloseEnough()`, i.e. on arrival. So
  every walk longer than one decision interval (0.5-0.95 s) was cancelled. Now a command counts as stuck only if the
  unit isn't moving, it isn't waiting for a cooldown, and more than 3 s have passed.
- **Real-time, `UnitMoveTo(dest, 0f)`** at 4 call sites: with radius 0 the move never completes. Now uses the default radius.
- **Newtonsoft.Json binding**: the shipped DLL referenced NuGet Newtonsoft.Json 13.0.0.0 (strong-named), but the game
  ships Owlcat's 8.0.0.0 (unsigned) and the release did not include 13.0. The project now compiles against the game's
  copy. `Save` uses `SerializeObject(object, Formatting, JsonSerializerSettings)`, an overload Assembly-CSharp itself
  calls, so it is known to exist.
- **Logging**: debug logging defaulted to on and wrote several lines per frame in TB. It is now off by default, and a
  concise always-on `[Cmd] ISSUED / REJECTED / ENDED` trace (EventBus, no Harmony) replaces it for movement diagnosis.

## 4. Verification status

| Check | Status |
|-------|--------|
| Compiles (0 errors, 0 warnings) against the real Assembly-CSharp | **Done** (Linux, .NET 8 SDK, net48 reference assemblies) |
| Owlcat/A*/Newtonsoft members used exist | **Done, indirectly.** Those DLLs were not supplied. The build used reference stubs generated from Assembly-CSharp's own TypeRef/MemberRef tables, so every member the mod binds to is one the game itself calls. Two facts were added by hand: `ABPath : Path`, and `PathCompleteState.Complete = 2` (the game compares against `2`). |
| Output assembly references | Same set as the shipped DLL, except Newtonsoft.Json now 8.0.0.0 unsigned (the game's copy) |
| Binary binds like a build against the real game DLLs | **Done** (`tools/SigDump`). All 141 external member references shared with the shipped DLL, which was built against the real assemblies, are signature-identical. Each of the 10 new external references appears verbatim in Assembly-CSharp's own reference table. This check found and fixed a stub defect: `NNInfo`/`Int3` had been generated as classes instead of structs. |
| `Build.ps1` from a clean clone, `-GamePath`, `-Deploy`, offline rebuild from `References\` | **Done** (PowerShell 7.4; Windows PowerShell 5.1 not run) |
| ZIP layout equals original package (2 root entries) and passes integrity test | **Done** |
| **In-game behaviour** | **Not tested - no game runtime available.** Use the checklist in README.md and send the `[Cmd]` log lines. |

## 5. Remaining uncertainty

- The game-side analysis is static. The order of `UnitCommandController` and `TurnController.Tick` within a frame was
  not observed. The design does not depend on it: an extra frame in `Preparing` just delays the start.
- `GraphNode.AllyBlocked` semantics are taken from native `ScorePath` usage. If trimming ever removes a legitimate
  endpoint, the trace shows a shorter `forcedPath` than expected.
- A player holding the movement-modifier key during an AI turn (`m_ModifyMovementLimit`) still forces five-foot steps.
  That is native behaviour, and it is not overridden.
- Mounted companions (`RiderPart`) are left to the game's mount command path. They were not analysed further.
- Planning quality (destination choice, step order) was not changed. Only execution was.
