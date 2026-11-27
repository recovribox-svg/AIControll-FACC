# v0.3.70 features3 playtest: review and fixes (27 November 2026, Claude)

The author played features3 (module `e32d25e0`) in one session: five turn-based fights, then seven real-time fights.
Source `bf7b1da` on `claude/v0370-features3-review`, stacked on `claude/v0370-features-review` (draft PR #118). Package
`test-builds/AI-Control-FACC-v0.3.70-dev-features5-bf7b1da-UMM-test.zip`, module `cfc0deda-c511-4e3a-a423-bf5be1cf502c`,
UMM 0.3.70, **unplayed**. It replaces features4 (`7131634`, never played) and adds the two follow-ups the author asked for
(fixes 4 and 5). [Receipt](competence/V0370_FEATURES3_REVIEW_CHECKS.json). The previous review is
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
- **F2, weapon sets:** never offered, though Camellia and the Bandit had a ranged weapon in another set (author report).
  The new `[WeaponSet]` line said no other set held one; that line had the same bug as the switch (fix 4).
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
4. **Both modes, weapon sets (F2) never saw another set's weapon** (author report: Camellia and the Bandit had a non-bow
   ranged weapon there).
   - The game's `WeaponSlot.MaybeWeapon` gives the character's empty-hand weapon (unarmed, a natural weapon) for any
     slot that is not active, and an inactive weapon set's slots never are.
   - F2 read the other sets through it, saw natural weapons only, and never offered a switch, whatever weapon they held.
   - Other sets are now read from their slots' items (`InactiveSetWeapon`).
   - Every ranged type is eligible: darts, javelins, light and heavy crossbows, throwing axes, bows and sling staves all
     have the game's Ranged attack type. The audit checks the 15 such types in the exports.
   - The other set's weapon is priced by the game's own attack, weapon-stat and range rules. That covers each type's
     range increment, a thrown weapon's or composite bow's Strength, and non-proficiency.
   - Its enhancement was added twice: the weapon-stat rule already applies the enchantments of a weapon its attacker
     does not wield (`RuleCalculateWeaponStats.ApplyEnchantmentsManually`), as for an inactive set's item.
   - No reload rule exists for crossbows in the game, so they are priced like bows.
5. **Real time, re-decision for a provoking action (author-approved):**
   - A provoking action waiting to start where the unit stands is decided again when an enemy now threatens the unit
     that did not when it was chosen, and can make an attack of opportunity on it.
   - Provoking actions are a cast with a defensive-casting check, a shot, a ray, a thrown bomb, or an ability that makes
     its caster provoke (`TacticalEngine.ProvokesWhenThreatened`, the same conditions the decision prices).
   - The new decision prices the attacks of opportunity as usual: it may cast defensively, step away, strike, or keep
     the action. An engager counted when the action was chosen changes nothing.
   - This is the Drow Magus's case (below).

## Observed, not changed

- **The Drow Magus's death (fight 7):**
  - At the start of the fight she threw a Stun Bomb; no enemy threatened her when she chose it.
  - Two Venomhodags reached her and bit her four times. The throw then provoked two attacks of opportunity, which killed
    her.
  - The log cannot tell whether her throw had started when the bites came. The HP-loss rule only drops a command that
    has not started.
  - Fix 5 (author-approved) re-decides such a throw while it has not started.
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
4. **Weapon sets:** keep Camellia's and the Bandit's ranged weapons in another set, range Mixed. Any ranged type works.
   - Look for "Switch to ranged weapon set N (…)" when nothing is in reach, and "Switch to melee …" back when an enemy
     stands next to them.
   - The switch is a move action and is not repeated within two rounds.
   - Both modes.
5. **Provoking action (real time):** a caster or archer that chose a cast, shot or bomb with nobody near.
   - When an enemy reaches it before the action starts, look for "[RT] … now threatens it and '…' would provoke - dropping
     the commands that have not started".
   - Look for a new decision that prices the attacks of opportunity.
6. **Log:** one `[WeaponSet]` line per summon kind and none for natural-weapon creatures; no blank ability names in
   `[Unused]` or `[Coverage]`.
7. **Not visible in the log:** issue #114 (UMM's UI scale), the previous replay's item 6.
