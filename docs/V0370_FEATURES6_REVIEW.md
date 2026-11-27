# v0.3.70 features6 playtest: review and fixes (27 November 2026, Claude)

The author played features6 (module `73eef393`) in one session: five turn-based fights, then nine real-time fights.
The fixes are on `claude/v0370-features6-review`, stacked on `claude/v0370-features5-review` (draft PR #120). At the
author's request they go into the v0.3.70 release, with every open PR merged into development first. The previous
review is [V0370_FEATURES5_REVIEW.md](V0370_FEATURES5_REVIEW.md).

## The logs

| Mode | Fights | Decisions | Commands |
| --- | --- | --- | --- |
| Turn-based | 5 | 519 | 295 issued |
| Real time | 9 | 2,066 | 583 issued: 399 succeeded, 39 interrupted after acting, 140 before acting, 3 failed |

- No AI Control exception.
- Party deaths, all in real time:
  - fight 9: arcan and Aeiluvetar;
  - fight 11: arcan.
  - The combat log names arcan `MagusArmored` and Aeiluvetar `EnigmaticCoConspiratorCompanionUnit`.
- No ally was dominated. The fights had 9–11 enemies.

## What the features6 changes did

- **Performance:** the slowest decision took 457 ms; none was over 500 ms (four were over 1 s before).
  - These fights had 9–11 enemies, against 42–47 last time, so the gain is not measured like for like.
  - The new counters show the drop-chance reads costing 1–14 ms in a slow decision, about 10 µs each. With a walk, up to
    400 allies a decision were skipped because nothing about them changed.
- **Coups de grace:** eight issued, seven delivered. The one interrupted was on a Corrupted Jungle Bulette. No two allies
  walked to the same target, so "an ally is already casting it" never had to apply.
- **Fused-spell saves:** the `[Save]` rows now name the fusion ("Knight Commander's Phantasmal Web/Boneshatter: 10 saves,
  5 passed"). No blank names are left.
- **Provoking actions:** seven re-decisions (the Derakni, an Incubus, the Knight Commander, arcan four times).

## Fixes

1. **Both modes, weapon sets:** Camellia now carries a crossbow in her second set. She switched seven times, and each switch
   read "against 0" for the weapon in hand.
   - The switch compared the other set with the current weapon's options affordable at that moment. Every switch came
     with her standard action spent (after Grease or Inspired Rage):
     - real time lists the current weapon's options during the cooldown, but they were skipped as unaffordable;
     - turn-based builds none once the standard action is spent.
   - Turn-based fight 2: after Inspired Rage she switched to the crossbow (3.4 a round) and ended her turn. Moments before,
     the greataxe's Move+Attack was worth 10.8.
   - Now the current weapon is priced by its options whatever the moment allows. Failing that, it keeps its price from
     earlier this turn (`WeaponSetRules`); with neither, no switch is offered.
   - An attack after the switch this turn is credited only with the standard action left. She was credited a shot she
     could not take.
2. **Both modes, drained levels (author-approved):** the two deaths in fight 9 came from level drain, not HP loss.
   - A Corpulent Spectre (2 levels a touch) and a Cairn Wight Rogue (1 a hit) drained arcan, an Arcanist 4, at 98 % HP.
     They also drained Aeiluvetar, a Guardian 5.
   - The game kills a unit whose negative levels reach its character level (`UnitLifeController.ForceTickOnUnit`).
   - The engine priced the drain: the Spectre's threat on arcan read "4 negative levels". But a withdrawal was only
     offered below half HP, so arcan never had one.
   - Now half or more of a unit's levels drained counts as badly hurt, as below the HP caution share
     (`TacticalEngine.BadlyHurt`, `ValueModel.HalfDrained`). The withdrawal is offered, and a walk pays for its exposure.
     Both are priced as before.
   - The decision header names "drained N/L".

## Observed, not changed

- **arcan's death in fight 11:** surrounded at the start of the fight, he lost 39 HP while his first cast waited for its
  cooldown.
  - Three newcomers each triggered the provoke re-decision; each new decision chose another provoking cast (and once a
    potion at 36.7 lost to Incendiary Runes at 40.4).
  - The drops cost him no time, since his cast was still waiting for the cooldown. Three melee hits killed him (72 HP).
- **Wenduag's potion for the Drow Magus failed** (fight 14): her grab caught a Bloodlusting Skeleton as the command
  started, and holding it forbids standard actions. She was free when she chose the potion.
- **arcan's Lesser Restoration item failed its Use Magic Device check** (20 against 23); the item keeps its charge.
- **Idle turns:** nine "nothing worth doing" rows. Their best options were either withheld before issue (a path through
  Grease) or valued 0 against incorporeal last enemies, as designed.

## Replay

Both modes. Send `Log.txt` and `combatLog.txt`.

1. **Weapon sets:** Camellia with Grease or Inspired Rage on her bar and the crossbow in set 2. No "Switch to … weapon set"
   right after a standard-action spell unless the other set beats her current weapon's best option, walk included.
2. **Level drain:** a fight with Shadows, Spectres or Wights against a low-level ally.
   - Once half its levels are drained, its header reads "drained N/L".
   - "Withdraw (low HP)" is offered as at low HP.
3. **Performance:** a fight with 30 or more enemies, as the previous page asked.
