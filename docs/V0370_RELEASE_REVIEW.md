# v0.3.70 release playtest: review and fixes (28 November 2026, Claude)

The author played the stable v0.3.70 package (module `670ef6c7`) in one session: four turn-based fights, then five
real-time fights. The settings file was new, so every character's role and range came from the build suggestions. The
fixes are on `claude/v0370-release-review`, from development after the release. The next build is numbered **0.5.0**,
at the author's request. The previous review is [V0370_FEATURES6_REVIEW.md](V0370_FEATURES6_REVIEW.md).

## The logs

| Mode | Fights | Decisions | Commands |
| --- | --- | --- | --- |
| Turn-based | 4 | 374 | 219 issued |
| Real time | 5 | 1,950 | 705 issued: 520 succeeded, 43 interrupted after acting, 128 before acting, 2 failed |

- No AI Control exception.
- Party deaths:
  - turn-based fight 4: the Bandit, while held by Hold Person, Mass;
  - real-time fight 5: the Bandit Conjurer;
  - fight 6: Wenduag, arcan (held), the Bandit Conjurer and the Bandit;
  - fight 7: Wenduag;
  - fight 8: Camellia (a Nabasu's 2 negative levels), Wenduag and Aeiluvetar.
- The fights had up to 26 enemies; the necromancers summoned many Ferocious Skeletal Champions. CR 12 Skeletal
  Champion Guards dealt most of the killing blows.

## Fixes

1. **Both modes, grapples:** Wenduag let go of three holds right after making them.
   - In real-time fight 8 she full-attacked a Corrupted Jungle Bulette with Grabber on, priced "hold +61 if it starts". A grab
     stops the routine there.
   - 1.5 s later, her standard and move actions spent, the keep test read "keep −5.3" and she let go. It did the same
     with an Axiomite and a Vermlek.
   - The keep test charged the holder's lost turn, but releasing then gives nothing back this round. The victim's held
     turn, the one the grab was priced for, had not come yet.
   - Now the holder's turn counts only while its standard or move action is still to use: the game's own
     `HasStandardAction` / `HasMoveAction`, which read what is used, not the grapple's restriction
     (`ValueModel.GrappleKeepValue`, `holderTurnNow`). The row says "no action to regain this round".
   - The test runs again once the actions come back (real time: their cooldown; turn-based: the next turn).
2. **Both modes, a helpless ally's danger:** the game's `RuleCalculateAC` gives melee attackers +4 against a helpless
   target, as against a prone one. The drop model counted prone only, so a held ally was read as hit like a standing one
   by every melee enemy. It now adds both (`ValueModel.MeleeDefenceLoss`). The helpless AC penalty itself
   (`UnitStatusEffectHelpless`) was already in the AC read.
3. **Both modes, the holder's own danger (author request, review2):** Wenduag kept her hold on Rustclaw in fight 7
   (keep +28.5) while it clawed her from full HP to 11 %.
   - A hold keeps its holder where it stands: the game's `UnitPartGrappleInitiator` adds `CantMove` while it holds, so
     a badly hurt holder could not withdraw, and the keep value did not count that.
   - Now, when the holder is badly hurt, keeping the hold costs the withdrawal that letting go would allow
     (`ValueModel.GrappleKeepWhileStuck`). The withdrawal is priced by the withdraw option's own code (`WithdrawWorth`,
     now shared by both): drop chance here against the retreat spot, the walk's attacks of opportunity and areas.
   - It counts only when the hold is all that stops the holder walking (`CombatMath.WalksOnRelease`). Another
     `CantMove`, a movement ban, being held itself, prone, dazed, stunned or overloaded means no withdrawal. The grab
     forms' six holder buffs add no `CantMove` of their own.
   - Turn-based, the holder needs its move action (Release Grapple is free). In real time, walking is free once the hold
     ends.
   - The `[Grapple]` row says "held where a withdrawal is worth N".
   - At full HP nothing changes: a healthy fighter's place is in melee, as for the withdraw option. In the log the
     Rustclaw hold was weighed once, at full HP. Wenduag had no other decision before she dropped, so this playtest
     cannot show the new rule acting.

## Seen working

- **Weapon sets:** the Bandit switched from his crossbow to his shortsword with a Bloodlusting Skeleton next to him, and
  Camellia to her crossbow when no enemy was reachable with her greataxe. "Against 0" now means the current weapon has no
  positive option this moment: the crossbow shots would provoke, or the greataxe could reach nobody.
- **Drained levels:** the header names them ("drained 4/10").
- **Release Grapple:** three releases in fight 8 when keeping stopped paying.
- **Bomb of Paralysis:** thrown by the Bandit, it caught 9 enemies at DC 35. Its option names the thrower because the bomb
  is aimed at its own spot; no ally was caught.
- **Performance:** the slowest real-time decision took 401 ms. The slowest turn-based one took 900 ms: the Knight
  Commander against 26 enemies, scoring 84 abilities.

## Observed, not changed

- **Hold Person, Mass** held five allies in fight 4 and seven in fight 6, for up to six rounds (Will DC 26; most
  of them save at +5 to +8).
  - Only the Knight Commander could free them, one at a time (Dispel Magic and Greater Dispel Magic, targeted). Freeing an
    ally is worth its output for the rounds it would stay held, about 40–60 points after the dispel's chance.
  - Chain Lightning on many summoned skeletons scored 380–640, so he cast it.
  - **Not changed (decided in review2): a held ally's added danger in the freeing value.** It would not change these
    decisions, and it would cost a second drop table per held ally.
    - The added danger is at most the ally's worth: 30–76 points for these characters. Freeing would rise from about
      40–60 to at most about 100–130, still far below the 380–640 of Chain Lightning on the summoned skeletons.
    - No enemy used a Coup de Grace on a held ally in this playtest.
    - Fix 2 already reads held allies as easier to hit in melee, which is what raises heals and protection on them.
    - Worth revisiting if a playtest shows a freeing dispel losing narrowly to a weak option while the held ally is
      in danger.
- **Healing:** Orvath drank Cure Critical Wounds four times while Camellia and Seelah handed him two more. He was at
  35–58 % of a large HP pool, so none was wasted.
- **Camellia** died at once to a Nabasu's drain (2 negative levels in one hit).

## Replay

1. A grab-capable form (Wenduag with Grabber) in real time against a strong melee enemy (review1 and review2). The `[Grapple]` row right after
   the grab should read "no action to regain this round" and keep the hold. It is weighed again when her actions come
   back.
2. The same in turn-based: no release on the turn of the grab; at the start of the next turn the hold is weighed with her
   turn.
3. A fight with Hold Person, Mass (or Sleep) on the party: melee enemies are read as hitting a held ally harder. Heals and
   protection on that ally should rank higher than before.
4. A grab held against an enemy that keeps hitting the holder: once the holder is below half HP, a `[Grapple]` row
   should name "held where a withdrawal is worth N". If N outweighs the hold, the holder lets go and withdraws.

Send `Log.txt` and `combatLog.txt`; the startup line is `[Build] v0.5.0 module=1a48f45f-a51a-4efa-b27e-657806359176`.
