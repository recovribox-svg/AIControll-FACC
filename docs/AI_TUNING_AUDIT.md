# AI tuning, balancing and refactoring audit (0.3.17 cycle): analysis and plan

This is analysis and planning only. Nothing in the mod was changed for it. The evidence comes from:

- **Code.** A full read of the live decision path, plus four separate audits: parameter inventory, live vs legacy
  code, hot-path cost, and log statistics. The audit reports are in `docs/AI_TUNING_AUDIT_APPENDIX.md`.
- **Logs.** Seven playtest logs with 4,229 decision lines: five turn-based (`0d742ea2`, `25a1e132`, `4de5be87`,
  `648247bb`, `e62751a4`) and two real-time (`87aa4bc7`, `9c5c6df2`).
  - The two real-time logs come from builds before 0.3.15, so Gather Power and other things fixed since then
    still appear in them.
  - No log yet covers 0.3.16. The cast-provoke pricing (`CastProvokeRisk`) has never been seen working in a game.
- **Game code.** The decompiled Assembly-CSharp, checked wherever a finding depends on a game rule.

Each finding carries one of these labels:

- **confirmed:** seen in the code or the logs, with a clear cause.
- **strongly evidenced:** the cause is clear, but the effect on play is inferred.
- **hypothesis:** plausible, but not yet shown.
- **insufficient evidence:** not enough to say either way.

"No change" is a valid decision and is used where the evidence does not justify a change.

---

## 1. Executive summary

1. **The engine is broadly sound.**
   - The single value scale ("damage-equivalent points") works.
   - Turn-based and real-time share one engine.
   - Game rules are used wherever it matters (attack rolls, saves, defensive casting, disengage checks).
   - Most choices in the logs are sensible: weapon attacks lead for martial characters, and spells and control lead
     for casters.
   - The selection rule (quick action first if worth ≥ 2, otherwise the best main action) is correct for Pathfinder's
     action economy. It stays.
2. **The biggest real behaviour gap is moving through harmful areas.**
   - A melee character whose enemies stand in Grease or Entangle can only plan a straight-line approach into the
     area. Since 0.3.15 that is priced honestly, so the character ends its turn instead.
   - Seelah did this for six rounds in a row (`648247bb` R1–R6).
   - Of the 18 turn-based "END TURN with a standard action left" cases, 13 are this pattern.
   - Fix: approach and advance points that keep out of harmful areas, checked against the real path.
3. **The engine expects fights to be about half as long as they are.**
   - The expected-rounds estimate is below the real remaining length at 81 % of round points. The median ratio
     (real / estimate) is 1.85, and 1.94 at fight start (28 fights).
   - Every multi-round value uses this horizon: buffs, control duration, summons, and an ally's worth (which feeds
     healing and risk).
   - So long-term options are systematically under-valued. Fix: calibrate the estimate from the party's observed
     damage per round.
4. **There are four scoring cliffs and inconsistencies with a clear cause. Each can be fixed locally.**
   - **Kill bonus.** It jumps fourfold the moment expected damage reaches the target's HP.
   - **Heal urgency.** It is counted twice, drops to zero at the threshold, and a summon's heal is only partly halved.
     A full-HP Camellia healed a Dretch instead of using Daze.
   - **Caster stepping out of melee.** The step is not checked against the spell's range, and its worth is not tied to
     the risk it avoids.
   - **Spell-slot multiplier.** It also applies to heals and buffs, against what the setting's text says.
5. **The game provokes on every ranged attack roll, rays included** (`UnitCombatEngagementController`, checked in the
   decompile).
   - Shooting in melee is priced by a flat guess.
   - Ray and ranged-touch spells are priced only for the casting, which defensive casting avoids. The attack roll
     still provokes.
   - Fix: price both with the same attacker model the 0.3.16 cast pricing uses.
6. **There are three live correctness bugs outside scoring:**
   - An immunity cache that is never cleared, so it is stale. It hard-filters spells.
   - The out-of-combat buff routines ignore the "control main character / companions / custom AI" settings and the
     item policy.
   - Three patches keep running while the mod is toggled off.
7. **About 19–20k of the ~37k lines are dead or reachable only through the old real-time engine setting.**
   - About 4–5k lines are wholly dead and can go without any change in behaviour.
   - Removing the old real-time engine is your call (§9).
   - Some legacy-only work still runs every frame in default real-time mode (`CombatDataCache`, `TeamBlackboard`).
     It can be switched off safely.
8. **There is no performance measurement yet.**
   - The hot-path audit found large, safe savings: per-decision caches and hoisting the attacker pricing out of the
     per-target loop.
   - Plan: measure first (timing summary per fight), then optimise what the numbers show, with a shadow comparison to
     prove decisions are identical.

**Recommendation:** do the work in four phases (§8).

1. Diagnostics, with no behaviour change.
2. Low-risk correctness fixes and dead-code removal.
3. Calibration, with value tables and playtests.
4. Consolidation.

Each phase ships as test builds and is checked against the log metrics in §7.

---

## 2. Architecture and scoring map (live paths)

```
Turn-based  TurnController.Tick ─► TurnBasedPatches.TurnControllerTick_Postfix ─► TurnBasedController.ProcessTurn
            ─► (StandUp: TacticalEngine.ShouldStandUp) ─► TacticalEngine.Decide ─► Execute ─► CommandDispatcher.Run
Real time   AiBrainController.TickBrain ─► CustomBrainPatch.TickBrain_Prefix ─► RealTimeController.ProcessUnit
            ─► [GetCannotActReason] ─► [AoE escape: AoeDangerAnalyzer.ShouldEscape ─► ExecuteMoveAction]   ◄ own policy
            ─► [stuck check ─► FailedAbilityTracker (nobody reads it)] ─► RealTimeTactics.Act ─► TacticalEngine
Legacy RT   RealTimeLegacyEngine=true ─► UnifiedDecisionEngine + Scoring/* + SituationAnalyzer + MovementPlanner
Out of      party combat end ─► RTC.ApplyPermanentBuffsOutOfCombat (AbilityClassifier, unit.Abilities only)
combat      UI button ─► PreBuffController ─► unit.Commands.Run directly (no dispatcher, no item policy)
Observers   DamageObserver, SaveObserver, CastOutcome, CommandTrace, MountPathHandler (EventBus)
```

**Value terms**, all in damage-equivalent points (formulas verbatim in the appendix, §A):

| Term | Core | Horizon | Role weight |
|---|---|---|---|
| Damage | `min(E, HP) + kill bonus(Threat)`, × wake factor | – | yes |
| Weapon | `DamageValue` from the game's own attack rules; full/single/Move+Attack/Charge | – | yes, plus range preference |
| Heal | `heal·0.6·urgency·1.25 + Δdrop·AllyWorth` | AllyWorth `min(ER,3)` | yes |
| Control | `p·Threat·strength·rounds` | `min(ER,3)`, per-target duration | **no** |
| Buff | `(1+1.5·SL)·rounds·quality` × timing × difficulty | `min(ER,3)` (long: ER) | yes |
| Summon / enchant | fallbacks for effects that cannot be verified | `min(ER,4)` / ER | enchant only |
| Risk | MoveRisk (area per round, exposure, disengage attacks of opportunity), CastProvokeRisk | AllyWorth | Tank 0.6 |

"Worth of a unit" is `4 + 3·level` (or `4 + 3·CR`) per round in five places. `Threat` doubles as an enemy's damage per
round in drop-chance maths.

---

## 3. Parameter inventory: the parts that matter for tuning

The full inventory is in the appendix, §A. It covers about 120 constants, each with file and line. The points that
matter for this plan:

- **One horizon, ExpectedRounds,** feeds about ten terms. That is why its bias (finding B2) matters more than any
  single constant.
- **The same concept is valued on different scales:**
  - Attacks of opportunity: `Threat×0.35` for shooting in melee and for Coup de Grace, `×0.4` for the step-out value,
    `×0.5` in withdraw, and the full attacker model elsewhere.
  - Drop chance: RiskMath, plus two inline `exp` copies.
  - Round caps: 2, 3, 4, or uncapped.
- **Cliffs:**
  - kill bonus (4×)
  - heal threshold (to 0) and urgency steps (1/2/3)
  - buff timing (×1 until round 2, then ×0.6; in real time a "round" is a 6-second span)
  - potion factor at difficulty 0.8
  - the summon factor at two enemies
- **Duplicated constants:**
  - `4+3·level` ×5
  - `min(ER,3)` ×7
  - `1+1.5·SL` ×3
  - the not-fighting factor 0.6 ×3
  - the save-fail formula ×3 (one copy unclamped)
  - `BuffWorth` copies the buff formula and is used only by the legacy scorer
- **Thresholds that are fine and stay:** MinMainValue 0.5 and MinQuickValue 2.
  - A quick action does not use up the main action, so "quick first" never costs anything.
  - The one interaction, the turn-based 5-foot step forbidding later movement, is already handled: the step is only
    offered against a main action that needs no movement.

---

## 4. Findings register

Severity reflects the effect on play. IDs: B = behaviour/scoring, C = consistency/architecture, P = performance,
D = dead code, N = reviewed with no change.

### Behaviour and scoring

#### B1. Melee characters idle when their enemies stand in a harmful area
- **Label:** strongly evidenced. **Severity:** high.
- **Evidence:**
  - In `648247bb` R1–R6, Seelah ends six turns with `[SMWF5]` and no options.
  - In `25a1e132:643`, `Move+Attack [into Grease]=0.1`. At :1534 and :1615, `Advance [into Grease]` is valued 0.3/0.4.
  - 13 of the 18 turn-based END TURNs with a standard action left follow this pattern.
- **Cause:**
  - `ApproachPoint` (`TacticalEngine.cs:827`) is a straight line from the unit to the target.
  - Move+Attack, Advance and move-then-cast all price that one point.
  - Since 0.3.15 the per-round area cost is honest (`danger × perRound × rounds × 2` if the unit ends inside), so every
    approach is worth nothing. No other point is ever considered.
- **Remedy:**
  - When the straight-line approach enters a harmful area, sample points around the target within reach (two rings,
    16 directions, like `FindExit`).
  - Keep the walkable, unoccupied points within move range. Take the one with the lowest MoveRisk, and check the
    dispatcher's real path against the areas, falling back to the next point.
  - If one exists, the candidate becomes "move to that point" (move action). The next decision attacks: the same
    two-step pattern the turn-based touch approach uses, at no action cost, since Move+Attack is move + standard
    anyway.
  - If no point reaches the target, Advance uses the nearest safe point at the area's edge. Enemies leaving the area
    then walk into the unit's reach.
  - Entering stays possible when it is worth it, because the old straight-line candidate keeps its honest price.
- **Side effects:**
  - Movement in both modes.
  - Real time uses the same candidates, so it benefits too.
  - Risk: more move-only turns if the second decision then prefers something else. That is acceptable, because each
    step is re-decided.
- **Test:**
  - Grease on enemies with a melee ally outside: the ally attacks from the edge, or moves to it. It never ends the turn
    while a safe reach point exists, and never walks in when entering is worth less than zero.
  - Regression: an ordinary approach with no areas is unchanged (same decision lines).
- **Decision:** implement (phase 1).

#### B2. Expected fight length is about half the real length
- **Label:** confirmed (logs). **Severity:** medium, wide reach.
- **Evidence:** 28 turn-based fights and 190 round points.
  - The real remaining length is above the estimate at 81 % of points.
  - Median ratio 1.85 (quartiles 1.25 / 1.85 / 2.5); at fight start, 1.94.
  - Example: 4 enemies, estimate 3.5, real 6.
  - Caveat: "real" runs to the last decision line, so mop-up rounds and reinforcements count. Part of the bias is
    measurement, but 81 % is too consistent to be noise.
- **Cause:** `ExpectedRounds = 1 + enemyHP / max(10, 4·Σlevels)` assumes the party deals `4·level` damage per member
  per round. It ignores misses, time spent approaching, healing and control turns.
- **Effect:**
  - Horizons capped at 3 are about 20 % too short at the median (`min(est,3)/min(real,3) = 0.8`). Uncapped terms
    (long buffs, the enchant fallback) are about 45 % short.
  - Buffs, control and summons are under-valued against immediate damage.
  - AllyWorth, and through it healing and risk avoidance, is about 20 % low.
- **Remedy:**
  - Use the observed party damage per round. `DamageObserver` already sees party → enemy damage; it needs a
    per-fight total.
  - Blend it with the prior: `DPR = (prior·k + observed·roundsElapsed)/(k + roundsElapsed)`, with k ≈ 2.
  - Keep the 1–6 clamp and the per-term caps.
  - Write it as a pure function with unit tests.
  - Phase 0 first adds a fight-end calibration line: estimate at R1, real length, observed vs prior DPR. Then the
    prior's constant can be set from data, not guessed.
- **Side effects:**
  - Most values rise by up to about 25 % (the capped ones).
  - Buff vs attack order shifts slightly in early rounds. Check it with the value table (§7) before shipping.
- **Decision:** diagnostics in phase 0, the change in phase 2.

#### B3. Caster stepping out of melee: no range check, and its worth is not tied to the risk avoided
- **Label:** confirmed (code); effect on play is a hypothesis. **Severity:** medium.
- **Evidence:**
  - `TacticalEngine.cs:1795-1807`: `keepsTarget` is set only for archers (`ctx.HasRangedWeapon && !castsInMelee`).
  - The step's value is `min(0.4·ΣThreat(engagers), followUp)`, whatever the defensive-casting chance is.
  - In `648247bb:7114` it worked out: Fire Blast range 10.5, cast after the step. A short-range or close spell can
    lose its target, and after a 5-foot step no further movement is allowed.
- **Remedy:**
  - For `castsInMelee`, the step must keep the best main action's target within that ability's range and line of
    sight.
  - Value = what casting here loses: `StopChance·Value + CastRiskCost` of that action. This is already computed by
    `CastProvokeRisk` and carried on the candidate. Cap it at `followUp`.
  - A caster with high Concentration then rightly stays and casts defensively.
- **Side effects:** fewer steps for good casters, safer steps for poor ones.
- **Test:**
  - An engaged caster with a 1.5 m-range spell does not step out of range.
  - An engaged caster with Concentration far above the DC casts in place.
  - Regression: archer step-out is unchanged.
- **Decision:** implement (phase 1), after a 0.3.16 log has confirmed `CastProvokeRisk` in play.

#### B4. Heal value: urgency counted twice, a cliff at the threshold, summons only partly halved
- **Labels:** (a) confirmed; (b) strongly evidenced. **Severity:** medium.
- **Evidence:**
  - `TacticalEngine.cs:1510-1526`: `heal·0.6·urgency(1/2/3)·1.25(threatened)` plus `Δdrop·AllyWorth`. Both terms grow
    as HP falls.
  - An ally at threshold +1 % is worth 0, even under heavy threat.
  - The summon factor 0.5 is applied only through AllyWorth, not to the first term.
  - `648247bb:4247`: Camellia at 100 % HP casts Cure Light Wounds on a summoned Dretch (5.7) over Daze (5.1) and an
    attack (3.2). `25a1e132:2383` is similar.
  - Heal is the highest category: median chosen value 18–21, against 7–8 for weapons.
- **Remedy:**
  - (a) Apply the summon factor to the whole heal value. Small and safe.
  - (b) Rebuild it as one continuous model. Goals:
    - one urgency term (the drop-chance term already is one)
    - restoration valued by the HP that matters (`min(heal, missing)`) with a weight that tapers smoothly to 0 at
      the threshold
    - values for a dying ally kept at or above today's
  - Write it as a pure function with a before/after value table.
- **Side effects:** fewer top-ups of mildly hurt allies, the same or more rescues. It interacts with B2 (AllyWorth).
- **Decision:** (a) phase 1; (b) phase 2, after B2.

#### B5. The kill bonus jumps fourfold at expected damage = HP
- **Label:** confirmed (code); effect on target choice is a hypothesis. **Severity:** low–medium.
- **Evidence:**
  - `DamageValue` (`TacticalEngine.cs:869-878`): below HP the bonus is `0.25·Threat·E/HP`; at or above HP it is the
    full `Threat`.
  - It uses expected damage, not the chance to kill.
  - 131 weapon/weapon near-ties (within 5 %) are target choices that this cliff can flip.
- **Remedy:** `bonus = Threat·[P(kill) + 0.25·(1−P(kill))·min(1, E/HP)]`, with `P(kill)` from the existing damage
  spread (`RiskMath.ChanceAtLeast`).
  - It is continuous.
  - Overkill still gets the full bonus.
  - "Exactly enough" now counts for about 0.6 of the bonus instead of 1.
- **Side effects:** every damage value changes slightly. Focus fire must still emerge: unit test, then playtest.
- **Decision:** phase 2.

#### B6. The same control spell is cast on the same target several rounds in a row
- **Label:** insufficient evidence. **Severity:** unknown.
- **Evidence:**
  - Daze → Werewolf 7 rounds (`648247bb:3604`); Color Spray ×5 (`e62751a4`); Sleep ×3 (`4de5be87`).
  - All of these casts were accepted by the game ("Success"), but the logs do not say whether the target saved.
  - Repeating Daze on a target that keeps failing its save is good play.
- **Remedy:** none yet. Phase 0 logs the save result of each own control cast. Then decide whether the save-chance
  estimate needs a per-target learning step: `SaveObserver` already records the bonus used, not the pass rate.
- **Decision:** diagnostics only.

#### B7. The spell-slot multiplier also hits heals and buffs
- **Label:** confirmed. **Severity:** low.
- **Evidence:**
  - `TacticalEngine.cs:1749`: `value *= clamp(0.55 + 0.45·Difficulty)` for every levelled spellbook spell.
  - The setting's text (Standard: "spends spell slots on damage and control spells"; Progressive: "Cantrips, buffs,
    heals and items are unaffected") says otherwise.
- **Remedy:** apply it to the damage and control parts only.
- **Side effects:** levelled heals and buffs are about 10–45 % more attractive in easy fights.
- **Decision:** phase 1.

#### B8. Real time: AoE escape runs its own policy before the engine
- **Label:** confirmed. **Severity:** medium (real time only).
- **Evidence:**
  - `RealTimeController.cs:211-223` escapes whenever `ShouldEscape` holds (summed danger ≥ 0.5). It does not keep a
    target, price attacks of opportunity, or compare with acting.
  - The engine's leave-area candidate does all three.
  - In the two real-time logs: 41 escapes and about 1,230 "ShouldEscape=True" lines (Stinking Cloud), also log spam.
- **Remedy:** in the new engine path, drop the pre-engine escape and let the engine's leave-area candidate decide. It
  already handles nausea (move-only permission), keeping reach, and attack-of-opportunity cost. Keep the old escape
  only for the legacy engine.
- **Side effects:** real time only; turn-based is untouched. Risk: a unit stays in a weak area to finish a kill. That
  is the intended trade-off, and it is priced.
- **Test:** real-time Stinking Cloud and Grease on the party: units leave when it is worth it, keep attacking from the
  edge, never path through threatened squares for nothing.
- **Decision:** phase 2.

#### B9. Real time: the "cannot act" pre-filter bypasses the engine's status rules
- **Label:** hypothesis. **Severity:** unknown.
- **Evidence:** `RTC.GetCannotActReason` refuses Dazed, Cowering, Frightened and Prone before the engine runs, so
  `UnitStatus.Permissions` / stand-up never apply in real time.
- **Open question:** whether the game stands prone units up on its own in real time, and what frightened units may do.
  Check the decompile before deciding.
- **Decision:** investigate (phase 3).

#### B10. Ranged attacks and rays provoke by the attack roll, and that is not priced the same way
- **Label:** confirmed (game code). **Severity:** medium.
- **Evidence:**
  - `UnitCombatEngagementController.OnEventDidTrigger(RuleAttackRoll)` provokes for every `Ranged`/`RangedTouch`
    attack roll unless `DoNotProvokeAttacksOfOpportunity` is set. It is set by `PointBlankMaster`(+Parametrized),
    `IgnoreAttacksOfOpportunityForSpellList`, and for attacks of opportunity themselves.
  - The mod prices shooting in melee as `ΣThreat·0.35` (flat, no drop chance).
  - Ray and ranged-touch spells are priced only for casting, which defensive casting removes. The roll still provokes.
- **Remedy:**
  - One `AttackRollProvokeRisk`: same attacker set and `PriceAttacks` as `CastProvokeRisk`, fail = 1, minus the
    features above.
  - It applies to ranged weapon attacks in reach of enemies and to spells whose delivery makes a ranged attack roll
    (`AbilityProfile.NeedsAttackRoll` + ranged).
  - It shares the attacker memo with P2.
- **Side effects:**
  - Archers and ray casters step out more often when engaged, which the step-out candidate already supports.
  - Point Blank Master users are unaffected.
- **Decision:** phase 2.

#### B11. The out-of-combat buff routines ignore control settings and item policy, and see class abilities only
- **Label:** confirmed. **Severity:** medium.
- **Evidence:**
  - `ApplyPermanentBuffsToParty` (RTC:2167) and `PreBuffController.GetPartyMembers` take every player-faction unit.
    `ControlMainCharacter`, `ControlCompanions` and `EnableCustomAI` are ignored.
  - PreBuff calls `Commands.Run` directly (`PreBuffController.cs:341`): no dispatcher, no `ConsumablePolicy`.
  - Both list abilities through `AbilityClassifier.ClassifyAllAbilities` → `unit.Abilities.Visible`. In the game that
    collection holds ability facts, not spellbook spells.
- **Remedy:**
  - Apply the same "under AI control" check and `ConsumablePolicy`, and route through `CommandDispatcher`.
  - Spellbook spells out of combat need your decision (§9).
- **Decision:** settings and policy gates in phase 1; spellbook scope is your decision.

#### B12. Three patches run while the mod is toggled off
- **Label:** confirmed. **Severity:** low.
- **Evidence:** `TrapAvoidancePatch` (redirects the player's own moves), `PreBuffUpdater.Update` and `PetItemsPatch`
  do not check `Main.Enabled`. `OnToggle` only unsubscribes EventBus handlers.
- **Remedy:** an `Enabled` guard in each.
- **Decision:** phase 1.

### Consistency and architecture

#### C1. Immunity cache never cleared
- **Label:** confirmed. **Severity:** medium.
- **Evidence:**
  - `CombatRulesAnalyzer._baseImmunityCache` / `_aoOImmunityCache` are keyed by unit id. The only `ClearCache` has
    no caller.
  - They store condition immunities and SR-part descriptor immunities, which buffs grant and remove (Remove Fear,
    Protection from Evil).
  - `AnalyzeResistance` hard-filters damage and control candidates with them (`TacticalEngine.cs:1442/1605`).
  - Result: a temporary immunity seen once lasts for the whole game session, and one gained later is never seen.
- **Remedy:** per-decision memo (P2), or at least clear at every combat start and every round.
- **Decision:** phase 1.

#### C2. Three resistance and immunity paths, two hit-chance calculators, two "needs an attack roll" tests
- **Label:** confirmed. **Severity:** medium (inconsistent filters).
- **Evidence:**
  - SR is computed by `CombatRulesAnalyzer` (no spell penetration, hard filter) and by
    `HitChanceCalculator.CalculateSpellResistance` (with penetration).
  - Energy immunity is computed three times.
  - Spell attack rolls use a hand-made BAB + ability score vs touch AC, while weapons use the game's rules.
  - `HitChanceCalculator`'s own attack-roll test can disagree with `AbilityProfile.NeedsAttackRoll`.
- **Remedy:**
  - One resistance path: `EffectChance` with penetration, `AfterTargetReduction` for energy, and `HasCCImmunity` +
    descriptor immunity for control.
  - Reduce `AnalyzeResistance` to the immunity part, or remove it.
  - Spell attack rolls through `RuleCalculateAttackBonus` with the ability's touch/ray weapon.
  - Equivalence tests for SR with and without penetration feats, energy immunity, and mind-affecting immunity.
- **Decision:** phase 3.

#### C3. Real-time stuck detection writes to a tracker that nothing reads
- **Label:** confirmed. **Severity:** low.
- **Evidence:** `FailedAbilityTracker.RecordFailure` (RTC:293) is read only by the legacy engine. The engine learns from
  `TacticalDriver` / `CombatMemory.Failed` / `CastOutcome`.
- **Remedy:** keep the interrupt, and record the failure in `CombatMemory.Failed`.
- **Decision:** phase 2.

#### C4. Duplicated value formulas
- **Label:** confirmed. **Severity:** low (maintenance).
- **Evidence:** §3.
- **Remedy:** a small `ValueModel` (unit worth per round, round horizons, buff per-round base, the not-fighting
  factor), unit-tested.
  - A pure refactor with byte-identical values.
  - It is the base that B2/B4/B5 then change in one place.
- **Decision:** phase 1.

#### C5. `NextCommandTime` checked twice, never set by the engine
- **Label:** confirmed. **Severity:** none observed.
- **Decision:** no change (documented).

#### C6. CLAUDE.md "Key folders" describes the legacy architecture as live
- **Label:** confirmed.
- **Decision:** fix together with D1.

### Performance

#### P1. Nothing is measured
- **Label:** confirmed.
- **Remedy:** a `[Perf]` summary per fight and mode: decision count, median, p95, max ms, worst decision label. It is
  cheap (Stopwatch) and logged once per fight.
- **Decision:** phase 0.

#### P2. Repeated identical work within one decision
- **Label:** strongly evidenced (code), cost not measured.
- **Main items:**
  - `CastProvokeRisk` reprices the same attackers for every (ability, target).
  - `EstimateWeaponAttack` repeats up to 5× per pair.
  - `CalculateParams` runs per target per helper.
  - The SR fact scans repeat per (ability, enemy).
  - `IncomingDamage` at the unit's own position is recomputed in every `MoveRisk`.
  - `MoveRisk` runs before `ScoreAbility` has shown the option is worth anything.
- **Remedy:** a per-decision memo on `TacticalContext`, plus hoisting the attacker pricing out of the per-target loop.
  - Never shared across the `ShouldStandUp` prone toggle.
  - Verified by a shadow mode that computes both ways and logs any difference.
- **Decision:** hoisting the attacker pricing and moving `MoveRisk` after `ScoreAbility` in phase 1 (simple and
  safe); the full memo in phase 2 if P1 shows a need.

#### P3. A full Decide rerun for each rejected candidate
- **Label:** confirmed; rare (14 REJECTED lines in 7 logs).
- **Decision:** no change.

#### P4. Legacy-only work every frame in default real time
- **Label:** confirmed.
- **Evidence:**
  - `CombatDataCache.RefreshIfNeeded` rebuilds lists that only `SituationAnalyzer` (legacy) reads.
  - `TeamBlackboard.UpdateTeamAssessment` returns at once.
- **Remedy:** run both only with the legacy engine (after a final check that no live code reads them).
- **Decision:** phase 1.

#### P5. Log cost and noise
- **Label:** confirmed.
- **Evidence:**
  - `BLACKLISTED: Touch of Fatigue` is logged with `Main.Log` on every decision.
  - `[AoeDanger] … ShouldEscape` appears about 1,230× in real time.
  - Verbose strings are built even when verbose logging is off.
  - The decision line lists the top 5 options, so a quick action ranked lower is chosen "unlisted" (218 cases). That
    misled this audit at first.
- **Remedy:**
  - Report the blacklist once per ability per combat.
  - Throttle area-danger lines to when the state changes.
  - Guard verbose calls in the hot path.
  - Always append the chosen candidate with its value.
- **Decision:** phase 0.

### Dead code

#### D1. Wholly dead code, about 4–5k lines
- **Label:** confirmed.
- **What it is** (appendix §C has the full list):
  - Planning/TurnPlanner, BasePlan, DPSPlan, TankPlan and SupportPlan
  - SequenceOptimizer and ActionSequence
  - ActionExecutor, TurnOrchestrator, TurnPlan and PlannedAction
  - the Abstraction adapters
  - the RTC role-logic region (~890 lines)
  - unused helpers
- **Note:** UDE:341's `IsAbilityFailedThisTurn` always returns false; replace it with `false`.
- **Remedy:** remove, with no change in behaviour. Verify by build plus identical decision lines on a replayed fight.
- **Decision:** phase 1.

#### D2. The legacy real-time engine, about 12–14k lines
- **What it is:** UnifiedDecisionEngine, Scoring/*, SituationAnalyzer and its chain, MovementPlanner, MovementAPI,
  most of GameAPI.
- **Decision:** yours (§9). My recommendation: keep it through 0.3.x as the fallback setting, marked deprecated, and
  remove it in 0.4.0 if no real-time regressions are reported.

### Reviewed, no change

#### N1. "Quick action first if ≥ 2"
- It does not use up the main action, and the 5-foot step case is handled.
- Mind Spike (swift) chosen ahead of a higher-valued main action (23 turn-based cases) is correct. In the case
  checked, the main action (Fire Blast) followed in the same turn (`648247bb:7138-7142`).
- Only the logging changes (P5).

#### N2. Values differ ~10× between characters on the same target
- Lann's FullAttack 1.3–1.5 vs Orvath's ~9.
- Values are only compared within one unit's options. Lann's are low because his expected damage against that target
  is low (AC/DR), and he still attacks (> 0.5).

#### N3. No role weight for control, summons or marks
- No evidence of a resulting misplay. Roles bias damage, heal and buff by design.

#### N4. Buff value from spell level × quality
- A check from first principles agrees: Prayer gives about +1.7/ally/round × 5 allies × 3 rounds ≈ 25, against the
  engine's 33.
- An effect-based model would be a large redesign without evidence of wrong choices. Revisit only with evidence.

#### N5. Threat = 4 + 3·CR
- It matches the Pathfinder monster damage-by-CR guide up to about CR 8–10, then falls below it: CR 20 gives 64 vs
  about 90–120.
- That would understate danger and drop chance late in the game. The current logs are low-level, so this is
  unobserved.
- Phase 3 can add observed enemy damage per round. Tune it then.

---

## 5. Tuning decisions (what changes, and why that and not more)

| ID | Change | Why it is the right size |
|---|---|---|
| B1 | Approach/advance points outside harmful areas, checked against the real path | Fixes the largest lost-turn source. It reuses `FindExit`-style sampling, `MoveRisk`, the dispatcher path, and the two-step touch-approach pattern. No new value terms. |
| B2 | Observed party damage per round in the expected-rounds estimate | Fixes the one input that biases about ten terms. Data first (phase 0), and the clamps stay. |
| B3 | Step-out keeps the spell's target; its value = risk avoided | Uses the numbers `CastProvokeRisk` already computes. |
| B4 | (a) summon factor on the whole heal; (b) one continuous heal model | (a) is a one-line fix with a clear bug. (b) waits for B2 and a value table. |
| B5 | Kill bonus weighted by kill chance | Removes a cliff with an existing helper. Validated by a unit test and a playtest. |
| B7 | Slot multiplier only on damage and control | Matches the setting text the player reads. |
| B10 | Ranged attack-roll provocation priced like casting | It is the game's own rule. One function shared by weapons and rays. |
| – | Not tuned | Selection thresholds, role weights, buff model, Threat curve, repeated control (N1–N5, B6) |

---

## 6. Refactoring and optimisation decisions

1. **Remove dead code** (D1) before any other refactor, so later diffs are smaller. Behaviour is identical.
2. **ValueModel** (C4): one place for the shared formulas, with unit tests pinning today's values. The calibration
   changes (B2/B4/B5) are then visible single-line diffs with updated tests.
3. **Per-decision memo** (P2): one `DecisionCache` object owned by `TacticalContext`, so its lifetime is exactly one
   decision.
   - It must not survive `ShouldStandUp`'s temporary prone toggle, which builds its own context.
   - Keys: (attacker, target, full) for weapon estimates; ability for `CalculateParams` and the SR scans; enemy for
     Threat, threat hand and concealment; rounded destination for `MoveRisk`.
   - A debug "shadow" switch computes the uncached value too and logs any difference. Zero differences over a fight is
     the acceptance test.
4. **Resistance consolidation** (C2) waits until the memo exists. Both touch the same calls, and equivalence tests are
   easier then.
5. **Legacy real-time engine** (D2): only after your decision.
6. **No other restructuring.** `TacticalEngine.cs` is large (about 2,150 lines), but it is cohesive. Splitting it into
   partial classes (candidates / scoring / risk / movement) would help navigation. I propose it only together with
   the memo work, as a pure move with no logic change.

---

## 7. Validation plan

- **Unit tests** (`tests/CompanionAI.Tests`, now 31 pass). New pure functions get tests:
  - `ValueModel`
  - the expected-rounds blend
  - kill bonus
  - heal value
  - step-out value
  - reach-point selection (geometry with a fake area predicate)
  - `AttackRollProvokeRisk` maths

  Each calibration change ships with a before/after value table generated by the tests and put in
  `docs/TACTICAL_AI.md`.
- **Shadow comparison** for pure refactors and caches (D1, C4, P2): the same decisions, checked in the game by the
  shadow log.
- **Log metrics as a regression dashboard.** `logstats.py`, kept in `tools/` and run on every test log. It reports:
  - END TURN with a standard action left
  - `[into X]` choices and their outcomes
  - safety-limit hits, REJECTED lines, "not retrying" lines
  - repeated actions on the same target
  - category mix per character
  - near-tie share
  - `[Perf]` p95
  - the expected-rounds ratio (after phase 0)

  A build passes if no metric gets worse without an explained reason.
- **Scenarios in the game.** Turn-based first, then real time for each.
  1. Grease or Entangle on enemies, melee ally outside (B1).
  2. A caster engaged by two enemies, with a short-range and a long-range spell (B3, B10).
  3. A wounded ally plus a summon, with the healer at full HP (B4).
  4. A long fight of five or more enemies, watching buffs in R1–2 (B2).
  5. Stinking Cloud on the party in real time (B8).
  6. Main character not under AI control, then combat ends (B11).
  7. Mod toggled off, then move the party through a trapped area (B12).
- **Compatibility:** no rule function is copied or bypassed. Each test build is run once with Tabletop Tweaks loaded,
  which you use.

---

## 8. Prioritised backlog

**Phase 0: diagnostics (test build 0.3.17-test2, no behaviour change)**
- P1: `[Perf]` summary per fight.
- B2: fight-end calibration line (estimate at R1, real length, observed vs prior party damage per round).
- B6: save result of each own control cast.
- P5:
  - chosen candidate always shown
  - blacklist reported once per combat
  - area-danger lines only on change
  - verbose guards

**Phase 1: correctness and cleanup (0.3.17)**
- B1 area-aware approach (the largest item)
- B3 caster step-out
- B4a summon heal factor
- B7 slot multiplier scope
- B11 settings and item-policy gates
- B12 toggle guards
- C1 immunity cache
- C4 ValueModel
- D1 dead code (+ CLAUDE.md, C6)
- P4 legacy-only per-frame work
- P2-lite: hoist attacker pricing; `MoveRisk` after `ScoreAbility`

**Phase 2: calibration (0.3.18)**
- B2 expected rounds from observed party damage
- B4b continuous heal model
- B5 kill-chance bonus
- B10 attack-roll provocation
- B8 real-time escape through the engine
- C3 stuck feedback into CombatMemory
- P2 full memo (if phase 0 shows a need)

**Phase 3: consolidation and investigation**
- C2 one resistance and hit-chance path
- B9 real-time cannot-act rules (decompile first)
- N5 observed enemy damage per round → Threat
- D2 legacy engine (your decision)
- B6 follow-up if the save data shows a need

Each phase is a series of test builds you play, analysed with the dashboard in §7, before a release.

---

## 9. What I need from you

**Files and logs**
- **A turn-based log from the current 0.3.16 release**, ideally a fight where a caster gets engaged. `CastProvokeRisk`
  has never been seen in a game, and B3/B10 build on it.
- **Later, the phase 0 test build played for 2–3 fights** in each mode, preferably including one with five or more
  enemies. This gives the `[Perf]` and calibration numbers.
- **No more DLLs are needed for this plan.** The real AstarPathfindingProject and Owlcat.Runtime.Core you sent cover
  the movement work. If B9 turns up a question the decompile cannot answer, I'll ask.

**Decisions**
1. **Legacy real-time engine** (D2): keep it as a deprecated fallback through 0.3.x and remove it in 0.4.0 (my
   recommendation), remove it now, or keep it indefinitely?
2. **Out-of-combat buffs from spellbooks** (B11): today only class abilities are cast after combat and by the PreBuff
   button. Should either use spell slots, for example day-long spells like Mage Armor? My recommendation: the PreBuff
   button yes (you press it on purpose); the automatic after-combat routine no.
3. **Order:** start with phase 0 and phase 1 as planned, or do you want B1 (the idle melee fix) as a quick
   stand-alone test build first?

---

## 10. Final recommendation

- **Behaviour.** The decision model does not need a redesign. The highest-value work:
  - one behaviour gap: approach around harmful areas (B1)
  - one systematic bias: fight length (B2)
  - a few local cliffs and inconsistencies (B3–B5, B7, B10), each fixable with helpers the engine already has
- **Correctness.** Three bugs outside scoring: the stale immunity cache, the out-of-combat routines ignoring settings,
  and the patches active while disabled. All are small.
- **Code base.** Shrink it by removing the code that is already dead.
- **Speed.** Make it measurable before making it faster.
- **What should not be touched now:** the selection rule, role weights, the buff model and the Threat curve. The
  evidence does not support changing them, and changing them without evidence is how the AI would lose the
  consistency it has gained since 0.3.0.
