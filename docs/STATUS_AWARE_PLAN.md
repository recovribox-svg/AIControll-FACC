# Status-aware companion AI: investigation and plan

Analysis only. Nothing here is implemented yet. Written against the 0.3.14 work-in-progress branch.

Evidence labels:
- **[Game]**: read in the decompiled game code (`Assembly-CSharp`).
- **[Mod]**: read in this mod's code.
- **[Log]**: seen in your playtest logs.
- **[Inferred]**: follows from the above but not observed directly.
- **[Untested]**: needs an in-game check before it can be called done.

## A. Executive finding

The mod does not need a separate "status manager". Almost every status is already handled correctly, because the
tactical engine reads its action economy from the game's own functions (`HasStandardAction`, `HasMoveAction`,
`HasFullRoundAction`). Those functions already include Nauseated, Staggered and surprise-round limits, and Tabletop
Tweaks does not change them.

What is broken is narrower. There are a few places where the mod either:
- stops a unit that the game still lets act, or
- offers an action that the game will refuse to start.

In order of impact:

1. **Turn-based: a prone companion never stands up and loses every turn** [Game][Mod]. A unit that is lying down
   (tripped, Grease, or healed back from unconscious) has `CanAct = false`. The mod reads that as "cannot act" and ends
   the turn after 1 second. The game, however, is waiting for an order: in turn-based mode a directly controlled prone
   unit stands up only when it is given a command (`UnitProneController.Tick`; `TurnController.UnitCanGetUpOnCommand`).
   So the unit stays down until the fight ends. This is very likely the second half of your observation that allies
   healed in turn-based combat only get up after the fight, while in real time they do (in real time the game stands
   them up by itself after 3 s). In the log you sent (`cc122b55`) the first half was the cause: the heal was too
   small and Seelah stayed unconscious, which 0.3.14 already fixes. A heal that was big enough would then have hit
   this bug.
2. **Real time: a Nauseated companion freezes inside the cloud that nauseates it** [Log][Mod]. `RealTimeController`
   returns "Cannot act - Nauseated" before it reaches the AoE escape check. Nauseated still allows movement (the game
   starts `UnitMoveTo` while nauseated). In `59302aeb` Orvath stood in a Stinking Cloud for 26 s (about 4 rounds; 32
   "Cannot act - Nauseated" lines). His queued Gather Power could not start, and his HP went from 100 % to 44 %.
3. **Turn-based has no "leave this harmful area" option** [Mod]. Since 0.3.14 the engine prices *entering* an area
   (`MoveRisk`), but it never considers *leaving* the one the unit is standing in. Only the real-time controller has
   an escape. A nauseated unit (movement only) in turn-based mode therefore just ends its turn in the cloud.
4. **The engine offers actions the game will refuse to start** [Game][Mod]. These cost decision attempts, and
   sometimes the whole turn:
   - **Nauseated**: swift actions, move-action abilities and toggles are offered, but only `UnitMoveTo` starts.
   - **Staggered** (only one action): Move+Attack and move-then-cast are offered. The move uses the only action and
     the attack never happens. This is the same flaw the surprise round had before 0.3.8; it was fixed for the
     surprise round only.
   - **CantMove** (Web, Hold, pinned): candidates that need movement are offered, because `HasMove` reflects the move
     *action*, not whether the unit can walk.
5. **Removing a condition is valued the same whatever it does** [Mod]. Removing Paralysis from the tank is worth the
   same as removing Shaken from a bystander, and the time left on the effect is ignored.
6. **Diagnostics gap** [Log]. 93 "cannot act this turn - ending turn" lines across six turn-based logs, but the log
   never says *why*, so none of them can be attributed to a cause after the fact.

Everything else (Dazed, Stunned, Paralysed, Helpless, Sleep, Confusion, Panic/Fear flight, Charm, Entangled) is
already handled correctly by the game boundary or the existing code. It should not be touched (see C).

## B. Call-flow traces (where status is checked today)

**Turn-based**: `TurnController.Tick` postfix → `TurnBasedController.ProcessTurn`.
- `CustomBrainPatch.ShouldControlUnit` is re-checked every tick. A unit that is not directly controllable (confused,
  panicked, charmed) is left to the game [Mod]. This is correct: confusion "act normally" releases control
  (`UnitConfusionController` → `ReleaseControl`) and the next tick picks the unit up [Game].
- `GameAPI.IsReadyForNextAction` = `Commands.Empty && CanAct`. With `CanAct == false` and the unit idle for more than
  1 s: "cannot act this turn - ending turn" (`TurnBasedController.cs:140`) [Mod]. This is the prone bug: `CanAct`
  includes `!Prone.Active` (`UnitState.IsAble`), but the game's own test for "may still act this turn",
  `UnitEntityData.IsAbleToAct()`, deliberately clears `Prone.Active` before testing [Game].
- `TacticalEngine.BuildContext` → `HasStandard/HasMove/HasSwift/HasFullRound` from the game. `CanFullAttack` also
  excludes Staggered. `Affordable()` filters candidates [Mod].
- The game's turn-based start check (`UnitCommandController.TickCommandTurnBased`, line 95–103) does two things
  [Game]:
  - It holds every command of a directly controlled prone unit until it has stood up.
  - It force-finishes any command that is close enough to start while the unit is nauseated, or `!CanAct`, as
    "Success" without acting. The mod's `LearnFromLastCommand` then records "force-finished before it started" and
    tries the next option [Mod].

**Real time**: `AiBrainController.TickBrain` prefix → `RealTimeController.ProcessUnit` [Mod]:
1. throttle and mount checks;
2. `GetCannotActReason`: Dead / Unconscious / Paralysed / Petrified / Stunned / CantAct / Dazed / **Nauseated** /
   Cowering / **Frightened** / **Prone** → return;
3. AoE escape (only when `CanMove`);
4. the stuck-command check (`WaitReason` "nauseated" holds a non-move command without calling it stuck);
5. `RealTimeTactics.Act` → `TacticalEngine`.

Step 2 runs before step 3, so the Nauseated case never reaches the escape.

In real time the game starts commands through `UnitCommandController.ShouldStartCommand` (line 238–252) [Game]:
- `UnitMoveTo` starts as soon as it is close enough, *before* the `CanAct`/Nauseated checks.
- Every other command needs `CanAct`, no equipment update, no cooldown, and `!Nauseated`.

## C. Status / action-permission matrix

| Status | What the game allows (source) | TB today | RT today | Verdict |
|---|---|---|---|---|
| Dazed, Stunned | Nothing. `IsAble` false. `WillTakeTurn` false; TB `Preparing → Acting → ToEnd` within ticks [Game] | turn ends | skip | **correct** |
| Paralysed, Helpless, Sleeping, Petrified, CantAct, Cowering | Nothing (`CanAct` false; `IsAbleToAct` false) [Game] | turn ends | skip | **correct** |
| Prone, can stand (`!ShouldBeProne`, `CanStandUp`) | TB: stands when given any command; uses the move action (standard if no move left; `TurnController.HandleUnitWillGetUp` +3 s move cooldown); provokes AoO unless `GetUpWithoutAttackOfOpportunity` (`UnitCombatEngagementController`). RT: stands by itself after 3 s, no cost [Game] | **turn ended: never stands** | skip (game stands it) | **TB bug (F1)**; RT correct |
| Prone, must stay (prone condition, unconscious, asleep) | `WillTakeTurn` false [Game] | turn ends | skip | correct |
| Nauseated | Movement only: `UnitMoveTo` starts; standard, swift, move abilities, toggles and attacks never start; no standard action (`IsStandardActionRestricted`) [Game] | standard actions filtered by the game; swift / move-ability options still tried and refused; **no escape option** | **frozen, no escape** | **bugs (F2, F3, F4)** |
| Staggered | One move *or* one standard action (`IsMoveActionRestricted` → `UsedOneMoveAction`); no full-round actions; vanilla halves speed (Tabletop Tweaks removes the halving; `CurrentSpeedMps` reflects either) [Game][TTT] | full attack excluded; **Move+Attack still offered** | n/a (cooldowns) | **TB gap (F4)** |
| CantMove, MovementBan (Web, Hold, pinned) | `CanMove` false; actions in place allowed [Game] | movement options suppressed; **Move+Attack / move-then-cast still offered** | `HasMove = CanMove` | TB gap (F4) |
| Entangled | Moves at half speed; concentration check to cast [Game] | concentration chance priced (0.3.14) | same engine | correct |
| Frightened, Panicked (enemy in sight) | `IsPanicked` → not directly controllable; the game makes it flee (`UnitFearController`) [Game] | left to the game | left to the game | correct |
| Frightened, not panicked (no enemy in sight for 3 s) | Directly controllable, may act; sees an enemy → panics again [Game] | acts normally | **frozen** | minor inconsistency (F6) |
| Confused / Charmed / Dominated | Control taken away (`PreventDirectControl`); "act normally" gives it back [Game] | left to the game, retaken next tick | same | correct |
| Shaken, Sickened, Fatigued, Blind... | Penalties only [Game] | penalties come from the game's own rules (hit chance, saves) | same | correct |

Overlapping statuses compose without special cases. Every row above is a game predicate, and a plan built on those
predicates combines them the way the game does. For example, Nauseated + Prone: the unit can stand up, then may only
move. Staggered + Prone: standing up uses the only action and nothing is left, which the game's `HasStandardAction`
then reports.

## D. Options and trade-offs

1. **A status subsystem** (per-condition handlers, a status priority list). Rejected. It duplicates rules the game
   already exposes, it has to be maintained for every mod that adds conditions, and it would have two sources of
   truth for the action economy. This goes against the CLAUDE.md rule "use the game's own rule functions".
2. **Patch individual gates** (reorder the RT check, special-case prone in TB). This fixes F1 and F2 cheaply, but it
   leaves F3, F4 and the mismatch between what the engine offers and what the game starts.
3. **Recommended: a small hybrid.** Fix the two gates (TB prone, RT nauseated). Add one game-derived *permission
   snapshot* to the existing `TacticalContext`, consumed by `Affordable()` and by the movement builder. Add one new
   movement candidate ("leave the harmful area"). There is no new pipeline and no change to how anything is scored;
   it only stops the engine from offering what the game will not start.

## E. Recommended design

**`ActionPermissions`**: a field of `TacticalContext`, built once in `BuildContext` for both modes from game
predicates only.

| Field | Game source |
|---|---|
| `CanWalk` | `State.CanMove` |
| `MovementOnly` | `CanAct && HasCondition(Nauseated)` |
| `OneActionOnly` | `unit.IsMoveActionRestricted()` (covers Staggered and the surprise round; replaces the separate `SurpriseRound` test) |
| `StandUpPending` | TB only: `IsDirectlyControllable && Prone.Active && !UnitProneController.ShouldBeProne(unit) && State.CanStandUp`. This is the formula `TurnController` uses for `UnitCanGetUpOnCommand`; read `CurrentTurn.UnitCanGetUpOnCommand` when the unit is the current turn's rider. |
| `Reasons` | short text for the log, e.g. `[nauseated: move only]` |

The pure part (flags in, which slots and kinds are allowed out) lives in a static function covered by unit tests,
the same pattern as `SpellTierPolicy`. The game-reading part lives in `BuildContext`.

How the permissions are used:
- `Affordable()`:
  - `MovementOnly` → only `CandidateKind.Move` candidates (moves are `UnitMoveTo`, which the game starts).
  - `OneActionOnly` → no candidate with `NeedsMove` and a standard action.
  - `!CanWalk` → no candidate with `NeedsMove`.
- **Prone (TB)**. `TurnBasedController`'s "cannot act" exit and `IsReadyForNextAction` treat `StandUpPending` as
  able. `BuildContext` reserves the action that standing up will use: the move if one is left (also no full attack),
  otherwise the standard. The engine then decides as usual. The first command makes the game stand the unit up. The
  game then cancels the waiting command (`TryCancelCommands`) and charges the move. `LearnFromLastCommand` does not
  count a command cancelled by standing up as failed (it remembers "prone when issued"), so the next decision runs
  with the real economy.
  - Standing up always happens. Staying down loses the turn and gives enemies +4 to hit in melee; the attack of
    opportunity for standing is the lesser cost. It is logged ("stands up: move action, provokes 2").
- **Leave harmful area** (both modes, in `AddMovementCandidates`). When the unit's own position is dangerous
  (`AoeDangerAnalyzer.AnalyzeUnit`), offer a move to the escape point the analyzer already computes, or a
  `FindRetreatPoint` that is outside every area.
  - Value: danger here × 0.5 × `AllyWorth`, minus `MoveRisk` at the destination. This is the same scale `MoveRisk`
    already uses for entering, so staying and entering are priced alike.
  - Under `MovementOnly` it is often the only option left, which is correct.
- **Real-time gate order**. `GetCannotActReason` stops treating Nauseated as "cannot act", so the existing emergency
  escape runs, and `RealTimeTactics` decides with `MovementOnly` (escape, withdraw, back off, or wait). A held
  non-move command stays queued, as now, and starts when the nausea ends.
  - Keep the proven real-time pre-engine escape. Whether to fold it into the engine candidate is a later,
    separate decision, once the turn-based candidate has playtest history.
- **Diagnostics**. Add a status snapshot (game conditions plus `Reasons`) to the `[Tactics]` decision line and to
  every "cannot act" or turn-ending line.

## F. Tactical safety model

- Every new or changed option is a *Move* candidate priced with the existing `MoveRisk` (AoO via the game's
  disengage checks, plus areas on the path), so no new risk accounting is introduced.
- Areas are only left for a point the analyzer considers safe; `FindRetreatPoint` already rejects points that enter
  areas, and in real time invalid navmesh points.
- Standing up is not an option, it is a fact of the turn (a reserved action), so it cannot outbid anything.
- Permissions only remove options the game would refuse anyway. They cannot make the AI do something new except the
  two moves above.

## G. Status removal by allies (secondary)

Today a cleanse is valued at `(2 + 2 × spell level) × rounds × (engaged ? 1 : 0.6)` per ally it would help, whatever
it removes [Mod]. A better rule, still in damage-equivalent points:

**Value = what the condition costs the ally per round × the rounds it would otherwise still last.**

- **Cost per round.** Use the existing condition classification in `AbilityProfile.ClassifyCondition`:
  - Hard (paralysed, stunned, nauseated, dazed, ...) ≈ the ally's own turn value: their best damage or role worth;
    `AllyWorth` is already in that unit.
  - Helpless also adds the Coup de Grace danger the engine already models for enemies.
  - Soft ≈ a fraction of that.
- **Rounds left.** From the buff itself (the game's remaining duration), capped by the combat's expected rounds. A
  daze with one round left is worth almost nothing to remove.
- **Feasibility.** High. `BuffsRemovedFrom` already lists exactly which buffs a cleanse would remove (descriptor
  removals included), and the conditions those buffs add can be read the same way `BuffEffectAnalyzer` reads them.
- **Main risk.** Overvaluing cleanses against damage. Mitigate by keeping the duration cap and the existing
  "verifiable effect: 0 means not useful" rule. Do this after the action-permission phases, because it changes
  scoring and needs its own log review.

## H. Risk / regression matrix

| Change | Could break | Why it should not / guard |
|---|---|---|
| TB prone stand-up | Turn flow for prone units; a command loop if the game does not stand the unit up | Only when the game's own `UnitCanGetUpOnCommand` formula is true. Failure memory still applies. If the unit is still prone after one issued command, fall back to today's behaviour (end turn) and log it [Untested: order of `TryCancelCommands` vs our learning] |
| RT Nauseated no longer "cannot act" | RT units moving while nauseated | Only `UnitMoveTo` candidates are allowed, which the game starts anyway; the stuck-command logic already knows "nauseated" |
| `MovementOnly` / `OneActionOnly` / `CanWalk` in `Affordable` | Healthy units losing options | Each flag is false unless the game condition is present. Unit tests on the pure rule. Compare decision lines before and after on healthy units in the same save |
| `OneActionOnly` replacing `SurpriseRound` | Surprise-round behaviour (fixed in 0.3.8) | `IsMoveActionRestricted` is exactly the game's surprise test plus Staggered; check a surprise round in the playtest |
| Leave-area candidate | Units leaving buffs/areas of their own side, or dancing in and out | Only harmful areas (same analyzer and Selective rule as `MoveRisk`); the destination must be outside; a moved-nowhere move is already recorded as failed |
| Cleanse valuation | Healers spending turns on minor cleanses | Duration cap; phase it separately with log review |
| Tabletop Tweaks | Different condition rules | No hard-coded rule tables. TTT changes the Nauseated/Staggered *descriptors* (what can remove them) and Staggered speed (`CalculateSpeedModifier`). Both come through the game functions we read (`CurrentSpeedMps`, buff contexts). TTT does not patch the action permission functions [TTT source] |

## I. Phased plan and acceptance criteria

| Phase | Content | Size | Accept when |
|---|---|---|---|
| 0 | Status snapshot in decision, cannot-act and turn-end lines | small, no behaviour change | Every "cannot act" line names the condition(s) |
| 1 | TB stand-up (F1) | small–medium | Tripped / Grease-prone / healed-from-unconscious companion stands up on its own turn and still uses its standard action; log shows "stands up"; no turn timeouts |
| 2 | RT nauseated: gate order + movement only (F2) | small | In a Stinking Cloud a nauseated companion leaves the cloud; no other RT change |
| 3 | `ActionPermissions` in `Affordable` (F4), both modes | medium | Nauseated: no refused swift/ability attempts in the log; Staggered: no Move+Attack picks; webbed unit: no move-needing picks; healthy-unit decisions unchanged |
| 4 | TB leave-harmful-area candidate (F3) | medium | A unit starting its turn in Stinking Cloud / Acid Fog / its own side's Entangle leaves it when the value says so; never steps into another area to do it |
| 5 | Cleanse value by condition and duration (G) | medium | Remove Paralysis / Restoration go to the most-disabled ally; no cleanses for effects about to expire |
| 6 (optional) | RT Frightened-not-panicked may act (no advance) (F6) | small | Same behaviour as TB; low priority |

Phases 0–2 are independent, low risk, and fix the only problems with log evidence. Phase 3 depends on nothing but
should come before 4, because 4 relies on `MovementOnly` to be the natural choice under nausea.

## J. Validation done / limitations

Done:
- The game rules above were read in the decompiled game: `UnitState`, `UnitEntityData` action and turn functions,
  `UnitCommandController`, `UnitProneController`, `TurnController`, `UnitFearController`, `UnitPartConfusion`.
- Tabletop Tweaks Base and Core source was searched for condition and command patches.
- All 20 logs were searched for status lines.

Limitations:
- No in-game run was possible here.
- The prone diagnosis is code-proven but not yet seen in a log. The logs do not record conditions, and the downed
  allies in the logs stayed unconscious, which is the heal-size problem already fixed in 0.3.14.
- Some of the 93 "cannot act" lines may be ordinary daze or stun: the game may still have been scrolling the camera
  when the 1-s idle limit hit.

To check in the game:
- the stand-up command sequence (Phase 1);
- that a nauseated unit's escape in real time starts while a Move-type ability is queued (Phase 2).

Suggested test fights: Grease (prone), a tripping enemy (wolves, mongrels), Stinking Cloud, Web / Hold Person,
Color Spray, Cause Fear, and a turn-based revive with a large enough heal.

## K. Vanilla vs Tabletop Tweaks

- **Action permissions** (`IsStandardActionRestricted`, `IsMoveActionRestricted`, `IsAble`, `CanAct`, `CanMove`,
  `ShouldStartCommand`): not patched by TTT. The behaviour is identical.
- **Staggered speed**: vanilla halves it; TTT (`StaggeredDescriptors` fix) removes the halving with a
  `CalculateSpeedModifier` transpiler. The mod reads `CurrentSpeedMps`, so both are handled.
- **Nauseated / Staggered buff descriptors**: TTT sets them to Nauseated / Staggered only. This decides which
  descriptor-based cleanses remove them. The mod checks removal against the buff's live context the way
  `ContextActionRemoveBuffsByDescriptor` does, so it follows TTT. Several TTT spell fixes (Repulsive Nature, Incense
  Fog) change which buffs apply Nauseated; the plan reads the resulting conditions, not the spell list.
- **Selective + lasting areas**: already asked of the game (0.3.14, `SelectiveRule`). The leave-area candidate uses
  the same rule.
