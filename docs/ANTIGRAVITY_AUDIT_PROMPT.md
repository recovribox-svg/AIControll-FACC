# Systemic code, decision-logic and roadmap audit of AI Control (v0.5.0 review2)

## Role and goal

You are acting as a principal systems architect and senior game-AI engineer. Audit the **AIControll-FACC** repository
(the Unity Mod Manager mod `CompanionAI_Pathfinder`, "AI Control - Fully Autonomous Combat Companions", for
Pathfinder: Wrath of the Righteous). The audit has two outputs:

1. An **audit report**: performance, decision logic, functionality gaps, mod compatibility, safety, code hygiene,
   the legacy engine's removal, the settings UI, diagnostics and the test infrastructure.
2. A **prioritised roadmap**: which outstanding limitations and unimplemented features future work should tackle
   first, and in what order.

This is an audit, not an implementation task. Do not change code, `Info.json` or packages.

**Author's approval:** the author approves, in principle, any improvement proposal that is evidence-based, well
integrated with the existing systems and performant. This includes changes to movement, targeting, commitment and
retreat policy, which normally need separate approval. Each proposal is still implemented later, as its own task, through the
normal review process.

## Current state (verify; do not assume)

- **Development branch:** `claude/dreamy-pasteur-b9h3rn`, head `ad6b9db` when this prompt was written (PR #122
  merged). Fetch it and audit the current remote head. Record the exact commit you audited.
- **Version:** `Info.json` 0.5.0. The latest test package is
  `test-builds/AI-Control-FACC-v0.5.0-dev-review2-ffb3e0b-UMM-test.zip` (startup line
  `[Build] v0.5.0 module=1a48f45f-a51a-4efa-b27e-657806359176`), not yet played. The latest stable release is v0.3.70 on
  `main` (`release/CompanionAI_Pathfinder_v0.3.70.zip`).
- **Last recorded checks (`ffb3e0b`):**
  - Release build: 0 warnings.
  - Tests: 1,196 unit tests, plus 27/21/3 script tests.
  - ProfileAudit: exit 0.
  - Native harnesses: 98/14/10.
- **Where the code is.** The draft of this audit assumed the hot path is in `Planning/` and `Scoring/`. It is not:

  | Folder | Files / lines | Role |
  | --- | --- | --- |
  | `Tactics/` | ~203 / ~48k | **The live decision engine for both combat modes**: `TacticalEngine` (many partial files), `CombatMath`, `ValueModel`, `RiskMath`, the `*Rules` classes, observers, `CombatMemory`, `TacticalDriver`, `RealTimeTactics` |
  | `Analysis/` | ~23 / ~15.7k | Shared analysers the engine uses (`AoeDangerAnalyzer`, `BuffEffectAnalyzer`, `BuffStackingAnalyzer`, `AbilityClassifier`, `BattlefieldGrid`, …), plus legacy-only ones (`SituationAnalyzer`, `TargetScorer`, `Situation`) |
  | `GameInterface/` | ~14 / ~6.4k | Harmony entry points and command issuing: `CustomBrainPatch`, `RealTimeController`, `CommandDispatcher`, `SummonControl`, `SummonTurnBridge`, `ModInterop`, `PetItemsPatch`, `PreBuffController`, `TrapAvoidancePatch` |
  | `Core/` | ~13 / ~4.2k | `TurnBased/TurnBasedController` (the turn-based driver), diagnostics logs, and legacy-engine state (`DecisionEngine/`, `TurnState`, `TurnPlan`, `PlannedAction`, `TeamBlackboard`, `CombatDataCache`, `FailedAbilityTracker`) |
  | `Scoring/`, `Planning/` | ~12 / ~5.8k | **Legacy real-time engine only** (`UtilityScorer`, scorers, `MovementPlanner`) |
  | `Settings/`, `UI/` | 5 + 3 / ~2k | Settings model and JSON persistence, build suggestions, ability blocklist; the UMM IMGUI panel and `UiScale` |
  | `Abstraction/` | 6 / ~0.5k | Adapters; check what still uses them |

- **Pipeline:** see `CLAUDE.md` › Architecture.
  - Turn-based: `TurnController.Tick` postfix → `TurnBasedController` → `TacticalEngine.Decide` / `Execute` →
    `CommandDispatcher.Run`.
  - Real time: `AiBrainController.TickBrain` prefix → `RealTimeController` → `RealTimeTactics` → the same engine.
  - Summons in turn-based use the game's own AI turn (`SummonTurnBridge`).

## Read first (in this order)

1. `CLAUDE.md`:
   - the **current handover** (top);
   - the **Architecture** and **Development Guidelines** sections (bottom);
   - skim the older handovers only for their "Open", "Limits", "Remaining" and "Not changed" bullets. They are a log
     of the project; the file is about 2,000 lines.
2. `AGENTS.md` and `CONTRIBUTING.md`. Antigravity uses `antigravity/<task>` branches and follows the Codex rules there.
3. `README.md`: features, settings, the blocklist, compatibility, known limitations.
4. `docs/TACTICAL_AI.md`. Its §1–§5 are the design (decision flow, values, consumables, **reading the log**). The
   later sections (to §230) are the change history, each with its evidence. Use them to find why code exists before
   calling it a defect.
5. Previous audits and backlogs, so you build on them rather than repeat them. Report which of their findings are
   still open, fixed, or wrong:
   - `docs/LIVE_CODE_AUDIT.md` (live-code map and findings register);
   - `docs/LIVE_CODE_AUDIT_V0325.md`;
   - `docs/CLAUDE_MASTER_PROMPT_V0351.md` (a ranked backlog as of 0.3.51; outdated, verify each item);
   - `docs/TACTICAL_EXPANSION_INVESTIGATION.md`;
   - `docs/REVIEW_86_89.md` (R1–R7).
6. Open proposals and limits:
   - `release/CHANGELOG_v0.3.70.md` › Known limitations;
   - `docs/V0370_RELEASE_REVIEW.md` (the latest review);
   - `docs/DEATH_TRIGGER_SAFETY_PROPOSAL.md`;
   - `docs/FUSION_COVERAGE_PROGRAMME.md`;
   - `docs/SUMMONS_CONSUMABLES_PLAN.md`;
   - `docs/COVERAGE7_FOLLOWUP_PROPOSALS.md`;
   - `docs/C06_SWARM_PROPOSAL.md`;
   - `docs/future/DOOR_OPENING.md`;
   - `docs/SUMMON_KIT_INVENTORY.md`.
7. `tools/README.md`: the offline verification tools (ProfileAudit, SigDump, native harnesses, BubblePrints data).

## Rules of evidence

- **Location:** every finding names the file, type, method and line range at the audited commit, with a short quote or
  a precise description.
- **Game behaviour:** claims about how the game behaves cite the decompiled game (type and method) or blueprint data
  (exports or the BubblePrints database). Claims about another mod cite its assembly or source. Don't rely on memory of
  Pathfinder tabletop rules: this game's code often differs.
- **Confidence:** mark each finding:
  - **Verified:** traced end to end, reproduced, measured or tested;
  - **Likely:** traced but not executed;
  - **Hypothesis:** say what evidence would settle it.
- **Performance:**
  - Separate measured numbers (log `[Perf]` rows, a benchmark you ran) from static estimates.
  - For static estimates, give the work per decision as a function of the fight, for example
    allies × enemies × candidate spots, and the call that dominates.
  - Do not claim a speed-up you have not measured.
  - The runtime is Unity's Mono with .NET Framework 4.8. Allocation rules:
    - LINQ closures and iterators, `IEnumerable` over interfaces, boxing struct keys without `IEquatable`, string
      interpolation that runs even when the log line is not written, and per-call `List`/`HashSet` all allocate;
    - `foreach` over a `List<T>` does not.
- **Check the rationale before calling something a defect.** Many choices are deliberate and documented:
  - values in damage-equivalent points;
  - the game's own rule functions instead of copied tables (other mods patch them);
  - bounded caps (for example 8 leap endpoints, 4 weapon quotes, 80 exit hints);
  - once-per-fight `ReportOnce` diagnostics;
  - fail-closed readers for optional mods;
  - "a value of 0 for a verifiable effect means not useful now".

  If you disagree with a documented decision, say so explicitly and give evidence.
- **Both modes.** Every finding says whether it affects turn-based, real time or both.
- **No guessed fixes.** A recommended fix names the existing mechanism it reuses or extends (no parallel systems),
  its per-decision cost, its risk, and how to verify it: a unit test, a ProfileAudit check or a playtest step.

## Baseline checks (run what your environment allows; report actual results)

- `dotnet test tests/CompanionAI.Tests` (expected 1,196 passing).
- Script tests:
  - `tools/BlueprintInventory`: `python3 -m unittest test_blueprint_inventory test_mechanic_map`;
  - `tools/InvocationLedger`: `python3 -m unittest test_invocation_ledger`;
  - `tools/ProfileAudit`: `python3 -m unittest test_summon_kit_supplement`.
- A Release build against the real game assemblies:
  `dotnet build CompanionAI_Pathfinder.csproj -c Release -p:GamePath=<game> -p:UmmPath=<UMM>`. Expected 0 warnings.
- ProfileAudit (`tools/ProfileAudit/run.sh`, see `tools/README.md`) on the blueprint exports. Use its coverage numbers,
  for example withheld or unread fusions, summon kits, potions and scrolls, as evidence for the roadmap.
- The native harnesses `tests/ToggleCosts.Native`, `tests/TomeBurn.Native` and `tests/ToyBoxSpend.Native` (98/14/10).

If a check cannot run, say why. Never report a check you did not run as passed.

## Playtest logs

> **The author fills this block in before sending the prompt.**
>
> - Logs attached: **[YES / NO]**
> - Files and where they are in your workspace: `[Log.txt path]`, `[combatLog.txt path]`
> - Build that was played: `[startup line, e.g. [Build] v0.5.0 module=1a48f45f-a51a-4efa-b27e-657806359176]`
> - Session: `[turn-based / real-time fights, roughly how many, party, difficulty, mods on, spawned or story fights,
>   rests between fights, manual orders given, anything odd you noticed, e.g. stutters or FPS with the settings panel open]`

If logs are attached:

1. **Identity.** Check that the startup line's module matches the build named above. If it does not, say so and treat
   the logs as evidence for that other build only.
2. **Counts.** Per mode: fights, decisions, commands issued and how each ended. List any AI Control exception.
3. **Performance.** The `[Perf]` rows: median, 95th percentile, slowest, and each `[Perf] slow decision` row with what it
   names as costly. Note which coincide with garbage collections.
4. **Decisions.** Rank the worst decisions with log lines and `combatLog.txt` evidence, and trace each to its code
   cause. Read `docs/TACTICAL_AI.md` §5 for the log's format.
5. **The review2 fixes.** Check each against the log:
   - the `[Grapple]` keep rows ("no action to regain this round", "held where a withdrawal is worth N");
   - the helpless +4 in melee;
   - holds kept or released.
6. **Unused abilities.** Which abilities and items were never used, and why (the `[Unused]` rows).
7. **Privacy.** Do not commit raw logs. Record their SHA-256 fingerprints only.

If there are no logs, do the static audit and list the playtest scenarios whose logs would settle your Hypothesis
findings.

## Audit sections

### 1. Performance and frame time

- **The decision hot path:**
  - `TacticalEngine.Decide`, candidate generation and `ScoreAbility`;
  - the drop model (`DropTableOf`, `SpotDrop`, `StandingDropChance`), walk pricing, `MoveRisk`, `FindRetreatPoint`;
  - approach and free-side planning, cone and line aim, the burst watch, teleport and leap quotes;
  - `CombatMath`'s per-frame memos (`EstimateMemo` and the others): check their invalidation is correct and complete.
- **Real-time scaling.** Decision interval (`RealTimeDecisionInterval`), how many units decide per frame, and fights with
  40+ enemies. Summon control is on by default since 0.3.70, so the party can grow large.
- **Repeated game-state queries** within one decision that could be read once, with a correct invalidation point.
- **Garbage collection:** where a decision allocates the most.
- **Logging cost:** strings built whether or not they are written.
- **Turn-based:** time between turns, `ReadyToEndTurn` waits, the summon turn bridge.

### 2. Decision logic and scoring integrity

- **Numeric safety.** Division by zero, NaN and infinity reaching a score, missing clamps. A NaN can make every
  comparison false.
- **Double counting.** For example a kill bonus plus damage, a heal plus revive, or an attack's value both in the option
  and in its mode switch.
- **Consistent units and horizons:** rounds left in the fight, buff durations, summon durations.
- **Real-time stability:**
  - oscillation between options;
  - hysteresis;
  - re-decision rules (`RealTimeReplanRules`, `ReconsiderPending`, `ShouldReplanOnNewThreat`);
  - interrupted commands;
  - commands interrupted before acting (six real-time Coups de Grace in one log).
- **Action economy:**
  - standard, move, swift and free actions, full rounds, the 5-foot step, surprise rounds;
  - Magus Spell Combat and Spellstrike, Shifter's Fury and forms, weapon-set switching (a move action);
  - Quicken and next-spell boosters, toggles, grapple holding.
- **Parity:** check that turn-based and real time read the economy consistently. Real time uses cooldowns and a 6 s round.

### 3. Summons, pets and companion parity

- **What a summon gets:** `SummonControl`'s temporary settings view, the owner's blocklist, its spell policy and friendly
  fire. It must not inherit Pure Mage, range or role.
- **Item access:**
  - which units can use belt or quick-slot items (`PetItemsPatch`, `PetsUseItems`);
  - that summons cannot reach party items or potion hand-overs.
- **Turn completion:** a summon's turn-based turn always ends (`TurnBasedPatches.CompleteTurn` backstop).
- **Exclusions:** units AI Control must not drive (pets when not chosen, swarms, enemy summons, dominated allies).

### 4. Safety and fail-safes

Look for null-reference risks and for turns or commands that never end, in each of these states:
- dominated (`FactionOriginal`), confused, nauseated (move only), helpless, prone, panicked;
- cutscenes, dialogue, area transitions, pause, loading a save mid-combat;
- a unit dying mid-command; viewless reinforcements.

Then check three things:
- **Exception handling:** what an exception in `Decide` does in each mode. It must not stall a turn-based turn.
- **Player control:** whether a player's manual order is ever overridden.
- **Writes:** that nothing writes `TurnController.Status`, a unit's controllability or its faction.

### 5. Functionality gaps and mod compatibility

- **Advertised vs implemented.** Compare the features `README.md` and `docs/TACTICAL_AI.md` advertise with the code:
  - half-implemented mechanics and unhandled ability variants;
  - brittle assumptions about blueprint components (readers that accept one exact shape).
- **Supported mods.** The list is in README › Compatibility and `docs/competence/INSTALLED_MODS.md`:
  - Tabletop Tweaks (Core/Base), IsekaiMod, Psionics, Mystical Mayhem, DarkCodex, Demonology and Tome of the Firebird;
  - Wrath Tactics (first refusal in real time) and Buff It 2 The Limit;
  - ToyBox and WrathPatches.

  Magic Deceiver and Magic Fusion are base-game mechanics, not a mod.
- **For each interop path, check:**
  - that readers of other mods' code (exact-module and exact-layout recognition) fail closed when the mod's version
    changes;
  - Harmony conflicts on the same game methods;
  - custom components that could throw.

### 6. Code hygiene, coupling and testability

- **Dead and duplicated code.** Unused methods and obsolete fallbacks. Logic duplicated between `Tactics/` and the
  shared `Analysis/` readers.
- **Partial-class sprawl.** The size of `TacticalEngine.cs` and its partials.
- **Diagnostic plumbing.** Memo and `ReportOnce` key sprawl.
- **Orphaned settings and localisation keys.**
- **Testability boundaries.** Which rules live in pure, tested classes (`ValueModel`, the `*Rules` classes) and which
  are buried in game-bound code that only playtests can check. Propose concrete extractions that would let important
  logic be unit-tested.

### 7. Legacy real-time engine: removal plan

The setting is "Real time: use the old decision engine" (`ModSettings.RealTimeLegacyEngine`, default off). It is
documented as unmaintained and due for removal.

- **Trace every reference.** Known starting points:
  - `GameInterface/RealTimeController.cs` (about lines 170, 265, 281, 420);
  - `GameInterface/SummonControl.cs` (`LegacyRealTime`);
  - `Tactics/TacticalEngine.AcceptSwitch.cs`;
  - `UI/MainUI.cs` (the checkbox);
  - `Settings/ModSettings.cs` (property and localisation keys).
- **Classify each class.** Several candidates are referenced from live code too (for example `ConsumablePolicy`,
  `TacticalDriver`, `AbilityDiscovery`, `AbilityClassifier`). Classify each of the following as legacy-only, shared, or
  shared through a member that can move:
  - `Core/DecisionEngine/*`, `Scoring/**`, `Planning/MovementPlanner`;
  - `Analysis/SituationAnalyzer`, `TargetScorer`, `Situation`;
  - `Core/TurnState`, `TurnPlan`, `PlannedAction`, `TeamBlackboard`, `CombatDataCache`, `FailedAbilityTracker`.
- **Deliverables:**
  - a dependency graph;
  - a safe deletion list (files, classes, settings, UI, localisation, tests, ProfileAudit references);
  - the refactors needed first;
  - the effect on saved settings (Newtonsoft ignores unknown JSON properties: confirm it);
  - a staged plan where each stage is checked by build, unit tests, ProfileAudit, native harnesses, SigDump and a
    real-time replay.

### 8. Settings UI and UX (`UI/`, `Settings/`)

- **IMGUI cost.** Unity Mod Manager draws the panel with `OnGUI` several times a frame while it is open. Look for
  `GUIStyle` or `GUIContent` created per pass, strings built per pass, LINQ or blueprint lookups per pass, and full-list
  filtering every pass.
- **"Abilities AI must not use"** (shared and per character): how search, sorting and variant grouping scale with large
  modded spellbooks.
- **Layout:**
  - scaling with UMM's UI scale (`UiScale`);
  - grouping and density;
  - whether conditional settings say when they apply, for example Pure Mage (only with Ranged) and
    `ProgressiveBossBypass` (only with Conservative);
  - the build suggestions (`SuggestedSettings`) and their buttons.
- **Persistence.**
  - How profiles are keyed (`CharacterId`) and what happens with blocklist GUIDs from mods no longer installed.
  - How often `ModSettings.Save()` writes to disk: on every click?
  - Whether changing a setting mid-combat can leave a decision half on the old value.
- **Localisation.** English and Korean completeness, and missing keys.

### 9. Diagnostics and logging

- Is the normal log enough for a reviewer to answer "why did it do that?" and "why did it not use X?"? Where does it
  fail?
- How much does it cost when debug logging is off?
- Are log volume and the debug tier right?
- Suggest additions that would shorten playtest reviews. The project lives on log-based reviews (see the review pages
  in `docs/`).

### 10. Verification infrastructure

- **Coverage:** what the unit tests, ProfileAudit checks and native harnesses cover, and what they cannot.
- **Brittleness:** checks that would pass on broken behaviour.
- **Highest-value additions:** the tests or offline checks that would most cut the dependence on playtests.

## Outstanding limitations and future work: the prioritised roadmap

Build one consolidated backlog. Sources:
- README and changelog known limitations;
- the "Open", "Observed, not changed" and "Limits" items in `CLAUDE.md` handovers and the review pages;
- the proposal documents;
- code comments that state a limit;
- ProfileAudit coverage (withheld or unread fusions, summon-kit pairs, potions and scrolls).

Known items to verify and score (the list is not complete; add what you find):
- enemies that explode or cast when they die (Risen Followers; `DEATH_TRIGGER_SAFETY_PROPOSAL.md`, set aside by the
  author before; re-assess);
- Unholy Nimbus's expiry blast, and shadow-spell variants;
- companions body-blocking each other in tight crowds;
- deaths from ability damage and level drain valued approximately; Death Ward and Restoration triage;
- occasional ~1 s decisions coinciding with garbage collection;
- real-time Coups de Grace interrupted before acting;
- searching for an invisible enemy; an archer in range but without sight;
- the geometry quote and burst-watch limits (`REVIEW_86_89` R1–R7);
- Shifter form facts not yet credited (Pounce and others);
- a party member held by a monster deciding to cast or break free;
- door opening (the game forbids it in combat; `docs/future/`);
- Fusion coverage left (Curse of Magic Negation; Hit Dice pools in cones and lines);
- summon kits and items the readers do not read;
- a held ally's danger in the freeing value (decided against in review2; re-open only with evidence);
- the legacy engine removal.

For each item give:
- **Impact on play:** how often it happens and how bad it is, with evidence.
- **Cost:** implementation cost and risk, and per-decision performance cost.
- **Dependencies.**
- **Testability:** offline, or only in a playtest.

Then produce:
- a **ranked top 10**;
- a **phased roadmap** (quick wins; medium; large). Each item has acceptance criteria and the playtest scenario that
  would prove it.

Include new features you judge worth more than the listed items. The author approves any well-evidenced proposal.

## Output

1. **Branch:** create `antigravity/audit-v050` from the current remote `claude/dreamy-pasteur-b9h3rn`.
2. **Report:** write it to `docs/AUDIT_V050.md`, in this order:
   - an executive summary: the ten most important findings in plain words;
   - findings by section 1–10;
   - the legacy engine removal plan (section 7) as its own chapter;
   - the roadmap;
   - a **"considered and rejected"** list with reasons, so later work does not retry them;
   - open questions for the author.
3. **Findings data:** add `docs/competence/AUDIT_V050_FINDINGS.json`, with one entry per finding: `id`, `section`,
   `severity`, `confidence`, `modes`, `location`, `summary`, `fix`, `effort` (S/M/L), `verification`.
4. **Finding format:** in the report, give each finding:
   - **ID** (for example `PERF-03`);
   - **Severity** (scale below);
   - **Confidence:** Verified, Likely or Hypothesis;
   - **Modes:** turn-based, real time, or both;
   - **Location** and **Evidence**;
   - **Issue** and **Impact on play**;
   - **Recommended fix:** the mechanism it reuses, its cost and its risk;
   - **Verification** and **Effort**.
5. **Severity scale:**
   - **Critical:** a crash, stall or softlock; settings or save corruption; AI acting for a unit it must not drive;
     friendly kills.
   - **High:** frequent wrong decisions, or a noticeable stutter.
   - **Medium:** occasional or limited impact.
   - **Low:** hygiene.
6. **Prioritised action plan:** the top 5 fixes to do first, including the legacy engine removal, ordered so that none
   breaks the existing unit tests or ProfileAudit checks.
7. **Pull request:** open a draft PR into `claude/dreamy-pasteur-b9h3rn`, documentation only, using the PR template
   sections. End it with a handover: what you audited (commit), the checks you ran and their results, and what needs a
   playtest.

## Constraints

- **Branches:** do not merge, push to development or `main`, tag or release.
- **Scope:** no code, `Info.json` or package changes in this task.
- **Privacy:** never commit game DLLs, decompiled sources, raw logs, saves or blueprint dumps.
- **Architecture to keep:**
  - every command goes through `CommandDispatcher.Run`;
  - never write `TurnController.Status`;
  - both combat modes;
  - the game's own rule functions, not copied tables;
  - no runtime LLM and no new third-party dependency.
