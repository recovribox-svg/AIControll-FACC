# Codex task: review the Kinetic Sharpshooter investigation and fixes (v0.3.28 test 4)

Written 5 November 2026 by Claude for the author (MonsieurAndroid) to hand to Codex.

## 1. Your role and goal

You are reviewing work Claude did on the Pathfinder: Wrath of the Righteous mod "AI Control - Fully Autonomous
Combat Companions". The work is an investigation of how the AI plays a **Kinetic Sharpshooter** (a Kineticist archetype
that shoots its blasts through a bow), plus three fixes. The author is a non-coder playtester. They want an
independent, evidence-based second opinion before the next play-test. You are looking for:

1. **Errors**: code that does not do what its comment and the notes say, wrong game-rule assumptions, edge cases that
   break, regressions in other classes.
2. **Missed gaps**: Sharpshooter or burn behaviour the investigation should have caught but did not.
3. **Misunderstandings**: claims in the notes that the game code or blueprints contradict.
4. **Performance**: cost added by the new code, and the existing slow decisions of a character with ~90 abilities.

Review first, then fix only what you can prove. A finding without evidence (game code, blueprint, log line, test,
or a traced code path) is a question for the author, not a fix.

## 2. Setup

- Read `AGENTS.md`, `CONTRIBUTING.md` and `CLAUDE.md` first. CLAUDE.md's technical rules apply to you.
- Base: remote `claude/dreamy-pasteur-b9h3rn` at commit `53664df` or later. Create `codex/review-kinetic-sharpshooter`
  from it and record the starting commit in your pull request.
- The work under review is three commits on top of `ea2e570`:
  - `f4f38ee`: quiver made out of combat, burn's Overflow offset, cone warning text;
  - `e620e71`: unread buffs that cost burn are worth 0 in the marker branch too;
  - `53664df`: notes and test package only.
- See it with `git diff ea2e570 e620e71` (code) and `git show 53664df -- docs CLAUDE.md test-builds/README.md` (notes).
- The notes to check: `docs/TACTICAL_AI.md` §101 (and §38-39, which it builds on), plus the test 4 entry at the top
  of `test-builds/README.md`.
- Older background: `docs/TASK_BOMBS_CARNAGE_QUIVER.md` §3. It concluded "no change"; §101 now reverses that with new
  evidence. Check that the reversal is justified.
- Check what this environment actually has. The game's assemblies, the decompiled game source and blueprint dumps may be
  missing. Without them:
  - you cannot do a full mod build;
  - you cannot run `tools/ProfileAudit` against real blueprints;
  - you cannot run `tools/SigDump` against the game.

  `dotnet test tests/CompanionAI.Tests` (359 tests) needs no game files. Never claim an unavailable check passed;
  say which checks you ran.
- The play-test logs are not in the repository. If the author attaches them (`Log.txt`, `combatLog.txt`, and their
  `Kinetic_sharpshooter_mechanics_audit.md`), use them. Otherwise rely on the excerpts in §5 and say so.

## 3. Background

- **The player's report:** a higher difficulty than before. Orvath was multiclassed into Kineticist (Kinetic
  Sharpshooter, longbow) alongside Isekai Protagonist levels. The player sometimes controlled Orvath by hand. The
  player was "not sure the AI was playing Kinetic Sharpshooter", and asked:
  - whether the AI understands the Kinetic Quiver;
  - whether it uses the quiver efficiently;
  - whether it manages burn.
- **The player's audit document asked about:**
  - quiver vs Gather Power;
  - bow blasts using the bow's enhancement, Weapon Focus, critical multiplier and Deadly Aim, one blast a turn, no full
    attack of blasts;
  - Sharpshooter-only infusions vs forbidden ones;
  - kiting;
  - quiver management;
  - no melee or Kinetic Blade.

  Some of its premises are not this game's (it lists "Rending / Splash / Penetrate" infusions). Judge each premise
  against the game, not the document.
- **The play-test:** build v0.3.28 test 3, MVID `09bc562d-a4dc-4c51-a8ff-ebc2e6f1cf3a`. 18 fights: 1-6 turn-based,
  7-18 real-time. No mod errors.

## 4. Game rules Claude relied on (verify each)

Claude read these in the decompiled game (`Kingmaker.UnitLogic.Class.Kineticist`) and the blueprint dumps.

1. **The quiver pays burn.** `AbilityKineticist.CalculateCost` = `max(0, CalculateBurnCost(ability).Total -
   quiver)`, unless `AllowOnlyBurnCost`. `Spend` takes `min(quiver, total)` from the quiver buff's Rank, then
   `AcceptBurn` for the rest. `GetQuiverResource` returns the buff's Rank only if the main-hand weapon is ranged or
   absent.
2. **Create Quiver** (`KineticSharpshooterCreateQuiverAbility`):
   - standard action, Personal range;
   - `AbilityKineticist` with `AllowOnlyBurnCost` (1 burn, never paid by the quiver itself);
   - `ContextActionRepeatedActions`, count = `KineticSharpshooterQuiverProperty` (believed to be class level +
     Constitution bonus), each applying `KineticSharpshooterObtainQuiverBuff` (guid
     `dcc5e9da41444def92d90cfcc5f8a133`) as Permanent.
3. **The quiver buff** has flags `StayOnDeath, RemoveOnRest`, `Stacking: Rank`, `Ranks: 1000`. On re-apply,
   `BuffCollection` does `buff.AddRank()`, so casting again adds arrows.
4. **Some wild talents bypass the quiver.** Searing Flesh, Enveloping Winds, the Shroud of Water abilities, Flesh of
   Stone and Aerial Evasion take burn through `AbilityAcceptBurnOnCast.OnCast` → `AcceptBurn` directly. The quiver
   does not pay them.
   - **Correction to an earlier Claude note:** an earlier summary said the quiver paid them. The shipped code and §101
     say it does not. Verify which is right.
5. **Elemental Overflow** (`AddKineticistElementalOverflow`):
   - attack bonus only for `WeaponCategory.KineticBlast` weapons;
   - damage bonus only on `RuleDealDamage` whose source ability (or its parent) is in `UnitPartKineticist.Blasts`;
   - size `min(bonus, ClassLevel/3, AcceptedBurn)`, times 2 (3 for fire with Fire's Fury).

   Bow attacks (`UnitAttack`) get nothing.
6. **Burn limits.** `MaxBurn` and `MaxBurnPerRound` are resources. `LeftBurnThisRound = min(LeftBurn, MaxBurnPerRound -
   AcceptedBurnThisRound)`, and `AcceptedBurnThisRound` only counts in combat. Burn is nonlethal damage of
   burn × character level (`BurnEffectBuff`), kept until rest.
7. **The archetype removes features.** It removes Gather Power and Supercharge, and adds the quiver at Kineticist 3.
   - **Not verified by Claude:** the exact level-by-level feature list. The audit's claim that Kinetic Blade, Extended
     Range and Deadly Earth are unavailable was answered only as "not on the action bar". Check the archetype blueprint
     if you can.
8. **Bow blasts.** An archer blast (`AbilityKineticist.IsArcherBlast`) is one attack with the bow. It is one ability
   use per standard action, never part of a full attack.

## 5. Evidence from the logs (excerpts)

- **Coverage line** for Orvath at Kineticist 6:

  ```
  [Coverage] Orvath kineticist: level 6, burn 0/3 (this round 3 of 3 left), archer blasts True, weapon Longbow, gather mode Off ...
  Exploding Arrows burn 2; Spindle burn 2; ... Enveloping Winds burn 1; Kinetic Quiver burn 1; Shroud of Water — Burn Bonus to AC burn 1; Aerial Evasion burn 1
  ```

  "burn 0/3" appears at the start of every fight (rests between fights). The Gather Power toggles read
  `available=False` after the archetype (`available=True` at Kineticist 4, before it).
- **The quiver was never made.** Every `[Kinetic]` archer line ends "quiver 0":

  ```
  [Kinetic] Orvath: FullAttack(3)->Young Hydra (77.8) over the best blast through the bow, Earth Blast->Young Hydra(dmg) (49.2); quiver 0. ...
  [Kinetic] Orvath: FullAttack(3)->Cultist Archer (87.4) over the best blast through the bow, Earth Blast->Cultist Archer(dmg) (36.3); quiver 0. ...
  ```

  Claude's tally: 48 burn accepted over 18 fights (about 21×1, 6×2, 5×3). Re-count it if you have the log.
- **Burn priced 0:**

  ```
  chosen Exploding Arrows->Cultist Evoker(active-infusion+active-infusion+dmg) [...] [burn 3: -0.0] = 94.7
  [Buff] Orvath: Searing Flesh — Increase Heat Damage->Orvath(buff) [burn 1: -0.0] = 2.1 (Free) - 2.1: effect not read (quality 0.05 x level)
  [Buff] Orvath: Enveloping Winds->Orvath(buff) [burn 1: -0.0] = 2.1 (Free) - 2.1: effect not read (quality 0.05 x level)
  ```

  Chosen-action burn labels: `[burn 1: -1.0]` ×4 and `[burn 3: -0.0]` ×5. Searing Flesh and Enveloping Winds were cast
  about 20 times.
- **The analyser's view of these buffs:**

  ```
  [BuffEffectAnalyzer] [] Combat=0.05, Categories=Utility, Components=[?AddFacts(AddTargetAttackWithWeaponTrigger,ContextRankConfig,ContextCalculateSharedValue,ContextRankConfig,AuraFeatureComponent,RecalculateOnLevelUp,ContextSharedBonus)]
  [BuffEffectAnalyzer] [Shroud of Water — Burn Bonus to AC] Combat=0.05, Categories=Utility, Components=[?AddFacts]
  ```
- **Cone warnings:**

  ```
  [Warning] [Cone] native recipient query unavailable (a240a6d61e1aee040bf7d132bfe1dc07, InvalidOperationException); cone candidate omitted
  [Warning] [Cone] native recipient query unavailable (91da41b9793a4624797921f221db653c, InvalidOperationException); cone candidate omitted
  ```
- **Performance:**

  ```
  [Perf] slow decision: 322 ms - Orvath R1: Searing Flesh ... heap +13944 KB; ... abilities=296.5 ...; abilities scored=84; costliest: Exploding Arrows 13.8 ms (1 options, +564 KB), Exploding Arrows 13.6 ms (+540 KB), Exploding Arrows 13.1 ms (+536 KB)
  [Perf] slow decision: 1159 ms - Orvath R5: FullAttack(3)->Cultist Conjurer (memory clean-up ran); abilities=1107.7; costliest: Cold Blast 929.0 ms
  ```

  47 slow-decision lines; Orvath's round-1 decisions cost 250-400 ms and +10-16 MB heap.
- Three companions went down in early fights (Seelah, Camellia, Lann). The higher difficulty was not investigated.

## 6. What was changed, and the reasoning

### 6.1 `Tactics/TacticalEngine.Kinetic.cs` - `QuiverToPrepare`, `QuiverAbility` (commit `f4f38ee`)

**Before:** the only quiver path was `AddKineticCandidates`, which returns early unless every own blast is unavailable
because of burn (`if (blasts.Count == 0 || blasts.Any(a => a.IsAvailable)) return;`). Plain blasts cost 0 burn, so the
quiver was never offered.

**After:** `RealTimeController.ApplyPermanentBuffsOutOfCombat` calls `TacticalEngine.QuiverToPrepare(unit)`. It runs
after the "AI recasts self buffs" gate, `ShouldControlUnit`, CanAct, empty commands and `RestoreEndedToggles`, and
before the permanent-buff list. If it returns an ability, `IssueOutOfCombatBuff` (extracted from the old inline code)
casts it on the unit through `CommandDispatcher.Run`.

`QuiverToPrepare` returns Create Quiver only when all of these hold:
- not in combat;
- `part.QuiverBuff != null`, and Create Quiver is found on the bar (`QuiverAbility`: an ability whose `AbilityProfile`
  CasterBuffs/TargetBuffs contain the quiver buff) and is not on a block list;
- the main hand is empty or ranged;
- the costliest non-quiver `AbilityKineticist` ability (not `AllowOnlyBurnCost`, not blocked) has
  `TotalBurn` = `CalculateAbilityBurnCost(...).Total` > the quiver's current ranks;
- `quiver.IsAvailableForCast`;
- `RealHpBeforeDown - quiverBurn × CharacterLevel >= 0.5 × MaxHP`.

Reasons are logged once per change (`QuiverNotes`, a static per-unit dictionary): issuing at Log level, the rest at
Verbose. The in-combat burn-blocked path is unchanged.

### 6.2 `Tactics/TacticalEngine.Kinetic.cs` - `BurnCost(ctx, burn, blastShortfall)` and `BlastShortfall` (`f4f38ee`)

**Before:** `BurnCost` subtracted `(overflowAfter - overflowNow) × (Horizon(ExpectedRounds, 4) - 1)` and clamped to ≥0.
It assumed one blast a round.

**After:** `PriceBurn` computes `BlastShortfall` = max(0, best weapon-attack candidate value − best own-blast candidate
value with `BurnOf == 0`), over Standard/FullRound candidates with Value > 0. It is null if there is no free blast.
The Overflow offset is applied only when `gain >= shortfall` (or when the shortfall is null). Other callers (blade
burn, the quiver option, the infusion switch-off) pass no shortfall, so they behave as before.

### 6.3 `Tactics/TacticalEngine.cs` - `BuffQuality` (commit `e620e71`)

**Before:** `q` was:
- `Recognized ? CombatValue`
- `: Enchants ? 0.8`
- `: OnlyUnrecognized ? (item || SpendsLimitedUse ? 0 : 0.5)`
- `: 0.05`

**After:** `SpendsLimitedUse(ability) ? 0` is added before the final `0.05`.

Root cause as Claude read it: `BuffEffectAnalyzer.AnalyzeComponent` itself adds `"?AddFacts(...)"` to
`DetectedComponents`. The outer loop then sees the count change and does not count the component as unrecognised, so
`OnlyUnrecognized` is false and the §39 (v0.3.19 test7) rule "an unread effect is not worth a daily resource" is
skipped. Claude chose the narrow fix in `BuffQuality`, not in the analyser, to avoid moving every other unread-AddFacts
buff from 0.05 to 0.5.

### 6.4 `Tactics/ProjectileCone.cs` (`f4f38ee`)

The `[Cone]` warning now includes `ex.Message`. That file throws `InvalidOperationException` for three causes (no aim
direction, no context, invalid range/origin).

### 6.5 Assessment claims in §101 not backed by a code change (check them)

- **Bow-blast hit chance** uses the bow (`CombatMath.DeliveryWeapon` / `SpellAttackHitChance` with
  `RuleCalculateAttackBonus`), against touch AC for energy blasts.
- **Bow-blast damage** adds the bow's `DamageModifiers` and enhancement (`KineticDamageBonus`) and its decline against
  swarms (`ArcherBlastDecline`).
- **Known gaps left open:**
  - critical hits are not in the blast estimate (the bow's ×3);
  - Deadly Aim is chosen only for weapon-attack candidates (`ChooseAttackModes`), so a blast counts it only if it is
    already on;
  - arrows are priced 0 once the quiver pays.
- **Full attack over blast:** the bow full attack beating a single blast is correct play, not a bug.

## 7. What to review

### A. Correctness of the new code

1. **`QuiverToPrepare` trigger rule:**
   - Is `TotalBurn` of the currently active configuration the right measure? Out of combat, substance infusions the
     AI switches on in combat (Entangling) may be off. That understates the burn the AI will spend, so the quiver may
     not be topped up.
   - Does `TotalBurn` include gather power? It should not for a Sharpshooter.
   - Would "ranks == 0 and some kinetic ability costs burn" or "ranks below the burn of the next N expected blasts" be
     better?
   - Which abilities have `AbilityKineticist`? Is the "costliest" one something the AI never casts (an unaffordable
     form, a variant the player blocked)?
2. **The 50 % hit-point floor** is a heuristic. Is `RealHpBeforeDown` right out of combat (it subtracts current
   nonlethal)? Should it use the existing burn pricing (`BurnCost`) instead of a fixed share? Note that `BurnCost` needs
   a `TacticalContext`, which does not exist out of combat.
3. **Timing.** The routine runs on real-time ticks out of combat (throttled by `_lastDecisionTime`) and after combat
   ends (`CustomBrainPatch` and the pending-buff queue).
   - Can it cast more than once before the buff's rank updates? Watch the pending command, the cast time, and
     `HasPendingCommands` vs `unit.Commands.Empty`.
   - Can it cast in a turn-based out-of-combat phase, during dialogue or cutscenes, or while the party is resting?
   - Can it run straight after rest, when the player may want the burn for something else?
4. **Burn out of combat.** Out of combat, `AcceptedBurnThisRound` is not increased. Confirm `IsAvailableForCast`
   passes, and that the 1 burn really lands as `AcceptedBurn` and nonlethal damage.
5. **The weapon check** matches the game's `GetQuiverResource` (empty or ranged). The older in-combat path uses
   `rangedInHand` (ranged only). Is that mismatch a bug?
6. **`BlastShortfall` units.**
   - It compares a raw damage gain per blast (Overflow points) with a difference of candidate values (damage-equivalent,
     already scaled by hit chance, kill bonus, provoke risk, and so on). Is that comparison fair, or should the gain be
     scaled the same way?
   - It looks only at `CandidateKind.WeaponAttack` as "other". Spells, toggles and moves are ignored: a caster-kineticist
     whose best option is a spell keeps the old Overflow offset.
   - It uses this decision's candidates: a move-only or swift-only decision, or a round without a full attack. Is that
     representative of the rounds that follow?
   - The Overflow on the blast being cast itself is not counted (`Spend` happens before the damage).
7. **`BuffQuality` change.** Find every ability where `SpendsLimitedUse` is true and the buff reaches the 0.05 branch,
   and check the change does not zero something useful.
   - `SpendsLimitedUse` counts any `AbilityResourceLogic` that spends a resource. That covers class features with daily
     uses whose buff has no components but whose real effect is elsewhere: in actions, auras, or `Enchants`, which is
     handled earlier.
   - The out-of-combat routine (`IsWorthKeepingUp`) and the toggle reader (`TacticalEngine.Toggles.cs`) use other paths.
     Are they consistent?
   - Should the analyser's counting be fixed at the root instead? If so, trace what moves from 0.05 to 0.5 or 0.
8. **`QuiverNotes`** is static and never cleared. That is harmless memory, but check save/load and unit-id reuse.

### B. Missed gaps (Sharpshooter and burn)

- **In-combat quiver.** When the quiver runs out mid-fight, the only in-combat quiver option is still the
  "all blasts burn-blocked" path. Is a standard-action refill ever worth it? Price it if so; leave it with reasoning if
  not.
- **Arrow price.** Arrows are free once paid by the quiver. Is that a problem when the quiver holds fewer arrows than a
  blast costs, or when Exploding Arrows + Entangling (3) drains 9 arrows in 3 shots that a cheaper blast would have
  done?
- **Burn buffs vs quiver.** The burn wild talents compete for the same 3 burn as the quiver. After the `BuffQuality`
  change, are Searing Flesh, Enveloping Winds and Shroud of Water ever worth it? Is there a readable effect the mod
  should value instead of zeroing (Shroud's AC bonus, Enveloping Winds' concealment)? Check their blueprints.
- **Blast estimate.** Is the bow's critical range and multiplier missing from the blast damage estimate? Is
  `ChooseAttackModes` missing Deadly Aim on archer blasts? Size the effect on real numbers before proposing a change.
- **Player control.** No quiver while the player controls Orvath (`ShouldControlUnit`). Is that right, and is it
  visible to the player?
- **Elemental Overflow at MaxBurn 3.** Overflow is capped by `ClassLevel/3` and `AcceptedBurn`. With a quiver,
  `AcceptedBurn` stays low, so Overflow damage is lost. Is that weighed anywhere?
- **The cone warnings.** The GUIDs appear in wizard spell lists (likely Burning Hands / Color Spray style cones). Can
  you work out which of the three `InvalidOperationException` causes fired, and whether a cone the AI should have
  used was omitted?

### C. Misunderstandings in the notes

Check every factual sentence in `docs/TACTICAL_AI.md` §101 and the test 4 README entry against the game code,
blueprints, or code paths. In particular:
- the archetype's removals and gains;
- "Kinetic Blade, Extended Range and Deadly Earth do not reach a Sharpshooter's bar";
- the list of `AbilityAcceptBurnOnCast` talents;
- "the burn was back to 0 at the start of every fight";
- the 48-burn tally.

### D. Performance

1. **Cost of `QuiverToPrepare`.** It runs `AbilityDiscovery.Gather`, `AbilityProfile.Get` for each ability, and
   `CalculateAbilityBurnCost` (which raises events) for every kinetic ability, on every out-of-combat tick for each
   Sharpshooter. Measure or estimate it, and cache cheap gates first: no quiver buff, wrong weapon, ranks already high,
   burn full. A per-unit cache keyed on rank, AcceptedBurn and weapon is one option.
2. **Cost of `BlastShortfall`.** It calls `BurnOf` (→ `CalculateCost` → `CalculateBurnCost`) per distinct ability
   among candidates. `PriceBurn` already computes the same per ability afterwards. Can one dictionary serve both?
3. **Orvath's slow decisions** are the existing open item "A5 allocations".
   - Each Exploding Arrows variant costs ~13 ms and ~540 KB.
   - Find where, likely in the area/burst evaluation of the form, the native recipient query, or the per-variant
     context creation.
   - Propose or make exact, behaviour-preserving allocation fixes, as in v0.3.25 test 3 (`CombatMath.ParamsOf`, struct
     cache keys). Do not change decisions to save time.

### E. Tests

The new rules have no unit tests. Where a rule can be pure (the quiver trigger, the Overflow shortfall rule), extract
it into a game-independent helper and add tests to `tests/CompanionAI.Tests`. The patterns to copy are `ArrivalRule`,
`RealTimePacing` and `ValueModel`, all linked into the test project. The game side must call the helper, so the test
covers the shipped rule.

## 8. Constraints (from CLAUDE.md and the author)

- No changes to movement, pathfinding, retreat, target commitment or stuck cancellation without the author's
  agreement: propose them only.
- Do not touch the legacy real-time engine (`Core/DecisionEngine`, `Scoring`, `Planning/MovementPlanner`).
- Use the game's own rule functions, never copied tables. Other mods (Tabletop Tweaks) patch them.
- Issue commands only through `CommandDispatcher.Run`. Both combat modes must keep working.
- Keep values in damage-equivalent points. Put shared formulas in `ValueModel` with tests.
- Don't commit game DLLs, decompiled sources, blueprint dumps or stubs.
- Don't change `Info.json` or release files; don't merge or push to development or `main`.
- Mark code changes `// ★ v0.3.28 (codex review): ...`.

## 9. Verification expected

- Before and after, run `dotnet test tests/CompanionAI.Tests` (359 at the start).
- If the game assemblies exist: build `CompanionAI_Pathfinder.csproj` in Release with 0 warnings, and diff `SigDump`
  output before and after (no unintended new game references).
- If blueprint dumps exist: run `tools/ProfileAudit` on the Kineticist and Kinetic Sharpshooter blueprints (see
  `tools/README.md`; the mod DLL path must be absolute).
- For each fix, re-read the diff adversarially and keep it minimal.

## 10. Deliverables

1. A review report at `docs/CODEX_REVIEW_KINETIC_SHARPSHOOTER_RESULTS.md`, with one entry per finding:
   - **severity:** bug / gap / misunderstanding / performance / note;
   - **verdict:** confirmed / plausible / rejected (with why);
   - **evidence:** a file:line, game type or blueprint field, or log line;
   - **impact:** on Orvath and on other classes;
   - **proposed fix** and its risk.

   Include the claims you checked and found correct, so the review is complete both ways.
2. Fixes for confirmed, in-scope findings on `codex/review-kinetic-sharpshooter`, with tests where the rule can be
   pure. Fixes needing the author's decision (movement, behaviour trade-offs, the burn-buff question) go in the report
   as proposals.
3. A pull request into `claude/dreamy-pasteur-b9h3rn` containing:
   - the starting commit;
   - what changed and why;
   - the checks you ran, with results, and the ones you could not run;
   - focused in-game test steps for the author (Orvath with a longbow, after a rest, AI control on).
4. A plain-language summary for the author, a non-coder: what the AI does with a Kinetic Sharpshooter now, what you
   changed, and what is still missing.
