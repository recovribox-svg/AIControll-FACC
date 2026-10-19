# Plan from the 0.3.14-test4 turn-based log (67e90edc) and your observations

Status: implemented in 0.3.14-test5 together with the real-time log findings (see `docs/TACTICAL_AI.md` §21).

## What worked
- **Standing up from prone.** 5 stand-ups, all worked: the game stood the unit up and the next decision ran.
- **Leaving harmful areas.** 12 "Leave Stinking Cloud" moves, mostly sensible (Orvath, Lann, Seelah, Camellia,
  Aeiluvetar walked out of the cloud at the start of their turns).
- **Status in the log.** `{nauseated}` and `{blindness}` now appear on decision lines.
- **No errors or exceptions.**

## Findings and planned fixes, by impact

### 1. Camellia's charge past the Large Water Elemental (your observation 5)
**What happened.** At line 14291: `Charge->Wandering Necromancer=6.3`, with no `[AoO]` tag. The elemental hit her as
she ran past, and she went down in one hit.

**Cause.** The AoO check only asks about enemies *already engaging* the mover, and only about the destination. Camellia
started out of anyone's reach, so the elemental, whose reach her path crossed, was never considered.

The game rule (`EngagementSimulator`, the code behind its own path preview and native AI): walking along a path, every
enemy whose reach the unit *enters and then leaves* gets an attack of opportunity.

**Fix (both modes).**
- Walk the planned path (a straight line for charges and approaches, the real path when one is known) through the
  game's own `EngagementSimulator` (an internal class, so called by reflection). If that's unavailable, fall back to
  the same rule using `IsReach`, `ShouldAttackOnDisengage` and `AttackOfOpportunity(simulate)`.
- Price each attack by its chance to **drop** the unit, not only its expected damage: hit chance × P(damage ≥ HP),
  with dice spread and crits. A big hitter against a medium-HP character then costs about "this character is out of the
  fight", which is what happened.
- The cost becomes: P(dropped now) × the unit's worth + the expected damage cost if it survives.
- Charge, Move+Attack, move-then-cast, Advance, Withdraw and Leave-area all use this one check.

### 2. Nauseated companions in turn-based lose their turns inside the cloud
**What happened.** At lines 7756 and 7823: `Aeiluvetar: cannot act this turn (nauseated)`, two rounds in a row, inside
the Stinking Cloud.

**Cause.** The nausea animation puts the unit in an "exclusive" animation state, so `CanAct` reads false, but
`CanMove` stays true. The game's own test for "may still act this turn" is `IsAbleToAct = CanActInTurnBased ||
CanMove`. The game waits for an order, and a walk order starts regardless of `CanAct`. The mod ended the turn instead.

**Fix.**
- Turn-based ends a turn as "cannot act" only when the game's `IsAbleToAct()` is false.
- When `CanAct` is false but the unit can walk, the turn is decided with movement only: leave the area, withdraw.
  `ActionPermissions.MovementOnly` covers this case as well as Nauseated.

### 3. "Leave area" picked a spot still inside, then walked again (Orvath, lines 7684–7700)
**What happened.** Three leave moves in one turn (3.3 m, then 1.6 m, then 1.0 m).

**Cause.** The mod tested "outside" as centre distance > radius. The game tests the unit's *body*:
`AreaEffectView.Contains(unit) = Shape.Contains(position, corpulence)`.

**Fix.** Use the game's shape test with the unit's corpulence (`View.Shape.Contains(point, corpulence)`) for both
leaving and entering. This is also more exact for non-round areas.

### 4. Leaving an area where it provokes (your observation 1)
**What happened.** At line 1707: Camellia, not nauseated, took `Leave Stinking Cloud [AoO 2]=3.1`. The first attack
stopped the move after 1.7 m, so she stayed in the cloud and took the hits.

**Current calculus.** Area danger × ½ × worth, minus AoO cost. It is missing:
1. **Whether the move gets out at all.** A hit stops the move, but the stop chance isn't used for "Leave" (Charge and
   Move+Attack already use it). With 2 attacks the stop chance is about 70–80 %.
2. **Whether staying would actually hurt.** Use the area's own save DC (`area.Context.Params.DC`) against the unit's
   save of that type (the spawning ability's profile names the save), immunity to what the area applies (condition and
   descriptor immunity), and spell resistance. A cleric with a good Fortitude save rarely fails.
3. **How long the area lasts.** Its remaining duration: none left at the start of the next round means no gain.
4. **Cheaper ways out.** An exit within the 5-foot step provokes nothing; it is already preferred, and stays so.

**New value:**
`P(move completes) × P(affected per round) × cost per round × rounds left (≤ remaining duration) − cost of the attacks`

The cost per round is the unit's per-round worth for hard control (nausea, stun), or the area's damage. Staying is then
compared with acting on the same scale, so a unit leaves when it should and stays when the attacks cost more.

### 5. Downed ally healed, stands up next to an enemy, dropped again (your observation 2)
**What happened.** Lines 2549, 2985 and 5919: Seelah went down, was healed to about 4–6 HP ("needs 3, heals ~7",
"needs 1, heals ~6"), stood up next to an enemy ("provokes up to 1/3"), and was dropped by the attack of opportunity.
Her turn ended right after standing in all three cases.

**Is "stay down until no enemy is adjacent" a good idea?** Not as a fixed rule:
- Lying down loses the whole turn.
- Enemies get +4 to hit a prone character in melee.
- An adjacent enemy usually doesn't leave.

So staying down mostly postpones the same problem and adds the +4. What is wrong is the *calculus* on both sides.

**Fix A, healer side (the main fix).** A revive is worth "the ally is back in the fight" × the chance it *stays* up:
- its HP after the heal, against the AoO it will take standing up (from the enemies engaging it), plus what can reach
  it before its turn;
- the same drop model as item 1.

With 4 HP next to a big hitter that chance is small. Then a bigger heal (Cure Moderate potion), killing or moving the
adjacent enemy first, or healing a little more before it stands, wins naturally. This is your second idea, done by
value rather than by a rule.

**Fix B, the downed ally's own choice.** At its turn, compare standing with staying down, using the same drop model:
- Standing: P(dropped by the AoO); otherwise its best action after standing, which the engine evaluates as if standing
  (the same toggle the game's own `IsAbleToAct` uses). A self-heal potion after standing is often the best answer.
- Staying: the enemies' attacks at +4 before the next turn.

It stays down only when standing is worse, e.g. 3 HP, two adjacent enemies, nothing to drink. It then usually gets a
heal from a companion and stands the following turn. The log says why: "stays down: standing provokes 2 (65 % to drop
her), best action after standing worth 4".

### 6. Fire Blast against the Quasit (your observation 4)
31 Fire Blasts were cast on Quasits, valued the same as against unprotected targets (14.0 against 14.4). Two causes,
both fixable:

1. **Missed immunity component.** Energy immunity was read only from `UnitPartDamageReduction.IsImmune`, which knows
   `AddEnergyImmunity`. The game's other immunity component, `AddEnergyDamageImmunity`, never registers there; the
   game's own character sheet reads both (`UnitDescriptionHelper.VisitComponents`). Quasits are fire-immune in the
   tabletop rules, so this is very likely the miss.
   - **Fix:** read both components, as the character sheet does.
2. **Two damage branches added together.** `[Profile] Fire Blast: branch condition IsEqual not evaluated - both
   branches counted`. Fire Blast's damage sits in two mutually exclusive branches, and both were added: an
   overestimate of about 2× against **every** target.
   - **Fix:** effects in opposite branches of the same unevaluated condition count as alternatives (the larger one),
     not as a sum. This applies to damage and heals.
3. **Safety net: learn from results.** Record, from the game's damage event (`RuleDealDamage`, global handler), what
   each target actually took from each damage type: immune, flat reduction, or multiplier. After the first Fire Blast
   that does nothing, the AI knows it, whatever the reason (a mod, an odd component, a buff). Per target, with the
   target's blueprint as a fallback for others of the same kind; reset each fight.

### 7. Seelah ended two full turns with no options at all (lines 1683 and 1778)
**What happened.** `Seelah R2 [SMWF5] enemies=2 :: => END TURN`, with no attack, no Move+Attack, no Advance.

**Likely cause.**
- `AddWeaponCandidates` does `if (single == null) return;`. When the game's attack list is empty for *one* enemy, all
  weapon options for *every* enemy are dropped. The comment assumes "cannot attack at all".
- With no weapon option, the Advance check also has nothing to go by.

**Fix.**
- Skip that enemy only.
- Log the reason when a unit with its actions unused has no option at all (no weapon attack against N enemies and why,
  M abilities considered).

### 8. Aid Another (your observation 3)
Used correctly:
- **Duplicates.** The same Aid Another is never stacked while the enemy still has it (`HasFact` check). Repeats in the
  Cultist Champion fight came after the Champion had used up the previous one: Distracted Attacks ends with its next
  attack, Distracted Defending when it is attacked.
- **Why so much Aid Another there (R6–R14).** Against that Champion the party's weapon attacks were valued 0.3–2 (a
  very high AC, e.g. touch AC 19), so a sure +2 for the next attack or −2 for its attack was worth more than a likely
  miss. That is a sensible choice.

Optional refinement, low priority: value Distracted Defending by the hit-chance gain of the ally attack that will
actually use it, and Distracted Attacks by the enemy's expected next attack, instead of a share of threat.

## Order and risk
- **First:** 2 (lost turns), 1 (path AoO + drop chance), 6 (damage estimates), 3 (area geometry), 7 (weapon
  candidates). These are clear defects, each with a contained fix.
- **Then:** 4 (leave-area calculus) and 5 (revive survival + stand-up choice). These are scoring changes that reuse the
  drop model from 1.
- **Real time:** 1, 3, 4, 6 and 7 act in real time too (same engine), and 6 includes a new event handler, so the
  real-time log will be checked against them before anything is built.
