# G2: the engine decides when to grab and when to let go (15 November 2026, Claude)

The author has been switching Grabber on and off by hand. They asked for the tactical engine to decide when to use it,
when not to, and when to end a grapple, and supplied a short guide of principles (summarised below). This page records
the native mechanics, the decisions the engine now makes, how they follow the guide, and what to test. Receipt:
[G2_GRABBER_CHECKS.json](competence/G2_GRABBER_CHECKS.json); `docs/TACTICAL_AI.md` §173. G1 (the hold's value) is in
[GRAPPLE_G1.md](GRAPPLE_G1.md).

## Native mechanics (decompiled game code)

- **Grabber toggles.** There are six on the exports: Griffon, Bear, Lizard, Tiger (Shifter forms), and the Smilodon
  and Shambling Mound pets. Each has `ActivationType Immediately`, `DeactivateImmediately`, no group, no unit command
  and no resource.
  - `ActivatableAbility.SetIsOn(true)` calls `OnDidTurnOn`, which starts the buff at once (`TryStart`).
  - `TurnOffImmediately` stops it at once.
  - So switching is free and instant in both modes. It is exactly what the engine already does for attack modes
    (Power Attack and the others) in `ApplyModes`.
- **Grabber buffs.** Each buff carries only grab triggers (the G1 shape). Tiger and Smilodon have two: claw and bite.
- **Release Grapple.** One per form: a free action on self whose only action is `ContextActionReleaseGrapple`.
  - `UnitHelper.ReleaseGrapple` removes both grapple parts.
  - The holder part's `OnDispose` removes `CantMove`, `CantUseStandardActions` and the attack-of-opportunity block.
  - So after letting go, the holder can act normally in the same turn.
- **While holding.** The holder buff's grapple bonus (`CMBBonusForManeuver`, +5 for the Griffon) acts on the maneuver
  roll, not on the CMB calculation, so it is added by hand to the maintenance chance. Its Dexterity −4 is a live stat
  modifier, so it is not added again to the victim's escape chance.
- **Immunity.** A target's `ManeuverImmunity` (swarms, Freedom of Movement) makes the grapple fail (fusion2g1b
  review, §172). Such a target is never valued as grappled.

## What the engine decides

1. **Grab or not, for each weapon attack (both modes).**
   - The Grabber is an attack mode of its own kind (`ModeKind.Grab`).
   - Each attack's mode combinations are priced twice: once with the grab assumed on, once off (`CombatMath.AssumeGrab`,
     a scope that changes nothing in the game).
   - With the grab on, G1's model applies: the routine is cut at the expected grapple, and the hold is valued.
   - The best combination wins, through the existing rules:
     - a change must clearly pay (hysteresis);
     - at most one switch of a toggle per round;
     - the blocklist;
     - a toggle you click during a fight stays yours for that fight.
   - The label shows the choice, for example `FullAttack(4)->Cultist Conjurer [Grabber on +6.2]`.
2. **Let go or keep (both modes).**
   - While the unit holds someone, Release Grapple is scored by `ValueModel.GrappleKeepValue` from the live hold
     (`CombatMath.ReadHold`).
   - Keeping is worth this round's turn the victim loses (held, or pinned) minus the holder's own lost turn, plus each
     later round only while it pays.
   - The holder lets go when keeping is clearly negative (below −0.5). It is never a fallback.
   - One `[Grapple] X: keeps …` or `lets go of …` row per fight, pair and verdict gives the numbers.
3. **The grab and the release agree.**
   - A holder that can let go values a grab with only the rounds that pay (`GrappleHoldValue(..., canRelease)`).
   - So it grabs a target worth one held turn without assuming it must keep holding a weak victim.

## How this follows the author's guide

| Guide | In the engine |
|---|---|
| Use it against spellcasters and caster demons | The victim's side is its control worth (its threat, casters included). Pinned victims lose standard actions natively, so they cannot cast. The concentration check itself is not modelled. |
| Deny actions, control high-value targets | Held turn at the soft share, pinned turns at the hard share, weighted by how long the hold lasts (maintain × not escaping). |
| High-CMB characters or pets | The grab and maintain chances come from the game's CMB against CMD, with the holder buff's bonus. |
| Free or sustained damage | The free damage-only natural attack each maintained round. |
| Release freely | Release Grapple is free; the engine lets go when a round no longer pays, then acts in the same turn. |
| Not against enormous, high-CMD bosses | A low grab chance gives a small hold value. A grab that would only cost the routine is switched off. |
| Not when standard-action restrictions hurt | The cut-short routine and the holder's lost turns are priced. A full attack worth more than the hold keeps the Grabber off. |

## Limits

- **The holder's lost turn** uses the engine's generic worth per round for its level, as G1 does, not its actual best
  alternative this turn.
- **A Death from Above leap** originally kept its grab fixed. The [cached mode follow-up](LEAP_GRAB_MODES.md) now
  compares it through the shared ordinary grapple kernel, without another native own-weapon quote. It is unplayed.
- **Danger to a holder** that cannot move is not priced.
- **A party member held by a monster** gets no special handling (G1's limit).
- **Grab mode with other modes.** Grabber takes part in the existing 32-set mode budget; a unit with many attack modes
  may leave some groups as they are, as before.
- **Unplayed.** The offline checks cover the native contracts, the real blueprints and the compiled routes. They run no
  actor, roll or command.

## Replay (g2 package)

1. **Hands off.** Leave Grabber as it is at the start of a fight and do not click it during the fight. A click during a
   fight hands that toggle back to you for the rest of it.
2. **Mixed targets.** Against a caster or a dangerous melee enemy and a summoned wolf or other weak target, in both
   modes:
   - attacks on the dangerous target should show `[Grabber on +X]`;
   - attacks on the weak one `[Grabber off +X]` (when it was on).
3. **Letting go.** Hold a weak target: the log should show `[Grapple] Wenduag: lets go of <victim> …`, then
   `Release Grapple->Wenduag`, then a normal attack in the same turn. A pinned caster should show `keeps …` and no
   release.
4. **A swarm or a Freedom of Movement target:** no `[Grapple]` row and no Grabber switch for it.
5. **After the fight**, Grabber returns to the state it had before the fight.
