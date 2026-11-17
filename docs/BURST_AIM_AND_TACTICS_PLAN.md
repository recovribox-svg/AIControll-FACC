# Plan: delayed-burst ground aim, and three tactical features

17 November 2026, Claude. Planning only: no code, manifest or package change. Branch `claude/burst-aim-tactics-plan`,
from development `ad63ee8`.

The author asked for two things:

1. The design for the delayed-burst fix, using ground aim.
2. A plan for Features 1, 2 and 4 of the supplied "Master Architectural Proposal". The author gave Claude free rein to
   rewrite them, with performance as a requirement.

Every native claim below was read from the supplied `Assembly-CSharp.dll` (decompiled locally, not committed) and the
author's blueprint exports. Section 7 lists the sources.

## 1. Summary

| Slice | What it does | Modes | Needs author approval? |
| --- | --- | --- | --- |
| **A. Delayed bursts** | Aim a burst that can hurt allies at a point on the ground: where the target will be when the burst lands. Re-check before the cast goes off. Tell the other AI units where it will land. | Both (re-check: RT only) | A1-A2: approved ("use ground aim"). **A3 (cancel before the cast), A4 (other units avoid the spot) and A5 (turn-based turn waits for the landing): yes** |
| **B. Disrupt enemy casts** (Feature 4) | Value a hit on an enemy whose spell has not gone off yet by the chance that the game's concentration check destroys the spell. | RT only | No (valuation only) |
| **C. Suppressed buffs** (Feature 1) | Do not cast a buff the recipient's form or seal suppresses on arrival. Do not pay to cleanse a harmful buff that is already suppressed. | Both | No |
| **D. Sneak attack** (Feature 2) | Count fortification against sneak attack and criticals. Count the flanking that a second attacker creates, for both attackers. | Both | No |

Recommended order: A, B, C, D. A has played evidence (outcome2) and the author asked for it. B is the largest new
tactical gain and is cheap. C prevents wasted spells on shifted allies (Wenduag, Ulbrig). D is a small, exact
refinement.

## 2. What the supplied document gets wrong

The document cannot be implemented as written. The corrections change the design, so they come first.

- **It targets the wrong engine.** It puts real time on `UnifiedDecisionEngine`, `TargetScorer`, `AttackScorer`,
  `BuffScorer`, `DebuffScorer` and `FailedAbilityTracker`. Since v0.3.14 both modes use `Tactics/TacticalEngine`. The
  old engine is off by default and is to be removed (CLAUDE.md: "do not adapt it"). All work below goes in `Tactics/`.
- **Feature 1's premise does not hold in this game.**
  - Mage Armor under better armor is not "suppressed". Bonuses that do not stack sit side by side, and only the
    highest counts. `BuffStackingAnalyzer` already values that at 0.
  - Real suppression (`UnitPartBuffSuppress`) checks each new buff as it is attached (`Buff.OnAttach`). A recast of a
    suppressed buff is suppressed too, so recasting never resolves anything.
  - When the suppressing fact ends, the game re-activates the buff by itself (`UpdateBuffSuppressionState`). There is
    no "dormant buff that needs reapplying".
  - Haste and Slow cancel each other by removing the other buff (a `Conditional` with `ContextActionRemoveBuff`). That
    is not suppression, and the engine already reads it per target (`AbilityProfile.AppliesTo`).
  - The real cases are elsewhere: of 336 suppressing blueprints, polymorph forms (wild shape, Shifter forms,
    Elemental Body, dragon forms) suppress Enlarge/Reduce Person (45 blueprints), Haste (10), Displacement, Mirror
    Image, Mage Armor, Foresight, Blur and rages. Ameliorating hexes and similar effects suppress harmful conditions.
    See slice C.
- **Feature 2 already exists, and two of its rules are not this game's.**
  - `CombatMath.ReadRoutine` prices sneak dice × 3.5 × hit chance on every attack line. It applies when the target is
    flanked or natively flat-footed (`RuleCalculateAC.IsTargetFlatFooted`), with precision immunity respected.
  - The native flat-footed rule (`RuleCheckTargetFlatFooted`) already covers stunned, helpless, cannot act yet, cannot
    see the attacker, total concealment, lost Dexterity, and Shaken/Frightened with Shatter Defenses. The engine gets
    all of these for free.
  - WotR has **no 30-foot limit on ranged sneak attacks** and **concealment does not stop sneak attack**.
    `RuleAttackRoll` checks only sneak dice, immunity, flat-footed or flanked, bombs and fortification. Adding those
    two gates would make the AI worse.
  - What is really missing: fortification, and the flanking that a second attacker creates. See slice D.
- **Feature 4's numbers are wrong, and turn-based mode does not need it.**
  - The game's concentration DC after damage is **10 + spell level + half the damage**, not the full damage
    (`RuleCheckConcentration.OnTrigger`).
  - In turn-based mode an enemy casts on its own turn, when our units cannot act. Only real time has a window.
  - Damage forces the check (`UnitConcentrationController`). A failed check spends the spell, which is lost
    (`FailIfConcentrationCheckFailed` calls `Ability.Spend`).
  - Stun, daze and the like stop the cast through `!CanAct || !CanCast`, but the enemy keeps the spell. The existing
    control value already counts those lost turns, so slice B adds no extra bonus for control.
  - The spell-level multipliers (0.1 / 0.5 / 1 / 2-3) and the flat +35 / +45-60 bonuses are replaced by the game's own
    rules and the engine's damage-equivalent points.

## 3. Slice A: delayed bursts, ground aim

### 3.1 What the game does

- An area burst picks its victims around its anchor when it is delivered (`AbilityTargetsAround.Select`). The anchor
  is the unit's eye position **at that moment** if the target is a unit, or the fixed point if it is a point.
- `AbilityDeliverDelay` waits `DelaySeconds` (Explosive Runes: 2.0 s), then delivers on `context.MainTarget`.
- A projectile aimed at a unit follows it and delivers on it at impact. The stock Fireball projectile (`Fireball00`)
  flies at 13.5 m/s: about 1.5 s at 20 m.
- Before that, a real-time cast takes 2 s from cast start to the moment it goes off (6 s for a full-round spell;
  0 s for a wand or an "Immediate" animation; `UnitUseAbility.m_CastTime`).
- So a unit-anchored burst follows its target for walk + cast + delay. Outcome2 shows the result:
  - AI 1453: Explosive Runes/Sleep on a Vescavor Swarm (a swarm walks) went off 8.7 s after the order. It hit Seelah
    (15), Wenduag (26), Bandit (29) and Tripper (32).
  - AI 7181: Explosive Runes/Frigid Touch walked 3.2 m first, went off 4.3 s later, and hit Wenduag (28) and
    Bandit (39).
  - The logs do not record positions when the bursts landed. Drift is the likely cause, but not a proven one for
    every hit.
- Explosive Runes can be aimed at a point (`CanTargetPoint` true). For a fused cast, `AbilityData.CanTargetPoint`
  follows the deliver spell (`MagicHackData.DeliverBlueprint`).
- `AbilityProfile.TargetOn` already aims point-capable areas at the ground. It skips this for areas that pick their
  own targets (`SelectsTargets`), so every burst is aimed at the unit today.
- The spell and the action are spent only when the cast goes off (`OnAction` calls `Spend`;
  `UnitCommandController` calls `SpendAction` at the `IsActed` transition). Cancelling before that costs nothing.
- In turn-based mode, the mod ends a turn about 0.2 s after the cast (`ReadyToEndTurn`: projectiles and an idle
  pause). The native turn end does not wait for an `AbilityDeliverDelay` either. A 2 s delay therefore runs into the
  next unit's turn, and a unit-anchored burst follows that unit if it walks.

Abilities affected in the exports (point-capable, picking targets around the anchor, with a delay):

| Ability | Delay | Radius | Who it picks |
| --- | --- | --- | --- |
| Explosive Runes (and its fusions) | 2.0 s | 10 ft | Any |
| Wail of the Banshee | 3.0 s | 40 ft | Any |
| Rain of Halberds (Trickster) | 1.0 s | 20 ft | Any |
| Stone Call | 0.9 s | 40 ft | Any |
| Ice Storm, Volcanic Storm | 0.8 s | 40 ft | Any |
| Cleansing Flames (Angel) | 0.5 s | 20 ft | Any |
| Flame Strike, Infernal Hellfire | 0.39 s | 10 ft | Any |
| Deep Slumber, Eye of the Sun | 0.2 / 0.5 s | 10 / 30 ft | Enemy only: **excluded** |

All projectile-delivered bursts (Fireball and the like) also qualify through their travel time. The ProfileAudit
section in 3.9 lists them.

### 3.2 A1: which abilities

A burst gets ground aim when all of these hold:

- it picks targets around its anchor (`SelectsTargets` without being a projectile cone);
- it can hurt an ally: its selector is `Any`, and `SelectiveSparesAllies` is false;
- it has no attack roll (a bomb's direct hit needs the unit as target);
- the cast can target a point, and the native `AbilityData.CanTarget` accepts that point.

If any of these fails, it keeps unit aim as today. An enemy-only selector is better aimed at the unit: it follows the
target and cannot hurt allies.

Metadata is read once per profile and cached with it, from the deliver blueprint (the fused deliver spell for a
fusion):

- the delay seconds;
- the first projectile's `Speed` and `MinTime`;
- the selector type and radius.

### 3.3 A2: aim and forecast

**Landing time.** For an aim at distance `d`:

    T = t_walk + t_cast + t_lag

- `t_walk` = (d − range) / own speed when the cast walks first, else 0.
- `t_cast` = the native cast time: 2 s; 6 s for a full-round spell not from an item; 0 for a wand or an Immediate
  animation. In turn-based mode it is 0, because nobody else moves during the caster's turn.
- `t_lag` = delay seconds + projectile travel, max(`MinTime`, distance / `Speed`).

**Lead point.** The point is where the anchor enemy will be at `T`:

- Walking enemy: along its walk by `min(walk length, speed × T)`. A walk is a command of its own that has not started
  and is not yet close enough to its target. This is the same rule `WalkClaim` uses for allies, extended to enemies
  in `BodiesFor`: one read per enemy per decision, at most 8 command slots.
- Otherwise: where it stands.
- Turn-based: always where it stands.

**Who is hit.** All terms are counted with the existing per-recipient damage, immunity and save valuation; only the
positions change.

- Enemies: their predicted position at `T`. An enemy counts as hit only within `R − u` (surely inside). `R` is the
  radius plus the enemy's body; `u = 0.25 × its speed × T`, the growing uncertainty of a prediction. (Implemented as
  this yes/no rule, so the existing per-recipient valuation is unchanged; see [BURST_GROUND_AIM.md](BURST_GROUND_AIM.md).)
- Allies: counted if any of five samples in `[T − 0.5 s, T + 0.5 s]` falls within `R + u`. This replaces the fixed
  2-second `AlliesWalkingInto` window for these bursts and keeps its conservative "passing through counts".
- Downed allies: where they lie.

**What stays the same.** The friendly-fire veto (Avoid Friendly Fire), the six-angle cone re-aim, Selective, the
blocklist, spell policies and `CastOutcome` are unchanged. A fused pool's Hit Dice forecast (`SharedHdControl.TryForAim`)
receives the point aim; the game's selector accepts a point anchor.

The candidate carries `Point` and no `Target`, as Grease and Entangle already do. The plan (point, radius, `T` parts,
predicted allies and enemies, value) is kept in a small `BurstPlan` record for A3 and A4.

**Cost.** For each anchor enemy, about (enemies + 5 × allies) distance tests: around 80 for a full fight. No path
query, rule trigger or actor preview is added. That is about what today's walk sampling costs: one sample every
0.5 m of each walking ally's next 2 s.

### 3.4 A3: re-check before the cast goes off (RT; approval needed)

This hook joins the existing replan predicates in `RealTimeController.ProcessUnit` (next to `ShouldReplanAfterDamage`)
as `RealTimeTactics.ShouldReplanBurst`.

**When it runs.** For the unit's own `UnitUseAbility` that has a `BurstPlan` and has not gone off (`!IsActed`):

- once at cast start;
- otherwise at most every 0.25 s.

It reads fresh positions and the remaining time: walk + cast + lag before cast start, then the cast time still left
plus the lag.

**When it cancels.** At least one of these must hold:

1. Avoid Friendly Fire is on and an ally the plan did not count is now predicted inside.
2. The damage to newly exposed allies exceeds the plan's net value. It is estimated with `CombatMath.EstimateAbilityDamage`,
   once per new ally per plan.
3. No enemy is predicted inside any more: the spell would be wasted.

**How it cancels.** It interrupts only that command, then the engine decides again. The interruption is free by the
native rules in 3.1. Never after `IsActed`.

**Limits against thrashing.**

- At most 2 cancels per caster and ability every 6 s.
- After a cancel, that ability is not aimed within 1.5 m of the same point for 1.5 s.

Each cancel is logged once: `[Burst] arcan: Explosive Runes cancelled before casting (Seelah walks into it)`.

**Cost.** At most 4 checks a second per casting unit. Each is about 50 distance tests.

### 3.5 A4: the other AI units avoid the landing spot (RT; approval needed)

`AoeDangerAnalyzer.RegisterPending` today records only lasting areas, and drops them when the cast command finishes,
which is before a delayed burst lands.

- Register ally-hurting bursts too, at the lead point.
- Keep the entry until `T` + 0.3 s (at most 12 s). Drop it early only if the command ends without going off.
- The existing walk pricing (`PendingOnWay`) then keeps the other AI-driven units from walking into a burst about to
  land.

Player-controlled characters are not affected. The list holds at most one entry per casting unit.

### 3.6 A5: turn-based turn waits for its burst (approval needed)

`ReadyToEndTurn` waits while the unit's own delayed burst has not landed. The landing time is the moment the cast
went off plus `t_lag`. The existing 4-second cap (`MaxTurnEndWait`) still applies. The burst then lands before
anyone else moves, which closes the turn-based case completely. The cost is up to 2-3 s more per such cast in
turn-based mode.

### 3.7 Diagnostics

One `[Burst]` line per issued cast:

- the point;
- the lead, in metres from where the enemy stood;
- `T` and its three parts;
- the predicted enemies and allies.

The A3 outcome (kept or cancelled, and why) is logged too. Bounded per fight, as the existing note memos are (128
casters, 8 lines each).

### 3.8 Limits

- A walk that starts after the decision cannot be predicted. A3 sees it if it starts before the cast goes off; A4 keeps
  the other AI units out afterwards. A player's new order during the delay can still walk into the burst.
- The lead follows a straight walk, as `WalkClaim` does. The game's path may curve.
- An enemy that changes course after the cast has gone off makes the burst land where it would have been. That costs
  value, not allies.

### 3.9 Checks

- **Unit tests** for a pure `BurstRules` class (like `SpotRules`):
  - landing time, including the full-round, wand and turn-based cases;
  - the lead point, including the end of a walk and a zero speed;
  - the enemy share with uncertainty;
  - the ally window;
  - the A3 decision and its limits.
- **ProfileAudit `BURST-AIM`** on the real exports:
  - classification of every ability;
  - Explosive Runes 2.0 s and Wail of the Banshee 3.0 s qualify;
  - Fireball qualifies by projectile (13.5 m/s);
  - Deep Slumber and Eye of the Sun (enemy only) and Selective casts are excluded;
  - a fused Explosive Runes reads its deliver spell;
  - the count of qualifying abilities.
- **Compiled-route checks:** point wrapper, native `CanTarget`, the selector anchor for points.
- **Release build:** 0 warnings and errors. Existing tests, script tests and native harnesses stay green.

**Replay (RT):**

1. Cast Explosive Runes on a walking melee enemy with an ally in its path. Expect the lead point in `[Burst]`, no hit
   on the ally, and a cancel when the ally walks in before the cast goes off.
2. Repeat with Avoid Friendly Fire on and off.

**Replay (TB):** the turn waits until the burst lands (A5).

## 4. Slice B: disrupt enemy casts (Feature 4, real time only)

### 4.1 Native rule

| Rule | Native source |
| --- | --- |
| A spell can be broken by damage if it is a spell, is not a sticky touch, and is not cast from an item other than a scroll | `AbilityData.CanBeInterruptedWithDamage` |
| Every damage packet to a caster whose cast has started and not gone off queues a concentration check | `UnitConcentrationController.OnEventAboutToTrigger` |
| DC = 10 + spell level + damage / 2, against d20 + concentration | `RuleCheckConcentration.OnTrigger` |
| On a failure the cast ends and the spell is spent | `UnitUseAbility.FailIfConcentrationCheckFailed` |
| Each hit is its own check; once one fails, later ones change nothing | `MakeConcentrationCheck` |

### 4.2 Design

**B1. Who is casting.** Once per decision, list the enemies whose `Commands.Standard` is a running `UnitUseAbility`
that has not gone off, can be broken by damage, and is not a cutscene. Time left = native cast time (the private
`m_CastTime`, through a cached `AccessTools` field reference; 2 s if unavailable) − `TimeSinceStart`.

**B2. What it costs us if it lands**, in damage-equivalent points (`S`). Computed once per enemy command and cached
weakly by command:

- If the engine can read the spell, the spell's own forecast on its native target: damage through the existing
  estimates, control through the existing control worth.
- Otherwise, that enemy's per-round threat share (`EnemyThreat`) for one standard action.

**B3. Chance our candidate breaks it.** Concentration and spell level come from the game's
`RuleCalculateAbilityParams`, through the existing `CombatMath.ParamsOf`. For each of our hits `i` that lands before
the cast goes off, with hit chance `h_i` and expected damage `d_i`:

    p_i = clamp01((10 + SL + floor(d_i / 2) − concentration − 1) / 20)
    P   = 1 − Π (1 − h_i × p_i)

**B4. Timing gate.** A hit counts only if it can land before the cast goes off:

    time to hit = walk + own action time (+ projectile travel), with 0.25 s to spare

- Own action time: 0.6 s for a weapon attack, until the replay measures it from DidAct times.
- Our own spells take their native cast time, so they rarely make it against a 2 s cast. Against a 6 s full-round
  cast they often do.
- Only the attacks of a full attack that land in time count.

**B5. Value.** The candidate gains `P × S` (never more than `S`), once per our unit and that enemy command. Target
choice follows from the existing per-target candidates: no separate target score is needed.

**B6. Not added:**

- An extra bonus for stun or daze. The control value already counts those turns.
- A walk to reach a caster in time. A walk is priced as usual and the timing gate usually rules it out.

**B7 (optional, later).** An enemy caster held in Entangle or Web, or casting under "difficult casting", must pass
15 + spell level for every spell. The engine already has that rule for its own casters
(`CombatMath.CastingConcentrationChance`). Entangle's value against enemy casters could include their expected lost
spells. It needs a reading of the enemy's spell share first, so it is not in the first slice.

### 4.3 Bounds, checks, replay

**Bounds.**

- One command read per enemy per decision.
- Spell stakes and parameters once per enemy command.
- A few multiplications per candidate that targets a casting enemy.
- No rule trigger that rolls dice, no actor preview.
- Turn-based: nothing runs.

**Checks.**

- Unit tests for the chance: single and multiple hits, half damage, clamping, timing gate.
- ProfileAudit `CAST-DISRUPT`: `CanBeInterruptedWithDamage` on real blueprints is true for spells and scrolls, and
  false for sticky touches, wands and abilities. The compiled route reads `m_CastTime`.

**Replay.** RT fight against enemy casters. `[Disrupt]` rows name the caster, the time left, `P` and `S`. Compare with
"concentration check failed" lines in `combatLog.txt`.

## 5. Slice C: suppressed buffs (Feature 1)

**C1. A buff suppressed on arrival is worth 0.**

- When the recipient has a `UnitPartBuffSuppress`, test each buff the cast would apply, by the native rule. A buff
  is suppressed when:
  - it is not on the exclusion list, and
  - it is on the buff list, **or** its descriptor or its school is suppressed.
- The buff's context takes the ability's descriptor plus the buff's own descriptor, and the buff's school or else the
  ability's (`MechanicsContext` order).
- The part's four private sets are read through cached `AccessTools` field references (read-only).
- If every buff the cast applies would be suppressed, `AppraiseBuff` returns 0 with `Modelled = true`. This also
  blocks the once-per-combat fallback for unread effects. The log names the suppressing fact once per fight.
- Example: Haste or Enlarge Person on an ally in Wild Shape or a Shifter form.
- Recipients without the part (nearly all) cost one cached part lookup.

**C2. A suppressed harmful buff is not urgent to remove.** It does nothing while it is suppressed. In cleanse and
dispel worth (`BuffsRemovedFrom`, `HasSomethingToDispel`), such a buff counts only for the time it outlasts its
suppressor: `worth × max(0, 1 − suppressor time left / buff time left)`. A permanent suppressor makes it 0. The same
applies to dispelling a suppressed buff on an enemy.

**C3. Not changed.** Shifter form appraisal already counts the buffs a form suppresses (`ShifterFormProjection`), and
it withholds forms that suppress by school or descriptor.

**Why not the document's design.** Recasting never resolves native suppression, release is automatic, and
`FailedAbilityTracker` belongs to the old engine. The engine's existing `CastOutcome` no-effect memory covers casts
that do nothing.

**Checks.**

- Unit tests for the predicate: list, descriptor, school, exclusion.
- ProfileAudit `BUFF-SUPPRESS`:
  - reads all 336 suppressors;
  - Enlarge Person under a Wild Shape buff is suppressed;
  - Ameliorating hexes suppress their conditions;
  - a buff on the exclusion list is not suppressed.

**Replay.** Cast Haste with Wenduag in a form. She should be skipped, with a logged reason.

**As built (17 November 2026, [BUFF_SUPPRESSION.md](BUFF_SUPPRESSION.md)).**
- C1 drops suppressed buffs from each recipient's list in `ScoreAbility` rather than inside `AppraiseBuff`. It has the
  same effect: no value, and no fallback for a plain buff. It also covers toggle grants and out-of-combat upkeep.
- The school follows the native context: the buff's own, else the ability's, else the buff's `AddSpellSchool`.
- C2: a cleanse counts a suppressed buff only if it outlasts its suppressor. A cleanse's value is flat per ally, so
  there is no time share to take. The dispel worth subtracts the suppressed rounds, as planned.
- The exports hold 337 suppressors (336 counted when planning).

## 6. Slice D: sneak attack refinements (Feature 2)

**D1. Fortification.** On a sneak attack, a confirmed critical or precise strike, the game rolls d100 against
`UnitPartFortification.Value`. A roll at or under it negates **all three** (`RuleAttackRoll`). Today the engine counts
fortification for neither. Multiply each attack line's sneak damage and critical extra by `1 − chance / 100`.

- Read once per target per frame.

**D2. Flanking from a second attacker.** Native flanking needs no positioning: it only counts engagers
(`UnitCombatState.IsFlanked`):

- turn-based: more than one unit engaging the target;
- real time: more than one engaging unit whose command targets it;
- never for `CannotBeFlanked`.

When our melee attack would be the second engager:

1. Price our own routine with `TargetIsFlanked = true`. The native attack-bonus rule then adds its flanking bonus,
   and the sneak line applies.
2. Add the partner's gain: its sneak dice × 3.5 × hit chance × its attacks for one round, × the fortification
   factor. Only partners with sneak dice, against targets not immune to precision damage.

Performance: the routine cache key gains the flanking flag. The partner's term is computed once per (partner, target)
per decision, only when exactly one other unit engages.

**D3. Not proposed.**

- The 30-foot and concealment gates: they are not this game's rules.
- A flanking-position search: it is not needed, because native flanking has no geometry.
- A flat bonus per sneak die: the expected damage is already computed.

**D4 (optional, later).** Shaken or Frightened makes a target flat-footed for an attacker with Shatter Defenses
(native rule). A Demoralize or Dazzling Display could count the sneak damage and lost Dexterity it opens up for those
allies.

**Checks.**

- Unit tests for the flanking prediction and the fortification factor.
- ProfileAudit `SNEAK`: fortification facts in the exports (`AddFortification`) are read, and the precision-immunity
  reader still passes.

**Replay.** A rogue and a front-liner on one enemy, in both modes. The routine line shows "flanked (second
attacker)", plus the partner's term.

**As built (17 November 2026, [SNEAK_FLANKING.md](SNEAK_FLANKING.md)).**
- D1 also covers the estimate's critical chances (Magus touch delivery), attack-roll abilities, archer blast modes and
  auto-threat attacks, through the same factor.
- D2 needs no cache-key change: the prediction is made inside the routine read from the attacker and target in that
  frame.
- The partner's term also counts the +2 on its melee attacks, not only its sneak dice. A partner without sneak dice
  still gains the bonus, and the attacker's own routine already counted its +2.
- The candidate tag is `[flanks]` or `[flanks with <ally> +N]`.
- D4 is not done.

## 7. Native sources read

| Fact | Source |
| --- | --- |
| Burst anchor at delivery; Selective; `CanTargetFriends` | `AbilityTargetsAround.Select` |
| Delay delivers on `MainTarget` | `AbilityDeliverDelay.Deliver` |
| Projectile follows a unit target; delivers on `proj.Target` | `AbilityDeliverProjectile.Deliver` |
| Fused point targeting follows the deliver spell | `AbilityData.CanTargetPoint` |
| Cast time 2 / 6 / 0 s; spell spent when the cast goes off; concentration failure spends it | `UnitUseAbility` (cast time, `OnAction`, `FailIfConcentrationCheckFailed`) |
| Action spent when the command goes off; `!CanAct` / `!CanCast` interrupts | `UnitCommandController` |
| `IsStarted`, `IsActed`, `Interrupt` | `UnitCommand` |
| Turn-based turn end does not wait for a delay | `TurnController` (Acting branch), `CustomBrainPatch.ReadyToEndTurn` |
| Damage queues concentration | `UnitConcentrationController` |
| Concentration DC | `RuleCheckConcentration.OnTrigger` |
| Entangled and difficult casting check every spell | `UnitUseAbility.MakeConcentrationCheckIfCastingIsDifficult` |
| Interruptible spells | `AbilityData.CanBeInterruptedWithDamage` |
| Suppression rule; new buffs checked on attach; automatic release | `UnitPartBuffSuppress`, `SuppressBuffs`, `Buff` |
| Context descriptor and school | `MechanicsContext` |
| Sneak conditions; fortification | `RuleAttackRoll` |
| Flat-footed conditions | `RuleCheckTargetFlatFooted` |
| Flanking by engagers | `UnitCombatState.IsFlanked` |
| Haste/Slow, Explosive Runes, suppressor tallies | the author's `Spells`, `Buffs`, `Classes` and `Mythic` exports |

## 8. Performance budget

| Slice | Added work per decision | Cache |
| --- | --- | --- |
| A2 | About 80 distance tests per anchor, for qualifying bursts only | Profile metadata; enemy walk claims in `BodiesFor` |
| A3 | Up to 4 checks a second per casting unit, about 50 tests each | None (fresh positions are the point) |
| A4 | One list entry per casting unit | Pruned by time |
| B | One command read per enemy | Stakes and parameters per enemy command |
| C | One part lookup per recipient; field reads only with the part | Predicate per (buff, recipient) per decision |
| D | One fortification read per target per frame; partner term only in the two-engager case | Routine cache key |

None of them adds a path search, a world scan, a dice-rolling rule, an actor or context preview, or a persistent
actor cache.

## 9. Delivery

Each slice is a separate task branch and PR into development, with:

- Release 0 warnings;
- unit, script and ProfileAudit runs;
- native harnesses 98/14/10;
- a receipt;
- a test package.

Slice A starts once the author has answered A3-A5. A1-A2 can be built on their own: then only the aim and the
forecast change, and the delay window stays unguarded.
