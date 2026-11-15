# Shifter forms: when to shift, which form, and Shifter's Fury (15 November 2026, Claude)

The author asked for the engine's Shifter form decisions to be checked and refined: when to take a major form, which one,
and how the minor aspects fit. They supplied a general proposal as a source of ideas; it is not specific to this mod.
This page records the evidence, how each idea maps onto the engine, the three changes made, and what is left open.
`docs/TACTICAL_AI.md` §174-175. Claude's `claude/shifter-forms` at `730c6b6` is continued on
`codex/shifter-form-validation`, stacked on PR #67 (`830d777`), then #66 and #65. Development remains `6b9be42`.

**Status.** Claude's implementation had 852 passing unit tests but no real-reference build. Codex's retained inputs
include the actual assemblies and exports, so no re-upload is required here. Both the gameplay source and audit tool
compile with zero warnings. Final checks, receipt and package identity are recorded under Verification after validation.
The new behavior remains unplayed.

## How the engine decides today (unchanged parts)

- **Which form.** Each major form ability is appraised in `TacticalEngine.AppraiseShifterForm`
  (`docs/SHIFTER_SUPPORT.md`). The form's native natural-attack routine is quoted against the same enemy and the current
  weapon routine (`ShifterFormProjection`, `CombatMath.ShifterFormDamage`). The quote also counts:
  - the Armor Class change: natural armour, size, lost armour and shield, Dexterity;
  - its worth against the incoming danger;
  - the hit points lost from a lower Constitution.

  At most eight forms are quoted per decision, and the best form competes with every other option.
- **When.** A form is a buff that costs the shift action. It must beat the best immediate main action by the existing
  15 % buff restraint (`ValueModel.BuffBeatsImmediate`).
- **Aspects.** Minor aspects are swift toggles valued by their stat bonuses. A quick action that clears its bar is taken
  before the main action, so an aspect comes first in a turn.
- **No thrashing.** An existing or suppressed form is never recast or swapped, and the engine never ends a form.
- **Casters.** New forms are withheld from units with a casting spellbook.
- **Out of combat.** Automatic out-of-combat upkeep never casts a transformation (the Ulbrig report, §146).

## Evidence (fusion2g1b log, Wenduag, Griffonheart Shifter)

These figures come from the fusion2g1b review (§172). The raw log is private and is not committed.

- **No Griffon Shape in 9 fights.** Her first appraisal rows of the fights were −6.9, 23.3, 22.8, −5.5 and 34.9. In the earlier
  fusion2 log she shifted twice, both in real time.
- **TB fight 2, R1 (lines 1220-1223).**
  - Griffon Shape was the best option at 23.3; the full attack was 20.5.
  - The 15 % restraint would have kept the full attack anyway (23.3 < 23.6).
  - Griffon Aspect (7.0, swift) went first. After it, the full attack was worth 31.4 and the form 20.2.
- **TB fight 3, R1 (lines 1613-1616).**
  - The full attack on the Large Air Elemental in reach (28.4) beat the form (22.8).
  - Because the form was positive, `AddShifterFuryCandidates` offered no Shifter's Fury.
  - Wenduag attacked without it (35.9 after the aspect) and selected no Fury in that fight.
  - In her other fights the Fury was worth 51.3 (8 → 15.8 damage a round), 136.2 (16 → 36.7) and 105.1 (10.5 → 26.4).
- **RT fight, RT1 (lines 3914-3917).**
  - Griffon Shape was the best option at 34.9; every other option was below 7.
  - Griffon Aspect went first. In the next decision the form was not among the five best (the fifth was 6.6), and she
    walked to attack instead.
  - The form's appraisal row is written once per fight, so the log cannot say what changed.

## The proposal, idea by idea

| Proposal | Engine | This change |
|---|---|---|
| Master Shapeshifter: keep the form for good | Native: permanent, no resource. The engine never ends or recasts a form; the projection includes Master Shapeshifter's +4. | None. A pre-combat shift stays off (the out-of-combat exclusion); that is a policy for the author. |
| Spend a charge only when the threat is high | The 15 % restraint already asks for a clear gain. | Not added. A form outlasts the fight (it carries into later fights until it lapses), and the log shows too few shifts, not too many. |
| Minor aspects are swift and stack with the form | Swift quick action, valued by its stats. | Audit all three playable Griffon tiers: on-cast removal is Polymorph; the payload also explicitly consumes Shifter's Rush. Neither removes an aspect. Numerical bonuses still follow native descriptor stacking. |
| Shifting costs a whole turn of attacks | The appraisal counted the casting round as a round of form attacks. | **Fixed (2).** |
| Pick the form by role (Pounce to close, grab against casters, tank forms when hurt, elementals against hazards) | Every form is priced against the same enemy and routine: damage, Armor Class against the incoming danger, hit points. A tanking form wins when the danger is high. | Not added: Pounce, grab, rend, damage reduction, immunities and energized damage are form facts not credited before the cast (limits). |
| A 3-round lock against form thrashing | Stronger already: an existing form is never recast or swapped in a fight. | None. |
| No spellcasting without Natural Spell | New forms are withheld from casters. | None. |
| Weapon and armour dependency | Lost armour and shield bonuses are priced, against the actual weapon routine. | None. |
| Feyform and Manticore | Manticore spikes use the ranged preference. Feyform forms keep equipment slots and are outside the projection, so they are never cast automatically. | None (limit). |
| Shifter's Fury (not in the proposal) | A positive form kept the Fury off even when the form lost. | **Fixed (1).** |

## Changes

1. **Shifter's Fury is no longer blocked by a form that will not be cast** (both modes).
   - **Why.** Choosing the Fury in a human body withholds a new major form for the rest of the fight. That native
     selection is not carried across a body change, a limit kept from `docs/SHIFTER_SUPPORT.md`. Before this change,
     any positive form candidate removed the Fury candidates, even when the form then lost to the attack (fight 3
     above), so the unit got neither.
   - **What it does now.**
     - The Fury candidates are always built (at most four quotes, as before).
     - In `Decide`, `TacticalEngine.HoldFuryForForm` holds a Fury quick action back only while a new form is the turn's
     main action above its minimum value, and only if that form beats, by the 15 % margin, the best immediate main action plus the Fury's own
       gain this round (`ValueModel.HoldFuryForForm`).
     - When the Fury waits, the next quick action is taken instead, and one `[Shifter] X: ... waits` row is logged a
       round.
     - Once in form, the existing Fury selection for the form's weapons applies.
2. **A form's attacks count from the round after a Standard or full-round shift** (`ValueModel.FormAttackRounds`).
   - In turn-based, the shift leaves only the move action; in real time the standard cooldown runs. Either way the form
     makes no attack this round.
   - The attack this round that the shift gives up is the competing candidate itself, so counting the round as a form
     round valued it twice.
   - The form's defence still counts from now.
   - A swift, free or move-action shift keeps this round.
   - In practice it changes the borderline cases: 23.3 at R1 of a 4.4-round fight becomes about 18 against a 20.5 full
     attack. The real-time case (34.9 against 6.7) still shifts.
3. **The form appraisal is logged again when it changes.**
   - After the first row of a fight, a new row is written when the value turns between paying and not paying, or moves
     by more than a quarter and at least 3 points (`ShifterRules.FormNoteChanged`).
   - At most 4 extra rows per fight, unit and form, within a 64-identity ledger. It clears at a new fight, never at
     capacity within the same fight; new identities wait until the next fight rather than restarting old allowances.
   - It adds no rule, context or forecast. The RT1 case above becomes readable.

## Cost

- No new rule, context, path query, actor cache or game reference.
- The Fury candidates are now also built while a form is positive, within their existing four-quote bound.
- The note store keeps at most 64 unit/form identities per fight, containing strings and scalar values only.
- Codex corrected the original mid-fight capacity clear, which could restart repeat allowances and cause message bursts.
  Three additional unit tests exercise the cap, fight reset and changes measured against the last logged value.
- Fury's wait diagnostic includes the fight identity, so a matching round number in a later fight is not suppressed.

## Blueprint clarification

The three **playable** Griffon Shape tiers are Standard actions in the supplied exports; cutscene-only free variants
are outside that claim. All three remove Polymorph buffs on cast and explicitly remove `ShiftersRushBuff` in their
payload. Thus the original statement that they remove buffs *only* by descriptor was inaccurate. The 26 supplied
`ShifterAspect*Buff` assets contain neither that descriptor nor the Rush identity, so this removal does not strip them.
That establishes survival, not universal stacking of every numerical modifier.

The temporary Rush buff lists all three tiers through native `FreeActionSpell` components. `SlotOf` already reads
`AbilityData.RequireFullRoundAction` and `RuntimeActionType`, rather than the blueprint's default action type; the
timing rule therefore preserves this round's attacks for a genuinely free shift. No ability, buff or action cost is
mutated to preview it. Unknown/native mod overrides remain subject to the existing projection limits.

## Limits and next steps

- **Form facts before the cast.** Pounce (a charge with a full attack when the enemy is out of reach), the form's
  Grabber (G1/G2 can value it once the form is on), rend, energized damage, damage reduction and immunities receive no
  pre-cast credit. Pounce is the most valuable of these: it needs a forecast of next turn's distance and charge path. It
  is proposed, not done.
- **Feyform-style forms** that keep equipment slots are outside the projection.
- **A pre-combat shift** with a long-lasting form (or Master Shapeshifter) would save the first turn of a fight. It is
  excluded by the author-reported Ulbrig behaviour, and is the author's call.
- **Fury inside a form after shifting** relies on the native regeneration of Fury choices on a body change. Codex's
  replay item 4 in `docs/SHIFTER_SUPPORT.md` asks to confirm it in game.
- **Unplayed.** The checks below cover rules, blueprints and compiled routes, not a played fight.

## Verification

- Full validation uses SDK 8.0.426 / Roslyn 4.11, the real supplied game references, and the same compatible blueprint
  scope for the PR #67 baseline and this branch. SHIFTER-FORMS must pass, CAMPAIGN-FLAG must remain passing, and the
  ability report and all other audit lines must be unchanged (apart from output file paths).
- Unit, script, native-provider, reference, source/compiler and test-package results will be recorded in
  `docs/competence/SHIFTER_FORMS_CHECKS.json`. The original raw Achaekek enum-load failure remains a separate negative
  input check; no raw dump is silently normalized or committed. Neither offline checks nor compilation establish
  actor execution or a Unity performance improvement.

## Replay

Play a Shifter with Fury (level 6+) and a major form, Grabber left to the engine, in turn-based and real time:

1. **A fight where the full attack beats the form:**
   - the Fury is selected in round 1 (`[Shifter] X: Shifter's Fury: ...`);
   - from then on the form shows `current/pending Fury selection retained`.
2. **A fight where the form clearly wins:**
   - when a human-body Fury candidate is available (its target is in reach), expect
     `[Shifter] X: Shifter's Fury: ... waits - Griffon Shape ... is this turn's main action`;
   - with only distant enemies there may be no Fury candidate, so no wait row is required;
   - then the shift;
   - then a Fury selection for a form weapon.
3. **The appraisal rows:**
   - a Standard/full-round shift reports attacks `for N rd from next round`; a native free shift does not;
   - a `form appraisal now X (was Y, round R)` row appears when the value changes during the fight.
4. **Aspects** stay on after the shift (the buff remains, and the decision header keeps its bonus).
5. **Shifter's Rush**, if owned and activated: the shift is free, the temporary Rush buff is consumed, and the remaining
   normal action is available. Compare its attack horizon with the same form's Standard-action shift.
