# Test builds

## Current test build: v0.3.27-dev-test4 (375589a) - test 3 log review

**[Download the UMM test ZIP (375589a)](AI-Control-FACC-v0.3.27-dev-test4-375589a-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.27-dev-test4-375589a-UMM-test.zip>

Review of the test 3 log (13 fights: 8 turn-based, 5 real time; no mod errors; [docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §94).
Tests 1-3 seen working: turn ends waited 0.2 s with no hang, no summons or buffs against helpless enemies, and no
real-time walk met an uncounted enemy (the new rule had nothing to do).
- **The fix (real time):** in fight 10 Seelah walked to a Corrupted Mongrel Ranger, and Camellia then put Grease on the
  Brute beside it. Seelah fell four times and lay prone about 15 s. A control area is now held back when an ally's
  walk to its target would take it into the circle, not only when its whole stopping place is inside; and a small height
  difference no longer hides it (the game's areas are 100 m tall).

UMM shows **0.3.27**. Startup line `[Build] v0.3.27 module=b6d01331-72ba-49db-a9b4-a97bd40666c3`.
- ZIP SHA-256 `1926476b458f6ed283a424027d74159a5dd47525d49382420fb746e3dab470d0`; DLL SHA-256 `e52285bc9007331cf5a5ecde7dba434bea8089808209cdacd5835b379aaddb1f`.
- [Identity](AI-Control-FACC-v0.3.27-dev-test4-375589a-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 324 unit tests pass; release build 0 warnings; no new game bindings. Not played yet.
- **Test:** real-time fights with Grease or Entangle casters and melee companions. The log shows
  `[Walk] <caster>: withhold Grease: <ally> is already approaching ...; its walk to where it stops ... meets the new ... circle`.

## Test 3: v0.3.27-dev-test3 (faf3bbf) - real-time walks and new enemies (also in test 4)

**[Download the UMM test ZIP (faf3bbf)](AI-Control-FACC-v0.3.27-dev-test3-faf3bbf-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.27-dev-test3-faf3bbf-UMM-test.zip>

Tests 1 and 2 (below, not played yet) plus one real-time fix ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §93).
- **The problem:** in the v0.3.26 log (real-time fight 8), Seelah set off to a Corrupted Mongrel Ranger at the back
  while only two enemies had joined the fight. A Plagued Wolf joined, reached her and bit; she kept walking, its attack of
  opportunity was a critical hit for 25, and she went down.
- **The fix:** while a companion is still walking to a target, the walk is dropped and decided again when an enemy it
  did not count gets in the way: one that reaches the companion on the way (not at the target), or one that joined the
  fight since and can step into the rest of the walk. The newcomer is then counted: walking on past it costs a free
  attack, so fighting it often wins, but the same target can still be chosen. Once per walk; turn-based is unchanged.

UMM shows **0.3.27**. Startup line `[Build] v0.3.27 module=7415e86a-471f-4b4f-bfca-4de5ec39513d`.
- ZIP SHA-256 `89f2cea19cf9df65a68b0ff4a7d70fe5ccc73b228d8ebdc495a2cff55e7fe84a`; DLL SHA-256 `6fe33766424c25b4567f01c5b84810a6458477575926e8c5b76eea031b6a4d86`.
- [Identity](AI-Control-FACC-v0.3.27-dev-test3-faf3bbf-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 316 unit tests pass; release build 0 warnings; no new game bindings. Not played yet.
- **Test:** real-time fights, ideally with enemies that join late (reinforcements, enemies out of sight at the start).
  Front-liners should turn to an enemy that reaches them instead of walking past it. The log shows
  `[RT] <name>: <enemy> engages it on the way ...` or `... joined the fight and can get in its way ...`, followed by
  `deciding again`.

## Test 2: v0.3.27-dev-test2 (56d0c15) - 0.3.26 log review (also in test 3)

**[Download the UMM test ZIP (56d0c15)](AI-Control-FACC-v0.3.27-dev-test2-56d0c15-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.27-dev-test2-56d0c15-UMM-test.zip>

Test 1 (turn-end pacing, below, not played yet) plus two fixes from the author's v0.3.26 log (10 fights, turn-based and
real time, no mod errors; [docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §92).
- **No summons or buffs against helpless enemies:** with only a sleeping Cambion Fighter left, waiting for Seelah's
  Coup de Grace, Orvath spent a daily Demonic Art summon and Camellia started Inspired Rage. While every enemy left is
  helpless, summons and pure buffs are no longer options.
- **Healing summons by the time they have left:** Camellia healed Orvath's summoned demon twice, valued as if it would
  fight the whole battle. A summon is now worth only the rounds until it vanishes.
- The "Abilities AI must not use" lists held: Entangle and Grease were never used while blocked.

UMM shows **0.3.27**. Startup line `[Build] v0.3.27 module=9bf1e011-c95c-45d2-b26e-d7f6548537d6`.
- ZIP SHA-256 `f8169823a318faafa119ca0f838b5b221df3b3c52b143fb4573864fedb48f162`; DLL SHA-256 `f01f54c85b678e5382042c42bba5e30439fc44ee990f7ab8d3783038377ca7e0`.
- [Identity](AI-Control-FACC-v0.3.27-dev-test2-56d0c15-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 311 unit tests pass; release build 0 warnings. Not played yet.
- **Test:** test 1's projectile check, plus fights with summoners and healers. When the last enemies are asleep or held,
  nobody should summon or start a performance or rage; the log shows `only helpless enemies left - no summons or pure
  buffs`. Summons close to vanishing should not get heals.

## Test 1: v0.3.27-dev-test1 (6508dc4) - turn-end pacing (also in test 2)

**[Download the UMM test ZIP (6508dc4)](AI-Control-FACC-v0.3.27-dev-test1-6508dc4-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.27-dev-test1-6508dc4-UMM-test.zip>

v0.3.26 plus one fix for turn-based combat ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §91).
- **The problem:** AI companions' turns ended the moment their last command finished. The game runs the gap between
  turns at triple speed, so a projectile still in flight, and the end of the animation, played sped up or vanished.
- **The fix:** a turn now ends the way the game ends its own units' turns. The AI waits until the companion's
  projectiles have landed and it has stood idle for the game's short pause (0.2 s at x1 animation speed). It never waits
  more than 4 s.

UMM shows **0.3.27**. Startup line `[Build] v0.3.27 module=634a3871-881b-4a28-8752-09afa11207a3`.
- ZIP SHA-256 `b2e08f1e30755614cf687db6c5c06693a1d7fdb74e9d321cd12eca7e4b00df99`; DLL SHA-256 `2e3ecaf151bef875408e702833f76d68354c894a3fe6bb821941d3560a4cb793`.
- [Identity](AI-Control-FACC-v0.3.27-dev-test1-6508dc4-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 311 unit tests pass; release build 0 warnings. Not played yet.
- **Test:** a turn-based fight with spells that fire projectiles (Fire Blast, rays, Magic Missile) and an archer.
  Projectiles should fly at normal speed and land before the camera moves to the next unit, and no turn should hang.
  With debug logging on, `[TurnTick] <name>: turn end waited ... s` shows each wait.

## Feature test 2: "Abilities AI must not use" + smaller log (d1d930f), released (with the [Reach] rows also moved to the debug log) as v0.3.26

**[Download the UMM test ZIP (d1d930f)](AI-Control-FACC-v0.3.25-dev-blacklist2-d1d930f-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.25-dev-blacklist2-d1d930f-UMM-test.zip>

Test 1 (played: blocking, unblocking and the shared list work) plus a log review. The normal log is 15-28 % smaller on the
recent logs, with fewer repeated diagnostic lines; decisions are unchanged
([docs/TASK_USER_ABILITY_BLACKLIST.md](../docs/TASK_USER_ABILITY_BLACKLIST.md) §2.9).

UMM shows **0.3.25**. Startup line `[Build] v0.3.25 module=d2dc1a06-0509-403e-87a3-3f21b580911a`.
- ZIP SHA-256 `7cfcafc05daacde191e0a2ba628da90558cc14586eb6ccd047bc8d82f52166dc`; DLL SHA-256 `7214f36d78edb4829813cf624c1baaedced9415d5c314940c3d423ac22fbb759`.
- [Identity](AI-Control-FACC-v0.3.25-dev-blacklist2-d1d930f-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 311 unit tests pass; release build 0 warnings; no new game bindings. Not played yet.

## Feature test 1: "Abilities AI must not use" (task branch, a030c79)

**[Download the UMM test ZIP (a030c79)](AI-Control-FACC-v0.3.25-dev-blacklist1-a030c79-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.25-dev-blacklist1-a030c79-UMM-test.zip>

v0.3.25 plus a list of abilities and toggles the AI must not use.
- **Where:** in each character's panel in the settings (Ctrl+F10), plus a shared list for everyone.
- **Covers:** turn-based and real-time combat, after-fight buffing and Pre-Buff.
- **Not touched:** your own orders, other mods and automatic effects.
- How to use it and what to test: [docs/TASK_USER_ABILITY_BLACKLIST.md](../docs/TASK_USER_ABILITY_BLACKLIST.md) §2.1
  and §2.7.

UMM shows **0.3.25**. Startup line `[Build] v0.3.25 module=aab34bce-39d7-45c1-9346-551a9b5aa05a`.
- ZIP SHA-256 `974abbb1cf2966f414b6dab399331261542f171cf462521115b9ba1278742d5a`; DLL SHA-256 `ffc9ccf0ced7890dad7273bdac7747e41e4d4450d2d36eae1c2f03c72eeadad7`.
- [Identity](AI-Control-FACC-v0.3.25-dev-blacklist1-a030c79-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 311 unit tests pass; the release build has 0 warnings; the matching passes on the game's real blueprints
  (offline). Not played yet.

## Current test build: v0.3.25-dev-test3 (ffed9ac), released as v0.3.25

**[Download the UMM test ZIP (ffed9ac)](AI-Control-FACC-v0.3.25-dev-test3-ffed9ac-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.25-dev-test3-ffed9ac-UMM-test.zip>

Test 2 plus performance work from the test2 logs ([report](../docs/LIVE_CODE_AUDIT_V0325.md) §9): far less memory garbage per
decision (the stutters were the game's memory clean-ups during Orvath's decisions). Decisions themselves are unchanged.

UMM shows **0.3.25**. Startup line `[Build] v0.3.25 module=b1f83486-ff07-4245-b5d2-f4b0998b5d1a`.
- ZIP SHA-256 `376e6da898704ca418c41c08101957fd9530d83d076ac815759c6fd9a6bb6dba`; DLL SHA-256 `07eae141ae6c631a36b8eb3af8872172ad3cceaf8f8ab60a8c74ac6acfc37bd9`.
- [Identity](AI-Control-FACC-v0.3.25-dev-test3-ffed9ac-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 282 unit tests pass; release build 0 warnings. Played (test3 logs: no mod errors); byte-identical to the [v0.3.25 release ZIP](../release/CompanionAI_Pathfinder_v0.3.25.zip?raw=true).

## Previous test build: v0.3.25-dev-test2 (2692562)

**[Download the UMM test ZIP (2692562)](AI-Control-FACC-v0.3.25-dev-test2-2692562-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.25-dev-test2-2692562-UMM-test.zip>

Test 1 plus the test1 log review and live-code audit ([report](../docs/LIVE_CODE_AUDIT_V0325.md)):
- **Faster:** spells the character cannot cast this turn (action used, or on cooldown in real time) are no longer
  scored - Orvath spent up to a second on them before ending his turn.
- **Healing potions:** no top-ups in nearly-won fights; still drunk when a companion is about to drop.
- **Weapons vs damage reduction:** the game's own rule decides what a weapon gets through (cold iron, silver, holy...).
- **Diagnostics:** slow decisions name their three costliest abilities and the memory they used.

UMM shows **0.3.25**. Startup line `[Build] v0.3.25 module=15e39809-c72b-47a8-8db8-e2593f1ca945`.
- ZIP SHA-256 `eb196e0ba68eada30ec9444ba172ed825ed7696c1a3ddd480f4e86053a4ea943`; DLL SHA-256 `e3e3775aec38ec08cc3043ecd85edbf839f60739b4bdcee787e8f8b496d51e9c`.
- [Identity](AI-Control-FACC-v0.3.25-dev-test2-2692562-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 282 unit tests pass; release build 0 warnings. Not yet played.

## Previous test build: v0.3.25-dev-test1 (907cd75)

**[Download the UMM test ZIP (907cd75)](AI-Control-FACC-v0.3.25-dev-test1-907cd75-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.25-dev-test1-907cd75-UMM-test.zip>

v0.3.24 plus ([notes](../docs/V0325_TEST1.md)):
- **Kineticist:** Unraveling and Chain Arrows are chosen automatically when they are worth their burn.
- **Psionics:** psionic focus is regained (a move action with Psionic Meditation is paired with the turn's attack), and
  a trance the AI's spending ended goes back on after the fight.
- **Potions:** Magical Vestment only when it beats the armor's or shield's own bonus (never without a shield); Align and
  Bless Weapon only against damage reduction they get through (Bless Weapon also for criticals on evil enemies); Lesser
  Restoration when fatigued, exhausted or under a stat penalty.

UMM shows **0.3.25**. Startup line `[Build] v0.3.25 module=b9270500-be53-43c5-8beb-8bbf61bbafe6`.
- ZIP SHA-256 `99ed0c04b0eb942e4f4ab1c059acbcf336b37a5a1d80f9a982e1388f3f3e3e02`; DLL SHA-256
  `519ab3d602c542801b4ba1b8fe15b144dda67743865ae714fe0bd88cec14a7b8`.
- [Identity](AI-Control-FACC-v0.3.25-dev-test1-907cd75-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 273 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors. Not yet played.

## Previous test build: v0.3.24-dev-build3 (0a203b4), released as v0.3.24

**[Download the UMM test ZIP (0a203b4)](AI-Control-FACC-v0.3.24-dev-build3-0a203b4-UMM-test.zip?raw=true)** - byte-identical to the
[v0.3.24 release ZIP](../release/CompanionAI_Pathfinder_v0.3.24.zip?raw=true).

Build 2 plus: a target the game's path does not lead to (an archer on a ledge), found by one companion, is not walked
at by the others (real time; author-approved). Startup line
`[Build] v0.3.24 module=0ea24da4-70d0-4c77-9046-58f05061174b`. [Identity](AI-Control-FACC-v0.3.24-dev-build3-0a203b4-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).

## Previous test build: v0.3.24-dev-build2 (2d6b49b)

**[Download the UMM test ZIP (2d6b49b)](AI-Control-FACC-v0.3.24-dev-build2-2d6b49b-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.24-dev-build2-2d6b49b-UMM-test.zip>

Build 1 plus ([review](../docs/V0324_BUILD1_LOGS_REVIEW.md)): a caster next to an enemy no longer takes a 5-foot step
away from a better option (a spell needing a short walk, its own melee attack).

UMM shows **0.3.24**. Startup line `[Build] v0.3.24 module=c3e6faed-f700-40ff-82b6-3cd864c6a4b0`.
- ZIP SHA-256 `8c3b1fe3c5108d4a8d7f010c6a19d3a5048691db38ef9ee67384f89b77b6599b`; DLL SHA-256
  `c53a5398acee914b16afbc3236c2369828afa4276873d9903e19c16a1355343d`.
- [Identity](AI-Control-FACC-v0.3.24-dev-build2-2d6b49b-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 258 unit tests pass; release build 0 warnings. Not yet played.

## Previous test build: v0.3.24-dev-build1 (432a219)

**[Download the UMM test ZIP (432a219)](AI-Control-FACC-v0.3.24-dev-build1-432a219-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.24-dev-build1-432a219-UMM-test.zip>

v0.3.23 plus ([review](../docs/V0324_BUILD1_REVIEW.md)):
- **Psionics:** a Psychic Warrior keeps its psionic focus (and the trance that ends with it) instead of spending it on
  1-round maneuvers, except as a fight's last action.
- **Sleeping enemies** in Grease or Entangle: the archers shoot when no melee ally can reach them safely.
- **Treat Affliction** (and other removal abilities) only on allies with something it can remove.

UMM shows **0.3.24**. Startup line:

```text
[Build] v0.3.24 module=05fff211-2d9a-452c-bce0-b988c4840974
```

- ZIP SHA-256: `5d4fa106c37ba88ed17a31636a9dd514925259085e8336f199ba69a40a31ed57`; DLL SHA-256:
  `da1d3d7169281a4e1e86cec1b447f10e6c6c3dbb10513ad0c5b2fda2eebc490a`.
- [Identity](AI-Control-FACC-v0.3.24-dev-build1-432a219-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 258 unit tests pass; release build 0 warnings. Not yet played.

## Previous test build: v0.3.23-dev-build6 (967050c), released as v0.3.23

**[Download the UMM test ZIP (967050c)](AI-Control-FACC-v0.3.23-dev-build6-967050c-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.23-dev-build6-967050c-UMM-test.zip>

The bombs / Carnage build (cabdb2e, played as build5) plus the fixes from its logs ([review](../docs/RELEASE_0323_BUILD5_REVIEW.md)):
- **5-foot step out of melee:** a caster steps only for a spell it then casts (the steps were made for spells the
  Progressive policy held back), never away from a revive or another option it would walk to, and a cone caster
  (Color Spray) steps only where the cone keeps its targets.
- **Sleeping enemies:** the archers shoot them when no melee ally is near enough to finish them with a Coup de Grace
  (two archers waited six rounds for an ally who never came).
- **Summons:** a further summon is worth less while the caster's summons already fight, and a daily summon is kept for
  fights that need it (six demons in six rounds against two sleeping Dretches).

UMM shows **0.3.23** (unchanged). Startup line:

```text
[Build] v0.3.23 module=09a7ab12-2cc2-477c-a688-74765960ea97
```

- Source: runtime commit `967050c` on development (`claude/dreamy-pasteur-b9h3rn`), base `c6f8608`.
- ZIP SHA-256: `8843109b813370dc60f8df5efc58d72879f1b3c2af6a385ebd436229903b2c7f`.
- DLL SHA-256: `b4efd3f509247207fa0366dc9a96c40ddab2c5a9834744682c831cdffde77820`.
- Archive: the mod DLL and `Info.json` (version 0.3.23) only.
- [Identity](AI-Control-FACC-v0.3.23-dev-build6-967050c-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).

**Checks**
- 254 unit tests pass.
- Release build: 0 warnings.
- SigDump: no external reference added or removed.

**What to play:** the same party and fights. Watch for steps out of melee (the spell after the step should be the one
it was for), revives of a downed ally next to an enemy, Color Spray after a step, sleeping enemies left at the end of a
fight, and Orvath's Demonic Art summons in easy fights.

## Previous test build: Alchemist bombs, Dreadful Carnage, Kinetic Sharpshooter (cabdb2e)

**[Download the UMM test ZIP (cabdb2e)](AI-Control-FACC-v0.3.23-dev-bombs-carnage-cabdb2e-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.23-dev-bombs-carnage-cabdb2e-UMM-test.zip>

Build 4 (development `00110eb`) plus the answers to three player reports
([evidence, plan and handover](../docs/TASK_BOMBS_CARNAGE_QUIVER.md)). These come from the game's code and data; none of
these characters has been played with them yet.
- **Grenadier Alchemical Weapon** now counts the flask it uses up:
  - "use thrown items" applies to it;
  - it must be worth a spent item;
  - it is used only right before a weapon attack (a bomb never uses it).
- **Force Bomb's knockdown** is now seen, so it can win over the fire bomb when knocking an enemy down is worth it.
- **Daily bombs** are kept for fights that need them, like spell slots: in an easy fight a bomb must be clearly better
  than the crossbow.
- **Dreadful Carnage:** an attack that is likely to kill now counts the free Dazzling Display the kill fires, so a
  likely kill is no longer passed over for a manual Display.
- **Kinetic Sharpshooter:** no change. In the game, arrows never carry blast damage; the quiver only pays a blast's
  burn. A log line now explains each fight's choice.

UMM shows **0.3.23** (unchanged). Startup line:

```text
[Build] v0.3.23 module=89ffb864-46e5-4840-ba2e-15318074028e
```

- Source: runtime commit `cabdb2e` (task branch `claude/alchemist-carnage-quiver`, base `00110eb`), merged into development
  by PR #16 (merge commit `0f7f2e6`).
- ZIP SHA-256: `26072f850ba32186631d15a688b37109c695866f389842f11401096b45444b82`.
- DLL SHA-256: `7481483f05d9012fd1f872980daaeafa5c268a9b206d3855fe8196612a6a4488`.
- Archive: the mod DLL and `Info.json` (version 0.3.23) only.
- [Identity](AI-Control-FACC-v0.3.23-dev-bombs-carnage-cabdb2e-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).

**Checks**
- 249 unit tests pass.
- Release build: 0 warnings.
- ProfileAudit: 0 errors; only the knockdown abilities change.
- New game references: read-only only.
- **No in-game run yet.**

**What to play** (each case in turn-based and in real time where possible)
1. A Grenadier with Alchemist's Fire in the bag, a crossbow or melee weapon, and bombs:
   - it should not use Alchemical Weapon before a bomb;
   - it should use it only when the next weapon attack gains enough;
   - with "use thrown items" off, never.
   Look for `[Buff] ... not used - it only adds to a weapon attack`.
2. The same Alchemist in an easy fight and a hard one:
   - an easy fight should show `daily bomb -x` on its bombs and more crossbow shots;
   - a hard fight should show no discount.
3. With Fast Bombs on: one `[Bombs] ... Fast Bombs is on` line per fight. Please send the combat log of a bomb turn: it
   shows how many bombs one order threw.
4. Force Bomb against a fire-resistant enemy that can fall: its knockdown appears as `maneuver` in the option.
5. A Dreadful Carnage fighter with several enemies close by:
   - attacks on a nearly dead enemy should show `[Dazzling Display on a kill N %: +x]`;
   - a manual Display should no longer win over a likely kill.
6. A Kinetic Sharpshooter:
   - the `[Kinetic] ... over the best blast through the bow` line shows both values;
   - if you still see arrows dealing blast damage, send the combat log, the archetype and the mod list.

## Previous test: v0.3.23-dev-build4 (da4e0a5)

**[Download the current UMM test ZIP — v0.3.23-dev-build4 (da4e0a5)](AI-Control-FACC-v0.3.23-dev-build4-da4e0a5-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.23-dev-build4-da4e0a5-UMM-test.zip>

Build 3 plus the fixes from its logs and the author's play-test notes ([review](../docs/RELEASE_0323_BUILD3_REVIEW.md)):
- a downed companion next to an enemy is no longer revived into an immediate attack of opportunity and the next blow
  (the healer deals with that enemy first unless the heal can really keep them up);
- real time: front-liners no longer walk past the enemy front line to chase archers and casters (long walks now cost
  their time and the enemies who step into the way);
- real time: no wasted orders while a companion is getting up.

UMM now shows **0.3.23** (`Info.json` bumped at the author's request). It is still a development test build. Startup
line:

```text
[Build] v0.3.23 module=f1604b11-326e-425e-a77e-2533856d4155
```

- Source: `claude/dreamy-pasteur-b9h3rn` at runtime commit `da4e0a5`.
- ZIP SHA-256: `e07c1ff42a68d66edad817a7038875d312991307da14b98fc8f0d3364b8de7b9`.
- DLL SHA-256: `8f8f1f9d85c87703e84332fe79ca0b507732f929b60c6a401951dc378667708c`.
- Archive: the mod DLL and `Info.json` (version 0.3.23) only.
- [Identity](AI-Control-FACC-v0.3.23-dev-build4-da4e0a5-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).

Checks: 229 unit tests pass; Release build with 0 warnings; no new game references. **No in-game run yet.**

## Previous test: v0.3.23-dev-build3 (089a402)

**[Download the v0.3.23-dev-build3 UMM test ZIP (089a402)](AI-Control-FACC-v0.3.23-dev-build3-089a402-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.23-dev-build3-089a402-UMM-test.zip>

The build the author played as "v0.3.23-dev-build2" (movement 0c2e0f5 below) plus the fixes from its logs
([review](../docs/RELEASE_0323_BUILD2_REVIEW.md)):
- in real time, melee companions walk up to a sleeping enemy and finish it with Coup de Grace;
- the last sleeping enemy is no longer shot awake when a melee ally can finish it;
- a spell whose walk stopped before casting no longer counts as a failure.

UMM still shows **0.3.22** until `Info.json` is bumped for the 0.3.23 release. Startup line:

```text
[Build] v0.3.22 module=582c262e-656d-4da3-83d0-0d2f43f4711f
```

- Source: `claude/dreamy-pasteur-b9h3rn` at runtime commit `089a402`.
- ZIP SHA-256: `18a41304cca547c287a35d87bf0408f9a97e8a56be6eda3aa02c9febf074bcc0`.
- DLL SHA-256: `870f30e1a36e81dc6e96f89ba9384c5bb33305e51a784edce689c5c6de33b4bd`.
- Archive: the mod DLL and the unchanged `Info.json` only.
- [Identity](AI-Control-FACC-v0.3.23-dev-build3-089a402-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).

Checks: 222 unit tests pass; Release build with 0 warnings; no new game references. **No in-game run yet.**

## Previous test: movement 0c2e0f5 (played as v0.3.23-dev-build2)

**[Download the movement UMM test ZIP — 0c2e0f5](AI-Control-FACC-dev-movement-0c2e0f5-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-dev-movement-0c2e0f5-UMM-test.zip>

The fixes2 build below plus the three movement follow-ups the author approved
([review](../docs/RELEASE_0322_FIXES2_REVIEW.md)):
- when acting from where a companion stands is worth as much as walking somewhere first, it stays (no walking into
  its own Entangle for nothing);
- a hurt caster next to an enemy can step away to heal herself instead of using a weak action;
- charges and an archer's "step back, then shoot" no longer aim at sleeping enemies.

Not the published v0.3.22 release. Startup line:

```text
[Build] v0.3.22 module=ba81d8ad-acb4-4dfc-80e1-1a46c8acd506
```

- Source: `claude/dreamy-pasteur-b9h3rn` at runtime commit `0c2e0f5`.
- ZIP SHA-256: `b2b766394fb696612306c9efbb25b3dbc4d099919445d8fb2412892f49d52998`.
- DLL SHA-256: `5f053fcfcfb26338e03ad8a71c735bb4679cb9c605cc62069dbddccb1253aa8d`.
- Archive: the mod DLL and the unchanged `Info.json` only.
- [Identity](AI-Control-FACC-dev-movement-0c2e0f5-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).

Checks: 218 unit tests pass; Release build with 0 warnings; no new game references. **No in-game run yet.**

What to look for, besides the fixes2 checks below:
- a hurt caster in melee takes a "5ft step out of melee" (turn-based) or "Back off" (real time), then heals;
- casters cast from where they stand when a walk would gain nothing;
- nobody charges a sleeping enemy.

## Previous test: fixes2 c3f1636

**[Download the fixes2 UMM test ZIP — c3f1636](AI-Control-FACC-dev-fixes2-c3f1636-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-dev-fixes2-c3f1636-UMM-test.zip>

The integrated ecabeb4 build below plus the fixes from the fixes2 logs ([review](../docs/RELEASE_0322_FIXES2_REVIEW.md)):
- archers no longer shoot sleeping enemies awake while another enemy is still fighting (the Sleep loop);
- Holy Water-style "only against undead or evil outsiders" branches are read per target.

It also carries Recommendation A's behaviour-preserving helpers. Movement is unchanged. It is **not** the published
v0.3.22 release.

UMM still displays **0.3.22**. Identify this build by its startup line, and please keep the startup part in the log you
send:

```text
[Build] v0.3.22 module=bd00b45e-bce8-4213-91e1-74425d708574
```

- Source: `claude/dreamy-pasteur-b9h3rn` at runtime commit `c3f1636`.
- Module MVID: `bd00b45e-bce8-4213-91e1-74425d708574`.
- ZIP SHA-256: `176aa2343601fe3ae0ad603c8e25b040c2b0014a75cf26538ab7d8a88e7be91a`.
- DLL SHA-256: `02ce9189fda9d47998d1215bdce62cf2bf381edefed9f8de118fca76d5f6037b`.
- Archive: exactly the mod DLL and the unchanged `Info.json`.
- [Identity](AI-Control-FACC-dev-fixes2-c3f1636-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).

Checks: 218 unit tests pass; Release build with 0 warnings; blueprint audit unchanged (4,801 rows). **No in-game run of
this DLL yet.**

What to look for:
- Sleep or Color Spray on one enemy while others still fight: archers shoot the awake enemies or wait, never the
  sleeper. Melee allies may Coup de Grace it.
- Holy Water is thrown only at undead or evil outsiders.
- Everything in the ecabeb4 checklist below.

## Previous test: ecabeb4 (integrated PR #13–#15)

**[Download the integrated UMM test ZIP — ecabeb4](AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.zip>

This is the **reviewed, integrated development revision**: published v0.3.22 plus PR #13 (optional Tabletop Tweaks
resistance preview, reach/Grease diagnostics), PR #14 (no false failure strike for uncast orders, missed-roll splash
kept out of damage calibration) and PR #15 (native arcane spell-failure chance in spell valuation, stalled-navigation
diagnostics). Its code is the same as Codex's last test (be0da2c), rebuilt from the merged revision, so it has its own
module ID. It is **not** the published v0.3.22 release: the [published stable ZIP](../release/CompanionAI_Pathfinder_v0.3.22.zip?raw=true)
does not contain these changes and is unchanged.

UMM still displays **0.3.22**. Identify this build by its startup line:

```text
[Build] v0.3.22 module=2cc6eb92-af79-4a1c-a65d-aad66d8ef315
```

Install as for the earlier tests below (fully exit the game, install through UMM or overwrite the two files in
`<game>/Mods/CompanionAI_Pathfinder/`, keep settings, restart).

- Source: `claude/dreamy-pasteur-b9h3rn` at runtime commit `ecabeb40141ede99c2f118d99f7ffa0667d746e4`.
- Module MVID: `2cc6eb92-af79-4a1c-a65d-aad66d8ef315`.
- ZIP SHA-256: `34618d191de35a16aa2a3006bad73bd5e6742c9fcc5e92d2d57c1da813126423`.
- DLL SHA-256: `06a274047b16a68841db613360848e31cfe4983fd6b9d33d5b5e5d73f21c70a0`.
- Archive: exactly the mod DLL and the unchanged `Info.json`; no game, TTT or verification files.
- [Identity](AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt) ·
  [Integration handover](../docs/INTEGRATION_0322_FOLLOWUPS.md).

Checks: Claude ran 164 unit tests (all pass), a Release build against the game's references (0 warnings, 0 errors) and
the v0.3.21-era blueprint reader comparison (4,801 rows identical to the v0.3.22 build). **No in-game run of this DLL
yet.** The focused replay is the be0da2c checklist below plus the earlier observer and TTT checks: arcane-failure
`[CastChance]` notes, uncast orders not counted, missed Holy Water splash not calibrated, TTT estimates, Burning
infusion, toggle ownership/restoration, group Sleep, healing, and the new `[Walk]`/`[Reach]` diagnostic rows.

## Previous test: be0da2c (Codex, PR #15)

*Historical handover text: PR #13–#15 have since been reviewed and merged (see the top of this page).*

**[Download the earlier UMM test ZIP — be0da2c](AI-Control-FACC-native-cast-chance-be0da2c-UMM-test.zip?raw=true)**

[Draft PR #15](https://github.com/recovribox-svg/AIControll-FACC/pull/15) targets development.
This **unmerged v0.3.22 follow-up** accounts for native arcane spell-failure
probability when valuing affected spells. It uses the game's own eligibility and
spellbook-UI calculator, lazily once per decision. A 20% failure chance reduces
expected benefit to 80%; it does not prevent legitimate random failures.
Independent movement/provoking costs retain their handling.

For Seelah's combat20 wall report, this adds **diagnostics only**: requested and
inspected endpoints, plus native movement data when existing stuck recovery fires.
There is no navigation, target-commitment, cancellation or exit-rule change.
Ardub/body-blocking integration is deferred; no such dependency is included.

Includes pending [PR #14](https://github.com/recovribox-svg/AIControll-FACC/pull/14)
and [PR #13](https://github.com/recovribox-svg/AIControll-FACC/pull/13). UMM remains
**0.3.22**. The [published stable ZIP](../release/CompanionAI_Pathfinder_v0.3.22.zip?raw=true)
and older test archives remain unchanged. New live acceptance is pending; there
is no performance quality cut or claimed speedup.

## Install and identify

1. Fully exit the game.
2. Install the ZIP through Unity Mod Manager's Mods tab.
3. If UMM refuses a same-version replacement, extract its two files into
   `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only; keep settings.
4. Restart and confirm complete startup contains:

```text
[Build] v0.3.22 module=bccf1618-8aa9-4a3d-9c3f-d68075ebafb8
```

A different/missing module means another DLL loaded. To return to stable, fully
exit, reinstall the linked stable ZIP and restart.

## Focused replay

- Check affected spells' `[CastChance]` notes against native failure percent and
  candidate values. Affected 20% sources should use benefit factor 0.8, while native
  unaffected items, sources and held-touch delivery keep factor 1. Chance changes
  should refresh on the next decision. Native failures must stay non-silent.
- Compare armor/shield reducers, no armor, granted abilities and TTT handlers in
  TB/RT. Spell names do not decide eligibility. Preserve useful spells rather
  than blacklisting unlucky failures.
- Preserve automatic Burning/DC/burn/off restoration, player ownership/disabled
  management, resistant-target avoidance, group Sleep, healing and cast safety.
- Replay the corridor/cloud escape. Existing Walk rows now label inspection goal/
  endpoint/gap; a stuck event adds native destination/path end/next waypoint.
  Include those rows; a queried path is not necessarily the execution route.
  Movement behavior is unchanged.
- Compare same-save/settings/mods ordinary actors, Orvath/summons and idle selection.
  Author reports preceding sessions unchanged 7.5/10; new build timing is untested.
  Send complete startup+AI logs, native CombatLog and the exact ZIP filename.

## Verification and identity

164 tests pass; Release has zero warnings/errors. 39 native/compiled chance guards
and retained observer/provider/infusion/cone checks pass. All 1,220 ordinary reader
details match release (4,097 exports loaded/one known failure, partial coverage).
No initialized actor, actual chance handler or Unity/Mono timing is run offline.

- Runtime source: `be0da2c5ff2d949ccf223c982dc2b01000c165c1`.
- Module: `bccf1618-8aa9-4a3d-9c3f-d68075ebafb8`.
- ZIP SHA-256: `018609c3e94c73fde180847c680ecc342d01687167ea1931bd1ebb641c068a2e`.
- DLL SHA-256: `4c20a8d6a8634b45d9d2240ab8ab5b5a5845a9e2187e40c803e3d9846225055c`.
- Archive: exactly checked mod DLL and unchanged manifest.
- [Identity](AI-Control-FACC-native-cast-chance-be0da2c-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- [Evidence and handover](../docs/RELEASE_0322_FIXES_REVIEW.md).
- Previous observer test: [649ed23](AI-Control-FACC-log2-fixes-649ed23-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-log2-fixes-649ed23-UMM-test.identity.json).
  [Its original handover](https://github.com/recovribox-svg/AIControll-FACC/blob/9301356fdd8405743f60958c8afe9678f4edcb81/test-builds/README.md).

## Earlier downloads

All prior ZIPs, identities and checksums remain unchanged. Their original
handover is retained below for reproducing earlier sessions; use the top link
for this test and its correct startup marker.

<details>
<summary>Previous follow-up and historical test instructions</summary>

# Test builds

**[Download the new UMM test ZIP — ea20a79](AI-Control-FACC-release0322-fixes-ea20a79-UMM-test.zip?raw=true)**

This **unmerged follow-up to released v0.3.22** fixes resistance appraisal when
Tabletop Tweaks' DR rework supplies a replacement component. It queries the
existing optional component's own preview; TTT is not required or included.
The Hand of Hosilla/Grease case gets **diagnostics only**. Movement behavior,
spell scoring weights, game speed and performance budgets are unchanged.

UMM still displays **0.3.22**. This is a separate test, not a replacement for the
[published stable ZIP](../release/CompanionAI_Pathfinder_v0.3.22.zip?raw=true).
No in-game acceptance or new performance improvement is claimed.
[Draft PR #13](https://github.com/recovribox-svg/AIControll-FACC/pull/13) targets development.

## Install and identify this test

1. Fully exit the game.
2. Install this ZIP through Unity Mod Manager's Mods tab.
3. If UMM will not replace a same-version install, extract the ZIP's two files to
   `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only; keep settings.
4. Restart and confirm this exact startup line in the complete AI log:

```text
[Build] v0.3.22 module=91661767-d748-41f6-8822-ec1492a08022
```

A different/missing module marker means another DLL loaded. To return to stable,
fully exit and reinstall the linked stable ZIP. Restart after every replacement.

## Focused replay

- With TTT DR rework active, meet a fresh resistant Dretch/Quasit. Before damage
  observations, look for `[Math] ... TTT estimate` or `TTT immunity` where its
  replacement part is installed. Compare direct Fire/Burning values with actual
  native damage/resistance; a zero-valued Burning infusion may correctly stay off.
- Check susceptible targets and automatic infusion activation, burn, off/restoration,
  player-owned toggles and disabled management in TB/RT. Compare TTT absent/rework
  off, immunity and changing pools. The provider preview is approximate; full
  stacking/bypass parity still requires replay.
- Replay the reach/Grease encounter. `[Reach]` records already-scored approaches,
  risk and bounded current foe attack/distance evidence. It does not make the
  party back away or guarantee pursuit. No movement behavior change is intended.
- Preserve group Sleep, heals and silent-cast safety. Check ordinary companions,
  Orvath/summons and idle selection on the same save/settings/mods.
- Send the complete AI log **including startup**, native **CombatLog**, exact ZIP
  filename and observations. CombatLog is useful: it confirms damage, resistance,
  saves, heals and ordinary versus opportunity attacks that AI logs cannot prove.
  Energy samples are capped at 24 rows/fight, so later cases may not print.

## Checks and identity

164 unit tests pass; Release has zero warnings/errors. Actual supplied Core 0.7.15
binding, optional absence and incompatible API fallbacks pass. Retained native/
compiled infusion, cost, memo, cone, Selective, concentration and HD checks pass.
All 1,220 ordinary reader details match release (4,097 exports loaded/one known
failure, partial coverage). No initialized actor, live resistance/pool/handler or
Unity/Mono timing test was run.

- Runtime source: `ea20a79082a56ef226446d31ef29d5b4c08277c5`.
- Module: `91661767-d748-41f6-8822-ec1492a08022`.
- ZIP SHA-256: `d1378472f5d711a7ed458b942f3db3677872910adbb870889b1580fb356351d5`.
- DLL SHA-256: `35594d2719c3858c54839f191363d18050587d893f59c56502c80d694f165c70`.
- Flat archive: exactly the checked mod DLL and unchanged `Info.json`.
- [Identity](AI-Control-FACC-release0322-fixes-ea20a79-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- [Evidence, limits and handover](../docs/RELEASE_0322_LOG_REVIEW.md).

## Archived earlier tests

The following is the historical Test[5] handover. Its PRs are now integrated in
released 0.3.22; its old 0.3.21 test metadata and checksums are retained unchanged.
Use the download at the top for this follow-up, or stable for ordinary play.


**For v0.3.22, use the [versioned UMM package](../release/CompanionAI_Pathfinder_v0.3.22.zip?raw=true).**
[Install and release identity](../release/README.md). The older test ZIPs below retain their original
0.3.21 metadata and checksums for reproducing historical logs.

## Test[5] fixes playtest — 25434a2

**[Download the new UMM test ZIP — 25434a2](AI-Control-FACC-test5-fixes-25434a2-UMM-test.zip?raw=true)**

Corrects a confirmed outcome bug: a visible failed concentration check could teach a silent/no-reach strike
against a useful spell/target. Matching native failures now supply refusal evidence; genuine silent-effect
protection remains. No casting-risk, resource, movement, target-commitment or cancellation policy change.

The author rated the preceding Test[5] **8/10 smoothness**, improved over Test[4]. This preserves prior
optimizations and decision scope. Sleep scoring stays unchanged: the reported Quasit-versus-human-cluster
choice lacks enough competing-aim evidence to justify new weights. Bounded diagnostics now expose existing
Sleep aim values/rejections and energy-estimate return routes. **No resistance fix or new speedup is claimed.**

[Draft PR #11](https://github.com/recovribox-svg/AIControll-FACC/pull/11) targets development and includes pending
PR #10/#9/#8/#7/#6. Main/shared development are unchanged. This is an unmerged test for intended v0.3.22;
release metadata remains 0.3.21. The withdrawn broad implementation remains withdrawn.

## Install and identify

1. Fully exit the game.
2. Install/update the ZIP through Unity Mod Manager's **Mods** tab.
3. UMM still shows **0.3.21**. If it will not replace a same-version install, extract these two files into
   `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only. Keep settings.
4. Restart and verify the complete log contains:

```text
[Build] test5-logic-fixes module=5bc34728-8614-4cd9-a9ef-046868de2d4f on v0.3.21
```

A different/missing marker means another DLL loaded. For normal play, fully exit and reinstall stable
v0.3.21. Send complete AI logs and native **CombatLog**, this ZIP filename and observations.

## Focused playtest

- Both modes: an ordinary failed concentration check should produce `[Cast] ... native failure, not counted
  as silent`, with no silent/no-reach strike for that event. Genuine silent-effect cases should still be detected.
  Live native/third-party event ordering needs replay; offline checks do not prove it.
- Replay TB7's Quasit/human cluster. `[HD-Aim]` shows up to 12 already-scored centres, existing values/rejections,
  scored risks and up to 3 HD-eligible recipient IDs each. Eligibility is not guaranteed positive benefit or
  a failed save. Rows precede selected-path preparation/policy; chosen current value is printed separately.
  All remaining centres still score; no new cluster bonus, priority, repeated-save blacklist or extra query.
- Fire/Burning against resistant targets: include `[Math] energy estimate` and native damage packets.
  The existing raw/native/observed/final values and return route are logged separately for direct/infusion reads,
  at most 24 rows/fight. Other energy reads can exhaust that sample. Initial resistance optimism is **unresolved**;
  actual criticals sometimes penetrate, so there is no blanket Fire blacklist.
- Recheck native infusion activation/burn/off/restoration, player ownership and management off. The prior
  logs confirm five successful Burning activations; they do not gameplay-validate every supported infusion.
- The committed-approach Grease safety guard remains narrow and respects actual Selective protection.
  Vanilla Selective Grease that spares allies bypasses the veto. A TTT setting that makes a lasting area
  affect allies remains guarded. No broader path/movement guarantee; no discovery change for the second
  blast the author confirmed was not actually granted.
- Same save/settings/mods: compare ordinary companions, Orvath/summons and idle selection in both modes.
  New bounded diagnostics allocate small notes/keys; live performance parity still needs replay.

## Verification and limits

**164 tests pass**, zero failed/skipped. Release builds against supplied game/UMM references with zero
warnings/errors. Compiled concentration/native bindings, Selective chain and HD diagnostic boundaries pass;
six detached vanilla Grease off/on/off assertions yield affects/spares/affects allies. No initialized actor,
TTT/AutoMetamagic handler, gameplay rule or spawn is run by these fixtures. Energy trace return/query scope
has an independent source review.

Retained checks pass: 13 memo assertions, 5 Grease gates, 20 activation metadata cases, Free/Swift thresholds,
18 recognized/two withheld layouts, 486 native cost/quiver vectors, Rare Metal,cone bindings/metadata and
compiled activation/resistance routes. Partial ordinary reader: 4,097 loaded exports, one known failed export;
1,220 rows, zero reader errors, all normalized details unchanged from the preceding build.

**No in-game test of this new build.** Concentration settlement and diagnostics need live acceptance.
Unraveling/Chain Arrows remain withheld; Deep Slumber keeps its spreading restriction. No new Gather,
metakinesis or form optimizer. Separate unacted Aid Another/no-reach ordering remains an investigation lead.
[Full review](../docs/TEST5_REVIEW.md) · [Infusion support](../docs/INFUSION_SELECTION_PLAN.md).

## Identity

- Runtime source: `25434a2332a467992a3385e4b1722049c1e54fbb`.
- MVID: `5bc34728-8614-4cd9-a9ef-046868de2d4f`.
- ZIP SHA-256: `bf91fc8ad1dabdbe4645071a54b1581c4f8e6018ec24b89f2c654ee5482bf6fd`.
- DLL SHA-256: `74734f45efed6f91930d3b2019395dec28a7d58749b481523734055de2576bee`.
- Flat archive: exactly checked `CompanionAI_Pathfinder.dll` and unchanged `Info.json`.
- [Versioned identity](AI-Control-FACC-test5-fixes-25434a2-UMM-test.identity.json) · [SHA256SUMS](SHA256SUMS.txt).

## Earlier downloads retained

- [Test[4] fixes — c6af593](AI-Control-FACC-test4-fixes-c6af593-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test4-fixes-c6af593-UMM-test.identity.json).
- [Native command fixes — aa9fe94](AI-Control-FACC-test3-fixes-aa9fe94-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test3-fixes-aa9fe94-UMM-test.identity.json).
- [Infusion selection — 05d1959](AI-Control-FACC-infusion-selection-05d1959-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-infusion-selection-05d1959-UMM-test.identity.json).
  **Known failed activation path; use the newest build for further testing.**
- [Test[1] scoring fixes — 8fb0257](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.identity.json).
- [Original Test[1] — 0a7ed97](AI-Control-FACC-infusion-value-0a7ed97-UMM-test.zip?raw=true)
  and [identity](build-identity.json).
- [Optional diagnostics — 664815b](AI-Control-FACC-infusion-value-664815b-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-infusion-value-664815b-UMM-test.identity.json).

Prior filenames/checksums remain unchanged for exact log-to-build matching.

</details>
