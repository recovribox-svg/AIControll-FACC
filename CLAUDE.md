# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Collaboration workflow

Read [CONTRIBUTING.md](CONTRIBUTING.md) for shared branch ownership, Codex pull requests, handovers, verification, and releases.
Codex works on task branches and targets pull requests at `claude/dreamy-pasteur-b9h3rn`; Claude reviews and integrates them there.
Keep `main` for author-authorized releases. Read [AGENTS.md](AGENTS.md) when coordinating agent work.

**Current handover (27 November 2026, Claude): stable v0.3.70 released to the repository; PRs #106–#121 merged into
development and `main` brought to it, at the author's request.** Read `docs/TACTICAL_AI.md` §229, the
[receipt](docs/competence/INTEGRATION_106_121_CHECKS.json) and the [release notes](release/CHANGELOG_v0.3.70.md).
- **features6 review (PR #121):** read [docs/V0370_FEATURES6_REVIEW.md](docs/V0370_FEATURES6_REVIEW.md) and §228.
  - Log: features6 module `73eef393`, 5 turn-based + 9 real-time fights; no AI Control exception.
  - Both modes, weapon sets: the switch is compared with the current weapon's price whatever this moment's actions allow
    (`WeaponSetRules`). Camellia switched "against 0" seven times.
  - Both modes, author-approved: half or more of the levels drained counts as badly hurt (`BadlyHurt`,
    `ValueModel.HalfDrained`). arcan and Aeiluvetar were drained to death.
- **Merged:**
  - Development was first fast-forwarded to `main` (README badges).
  - Then, with merge commits: #106–#109, #111, #112, #113 (brings #110), #116–#121. No conflict in this order.
  - GitHub shows #106–#121 merged. The integrated tree equals the #121 head apart from main's README lines.
- **Checks** on the integrated head `14e9d11`:
  - Release 0 warnings; 1,193 unit + 27/21/3 script tests.
  - ProfileAudit exit 0, `V0370-FEATURES6` 5; every other line, the Fusion matrix and the summon detail unchanged.
  - Harnesses 98/14/10; no external member added or removed.
- **Release:** `release/CompanionAI_Pathfinder_v0.3.70.zip`, MVID `670ef6c7-58c0-4bc5-ba40-add2fcb55ba7`, ZIP SHA-256
  `30ded758…`. Built from `14e9d11`; it is not a played test package. `Info.json` 0.3.70.
- **Author action:** create tag `v0.3.70` on `main` and the GitHub release (instructions in `release/README.md`).
- **Played:** features6 (14 fights). The weapon-set and drain corrections are not yet played.
- **Next:** the author plays v0.3.70 (replay in the features6 review page). No PR is open.

**Earlier handover (27 November 2026, Claude): features5 playtest reviewed and fixed on `claude/v0370-features5-review`
(draft PR), stacked on `claude/v0370-features3-review` (draft PR #119). Not played, nothing merged.** Read
[docs/V0370_FEATURES5_REVIEW.md](docs/V0370_FEATURES5_REVIEW.md), `docs/TACTICAL_AI.md` §227 and the
[receipt](docs/competence/V0370_FEATURES5_REVIEW_CHECKS.json).
- **Log:** features5 module `cfc0deda`, one session: 6 turn-based fights (356 decisions), then 7 real-time fights (1,643
  decisions); no AI Control exception, no party death. Real-time fights 7 and 12 had 42 and 47 enemies.
- **Fixes:**
  1. Both modes, performance (same numbers): four decisions over 1 s (slowest 2,069 ms), mostly walk pricing. The drop
     model reads each enemy's strike data once (`RowStrikeShare`, `OwnStrikeShare`). A walk re-reads only allies an enemy
     whose choice it changes can pick (`MoverChangedRows`). The slow row names the drop-chance reads.
  2. Real time: an ability that walks to its target first counts as being cast on it (`NotePendingWalk`). Three allies
     walked to the same helpless Nabasu for a Coup de Grace.
  3. Saves against fused spells: named by the fusion (`AbilityData.Name`), kept per fusion (`FusionIdentityOf`).
- **Seen working:**
  - the Bandit's crossbow switch (turn-based);
  - one provoke re-decision;
  - no dropped option barred.
  Camellia's other set holds a starknife (melee; author), so her "no ranged weapon" line is right.
- **Open:** six real-time coups were all interrupted before acting (state not in the log). Holding attacks off a coup
  target would be a commitment change for the author.
- **Checks** on `dbdc867`:
  - Release 0 warnings; 1,190 unit + 27/21/3 script tests.
  - ProfileAudit `V0370-FEATURES5` 10; every other line, the Fusion matrix and the summon detail unchanged, also for the
    packaged DLL.
  - Harnesses 98/14/10; no external member added or removed.
- **Package:** `test-builds/AI-Control-FACC-v0.3.70-dev-features6-dbdc867-UMM-test.zip`, MVID
  `73eef393-8d13-4837-85e7-df4922333fe7`, UMM 0.3.70.
- **Next:** the author plays features6 (replay in the review page).

**Earlier handover (27 November 2026, Claude): features3 playtest reviewed and fixed, plus two author requests, on
`claude/v0370-features3-review` (draft PR #119), stacked on `claude/v0370-features-review` (draft PR #118). Not played, nothing
merged.** Read
[docs/V0370_FEATURES3_REVIEW.md](docs/V0370_FEATURES3_REVIEW.md), `docs/TACTICAL_AI.md` §226 and the
[receipt](docs/competence/V0370_FEATURES3_REVIEW_CHECKS.json).
- **Log:** features3 module `e32d25e0`, one session: 5 turn-based fights (408 decisions), then 7 real-time fights
  (2,341 decisions); no AI Control exception. The Drow Magus (fight 7) and Wenduag (fight 8) died.
- **Fixes:**
  1. Real time: a command dropped to decide again is no longer barred for the turn (`RealTimeTactics.ReconsiderPending`).
     After an HP loss, a pending self-heal or walk away is left running (`RealTimeReplanRules`). Wenduag's potion was
     dropped, barred, and she died on a smaller one.
  2. Both modes: a Magus spell the game delivers through a weapon is not aimed at a dominated ally (`DeliveredByWeapon`).
     The Drow Magus's Flare put arrows into dominated Camellia and Wenduag.
  3. Log: `[WeaponSet]` once per summon kind, none for natural weapons; blank ability names logged by blueprint name.
  4. Both modes (author report): F2 never saw another weapon set's weapon. `WeaponSlot.MaybeWeapon` gives the
     empty-hand weapon for an inactive slot; sets are now read by slot item (`InactiveSetWeapon`).
     - Every ranged type (darts, javelins, crossbows, throwing axes, bows, sling staves) is eligible.
     - The other set's enhancement is no longer counted twice.
  5. Real time (author-approved): a provoking action waiting to start is re-decided when a newcomer threatens the unit
     (the Drow Magus's death).
- **Seen working:**
  - the companions' rage decline (Knight Commander);
  - group teleport quotes;
  - performance: slowest decision 640 ms, against 844 before.
- **Checks** on `bf7b1da`:
  - Release 0 warnings; 1,187 unit + 27/21/3 script tests.
  - ProfileAudit `V0370-FEATURES3` 14; every other line and matrix unchanged apart from the corrected F2 estimate check.
  - Harnesses 98/14/10; one external member added (`ItemSlot.get_MaybeItem`, resolved).
- **Package:** `test-builds/AI-Control-FACC-v0.3.70-dev-features5-bf7b1da-UMM-test.zip`, MVID
  `cfc0deda-c511-4e3a-a423-bf5be1cf502c`, UMM 0.3.70 (replaces the unplayed features4).
- **Next:** the author plays features5 (replay in the review page).

**Earlier handover (27 November 2026, Claude): features1 playtest reviewed, plus author requests; on
`claude/v0370-features-review` (draft PR #118), stacked on `claude/v0370-features` (draft PR #117). Not played, nothing
merged.** Read [docs/V0370_FEATURES_REVIEW.md](docs/V0370_FEATURES_REVIEW.md), `docs/TACTICAL_AI.md` §225 and the
[receipt](docs/competence/V0370_FEATURES_REVIEW_CHECKS.json).
- **Logs:**
  - features1 module `82dcebbe`: 5 turn-based fights (451 decisions), then 6 real-time fights in a new session (1,225
    decisions); no AI Control exception.
  - The first session's crash: the author says too many enemies were spawned. Not investigated.
- **Fixes (both modes):**
  1. A spell ban priced an unmeasured caster at 0; it is now estimated from the spellbook (`ValueModel.UnmeasuredSpellEdge`).
  2. Per-frame memo of ability damage and effect chances (`CombatMath.EstimateMemo.cs`); cones re-asked them per aim.
  3. Group teleports with the party's summons out (group bound 16 → 96).
  4. F4 caps an infinite teleport range.
- **Logs:** about 12 % smaller. `[Cmd]` rows are kept (the Invocation ledger parses them).
- **Author requests:**
  - Companions AI Control decides for decline a costly Inspired Rage (P2 extended from summons).
  - Issue #114: the settings panel follows UMM's UI scale (`UI/UiScale.cs`).
  - Issue #115: roles and ranges suggested from each character's build at first sight, plus buttons (`SuggestedSettings`).
  - Summon control is on by default: an older settings file is switched on once, and the player's later choice is kept.
- **Checks** on `6f262cb`:
  - Release 0 warnings; 1,182 unit + 27/21/3 script tests.
  - ProfileAudit `V0370-FEATURE-REVIEW` 11; `SUMMON-CONTROL` 71 (new default); every other line and matrix unchanged.
  - Harnesses 98/14/10; 4 external members added, all resolved.
- **Package:** `test-builds/AI-Control-FACC-v0.3.70-dev-features3-6f262cb-UMM-test.zip`, MVID
  `e32d25e0-351a-4083-81d2-14ba5ba9d574`, UMM 0.3.70 (replaces the unplayed features2).
- **Open:**
  - F2 was never offered (the new `[WeaponSet]` line will say why).
  - Two Vrock coups de grace never started.
  - Nobody freed the allies under Hold Person, Mass.
- **Next:** the author plays features3 (replay in the review page).

**Earlier handover (26 November 2026, Claude): features F1–F4 (author requests) on `claude/v0370-features`, stacked on
`claude/v0370-playtest-review` (draft PR #116). Not played, nothing merged.** Read [docs/V0370_FEATURES.md](docs/V0370_FEATURES.md),
`docs/TACTICAL_AI.md` §224 and the [receipt](docs/competence/V0370_FEATURES_CHECKS.json).
- **F1 (movement, turn-based):** a ranged-style unit with only its move action left takes a safer spot that keeps an
  enemy in range and sight.
- **F2:** a Mixed unit switches between its melee and ranged weapon sets with the native move-action command when the
  other set clearly beats its current weapon (inactive set priced by the game's attack rules).
- **F3:** summons are priced by their creatures: count, duration, CR against the enemies, measured damage per round of
  the kind, bodies per enemy. Variants of one spell now differ.
- **F4:** escape teleports also try safe spots at 60 % and 90 % of their range, within reach of the fight.
- **Checks** on `a6cd4a9`:
  - Release 0 warnings; 1,172 unit + 27/21/3 script tests.
  - ProfileAudit `V0370-FEATURES` 7; every other line, the Fusion matrix and the summon detail unchanged.
  - Harnesses 98/14/10; 17 external members added.
- **Package:** `test-builds/AI-Control-FACC-v0.3.70-dev-features1-a6cd4a9-UMM-test.zip`, MVID
  `82dcebbe-430b-479e-9e9b-58116176795e`, UMM 0.3.70.
- **Next:** the author plays it (replay in the feature page). Drain triage, re-scoped, after that playtest.

**Earlier handover (26 November 2026, Claude): P1–P3 implemented (author-approved) on `claude/v0370-playtest-review`
(draft PR #116), stacked on `claude/summons-review` (draft PR #113). Not played, nothing merged.** Read
[the review page's P1–P3 section](docs/V0370_PLAYTEST_REVIEW.md#p1p3-implemented-26-november-2026), `docs/TACTICAL_AI.md`
§223 and the [receipt](docs/competence/V0370_PROPOSALS_CHECKS.json). Both combat modes.
- **P1 (movement):**
  - A point-aimed teleport replaces a risky escape walk (withdraw, area exit, out of reach, out of a swarm), priced at
    the walk's spot.
  - Walk-first touch, cone and line casts can be reached by a teleport.
  - Pure Mages may teleport.
- **P3:** item teleports (Evading Steps) pass the item rules. A charged item pays the limited-use reserve
  (`ItemEntity.IsSpendCharges`).
- **P2:** a summon AI Control drives switches off its rage acceptance toggle when its measured spell edge beats the
  rage, and back on with hysteresis. The performer counts it as declining.
- **Correction:** Inspired Rage's ban is stock (a guarded child of `InspiredRageEffectBuff`), not a mod's run-time buff.
  It is now read from the blueprints.
- **Checks** on `e040d62`:
  - Release 0 warnings; 1,166 unit + 27/21/3 script tests.
  - ProfileAudit `V0370-PROPOSALS` 11; one other line changed (the rage's part list); Fusion matrix and summon detail
    byte-identical.
  - Harnesses 98/14/10; one external member added.
- **Package:** `test-builds/AI-Control-FACC-v0.3.70-dev-proposals1-e040d62-UMM-test.zip`, MVID
  `a8892900-11e1-4a95-b33d-926957a6ff43`, UMM 0.3.70.
- **Next:** the author plays it (replay in the review page). Drain triage, re-scoped, comes after that playtest.

**Earlier handover (26 November 2026, Claude): v0.3.70 playtest reviewed; fixes on `claude/v0370-playtest-review`, stacked on
`claude/summons-review` (draft PR #113). Not played, nothing merged.** Read [docs/V0370_PLAYTEST_REVIEW.md](docs/V0370_PLAYTEST_REVIEW.md),
`docs/TACTICAL_AI.md` §222 and the [receipt](docs/competence/V0370_PLAYTEST_REVIEW_CHECKS.json).
- **Logs:** v0.3.70 module `38994eec`; mastermind 8 fights (1,221 commands, party 16–24 against 8–13 enemies), overlord
  12 fights; no AI Control exception.
- **Fixes (both modes):**
  1. A spell ban costs each recipient its measured spell edge (`SpellEdgeMemory`). A run-time ban (Inspired Rage under
     Tome of the Firebird / Mystical Mayhem) is learnt from the banning buff's context chain.
  2. The teleport crowd bound goes from 24 to 96. It withheld every teleport in the mastermind fights.
  3. Fusion failure keys carry the fusion's identity (all fusions share the native holder blueprint).
- **Blindness (0.3.60 report):** fusions always outscored plain Blindness here. Legitimate reasons for the plain spell:
  union-descriptor immunity, burst friendly fire, damage that wakes a target. The shared-key bug is fixed.
- **Drain triage:** worth adding, re-scoped. First the ally-side lethal-drain drop; then Death Ward/Restoration (absent
  from both parties).
- **Checks** on `4d649ca`: Release 0 warnings; 1,162 unit + 27/21/3 script tests; ProfileAudit `V0370-REVIEW` 5, other
  lines and matrices unchanged; harnesses 98/14/10.
- **Package:** `test-builds/AI-Control-FACC-v0.3.70-dev-summons2-4d649ca-UMM-test.zip`, MVID
  `5256a3ed-ba04-4a0b-aa0b-68f206032ae3`, UMM 0.3.70.
- **Awaiting the author:** P1 escape and casting-spot teleports (movement), P2 summons declining a rage, P3 item
  teleports. Next: the author plays it (replay in the review page).

**Earlier handover (22 November 2026, Claude): S1 (#111) and S2 (#112) reviewed and corrected; v0.3.70 test build on
`claude/summons-review`, stacked on #112 with the consumables review (#110) merged in. Nothing merged into development.**
Read `docs/TACTICAL_AI.md` §221 and the [receipt](docs/competence/SUMMONS_REVIEW_111_112_CHECKS.json).
- **S1:** agreed with (Touch of Chaos, Song of Discord, marks, Contagion ticks, Nimbus withheld). Limits kept: a mark
  scores 0 out of reach or without a cached routine; a smite's AC part is not counted.
- **S2:** the native reading agrees with the game, but `AddTeleportApproach` had no caller, so no teleport was ever offered
  (and teleports had left ordinary scoring). Corrected: the decision calls it after real-path prices (not in a preview);
  teleports aimed at an enemy are not watched for an effect on it; each unused teleport names its reason. A new
  `TELEPORT-APPROACH` check fails on the S2 head.
- **Checks** on `2fca43d`, package from `4d2b1ce` (Info.json 0.3.70): Release 0 warnings; 1,157 unit + 27/21/3 script tests;
  ProfileAudit report equal to #112's head apart from the new check, Fusion matrix and summon detail byte-identical; the packaged
  DLL gives the same report; harnesses 98/14/10; no external reference added.
- **Package:** `test-builds/AI-Control-FACC-v0.3.70-dev-summons1-4d2b1ce-UMM-test.zip`, UMM **0.3.70**, MVID
  `38994eec-a452-421b-a096-424a31dd2fb1`, **unplayed**. It replaces the S2 package. No `main`, tag or release.
- **Next:** the author plays v0.3.70 (replay in §221), with summon control on. The summon-control default, shadow
  variants and Unholy Nimbus are judged from that playtest.

**Earlier handover (22 November 2026): summon abilities S2 available for review.**
Stacked on #111. Checked source `3c8595d`, UMM **0.3.60**, startup module `0dba3d92-6f8a-4473-a185-02666befd432`.
The package is unplayed. See [§219](docs/TACTICAL_AI.md#219-summon-abilities-s2-native-teleport-approaches-22-november-2026),
[checks](docs/competence/SUMMONS_CONSUMABLES_S2_CHECKS.json) and [build provenance](docs/competence/SUMMONS_CONSUMABLES_S2_BUILD_PROVENANCE.json).
- Native transport competes with existing weapon approaches. One relaxed landing forecast uses native passengers.
  Issue repeats eligibility and safety checks without a second scan or a walking fallback.
- 1,153 unit tests, 27/21/3 scripts, zero-warning Release, both audits and native 98/14/10 passed.
- Review the first replay for summons and passengers in both combat modes. The default still needs S1/S2 playtest evidence.

**Earlier handover (22 November 2026): summon abilities S1 available for review.**
Stacked on #109. Checked source `3340951`, UMM **0.3.60**, startup module `8a40df9e-f4d6-45eb-b5e9-3b39ffc3ddb2`.
The package is unplayed. See [§218](docs/TACTICAL_AI.md#218-summon-abilities-s1-immediate-control-roll-penalties-and-marks-22-november-2026),
[checks](docs/competence/SUMMONS_CONSUMABLES_S1_CHECKS.json) and [build provenance](docs/competence/SUMMONS_CONSUMABLES_S1_BUILD_PROVENANCE.json).
- The stock readers cover Screech, Drone, Chaos, Discord, Study and the supported smite components. Contagion follows the actual buff tick interval.
- Nimbus's expiry blast has no safe future recipient forecast in this build. Shadow variants still need played evidence.
- 1,151 unit tests, 27/21/3 scripts, zero-warning Release, both audits and native 98/14/10 passed.
- S2 native review found that `AbilityData.CanTarget` checks the aim, while `UnitUseAbility.Init` separately reads
  `GetApproachDistance`. Dimension Door offsets landings in target facing where applicable, translates passenger offsets,
  appends mounts and can relax points. The Vrock and Omox abilities use the group-capable component. An `AlongPath` cast
  calls `UnitPartRoaming.Advance`, which mutates its next point. Keep that call out of decision forecasts.
- The summon-control default awaits play evidence.

**Earlier handover (22 November 2026, Claude): Codex's consumables slices #106–#109 reviewed, on
`claude/consumables-review`, stacked on #109. Nothing merged.** Read `docs/TACTICAL_AI.md` §220 and the
[receipt](docs/competence/CONSUMABLES_REVIEW_106_109_CHECKS.json).
- **Agreed with:** C1 (potions by effect), C3 (one item price, slots first; touch spells match their spellbook copies), C2
  (the native potion hand-over: action, range, no defensive-casting check) and C4 (sight, movement immunity, Spell
  Resistance, Protection from Spells). C4's 23 changed audit lines are all intended readings.
- **Corrected (diagnostics only):** the `[Unused]` verdict now keeps "scored, not chosen" within a decision, items have their
  own 16 entries, and hand-overs are listed apart from drinking.
- **Checks** on `e4dd378`: Release 0 warnings; 1,149 unit + 27/21/3 script tests; ProfileAudit report, Fusion matrix and summon
  detail byte-identical to #109's head; harnesses 98/14/10.
- **Next:** Codex implements S1 and S2; Claude reviews them; then the v0.3.70 test build (author request). No package now.

**Earlier handover (22 November 2026): consumables C4 available for review.**
Stacked on #108. Checked source `eb8c2b5`, UMM **0.3.60**, startup module `eae9b430-ef8a-4055-b919-285eb384419f`.
The package is unplayed. See [§217](docs/TACTICAL_AI.md#217-consumables-c4-sight-and-typed-protections-22-november-2026),
[checks](docs/competence/SUMMONS_CONSUMABLES_C4_CHECKS.json) and [build provenance](docs/competence/SUMMONS_CONSUMABLES_C4_BUILD_PROVENANCE.json).
- Native friendly touch delivery and typed protections apply in TB and RT. Magical Vestment uses its pre-existing reader.
- 1,145 units, 27/21/3 scripts, zero-warning Release, both audits and native 98/14/10 passed.
- S1 and S2 follow. Touch of Chaos is a penalty on its recipient's rolls. Shadow variants and the control default await play evidence.

**Earlier handover (22 November 2026): consumables C2 available for review.**
This branch includes Slice 0/C1 and C3 (drafts #106 and #107). Checked source `2f890af`,
UMM **0.3.60**, startup module `a7db0656-cced-4fc8-a299-1314503fa3e3`. The package is unplayed.
Read [§216](docs/TACTICAL_AI.md#216-consumables-c2-hand-an-adjacent-ally-a-potion-22-november-2026),
the [checks](docs/competence/SUMMONS_CONSUMABLES_C2_CHECKS.json) and [build provenance](docs/competence/SUMMONS_CONSUMABLES_C2_BUILD_PROVENANCE.json).
- Native potion handoff for eligible quick-slot potions in TB and RT, with the game action, target and range.
- Existing touch approach and item safety apply. Handoff to self, enemies and summons is withheld.
- 1,143 units, scripts 27/21/3, zero-warning Release, complete audits and native 98/14/10 pass.
- C4 item readers, S1 and S2 remain pending.

**Earlier handover (22 November 2026): consumables C3 available for review.**
The branch includes Slice 0 and C1 (draft #106). Checked source `3ee4882`, UMM **0.3.60**,
startup module `03f36260-38ab-4267-9324-5d5f3370f7c5`. The package is unplayed.
Read [§215](docs/TACTICAL_AI.md#215-consumables-c3-one-spend-price-and-slot-copies-first-22-november-2026),
the [checks](docs/competence/SUMMONS_CONSUMABLES_C3_CHECKS.json) and [build provenance](docs/competence/SUMMONS_CONSUMABLES_C3_BUILD_PROVENANCE.json).
- One spend restraint for usable items and usable materials. Native delivery, healing, active relief and safety costs retain their roles.
- Use the same spellbook blueprint first while it is natively available and allowed. The witness identifies this choice.
- 1,135 units, scripts 27/21/3, zero-warning Release, complete audits and native 98/14/10 pass.
- The S1 plan distinguishes Unholy Nimbus's delayed blast. C2 and further slices are pending.

**Earlier handover (22 November 2026): Slice 0 and C1 available for review.**
Source `f646225`, UMM **0.3.60**, module `41783dad-c121-40d8-a852-08756fba45ee`. The package is unplayed.
Read [§214](docs/TACTICAL_AI.md#214-combat-consumables-c1-and-unused-ability-evidence-22-november-2026),
the [checks](docs/competence/SUMMONS_CONSUMABLES_C1_CHECKS.json) and [build provenance](docs/competence/SUMMONS_CONSUMABLES_C1_BUILD_PROVENANCE.json).
- Both combat modes now appraise potions by effect with continuous restraint on the buff benefit.
  The bounded `[Unused]` witness and `ITEMS` audit record what reached existing scoring and why it was skipped.
- Release 0 warnings, 1,133 unit tests, scripts 27/21/3, complete audits and native 98/14/10 pass.
  Apart from `ITEMS`, the existing audit lines and Fusion/summon results match the same input baseline.
- Use the replay in §214. C3, C2, C4, S1 and S2 are later slices in the plan.

**Earlier handover (22 November 2026, Claude): final plan for Codex to implement, on `claude/summons-consumables-plan`.**
Read [docs/SUMMONS_CONSUMABLES_PLAN.md](docs/SUMMONS_CONSUMABLES_PLAN.md) and its
[evidence](docs/competence/SUMMONS_CONSUMABLES_EVIDENCE.json). The author's decisions are recorded at its end.
- **Summons:** driven summons use the abilities the readers understand (54 casts of their own spells in coverage7). The
  gaps are guessed effects in the author's demon roster, teleports and leaps, and no "why not used" line.
- **Consumables:** readers understand most items (95/114 potions, 570/650 scrolls). Any potion without a heal is worth 0
  below difficulty 0.8, which held in over 90 % of decisions. The engine never builds the game's potion-for-an-ally option
  (`AbilityData.PotionForOther`).
- **Slices, in order:** 0 (diagnostics and an `ITEMS` audit) with C1; C3, C2, C4 (consumables); S1 (demon effects);
  S2 (teleports and leaps, movement approved); then the summon-control default, judged after the S1/S2 playtest.
- **Decisions:** buff potions take the spell slots' easy-fight restraint; slots before items of the same spell; no
  scarcity pricing yet; S2 approved.
- **Codex implements** on `codex/` task branches, one draft PR per slice into development; Claude reviews. No merge,
  `main`, tag or release.
- No code, manifest or package change in the plan's commits.

**Earlier handover (22 November 2026, Claude): stable v0.3.60 released to the repository; PRs #92–#105 merged into
development and `main` brought to it, at the author's request.** Read `docs/TACTICAL_AI.md` §213, the
[receipt](docs/competence/INTEGRATION_92_105_CHECKS.json) and the [release notes](release/CHANGELOG_v0.3.60.md).

- **PR #105 (Codex):** reviewed against the decompiled game and agreed with:
  - point-dispel loop and bonus (`TryDispelAreaEffect` adds `CheckBonus` only);
  - `AreaEffectView.Contains` as point membership;
  - first-hit state carry-over in `AbstractWeaponTrigger.IsSuitable`;
  - the dispatcher applies the priced-crossing constraint.
- **Merged** #92 → #105 in stack order, no conflicts (merge commits `e564adf` … `ac6744e`). Development's tree equals the #105
  head; its code is the checked source `27a427e`.
- **Checks** on `27a427e`:
  - Release 0 warnings; 1,129 unit + 27/21/3 script tests;
  - ProfileAudit exit 0, `COVERAGE8-REVIEW` 25, other lines identical to coverage8; Fusion matrix and summon detail byte-identical;
  - harnesses 98/14/10.
  The packaged DLL itself passes the same audit, harnesses and reference dump.
- **Release:** `release/CompanionAI_Pathfinder_v0.3.60.zip`, MVID `a2be2e6f-a510-45cc-a706-7edef3b0e100`, ZIP SHA-256
  `a0df22db…`. It is byte-identical to Codex's coverage8fix1 test build. `Info.json` 0.3.60.
- **Author action:** create tag `v0.3.60` on `main` and the GitHub release (instructions in `release/README.md`).
- **Played:** coverage8 (8 fights, no AI Control error). #105's corrections, Point dispel ending areas, Abandon Flesh,
  priced crossings and Circle of Death are not yet seen in play.
- **Next:** the author plays v0.3.60 (Codex's replay in `docs/COVERAGE8_PLAYTEST_REVIEW.md`). No PR is open.

**Earlier handover (22 November 2026, Codex): coverage8 playtest and PR #104 corrections, unplayed; nothing merged.**
Draft correction [PR #105](https://github.com/recovribox-svg/AIControll-FACC/pull/105), stacked on #104 for review.
Read [the review and replay](docs/COVERAGE8_PLAYTEST_REVIEW.md), [checks](docs/competence/COVERAGE8_REVIEW_CHECKS.json),
[compiler/reference provenance](docs/competence/COVERAGE8_REVIEW_BUILD_PROVENANCE.json) and §212.

- Branch `codex/coverage8-review`, from #104 **`d8ff91d`**, stacked PR base **`claude/coverage7-followups`**. Preserve the stack;
  this is a correction review, not integration into development.
- Both modes: guarded/active caster-removal heals, native first-hit state bounds and per-line grapple reach, decision-scoped
  rider invalidation, native point-dispel bonus/membership and bounded area-worth reuse. Faction diagnostic memos reset per fight.
- Approved RT crossing follow-up: the already queried native route must fit its quoted area-risk budget before exceptional
  permission is granted. Bounded sampling; mounted, unread and over-budget routes withhold permission. Other movement policies unchanged.
- Played coverage8 **`17e3e1e`**, module **`828dc676`**, **no cheats, rests between fights**: 8 fights, 1,012 measured decisions,
  328 issues/terminals, no AI exception or issue rejection. Healing works; no new Invocation spam violation established.
  Point dispel, Abandon Flesh, priced crossing and Circle of Death were not issued; no faction transition appears.
- Frozen checked source **`27a427ebffc18bad9090908fbf99329511ab8274`**, module **`a2be2e6f-a510-45cc-a706-7edef3b0e100`**; package
  `test-builds/AI-Control-FACC-v0.3.60-dev-coverage8fix1-27a427e-UMM-test.zip`, UMM **0.3.60**, **unplayed**. Do not rebuild for documentation commits.
- **1,129 units + 27/21/3 scripts**, Release **0 warnings/errors**, all strict/full audit sections pass; new **25** native-contract/
  callback/counterexample checks. Every old report line, both Fusion matrices and summon detail equal the fresh #104 baseline.
  Native **98/14/10**, supplementary Magus **44**, **1** added native signature resolved exactly.
- Remaining: conservative unknown first-hit state, immediate rider-HP kill credit/stat mortality, unread callbacks, approximate
  area/lingering/helpful utility, post-issue native rerouting and cancellation causes. No touch pruning or Unity speed-up claim.
- Next: review the corrections and replay the page's six TB/RT checks with both logs. No merge, development/main push, tag or release.

**Earlier handover (22 November 2026, Claude): coverage7 follow-ups on `claude/coverage7-followups`, stacked on Codex's
`codex/coverage7-review` (draft PR #103). Not played, nothing merged.** Read [docs/COVERAGE7_FOLLOWUPS.md](docs/COVERAGE7_FOLLOWUPS.md),
`docs/TACTICAL_AI.md` §211 and the [receipt](docs/competence/COVERAGE8_CHECKS.json).

- **PR #103 (Codex):** reviewed against the decompiled game and agreed with; its build, tests, audit, Fusion matrix and
  summon detail reproduce.
- **Author:** approved anything still needing approval, movement included, if worth adding and of acceptable quality.
- **Changes:**
  1. Both modes: on-hit riders per attack line by the native weapon filters, first attack and first hit. Codex's
     blanket withhold had dropped a Shadow's touch drain; target fact gates are read.
  2. Both modes: Abandon Flesh's removal heal (2d6+3) is valued as any heal.
  3. Both modes: point dispels are priced by the area effects they end, and aimed where an ally stands in an area.
  4. Real time (movement): an advance refused for crossing an area is offered priced through it.
  5. Approach-planning timings in slow rows; `[Faction]` rows. No pruning.
- **Checks** on `17e3e1e`: Release 0 warnings; 1,118 unit + 27/21/3 script tests; ProfileAudit `COVERAGE7-FOLLOWUPS` 20,
  `OFFENSIVE-AFFLICTION` 27, other lines identical; Fusion matrix and summon detail byte-identical; harnesses 98/14/10.
- **Package:** `test-builds/AI-Control-FACC-v0.3.60-dev-coverage8-17e3e1e-UMM-test.zip`, MVID `828dc676`. Manifest 0.3.60.
- **Author:** coverage7 was played with an infinite-health cheat. The Drow Magus HP100% item is closed (not a defect);
  that playtest showed no low-HP behaviour.
- **Next:** Codex reviews this PR; the author plays coverage8 (replay in the page, then Codex's coverage7 replay).

**Earlier handover (22 November 2026, Codex): coverage7 playtest corrections, unplayed; nothing merged.**
Read [the completed review](docs/COVERAGE7_PLAYTEST_REVIEW.md), [follow-up proposals](docs/COVERAGE7_FOLLOWUP_PROPOSALS.md),
[checks](docs/competence/COVERAGE7_REVIEW_CHECKS.json), [provenance](docs/competence/COVERAGE7_REVIEW_BUILD_PROVENANCE.json) and §210.

- Branch `codex/coverage7-review`, from PR #102 `e96b52b`, with draft PR base **`claude/coverage7-review`** as explicitly requested.
- Corrected Point dispel unit valuation, named caster-only removals/helpful losses, native dispel reach evidence, and
  positive on-hit disease timing/guards. On-hit utility is separate from HP kill/progress. Both combat modes.
- No proven dominated-unit control bypass: successful native round re-save precedes AI return; later group-hostility
  AoOs remain a replay question. No redundant ShouldControlUnit guard or movement/pruning change.
- Source **`fd9cdda7e60262d4327955daf2ee5be22d3b7a02`**, module **`c61d3aec-6677-4035-b7fa-4614b894f909`**, UMM **0.3.60**, frozen package `test-builds/AI-Control-FACC-v0.3.60-dev-coverage7fix1-fd9cdda-UMM-test.zip`.
- **1,110 units + 27/21/3 scripts**, Release **0 warnings/errors**, every audit passes; native **98/14/10**, Magus **44**,
  **4** new native signatures resolved. Same-input coverage **14,789/13,387** and summon detail unchanged.
- Limits: area-dispel benefit/deactivation healing unread, conditional caster costs conservative, filtered/contextual
  own riders withheld, direct action-rider HP lacks immediate kill credit; incoming threat remains conservative.
  C6/C8 execution, stat/level mortality, native hostility/HP/cancellation state, touch routes and measured pruning need replay.
- Next: review and play the complete package using the six TB/RT checks. Movement proposals need author approval.
  No merge, development/main push, tag or release. Preserve the validated DLL across later documentation commits.

**Earlier handover (21 November 2026, Claude): coverage7 playtest, partial review, on `claude/coverage7-review`, stacked on
`claude/fusion-coverage-c6-c8` (draft PR #101). No code changed. The author asked Codex to finish the review and the fixes.**
The current review above supersedes this partial handover, including its friendly-damage attribution and open control/movement claims.
Read [docs/COVERAGE7_PLAYTEST_REVIEW.md](docs/COVERAGE7_PLAYTEST_REVIEW.md) and the
[input fingerprints](docs/competence/COVERAGE7_PLAYTEST_INPUTS.json).

- **Played:** coverage7, MVID `0aa40527`. 10 fights (5 TB, 5 RT), 928 commands, no AI Control exception.
- **Holds:**
  - no AI-caused friendly fire (all 141 HP party-on-party damage is the game's attacks of opportunity on dominated allies);
  - no AI order aimed at a dominated ally; freeing by dispel works.
- **Findings, with causes traced:**
  1. Dispel Magic (Point) is valued by unit buffs and as freeing, though natively it only ends area effects.
  2. A removal from the caster only (Vermlek's Abandon Flesh) counts as removing anything (`CanRemove`): 4 casts at full HP.
  3. A successful dispel is logged "did nothing".
  4. Real time: plain advances "around" the party's area are refused at issue every decision (movement: ask the author).
- **Open:** possibly a dominated Aeiluvetar driven by AI Control (fight 8), and performance (GC over 1 s; touch-buff spot
  searches). The page lists what is not yet reviewed.
- **Next:** Codex continues on its own branch from this one, with a draft PR stacked on it. No merge, `main`, tag or release.

**Earlier handover (21 November 2026, Claude): Fusion C6 carriers and C8 Hit Dice pools that kill, merged with Codex's
PR #100 corrections, on `claude/fusion-coverage-c6-c8`, stacked on `codex/review-92-99` (draft PR #100). Not played,
nothing merged.** Read [docs/FUSION_COVERAGE_C6_C8.md](docs/FUSION_COVERAGE_C6_C8.md), `docs/TACTICAL_AI.md` §209 and the
[receipt](docs/competence/COVERAGE7_CHECKS.json).

- **Request:** the C6 carriers, then the shared Hit Dice gate unless a practical reason prevents it; fixes to limits
  invited. Then: review Codex's PR #100 and continue.
- **Change (both modes):**
  - C6: the eleven carrier buffs read by mechanism; ordinary carriers behind a save branch priced. Curse of Magic
    Negation stays unread.
  - C8: Circle of Death's kill pool, ordinary and fused: rolled pools, spreading delivery in distance order, the pool's
    reach in the joint value, friendly fire and the death rule. Pools in cones and lines stay withheld
    (frame-dependent native order).
  - Permanent death: a kill is ruled out only by its own Hit Dice test; otherwise PR #100's nested-save bound.
- **PR #100:** agreed with; merged here, with the overlaps resolved (drain vs certain kill, one `DeliveryChance`, aligned
  death scan). To revisit with evidence: modifiable dust damage counted as certain.
- **Coverage:** offered 13,197 → 14,789, with the joint value 8,635 → 13,387, none lost; 705 withheld.
- **Checks** on `1ae78e7`:
  - Release 0 warnings; 1,101 unit + 27/21/3 script tests.
  - ProfileAudit new `HD-POOL` 26 and `CARRIERS` 29; `PERIODIC` 9, `FUSION-DOT` 53, `FUSION-OUTCOME` 32; every other line
    equals PR #100's head on the same inputs apart from the coverage line; summon detail +2 read pairs (C6).
  - Native harnesses 98/14/10; 7 references added relative to PR #100, all compiled against the supplied assembly.
- **Package:** `test-builds/AI-Control-FACC-v0.3.60-dev-coverage7-1ae78e7-UMM-test.zip`, MVID `0aa40527`. It contains
  PR #100. Manifest 0.3.60; no `main`, tag or release.
- **Next:** the author plays coverage7 with summon control on (replay in the page, then PR #100's replay).

**Earlier handover (21 November 2026, Codex): critical review of PRs #92–99. Nothing merged.**
Read [docs/REVIEW_92_99.md](docs/REVIEW_92_99.md), §208 of `docs/TACTICAL_AI.md`,
[checks](docs/competence/REVIEW_92_99_CHECKS.json) and [provenance](docs/competence/REVIEW_92_99_BUILD_PROVENANCE.json).

- Branch `codex/review-92-99`, stacked after #99 `22ded8a`; development remains `0019856`. All eight PR heads were
  rechecked unchanged/open/draft/unmerged. Author explicitly prohibited integration.
- Checked source **`131309f620c6adcb495c58200dd214de7ff78283`**, module **`7ad87f85-5833-4798-baa5-4a5c9f0a9ffc`**. Frozen complete UMM **0.3.60** package:
  `test-builds/AI-Control-FACC-v0.3.60-dev-review92-99-131309f-UMM-test.zip`; identity/checksum beside it. Unplayed.
- Corrections: separate death delivery/save/HP/gate ordering and conservative unread tails; fresh known-ally death checks;
  dominated/downed-ally safety; actual HP separated from stat/level-drain utility; native shared dispel rolls, full-list
  order, suppressed attempts, guards, save paths, survival and retained own-buff losses; narrow target-only recasts;
  periodic unknown-harm handling and parent-cache invalidation; bounded PMF/DP/line work; TB delivery waits; clean timing evidence.
- Checks: **1,096 unit + 27/21/3 scripts**, real-reference Release **0 warnings/errors**, all audits, native **98/14/10**
  with Psionics, Magus **44**. Four added foreign signatures resolve exactly. Same-input matrices and summon detail
  unchanged: **13,197 admitted / 8,635 joint**. Missing stock projectile inputs were completed; their GUID manifest and
  reproduction steps are in the review. No raw native data/logs committed.
- Limits: death-risk upper bounds can refuse healthy-ally casts; multi-action dispel utility remains conservative;
  drain zero-stat/level deaths are not an HP distribution; C10 benefit, C6/C8 gaps and inherited engagement/timing/watch
  snapshot approximations remain. The death-trigger proposal stays author-set-aside.
- Next: review this correction with the eight-PR stack, then play the complete package's TB/RT replay. No permission to
  merge, tag or release is inferred. Preserve the frozen DLL across documentation commits.

**Earlier handover (21 November 2026, Claude): the remaining Fusion groups on `claude/fusion-coverage-rest`, stacked on
`claude/relief-spells` (draft PR #98). Not played, nothing merged.** Read [docs/FUSION_COVERAGE_REST.md](docs/FUSION_COVERAGE_REST.md),
`docs/TACTICAL_AI.md` §207 and the [receipt](docs/competence/COVERAGE6_CHECKS.json).

- **Request:** implement the remaining Fusion groups; additions for quality approved, such as moving to line up enemies.
- **Change (both modes):**
  - C4 lines: a line is an area (the game's `WouldTargetUnitLine`), ordinary and fused; re-aimed past allies and along
    enemies; a lined-up walk within this move, once a round (author-approved movement).
  - C7 fused dispels in the joint value; C9 nested saves (ordinary spells too: Phantasmal Killer's kill); C10 fused
    lingering areas (not credited, refused near an ally); C11 Vampiric Touch fusions offered.
- **Coverage:** offered 10,077 → 13,197, with the joint value 6,367 → 8,635, none lost. Left: C6 carriers (1,189) and
  shared Hit Dice pools (1,108; Circle of Death's fused pool is the slot's 2d6, not worth its code yet).
- **Checks** on `b32057b`:
  - Release 0 warnings; 1,072 unit + 27/21/3 script tests.
  - ProfileAudit new `LINE` 21 and `FUSION-REST` 27; `FUSION` 39, `FUSION-OUTCOME` 33; every other line unchanged.
  - Native harnesses 98/14/10; 7 references added, all compiled against the supplied assembly.
- **Package:** `test-builds/AI-Control-FACC-v0.3.60-dev-coverage6-b32057b-UMM-test.zip`, MVID `b63b47f2`. It contains
  relief1. Manifest 0.3.60; no `main`, tag or release.
- **Next:** the author plays coverage6 with summon control on (replay in the page).

**Earlier handover (21 November 2026, Claude): relief spells on `claude/relief-spells`, stacked on
`claude/coverage4-followups` (draft PR #97). Not played, nothing merged.** Read [docs/RELIEF_SPELLS.md](docs/RELIEF_SPELLS.md),
`docs/TACTICAL_AI.md` §206 and the [receipt](docs/competence/RELIEF1_CHECKS.json).

- **Report (0.3.52 user):** "none of the magic-using characters ever cast Dispel Magic".
  - Cause: every dispel was worth a flat 2-point fallback; Break Enchantment counted only its caster.
  - Remove Fear and Unbreakable Heart were valued only by their save bonus.
- **Change (both modes):**
  - Ending an effect on an ally (confusion, fear, a hold, a penalty) is worth its share of the ally's output for the
    rounds it would still act.
  - Dispels are priced in the native order on every unit they reach; the party's own buffs a dispel would take first
    are a loss.
  - Suppressing buffs (Remove Fear, Unbreakable Heart) are worth what they switch off while they last. Unbreakable Heart
    can free a dominated ally.
- **Checks** on `746d7a7`:
  - Release 0 warnings; 1,066 unit + 27/21/3 script tests.
  - ProfileAudit new `RELIEF` 16, every other line unchanged.
  - Native harnesses 98/14/10; 0 references added.
- **Package:** `test-builds/AI-Control-FACC-v0.3.60-dev-relief1-746d7a7-UMM-test.zip`, MVID `5537a531`. It contains
  coverage5. Manifest 0.3.60; no `main`, tag or release.
- **Next:** the author plays relief1 (replay in the page).

**Earlier handover (21 November 2026, Claude): the author's coverage4 follow-ups on `claude/coverage4-followups`, stacked on
`claude/coverage3-review` (draft PR #96). Not played, nothing merged.** Read [docs/COVERAGE4_FOLLOWUPS.md](docs/COVERAGE4_FOLLOWUPS.md),
`docs/TACTICAL_AI.md` §205 and the [receipt](docs/competence/COVERAGE5_CHECKS.json).

- **Changes:**
  1. Both modes: a dispel or removal that frees an ally under an enemy's control is valued: the enemy's lost fighter plus
     the ally back, for the rounds the hold would last (its round re-save). Dispels follow the game's order, and a party
     buff dispelled first is a loss.
  2. Movement (author-approved): with no full exit from several swarms, a spot that leaves one of them is tried
     (Seelah, fight 10).
  3. Fusion C6a: damage over time with a caster-level rank on the buff (Caustic Eruption) is a carrier. Offered 10,077,
     joint 6,367 of 15,494, none lost.
- **Checks** on `2b9d8ed`:
  - Release 0 warnings; 1,066 unit + 27/21/3 script tests.
  - ProfileAudit: new `FREEING` 12, `FUSION-DOT` 47, `SWARM-ESCAPE` 32; every other line unchanged.
  - Native harnesses 98/14/10; 0 references added.
- **Package:** `test-builds/AI-Control-FACC-v0.3.60-dev-coverage5-2b9d8ed-UMM-test.zip`, MVID `3d6cdb2b`. It contains
  coverage4. Manifest 0.3.60; no `main`, tag or release.
- **Next:** the author plays coverage5 (replay in the page). The rest of C6 needs a reader per spell (listed in the
  programme page).

**Earlier handover (21 November 2026, Claude): coverage3 playtest reviewed; fixes and Fusion C3 on
`claude/coverage3-review`, stacked on `claude/coverage2-review` (draft PR #95). Not played, nothing merged.** Read
[docs/COVERAGE3_REVIEW.md](docs/COVERAGE3_REVIEW.md), `docs/TACTICAL_AI.md` §204 and the
[receipt](docs/competence/COVERAGE4_CHECKS.json).

- **Log:** coverage3 MVID `60673199`; 15 fights (6 TB, 9 RT); 833 commands; no AI Control exception. The coverage2 fixes
  hold: arcan's bursts hurt no ally, and no touch heal chased its recipient.
- **Fixes:**
  1. Both modes: an ally under an enemy's control (Dominate; native `FactionOriginal`) is stopped, not killed. No weapon
     attack, walk, leap or Coup de Grace is aimed at it, and its damage is friendly fire. Seelah died to four party
     members in fight 15.
  2. Both modes, Fusion C3 and the author's policy: a cast that could kill an ally for good (Disintegrate's dust, a kill)
     is not used at 1 % risk or more, whatever the settings. Below that it costs 100× the ally's worth; summons are
     exempt. The risk is exact, from the dice. The dust gate is a stable fusion guard: offered 9,999, joint 5,995, none
     lost.
  3. Real time: burst forecasts, re-checks and disruption timing wait for the native start gate (action cooldown,
     surprised initiative). The caster's own cast walk no longer cancels its burst.
  4. Real time: an ally ordered to attack the target in melee that reaches it first counts toward a predicted flank.
- **Found, not changed:** freeing a dominated ally is not valued, and a nauseated unit in two swarms found no exit (both
  done in coverage5); an 8 HP splash; performance as before.
- **Checks** on `361776c`:
  - Release 0 warnings; 1,065 unit + 27/21/3 script tests.
  - ProfileAudit: new `PERMADEATH` 24, `BURST-AIM` 78, `EVIDENCE` 15, `SNEAK` 36, `FUSION-OUTCOME` 32; every other line
    unchanged.
  - Native harnesses 98/14/10; 7 references added, all resolved.
- **Package:** `test-builds/AI-Control-FACC-v0.3.60-dev-coverage4-361776c-UMM-test.zip`, MVID `248aa48c`. Manifest 0.3.60;
  no `main`, tag or release.
- **Next:** the author plays coverage4 (replay in the review page).

**Earlier handover (20 November 2026, Claude): coverage2 playtest reviewed; fixes on `claude/coverage2-review`, stacked on
the three coverage2 branches. Not played, nothing merged.** Read [docs/COVERAGE2_REVIEW.md](docs/COVERAGE2_REVIEW.md),
`docs/TACTICAL_AI.md` §203 and the [receipt](docs/competence/COVERAGE3_CHECKS.json).

- **Log:** coverage2 MVID `5cfd7765`; 14 fights (5 TB, 9 RT); 649 commands, each with an end row; no AI Control exception.
  C2 and the damage-over-time pricing work as intended.
- **Fixes (real time):**
  1. A burst whose cast takes time gets ground aim, the pre-cast re-check and the shared landing spot
     (`BurstRules.LandsLater`). Before, arcan's Incendiary Runes / Frigid Touch caught allies in 4 of 5 casts (76 HP).
  2. A touch walk that ended in reach casts from there, within one step, for 1.5 s (`TouchFromHere`). This ends the heal
     chase loop.
- **R items:**
  - R1 and R3 are not built (evidence).
  - R4 is calibrated: first melee swing 0.7 s, bow shots 1.8 s apart, at most 6 s / N.
  - Flank misses are now logged with their engagers.
  - A stray `[Timing]` line at fight start is gone.
- **Found, not changed:** a turn-based stall against a helpless enemy on a ledge; Tripper's knock-outs; performance as
  before.
- **Checks** on `1ed420b`:
  - Release 0 warnings; 1,059 unit + 27/21/3 script tests.
  - ProfileAudit: `BURST-AIM` 73, `EVIDENCE` 14, `CAST-DISRUPT` 50, every other line unchanged.
  - Native harnesses 98/14/10; 0 references added.
- **Package:** `test-builds/AI-Control-FACC-v0.3.60-dev-coverage3-1ed420b-UMM-test.zip`, MVID `60673199`. Manifest 0.3.60;
  no `main`, tag or release.
- **Next:**
  1. The author plays coverage3 (replay in the review page).
  2. C3 needs the author's policy on a permanent ally death (done in coverage4).

**Earlier handover (20 November 2026, Claude): coverage2 - R1–R7 decided, R2/R5, evidence logging, Fusion C2/C5 and
damage over time on ordinary spells. Three stacked branches, not played, nothing merged.** Read
[docs/COVERAGE2_BUILD.md](docs/COVERAGE2_BUILD.md), [docs/R_ITEMS_PLAN.md](docs/R_ITEMS_PLAN.md),
[docs/PERIODIC_DAMAGE.md](docs/PERIODIC_DAMAGE.md), `docs/TACTICAL_AI.md` §202 and the
[receipt](docs/competence/COVERAGE2_CHECKS.json). The death-trigger proposal is set aside (author).

- **Branches:** `claude/fusion-coverage-programme` (C1, draft PR #92) → `claude/review-r-items` (R2, R5, evidence) →
  `claude/fusion-coverage-c2-c5` (C2, C5, ordinary damage over time).
- **R items:**
  - R2 and R5 are done.
  - R1, R3 and R4 wait for the evidence lines `[Flank]`, `[Burst]` and `[Timing] fight summary`
    (`TimelineObserver`, read only).
  - R6 and R7 are not worth doing yet.
- **Coverage:** offered 9,655 and joint 5,651 of 15,494, none lost. All eight fusions on arcan's bar are offered with the
  joint value.
- **Checks** on `72acfd1`:
  - Release 0 warnings; 1,052 unit + 27/21/3 script tests.
  - ProfileAudit: `BURST-AIM` 67, `EVIDENCE` 13, `FUSION-RECAST` 12, `FUSION-DOT` 42, `PERIODIC` 8.
  - Native harnesses 98/14/10; 4 references added.
- **Package:** `test-builds/AI-Control-FACC-v0.3.60-dev-coverage2-72acfd1-UMM-test.zip`, MVID `5cfd7765`. Manifest
  0.3.60; no `main`, tag or release.
- **Next:**
  1. The author plays coverage2 and sends logs with the summary lines.
  2. Calibrate R4 and decide R1 and R3 from them.
  3. C3 needs the author's policy on a permanent ally death.

**Earlier handover (17 November 2026, Claude): Magic Fusion coverage programme and its first slice C1 on
`claude/fusion-coverage-programme` (from development `0019856`), draft PR into development. Not played.** Read
[docs/FUSION_COVERAGE_PROGRAMME.md](docs/FUSION_COVERAGE_PROGRAMME.md), [docs/FUSION_MAIN_TARGET_DOT.md](docs/FUSION_MAIN_TARGET_DOT.md),
`docs/TACTICAL_AI.md` §201 and the [receipt](docs/competence/FUSION_C1_CHECKS.json). Both modes, valuation only.
- **Programme:** coverage = offered + joint value + every effect priced. 15,494 configurations: 8,709 offered, 4,521 joint
  before C1. arcan's bar: 7 of 8 fully offered; Explosive Runes / Hideous Laughter withheld (C2). Slices C1–C11 are ordered
  by the author's bar, then gain × use × risk.
- **C1:** "is the main target" is a stable fusion guard (Molten Orb). A buff whose whole effect is HP damage each round
  (`CombatMath.IsPeriodicDamageCarrier`: Molten Orb's and Incendiary Runes' burning, Acid Arrow's acid) is priced in the
  joint value by save outcome (`TryFusionPeriodicDamage`). The ticks also join the friendly-fire cost and the burst
  re-check. The periodic reader accepts `IgnoreUnitModifiers` and `UseDCFromContextSavingThrow` (both exact for ticks).
- **Coverage:** offered 9,209 and joint 5,039, none lost. Molten Orb 0 → 338; Acid Arrow 38 → 208.
- **Checks** on `83ac4c1`:
  - Release 0 warnings; 1,052 unit + 27/21/3 script tests.
  - ProfileAudit `FUSION-DOT` 39 and `FUSION-OUTCOME` 33; every other line is unchanged.
  - Native harnesses 98/14/10; 1 reference added, 1 removed.
- **Package** `test-builds/AI-Control-FACC-v0.3.60-dev-fusiondot1-83ac4c1-UMM-test.zip`, MVID `add36407`. Manifest 0.3.60;
  no `main`, tag or release.
- **Next:** the author plays fusiondot1 (replay in the slice page); then C2 (Hideous Laughter's Best Jokes recast). Open
  from before: the death-trigger proposal and REVIEW_86_89's R1–R7.

**Earlier handover (17 November 2026, Claude): PRs #81–#91 reviewed and merged into development (`2f77d02`) at the
author's request.** Read `docs/TACTICAL_AI.md` §200 and the [receipt](docs/competence/INTEGRATION_81_91_CHECKS.json).
- Merged in stack order; development's tree equals the checked #91 head `a0311d1`. Codex's #90 and #91 were re-read
  against the supplied assembly, and no defect was found.
- Checks on `a0311d1`: Release 0 warnings/errors, 1,044 unit + 27/21/3 script tests, ProfileAudit A61/B48/C35/D34 and
  `BUFF-CONTEXT` 39 with every other line, the Fusion coverage and the summon detail unchanged, harnesses 98/14/10.
- The play package for development is **review2fix1** (`d669ec1`, MVID `e1aa77fe`), unplayed. Manifest 0.3.60; no
  `main`, tag or release.
- **Open:** the death-trigger proposal and REVIEW_86_89's R1–R7. **Next:** the Magic Fusion coverage programme, starting
  with Molten Orb and Acid Arrow (author request).

**Earlier handover (17 November 2026, Codex): review2 playtest review on `codex/review2-playtest`, stacked on
PR #90 `29cfc4e`. Nothing merged.** Read [docs/REVIEW2_PLAYTEST.md](docs/REVIEW2_PLAYTEST.md), §199,
[inputs](docs/competence/REVIEW2_PLAYTEST_INPUTS.json), [checks](docs/competence/REVIEW2_PLAYTEST_CHECKS.json) and
[compiler/reference provenance](docs/competence/REVIEW2_PLAYTEST_BUILD_PROVENANCE.json).

- Played **review2-f0b6097**, module **`0c600255`**: **13 fights, 643 TB / 669 RT decisions**. ToyBox spawns, rests between
  fights, Grabber left to the engine. One successful TB Death from Above (hit and grapple), seven grapple releases,
  two walk-in burst cancellations. No AI exception. Invocation **62 issues / 61 acted**, with contextual transfers;
  no arbitrary cap or stronger settling policy is added. The RT form case retains the form option; healing wins at 4% HP.
- **New high-impact gap:** two Risen Follower death sequences cause **485 of 501** party damage in the final fight.
  Native `DeathActions`/cast chains and actual payloads confirmed. See
  [death-trigger safety proposal](docs/DEATH_TRIGGER_SAFETY_PROPOSAL.md). Not implemented: flat kill penalties can stop
  progress; complete policy, terminal outcomes and any movement need design/author approval. No additional files needed
  for the initial scoped investigation. Do not call this gap fixed by the new ZIP.
- **Implemented performance correction:** completely read permanent constant stat buffs omit an unused preview context,
  with all twelve invocations checked. Same appraisal/stacking/holder/sibling/settling rules; unknown/ranked/timed/
  equipment effects retain context. No actor cache, scoring weights, extra rule/world query or movement change. The
  warm eligibility predicate allocates zero bytes in the offline host; no Unity speed-up claim.
- **Tool correction:** InvocationLedger counts the initial combat fragment (scope 0) as well as twelve later starts;
  casts/classifications/sequences unchanged. **21 ledger tests** (three new).
- Checked source **`d669ec1`**, startup MVID **`e1aa77fe-fef8-4496-aae9-9c751d2974ec`**. Release **0 warnings/errors**, **1,044 unit + 27/21/3 scripts**,
  new **BUFF-CONTEXT 39**, inherited **A61/B48/C35/D34**, native **98/14/10** with Psionics, Magus **44** pass. All previous
  audit rows, both **8,709/15,494** Fusion matrices and summon-kit detail equal the frozen played review2 on identical
  inputs. **0 added/removed native signatures**. Raw Achaekek enum failure remains an explicit negative test.
- Next package: [review2fix1](test-builds/AI-Control-FACC-v0.3.60-dev-review2fix1-d669ec1-UMM-test.zip), **UMM 0.3.60**, SHA-256
  `e2a3d3922c62c0dcc3415a0254facc3bbc15a661957b852f064326824216905e`; frozen checked DLL through later docs commits, identity/checksum sidecars. New package unplayed. Replay
  ordinary TB/RT invocations and timed/equipment buffs, then the earlier targeted cases. Send **Log.txt + combatLog.txt**,
  startup module and spawn/rest/manual-order context. Death explosions and REVIEW_86_89 R1–R7 limits remain.
- [Draft PR #91](https://github.com/recovribox-svg/AIControll-FACC/pull/91) targets development and follows **#81 → … → #90**, after author clearance. No merge,
  Claude-branch/development/main push, tag or release; no PR polling/reminders.

**Earlier handover (17 November 2026, Codex): critical review of draft PRs #86–89 on
`codex/review-86-89`, stacked on #89 `32943a6`. Nothing merged or played.** Read
[docs/REVIEW_86_89.md](docs/REVIEW_86_89.md), `docs/TACTICAL_AI.md` §198,
[checks](docs/competence/REVIEW_86_89_CHECKS.json) and
[compiler/reference provenance](docs/competence/REVIEW_86_89_BUILD_PROVENANCE.json).

- **F2 corrections:** match each pending native hand/iterative to the cached routine; read an issued command's actual
  animation window and adjacent selected/released sequence flags; do not infer a sequence from its table. Respect cheap
  native readiness gates and overlapping cooldown/approach, per-hand delivery and the issued approach radius. No RNG,
  native command execution, reservation or commitment change.
- **Burst corrections:** match ABPath/XPath's original request; trim the forecast at the full native approach distance
  with native height handling; revalidate retained roster references. Future LOS and repaths are not proved. No movement
  order, path search, retreat or stuck-policy change.
- **F1/F3:** remove unsupported range slack and qualify the incomplete engagement proof; a known expired suppressed
  debuff/horizon wins over unknown suppression. Ordinary F3 relative normalization stays unchanged.
- **Open decisions:** R1 origin-aware 3D/LOS/concealment quotes and cache identity remain high priority; watch overflow
  can still remove TB landing metadata or bypass new pending-danger registration. Cached ally effects/freshness,
  candidate-time control preparation, animation/projectile timing and relative cleanse valuation have explicit
  proposals in the review. Do not label them fixed or silently broaden movement/commitment policy.
- **Checked source `f0b6097`**, startup MVID **`0c600255-e6b8-4788-a5bc-4e0a9ad5f7e9`**. Release **0 warnings/errors**, **1,044 unit**,
  **27/18/3 scripts**, audit **A61/B48/C35/D34**, native **98/14/10** with Psionics, Magus **44** pass. All unrelated
  report rows, both **8,709/15,494** Fusion matrices and full summon-kit detail equal untouched #89 on identical inputs.
  **7 added / 3 removed signatures**, all resolved. Full corpus **45,248**, no prerequisite skips; raw Achaekek enum
  mismatch retained as an expected negative input test. SDK **8.0.426 / Roslyn 4.11**, 28 reference hashes recorded.
- **One package to play:** [review2](test-builds/AI-Control-FACC-v0.3.60-dev-review2-f0b6097-UMM-test.zip), UMM **0.3.60**,
  SHA-256 `3e8e46b97994ad21281e7e06ef67096a4b21021a13689284316f75ee7bad9b4b`; identity/checksum sidecars. Frozen checked DLL across later docs commits. This replaces cleanse1
  as the next test, not a stable release. Follow the review's TB/RT replay; send **Log.txt + combatLog.txt**, startup
  module and spawn/rest/manual-order context.
- [Draft correction PR #90](https://github.com/recovribox-svg/AIControll-FACC/pull/90) targets **development**, merge only after author clearance and **#81 → #82 → #83 → #84 → #85 →
  #86 → #87 → #88 → #89**. No merge, Claude-branch/development/main push, tag or release; no PR polling/reminders.

**Earlier handover (17 November 2026, Claude): F3 on `claude/cleanse-horizon`, stacked on the burst follow-ups (draft PR
#88) → F2 (#87) → F1 (#86) → #85 → #81, all unmerged. Not played.** Read [docs/CLEANSE_HORIZON.md](docs/CLEANSE_HORIZON.md),
`docs/TACTICAL_AI.md` §197, the [receipt](docs/competence/CLEANSE_F3_CHECKS.json) and
[provenance](docs/competence/CLEANSE_F3_BUILD_PROVENANCE.json). Both modes, valuation only.
- **F3 (C-R1).** A suppressed debuff's cleanse earns the share of its rounds left within the horizon that it acts after its
  suppressor ends (`SuppressionRules.ActiveShare`, `AbilityProfile.RemovedActiveShare`). An unsuppressed one keeps its
  full value; the dispel fallback skips a buff suppressed for the rest of the horizon.
- **Checks** on `02163d7`: Release 0 warnings/errors, 1,032 unit + 27/18/3 script tests, ProfileAudit `BUFF-SUPPRESS` 34
  with every other line unchanged, harnesses 98/14/10, no signature change.
- **Package** `test-builds/AI-Control-FACC-v0.3.60-dev-cleanse1-02163d7-UMM-test.zip`, MVID `2799ef2d`: everything in the
  open stack (A–D + #85 + F1 + F2 + burst follow-ups + F3), the one to play.
- **Next:** Codex reviews the Claude stack (#86–#89) and corrects what needs it; then the author plays cleanse1. Every
  selected follow-up is done; the full geometry quote stays not selected. Merge order when cleared: #81 → #88, then this
  PR. No merge, `main`, tag or release.

**Earlier handover (17 November 2026, Claude): burst follow-ups A-R1, A3 and A-P1 (author-approved) on
`claude/burst-path-forecast`, stacked on F2 (draft PR #87) → F1 (#86) → #85 → #81, all unmerged. Not played.** Read
[docs/BURST_PATH_FORECAST.md](docs/BURST_PATH_FORECAST.md), `docs/TACTICAL_AI.md` §196, the
[receipt](docs/competence/BURST_PATH_CHECKS.json) and [provenance](docs/competence/BURST_PATH_BUILD_PROVENANCE.json).
Real time.
- **A-R1, walks along the game's path.** The burst lead, the enemies inside, the walking allies and the re-check follow
  each walking unit's movement-agent path (`ReadRoute`, `WalkRoute`), once per decision, at most 64 points, no path
  search. Otherwise they use the straight walk.
- **A3.** An ally walking in is priced by the decision's own friendly-control rule (`BurstPlan.AllyControl`) as well as
  by its damage.
- **A-P1.** One roster per frame; damage estimated once per watch; a watch whose cast has not gone off is never dropped
  (cap 64); a `[Burst] fight summary` line per fight.
- **Checks** on `62dede5`: Release 0 warnings/errors, 1,025 unit + 27/18/3 script tests, ProfileAudit `BURST-AIM` 48
  with every other line unchanged, harnesses 98/14/10. 1 added signature (the next-waypoint field read, resolved);
  `Mathf.Lerp` removed.
- **Package** `test-builds/AI-Control-FACC-v0.3.60-dev-burstpath1-62dede5-UMM-test.zip`, MVID `1b3df06e` (A–D + #85 + F1 +
  F2 + these).
- **Next:** the author plays (replay in the page; send the fight-summary lines). F3 stays low priority. The full
  geometry quote is approved but not selected (reasons in the plan). Merge order when cleared: #81 → #87, then this PR.
  No merge, `main`, tag or release.

**Earlier handover (17 November 2026, Claude): review follow-up F2 on `claude/disrupt-marginal`, stacked on F1 (draft
PR #86) and PR #85 (#81 → #86 all unmerged). Not played.** Read [docs/DISRUPT_MARGINAL.md](docs/DISRUPT_MARGINAL.md),
`docs/TACTICAL_AI.md` §195, the [receipt](docs/competence/DISRUPT_F2_CHECKS.json) and
[provenance](docs/competence/DISRUPT_F2_BUILD_PROVENANCE.json). Real time, valuation only.
- **Allies' ordered hits.** Each ally's attack on a casting enemy was credited with the full break chance. Now, allies'
  running or approaching attacks on that caster are read once per decision, and this unit's value is
  `others-survive × own-break × stakes`. Nothing is reserved.
- **Native spacing.** A routine's second hit lands 6 s / N after the first (`UnitAttack.OnTick`) instead of 0.7 s;
  bow-style and sequenced attacks chain.
- **Checks** on `bcb46df`: Release 0 warnings/errors, 1,017 unit + 27/18/3 script tests, ProfileAudit `CAST-DISRUPT` 28
  with every other line unchanged, harnesses 98/14/10, 6 added references (all resolved).
- **Package** `test-builds/AI-Control-FACC-v0.3.60-dev-disrupt2-bcb46df-UMM-test.zip`, MVID `9a4d8a66` (A–D + #85 + F1 + F2).
- **Next:** F3 (horizon-aware cleanse) is low priority. The geometry quote, path forecast and watch overflow await the
  author. Merge order when cleared: #81 → #86, then this PR. No merge, `main`, tag or release.

**Earlier handover (17 November 2026, Claude): review follow-ups planned; F1 done on `claude/flank-engagement`, stacked on
PR #85 (#81 → #85 all unmerged). Not played.** Read [docs/REVIEW_FOLLOWUPS_PLAN.md](docs/REVIEW_FOLLOWUPS_PLAN.md),
[docs/FLANK_ENGAGEMENT.md](docs/FLANK_ENGAGEMENT.md), `docs/TACTICAL_AI.md` §194, the
[receipt](docs/competence/ENGAGE_F1_CHECKS.json) and [provenance](docs/competence/ENGAGE_F1_BUILD_PROVENANCE.json).
- **Assessment of Codex's open items.** Selected: F1 (D-R1) and F2 (B-R1, marginal disruption against friendly hits already
  issued). F3 (C-R1) is low priority. The origin-aware geometry quote, the path-based burst forecast and the watch overflow
  need approval; timing calibration and the TTT fortification provider need evidence.
- **F1 (both modes, valuation only).** A melee attacker becomes an engager when the game says it engages the target now
  (`UnitCombatState.IsEngage`), or when its threat range covers the attacking weapon's range and the target is not totally
  concealed from it now. Concealment never grows closer in. No geometry, path, raycast or cache.
- **Checks** on `7f749bb`: Release 0 warnings/errors, 1,008 unit + 27/18/3 script tests, ProfileAudit `SNEAK` 32 with every
  other line equal to a PR #85 build on the same inputs, harnesses 98/14/10, 1 added reference. SDK 8.0.428 / Roslyn 4.11.
- **Package** `test-builds/AI-Control-FACC-v0.3.60-dev-engage1-7f749bb-UMM-test.zip`, MVID `ac0cde8e` (A–D + #85 + F1).
- **Next:** F2. Merge order when cleared: #81 → #85, then this PR. No merge, `main`, tag or release.

**Earlier handover (17 November 2026, Codex): critical review of open stacked PRs #81–84 on
`codex/review-81-84`, based on #84 `53e793f`; nothing merged.** Read
[docs/REVIEW_81_84.md](docs/REVIEW_81_84.md), §193,
[checks](docs/competence/REVIEW_81_84_CHECKS.json) and
[compiler/reference provenance](docs/competence/REVIEW_81_84_BUILD_PROVENANCE.json).
Five runtime corrections: future independent fusion damage no longer intersects today's pool selection; attack modes
reprice disruption with their own timing/survival scale; suppression filtering reads each recipient once and carries
native aura context; partner-flank gain asks the native AB rule and includes existing sneak/critical damage, cached per
routine. Blueprint descriptor inheritance was checked and retained. Native engagement still needs reach/LOS/concealment:
the prospective all-routine assumption remains a **high-priority proposal requiring author approval**, not a verified
native fact. Conditional TTT fortification, marginal multi-ally disruption, forecast/capacity and future-cleanse limits
are explicit in the review. Do not merge the stack before author playtesting/decisions.

Checked source **`2d8a9b5`**, startup MVID **`9d4c4d08-9419-41da-b4e1-81c60270fc45`**; **1,000 unit + 27/18/3 script tests**,
real-reference Release **0 warnings/errors**, audit **A33/B19/C33/D25**, native **98/14/10**, Magus **44** pass.
Other report rows and the complete **8,709/15,494** Fusion matrix are identical to an untouched #84 build on identical
inputs. The exact historical 607-stock manifest was recovered; full audit loads 45,248, three fewer than the old receipt
whose extra graph files are unavailable. No hidden prerequisite skips. **0 added / 0 removed native signatures** versus
#84. SDK 8.0.426 / Roslyn 4.11, all compiler/reference hashes recorded. The test ZIP
[test-builds/AI-Control-FACC-v0.3.60-dev-review1-2d8a9b5-UMM-test.zip](test-builds/AI-Control-FACC-v0.3.60-dev-review1-2d8a9b5-UMM-test.zip)
is **unplayed**, UMM **0.3.60** unchanged, SHA-256 `8989b793a43cbd40652b4d8e645d6c6c71414c7ff2317de09d324c9879621f4a`; identity/checksum sidecars. The checked DLL is frozen across
this later documentation commit. Correction [PR #85](https://github.com/recovribox-svg/AIControll-FACC/pull/85) targets development and is **draft**; integrate only after author clearance,
**after #81 → #82 → #83 → #84**.
No Claude-branch/development/main update, tag or release. Author next: replay A–D plus review witnesses with paired logs
and startup module; decide the separately proposed geometry/coordination slices. No PR polling/reminders.

**Earlier handover (17 November 2026, Claude): sneak attack fortification and flanking, slice D, on
`claude/sneak-flanking`, stacked on slice C (`claude/buff-suppression`), slice B and PR #81 (slice A); no PR yet. Not
played.** Read [docs/SNEAK_FLANKING.md](docs/SNEAK_FLANKING.md), `docs/TACTICAL_AI.md` §192 and the
[receipt](docs/competence/SNEAK_FLANKING_CHECKS.json). Both modes, valuation only. D1: one fortification roll negates
sneak dice, a confirmed critical and a precise strike (`RuleAttackRoll`); attack lines, critical chances, attack-roll
abilities and archer blast modes pass `1 − chance / 100`. D2: native flanking counts engagers, not positions; a melee
attack makes its attacker an engager, so routines are priced flanked (+2, sneak) when one other unit counts as engaging
the target by the mode's rule, and an attack that creates the flank adds the other engager's round of gain
(`[flanks with <ally> +N]`). Checks on `86f8e82`: Release 0 warnings/errors, 998 unit + 27/18/3 script tests,
ProfileAudit `SNEAK` 20 (42 fortification facts) with every other line unchanged, harnesses 98/14/10, 3 added
references. Package `test-builds/AI-Control-FACC-v0.3.60-dev-sneak1-86f8e82-UMM-test.zip`, MVID `e432cb9a` (includes
slices A-C). The plan's four slices are done; next: the author plays the A-D replays, then PRs for B-D after PR #81.

**Earlier handover (17 November 2026, Claude): suppressed buffs, slice C, on `claude/buff-suppression`, stacked on
slice B (`claude/cast-disruption`) and PR #81 (slice A); no PR yet. Not played.** Read [docs/BUFF_SUPPRESSION.md](docs/BUFF_SUPPRESSION.md),
`docs/TACTICAL_AI.md` §191 and the [receipt](docs/competence/BUFF_SUPPRESSION_CHECKS.json). Both modes, valuation only.
C1: a buff the recipient's form or hex would suppress on arrival (`UnitPartBuffSuppress`: Enlarge Person or Haste on a
formed ally) is worth 0 for spells, toggle grants and out-of-combat upkeep, with one logged reason per fight and recipient.
C2: a suppressed harmful buff counts for a cleanse only if it outlasts its suppressor; a dispel counts only its
unsuppressed rounds. Checks on `3e70345`: Release 0 warnings/errors, 982 unit + 27/18/3 script tests, ProfileAudit
`BUFF-SUPPRESS` 25 (reader matches the game on all 337 exported suppressors) with every other line unchanged, harnesses
98/14/10, 10 added references. Package `test-builds/AI-Control-FACC-v0.3.60-dev-suppress1-3e70345-UMM-test.zip`, MVID
`00a9466f` (includes slices A and B). Next: slice D (sneak attack: fortification, second-attacker flanking); the author
plays the A/B/C replays.

**Earlier handover (17 November 2026, Claude): cast disruption, slice B, on `claude/cast-disruption`, stacked on
PR #81 (slice A, `claude/burst-ground-aim`); no PR yet. Not played.** Read [docs/CAST_DISRUPTION.md](docs/CAST_DISRUPTION.md),
`docs/TACTICAL_AI.md` §190 and the [receipt](docs/competence/CAST_DISRUPTION_CHECKS.json). Real time, valuation only:
a hit before an enemy's cast goes off forces the native concentration check (10 + spell level + damage / 2), and a
failure spends the spell. Weapon attacks (first one or two attacks) and damaging abilities that can land in time gain
the chance they break the cast times its stakes. The stakes are the spell's harm on its victim (shared `EnemyThreat.AbilityHarm`,
identical threat results), else the caster's worth for a round. Checks on `6845b83`: Release 0 warnings/errors,
965 unit + 27/18/3 script tests, ProfileAudit `CAST-DISRUPT` 15 and `BURST-AIM` 28 with every other line unchanged,
harnesses 98/14/10, 1 added reference. Package `test-builds/AI-Control-FACC-v0.3.60-dev-disrupt1-6845b83-UMM-test.zip`,
MVID `549869b4` (includes slice A). Next: review PR #81 first; the author plays both replays; then slice C.

**Earlier handover (17 November 2026, Claude): burst ground aim, slice A (A1-A5, author-approved) on
`claude/burst-ground-aim`, stacked on the plan branch over development `ad63ee8` (no PR yet). Not played.** Read
[docs/BURST_GROUND_AIM.md](docs/BURST_GROUND_AIM.md), `docs/TACTICAL_AI.md` §189 and the
[receipt](docs/competence/BURST_AIM_CHECKS.json). Bursts that can hurt allies and land after their cast goes off
(Explosive Runes and its fusions, Wail, Ice Storm, Flame Strike, Fireball-like projectiles) are aimed at the ground where
the enemy will be when they land; real time re-checks the cast until it goes off and cancels it for free, shares the
landing spot with the other AI units' walks, and a turn-based turn waits for the landing. Enemy-only, Selective,
attack-roll and lag-free bursts keep unit aim. Checks on `d7534ed`: Release 0 warnings/errors, 953 unit + 27/18/3
script tests, ProfileAudit `BURST-AIM` 28 with every other line and the Fusion coverage unchanged, harnesses 98/14/10,
9 added references all resolve. Package `test-builds/AI-Control-FACC-v0.3.60-dev-burst1-d7534ed-UMM-test.zip`, MVID
`bc92b339`; manifest 0.3.60, no `main`, tag or release. Next: the author plays the replay in the page; then slice B.

**Earlier handover (17 November 2026, Claude): plan only, on `claude/burst-aim-tactics-plan` (no PR yet).** Read
[docs/BURST_AIM_AND_TACTICS_PLAN.md](docs/BURST_AIM_AND_TACTICS_PLAN.md). It covers four slices:
A, delayed bursts aimed at the ground (the author chose ground aim; A3 pre-cast cancel, A4 landing-spot avoidance and
A5 turn-based wait need approval); B, real-time disruption of enemy casts by the native concentration rule; C, buffs
suppressed on arrival; D, fortification and second-attacker flanking for sneak attack. The supplied feature document
targets the old engine and several of its rules are not this game's (section 2). No code, manifest or package change.

**Earlier handover (17 November 2026, Claude): PR #80 reviewed and merged into development (`b218c28`); Codex's
review and merge of PRs #74-79 assessed and agreed with.** Read `docs/TACTICAL_AI.md` §188 and the
[receipt](docs/competence/INTEGRATION_80_CHECKS.json). Development's tree equals PR #80's head `5fe5771`; the play
package is still the outcome3 ZIP below (v0.3.60, MVID `56f51d96`), unplayed. Checks on development: Release 0 warnings
and errors, 935 unit + 27/18/3 script tests, ProfileAudit FUSION-POOL 39 with every other line unchanged, native harnesses
98/14/10. Codex's log counts were not re-checked. Next: the author plays outcome3; delayed bursts that follow a moving
target (Explosive Runes) remain an open friendly-fire limit whose fix would touch aiming, so it needs the author's approval.

**Earlier handover (16 November 2026, Codex): outcome2 playtest review and restored pooled-fusion area safety on
`codex/outcome2-playtest-review`, development base `acb1003`.** Read
[findings/correction/replay](docs/OUTCOME2_PLAYTEST_REVIEW.md), [input accounting](docs/competence/OUTCOME2_PLAYTEST_INPUTS.json),
[Invocation ledger](docs/competence/OUTCOME2_INVOCATION_LEDGER.json), §187. New repair is unplayed.
The played MVID matches outcome2; author confirms spawns, rests between fights and engine-controlled Grabber.
Eleven fights, 740 issues / 735 terminals: four missing summon terminals near departure plus one trailing OOC cast,
not fabricated failures. Thirty fusion issues / 18 DidAct; pooled Sleep is issued, saved Frigid still is not.
Five RT grapple releases act. Summons have 181 issues / 131 DidAct. Invocation 52 issues / 50 DidAct, no established
settle/stacking violation; no new cap or assignment policy.

Friendly fusion damage is recorded. F7 wrongly disabled existing walking-in ally safety whenever a pool forecast
succeeded. A broad fused area now retains that check for its independent payload. Native pool/order/eligibility,
Sleep targeting, Selective/immunity/damage policy remain. No new independent path/world/forecast pass or persistent actor cache,
movement or cancellation policy. Eight production-profile/route audit checks cover the correction. Moving burst
centres after selection are a separate material limitation; do not promise this repairs every recorded friendly hit.
A no-reach message and combat damage to the apparent target also need a controlled observer replay, not a guessed
blocking change. Warm ability costs, missing summon trace terminals and prior unplayed replays remain documented.
Checked runtime source **`faf867c`**, MVID **`56f51d96-d662-449f-af2b-7222e5d11aed`**: **935 unit + 27/3/18 script tests**,
real-reference Release **0 warnings/errors**. ProfileAudit 44,640 loaded / 4,808 abilities, no load/reader failure
or prerequisite skips; **FUSION-POOL 39 (8 new)**, FUSION-OUTCOME 34, DIAGNOSTIC-LOG 22, COUP-REACH 32 with the
supplied TTT provider and inherited checks pass. Magus 44, providers **98/14/10**, **0 new native signatures**.
Other report rows and the full **8,709 / 15,494** fusion coverage are unchanged on identical inputs.
[Checks](docs/competence/OUTCOME2_PLAYTEST_CHECKS.json),
[compiler/reference provenance](docs/competence/OUTCOME2_PLAYTEST_BUILD_PROVENANCE.json): SDK 8.0.426 / Roslyn 4.11,
UMM 0.33.2.0. [Cumulative outcome3 UMM test ZIP](test-builds/AI-Control-FACC-v0.3.60-dev-outcome3-faf867c-UMM-test.zip), SHA-256
`1bfd4ebe048814e2b0fd04745b602076705a929e89992bcc25665cf2eb6ccf2a`, identity/checksum sidecars. **The author requested UMM v0.3.60** after runtime validation:
Info.json metadata commit `082216f` supplies that menu/startup version; C#/project and validated DLL
remain unchanged. No stable release/main/tag, no merge. New correction unplayed; moving-target burst safety and
the other replay limits above remain. Newly recognized walking recipients use existing ordinary valuation;
no new independent path/world/forecast pass or persistent cache, no measured Unity speedup.


**Earlier handover (16 November 2026, Codex): outcome1 review and author-authorized integration of all
six open PRs #74-79 into development.** Read [review/integration/replay](docs/OUTCOME1_INTEGRATION.md),
[playtest findings](docs/OUTCOME1_PLAYTEST_REVIEW.md), [checks](docs/competence/OUTCOME1_INTEGRATION_CHECKS.json), §186.
Exact reviewed heads are merge ancestors; clean checked source **`04a7b9f`**, MVID
**`2f9ef9ef-8d7a-4574-9b98-fc8383b45bee`**. **935 unit + 27/3/18 script tests**, real-reference Release **0 warnings/errors**;
ProfileAudit 44,640 loaded / 4,808 abilities, no reader/load failures or prerequisite skips. **FUSION-OUTCOME 34,
FUSION-POOL 31, COUP-REACH 32, DIAGNOSTIC-LOG 22**, inherited rows unchanged, supplementary Magus 44,
providers **98/14/10**, all **4** added native signatures resolve. Coverage **8,709 / 15,494** admitted (498 pooled),
zero reader/cone errors. [Compiler/reference provenance](docs/competence/OUTCOME1_INTEGRATION_BUILD_PROVENANCE.json):
SDK 8.0.426 / Roslyn 4.11, supplied UMM 0.33.2.0.

[Cumulative outcome2 UMM test ZIP](test-builds/AI-Control-FACC-v0.3.52-dev-outcome2-04a7b9f-UMM-test.zip), SHA-256
`9c84960e25ff22925462b53ee6ac1ec2dcaa42ff6544d637e0a2d77b8f517486`; identity/checksum sidecars, unchanged manifest **0.3.52**. Frozen checked DLL across later docs/receipt commits.
No main, tag or release change. #79 keeps only effective pool metamagic; the speculative friendly-Sleep veto/map was
withdrawn, and earlier broader receipts are explicitly superseded. Native Sleep targeting/effects and damage safety
remain. Mixed-delivery per-effect protection is a controlled-native-baseline reconciliation item, not a reproduced bug.
The runtime merge preserves Coup hand verification plus shared fusion admission after the user blocklist.

Outcome1: nine fights, 666/666 issue-terminal matches, no AI exception; 66 Invocation DidAct without identified
settling/same-stat error. Cambion 13.2 m radius/Claw confirms a hand inconsistency, not distance of the hit. New
corrected Coup, F7, diagnostics and metamagic are **unplayed**. Next: paired TB/RT Coup and pooled-fusion replays,
actual saved Frigid including a passed save, bounded refusal details, TB release/actual leap with Grabber left to AI.
Warm ability work, ledge approach and prior holder/monster limits remain; no Unity speedup/stable acceptance claim.

**Earlier handover (16 November 2026, Codex): F7 review correction narrowed to effective metamagic on
`codex/fusion-pool-safety`, stacked on PR #77/#76.** Read [current correction](docs/FUSION_POOL_METAMAGIC.md), §185.
The extra friendly-Sleep veto/map was withdrawn after the author clarified ally targeting; no such incident was
shown in outcome1 and no candidate ZIP was supplied/merged. Existing damage safety remains. Earlier `f8788d7`
905-test/32-check receipts describe the superseded broader candidate, not the narrowed final source. Final checked
source **`097ff32`**, MVID **`cfcce6de-fbc9-428b-9247-15a89d9934f2`**: **899 unit + 27/3/18 script tests**,
real-reference Release **0 warnings/errors**, FUSION-POOL **31** / FUSION-OUTCOME **34**, inherited checks,
supplementary Magus 44 and providers **98/14/10** pass. Zero added native signatures; normal matrix remains
**8,709 / 15,494** admitted (498 shared-pool), zero reader/cone errors.
[Final checks](docs/competence/FUSION_POOL_METAMAGIC_CHECKS.json),
[final provenance](docs/competence/FUSION_POOL_METAMAGIC_BUILD_PROVENANCE.json). The combined package follows integration. Ordinary Sleep's enemy-only selector and an
enemy-only fusion delivery have explicit regression checks. No normal admission, movement or targeting policy change.

**Earlier handover (16 November 2026, Codex): outcome1 playtest review and diagnostic correction on
`codex/outcome1-playtest-review`, base development `980cf26`.** The author supplied outcome1 module `fbc220a5`,
confirmed spawns/rests between fights, and separately authorized reviewing/merging satisfactory open PRs next.
Read [review/evidence/replay](docs/OUTCOME1_PLAYTEST_REVIEW.md), §184,
[input receipt](docs/competence/OUTCOME1_PLAYTEST_INPUTS.json),
[Invocation ledger](docs/competence/OUTCOME1_INVOCATION_LEDGER.json),
[checks](docs/competence/OUTCOME1_PLAYTEST_CHECKS.json) and
[compiler/reference provenance](docs/competence/OUTCOME1_PLAYTEST_BUILD_PROVENANCE.json).
Nine encounters, 666 issued/666 terminal commands, no recorded exception. Saved Frigid Touch is admitted/valued
but never cast; F4 delivery and F7 are not gameplay-accepted. Six Coups act; Cambion Bard's 13.2 m radius pairs
with Claw damage/DC 12 (actual separation unknown), supporting #75's hand mismatch. Three RT grapple releases
act. Invocation: 68 issues/66 DidAct, no identified settle/stacking violation; no new cap or selection rule.

1,060 repeated native path refusal details share one elevated goal. Verdicts now return scalar witnesses; verbose
first/material-change diagnostics are bounded (128 caster IDs × 16 exact queries × five reports/fight), with no
actor/context/path/result cache. Native checks, failure strings, dispatch/retry/target/movement/stuck rules stay.
Rounded-log replay retains 33 details, not a live Unity speedup. Warm ability work remains substantial.
Checked clean source **`3a951dc`**, MVID **`264dc5eb-3552-48fa-8f9c-cfac958f5dc6`**: **883 unit + 27 + 3 + 18 tests**,
real-reference Release **0 warnings/errors**, compatible ProfileAudit **44,640 / 4,808**, no load/reader failure or
prerequisite skip; **DIAGNOSTIC-LOG 22**, inherited checks, supplementary Magus 44, native providers **98/14/10** pass.
**0 new native signatures**; other audit rows identical on the same augmented inputs. SDK 8.0.426 / Roslyn 4.11,
UMM 0.33.2.0; manifest 0.3.52. Checked DLL frozen across receipt-only commits. No standalone ZIP: the author wants
subsequent PR review/integration, then one validated combined candidate. New diagnostics are unplayed.

PR #77 review has found native-metamagic and pooled friendly-safety questions to resolve separately before
integration; do not treat outcome1 (which predates #77) as evidence of those issues occurring in play. Keep the
F4/F7, Coup, TB grapple and leap replays separate. No main/tag/release action is authorized by the integration request.

**Earlier handover (16 November 2026, Claude): Fusion F7 (Sleep fusions) on `claude/fusion-sleep-pool`, stacked on
Fusion F4 (PR #76, `claude/fusion-outcome-effects`); development base `980cf26`. Not played.** Read
[docs/FUSION_SLEEP_POOL.md](docs/FUSION_SLEEP_POOL.md), `docs/TACTICAL_AI.md` §183 and the
[receipt](docs/competence/FUSION_F7_CHECKS.json). Review PR #76 first.
- **Why.** The author's gaps3 log shows arcan's Snowball/Sleep and Explosive Runes/Sleep withheld ("shared Hit Dice
  selection"); the author asked for the Sleep fusions next.
- **Native finding.** A fused cast's context is the holder ability's. Its pool is the holder slot's own 2d6 roll, not
  Sleep's fixed 4 Hit Dice. Touch slots carry no pool.
- **Change (both modes).** `SharedHdControl` forecasts the fused pool: one pool source spell, the holder's exact dice
  distribution, the deliver spell's recipients in the game's order. `TacticalEngine.FusionWithheld` offers the fusion
  when that plan and the joint outcome value qualify. The sleep is credited with the forecast chance, and only when the
  cast's own damage comes before it.
- **Result.** 8,709 of 15,494 templates admitted (8,211 after F4): Sleep and Rainbow Pattern fusions. None lost.
  1,034 pooled templates stay withheld, each with a logged reason.
- **Checks** on `cf41e4b`: Release 0 warnings; 887 unit + 27 + 3 + 18 tests; 0 added references; ProfileAudit exit 0,
  two runs identical, `FUSION-POOL` 25, other lines equal to the F4 run; native harnesses 98/14/10.
- **Package:** `test-builds/AI-Control-FACC-v0.3.52-dev-sleep1-cf41e4b-UMM-test.zip`, MVID
  `61f28efb-85ea-4900-97d6-e9f1328da60e`. It contains F4 too, and not PR #75. Manifest 0.3.52; no `main`, tag or
  release change.
- **Open:** PR #74 (research) and PR #75 (Coup de Grace reach) from Codex, not reviewed here.
- **Next:** the author plays sleep1 (replay in the page). The order of the two spells in each fusion matters: with Sleep
  first, the damage wakes the sleepers and the sleep gets no credit.

**Earlier handover (16 November 2026, Claude): Fusion F4 slices A and B on `claude/fusion-outcome-effects` (no PR yet),
based on development `980cf26`. Not played.** Read [docs/FUSION_OUTCOME_EFFECTS.md](docs/FUSION_OUTCOME_EFFECTS.md),
`docs/TACTICAL_AI.md` §182 and the [receipt](docs/competence/FUSION_F4_CHECKS.json). The plan and the comparison with
tactical mobility are in [docs/FUSION_OUTCOME_EFFECTS_PLAN.md](docs/FUSION_OUTCOME_EFFECTS_PLAN.md).
- **Why.** After Codex's research (PR #74) the author asked for one plan, agreed with "Fusion first" and said to begin.
- **Change (both modes).** A fused spell with an effect on a passed save or on both outcomes was always withheld. It is
  now offered through the joint outcome value, which keeps one control envelope per save outcome
  (`TacticalEngine.FusionWithheld`, `TryFusionControlRounds`, `ValueModel.FusionValue`). Slice B lets Ray of
  Exhaustion's own exclusive branches pass the guard check.
- **Result.** 8,211 of 15,494 templates admitted (7,306 before): Fear, Boneshatter, Frigid Touch, Ray of Exhaustion.
  None lost; admitted fusions keep their values. 1,969 stay withheld by the new outcome gate.
- **The author's gaps3 log.** arcan's ten fusions: none was blocked by this rule; two Sleep fusions are withheld for
  "shared Hit Dice selection" (Fusion plan F7). He gains saved Frigid Touch fusions. He knows Molten Orb, Acid Arrow
  and Reduce Person (plan slices C and E).
- **Checks** on `f5ccb40`: Release 0 warnings; 881 unit + 27 + 3 + 18 tests; 0 added references; ProfileAudit exit 0,
  two runs identical, `FUSION-OUTCOME` 34, other lines equal to the §180 baseline; native harnesses 98/14/10.
- **Package:** `test-builds/AI-Control-FACC-v0.3.52-dev-outcome1-f5ccb40-UMM-test.zip`, MVID
  `fbc220a5-e4af-42eb-b386-e72ec1b61247`. It does not include PR #75. Manifest 0.3.52; no `main`, tag or release change.
- **Open:** PR #74 (research) and PR #75 (Coup de Grace reach) from Codex, not reviewed here.
- **Next:** the author plays outcome1 with a saved Frigid Touch fusion (replay in the page), then decides between the
  Sleep slice (F7), Molten Orb / Acid Arrow (C, E) and tactical mobility.

**Earlier handover (16 November 2026, Codex): AI Coup de Grace delivering-hand reach on
`codex/coup-de-grace-reach`, based on development `980cf26`; unplayed.** The author explicitly approved this fix.
Read [evidence/contract/TB-RT replay](docs/COUP_DE_GRACE_REACH.md), §181,
[checks](docs/competence/COUP_REACH_CHECKS.json) and
[compiler/reference provenance](docs/competence/COUP_REACH_BUILD_PROVENANCE.json).
Native Weapon ability range uses `GetFirstWeapon`; stock delivery uses `GetThreatHandMelee`, TTT uses `GetThreatHand`.
Appraisal, damage, sleeper preservation and shared preflight/final command radius now use the actual selected
melee weapon. The radius is bounded by both native ability and weapon radii (both bodies included); target legality
is rechecked after Init and before existing path preparation. Unknown/ambiguous delivery and TTT selecting a ranged
Snap Shot hand are withheld only by AI Control. Native/manual commands are not patched. Existing action economy,
full-round, LoS/height, pathfinding, movement and commitment policy remain. Gaps2 has **zero issued Coup commands**;
this is a verified source inconsistency, not a reproduced explanation of the reported episode.

Checked source **`f029d8e`**, startup MVID **`626629a3-5185-418c-bea4-dc6e22f5d3e5`**, DLL SHA-256
`eb9cd272ba74483c27a26b5233e1045f445b86a3dca1a5a83fe30615b4c9e22d`. **885 unit + 27 inventory + 3 script + 18 ledger tests**,
real-reference Release **0 warnings/errors**. Strict ProfileAudit **44,640 loaded / 4,808 abilities**, no load/reader
failure or prerequisite skip; **COUP-REACH 32**, real supplied TTT provider, inherited checks, supplementary Magus 44
and native providers **98/14/10** pass. All other audit rows match the existing checked gaps3 DLL on the identical
augmented inputs. **4 added external signatures resolve.** The actual identity guard allocates **0 bytes / 10,000
warm inert-wrapper calls**; no Unity timing/speedup claim. No additional path search pass, world scan, rule/context forecast or persistent actor
cache. A newly out-of-reach target uses existing approach/path preparation. Accepted-command reach notes are lazy and bounded per fight/caster/weapon.

[Test ZIP](test-builds/AI-Control-FACC-v0.3.52-dev-coup1-f029d8e-UMM-test.zip), SHA-256
`da0e1aaab488fd2b3f84949c2aa3a33fe7ee33bb7899a1ce23b34a22600e4c12`; identity/checksum sidecars and compiler provenance included in the branch.
SDK **8.0.426 / Roslyn 4.11**, UMM **0.33.2.0**, manifest **0.3.52**. Frozen checked DLL across later receipt/docs-only
commits. No merge, main/development push, tag or release. PR #74 is the independent research predecessor; its broader
feature proposals are not included. No new input files needed. Next: the focused companion/summon TB/RT Coup replay,
especially mixed bow/melee limbs, legal reach/natural/unarmed cases, TTT on/off and native target/LoS/path restrictions.
Equipment/form/provider changes **after acceptance but before native delivery** remain outside this dispatch-time
contract; require an actual trace before considering a delivery-time patch. Keep the earlier gaps3/grapple/form/
Frigid/fusion replay requirements as well.

**Earlier handover (16 November 2026, Codex): tactical expansion research, documentation only.**
Branch codex/tactical-expansion-investigation started at development **96dabaa** and is reconciled with
**980cf26**, after Claude integrated #65–73. Read
[investigation](docs/TACTICAL_EXPANSION_INVESTIGATION.md),
[advanced-model planning prompt](docs/TACTICAL_EXPANSION_PLANNING_PROMPT.md) and
[input/inspection receipt](docs/competence/TACTICAL_EXPANSION_RESEARCH.json).
The author clarified that WorldCrawl and Rogue Trader CompanionAI v3 are **design references, not compatibility
targets**. No third-party dependency, handler adapter, NPC quota, random casting or runtime LLM is proposed.

The inspected #73 candidate source **9a02565**, head **6efc13c**, now equals development's gameplay source;
the merge into this task branch preserves Claude's integration, with no new gameplay edits. Several claims in the uploaded investigation are historical or
refer to legacy paths: present threat, observed-DPR, concealment, immunity and buff/area forecasts already exist.
The S1 personal-AoE label can also disagree with runtime control scoring (Screech/Drone); verify before rewriting.
Stronger planning candidates are bounded native tactical mobility, verified hit/miss/proc forecasts, and timed
summon signature effects. The research ranks benefits/complexity and gives native contracts and acceptance checks.
Movement implementation still needs the author's specific approval; planning is authorized.

Gaps2 has **zero issued Coup de Grace** and no recorded summon takeovers, so the reported ranged execution is not
reproduced. Stock Weapon range reads the first weapon, whereas the native/TTT delivery and current damage estimate
may use different hands. A shared melee weapon/reach contract is proposed and scoped; no live fix is claimed.
Leaps and supported ordinary/no-save Frigid should be left to the engine for acceptance testing; #69's withheld
saved configurations remain a separate Fusion limit.

No runtime/project/manifest/binary/package changes, no new build or gameplay acceptance, no main/development push,
merge, tag or release. Static source/IL, selected stock graphs and log counts were checked. Third-party runtime
and performance claims were not adopted. The next step is for the advanced coder to select and write one
implementation-ready plan with a bounded first slice, not to port either architecture.

**Earlier handover (16 November 2026, Claude): all open PRs (#65, #66, #67, #68, #69, #70, #71, #73) reviewed and
merged into development at the author's request; development `ea91688`.** Read `docs/TACTICAL_AI.md` §180 and the
[receipt](docs/competence/INTEGRATION_65_73_CHECKS.json).
- The stack merged in order. Development's tree equals the verified head `6efc13c`, whose code is the gaps3 package's
  checked source `9a02565`. The play package is
  `test-builds/AI-Control-FACC-v0.3.52-dev-gaps3-9a02565-UMM-test.zip` (MVID `48ece05d`). No new package was built.
- Review: no change needed.
  - #71 fixes a defect in Claude's G2 (#67): a release that paid had a null reason and was dropped.
  - #69 matches the native selector, which runs one variant.
  - #68 and #70 tighten the Shifter and leap-grab work.
- Checks on the integrated tree:
  - Release 0 warnings; 866 unit + 27 + 3 + 18 tests.
  - 3 added game references, all present.
  - ProfileAudit exit 0, two runs identical, with 14 export folders and the 607 stock fixtures (identical to S1's,
    rebuilt from the #72 database).
  - Native harnesses 98/14/10.
- Manifest 0.3.52; no `main`, tag or release change.
- **Next:** the author plays gaps3. Codex's replay in `docs/GAPS2_PLAYTEST_REVIEW.md` covers TB release, an actual
  leap and Frigid Touch; the form replay is in `docs/SHIFTER_FORM_DECISIONS.md`.

**Earlier handover (16 November 2026, Codex): gaps2 review / Invocation evidence on `codex/gaps2-playtest-review`,
stacked on #71 `3664663` -> #70 -> #69 -> #68 -> #67 -> #66 -> #65, development `96dabaa` (#72).**
Read [review/judgment/replay](docs/GAPS2_PLAYTEST_REVIEW.md), [inputs](docs/competence/GAPS2_PLAYTEST_INPUTS.json),
[derived ledger](docs/competence/GAPS2_INVOCATION_LEDGER.json), §179. Supplied gaps2 MVID `9ce04a64` matches `96108ba`;
author confirms combat spawning, rests between fights, Grabber left to the engine. Ten encounters, all 572 command
issues have matching terminals, no recorded AI Control exception. **Five Release Grapple commands succeed in RT**,
with held restrictions and later recovered permissions; no refund of spent actions/cooldowns. TB release, actual
leap and ordinary/fused Frigid remain unplayed. Independent Stun Bomb state explains two release value changes.

Invocation: **32 completed casts**, ten distinct initial free placements. **27 current direct ability IDs,
4 explicitly holder-inferred tiers, 1 unidentified tier**. Classes: 13 first placements, 14 settled moves,
2 overrides, 2 holder-unseen, 1 unread; no identified under-settle or known same-stat error. One supposed coverage
shortcut would not clear the earlier holder loss/margin. No new cast cap, scoring/recipient/settling change or
joint-assignment model is warranted. See the review's exact rows and the limitations of logged issue timing.

The session-wide buffwhy/restraint keys omitted fight. Repeated early-round recipients lost their current ID/gross
appraisal, and the old ledger even reused an earlier 7.1 where the actual chosen value is 8.8. Both notes now use
a fight-scoped key and lazy formatting, preserving normal duplicate suppression. Ledger state is caster-scoped,
rejects stale appraisals and marks identity/value sources; an equal eventual coverage is not an earlier waste verdict.
No movement, targeting, forecasts, rule/path/world queries or actor/context cache changes. Warm ability work remains
material; Lann has no slow-decision row. No Unity speedup claim.

Checked source **`9a02565`**, MVID **`48ece05d-5895-46e3-896c-d87cb5a60b29`**: **866 unit + 27 inventory + 3 script + 18 ledger tests**,
real-reference Release **0 warnings/errors**; ProfileAudit 44,639 loaded / 4,807 abilities, no load/reader failures
or prerequisite skips. **DIAGNOSTIC-LOG 16 (2 new)** and inherited checks, supplementary Magus 44, providers 98/14/10
pass; **0 added external signatures**. Reports otherwise match #71; raw enum mismatch remains a separate expected
negative-input check. Workspace-full interrupted a provider build; duplicate generated outputs were removed and
remaining checks resumed on the exact unchanged checked source/DLL. No unresolved check failures.
[Receipt](docs/competence/GAPS2_PLAYTEST_CHECKS.json), [compiler/reference provenance](docs/competence/GAPS2_PLAYTEST_BUILD_PROVENANCE.json).
[Cumulative gaps3 UMM test ZIP](test-builds/AI-Control-FACC-v0.3.52-dev-gaps3-9a02565-UMM-test.zip), SHA-256
`e97222e507007a321f1b97334a1158a796c22fe90ba9a569809a805573b62ca6`. SDK 8.0.426 / Roslyn 4.11, UMM 0.33.2.0,
manifest 0.3.52. Diagnostic code is unplayed; the checked DLL stays frozen across receipt/docs-only commits.
No gameplay stack integration, main/development push, tag or release. Keep holder opportunity/safety, monster,
form-fact and saved-Frigid boundaries. No additional files needed. Next: repeated early-round buff recipients across
fights with current appraisals, TB release, actual leap and Frigid if feasible; paired logs and startup identity.

**Earlier handover (15 November 2026, Codex): gaps1 playtest review / Release Grapple repair on
`codex/gaps1-playtest-review`, stacked on #70 `d5a1207` -> #69 -> #68 -> #67 -> #66 -> #65,
development `6b9be42`; repaired gaps2 is unplayed.** Read [review/evidence/replay](docs/GAPS1_PLAYTEST_REVIEW.md),
[input hashes/context](docs/competence/GAPS1_PLAYTEST_INPUTS.json), §178. Supplied logs match gaps1 MVID `9aea0261`;
author confirms ToyBox spawns, rests between fights, Grabber left to the engine. All 572 issues have matching terminals.
AI rows 2812-2813 value releasing Hydra at 23.8, then discard Release Grapple: its null reason crashes candidate
pure-buff classification. The score now has a reason; the classifier guards null/empty. A bounded appraisal says
"release offer", not that a release already executed. Header reciprocity now uses native UnitReference ID equality
instead of another proxy resolution. The missing played hold conditions/headers still need a replay, not an invented
permission/movement rule. No new world/path/rule forecasts, actor mutation, movement or commitment policy.

Checked source **`96108ba`**, MVID **`9ce04a64-f9a0-43ce-8275-5f1b0c7bbe73`**: **861 unit + 27 inventory + 3 script + 11 ledger tests**,
real-reference Release **0 warnings/errors**; ProfileAudit 44,639 loaded / 4,807 abilities,
no load/reader failure or prerequisite skip. **GRAPPLE 58 (3 new)**, SHIFTER-FORMS 21, GRIFFON-MODES 29, selector 24
and inherited checks pass; supplementary Magus 44 and native providers 98/14/10 pass. **One added native equality
reference resolves**; reports otherwise match #70, raw enum mismatch remains a separate expected negative check.
[Receipt](docs/competence/GAPS1_PLAYTEST_CHECKS.json), [compiler/reference provenance](docs/competence/GAPS1_PLAYTEST_BUILD_PROVENANCE.json).
[Cumulative gaps2 UMM test ZIP](test-builds/AI-Control-FACC-v0.3.52-dev-gaps2-96108ba-UMM-test.zip), SHA-256
`bf5e955f32cd91512495656e30528cc1afad11733af108bc523012527cd681fa`. SDK 8.0.426 / Roslyn 4.11, UMM 0.33.2.0,
manifest 0.3.52; frozen checked DLL across receipt/docs-only commits. No merge, main/development push or release.

Played: engine Grabber on/off and actual grabs in TB/RT, native maintenance/free hit, three RT shapes and Fury.
Frigid now forecasts the one-round minimum; a Grabber-valued leap is offered. **No Frigid, leap or release is issued**;
no invocation/reveal acceptance. The suicidal-looking bomb price needs a controlled recipient/value check: native
projectiles launch before their ranged roll queues AoOs, not a proven pre-delivery loss. Nine slow rows coincide with
collections; warm ability/context workload remains material. No performance improvement is claimed by this repair.
Retain #69's saved-Frigid boundary (168 withheld), leap bounds, holder generic-cost/safety, monster and form limits.
No additional upload needed. Next: TB/RT replay with Grabber left to the engine; negative/positive holds, actual
release and recovered permissions/headers (respect spent actions/cooldowns), actual leap, ordinary/no-save Frigid;
paired logs and startup identity.

**Earlier handover (15 November 2026, Codex): cached leap Grabber modes on `codex/leap-grab-modes`, stacked on
#69 `2e86342` -> #68 -> #67 -> #66 -> #65, development `6b9be42`; unplayed.** Read [leap implementation/replay](docs/LEAP_GRAB_MODES.md),
[five-gap assessment](docs/GRAPPLE_GAP_ASSESSMENT.md), §177. Mode repricing previously ignored grapple truncation/hold
despite the initial leap quote including it. Both quote paths now share the ordinary line/summary kernel and cached
leap repricing reads the assumed grab only after exact routine/frame validation. Grabber joins the existing 32-set
chooser; native own-weapon/landing caps remain 4/8, with no new geometry or movement policy. Existing G2 ownership,
blocklist/once-per-round, safety, defence and final dispatch rules remain. Holder cost/safety and monster support
remain scoped in the assessment; their missing native/alternative views are not replaced with guesses.
Checked source **`351b709`**, MVID **`9aea0261-f5a3-4c3e-a025-502de27a716d`**: **855 unit + 27 inventory + 3 script
and 11 ledger tests**, real-reference Release **0 warnings/errors**; ProfileAudit 44,639 loaded / 4,807 abilities,
no load/reader failures or prerequisite skips; **GRIFFON-MODES 29**, VALUE-SELECTION 24 and inherited checks pass.
Supplementary Magus 44, native providers 98/14/10; **0 new references** relative to #69. Audit/report unchanged
outside the leap check count/optional coverage/output paths. Original raw enum failure remains separate.
[Receipt](docs/competence/LEAP_GRAB_CHECKS.json), [compiler/reference provenance](docs/competence/LEAP_GRAB_BUILD_PROVENANCE.json).
[Cumulative gaps1 UMM test ZIP](test-builds/AI-Control-FACC-v0.3.52-dev-gaps1-351b709-UMM-test.zip), SHA-256
`841c40650338f78d7c16999660e430a4e46d92011be9a447ee45b95d09359084`; includes #65-69 and leap modes.
SDK 8.0.426 / Roslyn 4.11, UMM 0.33.2.0, manifest 0.3.52. The checked DLL is frozen across receipt-only commits;
no live actor/flight, gameplay or Unity speedup acceptance. No merge, main/development push or release.
Retain the #69 Frigid minimum/saved-profile boundary and #68 form/Fury work. No new uploads are needed for these
slices. Next: TB/RT replay with Grabber left to the engine, actual leaps plus G2 release and Frigid checks.

**Earlier handover (15 November 2026, Codex): grapple-gap assessment and Frigid common-buff reader on
`codex/grapple-gap-review`, stacked on #68 `342dc27` -> #67 -> #66 -> #65, development `6b9be42`; unplayed.**
Read [assessment/proposal verification/replay](docs/GRAPPLE_GAP_ASSESSMENT.md), §176. Latest supplied logs hash-match
the old fusion2g1b pair for #67; no G2/#68 acceptance claimed. Native Frigid selector alternatives apply the same
buff for one minute or one round. The existing conditional walk was not the missing mechanism. Read only a verified
common fixed minimum; retain the longer/refresh risk for allies, native immunity/delivery and existing policies.
Do not guess critical rates or inject CC from a descriptor. Lazy per-profile metadata; empty ordinary safety fast path.
Checked source **`fad03c4`**, MVID **`b0da0523-bbc9-4044-8228-18ed4c118bfa`**: 855 unit + 27 inventory + 3 script
and 11 ledger tests; real-reference Release 0 warnings/errors; ProfileAudit 44,639 loaded / 4,807 abilities, no load/
reader failures or prerequisite skips; VALUE-SELECTION 24, inherited checks and native providers 98/14/10 pass.
Two added native flag references resolve. [Receipt](docs/competence/FRIGID_SELECTION_CHECKS.json),
[compiler/reference provenance](docs/competence/FRIGID_SELECTION_BUILD_PROVENANCE.json),
[input hashes](docs/competence/FRIGID_SELECTION_INPUTS.json). Ordinary empty-selector safety: 10,000 warm calls,
zero bytes, not Unity timing. Only the two Frigid ability verdicts gain Soft control. No intermediate ZIP; a
cumulative package follows with the separate leap-mode repair. Manifest 0.3.52; source-frozen DLL retained.
168 saved Frigid templates now expose the existing unsupported unconditional-control boundary and are withheld;
the played no-save Runes combination gains stagger. General per-outcome control remains a Fusion follow-up.
Leaps have a feasible separate cached-quote repair; holder cost/safety and monster help need the bounded native model
and next-play evidence set out in the assessment. No additional files needed for this slice. No movement or release.

**Earlier handover (15 November 2026, Codex): Shifter form verification on `codex/shifter-form-validation`, continuing
Claude's `730c6b6`, stacked on #67 `830d777` -> #66 -> #65; development base `6b9be42`. New behavior unplayed.**
Read [form design, corrections and replay](docs/SHIFTER_FORM_DECISIONS.md), §174-175. Real supplied game references
and exports are retained here; no re-upload is needed. SDK 8.0.426 / Roslyn 4.11. Checked source **`f00f4a6`**:
**855 unit + 27 inventory + 3 script + 11 ledger tests**, real-reference Release 0 warnings/errors;
ProfileAudit 44,639 loaded / 4,807 abilities without load/reader failures or prerequisite skips. SHIFTER-FORMS 21,
CAMPAIGN-FLAG 21, GRAPPLE 55, FUSION-VALUE 23; supplementary Magus 44; native harnesses 98/14/10. All other stdout/report lines
match a fresh #67 build on identical inputs (only new section/output paths normalized).

Retain Claude's Fury/form scheduling and next-round attack horizon. Correct the note ledger's mid-fight capacity
clear (preserve at most 64 identities/fight and four repeats), guard Fury holding with the main-action minimum,
and scope the Fury wait note by fight. Three extra scalar-ledger tests join Claude's 15. Actual playable Griffon
tiers are Standard; each also consumes Shifter's Rush, besides Polymorph descriptor removal. None of the 26
supplied aspect buffs is removed; numerical stacking remains native. Rush can make the shift free and SlotOf reads
the actual RuntimeActionType. No movement/commitment/retreat/path/stuck policy or new form-fact projection.

[Receipt](docs/competence/SHIFTER_FORMS_CHECKS.json), [compiler/reference provenance](docs/competence/SHIFTER_FORMS_BUILD_PROVENANCE.json),
[input hashes](docs/competence/SHIFTER_FORMS_INPUTS.json). SigDump: no new external signatures relative to #67.
The original raw Achaekek enum failure remains explicit. UMM reference is 0.33.2.0; the historical #67 package's
SDK 8.0.131 / UMM 0.33.0.0 inputs differ, and byte identity with it is not claimed.
[Shifter forms UMM test ZIP](test-builds/AI-Control-FACC-v0.3.52-dev-shifterforms-f00f4a6-UMM-test.zip), startup MVID
**`3a7db475-3b13-4362-a4dc-0fd20613e2c9`**, ZIP SHA-256
`9ac10ab46b0431ed4edd87a93bea3516a1754752c40a69e94df604a531eb28ea`.
The source-frozen checked DLL is retained across later receipt-only commits. Manifest stays 0.3.52;
no main/development push, tag, merge or release. No gameplay/Unity performance acceptance is claimed.
The author next plays the form replay with Grabber left to the engine, plus GRABBER_G2.md and fusion2 checks.

**Earlier handover (15 November 2026, Claude): Shifter form decisions refined on `claude/shifter-forms`, stacked on
PR #67 (no PR yet). Build, audit and package pending; not played.** Read [docs/SHIFTER_FORM_DECISIONS.md](docs/SHIFTER_FORM_DECISIONS.md)
and `docs/TACTICAL_AI.md` §174. The author asked for the form decisions (when to shift, which form) to be checked and
refined, with a general proposal as ideas.
- **Evidence (fusion2g1b, Wenduag).** No Griffon Shape in 9 fights. In TB fight 3 R1 a positive but losing form kept
  Shifter's Fury out of the candidates. In RT1 the best form (34.9) vanished after Griffon Aspect, and the once-a-fight
  row could not say why.
- **Changes (both modes).**
  1. Fury waits only while a new form is the turn's main action and beats the immediate action plus the Fury's round,
     by the buff margin (`HoldFuryForForm`).
  2. A Standard or full-round shift's attacks count from the next round (`ValueModel.FormAttackRounds`).
  3. The appraisal row is repeated on a material change, at most 4 times a fight (`ShifterRules.FormNoteChanged`).
- **Not changed:** the resource gate, the 3-round lock, the caster lockout, the equipment pricing, Master Shapeshifter
  permanence and pre-combat shifts. Pounce and other form facts are proposed, not done. The page gives the reasons.
- **Checks:** 852 unit tests (15 new). The container was replaced and the uploaded game assemblies and exports are gone,
  so the Release build, SigDump, ProfileAudit (new SHIFTER-FORMS, updated CAMPAIGN-FLAG) and native harnesses are
  pending. They need `Wrath_Data\Managed`, `UnityModManager.dll`, `Classes.zip` and `MythicAbilities.zip` again.
- **Next:** build and verify, package, open the PR. Then the author plays it (replay in the page).

**Earlier handover (15 November 2026, Claude): fusion2g1b played and reviewed; maneuver immunity and G2 (the engine
decides Grabber and Release Grapple, at the author's request) on `claude/maneuver-immunity`, PR #67 into development,
stacked on PR #66 and #65 (review #65, then #66). Not played.** Read [docs/GRABBER_G2.md](docs/GRABBER_G2.md),
[docs/FUSION2G1B_PLAYTEST_REVIEW.md](docs/FUSION2G1B_PLAYTEST_REVIEW.md), `docs/TACTICAL_AI.md` §172-173 and the
[receipt](docs/competence/G2_GRABBER_CHECKS.json).
- **Log.** fusion2g1b module `e4f231af`: 9 fights, 646/646 command terminals, no AI Control exception. Six of the seven
  Runes fusions now reach `[FusionValue]`.
- **Defect.** G1 valued holds on a grapple-immune Vescavor Swarm and walked Wenduag back into its gibber area. The fix,
  `CombatMath.ImmuneToManeuver`, reads `ManeuverImmunity` (swarms, Freedom of Movement) as well as the condition, in
  the grapple opening, `ManeuverOdds` and the trip forecast.
- **G2.** The six Grabber toggles switch at once for free.
  - The engine prices each weapon attack with the grab assumed on and off (`ModeKind.Grab`, `CombatMath.AssumeGrab`)
    and switches it through `ApplyModes`.
  - Release Grapple is scored from the live hold (`ReadHold`, `GrappleKeepValue`) and used when keeping is clearly
    negative.
  - The grab's hold counts the release option. Tiger and Smilodon grab with claw and bite.
- **Left open.** `ContextActionSelectByValue` (Frigid Touch's stagger, 3 blueprints in the exports) stays unread.
- **Checks** on `33fa9b3`: Release 0 warnings, 5 added references; 837 + 27 + 3 + 11 tests; GRAPPLE 55 (23 new). The
  coverage moves only the 6 Grabbers and 6 releases; native harnesses 98/14/10.
- **Package:** `test-builds/AI-Control-FACC-v0.3.52-dev-g2-33fa9b3-UMM-test.zip`, MVID
  `4b94e879-4a5c-4ec3-9b57-8b45d4eaabcc`. Manifest 0.3.52; no `main`, tag or release change.
- **Next:** the author plays g2 with Grabber left to the engine (GRABBER_G2.md replay), a swarm in melee, and the
  fusion2 and G1 checks.

**Earlier handover (15 November 2026, Claude): fusion2g1 played and reviewed; Fusion caster-fact guard fix on
`claude/fusion-caster-fact`, PR #66 into development, stacked on PR #65 (review #65 first). Played as fusion2g1b (above).** Read
[docs/FUSION2G1_PLAYTEST_REVIEW.md](docs/FUSION2G1_PLAYTEST_REVIEW.md), `docs/TACTICAL_AI.md` §171 and the
[receipt](docs/competence/FUSION2G1_REVIEW_CHECKS.json).
- Log: fusion2g1 module `e4cb4df9`, 10 fights, 370/370 command terminals, no AI Control exception. The author
  interrupted often in real time.
- G1 works as intended:
  - holds valued positive against casters and negative (at most +0.9) against summoned wolves and dogs;
  - only swift, free or move actions while holding, and no move;
  - 33 grapple maneuvers by Wenduag, 11 failed escapes.
- Defect: all seven Runes fusions fell back ("mutable/unread guard ContextConditionCasterHasFact"). Mystical Mayhem's
  Cyphermage patch adds a caster-fact gated Rune Master damage to both Runes; the exports lack it.
- Fix: `StableFusionGuards` accepts `ContextConditionCasterHasFact`. Caster-side payloads are already refused, and the
  ordinal check still refuses a fact an earlier application gives.
- Checks on `981299e`: Release 0 warnings, 832 + 27 + 3 + 11 tests, FUSION-VALUE 23 (8 new), other report lines
  unchanged, native harnesses 98/14/10. The stock qualified count stays 3,230.
- Package: `test-builds/AI-Control-FACC-v0.3.52-dev-fusion2g1b-981299e-UMM-test.zip`, MVID
  `e4f231af-1782-4e64-9697-3a3b2455e1a2`. Manifest 0.3.52; no `main`, tag or release change.
- **Next:** the author plays fusion2g1b. Runes fusions should log `[FusionValue]`; repeat the G1 and fusion2 checks.

**Earlier handover (15 November 2026, Claude): PR #64 (Fusion F2/F3 core) reviewed and merged into development (`6b9be42`);
G1 grapple state awareness on `claude/grapple-awareness`, PR #65 into development. Played as fusion2g1 (above).** Read `docs/TACTICAL_AI.md`
§170, [docs/GRAPPLE_G1.md](docs/GRAPPLE_G1.md) and the [receipt](docs/competence/G1_GRAPPLE_CHECKS.json).
- PR #64: no material defect. Rerun on its head: 821 unit tests, ProfileAudit exit 0, native harnesses 98/14/10.
  Its package DLL decompiles to the same code as a local build apart from compiler lowering.
- G1 evidence (fusion1 logs): with Grabber on, every full attack that grappled ended after one claw. The native
  `CantUseStandardActions` stops the routine, and the engine priced all of it.
- G1 change (both modes, shared weapon value): it reads the native grab trigger and cuts the forecast routine at the
  expected grapple. It values the hold: the victim held then pinned, against the holder's lost turns, plus the free
  natural attack each maintained round. The header names the pair, and one `[Grapple]` row is logged per fight and pair.
- Not done: Release Grapple (no played case; proposed G2), and spells cast by a party member a monster holds.
- Checks: Release 0 warnings; 832 unit + 27 + 3 + 11 tests; ProfileAudit GRAPPLE 32 checks, every other report line
  unchanged; native harnesses 98/14/10. Manifest 0.3.52; no `main`, tag or release change.
- Package (fusion2 + G1): `test-builds/AI-Control-FACC-v0.3.52-dev-fusion2g1-883bc9d-UMM-test.zip`, startup MVID `e4cb4df9-ad46-409a-a800-b96382c68a10`,
  ZIP SHA-256 `9130236430df…`. It is the checked DLL `4f46132d…` unchanged.
- **Next:** the author plays the fusion2g1 package. Test a grab-capable form against a dangerous and a weak target in TB
  and RT (replay in GRAPPLE_G1.md), with the fusion2 checks (`docs/FUSION2_PLAYTEST_REVIEW.md`).

**Fusion2 playtest review / qualified F2–F3 core (14 November 2026, Codex; new valuation unplayed):**
`codex/fusion1-playtest-review` starts at merged development `ab1b6a7`, no open PRs. The two supplied AI logs identify
fusion1 MVID `2e2896c3`; eight encounters, 258 TB / 1,475 RT measured decisions. ToyBox spawns, rests between fights,
manual Grabber with AI Control on confirmed. [Review / native contracts / next replay](docs/FUSION2_PLAYTEST_REVIEW.md),
[input hashes](docs/competence/FUSION2_PLAYTEST_INPUTS.json), §169. No observed AI Control exception; 14 OwnerBlueprint
warnings retain WrathPatches provenance. The lone missing terminal is the final OOC buff command at the file cutoff.

Fix qualified Fusion control to use each application/condition/duration and fresh component, with a 32-interval
overlap envelope. Burning's inspected damage/remove-self carrier gets no fake control or inherited duration; its
periodic HP benefit remains unpriced and ally safety is retained. Preserve shared-save outcomes through nonlinear
HP/kill utility and residual surviving control, without another damage estimate or save. Saved-result configurations
without a declared root save are withheld, matching native early return; independent saves keep their F0 refusal.
Metadata and GUID dependency checks are cached;
scalar duration memo uses the existing synchronous scope/version; no new world/path scan or persistent actor/context
cache. First-only qualified/fallback witnesses and school/shape metadata aid the next replay. Unknown/composite,
state-dependent, support, refresh/stacking and sleep/damage cases retain the stated legacy/refusal boundaries.

Checked source `6638404`: **821 unit + 27 inventory + 3 script + 11 ledger tests**, real-reference Release
0 warnings/errors; compatible ProfileAudit 44,639 loaded / 4,807 abilities, no load/reader failures or prerequisite
skips; FUSION 38, CONE 34, VALUE 15; supplemental Magus 44; native providers 98/14/10. Four new GUID references
resolve. The raw archive retains its documented Achaekek enum failure. [Receipt](docs/competence/FUSION2_CHECKS.json),
[compiler/reference provenance](docs/competence/FUSION2_BUILD_PROVENANCE.json). The matrix still admits 7,474/15,494
templates, 1,180 cone components resolve; 3,230 pass the new metadata gate, not a live-recipient guarantee.

[Fusion2 UMM test ZIP](test-builds/AI-Control-FACC-v0.3.52-dev-fusion2-6638404-UMM-test.zip), startup MVID
`e6257b91-248c-47bd-b717-dc588dee5987`, SHA-256 `8248fbc28f9e1189f0ad98bcb3445965429c3247c1781839b27766804e7e4aad`.
SDK 8.0.426 / Roslyn 4.11, UMM reference 0.33.2.0; Source Link names the checked commit. The frozen DLL is not
rebuilt after receipt-only commits. Scalar scratch microchecks allocate zero bytes; Unity speed/gameplay are unplayed.
Other profile admission, policies/slots, ordinary spells and movement stay on the existing paths.
This is a development test, manifest 0.3.52; no stable release, main push or merge.
G1 comparative grapple release/initiation remains separate: automatic native maintenance is observed, and cannot
be judged as a broken idle turn. Read the review before proposing a timeout or counterfactual release mutation.

**Fusion compiler provenance follow-up (14 November 2026, Codex; documentation only):** based on merged development
`9c03c3d`. Claude correctly identified the missing compiler field in F0/F1 receipts. The preserved environment uses
.NET SDK **8.0.426**, MSBuild **17.11.69**, Roslyn **4.11.0-3.25569.22 (3fb752d4)**. Fresh real-reference Release
replays reproduce both original DLLs byte-for-byte when Source Link names their checked source revisions;
both builds have 0 warnings/errors. [Supplement and hashes](docs/competence/FUSION_BUILD_PROVENANCE.json), §168.
This is retrospective evidence, not an amendment to the historical receipts or Claude's integration findings.
The existing fusion1 ZIP/identity/checksum, manifest, runtime code and SDK selection are unchanged. Build receipt
guidance now records compiler/SDK, references, source/debug metadata and effective options. See CONTRIBUTING.md.

**Earlier handover (14 November 2026, Claude): PRs #60 (Fusion F0), #62 (Fusion F1) and #61 (I1) reviewed and merged into development;
PR #59 (S2) reviewed after its merge.** Read `docs/TACTICAL_AI.md` §167 and the [receipt](docs/competence/INTEGRATION_60_62_CHECKS.json).
No material defect in any of them; manifest 0.3.52, `main`, tags and releases unchanged.
- #59: development `e1ae334` carries exactly the checked source `49ffd46`; real time unchanged; walk checks match the native contracts.
- #60: admission unchanged (last reason kept, list added per cached profile); S1's SUMMON-KITS detail stays byte-identical on S1's inputs;
  Fusion coverage reproduces (60 sources, 15,494 configurations, 7,474 admitted, 0 reader errors).
- #62: ordinary cones' delivery unchanged natively (`GetDeliverBlueprint`); the friendly-fire veto opens the existing six-angle re-aim, also for
  ordinary cones with Avoid Friendly Fire on; all 1,180 admitted cones resolve.
- Package: `test-builds/AI-Control-FACC-v0.3.52-dev-fusion1-7a56bbb-UMM-test.zip` (MVID `2e2896c3-5f13-42e1-bb9b-9626f6157228`) is a build of
  `7a56bbb`, which is development's exact code. A local SDK 8.0.131 rebuild differs in its bytes, and decompiles to the same code apart from
  compiler lowering. Its inputs also differ: UnityModManager 0.33.0.0 here, 0.33.2.0 in the package, and a different build path.
  PR #63 (§168) adds the missing toolchain, SDK 8.0.426 / Roslyn 4.11, with Codex's local byte-for-byte replays.

Integrated checks: Release 0 warnings/errors, 0 added references; 811 unit + 27 inventory + 3 script + 11 ledger tests; ProfileAudit exit 0, two runs
identical (FUSION 38, FUSION-CONE 34, APPROACH-SPOT 28, SUMMON-KITS 8); native harnesses 98/14/10. Limits: S2, F0 and F1 are unplayed; the raw corpus
keeps its Achaekek enum failure. **Next:** the author plays the fusion1 package in TB and RT: crowded melee approaches (§163 replay), cone fusions
(`docs/FUSION_IMPLEMENTATION.md` replay) and a grapple with AI Control on for the grappler. Then Fusion F2 (plan §7) and G1 (grapple awareness).

**Earlier handover (14 November 2026, Claude): Invocation transfer ledger (I1) on `claude/invocation-ledger`, PR into development. Tooling and
documentation only: no gameplay source, manifest or package change; v0.3.52 stays the validated, unplayed stable package.** Read
[docs/INVOCATION_LEDGER.md](docs/INVOCATION_LEDGER.md) and `docs/TACTICAL_AI.md` §164. `tools/InvocationLedger/invocation_ledger.py` (11 tests) lists
every Profane Invocation in a `Log.txt` with recipient, holder, the engine's values, outcome and class.
- Inputs: v0.3.51 `Log.txt` (`e661b9d8…`) and the author's new v0.3.52 `Log.txt` (`4a89f317…`, header MVID `981a7b77`, two fights) with its
  `combatLog.txt` (`90e946ad…`).
- Result: 29 issued, 28 acted. First placement 4, settled moves 19, overrides 2 (net 9.6 and 7.2), holder unseen 3 (holders down), 1 interrupted.
  Moves under settle 0, same-stat stacking 0, redundant casts 0.
- The named pair (v0.3.51 2032 -> 2067) is not redundant: tier-2 Dexterity straight onto Tuvel would have removed his tier-2 Charisma (9.4).
- Decision: I2 (joint assignment) is not warranted on this evidence; no runtime change. Low priority: a display-name fallback for an empty unit name.
- Grapple evidence for G1: the player switched Grabber on for Ulbrig, and the combat log shows six grapples (five succeeded). The turn-based grapple turns
  had AI Control off; in real time Ulbrig made no AI Control decision from RT3 to RT15. G1 needs a grapple turn with AI Control on.

Checks: 11 ledger + 27 inventory + 3 script tests; links resolve; tables regenerated from the deterministic tool output. No game build or ProfileAudit
(no C#, project or manifest change). [Receipt](docs/competence/INVOCATION_LEDGER_CHECKS.json). Next proposed: G1 grapple state awareness.

**Magic Fusion F1 (14 November 2026, Codex; unplayed):** `codex/fusion-cone-delivery` stacks on F0 / PR #60,
including integrated development `e1ae334` (S1/S2). Review F0 first. [Implementation / replay](docs/FUSION_IMPLEMENTATION.md#f1-selected-cone-delivery-and-friendly-fire-re-aim), §166.
Resolve the selected native delivery component in the existing cone adapter; retain the actual slot/context, native
range/recipient/Selective/obstacle queries and fail-closed behavior. Per-score hard-friendly-fire veto now admits
the existing six-angle re-aim even without a formatted penalty note; ordinary cones receive that shared fix too.
No movement, new search scope, scoring weights, resource policy or payload exclusion changes. Source edits are
read afresh; cache contains components, not actors/contexts. F2/F3 judgment and complex support remain outstanding.
Real slot/source fixtures, full admitted-cone matrix and warm resolver allocation check are added; native gameplay
and matched Unity timing remain pending. 811 unit + 27 inventory + 3 script tests pass; real-reference Release
0 warnings/errors; compatible/supplementary ProfileAudit and native providers 98/14/10 pass. FUSION-CONE 34,
all 1,180 admitted cone components resolve; 10,000 warm resolver calls allocate 0 bytes (not Unity timing).
[Checks](docs/competence/FUSION_F1_CHECKS.json), [fusion1 ZIP](test-builds/AI-Control-FACC-v0.3.52-dev-fusion1-7a56bbb-UMM-test.zip),
[identity](test-builds/AI-Control-FACC-v0.3.52-dev-fusion1-7a56bbb-UMM-test.identity.json); checked source `7a56bbb`, MVID `2e2896c3-5f13-42e1-bb9b-9626f6157228`;
0 new foreign signatures. Manifest 0.3.52 and stable release unchanged. Claude reviews/integrates; no gameplay acceptance.

**Magic Fusion F0 (14 November 2026, Codex):** author authorized the [revised implementation plan](docs/MAGIC_FUSION_PLAN.md).
`codex/fusion-coverage-audit` started at `6cf4281`, no open PRs; reconciled with actual development `e1ae334` (S1/S2), preserving both. Initial tracking ref was stale; use an explicit development fetch refspec. [Status/usage](docs/FUSION_IMPLEMENTATION.md), §165,
[baseline](docs/competence/FUSION_COVERAGE_BASELINE.json), [checks](docs/competence/FUSION_F0_CHECKS.json).
Offline native UI template enumeration: 60 sources, 3,540 pairs, 15,494 configurations; 7,474 profile-admitted,
8,020 withheld, zero reader errors. All known blockers/locations now survive cold profile construction; existing
last-reason admission, decisions and normal log stay unchanged. This diagnostic metadata changes the DLL, unlike
the initial tooling-only sketch; no additional rule/target/path queries, actor cache, settings/version or package change.
The 1,180 admitted cone templates still require F1's selected-delivery handoff repair; aim/value/actor availability
remain unassessed. Keep F2/F3 shared math, real root-save correlation, one native clicked target and actual costs;
do not add a second target/action or blanket blueprint/key replacements. 811 unit + 27 inventory + 3 script tests pass; real-reference Release 0 warnings/errors; compatible/supplementary audits and native providers pass. Checked source `c000c83`, MVID `cd740a06-f1f3-4f50-9874-05d16b6da11c`, zero new foreign signatures. S1's input subset retains its explicit missing-reference report; it is not S1's full stock-supplement scope. Claude reviews/integrates; no gameplay claim.

**Earlier handover (14 November 2026, Claude): approach end points (S2), merged into development by PR #59 (merge `e1ae334`; S1 by PR #58, merge
`8ef707c`). Source change, not played; manifest 0.3.52, packages and `release/` unchanged; v0.3.52 stays the validated, unplayed stable package.** Read
`docs/TACTICAL_AI.md` §163 and the [receipt](docs/competence/APPROACH_SPOT_CHECKS.json).
- Evidence (v0.3.51 `Log.txt`): 52 "path ends out of reach" and 35 "without line of sight" refusals, all in the two turn-based fights (47 + 26 in
  fight 6 next to 61 issued commands); 40 of the 52 belong to party members and pets, 12 to summons.
- The proposed cause (a summon-specific reach or body mismatch) is disproved: the radius is the game's own and everyone is refused alike.
- Cause: the game's turn-based path search ends on its end node, the free cell nearest the target. With the near side crowded that cell is out
  of reach and no other side is tried. Turn-based had no free-side logic (the v0.3.29 spot rules were real-time only).
- Change: `SpotRules.Applies` in both modes. Turn-based, a taken straight spot (a body on it, or an ally on its cell) sends the unit to a free
  grid cell in reach and sight of the target first (`TacticalEngine.CellSpot`, `Candidate.FreeSideWalk`, arrival 0.15 m); the action follows on
  the next decision. The dispatcher holds that walk to one move action and to its cell (`oneMoveWalk`). No free cell: the straight approach stays
  on offer, marked "(crowded, no free side found)". Real time is unchanged.
- Label fix: the refusal text follows the game's test (`CommandDispatcher.PathEndVerdict`, `SpotRules.JudgePathEnd`: mechanics distance, then
  sight). A verbose-only `[Dispatch] ... path end refused` line gives the end point, distance, radius, height and trimmed points.
- Bounds: only when the straight spot is taken; 16 cell lookups and reach tests, four priced spots, no added path query, cache or game reference.
- Limits: `FullAttack+5ft` still refused when crowded (3 of 52 rows); one ring of 16 samples; the 35 sight refusals are not addressed; the share
  of refusals this removes is unknown until a replay.

Checks: Release 0 warnings; 803 unit + 27 inventory + 3 script tests; ProfileAudit exit 0 with new `APPROACH-SPOT` 28 checks, every other report
line identical to the S1 run; native harnesses 98/14/10; 0 added game member references. Checked source `49ffd46`; verification build only (DLL SHA-256
`295591c8...`, MVID `f1f9895b-f92d-4287-bf92-0e47282d4ed2`), no package. Replay steps: §163. Next proposed: I1 (Invocation ledger from the supplied `Log.txt`).

**Earlier handover (13 November 2026, Claude): summon-kit inventory (S1) on `claude/summon-kit-inventory`, merged by PR #58. Tooling and
documentation only: no gameplay source, manifest or package change; v0.3.52 stays the validated, unplayed stable package.** Read
[docs/SUMMON_KIT_INVENTORY.md](docs/SUMMON_KIT_INVENTORY.md) and `docs/TACTICAL_AI.md` §162. ProfileAudit has a new section `SUMMON-KITS`
(`tools/ProfileAudit/SummonKitCheck.cs`): spawn sources, the unique creatures they summon, each creature's static kit and every active ability
through the summon policy and the mod's readers. A reading there is reader recognition, **not** tactical competence. Results on the author's exports
plus 607 missing-only stock fixtures and the Demonology creature list:
- 323 sources, 459 spawn references, 253 unique creatures (233 from loaded sources, 20 only from Demonology), 165 party-reachable non-swarm.
- Of those 165: all carry weapons, 157 melee, 27 with no active ability; 672 creature-ability pairs, 502 read; 37 pairs the creature's own brain
  casts are not fully read.
- Missing: six creature ids and three kit references are absent from the stock data itself. 107 creatures leave a part to the running game.
- The played summons (Demonology "Demonic Art") spawn stock creatures: their kits are read; the mod's own ability graphs and ritual buff are not.
- Ranked gaps: closing to melee (shared approach refusals), teleports and leaps the readers do not understand, conditional self-effects, on-hit
  riders (not classified here). No summonable stock creature grapples.

Checks: `SUMMON-KITS` 8; other 38 audit sections unchanged (959 checks); 787 unit + 27 inventory + 3 script tests; Release 0 warnings; native
harnesses 98/14/10; [receipt](docs/competence/SUMMON_KITS_CHECKS.json). The supplied `Log.txt` matches the v0.3.51 hash; the supplied
`combatLog.txt` is the v0.3.50 session's file. Next proposed: S2 approach end points, then I1 (Invocation ledger; the redundant-cast sequence stays a
hypothesis until the scores, stacking rules and state are checked).

**Current v0.3.52 handover (13 November 2026, Codex; new behavior unplayed):** `codex/v0351-live-review` starts at development `00ab4e52` after merged PR #55; no open PR at start. Latest logs exactly match stable v0.3.51 MVID `e31e626d`; author confirms swarm spawns during encounters/rests between encounters, requests a logging audit and explicitly selects next stable **0.3.52**. Read [review/replay](docs/V0352_PLAYTEST_REVIEW.md), [inputs](docs/V0352_PLAYTEST_INPUTS.json), §161, [updated master prompt](docs/CLAUDE_MASTER_PROMPT_V0351.md) and [short GitHub kickoff](docs/CLAUDE_KICKOFF_PROMPT.md). Six fights, 205 measured TB / 746 RT decisions, 343/343 commands have terminal rows, no observed AI Control exception. Leap candidates are admitted (467/1501/2358) but no leap issued; 2359 exposes missing mode optimization against a walking attack. Strict leap now uses existing chooser from exact cached native quote, with landing/combined charge-mode AC and existing ownership/blocklist/availability/hysteresis/dispatch rules. No extra own-weapon quote beyond existing four; 8 endpoints, safety/native costs/Pounce remain; no positive charge-AB/proc credit. Shared mode/charge-AC defense excludes recognized automatic swarm sources before nominal weapon quotes, retaining total danger and ordinary attack mitigation. Logging uses three bounded diagnostic-only change memos (128 IDs each/fight, no actors/calculation caching), guards six verbose math sites and discarded quiet RT summary formatting. Commands/errors/MVID/selected actions/native failure evidence remain; idle witnesses use existing state, no extra path query/policy change. Invocation18/17 successes still needs marginal review, no cap/settling retune. Four no-effect Dazzling Display casts need native selector/skill/immunity/event evidence; existing fear/buff filters are present. Eleven external WrathPatches ownership warnings retain foreign provenance. Six of 34 slow rows carry cleanup markers; warm scoring remains costly, heap deltas are not allocations or a matched speedup. Summons/grapple/idle gaps remain ranked; mode6 control total is printed twice, count it once. Stable packaging/development integration remain author-authorized; public main/tag/GitHub Release publication is separate. Checked source `30cfa2c`, startup MVID `981a7b77-a654-4da1-9a22-68bc1c6a77bd`; [receipt](docs/competence/V0352_PLAYTEST_CHECKS.json) / [v0.3.52 stable identity](release/CompanionAI_Pathfinder_v0.3.52.identity.json). 787 unit + 27 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits with explicit stock charge fixtures pass; original raw enum failure remains distinct. Stable package requested by author; corrected gameplay remains unplayed.

**Current v0.3.51 handover (13 November 2026, Codex; new behavior unplayed):** `codex/v0351-playtest-review` starts at merged development `c92b607` after PR #54, with no open PR at start. The author confirms these two logs are **stable v0.3.50**, MVID `45ab748a`, with **rests between encounters**, and requests **next build 0.3.51**; necessary movement improvements, stable packaging and development PR integration are authorized. Read [review/replay](docs/V0351_PLAYTEST_REVIEW.md), [inputs](docs/V0351_PLAYTEST_INPUTS.json), [native evidence](docs/competence/V0351_NATIVE_REVIEW.json), §160, and the requested [ranked Claude master prompt](docs/CLAUDE_MASTER_PROMPT_V0351.md). Four fights, 203 measured TB / 839 RT decisions, 43 slow rows, no observed AI exception. Invocation 28 issues / 27 acted successes; independent tiers, sibling/remote-holder losses and settling overrides explain some bursts but do not establish every transfer is useful. No blanket cap or valuation retune. No leap issued: endpoint/eligibility refusal needs gameplay. Fix queued native Pounce forecasting to use its ignored-cooldown/TB surprise/restricted-movement/RT recent-move branches instead of ordinary full-round availability; pricing and final issue share the adapter. Ability action cost, 8 endpoints / 4 weapon quotes, landing body/area/swarm/reach guards and native dispatch remain. Lazy refusal/admission witnesses use already-read results and the bounded shared memo, without new geometry/attack queries. Do not claim native touch spacing always overlaps or that every interrupted form failed. Idle approach and zero-value nauseated swarm cases remain evidence requests, as do broad summons/grapple competence and dense-kit profiling. Stable ZIP preparation does not establish new gameplay acceptance or authorize public main/tag/GitHub Release publication. Checked source `882c082`, startup MVID `e31e626d-9c4b-4536-8b26-63433d3c7425`; [receipt](docs/competence/V0351_PLAYTEST_CHECKS.json) / [v0.3.51 stable identity](release/CompanionAI_Pathfinder_v0.3.51.identity.json). 772 unit + 27 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 1/1 added signatures resolve. Compatible/supplementary audits with explicit stock charge fixtures pass; original raw enum failure remains distinct. Stable package requested by author; corrected gameplay remains unplayed.

**Current stable-package handover (13 November 2026, Codex; corrected gameplay unplayed):** `codex/review1-swarm-playtest` reviews all five review1-MVID logs and combat log; [evidence/replay](docs/REVIEW1_SWARM_PLAYTEST.md), [inputs](docs/REVIEW1_SWARM_INPUTS.json), §159. The author requested stable v0.3.50 packaging and authorized merging open PRs; PR #53 is merged at development `1946786`. This follow-up includes it. Twelve fights, 248 TB / 1,507 RT measured decisions; ToyBox rests between encounters. Invocation59issues/58acted successes;21in one mixed fight: settling works but large-gain overrides allow transfers; do not call all casts waste. Correct raw native AC thresholds and held-removal urgency from the protected baseline, retaining source/stat-link approximations. Swarm exits add only strictly known direct-HP reserve value at the existing0.3rate, with poison/stat/drain/attached/unknown excluded and all path/opportunity/risk gates retained. Add a strict native Death from Above route:8endpoint attempts/4weapon quotes, native eligibility/landing, safe body/hazard/reach/final-action checks, no ordinary walking fallback, queued-attack lifecycle; no prospective charge-AB/proc credit. Grabber remains unsupported: its self action/movement/AoO costs and maintenance/release need paired modelling and gameplay evidence. Two absent charge graphs use explicitly labelled BubblePrints2.7.0x stock fixtures;3duplicates retain unchanged user copies ([provenance](docs/competence/REVIEW2_CHARGE_INPUTS.json)). Mandatory native check requires those graphs. No new broad cache/kit cap or speedup claim. Stable ZIP packaging is author-requested, not proof that new gameplay was played; public main/tag/release publication is separate from development integration. Checked source `8efbb0e`, startup MVID `45ab748a-b0e9-4db9-b62a-3de2f4eff6fb`; [receipt](docs/competence/REVIEW1_SWARM_CHECKS.json) / [v0.3.50 stable identity](release/CompanionAI_Pathfinder_v0.3.50.identity.json). 760 unit + 27 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 16/16 added signatures resolve. Compatible/supplementary audits with explicit stock charge fixtures pass; original raw enum failure remains distinct. Stable package requested by author; corrected gameplay remains unplayed.

**Current playtest handover (13 November 2026, Codex; fixes unplayed):** `codex/v0350-playtest-review` starts at merged development `d466f384`; [broad review/replay](docs/V0350_PLAYTEST_REVIEW.md), [inputs](docs/V0350_PLAYTEST_INPUTS.json), §158. Integration1 is now played: exact MVID confirmed, 22 fights, 666 TB / 3,378 RT measured decisions; ToyBox spawns/rests confirmed, timing unknown. Profane: 59 issues, 57 acted successes, two unacted interrupts; both independent six-stat tiers matter. Fix actual-source unique sibling settling, narrow cached-native held weapon-loss appraisal near caps, and restore incoming explanations/identify variant keys. Exclude recognized automatic swarm harm from AC/concealment/images, retaining total HP/DR/energy/save danger and ordinary-enemy approximation. Replace quadratic conversion lookup with per-Gather first-index dictionary preserving identities/order/native replacement; name discovery and pre-score timing on slow rows. No new actor/rule preview, global kit cap or movement-policy retune. Ordinary/stale/complex/mixed-weapon appraisal limits remain explicit. Faerie Fire, Focused Shot, swarm exits, Griffon transformations and summon advance/attacks are exercised; this is not full numerical/pack performance acceptance. Manifest remains 0.3.50, no stable release. Checked source `ae92697`, startup MVID `25818a75-297f-47de-aea3-5cf93d0a9d09`; [receipt](docs/competence/V0350_PLAYTEST_CHECKS.json) / [review1 identity](test-builds/AI-Control-FACC-v0.3.50-dev-review1-ae92697-UMM-test.identity.json). 730 unit + 27 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct.

**Current integration handover (13 November 2026, Codex; v0.3.50, unplayed):** The author explicitly authorized review/merge of all open PRs #46–#51 and requested v0.3.50. [Review/findings/replay](docs/V0350_INTEGRATION_REVIEW.md), §157. This supersedes the older handovers' build-selection and integration status. All six original heads are preserved. The combined build includes optional/default-off summon control, paired form references, bounded recovery identity, Focused Shot and joint known activation costs. Review fixes: fresh summon authority/facts at final issue, no lifetime swarm classification, unique adoption counts across re-adoption, cheap excluded-actor gate exits; all audit/test registrations preserved. Summon implementation section is 156, keeping 152–155 intact. Checked source `ebfac70`, startup MVID `0b54fad9-fc28-483c-a87a-3f1e9f0efd62`; [receipt](docs/competence/V0350_INTEGRATION_CHECKS.json) / [integration1 identity](test-builds/AI-Control-FACC-v0.3.50-dev-integration1-ebfac70-UMM-test.identity.json). 705 unit + 27 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 27/27 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Native turns, casts and matched Unity performance remain pending. This task authorizes development integration and manifest 0.3.50, not a stable main/tag/release.

**Earlier handover (13 November 2026, Claude): summon control is implemented on `claude/summon-control` (PR into development), see
`docs/TACTICAL_AI.md` §156. Not played.** Opt-in, off by default ("Control Summoned Creatures (experimental)", `ModSettings.ControlSummons`). In combat,
both modes, AI Control drives linked summons of the characters it drives - not pets, swarms, nested or enemy summons, and not with the old real-time
engine. Rules: `Tactics/SummonRules.cs` (pure); game side `GameInterface/SummonControl.cs` (weak per-creature state, a temporary settings view that is
never stored, the summoner's blocklist); turn-based entry `GameInterface/SummonTurnBridge.cs` (the game's own AI turn: `TurnController.Prepare` postfix,
the `TickBrain` prefix and the `Tick` postfix forward to the existing `TurnBasedController.ProcessTurn`; no status, controllability, faction or group
write). The game ends an AI unit's turn itself (0.2 s idle, no time left, 9 s command cap); `TurnBasedPatches.CompleteTurn` is the shared, bounded
backstop. One behaviour change: a summon another mod makes clickable (ToyBox) is no longer driven like a companion unless the switch is on. Checks on
source `bfaccc6`:
- Release build: 0 warnings. Unit tests: 677 pass. Inventory tests: 27 pass.
- ProfileAudit: 35,497 loaded, the one known Achaekek failure, every check passes, new SUMMON-CONTROL 62 checks.
- Native harnesses: ToggleCosts 98, TomeBurn 14, ToyBoxSpend 10 checks pass.

Play `test-builds/AI-Control-FACC-v0.3.30-dev-summon1-bfaccc6-UMM-test.zip` (MVID `c72d459e-7d4f-417a-b030-6d90db7ba765`) with the checkbox on; §156
lists the tests, turn completion and pack performance first. `main` and releases are unchanged; manifest 0.3.30.

**Summon control planning handoff (12 November 2026, Codex; no implementation):** [Implementation plan](docs/SUMMON_CONTROL_PLAN.md), [native/blueprint/input evidence](docs/competence/SUMMON_CONTROL_EVIDENCE.json), §151. Checked integrated development `44b224f`, manifest 0.3.30. Claude implements; Codex remains involved in review/interim fixes. The supplied guide's “no TickBrain in TB” premise is false: native Prepare/Acting force the brain for the current non-controllable rider. Plan uses a narrow Prepare postfix + existing brain/driver entries and shared bounded completion, with no direct-control getter or Status patch. Explicitly verify phase/termination in game before enabling TB. Direct linked owned non-swarm summons only, default off; weak temporary actor policy preserves owner safety/resource settings without inheriting Pure Mage/range/role or creating saved IDs. Owner blocklists, OOC/pre-buff/UI exclusions, recognized summon-cast/item guards, mode/legacy/coexistence and transient RT pacing lifetime are part of the slice. Recent party timings refute an assumed cheap summon budget; cohort/frame measurements and matched replay are required. Code, hooks, settings and Info.json are unchanged by this planning task. The plan has not been executed or played. Baseline checks: 664 unit + 27 inventory, Release 0 warnings/errors, compatible ProfileAudit and toggle-native 98 pass; [receipt](docs/competence/SUMMON_CONTROL_PLAN_CHECKS.json). These do not validate the proposed feature.

**Joint spell/boost costs (13 November 2026, Codex; unplayed):** `codex/joint-spell-costs` starts at PR #50 `4745033` over refreshed development `44b224f`; includes review7/Focused Shot, not concurrent summon PR #49. [Native design/replay](docs/JOINT_SPELL_COSTS.md), §155. Before crediting a setup/main, check their current known activation costs; a qualifying free Quicken lazily selects the best already-scored jointly affordable second action. Native conversion root, SpellSlotCost, book/level, prepared equality/reverse order, fact per-day resource and base ResourceLogic/override semantics; no spend/refund/reservation or actor/context/rule preview. Per-pass reference quote memo and reusable pure budget scratch; limits 64 charges, 256 prepared slots, 16 conversion nodes. Unsupported prices stay unknown; failed setup/main quotes name their provider once per fight, while second alternatives are skipped. Ordinary actions remain. Current saving flags respected; speculative refunds/unlimited spend, custom/derived providers, materials/items, held charges/toggles and arbitrary future cost mutations are outside the joint quote. Existing DC/CL/metamagic/choice/movement rules remain. 24 new pure budget scenarios plus actor-free native checks; final gameplay/timing still needed. Checked source `7480e83`, startup MVID `ff8df076-59e9-468b-81c2-8f2d39302458`; [receipt](docs/competence/JOINT_SPELL_COSTS_CHECKS.json) / [jointcost1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-jointcost1-7480e83-UMM-test.identity.json). 692 unit + 27 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 13/13 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30; no main/development push, merge or release.

**Focused Shot support (13 November 2026, Codex; unplayed):** `codex/focused-shot-support` starts at PR #48 `4c0ef7d` over refreshed development `44b224f`. [Native design/replay](docs/FOCUSED_SHOT.md), §154. Review5 TB 244 shows the actual action withheld. Recognize its inspected before-cast self damage buff plus one ordinary ranged attack structurally; reuse native bonus calculation and cached weapon delta/defense/provoke/value paths. Require the native ranged hand and one attack; preserve zero/negative/active/disabled cases, Pure Mage, blocklists and native command/action/approach gates. Unknown variants/selectors/bonus restrictions stay unsupported. No actor preview, buff application, attack event, world/target scan or general movement policy change. Actual graph/negative/scalar/compiled-route checks added; existing weapon delta/critical approximations remain. Checked source `b890309`, startup MVID `22e24115-7500-4df6-828a-305f0075e591`; [receipt](docs/competence/FOCUSED_SHOT_CHECKS.json) / [focused1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-focused1-b890309-UMM-test.identity.json). 668 unit + 27 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 10/10 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30; Claude reviews/integrates, no main/development push, merge or release.

**Review5 playtest / recovery plan keys (12 November 2026, Codex; fix unplayed):** `codex/review5-advance-recovery` starts at PR #47 `cee7d48` over integrated development `44b224f`. [Evidence/replay](docs/REVIEW5_RECOVERY_KEYS.md), §153; includes the broad review and paired form fix. RT 2041–2051 rejects Seelah's plain Advance in preflight, then lazy recovery recreates it and native issue rejects the same Corrosive Bomb path. Filter recovery by actual refused plain-plan keys for this invocation only. Lazy ordinal set <=3 entries; no new identity strings or native queries, no stale/global target refusal. Untested/different plans and fresh decisions remain eligible. Existing offensive trigger, useful actions, status/cooldown gates and three-check cap unchanged. Four new unit scenarios, actual selector checks raise Idle Advance to 27; form-reference 14 inherited. Within the already authorized recovery scope; general movement/target/retreat/pathfinder/stuck policies unchanged. Checked source `3cb6422`, startup MVID `6383a7b3-201b-4c76-a335-027ee5ef4f8e`; [receipt](docs/competence/REVIEW5_RECOVERY_KEY_CHECKS.json) / [review7 identity](test-builds/AI-Control-FACC-v0.3.30-dev-review7-3cb6422-UMM-test.identity.json). 668 unit + 27 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30; no main/development push, merge or release.

**Review5 playtest / matched form reference (12 November 2026, Codex; fix unplayed):** `codex/review5-playtest-review` starts at integrated development `44b224f`; PR #46 planning is separate. [Broad review/replay](docs/REVIEW5_PLAYTEST_REVIEW.md), [input identities](docs/REVIEW5_PLAYTEST_INPUTS.json), §152. TB confirms review5 MVID; RT identified by author without header; combat spawns/rests confirmed. Five TB/six RT summaries, no AI exception; form and Fury now execute. Confirmed code bug: Offence loses Enemy for a zero weapon estimate, so a new-form appraisal can fall back to another context enemy. Retain the actual assessed target/estimate independently, pair both quotes and use its ranged flag; unavailable baseline stays unknown. Existing profitable-buff fields and Fury admission unchanged; note now names target. No new scan/forecast/native rule or movement policy; FORM-REFERENCE 14 actor-free checks. Lazy recovery reattempt of an already-refused move is a separate approved-scope follow-up. Checked source `4392e51`, startup MVID `3f9c3355-3c65-4477-b059-dd370eb20d2b`; [receipt](docs/competence/REVIEW5_PLAYTEST_CHECKS.json) / [review6 identity](test-builds/AI-Control-FACC-v0.3.30-dev-review6-4392e51-UMM-test.identity.json). 664 unit + 27 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30; no main/development push, merge or release.

**Earlier handover (12 November 2026, Claude): Codex's interim PRs #21-#45 (25 PRs) are reviewed and integrated into development,
see `docs/TACTICAL_AI.md` §150.** Merge commits are `485339f` (stack #22-#45: C06 view guard, Psionics scope, Tome burn, drain, swarms, attached damage,
resistance memo, caster policies (Pure Mage and the Aggressive policy are opt-in; Magic Fusion), C07 native Magus, Eldritch Archer, C08 Shifter/Fury, and the
author-approved movement repairs #29/#36/#37/#39/#45) and `d306f1e` (#21 inventory). Game rules were checked against the decompiled game, the BubblePrints
2.7.0x stock database and the Tome 1.4.18 source. The only correction was restoring history comments in `AddClosingCandidates`. Automated checks:
- Release build: 0 warnings.
- Unit tests: 664 pass.
- Inventory Python tests: 27 pass.
- ProfileAudit: 20,088 loaded, the one known Achaekek failure, every check passes.
- ToggleCosts.Native: 90 checks pass, 98 with the Psionics provider.
- Follow-up with the author's Units export and ToyBox 1.7.34: 35,497 blueprints loaded; AFFLICTION 4, SWARM-COVERAGE 27
  and ATTACHED 24 checks pass; TomeBurn.Native 14 and ToyBoxSpend.Native 10 checks pass (§150).

Play `test-builds/AI-Control-FACC-v0.3.30-dev-review5-45c3b4d-UMM-test.zip` (MVID `b3359daf-c8e5-4bb6-9245-d6be013a4d15`, the
integrated code). None of it is played yet; §150 lists what to confirm in game. `main` and releases are unchanged. The Codex handovers below describe each PR.

**Shifter1 review / approved idle-advance recovery (12 November 2026, Codex; unplayed):** `codex/rejected-approach-advance` starts at PR #44 `fe2caa7` over refreshed development `1f58150`. [Evidence/design/replay](docs/REJECTED_APPROACH_ADVANCE.md), §149. Author explicitly permitted the movement fix. Bandit R2/R3 loses all selected attack approaches after early step/full intent suppressed ordinary advances. After an actual offensive-approach refusal and no useful action, reuse the existing three-target closing builder and real-path risk repricing once. Native three-check preparation cap stays; plain moves retain final dispatcher validation. Preserve useful quick/main/weak/cantrip actions, native movement/action/status/Pure Mage gates and genuine RT in-place cooldown waits. A refused heal/plain move alone cannot recover into combat closing. No reused rejected path, full Decide retry, general retreat/commitment/pathfinder/stuck change. Checked source `45c3b4d`, startup MVID `b3359daf-c8e5-4bb6-9245-d6be013a4d15`; [receipt](docs/competence/REJECTED_APPROACH_ADVANCE_CHECKS.json) / [review5 identity](test-builds/AI-Control-FACC-v0.3.30-dev-review5-45c3b4d-UMM-test.identity.json). 664 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30; Claude reviews/integrates, no main/development push, merge or release.

**Shifter1 review / correlated EA sampling (12 November 2026, Codex; unplayed):** `codex/ea-correlated-calibration` starts at PR #43 `d144b80` over refreshed development `1f58150`. [Evidence/replay](docs/EA_CORRELATED_SAMPLING.md), §148. Native ranged Spellstrike rays share the first arrow roll, so the observer cannot compare a grouped multi-ray packet stream to one ray. Exclude only projectile Spell samples from actual EA actors with a physical ranged event roll; pending payload/toggle state is not an identity. Packet defense observations run first and remain; ordinary rays and spell-like kinetic calibration remain. Eight unit boundary cases and four added native-route checks (EA 29 total); no new forecast, target scan or cache. Checked source `3f66960`, startup MVID `b7d068bd-340a-43b7-94fe-f12f51b0c74a`; [receipt](docs/competence/EA_CORRELATED_SAMPLING_CHECKS.json) / [review4 identity](test-builds/AI-Control-FACC-v0.3.30-dev-review4-3f66960-UMM-test.identity.json). 655 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30; Claude reviews/integrates, no main/development push, merge or release.

**Shifter1 broad playtest review / campaign flags (12 November 2026, Codex; fix unplayed):** `codex/shifter1-playtest-review` starts at PR #42 `20137ba` over refreshed development `1f58150`. [Review/replay](docs/SHIFTER1_PLAYTEST_REVIEW.md), [inputs](docs/competence/SHIFTER1_PLAYTEST_INPUTS.json), §147. Confirmed shifter1 MVID, nine fights / 465 TB+RT summaries, no logged AI exception; author sometimes spawned/rested mid-combat. Actual Wenduag Shifter 8 has no form/Fury use; FlagUnlocked was omitted from safe conditions, withholding unique-form appraisal. Exact native flag predicate now allowed (read-only ContainsKey/TryGetValue; native Not/value filters), no quest write/hard-coded state. Zero/profitable form reasons use bounded existing note ledger, no new forecast/context. 21 actor-free exported/store/routes checks plus inherited Shifter 48. Largest three slow rows overlap GC, not evidence of a two-second form estimate. Correlated EA damage sampling and an author-approved lazy advance fallback are separate follow-ups; this PR changes no movement/commitment/retreat/pathfinder/stuck policy. Checked source `a58de3e`, startup MVID `d3873816-d1f0-4bc4-bc80-dc5e5e681a3b`; [receipt](docs/competence/SHIFTER1_REVIEW_CHECKS.json) / [review3 identity](test-builds/AI-Control-FACC-v0.3.30-dev-review3-a58de3e-UMM-test.identity.json). 647 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30, Claude reviews/integrates; no main/development push, merge or release.

**Requested class support: Shifter/Ulbrig (12 November 2026, Codex; unplayed):** `codex/shifter-support` starts at PR #41 `a6c3f53` over fetched development `1f58150`; includes EA/review2 prerequisites. [Native design/replay](docs/SHIFTER_SUPPORT.md), [input contracts](docs/competence/SHIFTER_NATIVE.json), §146. Report is v0.3.27, upkeep probably enabled but unconfirmed; no scene log/MVID, not a reproduced cause. Automatic OOC upkeep excludes native transformations. Major Shifter forms gain core native natural-attack/stat/absolute-size/equipment-loss valuation; existing/suppressed forms and final issue guard prevent recast/tier loops. Actual minor-aspect/C06 resource/group path reused. Fury selects only when off via actual generated weapon/hand identity, native counts/secondary/transfer rules, lists/ownership/round/restore; player/pre-existing selection retained. Eight form quotes/one existing enemy, four Fury kinds, bounded native layouts/routines, decision-local transfer facts, no actor/buff/equipment preview or ordinary-hit memo contamination. New form facts/proc-only benefits uncredited; caster-multiclass new forms withheld; legacy RT/general movement/commitment/retreat/pathfinder/stuck rules not expanded. Checked source `f904c39`, startup MVID `875cf339-de55-4d04-89f9-d5235ec7ecbe`; [receipt](docs/competence/SHIFTER_CHECKS.json) / [shifter1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-shifter1-f904c39-UMM-test.identity.json). 647 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 73/73 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30; Claude reviews/integrates, no main/development push, merge or release.

**Requested class support: Eldritch Archer (12 November 2026, Codex; unplayed):** `codex/eldritch-archer-support` starts at review2 / PR #40 `8985e40`, fetched development `1f58150`. [Native design/replay/PR #38 trade-offs](docs/ELDRITCH_ARCHER.md), §145. The shared bounded Magus planner now supports ranged equipment, native Ranged Spell Combat and Ranged Spellstrike. First arrow consumes its payload even on a miss; every spell projectile shares that arrow roll, spell critical x2, no later-arrow/Manyshot retry. Actual bow eligibility/clock/movement cleanup, source availability and native queue/spend remain. Pending payloads contribute once and are not re-armed; stale fields await native cleanup. Arming does not trigger a false no-effect verdict. Four casts/two states, existing policies/restore/ordinary Ranged/Pure Mage boundaries, no actor/equipment simulation or new path/world scan. 25 native exported/scalar/routes checks; source/model limits and live replay documented. Shifter/Ulbrig is a separate authorized next task. Checked source `5ddcbca`, startup MVID `8e6a8470-b08a-41d6-9771-0b995ae18465`; [receipt](docs/competence/ELDRITCH_ARCHER_CHECKS.json) / [archer1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-archer1-5ddcbca-UMM-test.identity.json). 629 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 5/5 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30, Claude reviews/integrates; no development/main push, merge or release.

**Exit1 follow-up: friendly treatment coordination (12 November 2026, Codex; unplayed):** `codex/friendly-dispel-coordination` starts at PR #39 `8886762` over fetched development `1f58150`; includes review1/exit2 and prior prerequisites. [Evidence/replay](docs/FRIENDLY_TREATMENT_COORDINATION.md), [export/stock cross-check](docs/competence/TREATMENT_STOCK_INPUTS.json), §144. Lann/arcan both begin Treat Affliction on Seelah (AI 6722–6737); arcan interrupts unacted, not two consumed cures. Actual Classes archive graphs confirm native skill-check removal plus a recipient-wide daily exclusion marker; supplied BubblePrints stock Data matches, without adding unique coverage. Withhold only the unmodelled friendly removal fallback while an identical ability is pending on that recipient, reusing native command completion/expiry and current identity tracking. HP heals/modelled effects and other recipients remain; no new cache/context/world/path query or movement policy. Ten native metadata/compiled checks; actual two-actor timing remains gameplay work. Checked source `9464c2d`, startup MVID `a21cbb58-f176-4d4a-8ec2-cf912910bc3e`; [receipt](docs/competence/TREATMENT_PENDING_CHECKS.json) / [review2 identity](test-builds/AI-Control-FACC-v0.3.30-dev-review2-9464c2d-UMM-test.identity.json). 629 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Claude reviews/integrates; manifest 0.3.30, no main/development push, merge or release.

**Approved exit follow-up: native entity bounds (12 November 2026, Codex; unplayed):** `codex/area-exit-native-bounds` starts at PR #38 `e1e3149`, over fetched development `1f58150`; includes prior caster/Magus/step/exit prerequisites. [Design/evidence/replay](docs/MOVEMENT_EXIT_BOUNDS.md), [native contracts](docs/competence/MOVEMENT_BOUNDS_NATIVE.json), §143. Exit1 is now played: endpoints match ground, but some smoke leaves repeat. Native cylinders/boxes use entity-bounds physics queries, not Shape.Contains. Read actual sphere world bounds/offset, then native capsule/OBB geometry; other shapes retain Contains. One search snapshot, fresh preflight/issue snapshots, same 80-hint/three-path limits, no overlap queries or actor/collider mutation. Registered-but-geometrically-clear bodies receive no new escape credit; membership/global danger is untouched. Bounded native-bound diagnostic helps distinguish update lag and geometric mismatch; pursuing smoke can still recapture actors. 22 pure geometry cases and 28 native math/routes checks; actual Unity queries/commands remain gameplay work. General commitment/retreat/pathfinder/swarm search/stuck policies remain. Checked source `9145404`, startup MVID `36d0bc00-2822-419e-a10a-d3a60bd536fa`; [receipt](docs/competence/MOVEMENT_BOUNDS_CHECKS.json) / [exit2 identity](test-builds/AI-Control-FACC-v0.3.30-dev-exit2-9145404-UMM-test.identity.json). 629 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 17/17 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Claude reviews/integrates; manifest 0.3.30, no main/development push, merge or release.

**Exit1 broad review / constant debuff cost (12 November 2026, Codex; fix unplayed):** `codex/exit1-playtest-review` starts at PR #37 `dc5235f`, containing fetched development `1f58150` and caster2/magus1/step1/exit1 prerequisites. [Review](docs/EXIT1_PLAYTEST_REVIEW.md), [inputs](docs/EXIT1_PLAYTEST_INPUTS.json), §142. Eight fights / 900 summaries, no AI exception observed. Fixed/simple numerical debuffs omit redundant native source contexts only with the same caster; dynamic ranks/dice/pools/formulas retain binding, recipient stats stay live, no mutable context sharing. Native fixture checks distinguish amounts/classification from gameplay timing. Successful Fusion and chosen swarm exits now have evidence; Drow Magus is EA (outside melee C07); four low ray rolls do not justify recalibration. Repeated smoke exits expose native physics membership versus shape forecast, to be corrected separately within the approved exit scope. Friendly Treat Affliction can duplicate an in-flight ally cast; general pursuit/retreat/stuck policies remain. Checked source `318b52f`, startup MVID `ffce03c5-bfe3-414f-b6bd-4a946d673dbf`; [receipt](docs/competence/EXIT1_REVIEW_CHECKS.json) / [review1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-review1-318b52f-UMM-test.identity.json). 607 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Claude reviews/integrates; manifest 0.3.30, no main/development push, merge or release.

**Approved movement: reachable area exits (12 November 2026, Codex; unplayed):** `codex/area-exit-endpoints` starts at PR #36 `ac7d0df` over development `1f58150`; step1/magus1/caster2 prerequisites are included. This is the author's second approved movement proposal. [Design/replay](docs/MOVEMENT_AREA_EXITS.md), [native evidence](docs/competence/MOVEMENT_EXIT_NATIVE.json), §141. Native horizontal inside-navmesh checks do not project Y; exit hints now use native nearest-surface/linecast results on the same connected graph, with body plus the existing 0.15 m arrival clearance. Shared unmounted escape plans carry a decision-local endpoint/route-budget constraint through selected preflight and issue. Three selected route checks, 80 bounded hints, reused ground start, no full path per hint/context/actor simulation. Other moves keep their fallback; mounted/legacy dispatch not expanded. General commitment, retreat, pathfinder, swarm exit geometry and stuck cancellation remain. Checked source `e806f4d`, startup MVID `905bb172-799c-44cb-a46a-ff208a31dab4`; [receipt](docs/competence/MOVEMENT_EXIT_CHECKS.json) / [exit1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-exit1-e806f4d-UMM-test.identity.json). 607 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 4/4 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30, live gameplay/matched Unity timing pending, Claude reviews/integrates, no main/development push, merge or release.

**Approved movement: step/full preflight (12 November 2026, Codex; unplayed):** `codex/step-attack-preflight` starts at PR #35 `aaf9b20` over fetched development `1f58150`. The author approved both movement proposals; this first task covers only selected-path validation for five-foot-step full attacks. [Evidence/replay](docs/MOVEMENT_STEP_PREFLIGHT.md), §140. Explicit step intent enters the existing three-check preflight before boosts without spending a normal Move action. Shared weak-weapon fallback eligibility closes the low-value route too. Actual candidate/native scheduling checks cover the gap; native dispatcher retains final range/sight/step limits and path-claim cleanup. No global unreachable cache or all-target scan; in-place routines add no paths. Area exits are separate; commitment/retreat/pathfinder/stuck cancellation remain. Checked source `ee24df1`, startup MVID `9523fbe4-edb2-46b5-9fbe-e430636c9634`; [receipt](docs/competence/MOVEMENT_STEP_CHECKS.json) / [step1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-step1-ee24df1-UMM-test.identity.json). 607 unit + 11 inventory, step 13 and inherited checks pass; real-reference Release 0 warnings, 0/0 added signatures resolve. Compatible/supplementary audits pass; original full-corpus enum failure remains distinct. Manifest 0.3.30, gameplay/matched timing pending, Claude reviews/integrates, no main/development push, merge or release.

**C07 native melee Magus (12 November 2026, Codex; review/gameplay pending):** `codex/magus-native-combat` starts at PR #34 `02d2c2a` over fetched development `1f58150`; PR #33/C06 prerequisites are inherited. Read [design/replay](docs/C07_MAGUS.md), [native contracts](docs/competence/C07_MAGUS_NATIVE.json) and §139. The broad caster1 review/caster2 fixes are included. Native free mode identities permit bounded preparations through existing ownership/lists/restore; four in-place casts and two future states. Actual root penalties/extra attacks and native predicates govern forecasts. Native prepared windows/finite deadlines and explicit full/single flags govern continuation. First successful eligible weapon hit prices one held payload with x2 spell critical/shared overkill; helpful/blocked charges are guarded (including the native natural-weapon route with Spellstrike off, for all units), item-retained charges checked, marginal charge loss retained, paid slot not discounted again. Native command factory/controller retains delivery/spending. Scalar feature/grip previews restore and invalidate dependent caches; distant pairs skip attack calculation. Pure Mage/ordinary Ranged remain; fired rays are not melee charges. EA/Fusion automatic combinations and legacy RT expansion are outside scope. No movement/commitment/retreat/path/hazard/stuck change or localization rewrite. Checked source `e2473e8`, startup MVID `66d1bd9e-d6a8-4c0c-8b1b-7dbc99b2db72`; [receipt](docs/competence/C07_MAGUS_CHECKS.json) / [magus1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-magus1-e2473e8-UMM-test.identity.json). 607 unit + 11 inventory, Magus 32 / supplementary-stock 44 and inherited regressions pass; real-reference Release 0 warnings, 29/29 signatures resolve. Compatible and supplementary audits pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending, manifest 0.3.30, Claude reviews/integrates, no main/development push, merge or release.

**Caster1 broad playtest review (11 November 2026, Codex; fixes unplayed):** `codex/caster1-playtest-review` starts at played caster1 packaging `f278114`, over fetched development `1f58150`; PR #33 is its prerequisite. [Review and movement proposal](docs/CASTER1_PLAYTEST_REVIEW.md), [input accounting](docs/CASTER1_PLAYTEST_INPUTS.json), [native contracts](docs/competence/CASTER1_REVIEW_NATIVE.json), §138. 13 fights / 1,043 summaries; no AI exception observed. Five ray damage-check discrepancies expose missing simple-projectile counts; shared estimates now use actual root arrays/native ranks with per-payload mitigation. Ray samples separate actual attack rolls. Orvath's critical heal refusal is fixed by carrying the priced command's native sight flag to the Pure Mage guard. Native burn getter settings use actor-free `PropertySettings.Apply` before ranks; optional provider boundaries stay bounded. New metadata/flag reads create no extra scoring contexts. 25 path refusals/repeated smoke exits are proposal-only: no movement/commitment/retreat/path/stuck changes. Magus modes/held-charge continuation remain a separate C07 task; fired rays are not normal melee Spellstrike charges. Tooltip provenance unresolved; Profane Invocation/fusion gameplay unexercised. Checked source `281c679`, startup MVID `4902d9eb-2d73-4f6e-a9e4-813adba6d7ce`; [receipt](docs/competence/CASTER1_REVIEW_CHECKS.json) / [caster2 identity](test-builds/AI-Control-FACC-v0.3.30-dev-caster2-281c679-UMM-test.identity.json). 599 unit + 11 inventory, projectile 29 / burn 50 / caster 36 / fusion 35 and inherited native checks pass; real-reference Release 0 warnings, all five added signatures resolve. Compatible scope passes; original full-corpus enum failure remains separate; manifest stays 0.3.30, Claude reviews/integrates, no main/development push or release.

**Caster policies / Magic Deceiver (11 November 2026, Codex; review/gameplay pending):** `codex/ranged-pure-caster` starts at C06R5 / PR #32 `0f3061f`, over development `1f58150`. Read [docs/CASTER_POLICIES.md](docs/CASTER_POLICIES.md), [native contracts/input identities](docs/competence/CASTER_NATIVE_EVIDENCE.json) and §137. **Ordinary Ranged characters retain weapon attacks.** `PureMage` defaults false and acts only with Ranged; forbids physical fallbacks, with positive native spell/energy-shot exceptions and explicit single-attack flags in both modes. Critical touch-heal walks use existing safety/real-path checks; actual Reach uses the sticky delivery/ray weapon and cast-specific ranged risk. Conservative is the displayed old `Progressive = 1` (numeric/textual saves retained); Aggressive = 2 removes only offensive book-slot thrift, without reapplying resistance/overkill. Native `MagicHackData` supplies two ordered payloads, selected delivery/save and native dice caps; configuration-aware weak metadata/cache identities, per-action shared-save math and explicit unsupported-structure diagnostics. No fake familiar delivery or speculative fused next-spell boost spending. New modes/native Magic Deceiver use the shared backend when legacy RT was selected; other characters retain that choice. General movement/commitment/retreat/path/stuck cancellation are not retuned; C07 remains separate. Checked source `d184c8e`, startup MVID `35b96ebd-8929-4ce8-baa4-564d03bc454f`; [receipt](docs/competence/CASTER_CHECKS.json) and [caster1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-caster1-d184c8e-UMM-test.identity.json). 599 unit + 11 inventory, 32 caster / 35 fusion checks and inherited regressions pass, real-reference Release 0 warnings; 28/28 added foreign signatures resolve. Compatible scope passes; original full-corpus enum failure remains distinct. Offline metadata/flag checks and a zero-warning real-reference build do not establish live cast/rod/path/performance outcomes. Manifest 0.3.30; Claude reviews/integrates, no main/development push or release.

**C06R4 played / C06R5 performance review (Codex; fixes unplayed):** `codex/c06r4-playtest-review` starts at PR #31 `ca12a28` over development `1f58150`. [Review](docs/C06R4_PLAYTEST_REVIEW.md), [inputs](docs/C06R4_PLAYTEST_INPUTS.json), §136. 19 fights / 946 logged summaries; no AI exception; four poison risk notices, Cling absent, two chosen swarm exits. Slow samples 178 / 24 marked cleanups. Repeated native SR queries now use exact caster/recipient/AbilityData identity within one synchronous EvaluateAbility scope; no context sharing, native immunity/resistance logic preserved. Nested scopes isolate, ClearAttackCache epochs invalidate state previews, and later evaluations start empty. New SR read/reuse telemetry supports replay. Empty/non-numerical appraisals skip context only without numerical/rank/equipment parts; Death Ward and weapon/armour carrier regressions remain. Unsupported periodic notices are once per blueprint/session. Checked source `6794275`, startup MVID `64cd2d32-7182-47c5-a7c5-fef817edf58d`; [receipt](docs/competence/C06R4_REVIEW_CHECKS.json) and [C06R5 identity](test-builds/AI-Control-FACC-v0.3.30-dev-C06R5-6794275-UMM-test.identity.json). 594 unit + 11 inventory, Release 0 warnings; 18 new native memo/equipment checks and inherited C06 regressions pass; no added foreign member reference. Strict compatible scope passes; original full corpus retains named enum failure. Manifest 0.3.30; no movement/commitment/retreat/path/dispatch/cancellation policy change, no main/development push or release. C07 separate; Claude reviews/integrates.

**C06R4 attached damage (author approved 10 November 2026; review/gameplay pending):** `codex/c06-attached-damage` starts at C06R3 / PR #30 `b179597` over development `1f58150`. Read [docs/C06_ATTACHED_DAMAGE.md](docs/C06_ATTACHED_DAMAGE.md), [native evidence](docs/C06_ATTACHED_DAMAGE_NATIVE.json) and §135. Supported active recipient-owned NewRound/poison damage uses actual native schedules, contexts, saves and counters; it stays at every spot after enemy calibration and can survive a resolvable caster's death. Healing/drop/burn danger and eligible cleanse values use it. Armour/images/enemy marks cannot prevent this stream, and recognised native packets are kept out of enemy offence/recent-hit calibration after live-context checks. Current Ignore effects add no duplicate future ticks. Metadata/recipient caches keep work out of spot loops; no actor/context/action factory is used. Native energy removal checks packet type, including absorbed damage; no deliberate damaging casts on allies. Existing movement/retreat/commitment/path/dispatch/cancellation policy remains in place. Checked source `736358a`, startup MVID `3705efbc-ad31-48e0-a8b7-c01ea9654b1d`; [receipt](docs/competence/C06_ATTACHED_DAMAGE_CHECKS.json) and [C06R4 identity](test-builds/AI-Control-FACC-v0.3.30-dev-C06R4-736358a-UMM-test.identity.json). 588 unit + 11 inventory, Release 0 warnings, 24 attached / 27 swarm / 98 toggle / 14 Tome / 10 ToyBox / 7 reveal / 6 viewless checks pass; 26/26 added references resolve. Strict compatible scope passes; original full corpus retains the named enum failure. Native/gameplay and matched Unity performance acceptance remain distinct. Manifest 0.3.30; no main/development push or release.

**C06R2 played / C06R3 log review (9 November 2026, interim Codex; review/gameplay of fixes pending):** `codex/c06r2-playtest-review` starts at PR #29 `ba5d5d3` over development `1f58150`. The uploaded startup MVID matches C06R2. Read [docs/C06R2_PLAYTEST_REVIEW.md](docs/C06R2_PLAYTEST_REVIEW.md), [input accounting](docs/C06R2_PLAYTEST_INPUTS.json) and §134. 23 fights / 714 logged decision summaries; no AI exception. Swarm casts and twelve exit orders are exercised, with short-exit/Cling limits kept explicit. Fixes price pure concealment-removal utility by native removable miss chance, preserve hidden reveals, and recognize only the exact optional ToyBox spend predicate/getter contract in AI Control's resource reader. ToyBox itself is unchanged; module identity mismatch is not assumed away, and no plugin getter/setup is invoked. Native Disable is a counter: preview retain/release must balance exactly. Empty immutable buff parts skip needless context creation. Movement/retreat/commitment/path/cancellation behavior is unchanged; [attached-damage proposal](docs/C06R2_ATTACHED_DAMAGE_PROPOSAL.md) is not implemented. Final checked source `b19312a`, startup MVID `8af1ad59-23ff-429e-9a1a-55033bb86e0a`; [receipt](docs/competence/C06R2_REVIEW_CHECKS.json) and [C06R3 identity](test-builds/AI-Control-FACC-v0.3.30-dev-C06R3-b19312a-UMM-test.identity.json). 558 unit + 11 inventory, Release 0 warnings, 7 reveal / 10 ToyBox / 27 swarm / 98 toggle / 14 Tome / 6 viewless checks pass; both added native references resolve. Strict compatible scope passes; full original corpus retains the named enum failure. Gameplay/matched Unity performance pending. Manifest remains 0.3.30; Claude reviews/integrates, no main/development push or release.

**C06R2 approved swarm work (9 November 2026, Codex; review/gameplay pending):** the author approved [the bounded proposal](docs/C06_SWARM_PROPOSAL.md), explicitly requiring performance. `codex/c06-swarm-coverage` starts at C06R1 / PR #28 `758d7b1` over development `1f58150`. Read [docs/C06_SWARM_IMPLEMENTATION.md](docs/C06_SWARM_IMPLEMENTATION.md), [native evidence](docs/C06_SWARM_NATIVE_EVIDENCE.json) and §133. Rat/Bhoga use near-contact target-centred Standard casts, which the former live-aura-only hazard reader misses; Bhoga's damage also lacks a harmful label. The repair reads native cast reach/selection/consequences, validates active source snapshots, and uses actual boundaries for the existing step/move competition. Metadata/actor/pair/LOS caches and buffer/record reuse keep work out of spot loops; twelve directions and at most two supported-space search modes. General retreat, commitment, pathfinding, dispatcher and cancellation remain unchanged. Checked source `20a7d5a`, startup MVID `675da0f1-3cb6-4531-8724-70bc9ae2dedd`; [C06R2 identity](test-builds/AI-Control-FACC-v0.3.30-dev-C06R2-20a7d5a-UMM-test.identity.json) and [final receipt](docs/competence/C06_SWARM_CHECKS.json): 557 unit + 11 inventory, Release 0 warnings, 27 swarm / 98 toggle / 14 Tome / 6 viewless checks pass. Native transforms/LOS/commands and matched Unity performance remain gameplay checks. Manifest 0.3.30; no main/development push or release. C07 remains separate. Earlier C06R1/proposal-only notes below describe that older build, not this approved revision.

**C06R1 combined replay candidate (9 November 2026, Codex; review/gameplay pending):** `codex/c06-playtest-candidate` assembles the five focused C06 branches for a single UMM test ZIP. Read [docs/C06_PLAYTEST_CANDIDATE.md](docs/C06_PLAYTEST_CANDIDATE.md) / §132 and its final identity/check receipt. It includes the view guard, Psionics scope, Tome burn reader, drain-threat/remaining-level repair and passive SwarmAudit only. Swarm movement stays proposal-only and C07 held-charge command/pricing remains separate. Manifest stays 0.3.30; Claude reviews/integrates, no main/development/release push. Focused branch receipts below describe their standalone DLLs; the combined receipt identifies the packaged DLL. Packaged source `a19fb54`, startup MVID `1a2a9e3e-dd56-4849-b49e-7144ad3b7ed5`, [C06R1 identity](test-builds/AI-Control-FACC-v0.3.30-dev-C06R1-a19fb54-UMM-test.identity.json) and [combined checks](docs/competence/C06_COMBINED_CHECKS.json): 551 unit + 11 inventory, 98 toggle / 14 Tome / 6 viewless checks pass; Release 0 warnings. The original full corpus still has its named single enum failure.

**C06 playtest review (9 November 2026, interim Codex):** the author played C06 MVID `641ff828-00b6-46aa-9f92-3748fae7f2f6` on reported Steam 2.7.0x; four logs are fingerprinted in [docs/C06_PLAYTEST_REVIEW_INPUTS.json](docs/C06_PLAYTEST_REVIEW_INPUTS.json). Read [docs/C06_PLAYTEST_REVIEW.md](docs/C06_PLAYTEST_REVIEW.md) / §127. Separate native-view, Psionics cost-scope, Tome burn-property and drain-threat branches carry focused repairs. Passive SwarmAudit coverage logging and [docs/C06_SWARM_PROPOSAL.md](docs/C06_SWARM_PROPOSAL.md) collect the missing actor/area/cache evidence; do not implement swarm movement changes without author approval. Main/releases/version remain unchanged; Claude reviews all PRs later. Slow-row heap measurements are not exact allocations; hidden reveals/Profane Invocation/Judgment transitions are unexercised. C07 is now eligible but is a separate command/action/pricing contract.

Verification receipt: [docs/competence/C06_PLAYTEST_REVIEW_CHECKS.json](docs/competence/C06_PLAYTEST_REVIEW_CHECKS.json). Exact checked source and DLL identity are recorded there; a later receipt-only commit does not replace that tested DLL.

**C06 playtest interim fix (9 November 2026, Codex; review pending):** `codex/c06-native-query-guards` is based on played C06 / PR #22 head `ab810b3`, which includes development `1f58150` / `3443d94`. Read [docs/C06_NATIVE_VIEW_GUARD.md](docs/C06_NATIVE_VIEW_GUARD.md). Log 3 has one viewless reinforcement target that aborts weapon and movement candidate stages through native `UnitAttack.GetApproachRadius`; `CombatMath.ReadRoutine` now waits for both native views before querying geometry. Six compiled native checks cover viewless/null actor pairs without initializing an actor. No movement, target commitment, cancellation or dispatch policy changes. Other C06 playtest findings are separate tasks; manifest stays 0.3.30.

Verification receipt: [docs/competence/C06_QUERY_GUARD_CHECKS.json](docs/competence/C06_QUERY_GUARD_CHECKS.json). Exact checked source and DLL identity are recorded there; a later receipt-only commit does not replace that tested DLL.

**C06 playtest / Tome burn (9 November 2026, Codex; review pending):** `codex/c06-tome-burn-property` starts at played C06 / PR #22 `ab810b3`, including development `1f58150` / `3443d94`. Read [docs/C06_TOME_BURN_PROPERTY.md](docs/C06_TOME_BURN_PROPERTY.md) and §129. Orvath's named fallback matches the installed Tome Burn Resistance rewrite of `KineticNonLethalDamageProperty`, which uses TTT's composite getter even when the feat is not owned. The optional reader recognizes only the inspected module/layout/arithmetic, queries actual native feature ownership and preserves per-term float/floor arithmetic. Unknown shapes stay fallback. No mod getter/condition/setup is executed; 14 supplied-provider fixture cases pass without actor initialization. Manifest 0.3.30; movement/dispatch unchanged.

Verification receipt: [docs/competence/C06_TOME_BURN_CHECKS.json](docs/competence/C06_TOME_BURN_CHECKS.json). Exact checked source and DLL identity are recorded there; a later receipt-only commit does not replace that tested DLL.

**C06 playtest / drain threat (9 November 2026, Codex; review pending):** `codex/c06-drain-threats` starts at played C06 / PR #22 `ab810b3`, including development `1f58150` / `3443d94`. Read [docs/C06_DRAIN_THREATS.md](docs/C06_DRAIN_THREATS.md) and §130. Native death compares `UnitPartNegativeLevels.Count` with character level; new drain now uses the remaining limit. Existing recognized beyond-HP harm also floors target-removal/control worth, which otherwise lost most of its magnitude through the x2 CR share cap. Incoming-HP calibration and action/commitment/movement algorithms are unchanged; the shared effect reader now knows existing negative levels. 549 unit tests pass in the first check; final committed build/audit receipt follows. Manifest 0.3.30; no forced target or class-name bonus.

Verification receipt: [docs/competence/C06_DRAIN_THREAT_CHECKS.json](docs/competence/C06_DRAIN_THREAT_CHECKS.json). Exact checked source and DLL identity are recorded there; a later receipt-only commit does not replace that tested DLL.

**C06 playtest / Psionics cost scope (9 November 2026, Codex; review pending):** `codex/c06-psionics-cost-scope` starts at played C06 / PR #22 `ab810b3`, including development `1f58150` / `3443d94`. Read [docs/C06_PSIONICS_COST_SCOPE.md](docs/C06_PSIONICS_COST_SCOPE.md) and §131. All sampled paid performances had the old generic patch fallback; inspected Psionics 0.2.3 globally postfixes cost but only changes GUIDs in `MindBladeEnchantments.AbilityToggleCosts`. The exact module/member is recognized; the live map is checked each quote, mapped enchantments remain fallback, and unknown hooks now identify their method/type/module. Snapshot lifetime remains one decision. No C07/C08, movement, ownership or dispatch change; version 0.3.30.

Verification receipt: [docs/competence/C06_PSIONICS_SCOPE_CHECKS.json](docs/competence/C06_PSIONICS_SCOPE_CHECKS.json). Exact checked source and DLL identity are recorded there; a later receipt-only commit does not replace that tested DLL.
**Phase B mapping task (8 November 2026, Codex; PR #21 review pending):** originally `fdc5e5e`; self-review based on author-requested `3443d94`, reconciled with packaging `1f58150`.
Read [docs/CLASS_COMPETENCE_CURRENT.md](docs/CLASS_COMPETENCE_CURRENT.md) and
[docs/competence/PHASE_B_VALIDATION.md](docs/competence/PHASE_B_VALIDATION.md): 74 exported class assets / 213 archetypes,
9,426 per-feature rows and bounded selection-aware signatures; installed DLL/settings identities and eight byte-identical loader caches.
No runtime/version/package change. Strict reader: 44,637 loaded / one known Unity-bound failure, no verdicts; separately allowed-one
PARTIAL pass on competence3b: 4,807 rows / zero reader errors, 8/8 actor-shaped burn kits, reveal/cast-bonus checks; 27 Python + four static-assembly checks. Self-review corrected query/grant edges, value/action signatures and split-reader/scope citations: 13,576 nodes / 4,510 signatures / 56 missing / 767 frontier / 6,159 unclassified relations.
Generated mod graphs remain absent despite post-initialization export provenance; DarkCodex settings enable Elemental Scion,
whose `BurnNumberProperty` replacement is not present in the exported native property. This affects the Psychokineticist stat-penalty consumer; it does not explain Orvath's `KineticNonLethalDamageProperty` fallback (§126, diagnostic `3443d94`). Do not infer mod compatibility or full actor competence.
**Ownership (superseded 12 November 2026):** the C06 stack, C07 Magus/Eldritch Archer (#35, #41) and C08 Shifter forms (#42) were later implemented by Codex as interim coder and integrated by Claude with PR #22-#45. Movement, acceptance, blocklist, release and version boundaries remain as below.

**Competence programme (8 November 2026): PR #20 integrated, read [docs/CLASS_COMPETENCE_CURRENT.md](docs/CLASS_COMPETENCE_CURRENT.md)
first.** Codex's foundation (`codex/class-competence-foundation`, from 0.3.30 test 2 `71ca3de`) was reviewed against the
author's programme text and merged into development (`452c761`). Review fixes: burn consequences read on real kineticist kits
(Elemental Overflow from level 6 and the Adaptation talents had made every such kit "unknown") and every burn keeps its per-rest
price (`cf62263`); next-spell Persistent through the save-effect factor, disbelief never Persistent (`4c63578`); inventory keeps
prerequisite gate values, area-grant note corrected (`7e89a16`). Derived roster from the Classes + Mythic exports:
`docs/competence/ROSTER.md` (72 class blueprints, 212 archetypes). Next stages re-ranked by the programme's Phase D order
(C07 Magus held charge first - confirmed execution gap; then C06 toggle resource horizons, C08 forms). Test ZIP
`AI-Control-FACC-v0.3.30-dev-competence2-7e89a16-UMM-test.zip` (MVID `0ed42ac5-d9ce-4f88-a339-388028b036c3`); 522 unit +
11 inventory tests; not played. Manifest stays 0.3.30. Preserve all movement/target/pathfinding/cancellation boundaries below.
**competence3 (8 November 2026, `docs/TACTICAL_AI.md` §126, not played):** competence2 played (6 TB + 8 RT fights, higher enemy
difficulty, eleven-member party, no mod errors). Invisible last enemy revealed by Faerie Fire/Glitterdust where the party saw
it vanish (`HiddenEnemies`, `TacticalEngine.Hidden.cs`; a search walk would be a movement change - for the author); walk prices
read once per decision (`TacticalEngine.DropModel.cs`, allies x enemies x allies per spot before); unique buffs settle 3 rounds
(`ValueModel.UniqueMoveAllowed`); cast-time damage bonuses and Point-Blank Shot in ability damage (`CombatMath.CastBonus.cs`:
Throw Anything bombs were valued at a third). Orvath's burn stays on the older model, cause not yet known (not DarkCodex: its patch hits
`BurnNumberProperty`, which his native burn buffs do not rank by); competence3b names buff, property and getters in that line.
Test ZIP `AI-Control-FACC-v0.3.30-dev-competence3-cfd8f0a-UMM-test.zip` (MVID `97bdfced-0fac-49d9-9673-2138acc373f2`), then
`AI-Control-FACC-v0.3.30-dev-competence3b-3443d94-UMM-test.zip` (MVID `740e7ec5-27f8-4575-9187-258b0f61f426`, the named burn line); 531 tests.

**Earlier handover (29 October 2026, Claude): read [docs/INTEGRATION_0322_FOLLOWUPS.md](docs/INTEGRATION_0322_FOLLOWUPS.md)
before resuming.** With the author's authorization, Claude reviewed and merged PR #13, #14 and #15 into development
(merge commits `f0a44df`, `1d49979`, `ecabeb4`) and fast-forwarded `main` to the same reviewed revision. The integrated
test package was `test-builds/AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.zip` (MVID
`2cc6eb92-af79-4a1c-a65d-aad66d8ef315`); then `AI-Control-FACC-dev-fixes2-c3f1636-UMM-test.zip`; then
`AI-Control-FACC-dev-movement-0c2e0f5-UMM-test.zip` (played as v0.3.23-dev-build2); then
`AI-Control-FACC-v0.3.23-dev-build3-089a402-UMM-test.zip`; then
`AI-Control-FACC-v0.3.23-dev-build4-da4e0a5-UMM-test.zip` (MVID `f1604b11-326e-425e-a77e-2533856d4155`, UMM 0.3.23, development only); in-game replay is pending. The published v0.3.22 ZIP/tag is unchanged and
does not contain these follow-ups; the author publishes GitHub Releases and creates tags. Movement, target commitment,
retreat, pathfinding and stuck-cancellation changes need discussion with the author first; the reach/Grease and Seelah
wall rows are diagnostics only; Ardub/body-blocking stays deferred. Recommendation A (reproducible verification) is done and on
`main` too: `tools/ProfileAudit` builds again (plus `--optional-provider` for a real TTT DLL), and the PR #13-#15 rules
are unit-tested helpers (218 tests); see the handover. The fixes2 log review (30 October,
[docs/RELEASE_0322_FIXES2_REVIEW.md](docs/RELEASE_0322_FIXES2_REVIEW.md)) fixed sleepers being shot awake and unread nested
conditions (Holy Water) and, with the author's approval, three movement follow-ups (tie-break for walk-first actions,
step-out for the best provoking cast, `WakeFactor` on Charge and the step's shot), on development only. The build2 log
review (31 October, [docs/RELEASE_0323_BUILD2_REVIEW.md](docs/RELEASE_0323_BUILD2_REVIEW.md)) added real-time Coup de
Grace with a walk, the last sleeper left for a Coup de Grace, and no failure strike for force-finished casts. The
build3 review (31 October, [docs/RELEASE_0323_BUILD3_REVIEW.md](docs/RELEASE_0323_BUILD3_REVIEW.md)) answered the author's
play-test notes: revives priced against the stand-up attack and the next blow in sequence, real-time walks priced by
their time and interceptions (front-liners stay on the enemy front line), no orders during the get-up animation;
`Info.json` is 0.3.23 from this build on. PR #16 (task branch `claude/alchemist-carnage-quiver`, merged into development 1 November with the author's
authorization, merge commit `0f7f2e6`): player reports on Alchemist bombs / Alchemical Weapon, Dreadful Carnage and the Kinetic Sharpshooter quiver -
evidence, changes and replay steps in [docs/TASK_BOMBS_CARNAGE_QUIVER.md](docs/TASK_BOMBS_CARNAGE_QUIVER.md); test ZIP
`AI-Control-FACC-v0.3.23-dev-bombs-carnage-cabdb2e-UMM-test.zip` (MVID `89ffb864-46e5-4840-ba2e-15318074028e`), played as
build5. The build5 log review (1 November, [docs/RELEASE_0323_BUILD5_REVIEW.md](docs/RELEASE_0323_BUILD5_REVIEW.md), on
development) fixed the 5-foot step out of melee (only for casts the Progressive policy keeps, against options that walk,
cones keep their targets), sleepers left unshot for a far-away Coup de Grace ally, and summon after summon; test ZIP
`AI-Control-FACC-v0.3.23-dev-build6-967050c-UMM-test.zip` (MVID `09a7ab12-2cc2-477c-a688-74765960ea97`) is now the current
development test build. At the author's request (1 November) it is released as **v0.3.23**: `release/CompanionAI_Pathfinder_v0.3.23.zip`
(byte-identical to the build6 ZIP), notes `release/CHANGELOG_v0.3.23.md`, `main` fast-forwarded to the release revision;
the author created the GitHub release and tag `v0.3.23`. The build5 review fixes themselves are not yet played.
**v0.3.24 work (1 November, [docs/V0324_BUILD1_REVIEW.md](docs/V0324_BUILD1_REVIEW.md)):** the "v0.3.23" logs the
author sent ran the build5 DLL (`89ffb864`), not the release. Fixed: held states (Psionics' psionic focus and the trance it
ends: `TacticalEngine.HeldStates.cs`), a Coup de Grace ally must reach the sleeper outside harmful areas (Selective areas
pass), dispel/removal fallback only for what the ability can remove; `Info.json` 0.3.24; test ZIP
`AI-Control-FACC-v0.3.24-dev-build1-432a219-UMM-test.zip` (MVID `05fff211-2d9a-452c-bce0-b988c4840974`). Its logs
([docs/V0324_BUILD1_LOGS_REVIEW.md](docs/V0324_BUILD1_LOGS_REVIEW.md)): the ranged-style step out of melee now subtracts what
it gives up; test ZIP `AI-Control-FACC-v0.3.24-dev-build2-2d6b49b-UMM-test.zip` (MVID `c3e6faed-f700-40ff-82b6-3cd864c6a4b0`)
was played next. With the author's approval, real time now shares a target out of walking reach (the stuck walk's native
path ends short of it) across the party (`CombatMemory.NoteOutOfWalkingReach`, `TacticalEngine.WalksIntoMelee`). At the
author's request this is released as **v0.3.24** (commit `0a203b4`): `release/CompanionAI_Pathfinder_v0.3.24.zip`
(byte-identical to `test-builds/AI-Control-FACC-v0.3.24-dev-build3-0a203b4-UMM-test.zip`, MVID `0ea24da4-70d0-4c77-9046-58f05061174b`), notes
`release/CHANGELOG_v0.3.24.md`, `main` fast-forwarded; the author created the GitHub release and tag `v0.3.24`.
**v0.3.25 test 1 (1 November, [docs/V0325_TEST1.md](docs/V0325_TEST1.md), `docs/TACTICAL_AI.md` §84-86, development
only, not played):** Unraveling and Chain Arrows in automatic infusion selection (`TacticalEngine.Unraveling.cs`,
`TacticalEngine.Ricochet.cs`, `ValueModel.Dispel.cs`); getting a held state back - psionic focus regained in and after
combat, an AI-ended trance switched back on after the fight, move action + standard compared with a full round
(`TacticalEngine.HeldStates.cs`, `ValueModel.HeldStateWorth`); potion audit - Magical Vestment, Align/Bless Weapon and
Lesser Restoration read instead of guessed or ignored. `Info.json` 0.3.25; test ZIP
`AI-Control-FACC-v0.3.25-dev-test1-907cd75-UMM-test.zip` (MVID `b9270500-be53-43c5-8beb-8bbf61bbafe6`); 273 tests.
**v0.3.25 test 2 (2 November, [docs/LIVE_CODE_AUDIT_V0325.md](docs/LIVE_CODE_AUDIT_V0325.md), `docs/TACTICAL_AI.md` §87):** test1
played (14 fights, no mod errors); log review plus live-code audit. Fixed: unaffordable offence no longer scored
(`AbilityScoringPolicy.SkipUnaffordableOffence`), used-up healing items restrained in easy fights
(`ValueModel.SpentHealValue`, item bar), weapon damage reduction by the native `AddDamageResistancePhysical.Hit`
(`CombatMath.WeaponDamageProbe`). Open: A4 sustained Orvath ability cost, A5 allocations - the slow-decision line now names
the costliest abilities and heap growth. Test ZIP `AI-Control-FACC-v0.3.25-dev-test2-2692562-UMM-test.zip` (MVID `15e39809-c72b-47a8-8db8-e2593f1ca945`); 282 tests.
**v0.3.25 test 3 (2 November, audit report §9, `docs/TACTICAL_AI.md` §88):** test2 played - stutters are garbage collections
inside Orvath's decisions (+4-10 MB each). Exact allocation fixes: ability parameters reused per decision
(`CombatMath.ParamsOf`), struct condition-cache key, `NeedLoS` once per ability, GUID text cached; per-ability heap growth
in the slow line. Test ZIP `AI-Control-FACC-v0.3.25-dev-test3-ffed9ac-UMM-test.zip` (MVID `b1f83486-ff07-4245-b5d2-f4b0998b5d1a`). Test3 played (heap growth
mostly 40-300 KB a decision, 3 collections in 16 slow lines, no mod errors). At the author's request it is released as
**v0.3.25** (commit `ffed9ac`): `release/CompanionAI_Pathfinder_v0.3.25.zip` (byte-identical to the test3 ZIP), notes
`release/CHANGELOG_v0.3.25.md`, `main` fast-forwarded; the author creates the GitHub release and tag `v0.3.25`. Open for
discussion: an archer in range but without sight of an enemy advances to range, not to a spot with sight (moved nowhere).

**User ability blacklist (2 November, task branch `claude/user-ability-blacklist`, PR into development,
[docs/TASK_USER_ABILITY_BLACKLIST.md](docs/TASK_USER_ABILITY_BLACKLIST.md), `docs/TACTICAL_AI.md` §89-90):**
"Abilities AI must not use", a shared list and one list per character (`Settings/AbilityBlocklist.cs`). There is one
policy, `Tactics/UserAbilityPolicy.cs`, with its pure rules in `Tactics/UserAbilityRules.cs`. It is checked before
appraisal and again in `Execute` / `CommandDispatcher`, and on every switch-on and restore route; predictions follow it.
The panels are in `UI/AbilityBlocklistUI.cs`. `Info.json` stays 0.3.25. Test ZIP
`AI-Control-FACC-v0.3.25-dev-blacklist1-a030c79-UMM-test.zip` (MVID `aab34bce-39d7-45c1-9346-551a9b5aa05a`); 311 tests.
Test 1 was played (turn-based, 6 fights: blocking, unblocking and the shared list work). The log review (task doc §2.9,
`docs/TACTICAL_AI.md` §90) cut repeated coverage, energy, Progressive and walk-inspection lines (normal log 15-28 %
smaller). Test ZIP `AI-Control-FACC-v0.3.25-dev-blacklist2-d1d930f-UMM-test.zip` (MVID
`d2dc1a06-0509-403e-87a3-3f21b580911a`). At the author's request the `[Reach]` rows went to the debug log, PR #17 was
merged into development (merge commit `f7134ad`) and released as **v0.3.26** (commit `e10513b`, `Info.json` 0.3.26):
`release/CompanionAI_Pathfinder_v0.3.26.zip` (MVID `24ddcf26-2006-43f5-ade7-b326abb2d62c`), notes
`release/CHANGELOG_v0.3.26.md`, `main` fast-forwarded; the author created the GitHub release and tag `v0.3.26`.
**v0.3.27 test 1 (3 November, `docs/TACTICAL_AI.md` §91, development only, not played):** the author saw turn-based
projectiles and animations sped up as the next turn began. The game runs the gap between turns at x3
(`TimeScaleBetweenTurns`), and the mod's `ForceToEnd` came before the game's own end-of-turn waits (projectiles landed,
idle pause). `TurnBasedPatches.ReadyToEndTurn` now applies those waits, capped at 4 s. `Info.json` 0.3.27; test ZIP
`AI-Control-FACC-v0.3.27-dev-test1-6508dc4-UMM-test.zip` (MVID `634a3871-881b-4a28-8752-09afa11207a3`).
**v0.3.27 test 2 (3 November, `docs/TACTICAL_AI.md` §92, development only, not played):** review of the author's v0.3.26
log (10 fights, TB and RT, no mod errors, blocklists held). No summons or pure buffs while only helpless enemies are left
(`TacticalEngine.PointlessAgainstHelpless`); a summon's worth, revive and heal count only the rounds its
`SummonedUnitBuff` has left (`TacticalEngine.RoundsInFight`). Includes test 1. Test ZIP
`AI-Control-FACC-v0.3.27-dev-test2-56d0c15-UMM-test.zip` (MVID `9bf1e011-c95c-45d2-b26e-d7f6548537d6`).
**v0.3.27 test 3 (3 November, `docs/TACTICAL_AI.md` §93, development only, not played, author-approved movement change):**
real time, a walk to a target is decided again when an enemy it did not count gets in its way - it engages the unit on
the way, or it joined the fight since and can intercept (`RealTimeTactics.ShouldReplanOnNewThreat`,
`TacticalEngine.CanInterceptWalk`). Includes tests 1-2. Test ZIP `AI-Control-FACC-v0.3.27-dev-test3-faf3bbf-UMM-test.zip` (MVID `7415e86a-471f-4b4f-bfca-4de5ec39513d`); 316 tests.
**v0.3.27 test 4 (3 November, `docs/TACTICAL_AI.md` §94, development only, not played):** test 3 played (13 fights, no
mod errors, tests 1-3 working). Real time, a control area is held back when an ally's committed walk meets it
(`CommittedApproachSafety.WalkMeetsCircle`) and at any height in the game's 100 m cylinder. Open for the author: a
healthy unit's walk into melee is not priced by the danger there (Camellia vs a Large Water Elemental). Test ZIP
`AI-Control-FACC-v0.3.27-dev-test4-375589a-UMM-test.zip` (MVID `b6d01331-72ba-49db-a9b4-a97bd40666c3`); 324 tests.
**v0.3.27 test 5 (3 November, `docs/TACTICAL_AI.md` §95, author-approved movement changes, not played):** healthy walks
priced by where they end less the party's gain (`TacticalEngine.StandingDropChance` / `HealthyExposure`,
`ValueModel.TargetChance`/`HitWeight`/`CombinedDrop`/`HealthyWalkExposure`); turn-based sight spots for advances; no
advance within full ranged reach. The author confirmed the own-area reluctance is intended; Ardub stays on hold. Test ZIP
`AI-Control-FACC-v0.3.27-dev-test5-ab559e8-UMM-test.zip` (MVID `8a238333-6e5f-46d3-a992-c50059651499`); 337 tests.
Test 5 played (`docs/TACTICAL_AI.md` §96: 13 fights, no mod errors, nobody down; all §91-95 rules seen working). Open for
the author: the "in range for 2 s" stuck rule restarts on a flickering gap (proposal in §96).
**v0.3.27 test 6 (4 November, `docs/TACTICAL_AI.md` §97, author-approved, not played):** that rule now counts in range by the
game's `IsUnitCloseEnough` or 0.3 m past the radius (`Tactics/ArrivalRule`). Test ZIP `AI-Control-FACC-v0.3.27-dev-test6-3f005b9-UMM-test.zip` (MVID `ae5d4340-5d51-454f-bec5-2888b74cbed6`); 342 tests. At the author's request it is
released as **v0.3.27** (commit `3f005b9`): `release/CompanionAI_Pathfinder_v0.3.27.zip` (byte-identical to the test6 ZIP),
notes `release/CHANGELOG_v0.3.27.md`, `main` fast-forwarded; the author creates the GitHub release and tag `v0.3.27`.
**v0.3.28 test 1 (4 November, `docs/TACTICAL_AI.md` §98, development only, not played):** review of the v0.3.27 logs. Advances
count the attack they give up (`ValueModel.AdvanceWorth`), every move counts the rise in the party's danger
(`TacticalEngine.PartyShift`, `ValueModel.LeavesAlliesCost`), attacks of opportunity count on-hit ability damage
(`CombatMath.OnHitAbilityDamage`, Shadows), real-time walks to the world origin end at once. `Info.json` 0.3.28; test ZIP
`AI-Control-FACC-v0.3.28-dev-test1-f00d679-UMM-test.zip` (MVID `3cb426c7-4b45-4d06-b226-96a2b089931a`); 350 tests.
**v0.3.28 test 2 (4 November, `docs/TACTICAL_AI.md` §99, not played):** the leaves-allies cost is the whole party's net rise
(`ValueModel.LeavesAlliesCost(shift, ownFall)`, `TacticalEngine.OwnFall`), so casters and archers may step away from a
melee a front-liner holds. Test ZIP `AI-Control-FACC-v0.3.28-dev-test2-5c87b6d-UMM-test.zip` (MVID `cd797bdb-e0cc-4fd7-8ffc-54e02040954f`); 354 tests.
**v0.3.28 test 3 (4 November, `docs/TACTICAL_AI.md` §100, not played):** hit chances count concealment and mirror images
(`CombatMath.LandsPastDefences`), drop chances temporary HP (`HpBeforeDown`; `RealHpBeforeDown` for heals, caution, burn),
prone +4 in the survival model. Test ZIP `AI-Control-FACC-v0.3.28-dev-test3-2cb47db-UMM-test.zip` (MVID `09bc562d-a4dc-4c51-a8ff-ebc2e6f1cf3a`); 359 tests.
**v0.3.28 test 4 (5 November, `docs/TACTICAL_AI.md` §101, not played):** test 3 played (18 fights, Orvath as Kinetic
Sharpshooter). The quiver is made out of combat (`TacticalEngine.QuiverToPrepare` from `ApplyPermanentBuffsOutOfCombat`),
burn's Overflow offset counts only when a blast is the attack (`BlastShortfall`), unread buffs that cost burn are worth 0
(`BuffQuality`). Test ZIP `AI-Control-FACC-v0.3.28-dev-test4-e620e71-UMM-test.zip` (MVID `213bf9ff-50b8-4c91-acaf-8c370286a712`); 359 tests.
**v0.3.28 test 5 (5 November, `docs/TACTICAL_AI.md` §102, not played):** Codex PR #18 (quiver preparation in
exploration - native AI skips out-of-combat groups; queued-order guard; ricochet jumps at their delivery layer's half
damage; half packets out of blast calibration) and PR #19 (catalogue "Hide blocked") reviewed and merged (merge commits
`fc2b147`, `6841cce`); follow-up `96ba9cc` keeps a quiver refusal while its signature is unchanged (10 s backstop). Test ZIP
`AI-Control-FACC-v0.3.28-dev-test5-96ba9cc-UMM-test.zip` (MVID `fade5da1-cea0-4048-a243-54873850148f`); 388 tests.
**v0.3.28 test 6 (5 November, `docs/TACTICAL_AI.md` §103, author-requested, not played):** kineticist/archer engine scaled
by the game's own numbers - planned-shot quiver reserve and in-combat refill (`PlannedShotBurn`, `TryQuiverRefill`), arrow
and burn prices shared with the infusion planner (`KineticPriceBook`), Overflow in expected damage, critical hits for
attack-roll abilities (`AbilityCritFactor`, `ValueModel.CritFactor`), Deadly Aim on archer blasts (`ChooseBlastAttackModes`),
Enveloping Winds / Shroud of Water increments (`TacticalEngine.RankTalents`), context counters in slow lines. Test ZIP
`AI-Control-FACC-v0.3.28-dev-test6-2765e04-UMM-test.zip` (MVID `3402358f-f2ee-433f-af3a-fdc6c4755e8d`); 406 tests.
**v0.3.28 test 7 (6 November, `docs/TACTICAL_AI.md` §104, author-approved movement change, not played):** test 6 played (14
fights, no mod errors). A melee enemy's danger at a spot counts its full attack only in reach or a step of it, else one attack
if its area lets it go (`ValueModel.StrikeShare`, `TacticalEngine.Reach.cs`, `AoeDangerAnalyzer.HeldInsideChance`); a step out
of an unanswered reach at any HP (`AddOutOfReachCandidate`); every out-of-combat quiver refusal is named. Test ZIP
`AI-Control-FACC-v0.3.28-dev-test7-f180d9b-UMM-test.zip` (MVID `c786e760-c342-4fc4-9e1e-f130a36152f8`); 413 tests.
**v0.3.28 test 8 (6 November, `docs/TACTICAL_AI.md` §105, not played):** test 7 played (13 fights, no mod errors). The step
out of reach only when the unit's own weapon reaches no enemy (not for ranged-weapon units); the real-time back-off scaled
by its stop chance; Orvath's kineticist part names no quiver buff (the game then never takes burn from arrows), and
`NoQuiverBuffNote` names the feature that set it up. Test ZIP `AI-Control-FACC-v0.3.28-dev-test8-94d0835-UMM-test.zip`
(MVID `54337c75-a6f2-424d-b5bc-769ebc4849f5`); 413 tests.
**v0.3.28 test 9 (6 November, `docs/TACTICAL_AI.md` §106, not played):** test 8 played (13 fights, no mod errors). Orvath's
kineticist part is `DarkElementalistBurnFeature` (likely Isekai's Dark Elementalist Legacy: no quiver, Intelligence); the
author confirmed arrows never pay. Note once a session with the main stat. Test ZIP
`AI-Control-FACC-v0.3.28-dev-test9-18fba94-UMM-test.zip` (MVID `fd940d66-16b4-4fad-8d02-d5e97e6e9be0`); 413 tests.
**v0.3.28 test 10 (6 November, `docs/TACTICAL_AI.md` §107, author-requested, not played):** Dark Elementalist. Soul Power by
the game's rules (`TacticalEngine.SoulPower.cs`: corpse character levels through the ability's filter, burn price, Overflow
rank under its cap) and harvested after fights (`RealTimeController.TryHarvestSouls`); kineticist main-stat buffs valued for
blast damage. Test ZIP `AI-Control-FACC-v0.3.28-dev-test10-f5faa25-UMM-test.zip` (MVID `eeb140e7-6d84-43b2-bbcc-a0e1cf8e0529`); 423 tests.
**v0.3.28 test 11 (6 November, `docs/TACTICAL_AI.md` §108, not played):** test 10 log reviewed (12 fights, no mod errors).
The after-fight Soul Power harvest waits until the party has stood still for 2 s (`RealTimeController.PartyOnTheMove`: 14 of
21 casts were cut off by the walk); damage estimates share one context per ability and frame when the target cannot change
the numbers (`CombatMath.DamageContext` / `ContextIsTargetFree`, ProfileAudit DAMAGE-CONTEXT); log trimmed (Coverage diffs,
energy rows per session, one turn-start line, revive notes merged, Progressive by spell). Test ZIP `AI-Control-FACC-v0.3.28-dev-test11-4d6036e-UMM-test.zip` (MVID
`7aa18b9e-c844-477e-854b-d86589fb09f7`); 423 tests.
**v0.3.28 test 12 (6 November, `docs/TACTICAL_AI.md` §109, author-requested, not played):** test 11 log reviewed (16 fights,
no mod errors). Cones that need a walk walk to the priced spot first (`MoveFirst`), cones that catch allies are also tried
turned past them (`TacticalEngine.ConeAim.cs`), friendly fire beyond the enemies' worth counts against the option; cone
delivery judged when its execution ends (`ObserverRules.AwaitDelivery`); weapon ability damage in enemy threat
(`ValueModel.OnHitAbilityOffence`, Shadows); after-fight Soul Power waits for 3 s without orders (the author's Dispel Magic
casts replaced it). Test ZIP `AI-Control-FACC-v0.3.28-dev-test12-03d3f6a-UMM-test.zip` (MVID
`fc4b18a2-a859-4465-b8dd-c73ea85a0633`); 434 tests.
**v0.3.28 test 13 (6 November, `docs/TACTICAL_AI.md` §110, author-requested, not played):** ability damage and negative levels
by the game's rules (`CombatMath.Drain.cs`, `ValueModel.Drain.cs`): a score below 1 kills, as many negative levels as the
character level kill; worth the share of that life bar of the hit points left (5 HP per level first), immunities read; in
every damage estimate (party spells, friendly fire, enemy threat), poisons of any score; ProfileAudit DRAIN. Test ZIP
`AI-Control-FACC-v0.3.28-dev-test13-1b74af6-UMM-test.zip` (MVID `76028a95-9ba1-43ce-8250-abb0aecaa5ef`); 444 tests.
**v0.3.28 test 14 (6 November, `docs/TACTICAL_AI.md` §111, author-requested, not played):** what weapon hits and swarm rounds
carry beyond hit points, read from the game's actions (`CombatMath.Afflictions.cs`): on-hit triggers of facts and weapon
enchantments (giant spider poison, Shadow touch), swarm rounds (Mandragora swarm: 5d6, nausea, poison), saves, poison doses,
control, immunities; in enemy threat, walk/revive prices and the party's poisoned weapons; ProfileAudit AFFLICTION. Test ZIP
`AI-Control-FACC-v0.3.28-dev-test14-eb73349-UMM-test.zip` (MVID `a3caf884-f21e-49e4-aa34-52758a29f726`); 450 tests.
**v0.3.28 test 15 (6 November, `docs/TACTICAL_AI.md` §112, author-approved movement change, not played):** a swarm's space as
danger of the spot by the game's swarm rules (`CombatMath.Swarms.cs`, `TacticalEngine.Swarms.cs`, `ValueModel.Swarm.cs`):
struck in full inside it (inner-circle rule where the strike waits for it), half within its walk; walk prices, approach spots
clear of a swarm, a step or move out (`AddOutOfSwarmCandidate`); harmful-area analyzer skips swarm spaces; ProfileAudit
SWARM-AREA. Test ZIP `AI-Control-FACC-v0.3.28-dev-test15-2415cf8-UMM-test.zip` (MVID `794e6682-f29b-426a-a940-680ed813c83c`); 457 tests. Test 15 played
(14 fights, no mod errors). At the author's request it is released as **v0.3.28** (commit `2415cf8`):
`release/CompanionAI_Pathfinder_v0.3.28.zip` (byte-identical to the test15 ZIP), notes `release/CHANGELOG_v0.3.28.md`, `main`
fast-forwarded; the author creates the GitHub release and tag `v0.3.28`.
**v0.3.29 test 1 (6 November, `docs/TACTICAL_AI.md` §113, development only, not played):** test-15 log review. The pre-issue path
check keeps plain moves when its budget runs out and notes "longer than one move" refusals (`SelectedApproachFilter` keepUnchecked,
`TurnMemory.NoteTooFar`: Lann's unused turn); a round's hits read together, control once over (`Affliction.Repeated`,
`ValueModel.RepeatedControl`: Ghoul Creeper). `Info.json` 0.3.29. Proposal for the author in §113: real-time re-decision when
engaged on a walk (R1), 2 s blocked walks (R2), drop chance from enemies' real hits (R3). Test ZIP
`AI-Control-FACC-v0.3.29-dev-test1-d752fa8-UMM-test.zip` (MVID `67c56db1-76e4-40bb-bfac-83ed603be6b6`); 461 tests.
**v0.3.29 test 2 (6 November, `docs/TACTICAL_AI.md` §114, author-approved movement changes, not played):** R1 real time decides again
when an engager it can hit back stands near the walk's destination (`TacticalEngine.CanHitBack`); R2 a walk on the spot for 2 s ends
as blocked (`RealTimeController.BLOCKED_WALK_SECONDS`); R3 drop chances take the larger of the exponential and melee enemies' real
hits (`RiskMath.FullAttackDropChance`, `TacticalEngine.Strikes.cs`: `StandingDropChance`, `SpotDrop`), reads once a round per pair.
Test ZIP `AI-Control-FACC-v0.3.29-dev-test2-6221118-UMM-test.zip` (MVID `add726f5-13c2-4f48-ab1e-cf3a3c819b70`); 466 tests.
Test 2 played (`docs/TACTICAL_AI.md` §115: 12 fights, no mod errors, R1-R3 seen working, R3 at most 6.4 ms a slow decision; every
extra interrupted command traced, no loop). **v0.3.29 test 3 (6 November, §115, not played):** a real-time walk to touch range ends
once the game's reach test reaches the touch's target (`RealTimeTactics.ShouldEndArrivedWalk` since test 4; Lann circled Camellia 7.6 s).
Test ZIP `AI-Control-FACC-v0.3.29-dev-test3-71959e6-UMM-test.zip` (MVID `a5d5b19e-da23-4aaf-bd93-d6037457f66d`); 466 tests.
**v0.3.29 test 4 (6 November, §116, author-approved body-blocking fix 1, not played):** real time, an approach that ends in body contact
takes a free side of its target when another body, or a companion's walk end (`SpotRules.WalkEnd`), takes its straight spot
(`TacticalEngine.Spots.cs`, `PlanApproach`); the walk ends once the enemy is in reach (`RealTimeTactics.ShouldEndArrivedWalk`); no free
side: no weapon walk or advance to it. Turn-based unchanged. Test ZIP `AI-Control-FACC-v0.3.29-dev-test4-090023a-UMM-test.zip`
(MVID `fd38dde0-9b19-4290-bba5-1606b45874f0`); 474 tests.
Test 4 played (`docs/TACTICAL_AI.md` §117: Orvath's forward cones and close-range shots are his stats - armour class 29, enemies hit
8 of 84; Auto Selective is read). **v0.3.29 test 5 (7 November, §117, not played):** real time, allies whose walk passes into a cone
or burst during the cast count as caught (`TacticalEngine.AlliesWalkingInto`, `ProjectileCone.Query.ReachesAt`: Fan of Flames hit
Seelah and Camellia); a walk the game counts as arrived ends after 0.4 s (`RealTimeTactics.WalkArrived`: circling next to enemies).
Test ZIP
`AI-Control-FACC-v0.3.29-dev-test5-9a75cbd-UMM-test.zip` (MVID `9e40ed9f-390a-415c-ad46-b46af6b505ab`); 477 tests.
**v0.3.29 test 6 (7 November, §118, author-approved, not played):** real time, two walks in a row that got nowhere from one spot box
the unit in there for 6 s - no option that walks first is offered except leaving an area or a swarm (`CombatMemory.NoteStuckWalk` /
`BoxedIn`, `SpotRules.StuckInARow`, `TacticalEngine.BoxedInWalk`). Test ZIP `AI-Control-FACC-v0.3.29-dev-test6-1bb3f79-UMM-test.zip`
(MVID `a0e8296b-b0d4-4019-99ae-a54bdd385ac7`); 479 tests.
Test 6 played (§119: 12 fights, no mod errors, no stuck walks in real time). **v0.3.29 test 7 (7 November, §119, not played):**
"Open door to X" in both modes for a closed, unlocked door on the way to an enemy out of sight (`TacticalEngine.Doors.cs`,
`DoorRules`, `CandidateKind.Interact`, the game's `UnitInteractWithObject`; real time drops an opening whose door is open,
`ShouldDropOpenedDoor`); `AoeDangerAnalyzer.IsPositionValid` traces from the nearest navmesh point (an off-mesh start read every
spot as blocked). Test ZIP `AI-Control-FACC-v0.3.29-dev-test7-ed5cb78-UMM-test.zip` (MVID `ebc7a0d7-4e64-4104-8b08-0332bac68e76`); 485 tests.
Test 7 played (§120: no door was ever offered - `IsRevealed` is never set for doors with the fog on). **v0.3.29 test 8 (7 November,
§120, not played):** doors by the game's visibility rule, passed locks (`InteractionRestrictionPart.IsPassed`), enemies out of sight or
reach, detour test (`DoorRules.OnTheWay`), no walk when at the door, `[Door]` reasons; turn-based walks of 0.0 m refused
(`CommandDispatcher.TryPrepare`); real-time stop-start dead-end walks end (`IsCommandStuck` clocks). Test ZIP
`AI-Control-FACC-v0.3.29-dev-test8-d2ed55e-UMM-test.zip` (MVID `2cfa0361-1e09-4a1b-93a9-ee9997377af8`); 489 tests.
Test 8 played (§121: the door is `NotInCombat` - the game forbids opening it in a fight). At the author's request the door work is
taken out (commit `811fae2`) and saved for a later version (`docs/future/door-opening.patch` + `DOOR_OPENING.md`); kept: walkable-spot
trace, turn-based walks that go nowhere, real-time stuck-walk clocks. Released as **v0.3.29**: `release/CompanionAI_Pathfinder_v0.3.29.zip`
(MVID `b1371c3f-bb68-4d1c-b881-9753eb663685`, not byte-identical to a played build: test 8 minus the door work), notes
`release/CHANGELOG_v0.3.29.md`, `main` fast-forwarded; the author creates the GitHub release and tag `v0.3.29`; 479 tests.
**v0.3.30 test 1 (7 November, `docs/TACTICAL_AI.md` §122, not played):** bug report reproduced by the author - Lann cast Profane
Invocation (Demonic Overlord, free action, permanent `UniqueBuff`) 25 times a fight. A unique buff held by another ally costs that
ally's loss (`UniqueBuffHeldElsewhere`, `AppraiseHeldBuff`: `Increment` skips the buff's own `Modifier.Source`), needs a clearly better
recipient (`ValueModel.BuffSwitchGain`, 1.25) and moves at most once a round; sibling-buff switches judged by both appraisals
(`ReplacedWorth`). `Info.json` 0.3.30. Test ZIP `AI-Control-FACC-v0.3.30-dev-test1-cf080c6-UMM-test.zip` (MVID
`fdee1c86-2d4f-4ddb-8256-621cec66b451`); 486 tests.
**v0.3.30 test 2 (8 November, `docs/TACTICAL_AI.md` §123, author-requested, not played):** all six ability scores valued by what they
feed on the recipient, from the game's links (`TacticalEngine.StatLinks.cs`: attack/damage stat by the rules, save/AC/HP base stats,
`DerivativeStatBonus` and stat-ranked `AddContextStatBonus` facts; `ValueModel.StatUse`), mental stats modelled for every recipient;
ProfileAudit STAT-LINKS. Test ZIP `AI-Control-FACC-v0.3.30-dev-test2-827841f-UMM-test.zip` (MVID
`05361eac-e11f-4c14-a363-3355229923c2`); 493 tests.
**v0.3.30 competence2 (8 November, `docs/TACTICAL_AI.md` §124-125, not played):** PR #20 (competence programme foundation)
merged with review fixes - see the competence handover at the top. Test ZIP
`AI-Control-FACC-v0.3.30-dev-competence2-7e89a16-UMM-test.zip` (MVID `0ed42ac5-d9ce-4f88-a339-388028b036c3`); 522 tests.

Historical handovers, superseded by the one above: [v0.3.22 release preparation](docs/RELEASE_0_3_22_HANDOVER.md)
(27 October, PR #6–#12), [released-log review](docs/RELEASE_0322_LOG_REVIEW.md) (PR #13),
[second-log review](docs/RELEASE_0322_LOG2_REVIEW.md) (PR #14), [fixes-playtest review](docs/RELEASE_0322_FIXES_REVIEW.md)
(PR #15), the [v0.3.22 withdrawal postmortem](docs/V0_3_22_POSTMORTEM.md) and the
[PR #4 integration handover](docs/INTEGRATION_HANDOVER.md) (work later withdrawn).

## Project Overview
- **Mod name**: AI Control - Fully Autonomous Combat Companions (author MonsieurAndroid); UMM `Id` stays `CompanionAI_Pathfinder` (settings path, installs)
- **Game**: Pathfinder: Wrath of the Righteous
- **Type**: Unity Mod Manager mod (C# .NET 4.8)
- **Purpose**: Complete replacement of companion AI with tactical decision-making system
- **Version**: tracked in `Info.json` (0.3.0: step-wise turn-based tactical engine in `Tactics/`; 0.3.1: per-target
  Conditional branches, next-attack buffs, cleanses, time-based command wait; 0.3.2: buff quality from components,
  harmful-only cleanses, mode switches; 0.3.3: Soul Power/burn, day-long markers, summon after-spawn effects, toggles,
  game stacking rule; compatibility notes in README; 0.3.4: enchant carrier buffs, partial-stacking, dispel targets; 0.3.5: out-of-combat buff routine
  judged by AbilityProfile, cannot-act turns, no utility fallback for removal abilities; 0.3.6: self-dispelling buffs, casting in melee; 0.3.7: Progressive spell policy, area-effect spell analysis; 0.3.8: enchantments seen as active,
  own-attack marks on the attack target, area target filters, surprise round / no-move turns; 0.3.9: ranged 5-foot-step full attacks,
  danger-aware healing and downed allies, Coup de Grace by the rules, sleeping enemies; 0.3.10: 5-foot-step movement rule,
  path-end reach check, danger-aware withdraw; 0.3.11: only companions/pets revivable, summons
  worth half, no advance towards unreachable enemies; 0.3.12: harmful-content buffs never valued as helpful, CC
  immunity for combined flags, real-time friendly-fire veto / queue gates / classifier cache key, visible analysis
  failures; audit in `docs/PROGRESSIVE_SPELLS.md` §A; 0.3.13: attacks of opportunity priced into moves (game's own
  disengage checks), no second copy of an own active control area, Empower/Maximize (incl. auto metamagic) in
  estimates, AddContextStatBonus stacking, optional pet belt items (`GameInterface/PetItemsPatch`); 0.3.14: real time on the
  tactical engine, status-aware actions (`Tactics/ActionRules`, `UnitStatus`; `docs/STATUS_AWARE_PLAN.md`), path attacks of
  opportunity and drop chance (`Tactics/RiskMath`), area exits, SR/energy/observed damage (`Tactics/DamageObserver`); 0.3.15:
  only action-bar abilities (`AbilityDiscovery`), silent cast failures and touch charges (`Tactics/CastOutcome`), TB touch
  spells walk into reach first, per-round area cost, per-target control duration, observed saves (`Tactics/SaveObserver`);
  0.3.16: touch reach by the game's `MechanicsDistance`, provoking casts priced by the defensive-casting rule
  (`CastProvokeRisk`), unfiltered grid-cell lookups (`BattlefieldGrid.CellAt`); 0.3.17: approach around harmful areas,
  Hit Dice conditions with shared values, per-target "reached nothing" safety net, diagnostics (`FightStats`,
  `CoverageReport`), v0.2 planner removed; 0.3.18: calibration - shared formulas in `Tactics/ValueModel` (unit-tested),
  expected rounds and enemy danger from the fight's observed damage, kill-chance bonus, continuous heal curve, ranged
  attack rolls provoke (`Tactics/ProvokeObserver`, `ProvokeRisk`), real-time area escape through the engine; one
  resistance path - spell immunity in the game's order (`Tactics/ResistanceRule`), buff immunity (`CombatMath.BuffImmune`),
  spell attack rolls by the game's attack rule; real-time cannot-act rules from the game code; active abilities AA-1 -
  damage/heals by the game's preview (`IDealDamageProvider`), kill actions, conversions, point-target summons, toggle-buff
  effects, caster-provoke detection, no "utility" fallback, more read-only conditions; AA-2 - command-activated toggles
  (performances, Rage family, spirits) as candidates, `Tactics/TacticalEngine.Toggles.cs`; AA-3 - attack modes (Power
  Attack, Deadly Aim, Fighting Defensively, Combat Expertise) chosen per attack and Mobility per move,
  `Tactics/TacticalEngine.Modes.cs`; AA-4 - Demoralize by the Intimidate rule, combat maneuvers by CMB vs CMD, Vital
  Strike / Cleave / Pounce, Persistent and Extend, chain targets, `Tactics/TacticalEngine.Martial.cs`; play-test fixes
  test10-11 in `docs/TACTICAL_AI.md` §33-34: real-time path check on the game's funnelled walk, pending ally areas,
  Sleep Hit Dice pool, summons with ally-catching areas; released as 0.3.18 with `release/CHANGELOG_v0.3.18.md`; 0.3.19: buffs valued by what they do for the recipient
  (`Analysis/BuffParts`, `Tactics/TacticalEngine.Buffs.cs`, `docs/SHORT_BUFFS_AUDIT.md`), area toggles read by their
  actions with per-recipient conditions (`TacticalEngine.Toggles.cs`), class competence audit
  (`docs/CLASS_COMPETENCE_AUDIT.md`: Fervor conversions, Cackle/Chant extensions, rage parts), kineticist burn / quiver /
  Overflow (`TacticalEngine.Kinetic.cs`), revives, next-spell boosters (`TacticalEngine.Boosters.cs`), caster-side marks
  (`TacticalEngine.Marks.cs`), RT no-sight memory, one toggle owner + self-buff switch (`GameInterface/ModInterop`, Buff It
  2 The Limit), log trimming; `release/CHANGELOG_v0.3.19.md`; 0.3.20: weapon damage by the game's rule, enemy threat
  by offence (`[Threat]`), one-move walk-then-act, casts counted when performed (`Tactics/CastLedger`), real-time quiet
  waits (`Tactics/RealTimePacing`), attacks of opportunity avoided by approach spot and counted on the real walk
  (`PlanApproach`, `CommandDispatcher.Run` pathAttackers, `TurnMemory.PathAttackers`), live-code audit
  `docs/LIVE_CODE_AUDIT.md`; `release/CHANGELOG_v0.3.20.md`; 0.3.21: attack modes in the
  base game's trigger form, Wrath Tactics first-refusal handoff in real time (`GameInterface/ModInterop`,
  `Tactics/WrathHandoff`), coverage and verification audit with the real readers (`tools/ProfileAudit`,
  `docs/COVERAGE_AUDIT.md`), auras on buffs (`Tactics/TacticalEngine.Auras.cs`), immunities, sight, claws, pits,
  regeneration, spellcasting boosts, on-hit effects, Quicken / Extend boosters, shadow spells, caster-only granted facts - `docs/TACTICAL_AI.md` §53-64; `release/CHANGELOG_v0.3.21.md`)

## Reference Resources
- **BubblePrints input handover (15 November 2026, Codex; author-authorized repository storage):**
  [tools/BubblePrintsData/README.md](tools/BubblePrintsData/README.md) contains five versioned stock-data archives,
  checksums, an offline assembler and headless reader source. Run `python3 tools/BubblePrintsData/assemble_database.py`
  to reconstruct the exact 2.7.0x database under the ignored `.local` directory, without external downloads.
  The original viewer ZIP did **not** include the database; Codex downloaded it separately. This scoped stock-input
  exception does not permit committing private/live dumps, game/viewer DLLs or decompiles. Reader libraries still
  come from the supplied viewer ZIP; stock data does not prove runtime mod patches. The author requested this
  handover be integrated into development. It changes no game code/manifest; no mod DLL or UMM package was rebuilt
  or replaced. See its checks receipt for assembly/reader verification, not new mod-build or gameplay acceptance.
- **Rogue Trader CompanionAI v3.5.7**: `C:\Users\veria\Downloads\CompanionAI_v3-master - v3.5.7`
  - Reference for complete AI replacement architecture
  - Key patterns: Behaviour Tree replacement, single decision point, no basic attacks
- **Pathfinder Decompiled Source**: `c:\Users\veria\Downloads\pathfinder_decompile`
  - Game internals reference for accurate API usage

## Build Command
```powershell
.\Build.ps1 [-GamePath "<...\Pathfinder Second Adventure>"] [-Deploy]
# or: dotnet build CompanionAI_Pathfinder.csproj -c Release -p:GamePath="<game folder>"
```
Output: `bin\Release\` and `dist\CompanionAI_Pathfinder_v<version>.zip`. `-Deploy` / `-p:DeployToGame=true` copies to `<game>\Mods\CompanionAI_Pathfinder\`.

## Turn-based command rule (v0.2.119)
Issue every unit command through `GameInterface/CommandDispatcher.Run`. In turn-based combat the game's
`UnitCommand.TickApproaching` only follows `ForcedPath` (else the mouse-cursor prediction path, else interrupt);
the dispatcher computes it like native AI (`AiAction.ScorePath`). Never write `TurnController.Status`.
See `docs/INVESTIGATION.md`.

## Architecture

### AI Decision Pipeline (v0.3.0)
```
Turn-based:  TurnController.Tick (Harmony postfix, CustomBrainPatch.TurnControllerTick_Postfix)
                 → Core/TurnBased/TurnBasedController   (waits for commands, per-turn memory)
                 → Tactics/TacticalEngine.Decide        (ONE best next action from current action economy)
                 → Tactics/TacticalEngine.Execute → GameInterface/CommandDispatcher.Run
Real-time:   AiBrainController.TickBrain (prefix) → RealTimeController (waits: commands, stuck check, AoE escape)
                 → Tactics/RealTimeTactics.Act → TacticalEngine.Decide / Execute   (v0.3.14: same engine as turn-based;
                   actions left = the game's real-time cooldowns, round = 6 s, no 5-foot step; old UnifiedDecisionEngine
                   only with the "Real time: use the old decision engine" setting)
```
Design and value formulas: `docs/TACTICAL_AI.md`. Root-cause evidence: `docs/INVESTIGATION.md`.

### Key Folders
- **Tactics/**: the live decision engine (both modes) - `TacticalEngine`, `AbilityProfile`, `AbilityDiscovery`,
  `CombatMath`, `RiskMath`, `ValueModel` (shared value formulas, unit-tested), `ResistanceRule` (spell resistance order,
  unit-tested), `CombatMemory`, `TacticalDriver` (RT),
  observers (`CastOutcome`, `DamageObserver`, `SaveObserver`, `ProvokeObserver`), per-fight numbers (`FightStats`: also
  the calibration input), `CoverageReport`, `ConsumablePolicy`, `SpellTierPolicy` / `ProgressiveGate`
- **Core/TurnBased/**: `TurnBasedController` (TB driver); **Core/**: `ExecutionResult`; legacy-engine state
  (`TurnState`, `TurnPlan`, `PlannedAction`, `TeamBlackboard`, `CombatDataCache`, `FailedAbilityTracker`)
- **Analysis/**: shared analyzers used by the engine (`AbilityClassifier` blacklist, `BuffEffectAnalyzer`,
  `BuffStackingAnalyzer`, `AoeDangerAnalyzer`, `CombatRulesAnalyzer`, `HitChanceCalculator`, `BattlefieldGrid`) and the
  legacy engine's (`SituationAnalyzer`, `TargetScorer`, `Situation`)
- **Core/DecisionEngine/, Scoring/, Planning/MovementPlanner**: the old real-time engine (setting "Real time: use the old
  decision engine"). Not maintained: to be removed entirely; do not adapt it or spend changes on it. The v0.2 planner
  (TurnPlanner, role plans, TurnOrchestrator, ActionExecutor) was removed in 0.3.17.
- **tools/**: offline verification - `ProfileAudit` (the mod's readers on blueprint dumps), `RunnableStubs`, `SigDump`,
  `RefStubGen`, `CoverageAudit` (`tools/README.md`); none of them ship with the mod.
- **docs/archive/**: superseded documents (the v0.2.57 architecture, early research reports).
- **Abstraction/**: `PathfinderCombatStateAdapter` (mode display in the UI)
- **GameInterface/**: Harmony patches and command issuing - `CustomBrainPatch`, `RealTimeController`, `CommandDispatcher`
- **Settings/**: Per-character configuration with bilingual support (EN/KR)
- **UI/**: Unity Mod Manager GUI

### Action Economy
Pathfinder action types: Standard (1), Move (1), Swift (variable), Full-Round (2)
Read from the game at each decision (`TacticalContext`: `HasStandardAction` etc.; real time: the game's cooldowns);
`TurnState` is the old real-time engine's only.

### Critical Design Decisions
- **GUID-based ability identification** for multilingual compatibility (not string matching)
- **Type-based blacklisting** for abilities (not name-based)
- **Adapter pattern** isolates game API dependencies
- **Dual combat modes**: Turn-based and real-time handled separately

## Critical Architecture Notes

### Weapon attacks (v0.3.0 - supersedes the v0.2.21 "no basic attack" rule)
In Pathfinder a weapon attack IS the `UnitAttack` command, and it is the main damage source of martial characters,
archers and animal companions. Excluding it (v0.2.21 rule, borrowed from Rogue Trader) left Lann with `Attacks=0` and
made him retreat every turn (see docs/TACTICAL_AI.md §1). The turn-based engine values weapon attacks with the game's
own rules and competes them against spells by expected value. Always set `ForceFullAttack` or `IsSingleAttack`:
otherwise the UI smart-cursor mode decides.

### RT v3.5.7 vs PF Differences
| Aspect | Rogue Trader v3.5.7 | Pathfinder Current |
|--------|---------------------|-------------------|
| AI Entry | `PartUnitBrain.Tick()` Behaviour Tree replacement | `AiBrainController.TickBrain()` prefix (real time), `TurnController.Tick` postfix (turn-based) |
| Basic Attacks | Completely excluded from TurnPlanner | Weapon attacks valued by the game's rules (v0.3.0) |
| Decision Point | Single `CompanionAIDecisionNode` | One: `TacticalEngine.Decide` for both modes (v0.3.14) |
| Command Queue | `Commands.Empty` strict check | No decision while any command or queued command is pending (`GameAPI.HasPendingCommands`) |

Live-code map, findings register and audit backlog: `docs/LIVE_CODE_AUDIT.md`.

### Pathfinder AI System (from decompile)
- `AiBrainController.Tick()` → `ForceTick()` → `TickBrain()` (static)
- `TickBrain()` checks `NextCommandTime` before issuing commands
- `UnitCommands.Run()` replaces same-type commands (Standard replaces Standard)
- Action cooldown: `CombatState.HasCooldownForCommand(ActionType)`

## Development Guidelines

### Code Changes
- Update `Info.json` version before release
- Mark changes with version comments: `// ★ v0.2.22:`
- Entry point: `Main.cs` → `Main.Load()` initializes Harmony patches

### Harmony Patching
- Turn-based entry: `TurnBasedPatches.TurnControllerTick_Postfix` (postfix on `TurnController.Tick`)
- Real-time entry: `CustomBrainPatch` prefixes `AiBrainController.TickBrain`
- EventBus (no Harmony): `CommandTrace` (command outcome log), `MountPathHandler` (mount ForcedPath)
- Uses manual patching via `AccessTools` for private methods
- Harmony instance ID: `CompanionAI_Pathfinder`

### Changing turn-based behaviour
- Add or retune options in `Tactics/TacticalEngine.cs` (candidates and `ScoreAbility`); keep values in
  damage-equivalent points so options stay comparable. Shared formulas (worth per round, horizons, kill bonus, heal
  curve, fight-length and danger calibration) live in `Tactics/ValueModel.cs` with unit tests: change them there.
- What an ability does: `Tactics/AbilityProfile.cs`. Game-rule math: `Tactics/CombatMath.cs`.
- Item-use policy must go through `Tactics/ConsumablePolicy.IsAllowed` (shared by turn-based and real-time).
- Turn/combat facts that must survive re-decisions live in `Tactics/CombatMemory.cs`, never in a candidate.
- Effects behind a `Conditional` (Cure/Inflict, Lay On Hands) must be checked per target with
  `AbilityProfile.AppliesTo` / `BuffAppliesTo`; never count both branches for everyone.
- A buff's worth comes from what it does (`Analysis/BuffEffectAnalyzer`, shared with real-time) and whether it stacks
  on the recipient (`BuffStackingAnalyzer.Analyze`, descriptor-based); only harmful buffs count as something to cleanse.
- Use the game's own rule functions (e.g. `ModifierDescriptorHelper.IsStackable`), never copied tables: other mods
  (Tabletop Tweaks) patch those functions, and the mod must stay independent of them.
- Spell-resource policy (Progressive): pure rule in `Tactics/SpellTierPolicy.cs` (unit-tested, `dotnet test
  tests/CompanionAI.Tests`), game side in `Tactics/ProgressiveGate.cs`, shared by both engines; slot level always from
  `Spellbook.GetSpellLevel`, never a stored counter. Design: `docs/PROGRESSIVE_SPELLS.md`.
- A value of 0 for a verifiable effect (damage, heal, cleanse) means "not useful now": never let it fall through to
  the once-per-combat fallback for unverifiable effects. Consumables never use that fallback.

### Claude Behavior Guidelines
- **Investigate before concluding** - use decompiled sources when game behavior is unclear
- **Complete solutions** - avoid partial fixes or "implement later" suggestions
- **Refactor proactively** - eliminate duplication when found
- **Side effects matter** - consider impact across turn-based and real-time modes
