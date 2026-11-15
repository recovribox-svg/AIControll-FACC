# G1: grapple state awareness

15 November 2026, Claude. Task branch `claude/grapple-awareness`, based on development `6b9be42` (PR #64 merged).
Inputs: the author's fusion1 logs (`Log 1 … Grapple-no fusion spells`, `Log 2 … Grapple-fusion spells`, `combatLog.txt`).
Their hashes are in [FUSION2_PLAYTEST_INPUTS.json](competence/FUSION2_PLAYTEST_INPUTS.json): the same three files.
Both AI logs identify MVID `2e2896c3` (fusion1, source `7a56bbb`). The author switched Grabber on by hand for Ulbrig
(Log 1) and Wenduag (Log 2) and left AI Control on for both. Raw logs are not committed.

## What the logs show

**Grapples.** The combat log has 18 grapple attempts by Ulbrig and Wenduag on their own claw hits: 17 succeeded,
1 failed. It also has 5 maintenance rolls (all kept the hold) and 6 escape attempts made as maneuvers (1 succeeded).
At least one more escape went through a skill check: the Hag's "Athletics check succeeded" ends the hold between the
two Hag grapples in fight 8. No grapple was released by command; Release Grapple is never offered ("effect not
understood", Log 1 rows 530/544).

**Attacks cut short.** A claw hit that grapples gives the attacker `CantUseStandardActions`, and the game's
`UnitAttack.TryStartNextAttack` then starts no further attack. Every grapple-starting routine ended `Interrupt`:

| Log row | Decision | What happened (combat log) |
|---|---|---|
| L1 821–823 | TB R2 Ulbrig `FullAttack(3)->Ghoul Stalker` = 118.7 | one claw hits, grapple; command ends Interrupt after 1.0 s |
| L1 933–936 | TB R3 Ulbrig `FullAttack(3)->Screaming Will-o'-Wisp+5ft` = 52.2 | one claw hits, grapple |
| L1 1576–1578 (fight 2) | TB R2 Ulbrig `FullAttack(3)->Fiendish Large Earth Elemental` = 56.0 | one claw hits, grapple, Interrupt after 1.0 s |
| L2 1837–1854 | RT2 Wenduag `FullAttack(4)->Bandit` = 78.3 | one claw hits, grapple, Interrupt after 1.5 s |
| L2 1962–1974, 2034–2042 | RT4/RT5 Wenduag `FullAttack(4)->Hag` | grapple each time, Interrupt after 1.5 s |
| L2 2088–2096 | RT7 Wenduag `FullAttack(5)->Fiendish Large Fire Elemental` = 47.5 | grapple, Interrupt after 1.2 s |

The engine priced each of these as the whole routine (three to five attacks). Of the 25 attack commands the two issued
while their Grabber was on, 16 ended Interrupt. The rows above are the ones matched to a grapple start in the combat
log; other interrupts include re-decided walks. Wenduag's completed full attacks at RT12–RT15 came after her form had
ended: Griffon Shape is offered again in those decisions.

**While holding.** AI Control already follows the holder's restrictions, because it reads the native conditions:
Wenduag RT6 (Log 2 row 2059) is `{cantmove, cantusestandardactions}` and uses a swift Shifter's Fury. No turn-based
turn started with a hold still in place: every victim died or escaped first. Nothing in these logs shows a hold that
should have been released.

## Native contracts (decompiled game, real blueprints)

- **Start.** `AddInitiatorAttackWithWeaponTrigger` (on hit, the claw's weapon type) → `ContextActionCombatManeuverCustom`
  Grapple (`RuleCombatManeuver`: natural 20 succeeds, natural 1 fails, d20 + CMB ≥ CMD, immunity, concealment) →
  `ContextActionGrapple`. It refuses a holder that already holds and a victim already held.
- **Holder.** `UnitPartGrappleInitiator.Init`: holder buff, `CantMove`, `CantUseStandardActions`, no attacks of opportunity.
  The Griffon's holder buff adds +5 CMB to grapple, -2 to attack rolls and -4 Dexterity.
- **Each round.** `ShifterGrabInitiatorBuff.HandleNewRound`, at the holder's own new round in turn-based play and every
  6 s in real time. The holder rolls the grapple again. Success pins the victim (`CantUseStandardActions`, AC -4) and
  queues `UnitGrappleInitiatorAttack`, a free damage-only natural attack. Failure releases the pair.
- **Escape.** `UnitGrappleController` makes the victim try `TryBreakFree` once a round by itself, without an action. With
  `BreakFreeFlags.Default` it uses a grapple maneuver when its CMB beats its Mobility and Athletics, and otherwise the
  better skill, in both cases against the holder's CMD. If either side moves more than 0.1 m, the hold ends. A dead
  or unconscious partner ends it too.
- **Release.** Release Grapple (`ContextActionReleaseGrapple`) is a free action.
- **Who grapples.** The same parts serve the Shifter Griffon/Bear/Lizard/Tiger grabs, the Smilodon companion, and the
  Shambling Mound, Mimic Ooze and Autumn Golem monsters.

## Change

1. **Grab reader** (`Tactics/CombatMath.Grapple.cs`). It recognises the inspected shape structurally on the attacker's
   active buffs: on hit; a weapon type, category or range filter; an optional caster-fact gate whose true branch is
   empty; a custom Grapple maneuver whose only success action is `ContextActionGrapple`; and a holder buff with the
   native maintenance. It reads the holder buff's simple grapple CMB bonus and Dexterity change. Any other flag, branch
   or action keeps the old forecast. On the real blueprints it reads 8 triggers: Griffon, Bear, Lizard, Tiger (two),
   Smilodon (two) and Shambling Mound. It refuses 5, all monsters: the Mimic Ooze (extra damage on success), the
   Autumn Golem and Fafnheir (a plain maneuver), and the Balor whip and Nightcrawler (the grapple behind another kind
   of branch). Only party units are modelled, so an enemy's routine keeps its old danger forecast.
2. **Attacks cut short** (`EstimateWeaponAttackCore`). Each attack counts by the chance the routine is still running
   when it comes: on a suitable weapon, (1 - hit × grapple success) per earlier attack. The grapple itself follows a
   hit, so the chance of at least one hit is unchanged. On-hit riders count the expected attacks.
   `ValueModel.GrappleRun`.
3. **The hold's value** (`TacticalEngine.GrappleHold`, `ValueModel.GrappleHoldValue`). It is added in the shared weapon
   value, so every weapon option in both modes uses it: full attack, attack, move and attack, Charge, Death from Above,
   mode variants. The value is the chance the grapple starts times the hold value, over the control horizon
   (min(expected rounds, 3)):
   - **Victim:** its control worth, at the soft share (0.4) on its first turn (held, not pinned) and the hard share (1.0)
     on each later turn it stays pinned.
   - **Holder:** on each of those rounds it loses its worth per round at the hard share, priced as when our own control
     catches an ally. It gains the free natural hit, valued as the mean one-hit damage of its natural weapons.
   - **Persistence:** each round the hold lasts with the chance the maintenance roll succeeds and the victim fails to
     escape. Maintenance uses the existing maneuver reading plus the holder buff's bonus. Escape uses the victim's CMB
     or better skill against the holder's CMD, lowered by the holder buff's Dexterity change.

   A dangerous victim makes the hold worth starting. A weak one makes it cost more than it gains: grappling a summoned
   dog with a strong holder comes out negative. The maneuver odds now share one CMB/CMD reading with the existing
   combat-maneuver options (`ManeuverValues`, `ManeuverConcealmentShare`): a refactor that keeps every value the same.
4. **State in the log.** The decision header names the pair the game keeps: "holds Ghoul Stalker", "held by Ulbrig
   (pinned)" (`GrappleState.Describe`). The first estimate per fight and pair prints one `[Grapple]` row with the
   chances and both sides of the hold.

Bounds: one buff scan per unit and frame, one CMB/CMD reading per pair and frame, no new candidate, command, path query
or actor/context cache. Nothing rolls, previews a maneuver, releases a pair or moves a unit.

## Not done, and why

- **Release Grapple.** No played case shows a hold that should have ended. A release decision needs the holder's
  options after release, which this engine prices only from the live state; faking the release state is what the
  native contract forbids. With the hold now valued, the next replay can show whether a negative hold is ever entered,
  or kept, against a better option. Proposed next slice: G2, release with a positive reason only, at most once a
  round, for a valid pair.
- **Party members held by monsters** (Shambling Mound and the others). Restrictions already come from native conditions.
  The victim's concentration check for spells (`ShifterGrabTargetBuff`: DC 10 + holder CMB + spell level) is not yet
  priced, and there is no played case.
- **Allies attacking a pinned victim.** The native AC and flat-footed rules already reach their hit chances. The extra
  worth of freeing our holder by killing its victim is not counted.
- The existing combat-maneuver forecast reads `RuleCalculateCMB`, so `CMBBonusForManeuver` bonuses on the attacker's
  other facts (they act on `RuleCombatManeuver`) are missed, as before. Only the holder buff's own bonus is added.

## Replay

Keep Grabber on, AI Control on, and supply the AI log and combat log with the startup MVID. In both modes:

1. A grab-capable form against a dangerous caster and against a weak summon. Expect `[Grapple]` rows with a positive
   hold for the first and a negative one for the second, and attack choices that follow.
2. A full attack that grapples on the first claw. The forecast attacks shrink in the `[Grapple]` row, and the command
   still ends Interrupt (native).
3. A turn that starts while holding: the header names "holds X". Only swift or free actions follow, and the turn ends
   without a move.
4. A party member held by a monster, if one appears: the header names "held by X (pinned)".
