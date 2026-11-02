# Live-code audit, v0.3.25 (test1 playtest → test2)

2 November 2026, Claude, on `claude/dreamy-pasteur-b9h3rn`. Brief: the author's live-audit master prompt, started
from the v0.3.25-test1 playtest logs. It follows the v0.3.20 audit ([LIVE_CODE_AUDIT.md](LIVE_CODE_AUDIT.md)), which still
holds the full reachability map and findings register for everything this pass did not touch.

## 1. Baseline

| Item | Value |
|---|---|
| Starting development head | `35b4f63` (docs and package for test1); runtime source of test1 `907cd75` |
| Played package | `AI-Control-FACC-v0.3.25-dev-test1-907cd75-UMM-test.zip`; the log's startup line shows MVID `b9270500-be53-43c5-8beb-8bbf61bbafe6` - the played DLL is that package |
| Open pull requests | none |
| Final head | `58dece2` (docs); runtime source of test2 `2692562`, MVID `15e39809-c72b-47a8-8db8-e2593f1ca945` |
| Inherited checks reproduced | Release build with the game's managed assemblies: 0 warnings; `dotnet test tests/CompanionAI.Tests`: 273 passed at the start, 282 at the end |
| Tools | game/UMM managed assemblies (reference build only), ILSpy decompiler on `Assembly-CSharp`, blueprint exports (classes, buffs, spells, mythic, feats, items, weapons, equipment), `tools/ProfileAudit`, SigDump, MVID reader |
| Not available | the game itself (no runtime capture, profiler, frame timing or replay); Unity/Mono timing; optional-mod DLLs other than what earlier sessions decompiled |

Playtest evidence (author, v0.3.25-test1): one combined log (14 fights: 11 turn-based, 3 real-time; party Orvath,
Seelah, Camellia, Lann, Aeiluvetar; Tabletop Tweaks, Isekai, Buff It 2 The Limit and Wrath Patches loaded) and the
game's CombatLog for the same 14 fights. No mod exception or error line in the log.

## 2. Live system map (this pass)

Unchanged from the v0.3.20 map except the partials added since then. Turn-based: `TurnController.Tick` postfix →
`TurnBasedController` → `TacticalEngine.Decide` → `Execute` → `CommandDispatcher.Run`. Real time:
`AiBrainController.TickBrain` prefix → `RealTimeController.ProcessUnit` (pending-command, stuck, area and Wrath Tactics
gates; out of combat `ApplyPermanentBuffsOutOfCombat`) → `TacticalDriver` (quiet waits, `RealTimePacing`) → the same
`Decide`/`Execute`.

Inside `Decide`: context and threat → weapon candidates and attack modes → **ability candidates** (`AbilityDiscovery`,
`AbilityScoringPolicy`, `EvaluateAbility`/`ScoreAbility` with the buff, heal, control, held-state, aura, cleanse and
fallback sections) → kineticist burn and recovery → movement (`anyAttackNow`, step-out, advances) → real-path repricing →
toggles → Mobility → held charges → `usable` (`Affordable`, failures, silent failures) → walk preflight → Progressive →
marks, weapon riders, spell boosters → infusion search → quick/main choice (tie rule, move+standard pair, restraint) →
logging.

Module classes for what this pass read: active modern core - `TacticalEngine` (Decide, AddAbilityCandidates,
AddMovementCandidates, the heal section, item bar, ConsumableFactor), `.HeldStates`, `.Unraveling`, `.Ricochet`,
`.InfusionEffects` changes, `.Buffs` (armor and alignment readers), `DecisionTimings`, `AbilityScoringPolicy`,
`ValueModel.Resources`; shared active dependency - `Analysis/HitChanceCalculator.CalculateDamageReduction` (only the two
weapon estimates in `CombatMath` call it), `Analysis/BuffParts`; compatibility boundary - `RealTimeController`
out-of-combat routine, `RealTimePacing` inputs; legacy-only (excluded, reachable only with "Real time: use the old
decision engine") - `Core/DecisionEngine`, `Scoring`, `Planning/MovementPlanner`.

## 3. Coverage ledger

- **Substantive review, this pass:** every reader of an ability candidate whose slot is not affordable (movement's
  `anyAttackNow`, step-out views, Progressive's step view, kineticist recovery, toggles' forbid-spell price, held
  charges, `usable`, real-time pacing); the heal section and the item bar; potion gating (`ConsumablePolicy`,
  `ConsumableFactor`); weapon damage reduction; the v0.3.25 additions (held-state regain, toggle restoration, the
  move+standard pair, Unraveling, Chain Arrows, the four potion readers).
- **Boundary only:** `CommandDispatcher`, `CommandTrace`, `TurnBasedController`, `CombatMemory` resets and
  `SwitchOffAiToggles`, `ModInterop` (Buff It 2 The Limit detected and reported in the log).
- **Excluded (proved):** the legacy real-time engine - selected only by its setting, not maintained (CLAUDE.md).
- **Not reviewed again this pass** (v0.3.20 audit and later reviews stand): observers (`CastOutcome`, `DamageObserver`,
  `SaveObserver`, `ProvokeObserver`), `.Martial`, `.Modes`, `.Toggles` internals, `.Auras`, `.Marks`, `.Boosters`,
  Progressive gate, area danger and grid, settings UI.

## 4. Score contract (families touched this pass)

| Family | Base | Factors | Restraint / bar | Changed here |
|---|---|---|---|---|
| Weapon attacks | the game's attack and weapon-stats rules, per hit | hit chance, crits, sneak, **damage reduction by the native bypass rule** | none | DR bypass (A3) |
| Healing | heal x `HealPerHp` below threshold + drop-chance cut x ally worth; revive x stays-up chance | role weight | **used-up items: restoring part x easy-fight share, item bar 5** | A2 |
| Buffs (incl. potions) | parts appraisal over useful rounds | stacking increment, what comes at the recipient, enemy alignment share | potions: hard fight only, x0.5, bar 5; restraint 15 % on main actions | readers for Magical Vestment, Align/Bless Weapon (test1) |
| Held state regain | `HeldStateWorth`: readable comes-with + 0.15 x round per dependent feat + best trance after its command | rounds left minus a standard action | restraint as a buff | - |
| Unaffordable offence | not scored in turn-based; real time until "attack from here" is known | - | - | A1 |

## 5. Findings register

| ID | Pri | Finding (first cause) | Evidence | Status |
|---|---|---|---|---|
| A1 | P1 | Every offensive ability was scored when its action was already used (turn-based) or on cooldown (real time). Only real time's `anyAttackNow` reads such candidates, and only as a yes/no; every turn-based reader checks `Affordable`. | Log: 12 of 57 slow decisions (6.0 of 24.5 s logged) had no standard action; TB fight 5 R2 769 ms then END TURN; RT 794 and 1060 ms then "wait". Code: all consumers traced (§3). | **Fixed** (`ff01787`): skipped in turn-based; real time stops once one burn-free option answers the question. Turn-based decisions identical by construction; real time keeps the same boolean. Measured gain pending replay. |
| A2 | P2 | A used-up healing item had no easy-fight restraint and no item bar (only the HP threshold), so it topped HP up in won fights. | Seelah: Cure Light Wounds at 53 % with one enemy left (difficulty 0.10), at 65 % in easy fights; 9 potions in 14 fights. | **Fixed** (`ff01787`): restoring part x easy-fight share (`ValueModel.SpentHealValue`, tested), bar 5; drop-chance part and revives undiscounted, so emergencies (6 %, 29 %) still drink. Intended decision change. |
| A3 | P2 | Weapon damage reduction: any +1 weapon counted as getting through "and" reductions (DR/magic and cold iron); material and alignment never counted (a TODO). | Code; native `AddDamageResistancePhysical.Hit` decompiled. | **Fixed** (`62a4ea0`): the native rule on the weapon's physical damage as the game describes it. Ki Strike-like wielder facts not read (conservative). |
| A4 | P2 | Orvath's decisions with his standard action free stay at about 230-300 ms in the abilities stage (77 abilities). Which abilities cost most is unknown. | Log [Perf] lines. | **Open**: the slow-decision line now names the three costliest abilities and the scored/skipped counts. Capture in the next replay, then target the measured ones. |
| A5 | P3 | 11 of 57 slow decisions overlapped a garbage collection; allocation sources unmeasured. | Log ("memory clean-up ran"). | **Open**: the slow line now reports heap growth when no collection ran. GC overlap does not prove ownership. |
| A6 | P3 | First decision of the session is cold: 800 ms, 76 profiles built, threat 333 ms. | Log. | Accepted (once per load). |
| - | - | Seelah's death (Large Water Elemental, 16 + 19 in one round) | CombatLog | Not an AI defect: full HP to 6 % in one round; she drank and was hit mid-drink. |
| - | - | Potions otherwise | log | Sound: Shield of Faith and Barkskin only in hard fights on the front-liner, heals below threshold. |

No finding: command outcomes (267 success, 151 interrupted after acting, 14 interrupted before acting, 1 fail - a Daze),
one "moved nowhere", Fighting Defensively switched on and back off after fights, Buff It 2 The Limit interop. Not
exercised in this log: Unraveling, Chain Arrows (Orvath's substance plans "keep actual setup"), psionic focus, the
move+standard pair (no Psionic Meditation), Magical Vestment / Align / Bless Weapon.

## 6. Performance plan and acceptance

- **A1:** scenario Orvath turn-based after his standard action and real time while it cools down. Metric: the
  abilities stage of those decisions; expected from the log, most of the 6.0 s. Permitted differences: none in
  turn-based; in real time none in whether a melee unit waits beside its enemy. Evidence: the next log's [Perf] lines
  ("skipped unaffordable=N").
- **A4/A5:** capture plan. Play two or three Orvath fights in each mode with verbose logging as usual. Each slow line will name
  the costliest abilities with their option counts and the heap growth. Optimise only what those lines show: exact reuse
  first, then caches with stated invalidation. No bounded approximation without a proposal.

## 7. Validation

Release build 0 warnings; 282 unit tests pass (new: `UnaffordableOffenceOnlyForTheOpenQuestion`, three
`SpentHeal_*`); SigDump: read-only members added (`AddDamageResistancePhysical.Hit`, `WeaponMaterial.Material`,
`PhysicalDamage.AddMaterial`, `BaseDamage.AddAlignment`), none removed. Not verified: anything in game.

## 8. Replay steps (test2)

1. A turn-based fight with Orvath: after his spell, the [Perf] line of the move-only decision should be short or
   absent; "skipped unaffordable" should be high.
2. A real-time fight: Orvath's "wait" decisions while his spell cools down should be cheap; melee companions still
   wait beside their enemy rather than walking off.
3. An easy fight with a wounded front-liner holding healing potions: no top-up below the threshold once the fight is
   nearly won; in a hard fight or when about to drop, the potion is still drunk.
4. Against an enemy with DR (demons, skeletons DR/bludgeoning): weapon estimates in the decision lines should no longer
   treat a +1 weapon as getting through DR/cold iron.
5. Send the log: the slow lines now name the costliest abilities (A4) and heap growth (A5).

## 9. Test2 playtest: stutters (test3)

**Evidence** (v0.3.25-test2 logs, MVID `15e39809`; 4 real-time and 4 turn-based fights; no mod errors). 34 slow
decisions, all but two Orvath's (77 abilities, about 63 scored). Every one without a collection grew the heap by
**4-10 MB**. The longest (500-950 ms) all had "memory clean-up ran", and the cost sat in one ability that happened to be
running when the collection came (Guidance 753 ms, Fan of Flames 698 ms, Grease 821 ms, Lullaby 500 ms). Without one, the
costliest abilities were 10-30 ms each (Snowball, cantrips, blasts) over about 63 abilities. The freezes the author
reports are the game's memory clean-ups, triggered by how fast the decisions allocate. Skipping unaffordable offence
worked: "scored=11, skipped unaffordable=33" on a move-only decision.

**A7 (P1, fixed `ffed9ac`):** per target, every damage estimate, buff appraisal, save check, context and branch
condition ran `AbilityData.CalculateParams` (the game's `RuleCalculateAbilityParams`, through every subscribed fact). That
is a few thousand rule triggers a decision. They are now `CombatMath.ParamsOf`, once per ability per decision and frame. Contexts get
them through the native `CreateExecutionContext(target, cachedParams)`. Exact: parameters depend on caster and ability
(AbilityData equality), and `AbilityExecutionContext` clones what it is given. Contexts themselves are not shared, because
Hit Dice conditions write shared values into them.

**A8 (P2, fixed):** the branch-condition cache (`AbilityProfile.CheckFor`) built a string of seven parts for every
lookup, hits included; it is a struct key of the same parts. `EvaluateAbility` built a `UnitUseAbility` per target to read
`NeedLoS` (a blueprint property: no `ILineOfSightIgnore`); read once per ability. `CombatMemory.AbilityKey` keeps each
GUID's text.

**Acceptance for the replay:** same kind of fights; the slow lines should show far smaller heap growth (KB per costliest
ability is now printed) and fewer "memory clean-up ran". Decisions are unchanged by construction. If a stutter remains,
the line names the ability and its KB; that is the next target. A frame-level stutter outside decisions would not appear
in these lines.

## 10. Test3 playtest and release

Test3 (MVID `b1f83486`) played: 3 turn-based and 3 real-time fights, no mod errors. 16 slow decisions, all Orvath's.
Most of them grew the heap by **40-300 KB** (test2: 4-10 MB); a few still grew by 5-14 MB, with no single ability
standing out (Mono's heap figure is coarse). Three overlapped a collection (861, 897, 1446 ms) against six in test2's
34. CPU per Orvath decision is unchanged at about 300 ms (Snowball and damage cantrips 15-30 ms each over 63 abilities):
A4 stays open. Behaviour: no new defect. Potions were drunk in hard fights or when about to drop. The five "moved
nowhere" turns were archers advancing towards an enemy in range but out of sight: the advance stops at range, not at a
spot with sight. That is a movement change, left for discussion. Released as v0.3.25 at the author's request.
