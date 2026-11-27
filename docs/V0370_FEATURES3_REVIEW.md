# v0.3.70 features3 playtest: review and fixes (27 November 2026, Claude)

The author played features3 (module `e32d25e0`) in one session: five turn-based fights, then seven real-time fights.
Source `7131634` on `claude/v0370-features3-review`, stacked on `claude/v0370-features-review` (draft PR #118). Package
`test-builds/AI-Control-FACC-v0.3.70-dev-features4-7131634-UMM-test.zip`, module `5303e299-817e-40eb-8592-5958d5eff1fa`,
UMM 0.3.70, **unplayed**. [Receipt](competence/V0370_FEATURES3_REVIEW_CHECKS.json). The previous review is
[V0370_FEATURES_REVIEW.md](V0370_FEATURES_REVIEW.md).

## The logs

| Mode | Fights | Decisions | Commands |
| --- | --- | --- | --- |
| Turn-based | 5 | 408 | 213 issued |
| Real time | 7 | 2,341 | 550 issued, 543 ended: 345 succeeded, 56 interrupted after acting, 138 before acting, 4 failed |

- No AI Control exception.
- Party deaths: the Drow Magus (real-time fight 7) and Wenduag (fight 8). Summons lost: a Vrock, a Cambion, the Derakni.
- Enemy spawns came from RandomReinforcementsPerEncounter.
- Succubi dominated Camellia (fight 10) and Wenduag (fight 11).

## What the features3 changes did

- **Companions decline a costly rage:** works.
  - The Knight Commander switched off "Inspire Rage — Accept Effect" in three turn-based fights. Its spells measured
    375–502 a round, against nothing the rage gave it.
  - The Derakni did so twice. Both switched back on after each fight.
  - Camellia's rage still started for the others.
- **Performance:**
  - The slowest decision was 640 ms, Orvath's first of fight 1. 190 ms of it was the fight's first threat reading.
    The previous log's slowest was 844 ms.
  - Slow rows reuse 600–3,500 damage estimates and 500–4,600 effect chances each.
  - 28 decisions over 250 ms in 12 fights.
- **Group teleports:** Greater Teleport now gets a landing quote with the summons out ("2 passengers, landing risk 0.0").
  It lost to a weapon attack (0.7 against 9.7).
- **F2, weapon sets:** the new `[WeaponSet]` line answers why it was never offered. No Mixed character has a second weapon
  set of the other kind:
  - Camellia (greataxe) and the Bandit (shortsword) have no bow set;
  - the Drow Magus (longbow) has no melee set.
  - To see F2 work, put a bow in Camellia's second set.
- **Suggested roles (#115):** applied to each newly recruited character (Bandit Conjurer, Bandit Alchemist, Cambion Bard, a
  new Drow Magus). Every `[Settings]` row is a new character id; characters with saved settings kept them.
- **Summon control on by default:** 50 summon turns taken over in turn-based mode.

## Fixes

1. **Real time: our own re-decisions counted as failures.**
   - Fight 8, Wenduag:
     - At full HP she switched on Griffon Aspect.
     - Bulettes and a Dire Bear took 63 HP while it waited to start. The HP-loss rule dropped the command to decide
       again, and the driver then barred the toggle for the turn, as if it had failed.
     - At 35 % she chose a Cure Critical Wounds potion. It was dropped again at 34 → 9 HP and barred.
     - She drank a Cure Serious Wounds potion instead and died.
   - A Cambion's walk out of a Locust Swarm was dropped the same way, then barred.
   - Now:
     - A command dropped to decide again is not learnt from (`RealTimeTactics.ReconsiderPending`). This covers HP loss, a
       new harmful area, a new threat on the way, an arrived walk, and commands dropped alongside.
     - A pending heal of the unit itself, or a walk away, is left running after an HP loss: it is the answer that rule is
       for (`RealTimeReplanRules`).
2. **Both modes: a Magus spell delivered through a weapon at a dominated ally.**
   - With Spellstrike on, the game turns a Magus spell cast at an enemy into a weapon attack. An Eldritch Archer's spell
     goes through the bow (`UnitUseAbility.OnAction` queues a `UnitAttack`). A touch spell's charge goes through the melee
     weapon (`UnitUseAbility.CreateCastCommand`).
   - A dominated ally counts as an enemy to that test, so the rule that keeps weapon attacks off it did not cover these.
   - The Drow Magus cast Flare on dominated Camellia (four arrows, all missed) and on dominated Wenduag (an arrow for 13).
   - Such a cast is now spared like a weapon attack (`DeliveredByWeapon`). A Spell Combat follow-up is not priced at such
     an ally either. Other spells on a dominated ally (Daze, Command, Flare from a non-Magus) are unchanged.
3. **Log:**
   - the `[WeaponSet]` line is printed once per summon kind, and not for creatures with natural weapons (the Summoned
     Dogs, Vrocks and Cambions each printed their own);
   - an ability with an empty display name is logged by its blueprint name. A Derakni's teleport read
     "Derakni: … ;  best 0".

## Observed, not changed

- **The Drow Magus's death (fight 7):**
  - At the start of the fight she threw a Stun Bomb; no enemy threatened her when she chose it.
  - Two Venomhodags reached her and bit her four times. The throw then provoked two attacks of opportunity, which killed
    her.
  - The log cannot tell whether her throw had started when the bites came. The HP-loss rule only drops a command that
    has not started.
  - The change would be a new re-decision trigger: a newly arrived enemy threatening a pending action that provokes, not
    only a walk. It changes when a choice is reconsidered, so it is proposed here, not made.
- **Unexplained real-time interrupts:** about 83 commands ended before acting with no AI Control reason in the log.
  - Several ended together (six at once in fight 9). Many came as their target died: Orvath's Command Undead on a Ghoul
    Huntmaster that Chain Lightning killed.
  - They look like the game's own interrupts (target dead or no longer valid) or player orders. The log does not name the
    cause.
- **Coup de grace:** four issued, one succeeded. The other three were interrupted before acting: the target had died or
  was no longer helpless, as in the last log.
- **Domination:** freeing was offered (Dispel Magic frees Camellia, 30). The Knight Commander's Ki Shout on the Succubus
  (71) won. The others used Daze and Command on Camellia, as the policy wants.
- **Grease scores high** (up to 299 on a Bulette): an area that knocks low-Reflex brutes prone at DC 23. Saves failed
  often. It fits the control model.
- **Incorporeal last enemies:** weapon attacks on the last Greater Shadow or Shadow Demon were worth about 0 at
  difficulty 0.10. A few turns ended with "nothing worth doing" while casters finished them. That is the easy-fight
  restraint, as designed.

## Replay

Both modes. Send `Log.txt` and `combatLog.txt`.

1. **HP loss with a heal pending (real time):** let a melee character take heavy hits while it waits to drink a potion.
   - The potion should not be dropped.
   - After any "lost N HP … dropping the commands" row, there should be no "not retrying this turn" row for that option.
2. **Swarm escape (real time):** a walk out of a swarm should continue after a loss of HP.
3. **Domination with the Eldritch Archer:** a Succubus fight.
   - There should be no "… [native Ranged Spell Combat]" row aimed at a dominated ally.
   - The combat log should show no Drow Magus arrows at one.
4. **Weapon sets:** put a bow in Camellia's second set, range Mixed. Look for a "Switch to … weapon set" option.
5. **Log:** one `[WeaponSet]` line per summon kind and none for natural-weapon creatures; no blank ability names in
   `[Unused]` or `[Coverage]`.
6. **Not visible in the log:** issue #114 (UMM's UI scale), the previous replay's item 6.
