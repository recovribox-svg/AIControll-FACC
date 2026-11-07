# Test builds

## Current test build: v0.3.29-dev-test5 (9a75cbd) - no friendly fire on allies walking in, no circling

**[Download the UMM test ZIP (9a75cbd)](AI-Control-FACC-v0.3.29-dev-test5-9a75cbd-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.29-dev-test5-9a75cbd-UMM-test.zip>

Test 4 (below) plus two real-time fixes from your test 4 logs ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §117):
- **Allies walking into a cone or blast count as caught.** Orvath's Fan of Flames was aimed past Seelah and Camellia
  where they stood, but both were walking into its path and took 9 each when it landed (Seelah dropped to 4 HP). The
  check now also looks at where walking companions will be when the spell lands. The log says
  `catches Seelah (walking in)`.
- **No more circling next to enemies.** A walk the game already counts as arrived used to go on until the walker
  stopped moving, so companions pushed round an enemy's body for 2-7 s. It now ends about half a second after arriving.
  The log says `arrived by the game's own test ... - the walk ends`.
- Orvath's forward cone casting and close-range shooting are unchanged: by the game's own rules enemies hit him about
  1 time in 10 (armour class 29), so standing there costs him almost nothing. Other companions still step back.

UMM shows **0.3.29**. Startup line `[Build] v0.3.29 module=9e40ed9f-390a-415c-ad46-b46af6b505ab`.
- ZIP SHA-256 `393738433a22fb428413e4fd18879e01c5515712a4532b5c54427a7ab040d155`; DLL SHA-256 `420dc1bc9dbe40daa0969e20665f0be28240ed45788b1101cb1f9f11e6254e2a`.
- [Identity](AI-Control-FACC-v0.3.29-dev-test5-9a75cbd-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 477 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors; two new game references,
  both public game functions. Not played yet.
- **Test:** real-time fights where Orvath uses Fan of Flames or another non-selective area while melee companions run
  in; and melee companions advancing on enemies: they should stop and fight once in reach, not circle.

## Test 4: v0.3.29-dev-test4 (090023a) - companions take a free side of the enemy (also in test 5)

**[Download the UMM test ZIP (090023a)](AI-Control-FACC-v0.3.29-dev-test4-090023a-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.29-dev-test4-090023a-UMM-test.zip>

Test 3 (below) plus body-blocking fix 1, which you approved ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §116):
- **Real time - no two companions aim for the same spot.** Before walking up to an enemy, a companion checks whether the
  spot it would stand on is taken: by a companion standing there, by one already walking there, or by another enemy
  beside the target. If it is, it walks to the nearest free side instead (often flanking). The log tag is
  `(to a free side: Seelah stands where it would)` or `... walks to where it would`.
- **The walk ends as soon as it can swing.** On the way to that side, the companion stops and attacks the moment the
  enemy is in reach. The log says `... is in reach of its weapon - the walk to a free side of it ends`.
- **No room at all:** a companion doesn't walk to an enemy that is fully surrounded, and picks another enemy or action.
  The log says `no free side beside ...` (once a fight per companion and enemy).
- Turn-based fights are unchanged (the game already handles it there). Heals and spells keep their usual approach.
- Cost: a few distance checks per decision. Only when the spot is taken does it price up to four other spots. The
  slow-decision line counts them (`crowded approaches searched for a free side`).

UMM shows **0.3.29**. Startup line `[Build] v0.3.29 module=fd38dde0-9b19-4290-bba5-1606b45874f0`.
- ZIP SHA-256 `0287627fc021dde561ea4222f87518ab8e4ead15e4deddbcfae8b9d835f28053`; DLL SHA-256 `9b2af9babc55ed42d7f5db7180f106191fc2ec6c25a2ecf206e7d3a4cb2ecd0c`.
- [Identity](AI-Control-FACC-v0.3.29-dev-test4-090023a-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 474 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors; no new game references. Not
  played yet.
- **Test:** real-time fights where two or three melee companions go for the same enemy, especially at the start of a
  fight. They should spread round it instead of queueing behind each other, and fewer `Stuck command` lines should
  appear.

## Test 3: v0.3.29-dev-test3 (71959e6) - healers stop walking once they can touch (also in test 4)

**[Download the UMM test ZIP (71959e6)](AI-Control-FACC-v0.3.29-dev-test3-71959e6-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.29-dev-test3-71959e6-UMM-test.zip>

Test 2 (below) plus one real-time fix from your test 2 logs ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §115):
- **Real time - a walk to touch an ally ends as soon as the touch reaches.** Lann walking to heal Camellia with a Cure
  Light Wounds item circled her for 7.6 s: he was close enough to touch her, but not yet at the spot the walk aimed for.
  The walk now ends the moment the game says the touch reaches, and the companion casts. The log says
  `... is in reach of its Cure Light Wounds touch - the walk to it ends`.
- Your test 2 showed R1-R3 working, no mod errors and nobody of the party down. R3 cost at most 6.4 ms in a slow
  decision.

UMM shows **0.3.29**. Startup line `[Build] v0.3.29 module=a5d5b19e-da23-4aaf-bd93-d6037457f66d`.
- ZIP SHA-256 `96135845e799b67d0bb91589790cb1778771c2074fb520cc8968dbd140dd600e`; DLL SHA-256 `44adcc68f68bc8010b5f3c1776f1aeb177e54ed10354feb30815cfc66f547132`.
- [Identity](AI-Control-FACC-v0.3.29-dev-test3-71959e6-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 466 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors; no new game references. Not
  played yet.
- **Test:** real-time fights where a companion heals an ally by touch (Cure Light Wounds spell or wand) in a crowd: it
  should cast as soon as it is next to the ally, not circle them.

## Test 2: v0.3.29-dev-test2 (6221118) - melee companions answer who engages them (also in test 3)

**[Download the UMM test ZIP (6221118)](AI-Control-FACC-v0.3.29-dev-test2-6221118-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.29-dev-test2-6221118-UMM-test.zip>

Test 1 (below) plus the three changes you approved ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §114):
- **R1, real time - fight who engages you.** A melee companion walking to a distant enemy now stops to think when a
  nearer enemy engages it on the way, even one standing next to its target, as long as it can hit that enemy back. It
  then compares fighting it there with walking on, and walking on now pays for the free attack it would take. The log
  says `... engages it on the way ... it can hit back from here`.
- **R2, real time - blocked walks end after 2 s.** A companion walking on the spot, boxed in by bodies, gives up after
  2 seconds instead of 6 and picks something else. The log says `Stuck command ... blocked: walking on the spot`.
- **R3, both modes - heavy hitters judged by their real blows.** The chance of going down next to a melee enemy now also
  looks at its actual hits (hit chance, damage per hit, critical hits) and takes the higher reading. A low-HP companion
  is more careful around big hitters, especially late in a fight. Healthy front-liners are unchanged.
- **Cost:** the new calculation takes under a microsecond. Each enemy's attack against each companion is read once a
  round. The slow-decision line in the log shows how often (`enemy attacks read for the drop chance`).

UMM shows **0.3.29**. Startup line `[Build] v0.3.29 module=add726f5-13c2-4f48-ab1e-cf3a3c819b70`.
- ZIP SHA-256 `9ce6385cc7d5025816620f67b5ea1cd0d19f6542d640ba0966986cf383f54f40`; DLL SHA-256 `3ae198257be22cc4d44038b757b7bdb42b9d539a3cdc29b58a28b239d92f2432`.
- [Identity](AI-Control-FACC-v0.3.29-dev-test2-6221118-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 466 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors; no new game references. Not
  played yet.
- **Test:**
  - real-time fights where a melee companion walks past weaker enemies towards a stronger one: it should fight what
    engages it;
  - crowded real-time fights: no companion should stand pushing against bodies for long;
  - low-HP companions next to heavy hitters, either mode: they should step back more readily;
  - slow-decision lines: check the new attack-read count.

## Test 1: v0.3.29-dev-test1 (d752fa8) - two fixes from the test 15 logs (also in test 2)

**[Download the UMM test ZIP (d752fa8)](AI-Control-FACC-v0.3.29-dev-test1-d752fa8-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.29-dev-test1-d752fa8-UMM-test.zip>

v0.3.28 (released) plus two fixes from your test 15 logs ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §113):
- **No more unused turn when every walk is just too long.** In turn-based fight 6, Lann ended round 1 doing nothing.
  His three walk-then-shoot options were each slightly longer than one move. The check that refused them also held back
  his plain advances. Now an advance is still offered, as it was before that check existed.
- **Paralysing enemies rated sensibly.** The Ghoul Creeper's several hits each counted a full paralysis, so it read about
  twice as dangerous as it is (63.9 a round against 24-31 dealt). A second paralysis in the same round costs no more
  turns, so the hits now count once over for that.
- Also there: a proposal for the real-time melee targeting you reported (Seelah and the Earth Elemental), built in test 2.

UMM shows **0.3.29**. Startup line `[Build] v0.3.29 module=67c56db1-76e4-40bb-bfac-83ed603be6b6`.
- ZIP SHA-256 `be320cd6d58e8d22d85223b25365ccf44542881756fbb5143a82ed5fd7bbd8c5`; DLL SHA-256 `41a15ecf3eeed8f89b030536a63cb40545a484e0636499c5477e7c7f7aab59b3`.
- [Identity](AI-Control-FACC-v0.3.29-dev-test1-d752fa8-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 461 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors; no new game references. Not
  played yet.
- **Test:** turn-based fights where enemies start far away: archers and casters should advance when they cannot reach
  anyone this turn, not end the turn idle. Fights with ghouls: `[Threat]` should rate them closer to what they deal.

## Test 15: v0.3.28-dev-test15 (2415cf8) - standing in a swarm priced as danger (released as v0.3.28)

**[Download the UMM test ZIP (2415cf8)](AI-Control-FACC-v0.3.28-dev-test15-2415cf8-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test15-2415cf8-UMM-test.zip>

Test 14 (below) plus the movement change you approved: standing inside a swarm is now priced as danger
([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §112). The AI follows the game's own swarm rules. A swarm carries a small
space around it (5 ft for the Mandragora swarm). Every party member inside is hit by its round, as long as one of them is
right at its centre. The Raven swarm and Creeping Doom hit everyone inside every round. Until now a swarm counted like a
melee enemy without a weapon: standing in it cost no more than standing next to it, and nothing made anyone walk out.
- **Inside a swarm** the whole round counts for everyone it hits, not shared out among them. **Just outside** it counts
  half (the swarm has to come to you first), shared among those it could reach.
- **Walking into a swarm** is priced like walking somewhere dangerous; the log tags it `[in Mandragora Swarm's swarm]`.
- **Approaching an enemy next to a swarm**, the AI looks for a spot in reach of that enemy but clear of the swarm; the log
  says `(clear of Mandragora Swarm's swarm)`.
- **Already inside**, a companion may take a **5-foot step or a short move out of the swarm**. It does so when the danger it
  escapes is worth more than what it gives up. Spells and ranged attacks still work from the new spot. A melee attack is
  lost if its target is out of reach from there. A fighter who is beating the swarm stays while its attacks are worth more.
- The log notes each swarm once a fight: `[Swarm] Mandragora Swarm: its round strikes every party member in its space ...`.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=794e6682-f29b-426a-a940-680ed813c83c`.
- ZIP SHA-256 `eaf819611a872275f61964a89f154b7f03eb971aaabab5f29a3893c52f532589`; DLL SHA-256 `a3461a0e67ba1bb60679148eb4d32f467d4e9eaf2c0e6562d98296743e2d75e3`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test15-2415cf8-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 457 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors (new swarm-space check: 15 swarm
  spaces read, 15 of the 16 swarm buffs that carry one strike each round). Not played yet.
- **Test:** a fight with Mandragora (or rat, locust, spider) swarms, turn-based and real time. Casters and archers caught in
  a swarm should step or walk out of it (`5ft step out of ... swarm`, `Move out of ... swarm`); others should not walk into
  one without reason. A front-liner with good HP may still fight a swarm from inside.

## Test 14: v0.3.28-dev-test14 (eb73349) - Mandragora swarms and poisoned weapons valued by what they do (also in test 15)

**[Download the UMM test ZIP (eb73349)](AI-Control-FACC-v0.3.28-dev-test14-eb73349-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test14-eb73349-UMM-test.zip>

Test 13 (below) plus your request: the Mandragora swarm's poison and weapon poisons ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §111).
The AI now reads what an enemy's hit, or a swarm's round, does beyond hit points, from the actions the game itself runs:
- **Swarms** (Mandragora swarm): each round in its space is 5d6 damage, a save or a round of nausea, and a save or its
  poison (more saves, Constitution damage, confusion). Before, the swarm's round was not counted at all.
- **Poisoned hits** (giant spiders' bite, poisoned or draining weapon enchantments, a Shadow's Strength touch): each hit
  counts its save, the poison's doses over the next rounds, the score they damage and any control it causes.
- **Saves and immunities** are read per target: a high Fortitude save makes a poison worth less, and a target immune to
  poison, to that score's damage or to negative levels gets nothing from it.
- **The party's own poisoned weapons** count the same way when the AI compares a weapon attack with its other options.
- The `[Threat]` line names it: `(X of it beyond hit points: swarm: ...)`.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=a3caf884-f21e-49e4-aa34-52758a29f726`.
- ZIP SHA-256 `8f8f1428e4bea326737d0ae30c83cdf54806c51b033f5af1bf22f15ef1ef4e2c`; DLL SHA-256 `21b7ab720e4a005a8a7da986159a14615f60162882a76654a5b62880dc60bac7`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test14-eb73349-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 450 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors (new affliction check: the giant
  spider's bite, the Shadow's touch and the Mandragora swarm's round read correctly). Not played yet.
- **Test:** a fight with Mandragora swarms or giant spiders. `[Threat]` should rate them higher than before and name the
  swarm or poison part, and the party should go after them sooner. (Standing inside a swarm is priced from test 15 on.)

## Test 13: v0.3.28-dev-test13 (1b74af6) - ability drain and negative levels valued by the game's rules (also in test 14)

**[Download the UMM test ZIP (1b74af6)](AI-Control-FACC-v0.3.28-dev-test13-1b74af6-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test13-1b74af6-UMM-test.zip>

Test 12 (below) plus your request: ability damage and energy drain recognised for what they do ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §110).
In this game a score taken below 1 kills the creature, and as many negative levels as its character level kill it, so both
are a second and third "life bar". Until now the AI counted them as a little hit-point damage (Enervation's 1d4 levels as
2.5 damage). Now:
- **Ability damage** (Strength, Dexterity, ... - Shadows, Polar Ray, Wracking Ray, diseases) is worth the share of the
  score it takes, applied to the target's remaining hit points; taking the whole score counts as a kill.
- **Negative levels** (Enervation, Energy Drain, Nabasu spells) are worth their real 5 HP each plus the share of the way to
  the target's level; enough levels count as a kill. Enervation on a level-4 enemy with 30 HP is now worth about 23, not 2.5.
- **Poisons** count every score they damage, not only Constitution.
- **Immunities** are respected (undead without Constitution, Death Ward, creatures immune to ability damage).
- This applies everywhere: what the party casts, what enemies are rated by (`[Threat]`), and the danger of standing next to
  them. The ability list in the log says `ability damage` / `negative levels` for such spells.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=76028a95-9ba1-43ce-8250-abb0aecaa5ef`.
- ZIP SHA-256 `4f9769b5993fe8d57ce0b1cd87d0b7ce8b3c940130cf9471c24afbcd99d888e5`; DLL SHA-256 `6feb12e87683113a307bd9d7e3b3370572ce2c2f5f64160907f659641c9d0767`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test13-1b74af6-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 444 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors (new drain check: 8 negative-level
  and 27 ability-damage spells read correctly). Not played yet.
- **Test:** fights with Shadows or a Nabasu: `[Threat]` should rate them above x0.5, and the party should go after them
  earlier. A caster with Enervation or Polar Ray should now pick them more often against suitable targets.

## Test 12: v0.3.28-dev-test12 (03d3f6a) - cones that spare the party, Shadows taken seriously (also in test 13)

**[Download the UMM test ZIP (03d3f6a)](AI-Control-FACC-v0.3.28-dev-test12-03d3f6a-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test12-03d3f6a-UMM-test.zip>

Test 11 (below) plus the review of its log and your cone / ability-damage questions ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §109):
- **Fan of Flames (and every cone) and your front line.** Three of four Fan of Flames casts hit Seelah or Camellia. The AI
  did count allies in the cone, but when Orvath had to walk into range first it priced the cone from a spot up to 1.6 m
  further forward than where the game actually stopped him - so allies just in front of him were "behind" the cone in
  the plan and inside it for real. Now:
  - a cone that needs a walk walks to the spot it was priced from first, and is aimed on the next decision from where he
    really stands;
  - a cone that would catch allies is also tried turned 12-36 degrees past them (cones can be aimed at the ground, so the
    flames go beside the ally instead of through them); the better aim wins;
  - if allies would still be caught and that costs more than the enemies are worth, the cone loses to other options
    (before, that cost was cut off at zero and other parts of the cast could still make it look good);
  - decision lines say who a cone would catch, e.g. `[catches Seelah -52]`, or `[turned +24 deg past Seelah]`.
- **Shadows (Strength drain) now count as a real threat.** They were rated the least dangerous enemy in every fight
  ("Shadow x0.5, attacks 0.8"), because the threat rating only counted hit-point damage. Their Strength damage now counts
  as the share of the victim it takes; the `[Threat]` line shows it, for example `attacks 5.9 on Seelah (5.1 of it Strength damage)`.
- **Soul Power after fights waits for you.** Your Dispel Magic casts after fights replaced the harvest each time (16 of 23
  cut off). It now starts only when Orvath has had no order for 3 seconds, and gives up after 5 cut-offs until the next fight.
- **A cone is no longer marked as having "missed" its target** while its flames are still travelling (that mark made the
  AI stop counting the enemy in its areas after two casts).

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=fc4b18a2-a859-4465-b8dd-c73ea85a0633`.
- ZIP SHA-256 `8d873e22dab6c2e1ae6453681f79180f51cb6de417a5c566ea9e9448f0f813ae`; DLL SHA-256 `b2f15e2efa6259d1d32737fa32a79bf1434c836230ed083d712522a1da1c9a7c`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test12-03d3f6a-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 434 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors. Not played yet.
- **Test:** a fight where Seelah or Camellia stand in melee in front of Orvath: Fan of Flames should go past them (look
  for `turned` or `walks to its casting spot first` on his decision line), or he should pick something else. With Shadows
  around, `[Threat]` should rate them above x0.5. After a fight, cast your Dispel Magic: Soul Power should follow once you stop.

## Test 11: v0.3.28-dev-test11 (4d6036e) - smoother harvest, lighter decisions, shorter log (also in test 12)

**[Download the UMM test ZIP (4d6036e)](AI-Control-FACC-v0.3.28-dev-test11-4d6036e-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test11-4d6036e-UMM-test.zip>

Test 10 (below) plus the review of its log ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §108):
- **Soul Power after fights waits until the party stops walking.** It is a 6-second cast: in the test 10 log it was
  started 21 times after fights and your party's walk cancelled 14 of them, so Orvath kept halting behind. It now starts
  once everyone has stood still for 2 seconds.
- **Less memory garbage in kineticist decisions** (the cause of the occasional 0.7-1 s pause): a blast's damage was worked
  out with a fresh game "context" for every enemy it was compared on; the numbers do not depend on the enemy, so one is
  reused. Decisions are unchanged.
- **Shorter log:** repeated ability lists show only what changed, one turn-start line instead of three, revive details in
  one line, repeated "held back" and energy lines printed once. About 80 KB less in a log like test 10's (611 KB).

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=7aa18b9e-c844-477e-854b-d86589fb09f7`.
- ZIP SHA-256 `b21be61c7e528dbdf68503b0bcfc50d085984f7d52787d26e64ee1c382a85e61`; DLL SHA-256 `d6dbfb11594ce6b113bde015aedcd1b403b1467e8222ef3b2d2777f533618019`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test11-4d6036e-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 423 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors (new damage-context check: all
  170 blasts share one context). Not played yet.
- **Test:** after a fight, walk on at once: Orvath should follow without stopping, and cast Soul Power when you stop.
  The `[Perf] slow decision` lines should show less `heap +` growth for Orvath (it was +4-6 MB).

## Test 10: v0.3.28-dev-test10 (f5faa25) - Dark Elementalist (also in test 11)

**[Download the UMM test ZIP (f5faa25)](AI-Control-FACC-v0.3.28-dev-test10-f5faa25-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test10-f5faa25-UMM-test.zip>

Test 9 (below) plus the Dark Elementalist review ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §107). Intelligence for blasts,
DCs and the 3-burn cap were already handled (the AI reads them from the game). What changes:
- **Soul Power by the game's real rules:** it works only when the dead bodies within 30 ft (not yet harvested) add up to
  Orvath's character level, then it heals 1 burn and adds +1 Elemental Overflow until rest - which stops at a third of his
  kineticist level (2 at level 6). The AI now counts the bodies the way the game does, and values a cast by the burn it
  heals and by an Overflow rank only while under that cap (an older log shows 24 casts for nothing more).
- **Harvest after fights:** right after a fight, while the bodies still lie around, Orvath casts Soul Power when it would
  work and is worth it - no combat turn spent. Look for `[Kinetic] Orvath: out of combat, Soul Power - harvest: souls ...`.
- **Fox's Cunning (or Bear's Endurance for a normal kineticist)** is now valued for the blast damage it adds.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=eeb140e7-6d84-43b2-bbcc-a0e1cf8e0529`.
- ZIP SHA-256 `952bc475e6f78c42e9e5b99b3884cd003620840b1d7ebf5ad8dc93636a2f45e3`; DLL SHA-256 `5387b5c38087263eaf8bcbb51b5b927296781d3fa9c71db62a316a8453bce633`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test10-f5faa25-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 423 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors. Not played yet.
- **Test:** after a fight with several kills near Orvath while he has burn, he should cast Soul Power once (burn goes down);
  in fights, Soul Power should only appear when enough bodies lie within 30 ft.

## Test 9: v0.3.28-dev-test9 (18fba94) - the quiver explained (also in test 10)

**[Download the UMM test ZIP (18fba94)](AI-Control-FACC-v0.3.28-dev-test9-18fba94-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test9-18fba94-UMM-test.zip>

Test 8 (below), reviewed on its play-test (13 fights, no mod errors, nobody down in turn-based). [docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §106.
- **The quiver:** Orvath's kineticist part comes from **DarkElementalistBurnFeature** (most likely the Isekai Protagonist's
  Dark Elementalist Legacy). That part has no quiver, uses Intelligence as the kineticist main stat and the Dark
  Elementalist burn pool. Your in-game check agrees: Exploding Arrows still cost burn with a quiver. This is the save's
  character build, not something AI Control can change; the AI correctly leaves Create Quiver alone.
- The note is now logged once a session (it was repeated every fight) and names the main stat.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=fd940d66-16b4-4fad-8d02-d5e97e6e9be0`.
- ZIP SHA-256 `03b0690ebed4dc6127bb468307cf5e61e2964fee3f274fc42d49028e2c73270b`; DLL SHA-256 `a98e1d7ac191e95b84cb92a07d363b21c72d9c0dc18a97c524d107623a877751`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test9-18fba94-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 413 unit tests pass; release build 0 warnings. Not played yet.

## Test 8: v0.3.28-dev-test8 (94d0835) - test-7 log fixes (also in test 9)

**[Download the UMM test ZIP (94d0835)](AI-Control-FACC-v0.3.28-dev-test8-94d0835-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test8-94d0835-UMM-test.zip>

Test 7 (below) plus fixes from its logs ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §105):
- **Stepping out of reach, as agreed:** only for a companion whose own weapon cannot reach the enemy hitting it (and who
  is not fighting anyone else in reach). In test 7 it also fired in the wrong cases: Camellia hit the Cultist Champion and
  then walked away from it (taking an attack of opportunity), and in real time Seelah walked off the Mongrel she was
  beating between two swings. Archers no longer use it; they keep their own "back off out of melee" before a shot.
- **"Back off out of melee" (real time)** now counts the chance the attacks of opportunity stop the walk. Camellia,
  entangled at 6 HP, backed off past two attacks that dropped her 97 % of the time.
- **The quiver, found:** in your game Orvath's kineticist part has no quiver at all. The game sets it up once, from the
  first kineticist feature that turns on, and only takes burn from arrows through it, so arrows would never pay for his
  blasts, even if you made a quiver by hand. The AI was right to leave Create Quiver alone. The log now names the feature
  that set the part up: look for `[Kinetic] Orvath: ... the game gives it no quiver: its kineticist part comes from ...`.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=54337c75-a6f2-424d-b5bc-769ebc4849f5`.
- ZIP SHA-256 `69fe08eccdba62aa3d4c713cec1e7a9333034d8b2da68452acabf9f167e71cbc`; DLL SHA-256 `20946fce1dbb16aaa6820da563a9f799a466decca893cb6736aafff0240cbec5`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test8-94d0835-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 413 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors. Not played yet.
- **Test:**
  - Send the `[Kinetic] Orvath: ... no quiver` line: it names the feature behind it. In game, a quick check: cast Create
    Quiver by hand and see whether Exploding Arrows still costs burn.
  - Front-liners should no longer walk away from an enemy they are fighting; at the edge of a Grease, out of a
    reach-weapon enemy's reach, they still may.

## Test 7: v0.3.28-dev-test7 (f180d9b) - reach and Grease, quiver reasons (also in test 8)

**[Download the UMM test ZIP (f180d9b)](AI-Control-FACC-v0.3.28-dev-test7-f180d9b-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test7-f180d9b-UMM-test.zip>

Test 6 (below) plus the fixes from its play-test ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §104):
- **Reach enemies in your own Grease (the Ferocious Schir, fight 7).** The AI now knows the difference between an enemy that
  can hit a spot from where it stands and one that must walk there first: a walk costs it its full attack (one swing
  instead), and out of a slippery area only if it stays on its feet. A companion that cannot hit such an enemy back may take
  a 5-foot step (or a short move) just out of its reach, at any HP, when that is worth more than what it gives up -
  including leaving an ally the enemy would then turn on, so front-liners guarding a caster hold. If the enemy comes out of
  the Grease to follow, the companion's attacks are back; walking into the Grease to hit it stays possible when it is worth
  the risk (the enemy nearly dead, for example).
- **The Kinetic Sharpshooter's quiver.** Test 6 never made a quiver and never said why. Every reason it stops is now
  logged, once: `[Kinetic] Orvath: quiver not prepared out of combat - <reason>` or `out of combat, no quiver - <reason>`,
  and in a fight `no quiver refill - <reason>` once.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=c786e760-c342-4fc4-9e1e-f130a36152f8`.
- ZIP SHA-256 `5b67e4cd29c5cf016f4f867ee62abe628529884b26c0f32ac26f15dc97e74e05`; DLL SHA-256 `1fe5b23b1ecd3742137f26672c9bf555c7f68d076938f2864c6798ef70fe3ef1`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test7-f180d9b-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 413 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors. Not played yet.
- **Test:**
  - A melee companion at the edge of a Grease with a reach-weapon enemy inside it: look for `5ft step out of <enemy>'s
    reach` or `Move out of <enemy>'s reach` on its `[Tactics]` line; when it stays, a `[Reach]` line (debug log) says why.
  - After any fight with Orvath: send the log lines with `quiver` in them; they name what stops the quiver.

## Test 6: v0.3.28-dev-test6 (2765e04) - kineticist and archer engine (also in test 7)

**[Download the UMM test ZIP (2765e04)](AI-Control-FACC-v0.3.28-dev-test6-2765e04-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test6-2765e04-UMM-test.zip>

Test 5 (below) plus the engine work you asked for ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §103). Every number is read from
the game at the time, so it follows the character as it levels: burn per day and per round, arrows per quiver,
Elemental Overflow, infusion costs and their reductions, Deadly Aim's size, critical range and multiplier.
- **Quiver kept for the costliest shot.** The AI keeps at least as many arrows as its costliest blast needs, counting a
  substance infusion it may switch on in the fight (Entangling, Pushing...), not only the one on now.
- **Quiver refilled in a fight when it pays.** When the arrows run short, Create Quiver competes with the other options:
  it is worth the better blasts the new arrows allow, for the rounds they last, less the turn and the burn.
- **Arrows have a price**, a share of one burn (it gets cheaper as the quiver grows), so cheaper blasts are preferred when
  the costly one adds little.
- **Burn and Elemental Overflow** compared fairly: Overflow counts as the damage it adds to blasts that hit, and only when
  it makes a blast better than the best other option, spells included.
- **Critical hits for blasts and other attack rolls.** A bow blast crits with the bow (x3 for a longbow), rays x2. Weapon
  criticals are also no longer overcounted when the hit chance is low.
- **Deadly Aim on bow blasts.** A bow blast is a bow attack, so Deadly Aim applies to it. The AI now weighs it and
  switches it on or off for the shot.
- **Enveloping Winds and Shroud of Water** are valued by what one more cast adds: +5 % miss chance against ranged attacks
  (for the share of the danger that comes from ranged attackers), or +1 AC up to the shroud's limit. Each is then weighed
  against its burn.
- **Stutter evidence:** slow-decision lines now name how many contexts and damage estimates each costly ability made.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=3402358f-f2ee-433f-af3a-fdc6c4755e8d`.
- ZIP SHA-256 `e04bb6289e59fb9ded3225e077af16313102efae5492d530b381d2ddc8bc7410`; DLL SHA-256 `f41160638556bb7e2ed518fbee53d54b10808193f014d59f192a31c7b877d9ac`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test6-2765e04-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 406 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors. Not played yet.
- **Test:**
  - Orvath with a longbow and Deadly Aim learned: look for `[Deadly Aim on +N]` on blast lines, `[arrows N: -X]`, and
    `arrows short of the costliest shot` when the quiver runs low in a fight.
  - Any caster with rays (or a kinetic archer): crit-capable attacks should look a bit stronger than before.
  - With Enveloping Winds or Shroud of Water: their `[Buff]` lines should name `miss` or `AC` instead of "effect not read".

## Test 5: v0.3.28-dev-test5 (96ba9cc) - quiver after rest, Chain Arrows, Hide blocked (also in test 6)

**[Download the UMM test ZIP (96ba9cc)](AI-Control-FACC-v0.3.28-dev-test5-96ba9cc-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test5-96ba9cc-UMM-test.zip>

Test 4 (below) plus Codex's review fixes, reviewed and merged (PR #18 and #19, [docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §102).
- **The quiver is now made right after a rest.** The game's AI loop only runs for a party in combat, so test 4 made the
  quiver only when a fight ended. A separate check now runs in normal exploration (not while paused, in dialogue, a
  cutscene, resting or loading, and never while anyone in the party is fighting).
- **Waiting orders are not replaced.** The out-of-combat buff routine now also waits for orders in the game's command
  queue, not only the one being carried out.
- **Chain Arrows jumps are priced at half damage**, as the game deals them; before, each jump was counted at full damage.
  Those half hits no longer make the AI think its blasts are weak.
- **"Hide blocked"** in a character's ability list (Abilities AI must not use), beside "Show blocked only".
- Between fights the quiver check no longer re-reads all of Orvath's abilities twice a second while nothing has changed.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=fade5da1-cea0-4048-a243-54873850148f`.
- ZIP SHA-256 `5535c5129f09259ca84c24eb899b4070509b4282e531dc22a5881ae27db11fbf`; DLL SHA-256 `8b8d06cbaf2949da15fa6182f16f40e655701c6504f65bae760787382b368961`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test5-96ba9cc-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 388 unit tests pass; release build 0 warnings; offline blueprint audit 0 errors. Not played yet.
- **Test:**
  - Orvath with a longbow, AI control and "AI recasts self buffs" on: rest, unpause, and wait a few seconds before any fight.
    Expect `[Kinetic] Orvath: out of combat, make the quiver` once, not again while the quiver has enough arrows.
  - With Chain Arrows: the jump targets take half damage, and the AI still weighs it against a single blast.
  - The ability list: "Hide blocked" hides blocked rows and turns "Show blocked only" off, and the other way round.

## Test 4: v0.3.28-dev-test4 (e620e71) - Kinetic Sharpshooter quiver (also in test 5)

**[Download the UMM test ZIP (e620e71)](AI-Control-FACC-v0.3.28-dev-test4-e620e71-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test4-e620e71-UMM-test.zip>

Tests 1-3 (below; test 3 played) plus the Kinetic Sharpshooter fixes from the test 3 log ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §101).
- **Quiver preparation added (driver limitation below).** In 19 fights the AI never made a Kinetic Quiver;
  all 16 weapon-versus-blast diagnostics showed "quiver 0". Native burn messages sum to 48, which is not a reliable
  total of actual burn. The old option required burn to block every blast. Test 4 adds an out-of-combat attempt
  that costs no combat turn (still a native standard-action cast). It triggers when arrows left are fewer than
  the burn of his costliest kinetic ability (Exploding
  Arrows with Entangling: 3), and when the quiver's burn (1 burn = nonlethal damage equal to his character level) leaves at least half his hit
  points. The arrows then pay the burn of his blasts and infusions. They do not pay for Searing Flesh, Enveloping Winds or
  Shroud of Water, which take their burn directly in the game.
- **Burn credit is restricted when the bow looks better.** Burn's cost was cancelled by the extra damage Elemental
  Overflow gives to later blasts. The recorded comparisons favored Orvath's bow, making that credit too optimistic:
  a burn-3 Exploding Arrows was priced 0. Overflow now counts only when its raw gain covers the current
  weapon-versus-free-blast value gap; this is an approximation.
- **No burn on buffs the AI cannot read.** Searing Flesh and Enveloping Winds (1 burn each) were cast 20 times. The mod
  cannot read what their effect is worth, and a rule from v0.3.19 says such an effect is not worth burn. A gap let these two
  through at a small value. They are now left to you; Orvath's reported burn limit was 3 a day.
- **Cone warning** now says why (two such warnings in the log, reason not recorded).

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=213bf9ff-50b8-4c91-acaf-8c370286a712`.
- ZIP SHA-256 `b0d4d05e57c4e72b349c9d782623fc2bae504be5f05f8fcc512f9473d53f8175`; DLL SHA-256 `c8e56a7f7b170ed46597b36fa815296cad879808ef8a1159c957fce86a4757f0`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test4-e620e71-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 359 unit tests pass; release build 0 warnings; no new game references. Not played yet.
- **Test:** Orvath (Kinetic Sharpshooter, bow in hand, "AI recasts self buffs" on). After a rest, out of combat, look for
  `[Kinetic] Orvath: out of combat, make the quiver` and `Out-of-combat permanent buff (Kinetic Quiver)`. In fights,
  look for `quiver N` in `[Kinetic]` lines and Exploding Arrows with `burn 0`.

**Independent review correction (separate Codex branch; not included in this ZIP):** the native AI loop skips
wholly out-of-combat groups, so test 4 does not reliably prepare the quiver immediately after a rest. The review
adds an independent quiver-only exploration check and queued-command guards. Its supplemental review also corrects
ricochet delivery-layer damage forecasts and excludes deliberate half packets from whole-blast calibration. The logs contain 19 fights, including
a TB-to-RT switch in fight 7; five coverage samples show zero burn, not every fight start.
See [the audit report](../docs/CODEX_REVIEW_KINETIC_SHARPSHOOTER_RESULTS.md) for confirmed rules, fixes, limitations and replay steps.

## Test 3: v0.3.28-dev-test3 (2cb47db) - defences counted (also in test 4)

**[Download the UMM test ZIP (2cb47db)](AI-Control-FACC-v0.3.28-dev-test3-2cb47db-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test3-2cb47db-UMM-test.zip>

Tests 1-2 (below, not played yet) plus defences the AI did not count ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §100).
- **Concealment and Mirror Image.** Hit chances were the plain dice roll against Armor Class. The game then gives a
  20 % miss for Blur, fog or dim light, 50 % for Displacement or invisibility, and a hit lands on the real target only
  1 time in (images + 1) under Mirror Image. Now counted everywhere hits are estimated, both ways:
  - a blurred, displaced or mirror-imaged companion is less afraid of attacks of opportunity and of standing in melee
    (your high-Dexterity fighter under Blur);
  - attacking a displaced or invisible enemy is worth less, so companions prefer other targets or spells that ignore it.
- **Temporary hit points** (False Life, Aid) now count towards how far a companion is from going down; healing and the
  low-HP caution still use real HP.
- **Prone companions** are treated as easier to hit in melee (+4), as in the game.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=09bc562d-a4dc-4c51-a8ff-ebc2e6f1cf3a`.
- ZIP SHA-256 `3ddbeac8dbdb091c7243ee38ed16781b2fa2cd17e2751f45b80e4b5465dda9b0`; DLL SHA-256 `519aeea0370b3b4e9ca2249e186a8fba9069764c012b00e3b8b946d6d2054eaa`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test3-2cb47db-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 359 unit tests pass; release build 0 warnings. Not played yet.
- **Test:** fights where companions have Blur, Displacement or Mirror Image, and enemies that do (Shadows, displacer
  creatures, invisible casters).

## Test 2: v0.3.28-dev-test2 (5c87b6d) - squishies free to step away (also in test 3)

**[Download the UMM test ZIP (5c87b6d)](AI-Control-FACC-v0.3.28-dev-test2-5c87b6d-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test2-5c87b6d-UMM-test.zip>

Test 1 (below, not played yet) plus one correction to its "leaves allies" cost ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §99).
- **The problem (your point):** the cost counted only how much more danger the others would be in, never how much
  safer the companion walking away became. A caster or archer stepping out of a melee that a front-liner shared was
  charged for the front-liner taking the blows, even though the party was safer for it.
- **The fix:** the cost is now the whole party's change, the walker included. Its own relief only cancels the cost,
  never pays for the move.
  - A squishy companion leaving an enemy a front-liner holds: the enemy turns to the harder target, the party is
    safer, no cost.
  - A front-liner walking off the enemy it holds, leaving a squishy in its reach: still charged.
  - This rests on armour class and HP, not class names: a heavily armoured cleric counts as a front-liner, a lightly
    armoured fighter does not.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=cd797bdb-e0cc-4fd7-8ffc-54e02040954f`.
- ZIP SHA-256 `7638caec9b78317b02d3527973d58e69450863af97e6f22927e2babaa6410df5`; DLL SHA-256 `0f18d91142e72d569a0986ca3ce1fc37a1d6e8acb9b4f160ceedd0f7649ab4fe`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test2-5c87b6d-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 354 unit tests pass; release build 0 warnings; no new game bindings. Not played yet.
- **Test:** test 1's checks, plus casters and archers in melee next to a front-liner: they should step or back away to
  cast or shoot without `[leaves allies]` holding them back; front-liners leaving squishies exposed still show it.

## Test 1: v0.3.28-dev-test1 (f00d679) - v0.3.27 log review (also in test 2)

**[Download the UMM test ZIP (f00d679)](AI-Control-FACC-v0.3.28-dev-test1-f00d679-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.28-dev-test1-f00d679-UMM-test.zip>

v0.3.27 plus fixes from your v0.3.27 logs (7 turn-based and 5 real-time fights, no mod errors;
[docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §98). Both modes unless noted.
1. **Front-liners hold the enemy they are fighting.** In turn-based fight 3 Seelah, beside the Cultist Champion, used her
   move to advance on the Cultist Archer and took the Champion's free attack (82 % to 18 %); Lann then walked past it
   to heal her. Now:
   - an advance counts the attack it gives up: if the companion could hit an enemy next turn from where it stands, walking
     away from it is worth only the difference (`[leaves <enemy>]` in the log);
   - every walk counts how much more exposed it leaves the rest of the party (the Champion swung at Camellia next):
     `[leaves allies -N]`. A badly hurt companion's retreat is weighed against this too.
2. **Shadows' touches are no longer free.** In turn-based fight 4 Seelah cast beside a Shadow four rounds running and
   lost Strength to three touches: their Strength damage was not counted. Attacks that drain an ability score are now
   priced (Shadows, Nocticula's Charisma drain, some devils).
3. **Real time:** a walk whose target point is lost (it pointed at the map's origin) ends at once instead of after 6 s.

UMM shows **0.3.28**. Startup line `[Build] v0.3.28 module=3cb426c7-4b45-4d06-b226-96a2b089931a`.
- ZIP SHA-256 `b8842e024c845f94b20c54aa34869fb14cf7bfbb7462d6cf2ee140e6a54d35d3`; DLL SHA-256 `4b92deebe68c7e821f2e7f0cd09d8f7ffd20e1befe2a2e4c2675b7ae06726fa0`.
- [Identity](AI-Control-FACC-v0.3.28-dev-test1-f00d679-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 350 unit tests pass; release build 0 warnings; reader verified on the game's blueprints. Not played yet.
- **Test:** fights where a front-liner is engaged with one enemy while a more dangerous one stands behind; fights with
  Shadows. Watch for front-liners now refusing to leave a weak enemy at all, or hurt companions not retreating.

## Test 6: v0.3.27-dev-test6 (3f005b9) - arrived walks end in time (released as v0.3.27)

**[Download the UMM test ZIP (3f005b9)](AI-Control-FACC-v0.3.27-dev-test6-3f005b9-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.27-dev-test6-3f005b9-UMM-test.zip>

Test 5 (played: 13 fights, nobody down, all its rules working; [docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §96) plus the
real-time stuck-walk fix you approved (§97).
- **The problem:** Seelah walked up to a Vermlek and was already close enough, but kept pushing against its body. The
  "in range for 2 s and still walking" rule kept restarting because her distance wobbled by a few centimetres around
  the limit, so the walk only ended after 6.6 s, while she lost 41 % of her HP.
- **The fix:** "in range" now uses the game's own distance test, or up to 0.3 m past the limit (with a clear view where
  needed), so such walks end after about 2 s and the companion acts.

UMM shows **0.3.27**. Startup line `[Build] v0.3.27 module=ae5d4340-5d51-454f-bec5-2888b74cbed6`.
- ZIP SHA-256 `3086371601af7e94dacc86cc66b75823d171d0cbb55d50fb7ac946098acc1e26`; DLL SHA-256 `99d0dcfeb1446c8ea0bdedaacb42aea7bdeca38a18aa8e8c4082beedde5f534c`.
- [Identity](AI-Control-FACC-v0.3.27-dev-test6-3f005b9-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 342 unit tests pass; release build 0 warnings; no new game bindings. Not played yet.
- **Test:** real-time melee fights in crowds. A companion that reaches its target but is blocked by bodies should act
  after about 2 s; the log shows `Stuck command (...) - in range for 2 s and still walking`, rarely the 6 s
  `walking for 6 s without getting nearer` one.

## Test 5: v0.3.27-dev-test5 (ab559e8) - movement changes you approved (also in test 6)

**[Download the UMM test ZIP (ab559e8)](AI-Control-FACC-v0.3.27-dev-test5-ab559e8-UMM-test.zip?raw=true)**

Direct link: <https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-v0.3.27-dev-test5-ab559e8-UMM-test.zip>

Test 4 (below, not played yet) plus three movement changes you approved ([docs/TACTICAL_AI.md](../docs/TACTICAL_AI.md) §95).
Both modes.
1. **Walking into danger costs something, protecting others is not punished.** A healthy companion's walk now pays for
   how much likelier it is to go down where it ends up. That price is reduced by how much safer it makes the rest of
   the party (an enemy fighting it is not fighting someone else), and never goes below zero. Each enemy is assumed to
   go for one of the companions it can reach, the easier-to-hit ones (lower AC) more often. A front-liner stepping in
   front of a heavy hitter that is on a squishy companion pays little or nothing; a squishy companion walking up to a
   heavy hitter alone pays a lot. Hurt companions, and walks that draw free attacks, are priced as before.
2. **Archers walk to a clear shot in turn-based** when an enemy is in range but behind cover, as in real time (they
   used to "advance" 0 m).
3. **No more creeping forward:** an archer within its bow's full range no longer steps closer after shooting.

UMM shows **0.3.27**. Startup line `[Build] v0.3.27 module=8a238333-6e5f-46d3-a992-c50059651499`.
- ZIP SHA-256 `d4744022f27000a804964ca8c2c8b93d59b51d569d0e225c3093070140c5d97a`; DLL SHA-256 `6cdbd44a3e557f3c96f433bb0d50d95b3c613a22b02e7fb5ea666e5dbe5a3363`.
- [Identity](AI-Control-FACC-v0.3.27-dev-test5-ab559e8-UMM-test.identity.json) · [Checksums](SHA256SUMS.txt).
- Checks: 337 unit tests pass; release build 0 warnings; no new game bindings. Not played yet.
- **Test:** fights against one or two hard hitters (elementals, ogres, bosses).
  - Front-liners should still engage them, especially when they are on a squishy companion.
  - Casters and support companions should not walk up to them alone; they may still join a front-liner.
  - Watch for melee companions hanging back too much: that would mean the price is too high.
  - Real-time `[Walk]` lines show `exposed=True` and `shields allies N` when this is at work.

## Test 4: v0.3.27-dev-test4 (375589a) - test 3 log review (also in test 5)

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
