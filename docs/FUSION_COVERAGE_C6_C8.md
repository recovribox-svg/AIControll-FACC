# Fusion coverage C6 and C8: carrier buffs, and Hit Dice pools that kill

21 November 2026, Claude. The author asked for the C6 carriers and then the shared Hit Dice gate, unless a practical reason
prevents a high-quality implementation, and invited fixes to any limit worth improving. Both modes. Not played.

**Package:** [coverage7](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage7-1ae78e7-UMM-test.zip)
- startup MVID `0aa40527-d88f-4dfb-977b-3c6b101daaee`;
- UMM 0.3.60;
- checks: [COVERAGE7_CHECKS.json](competence/COVERAGE7_CHECKS.json);
- compiler and references: [COVERAGE7_BUILD_PROVENANCE.json](competence/COVERAGE7_BUILD_PROVENANCE.json).

It is Codex's review package (PR #100: corrections to #92–99) plus these changes, on `claude/fusion-coverage-c6-c8`,
stacked on `codex/review-92-99` (draft PR #100). Nothing is merged. The review and how the two meet are in
[the section below](#review-of-pr-100-and-the-merge).

## Result

15,494 configurations:

| | coverage6 and PR #100 | after C6 | coverage7 (C6 + C8) |
| --- | --- | --- | --- |
| Offered | 13,197 | 14,329 | **14,789** |
| Offered with the joint value | 8,635 | 12,927 | **13,387** |
| Lost | — | 0 | 0 |

PR #100 changes no configuration's admission: it reproduces 13,197 and 8,635 on these inputs.

705 stay withheld:
- 492 by the shared Hit Dice gate:
  - 234 are pools in a cone or a line. They stay withheld for a practical reason (below).
  - 78 are pools in a touch slot or with an extra area. Touch slots have no pool of their own, so the pool spell does
    nothing there.
  - 180 are refused by the other spell: Baleful Polymorph's removal, Phantasmal Killer's kill, Curse of Magic Negation,
    two pool spells together, a projectile delivery, a dispel after the cast's own buff.
- 213 by the outcome gate:
  - Phantasmal Killer's kill (64) and Curse of Magic Negation (64).
  - Baleful Polymorph's removal (56) and a dispel after the cast's own buff (28).
  - One guard fact that an earlier application changes.

## C6: carrier buffs, read by mechanism (both modes; `CombatMath.Carriers.cs`)

**Before.** Eleven buffs that spells put on their target were read as "not harmful". A fused cast carrying one was kept
off the joint value, and an ordinary spell's carrier was guessed or left unpriced.

**Change.** Each buff is read from its components:
- **Damage carriers:** Acidic Spray, Serenity, Touch of Slime and Excruciating Deformation.
  - Their damage is priced at its own time: when it lands, each round, when it ends, or when the bearer attacks.
  - Ability damage counts by its HP equivalent. Serenity's damage is weighted by the chance its bearer attacks.
  - They join the damage-over-time pricing and count as harmful (cleansable, aimed at enemies).
- **Hindering setup debuffs:** the Peer Through spells. Admitted, with no invented credit.
- **Reduce Person:** its size and stat changes earn no credit.
- **Enervation on an undead:** the temporary HP it gives are damage the target will not take.
- **Phantasmal Putrefaction:** its nausea is priced by its two round saves (`ValueModel.TickControlRounds`).

Ordinary spells gain this too. A carrier behind a branch of the root save is priced by that branch's chance
(`OrdinaryPeriodicLater`, 10 abilities). Serenity, Touch of Slime and Excruciating Deformation are no longer guessed.

**Not read:** Curse of Magic Negation. Its spell resistance also stands against the party's own later spells on that enemy,
so crediting it would mislead.

## C8: Hit Dice pools that kill or deal damage (both modes; `SharedHdControl`)

**Native contract (verified in the supplied assembly).**
- Circle of Death rolls a pool of d4 per caster level (up to 20, 25 with Intensified), with Maximize and Empower
  (`ContextValueHelper.CalculateDiceValue`).
- A recipient qualifies while its character level is at most the pool left and it is not undead or a construct. It then
  rolls a Fortitude save:
  - on a failure it is killed at 9 Hit Dice or less, else takes 100 damage;
  - either way the pool loses its character level (`SubHD`).
- The area spreads at 10 ft a second. Each recipient is reached once the front has travelled its 3D distance from the
  clicked point (`AbilityApproachingEffect.TickApproaching`); those reached in the same frame follow the game's unit
  order. So the pool is spent in distance order.
- It targets everyone, allies and the caster included, unless the cast is Selective.

**Before.**
- The ordinary spell priced every enemy as if the pool covered it, checking the pool only at its lowest, highest and
  average values, and in no order.
- Allies were priced the same way. The permanent-death rule counted the kill for an ally of any level.
- The fused spell was withheld (528 configurations).

**Change.**
- **Pool forecast.**
  - The forecast reads a save outcome that kills or deals damage.
  - It covers the pool's exact rolled distribution in one pass.
  - A spreading area is read in distance order.
  - Each recipient's conditions are read once per pool interval, not once per pool value.
- **Ordinary casts.** Each recipient's kill or damage is priced as if covered, then weighted by the chance the pool still
  covers it when the spread reaches it.
- **Fused casts.** The joint value is priced with the pooled payload and without it, mixed by that chance. Whether the
  pool covers a recipient does not depend on that recipient's own save, so the mixture is exact. A pooled kill is certain
  on its outcome: the HP left and the target's whole worth, and no control after it.
- **Friendly fire.** An ally's damage is weighted by the same chance. An ally the forecast did not select (walking in)
  counts as covered.
- **Permanent-death rule** (a limit fix, both modes, every spell; merged with PR #100's corrections).
  - A kill is ruled out for an ally only by its own Hit Dice test, since character level cannot change during the cast.
    A test that sits in an AND beside other conditions still counts when it alone is false. Any other condition can be
    changed by an earlier part of the same cast (a knock-out, a granted fact), so it never rules a kill out.
  - With no dust gate and every kill ruled out, the cast cannot kill that ally for good. Circle of Death no longer
    vetoes the cast against an ally above 9 Hit Dice; that ally's 100 damage is priced as friendly fire.
  - Otherwise PR #100's bound stands: Circle of Death's Fortitude save is nested, so an ally it could kill has the
    delivery chance as its risk. The pool's reach lowers it: the risk is mixed with the pool covering the ally and
    without it (a fused cast's other spell can still kill either way).
- **Also covered.** Sleep and Rainbow Pattern delivered by a spreading area. Ordinary Undeath to Death (the same pattern
  for undead) and the other rolled pools now use the exact distribution.

**Left, for a practical reason: pools in a cone or a line (234).** A cone or line takes, each frame, every unit its
projectile has passed, in the game's own unit-list order (`AbilityDeliverProjectile.StepBasedDeliver`). The projectile
covers about half a metre a frame, and one stutter frame can cover the whole cone. So which recipient the pool covers
first depends on frame timing, not on where the units stand, and a forecast from positions would be wrong in exactly the
crowded cases where the pool runs out. Their pool is the slot's 2d6 Hit Dice, so the value lost is small.

## Review of PR #100 and the merge

Codex reviewed #92–99 while this slice was being built, and published corrections in draft PR #100. I read the review
against the code and the decompiled game, and agree with its findings. Checked natively:
- Targeted Dispel Magic reuses one d20 for every buff (`OneRollForAll`, `SetRollOverride`).
- The buff list is sorted unstably by caster level before filtering.
- A filtered buff never rolls.
- The permanent-death risk applied the save twice, through `EffectChance`. That was my C3 code.
- An unread dice group was skipped, which lowered the risk. It now counts as reaching the gate.

The one I would revisit with evidence is a precision limit Codex already names: dust damage that the game can modify
(Disintegrate's) counts as certain to reach its gate, so a fused Disintegrate area is refused near any ally.

Merging the two branches needed these decisions:
- **Joint value.** PR #100 keeps drain utility out of an HP knock-out; C8 makes a pooled kill certain on its outcome.
  Both hold. The temporary HP a cast gives (C6) is taken off the HP damage too.
- **Periodic damage.** PR #100's unknown-harm sentinel also covers C6's carriers behind a save branch.
- **Dominated allies.** PR #100 adds their damage over time to the cost; C8 weights it by the pool's reach.
- **Delivery chance.** Both branches added a `DeliveryChance`. Mine included the shadow share, and a four-argument call
  would have bound PR #100's death risk to it, against PR #100's choice to leave shadow reality out of that bound. Only
  PR #100's remains; C6 applies the shadow share at its own call.
- **Death scan.** PR #100's depth guard added a kill without an action, which would have shifted C8's list of kill
  actions; it now adds an empty entry.

Against PR #100's head on the same inputs, every audit line is identical apart from those C6 and C8 change.

## Checks

On `1ae78e7`:
- Release build: 0 warnings.
- Tests: 1,101 unit (PR #100's 1,096 + 5) + 27/21/3 script tests.
- ProfileAudit exit 0. Against PR #100's head on the same inputs:
  - new `HD-POOL` 26 checks (native contracts, actual graphs, fused admission, formulas, compiled routes including the
    merged permanent-death rule, a census of ordinary pools: Circle of Death, Undeath to Death, Sleep, Deep Slumber,
    Rainbow Pattern and two Kitsune variants);
  - new `CARRIERS` 29 checks (the eleven buffs and their kinds);
  - `PERIODIC` 8 → 9 (ordinary abilities with damage over time 6 → 8), `FUSION-DOT` 52 → 53;
  - `FUSION-OUTCOME` 33 → 32: its two carrier gates (Reduce Person, Enervation) became one (Curse of Magic Negation),
    since the other two are read now;
  - route checks in `FUSION-DOT`, `FUSION-OUTCOME`, `FUSION-VALUE` and `CARRIERS` follow the joint value to
    `TryFusionValuesOf`;
  - summon detail: 2 creature-ability pairs move from "read with a guessed part" to "read" (C6); C8 changes nothing there;
  - every other line is identical, apart from the coverage line.
- Native harnesses: 98/14/10.
- 7 game references added relative to PR #100, all compiled against the supplied assembly: the carrier readers' fields
  and an `ActionList` (executed by `CARRIERS` on every loaded buff), and `ConditionsChecker`'s constructor (one Hit
  Dice test read alone).

## Replay (coverage7)

Record the startup MVID `0aa40527`. Send `Log.txt` and `combatLog.txt`, and say where you spawned enemies, rested, or
gave manual orders.

1. **Circle of Death, ordinary.**
   - Against a group of weak enemies it is offered and valued by the enemies the pool can cover, nearest first.
   - It should be refused (or priced down) when allies stand inside the area and the pool could still reach them. Look
     for "catches …" or "friendly fire".
   - It should no longer be refused only because an ally above 9 Hit Dice is in the area ("could kill … for good"). An ally
     at 9 Hit Dice or less that the pool can still reach does refuse it: that row reads "risk bound".
2. **A fused Circle of Death** (for example with Hold Person or Phantasmal Web): offered, with a `[FusionValue]` row that
   shows the pool's chance for its first recipient.
3. **The carrier spells:** Serenity, Touch of Slime, Acidic Spray and Phantasmal Putrefaction, ordinary or fused. Their
   damage over time should appear in the decision rows; Reduce Person and the Peer Through spells earn no credit by
   themselves.

## Limits

- Pools in cones and lines stay withheld (above). Pools behind a projectile delivery (Fireball) keep the earlier refusal.
- In real time units move during the 4-second spread; the forecast uses where they stand now.
- Recipients that a spread reaches in the same frame follow the game's unit order; the forecast uses distance order for
  them too (a gap below about 5 cm at normal frame rates).
- Phantasmal Killer's kill behind its own nested save is not in the joint value: its not-killed branch needs the damage
  split by that save.
- Baleful Polymorph's removal and form, and Curse of Magic Negation, stay as before.
- Death risk for an ally Circle of Death could kill is PR #100's upper bound (its nested save is not priced), times the
  pool's reach. Pricing that Fortitude save exactly would lower it.
- PR #100's own limits (its review's last section) apply to this package too.
