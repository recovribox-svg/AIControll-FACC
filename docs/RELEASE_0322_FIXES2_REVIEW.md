# Review of the "v0.3.22 Release Version-fixes2" logs (30 October 2026, Claude)

Inputs supplied by the author:
- `Turn_Based_Mode_v0.3.22_Release_Version-fixes2_Log.txt`: 9 turn-based fights;
- `Real_Time_Mode_v0.3.22_Release_Version-fixes2_Log.txt`: 9 real-time fights;
- the game's own `combatLog.txt`: 18 combats; 1-9 are the turn-based fights, 10-18 the real-time ones.

Party: Orvath (Mastermind arcanist / kineticist), Seelah, Camellia, Lann, Aeiluvetar. Mods include TabletopTweaks-Core
(its damage-reduction rework is active), Buff It 2 The Limit, DarkCodex and others.

**Build identity:** both logs start at the first combat, so the `[Build] … module=` startup line is missing. They
contain the `[Reach]`, `[Walk]` and `TTT estimate` rows of PR #13 and #15, so they come from a build with those
changes (most likely the ecabeb4 test ZIP). Please include the startup part of the log next time.

## Fixed

### 1. Archers shot sleeping enemies awake: a Sleep loop (turn-based and real time)

**Turn-based fight 9 (combat 9).** Orvath put the Cultist Archer to sleep five times. Each Sleep was valued at about
45-50. Lann shot it awake after four of them; the combat log has each shot right after a Sleep. Lann had no other
target: the only other enemy was a Ravenous Shadow, which his cold-iron arrows cannot hurt. The shot was his
"last resort" at 0.5.

**Real-time fight 7 (combat 16).** The Neophyte Fighter slept from Orvath's Sleep (DC 22).
- Lann shot it for 3 damage, an option the AI valued at 0.6.
- The shot woke it while Seelah's Coup de Grace on it was starting (13.9, very likely a kill).
- The Coup de Grace ended without acting, and Seelah's next swing missed the now-awake target.

**Cause.** `WakeFactor` multiplied a non-lethal hit on a sleeper by 0.1 while another enemy was still acting. A
discount can never make waking the sleeper worse than doing nothing, so any unit with nothing better still took the
shot. The same loop was reported at 0.3.18-test9/10 and 0.3.19-test6; ×0.5, then ×0.1, were the earlier attempts.

**Fix.** That factor is now 0: such a hit is "not useful now" (below `MinMainValue`), and the unit ends its turn or
waits instead.
- Unchanged: a hit that is expected to kill the sleeper, and the half value once every enemy is disabled.
- The sleeper stays out of the fight, and helpless for a melee ally's Coup de Grace.
- It applies everywhere `WakeFactor` is used: weapon attacks, full attacks, spell damage, maneuvers, infusion effects.

### 2. Holy Water thrown at an enemy it cannot hurt (both branches of a nested condition counted)

**Turn-based fight 2 (combat 2).** Lann threw Holy Water at a Medium Earth Elemental, valued at 5.9. The splash hit
and did no damage: Holy Water only hurts undead and evil outsiders. The log said
`[Profile] Holy Water: branch condition OrAndLogic not evaluated - both branches counted`.

**Cause.** The per-target condition reader allows only listed read-only condition types. `OrAndLogic` is a group of
nested conditions and was not on the list.

**Fix.** A nested group is read when everything inside it is on the list.
- In the decompiled game, `OrAndLogic.CheckCondition` is just `ConditionsChecker.Check()`.
- Hit Dice limits stay top-level only, because their shared values are set there.
- Holy Water's group holds "has the undead or outsider fact" and "alignment evil", both already on the list.
- `OrAndLogic` appears in 195 dumped blueprints.
- The blueprint audit (4,801 rows) is identical before and after. Branch conditions are judged per target in combat,
  which the offline audit cannot see; the identical result shows the static readers are unchanged.

## Looked at, no change

- **Ravenous Shadow took six rounds (TB fight 9).** Dice and rules, not logic.
  - The Shadow is incorporeal (half damage). Lann's and Aeiluvetar's non-magical arrows cannot hurt it, so their
    0.5 Aid Another options were correctly refused.
  - Orvath's Air Blasts rolled a natural 1 and three misses in a row.
- **Revive notes like "needs 14 HP, heals ~9, stays up ~31%".** By design (v0.3.14/v0.3.20).
  - A partway heal is credited at 0.6 × the share it covers.
  - The "stays up" chance is for when the next heal brings the ally up.
- **A Cure Moderate Wounds that "healed only 7".** It rolled 2d8+4 = 7 (combat 5).
- **Real-time Coup de Grace by Seelah while getting up (fight 8).** It waited 5.1 s and ended without acting. The
  target was a Corrupted Mongrel under Color Spray, which leaves low-level targets unconscious and then only stunned,
  and stunned is not helpless. Most likely the helpless window closed while she stood up. Not changed: there is no
  evidence that a different decision would have worked.
- **Three real-time orders interrupted at the same instant (RT fight 3).** Two potions and one attack, from three
  companions at once. That is a game-wide interrupt, not a decision.
- **Arcane spell failure (PR #15).** No `[CastChance]` line in either log: nobody in this party casts under arcane
  failure, so this session neither confirms nor contradicts it.
- **TabletopTweaks preview (PR #13).** Active: `route=TTT estimate` rows. For example, Orvath's Jolt on the
  Neophyte Sharpshooter came out as 2 → 0 (electricity resistance). No error rows.
- **Observers (PR #14).** The "ended before it was cast - not counted" lines appear as intended (Divine Zap, Faerie
  Fire, Earth Blast, Cure Light Wounds, Coup de Grace). No false failure strikes.

## Movement proposals (author-approved 30 October, implemented)

The author approved these after the first round of this review.

1. **Orvath walked into his own Entangle (RT fights 1 and 5).** A closer look split this in two.
   - **RT5 was a tie.** Color Spray "into Entangle" and Sleep from where he stood were both 61.6, and the tie went to
     the walk. He was entangled (the 20 % the walk was priced at) and spent his next action leaving.
   - **Fix:** an action the unit can do from where it stands now wins a tie within 1 % (a difference the estimates
     cannot resolve) against one that walks first. A move that is itself the point (withdraw, leave an area, step
     out) is not affected.
   - **RT1 was priced correctly.** Color Spray at 58.3 against Sleep at 31.8; the area cost 5.8 at a 15 % chance to
     be held. The exposure cost dropped out of the second appraisal because, with the enemies entangled, the game's
     own attack-of-opportunity check no longer counted attacks on the way in. Both enemies failed the Color Spray,
     and the three attacks of opportunity on the way out did no damage. Not changed.
2. **Camellia at 11 % HP next to the Cultist Champion (TB fight 7, R3).** The step out of melee (turn-based) or
   back-off (real time) now follows the provoking cast worth most without its risk, not only the best action now.
   - It applies when that cast, safe, beats the best action now.
   - The step is worth what the best action gains by it; for the best action itself that is the risk the step
     removes, as before.
   - A cast on the caster itself (a self-heal) no longer needs the old spot in its range.
   - Her own Cure Light Wounds, priced out by the attack of opportunity it provoked, now makes the step worth taking.
3. **Charge and the archer's step-then-shoot estimate** now use the sleeping-target rule (`WakeFactor`), like every
   other attack.
4. **Diagnostics.** The `[Reach]` and `[Walk]` rows are unchanged and still collect evidence for recommendation E.

## Performance evidence (for recommendation B; nothing changed)

- **72 slow decisions (over 250 ms).** Every one over about 1.1 s coincided with a garbage collection ("memory
  clean-up ran"); 8 of the 72 did.
  - Slowest: 1,672 ms, Camellia R3 (TB), choosing nothing; 1,465 ms, Orvath (RT).
  - These look like collector pauses on the whole game heap, not the engine's own work.
- **Without a collection, Orvath's real-time decisions take 250-370 ms.** Nearly all of it is the abilities stage,
  with 79 abilities on his bar.
- Matched sessions are still needed before tuning anything.

## Checks run (Claude)

- Release build against the game's references: 0 warnings, 0 errors.
- `dotnet test tests/CompanionAI.Tests`: 218 passed.
- `tools/ProfileAudit` on the author's dumps: 44,644 loaded, 1 known failure, 4,801 rows, 0 reader errors. Identical
  to the previous build.
- Not run in the game: the replay checklist is in `test-builds/README.md`.
