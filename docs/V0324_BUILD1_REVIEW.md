# v0.3.24 build 1: v0.3.23 log review and Psionics (1 November 2026, Claude)

Inputs supplied by the author:
- `CompanionAI_Pathfinder_v0.3.23_Turn_Based_Mode_log.txt` (14 fights), `..._Real_Time_Mode_log.txt` (13 fights) and the
  game's `combatLog.txt`;
- `Psionics_mod__AI_control_feedback.txt` (a player's report);
- the Psionics mod 0.2.3 ZIP (`Psionics.dll` and blueprint lists).

## Which DLL these logs ran

**Not the released v0.3.23.** The turn-based log's startup line is `[Build] v0.3.23 module=89ffb864-...`: the cabdb2e
build (played as build5). The release is `module=09a7ab12-...`. The real-time log has no startup line, but it values
every new summon at full 18.0 while Orvath's Abrikandilu fights; the release would discount it. So everything v0.3.23
fixed (the 5-foot step for held-back spells, sleepers far from a Coup de Grace ally, summon after summon) shows up
again in these logs and is not counted below. All 7 turn-based steps had spells held back in the same decision.

Please check the installed `Mods\CompanionAI_Pathfinder\CompanionAI_Pathfinder.dll`: UMM now shows **0.3.24** for
this build, and its startup line is in the identity file.

## 1. Psionics: Psychic Warrior spends its focus for a 1-round buff - fixed

**The report.** "The AI loves to constantly waste the focus for little gain"; stances and maneuvers "expand the
passive focus for a short buff ... mostly good for a burst against bosses".

**How the mod works (from `Psionics.dll`, decompiled; the blueprint lists only give names and GUIDs).**
- Psionic focus is a permanent buff (`PsionicFocusBuff`). It is gained with a standard action (a move action with a
  feat).
- While it is held, a trance can be on: a stance toggle that requires the focus and takes a standard action. The
  Weaponmaster trance, for example, gives +1 to +5 attack for the whole fight.
- Many feats work only while it is held: Psionic Weapon / Shot / Fist add 1d6 to 4d6 per hit, Psionic Dodge gives +1
  AC, and so on.
- When the focus buff goes, its own actions end the trance and the focus-only buffs.
- A maneuver (and Wounding Attack, Psychic Strike's recharge, ...) is a swift action that removes the focus and gives a
  1-round buff (Weaponmaster: +2 dodge AC and +2 to the next attack).

**Cause.** The AI valued the 1-round buff only. Removing one of the caster's own good buffs was compared by that buff's
per-round "quality"; the focus buff has no stats of its own, so spending it looked free. Neither the trance it ends nor
the two standard actions it takes to get both back were counted.

**Fix (general, recognised by structure, not by name).** A **held state** is a lasting good buff on the caster that the
ability takes off without putting a lasting effect in its place (so not a mode switch such as a mutagen or a wild
shape), and that something else of the unit depends on:
- its removal ends other buffs the unit has, or
- another ability or toggle of the unit requires it.

Spending one now costs:
- what getting it back costs: the unit's own ability that re-applies it, plus the commands of the toggles that ended
  with it, at one round of the unit's worth per standard action (a move action half, a swift a quarter);
- plus the readable worth of what ends with it,
- for the fight left after the gained effect (`ValueModel.SpentStateCost`, unit-tested).

**Effect.** At level 5 with focus and a trance up, a maneuver costs about 38 points against a gain of a few, so it is
not used; in the last round of a fight (nothing left to protect) it can still be. The decision log shows
`spends Psionic Focus (ends Weaponmaster Trance) -38`.

**Not verified in play.** No log with a psionic character was supplied. Two limits:
- The AI does not re-gain focus or switch a trance on by itself: those effects are not readable as values. Focus kept
  from before the fight stays up, which is the point of the fix.
- A real boss "burst" is not singled out; the rule allows it only when the fight is nearly over.

**Were the blueprints useful?** The `.md` lists gave names and GUIDs only; the mechanics came from decompiling
`Psionics.dll`, which builds its blueprints in code. For mods like that, the DLL is what helps. For mods that ship real
blueprint JSON (exports with components), those help directly with the offline ProfileAudit. A note on what a player
saw (which ability, which character, ideally a log) helps most.

## 2. Archers held their arrows at sleepers in Grease or Entangle - fixed

**What happened.**
- **Turn-based fight 6, R7-R10.** The last enemy, a Deskari Cultist, was asleep (combat log: Will save failed against
  Orvath's Sleep) and lay in Grease. Seelah and Camellia walked in, fell prone and were interrupted. Lann and Aeiluvetar
  ended four turns each with every action unused.
- **Fight 3, R8-R9.** The same at a sleeping Neophyte Fighter in Orvath's Entangle.

**Cause.** A Coup de Grace ally counted if it was within its walk (v0.3.23), even when the walk led into a harmful area.

**Fix.** It must also reach the sleeper without walking into a harmful area (`AoeDangerAnalyzer.EnteringDanger`, the
check the AI already uses for its own walks). An own area made Selective is no obstacle: that check reads the game's
record of whether the live area affects allies. Otherwise the archers shoot the sleeper at half value, as before.

## 3. Treat Affliction cast on anyone with any harmful buff - fixed

**What happened.** Treat Affliction showed up at 2.0 on Seelah, Orvath, Aeiluvetar and a summoned Abrikandilu. Aeiluvetar
spent turn-based fight 13, R4 on it (on Orvath).

**Cause.** The "something to remove" check for dispel and removal abilities counted any harmful buff on an ally, not
one the ability can take off.

**Fix.** Only a buff the ability's actions can remove counts: for a dispel, the game's own `ContextActionDispelMagic`
filters (dispellable, visible, from spells or not, schools and descriptor); for a removal, the named buffs or
descriptors.

## Looked at, no change

- **Real time:** 154 waits, none while an option worth more than 2 existed. Five stuck commands were cancelled by the
  existing safety net (paths and line of sight).
- **Turn-based "nothing worth doing" (4):** Orvath entangled with no way out, and Lann with the last enemy in
  Entangle.

## Checks run

- Release build against the game's references: 0 warnings, 0 errors.
- Unit tests: 258 passed (new: `SpentStateCost`).
- SigDump against the released v0.3.23 DLL: 10 read-only game members added (dispel filters, `RestrictionHasFact`,
  `AbilityCasterHasFacts.Facts`, `Buff` flags); none removed.
- Not yet run in the game.

## Test build

`test-builds/AI-Control-FACC-v0.3.24-dev-build1-432a219-UMM-test.zip`, UMM **0.3.24**, startup
`[Build] v0.3.24 module=05fff211-2d9a-452c-bce0-b988c4840974`.

**What to play.**
1. A Psychic Warrior with focus and a trance: maneuvers are used rarely (end of fights), focus and trance stay up.
2. Sleeping enemies in Grease or Entangle at the end of a fight: the archers shoot.
3. Treat Affliction only on an ally with something it can remove (its blueprint was not in the exports, so which
   descriptors it filters on is read in the game, not assumed here).
