# v0.3.70 features1 playtest: review and fixes (27 November 2026, Claude)

The author played features1 (module `82dcebbe`): first five turn-based fights in one session, then six real-time fights
in a second session. The first session ended in a crash at the start of its first real-time fight; the author put it
down to spawning too many enemies and asked for it not to be investigated. Source `6f262cb` on
`claude/v0370-features-review` (draft PR #118), stacked on `claude/v0370-features` (draft PR #117). Package
`test-builds/AI-Control-FACC-v0.3.70-dev-features3-6f262cb-UMM-test.zip`, module `e32d25e0-351a-4083-81d2-14ba5ba9d574`,
UMM 0.3.70, **unplayed**. It replaces features2 (`ac71295`, never played) and adds the
[author's requests](#additions-author-requests-27-november-2026) below. [Receipt](competence/V0370_FEATURES_REVIEW_CHECKS.json).

## The logs

| Session | Fights | Decisions | Commands | Notes |
| --- | --- | --- | --- | --- |
| 1, turn-based | 5 (and the start of a real-time one) | 451 | 274 issued, 268 ended (the last cut off by the crash) | no AI Control exception |
| 2, real time | 6 | 1,225 | 411 issued, 410 ended | no AI Control exception, no party death, one Vrock lost |

- Real-time outcomes: 266 commands succeeded, 28 were interrupted after acting, 115 were interrupted before acting, 1 failed.
  The 115 are mostly the AI's own re-decisions: arrived walks ended, an enemy engaging the walk, new reinforcements in the
  way, burst cancels, a heal walk whose target came into reach. Others are the game's (Hold Person, prone).
- Neither combat log has any damage between party members.
- An enemy Nabasu cast Hold Person, Mass three times and held up to nine party members each time. Nobody died of it.

## What the new features did

- **F1, reposition (turn-based):** offered once (arcan, 55 % HP, 5.6), and rightly lost to a heal (15.5).
- **F2, weapon sets:** never offered in either session. The logs cannot say why. A new once-a-session `[WeaponSet]` line
  names the reason for a Mixed unit with no other weapon set of the other kind.
- **F3, summon choice:** works. Rows name the creatures, their count and value per round, and once seen what they dealt
  ("3x DogSummoned CR1 0.8/rd (dealt 0.2/rd over 37 rd)"; "3.5x CR11_RetrieverStandard CR11 21.2/rd").
- **P2, declining a rage:** works. The Derakni and the Shadow Demons switched off their Inspire Rage acceptance (their
  spells -47 to -116 a round against +0.7 to +2.7), and back on when the fight ended.
- **P1/F4, teleports:** offered twice in real time and rightly not chosen. A Vrock's teleport approach was worth 37 against
  its full attack at 113.7; a teleport to cast Aid Another was worth 1.7. Most other teleport decisions stopped at
  "no verified safe teleport landing": see fix 3.

## Fixes (both modes)

1. **A spell ban priced an unmeasured caster at 0.** In turn-based fight 1, Camellia's Inspired Rage was worth 33.7, with
   "Knight Commander 0 (stops its spells)": the Knight Commander, a Pure Mage, had not yet made a decision that
   measures its spells (`SpellEdgeMemory`). The rage then forbade its spells, and it ended its turn after one scroll.
   - Now, until its own decisions measure it, a spellbook caster's spells are estimated from its spellbook
     (`ValueModel.UnmeasuredSpellEdge`): a round of a unit of its caster level, by the share of full casting its highest
     spell level gives, halved when it also uses a weapon.
   - A 20th-level six-circle Pure Mage loses 36.6 a round: 161 over the rage's 4.4 rounds, more than the 33.7 the rage
     gave the rest. Measured edges replace the estimate.
2. **Cones and lines re-asked the same damage.** A cone or line is priced from many aims and spots: straight, turned past
   allies, along enemies, walk-first spots and crowded free sides. Each aim asked the game again for the same ability's
   damage and effect chance on the same unit.
   - In real time, a Shadow Demon's ten Dragon's Breaths made ~900 damage estimates each, about 150 ms a breath. Its
     decision took 844 ms, with a garbage collection inside.
   - Cone of Cold, Burning Hands and Fan of Flames led the other slow rows.
   - Both numbers are now remembered for the frame (`CombatMath.EstimateMemo.cs`). Within a frame they do not depend on
     the aim: the caster has not moved.
   - Every state preview that changes them (prone, infusions, Magus modes, forms) clears the attack cache, and with it
     the memo. The memo stands aside inside a Hit Dice pool outcome or a Magus roll.
   - The slow-decision row now counts "damage estimates reused" and "effect chances reused".
3. **Group teleports were withheld whenever the party had more than 16 members.** The bound counted summons too. It
   stopped the Vrocks' Greater Teleport and Evading Steps (Dimension Door, Mass).
   - The native passenger selection walks the group once, one distance test per member. The bound is now the crowd bound
     (96); travellers stay at most four.
4. **F4 with an Unlimited teleport:** the game gives an Unlimited range as infinity (`BlueprintAbility.GetRange`). The
   far-escape spot search could run at an infinite distance. Nothing in these parties has one, but 40 stock teleports do.
   The search is now cut to the farthest awake enemy plus the fight reach.

## Log size (author request)

Measured on these logs, the trims below remove about 12 % of each (1.1 MB real time, 0.8 MB turn-based). Nothing a
decision reads is lost.

- **Coverage:** once per summon kind, and only toggle lines not printed before. Each new Summoned Dog had reprinted the
  same seven lines (15 times); Wenduag's form changes reprinted 24 identical Fury lines.
- **No free side:** one row a decision naming the enemies and who takes their side. The spot counts go to the debug
  log. Before, there was one 230-byte row per enemy (269 rows, 61 KB).
- **`[Unused]`:** an option is listed only when its verdict changes, with a count of the rest. Options that competed and
  lost are always listed. "Treat Affliction best 0 (summon policy)" had been repeated every fight.
- **Energy estimates:** the rule note is printed once a session, not on all 288 rows.
- **Left as they are:**
  - `[Cmd]` rows: the Invocation ledger tool parses them.
  - Decision rows and slow-decision rows: they are the evidence a review needs.
  - Debug rows: they appear only with the debug setting on.

## Observed, not changed

- **Hold Person, Mass:** nobody tried to free the held allies.
  - The Knight Commander had Dispel Magic (Target) and Greater Dispel Magic. A single-target dispel frees one ally, and
    its damage spells scored over 400.
  - Freeing is valued by the relief model (§205, §206). Nobody died, and no enemy made a coup de grace on a held ally.
  - The value of a held ally's exposure to a coup de grace is not modelled. It is not worth adding without a case.
- **Two Vrock coups de grace never started.** They waited 4–5 s and were interrupted without acting, probably because the
  target stopped being helpless; Wenduag's succeeded. The cause is not proven. A replay with the debug log would show it.
- **The crash:** not investigated, at the author's request.
  - The mod log is written in batches: up to 100 lines or half a second (UnityModManager's logger). A crash loses the
    last batch.
  - Unity's `Player.log` holds the same lines and the crash report, should one be needed.

## Additions (author requests, 27 November 2026)

1. **Companions decline a costly Inspired Rage** (author-approved; both modes). The summon rule (P2) now applies to any
   unit AI Control decides for.
   - A companion whose spells the rage would stop switches off "Inspire Rage — Accept Effect" when they are worth more
     than the rage gives it, and back on with the existing margin.
   - A caster not measured yet declines on its spellbook's estimate.
   - Camellia counts such an ally as declined (0, not a loss) only when this engine makes its decisions: turn-based, or
     real time without the old engine. Otherwise the loss stays priced (the fix above).
   - In the turn-based case the rage then starts for Wenduag and the Bandit, and the Knight Commander keeps casting.
   - The safeguards of the other managed toggles apply: only with "AI manages toggles" on, never a toggle the player
     switched in the fight, and restored when the fight ends.
2. **Issue #114, the settings panel ignored UMM's UI scale.**
   - UMM scales its window through the skin's base font and its own styles. The panel set fixed fonts (12/14/18),
     `<size=11>` rich-text tags and fixed widths and heights.
   - All 108 sizes now go through `UnityModManager.UI.Scale` (`UI/UiScale.cs`), and the styles are rebuilt when the
     scale changes.
3. **Issue #115, roles and ranges from the build.**
   - A character seen for the first time gets the role and range its build suggests (`SuggestedSettingsRules`),
     read from its weapon, shield, spellbook and weapon feats:
     - a bow or crossbow, a blasting kineticist, or a caster without weapon feats → Ranged;
     - a weapon-trained full caster → Mixed;
     - others → Melee;
     - a divine full caster → Support;
     - a melee fighter with a shield → Tank.
   - Saved choices are never changed automatically. The panel has "Suggest from build" per character and "Suggest
     roles for the whole party".
   - Checked against the companions' default auto-level builds (Frostbird's guide):
     - Seelah: Tank, Melee;
     - Lann, Wenduag, Arueshalae: Ranged;
     - Ember, Nenio: Ranged;
     - Daeran, Sosiel: Support, Ranged;
     - Camellia: Support, Mixed;
     - Woljif, Greybor, Regill: Melee.
   - Reading the character's actual build means it fits the default build when the game levels the companion, and a
     custom build when it doesn't.
4. **Summon control is on by default** (author decision).
   - An older settings file, saved "off" while the option was experimental, is switched on once.
   - Switched off afterwards, it stays off.
   - The option is no longer labelled "experimental".

## Replay

Both modes. Send `Log.txt` and `combatLog.txt`.

1. **Rage and an unmeasured caster:** start a new session (so nothing is measured yet). In the first round, let Camellia
   (Inspired Rage) act before the Knight Commander.
   - The rage row should list "Knight Commander -… (stops its spells (-36.6/rd, estimated before its first decision))".
   - The rage should not start while that loss outweighs it.
2. **Performance:** a fight with the Shadow Demons (their breaths) or a Cone of Cold caster in real time. Slow rows should
   show "damage estimates reused=N", and the costliest breaths far fewer estimates and milliseconds.
3. **Group teleports:** the Vrocks with the full party and its summons out. Greater Teleport should now reach a landing
   quote ("… passengers, landing risk …") instead of "no verified safe teleport landing".
4. **Weapon sets:** a Mixed companion. Look for its `[WeaponSet]` line, or for "Switch to … weapon set" options.
5. **Companions declining the rage:** Camellia's rage with the Knight Commander in it.
   - Look for "Inspire Rage — Accept Effect off - … stops its spells" on the Knight Commander.
   - Its spells should still be cast while Wenduag keeps the rage.
6. **UI scale (#114):** set UMM's UI scale to 2 (Settings tab, Apply). The whole AI Control panel should grow with it.
7. **Suggested roles (#115):** a new companion, or "Suggest roles for the whole party". A `[Settings] … suggested from
   its build` line should appear per character. Summoned creatures are now driven without ticking the option.
8. **Log:**
   - one `[Coverage]` block per summon kind;
   - one "no free side" row a decision;
   - `[Unused]` rows ending "(+N as last reported)".
