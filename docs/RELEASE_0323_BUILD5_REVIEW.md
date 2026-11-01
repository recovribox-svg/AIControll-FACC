# Review of the "v0.3.23-dev-build5" logs (1 November 2026, Claude)

Inputs supplied by the author:
- `Turn_Based_Mode_v0.3.23-dev-build5_log.txt`: 16 fights;
- `Real_Time_Mode_v0.3.23-dev-build5_log.txt`: 16 fights;
- the game's `combatLog.txt`: 32 combats; 1-16 are turn-based, 17-32 real time.

Build: `[Build] v0.3.23 module=89ffb864-46e5-4840-ba2e-15318074028e` (the bombs / Carnage test build, cabdb2e).
Party: Orvath (Isekai protagonist 4: kineticist blasts, Mastermind spells, Demonic Art summons), Aeiluvetar (guardian
companion 2, bow), Seelah (witch 1 / monk 1), Camellia (skald 2), Lann (ranger 2, bow).

No Alchemist, Dreadful Carnage fighter or Kinetic Sharpshooter is in this party, so the PR #16 changes did not come
into play. No errors or exceptions were logged by the mod.

## 1. "5ft step out of melee" that made things worse (turn-based) - fixed

**What happened.** Orvath took 18 five-foot steps out of melee in the turn-based fights. In 13 of them the action
after the step was worth less than the best action before it. Examples:

| Fight, round | Best option before the step | After the step |
| --- | --- | --- |
| 2, R4 | Full attack on the Ghoul Creeper, 25.8 | Earth Blast, 24.1 (with a swift buff) |
| 3, R3 | Color Spray catching several enemies, 130.5 | Grease, 70.7 (Color Spray now 68.9) |
| 5, R6 | Revive the downed Seelah with Cure Moderate Wounds, 44.0 (stays up ~91 %) | Earth Blast, 22.0; Seelah left down |
| 5, R7 | The same revive, 44.4 | Earth Blast, 23.2; Seelah left down |
| 6, R3-R4 | Color Spray on the Dretches, 71.8 and 95.8 | Snowball, 24.7 and 35.6 |

**Causes (three, all in how the step was valued).**
1. **Spells the policy holds back.** The step is offered for the provoking cast that is worth most without its risk.
   That choice ran before the Progressive spell policy, so it could pick a spell the policy then held back. In all
   18 steps the same decision logged "held back: Command Undead / Boneshaker / Blindness". The step was taken for a
   spell that was never cast.
2. **Options that walk were ignored.** The step's worth was measured against the best action Orvath could take where
   he stood. In turn-based combat a 5-foot step forbids any other movement that turn, so a better option needing a
   short walk (the revive of Seelah) was silently given up.
3. **Cones move with the caster.** The step spot only had to keep the cone's main target in range. From the new spot
   the cone caught fewer enemies.

**Fixes.**
1. The step is considered only for casts the Progressive policy will keep (a silent pass of the same policy, on the
   same options).
2. Turn-based, the step is compared with the best option of all, including those that walk first (real time keeps
   the old comparison: backing off there does not forbid a later walk).
3. For a cone, a step spot must keep every enemy the cone was valued on and add no ally (the game's own cone query,
   as when the cone was valued). If no spot does, there is no step.

**Effect on the logged cases.** The revives of Seelah would have been cast; the full attacks and the wide Color
Sprays would have been made where Orvath stood. In the 5 steps that did no harm the same spell was cast after the
step; such a step is still offered whenever that spell is itself worth stepping for.

## 2. Archers holding their arrows for six rounds (turn-based fight 6) - fixed

**What happened.** From R5 to R11 only two Dretches were left, both asleep from Orvath's Sleep in R2. Seelah and
Camellia were down. Lann and Aeiluvetar ended six turns in a row with every action unused (only Demoralize, worth
0.1-0.2, was offered). The Dretches woke when the Sleep ran out (R12), and the fight went on.

**Cause.** The build2 rule "do not shoot the last sleeper awake while a party member can finish it with a Coup de
Grace" counted any conscious melee ally with Coup de Grace, wherever it stood. Here that was Orvath (an empty hand is
the game's unarmed strike). He was more than 10 m away, at half speed, and never came.

**Fix.** That ally now counts only if it can get to the sleeper: within one move turn-based (it walks, then delivers
the full-round Coup de Grace next turn), within a round's walk in real time. Otherwise shooting a sleeper is valued at
half, as before build2. The build2 cases (an archer beside a melee ally) are unchanged.

## 3. Six summons in six rounds against two sleeping Dretches (turn-based fight 6) - fixed

**What happened.** In the same rounds, with difficulty 0.10-0.14, Orvath summoned a different demon every round
(Abrikandilu, Vermlek, Schir, Cambions, Quasits, Dretches), each at full value (11.5-18.0).

**Cause.**
- The rule "a summon once per fight while the caster's summoned creature still fights" applies per ability, and each
  Demonic Art II summon is its own ability.
- Summons had no easy-fight restraint, unlike spell slots and (since PR #16) daily bombs.

**Fix (an AI policy, documented in `ValueModel`).**
- A further creature is worth `1 / (1 + n)` while `n` creatures the caster summoned still fight
  (`ValueModel.SummonCrowdShare`, unit-tested).
- A summon that spends a spell slot or a daily resource gets the same easy-fight discount as spell slots and bombs
  (`ValueModel.LimitedUseDiscount`; none at difficulty 1 and above).
- The decision log says so: `summon (2 of its own fighting: x0.33) (easy fight x0.6)`.

**Effect.** At R7 of fight 6 the second summon would have been worth 16.6 × 0.5 = 8.3, and about 5.0 if the Demonic
Art summons spend a daily resource (the log does not say; the code checks the ability's own resource component). The
third and later summons fall further, and with fix 2 the archers' shots finish the Dretches sooner.

## Looked at, no change

- **Real time, round 1: Orvath picks a weaker spell ranked 8th-11th** (Sleep, Snowball, Faerie Fire). The game casts
  them as swift actions for him (his swift slot is spent afterwards), and he then casts Color Spray or a blast with the
  standard action. Correct.
- **Real-time fight 5: Sleep cast twice on the Cambion Fighter.** The first Sleep landed; an arrow already in flight
  woke it, and it attacked Orvath before the second. Correct.
- **Turn-based fight 3: Seelah and Camellia cast Daze (1.6) for nine rounds.** The enemies stood in Orvath's Entangle
  and Grease; every walk to them was priced into those areas and came out below zero. The party was safe and the
  archers and Orvath did the damage. This is the known area stand-off (diagnostics only; movement changes need the
  author's agreement).
- **Lann advances a little after shooting.** He closes to 85 % of his bow's range. Small, and a movement change:
  left for discussion.
- **Real-time stuck commands.** Three of Lann's attacks were cancelled as stuck (no line of sight). The existing safety
  net worked; no change.
- **Performance.** Turn-based: 71 slow decisions, average 393 ms. The 9 over 1 s all coincided with a garbage
  collection ("memory clean-up ran"). Real time: median 14-87 ms per fight. The "abilities" stage dominates for
  Orvath (77 action-bar abilities). No change in this review.
- **Downed companions.** Seelah and Camellia went down often (level 2 against CR3 elementals and a Baphomet's Evoker
  whose Scorching Ray hit Seelah for 21). That is enemy strength, not a decision fault.

## Checks run

- Release build against the game's references: 0 warnings, 0 errors.
- Unit tests: 254 passed (new: `SummonCrowdShare`).
- `tools/SigDump`: the same 1,495 external references as the cabdb2e build; none added or removed.
- Not yet run in the game.

## Test build

`test-builds/AI-Control-FACC-v0.3.23-dev-build6-967050c-UMM-test.zip`, startup
`[Build] v0.3.23 module=09a7ab12-2cc2-477c-a688-74765960ea97`. UMM still shows 0.3.23.

**What to look for in play.**
1. A caster next to an enemy steps out only to cast something it then casts, and never steps away from a revive it
   would have walked to.
2. A cone caster (Color Spray) does not lose half its targets by stepping.
3. With only sleeping enemies left and no melee ally near them, the archers shoot.
4. A summoner with demons already fighting, in an easy fight, does something else than summoning again.
