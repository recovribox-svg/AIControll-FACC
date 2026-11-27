# v0.3.70 features5 playtest: review and fixes (27 November 2026, Claude)

The author played features5 (module `cfc0deda`) in one session: six turn-based fights, then seven real-time fights.
Source `dbdc867` on `claude/v0370-features5-review`, stacked on `claude/v0370-features3-review` (draft PR #119). Package
`test-builds/AI-Control-FACC-v0.3.70-dev-features6-dbdc867-UMM-test.zip`, module `73eef393-8d13-4837-85e7-df4922333fe7`,
UMM 0.3.70, **unplayed**. [Receipt](competence/V0370_FEATURES5_REVIEW_CHECKS.json). The previous review is
[V0370_FEATURES3_REVIEW.md](V0370_FEATURES3_REVIEW.md).

## The logs

| Mode | Fights | Decisions | Commands |
| --- | --- | --- | --- |
| Turn-based | 6 | 356 | 209 issued |
| Real time | 7 | 1,643 | 426 issued, 415 ended: 258 succeeded, 33 interrupted after acting, 122 before acting, 2 failed |

- No AI Control exception; the only errors are ModMenu's and VisualAdjustments2's.
- No party member died (all 214 deaths in the combat log are enemies).
- Enemy spawns came from RandomReinforcementsPerEncounter. Real-time fights 7 and 12 had 42 and 47 enemies.
- No ally was dominated this session.

## What the features5 changes did

- **Weapon sets (F2):**
  - The Bandit switched to his Light Crossbow +5 set twice in turn-based mode, as a move action before his attack
    (fight 1 round 2, fight 2 round 1). He kept the crossbow for the rest of the session; the combat log shows only
    crossbow attacks.
  - No switch back to melee was ever offered; his crossbow (+5, sneak attacks) priced above the Shortsword +1.
  - Camellia's `[WeaponSet]` line says no other set holds a ranged weapon: her other set holds a starknife, a melee
    weapon in this game (author).
- **Re-decisions:** no option dropped to decide again was barred afterwards. Every "not retrying this turn" row follows
  a game interrupt, mostly as the target died (a whole group at once when the last enemy fell).
- **HP loss:** a Vrock at 73 → 20 HP had its Stunning Screech dropped. It decided again and chose the Screech again.
- **Provoking action:** one re-decision (fight 10). A Quickwood Sprout reached a Nabasu walking to deliver a Coup de
  Grace. The new decision priced the attacks of opportunity and chose Enervation instead.
- **Domination:** not exercised (no ally was dominated).

## Fixes

1. **Both modes, performance (the same numbers):**
   - Fights 7 and 12 had four decisions over 1 s; the slowest took 2,069 ms (arcan, 42 enemies). The previous log's
     slowest was 640 ms.
   - Most of that time was walk pricing: 195–242 approaches planned a decision, 400–950 ms.
   - Each priced spot read every ally's drop chance against every enemy. For each pair it made native reads of the
     enemy's melee weapon, reach and lying-down state: allies × enemies × spots.
   - Now:
     - Each enemy's strike data is read once a decision (`RowStrikeShare`). So is an ally's strike share where it stands
       (`OwnStrikeShare`).
     - A walk no longer re-reads an ally that no enemy whose target choice it changes can pick (`MoverChangedRows`).
       That ally's chance is exactly what it was. With a swarm in the fight every ally is read, as before.
     - The lists are reused, and the threats are combined without an enumerator.
   - The rules are unchanged. One caveat: the single-attack share is read "against the first party member asked
     about", once a frame, and fewer reads can change which member that is. That was already order-dependent.
   - The slow-decision row now names the drop-chance reads, their time and the allies skipped, so the next log shows
     the gain.
2. **Real time, a walk before a cast:**
   - In fight 9 the Vrock, a Shadow Demon and Seelah each walked to the same helpless Nabasu to deliver a Coup de Grace,
     one after another.
   - The rule that withholds an ability an ally is already casting on that target (coups, debuffs, control, marks) only
     saw cast commands. A walk to a free side, around an area or to touch range was not one.
   - Such a walk now counts as the cast while it runs (`NotePendingWalk`). Its end clears it, and the cast is noted
     again when it is issued.
3. **Log and saves, fused spells:**
   - The `[Save]` rows showed "Knight Commander's : 3 saves" (69 rows in this log). Every Magic Fusion is cast through
     one native holder ability, which has no name.
   - The rows now use the fused spell's name (`AbilityData.Name`: "Phantasmal Web/Boneshatter"). Each fusion has its
     own fight tally.
   - What a target showed against one fusion was also remembered for every other fusion, such as an automatic pass
     against a mind-affecting part. It is now remembered per fusion (`AbilityProfile.FusionIdentityOf`).

## Observed, not changed

- **Coups de grace:** six issued in real time, none delivered; each was interrupted before acting.
  - The Vrock stood 4.4 s beside the Nabasu. It was interrupted as the Bandit's crossbow volley on the Nabasu ended.
  - A Phantasmal Web held the Nabasu (entangled, unable to move: not helpless by the native rule). Its state at the
    coup is not in the log. The Coup de Grace here is Tabletop Tweaks', and its target rule may differ.
  - In fight 12 the Bandit Necromancers died or recovered under others' attacks.
  - Fix 2 stops the doubled walks. Holding other attacks off a coup target is a commitment change for the author.
- **Many potions on Seelah at once (fight 9):** five Cure Critical Wounds in two bursts, from Aeiluvetar, Wenduag and
  arcan, plus one she drank herself. All healed in full (24, 26, 27, then 21 and 23 HP); she has about 150 HP. Nothing
  was wasted, so heals are not coordinated further.
- **Interrupts before acting:** 122 in the real-time fights. 70 commands ended before acting with no AI Control reason in
  the log, 66 of them in real time. The samples checked came as the target died; several units were interrupted at
  once when the last enemy fell.
- **Vrock full attack with a step (turn-based):** offered with a 5-foot step that needed 2.5–8 m of movement, and
  withheld before issue by the path check. The Vrock then took its next option, as designed.

## Replay

Both modes. Send `Log.txt` and `combatLog.txt`.

1. **Performance:** a fight with 30 or more enemies, in real time.
   - The slow rows (`[Perf] slow decision`) should be fewer and shorter.
   - Each names "drop chances read N (X ms), allies re-read for walks N (unchanged, skipped N)".
2. **Coup de grace (real time):** put an enemy to sleep or hold it, with several melee allies near.
   - Only one ally should walk to deliver the Coup de Grace. The others' coup reads "an ally is already casting it on
     this target" while it walks.
3. **Weapon sets:** keep the Bandit's crossbow set. When an enemy stands next to him and the shortsword is clearly
   better, look for "Switch to melee weapon set …". Both modes.
4. **Fused spells:** an Arcanist's fused spells against saving enemies. The `[Save]` fight rows name each fusion
   ("Knight Commander's Phantasmal Web/Boneshatter: …").
5. **Carried over:** domination with the Eldritch Archer (the previous page's item 3) and a pending potion under HP loss
   (item 1).
