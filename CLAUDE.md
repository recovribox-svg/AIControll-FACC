# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Collaboration workflow

Read [CONTRIBUTING.md](CONTRIBUTING.md) for shared branch ownership, Codex pull requests, handovers, verification, and releases.
Codex works on task branches and targets pull requests at `claude/dreamy-pasteur-b9h3rn`; Claude reviews and integrates them there.
Keep `main` for author-authorized releases. Read [AGENTS.md](AGENTS.md) when coordinating agent work.

**C06 playtest / Psionics cost scope (9 November 2026, Codex; review pending):** `codex/c06-psionics-cost-scope` starts at played C06 / PR #22 `ab810b3`, including development `1f58150` / `3443d94`. Read [docs/C06_PSIONICS_COST_SCOPE.md](docs/C06_PSIONICS_COST_SCOPE.md) and §131. All sampled paid performances had the old generic patch fallback; inspected Psionics 0.2.3 globally postfixes cost but only changes GUIDs in `MindBladeEnchantments.AbilityToggleCosts`. The exact module/member is recognized; the live map is checked each quote, mapped enchantments remain fallback, and unknown hooks now identify their method/type/module. Snapshot lifetime remains one decision. No C07/C08, movement, ownership or dispatch change; version 0.3.30.

Verification receipt: [docs/competence/C06_PSIONICS_SCOPE_CHECKS.json](docs/competence/C06_PSIONICS_SCOPE_CHECKS.json). Exact checked source and DLL identity are recorded there; a later receipt-only commit does not replace that tested DLL.

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

**Current handover (29 October 2026, Claude): read [docs/INTEGRATION_0322_FOLLOWUPS.md](docs/INTEGRATION_0322_FOLLOWUPS.md)
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
