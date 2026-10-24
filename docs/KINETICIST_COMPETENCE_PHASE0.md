# Kineticist competence: Phase 0 baseline and implementation design

Status: Phase 0 audit and subsequent data intake. No runtime behavior changed; phases 1-3 are not implemented.
The historical environment findings below are supplemented by the data-intake follow-up at the end.
Baseline: `cbc710a49f5fd1a303313d7c66f9405321e48a45` on `claude/dreamy-pasteur-b9h3rn`.
Task branch: `codex/kineticist-competence`. Target: development, not main.

## Environment and evidence

- Read the complete supplied specification, CONTRIBUTING.md, AGENTS.md, CLAUDE.md, TACTICAL_AI.md sections 37-38, TacticalEngine.Kinetic.cs, the requested CombatMath methods/attack-line code, CoverageReport.ReportKineticist, and toggle exclusions.
- No open PRs were returned at the start of the audit.
- The preloaded checkout was older (`2f467ac27b7f5894b147813101bca1276c063f35`). Git fetch failed because the configured proxy was unreachable. GitHub connector reads and writes work.
- Created the task branch at the current remote baseline using GitHub. For baseline checks, copied the local tracked source into an isolated scratch directory and overlaid every changed/added text file reported by GitHub's comparison. The only changed production C# files were Analysis/BuffParts.cs and Tactics/TacticalEngine.Buffs.cs; both were overlaid. Tests and other production sources were unchanged. Release ZIPs were not downloaded or verified in this source-only snapshot.
- .NET SDK 8.0.426 and cached NuGet packages are present. Game reference DLLs are present in the ignored References/Managed folder, including Assembly-CSharp, Owlcat, Unity, Newtonsoft.Json and Unity Mod Manager references. Do not commit these.
- Assembly-CSharp has executable method bodies, not just compile-only signatures. SHA-256: `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`.
- No pre-existing decompiled source tree, Kineticist blueprint dumps, game installation, saves, gameplay logs, or live Unity runtime were found in the supplied workspace assets. The Windows paths in CLAUDE.md are not available here.
- Inspected the supplied assembly's IL using a scratch Mono.Cecil tool. No game code or IL dump is included in this PR. Static assembly inspection establishes base-game structure, not the behavior of a running game patched by other mods.

## Current behavior, with source locations

All line numbers below refer to the baseline commit, before this audit. Names supplied by the specification identify the options; without blueprint dumps, their exact component wiring and live values cannot be independently reproduced.

### Shared routes

**S — substance infusion:** TacticalEngine.Toggles.cs:71-99 excludes FormInfusion, SubstanceInfusion and GatherPower from ordinary toggle candidates. No AddKineticistInfusionDamageTrigger reader exists in AbilityProfile.cs or BuffParts.cs. BurnOf (TacticalEngine.Kinetic.cs:64) queries the game's CalculateCost; PriceBurn (:128) subtracts its value. AddKineticCandidates (:165) exits if any blast is available. Only if every blast is unavailable and burn blocks at least one does it consider quiver recovery or stopping a blocking modifier. Consequently there is no per-target, per-blast comparison of the infusion's benefit against its burn.

**M — metakinesis:** CombatMath.cs:528 reads AbilityData.HasMetamagic, so current Empower/Maximize can affect blast damage. The missing piece is comparing alternative toggle configurations and their costs, not applying an arbitrary extra damage multiplier. Whether a particular metakinesis toggle enters the ordinary toggle path depends on its blueprint group and flags, unavailable here. The specification's 0.5 unknown-buff verdict is an offline report, not a reproduced runtime score.

**D — defensive talent/debuff:** effects go through AbilityProfile and BuffPartsReader (Analysis/BuffParts.cs:267). Unread components are recorded rather than understood (:662); the existing legacy buff-quality fallback at TacticalEngine.cs:1988 allows 0.5 only for an unread, unlimited non-item buff, and 0 for one spending burn/limited uses. Actual scores depend on blueprint components, state, recipient, duration and cost. Do not assign every named talent a fixed value from its name.

### Affected option inventory

| Option from specification | Current value route / missing contribution |
| --- | --- |
| Bowling | S; trip actions on infusion damage are not added to blast score |
| Entangling | S; infusion-applied control is not added |
| Pushing | S; infusion maneuver effect is not added |
| Chilling | S; infusion-applied effects are not added |
| Burning | S; infusion-applied effects are not added |
| Dazzling | S; infusion-applied effects are not added |
| Flash | S; infusion-applied effects are not added |
| Synaptic | S; infusion-applied effects are not added |
| Bleeding | S; infusion-applied effects are not added |
| Grappling | S; infusion-applied effects are not added |
| Gut-Wrenching | S; infusion-applied effects are not added |
| Magnetic | S; infusion-applied effects are not added |
| Foxfire | S and D; exact outline component and affected attack rules need blueprint verification |
| Rare Metal | S; no alternative-set cost/effect comparison; runtime damage rules may already see the active modifier |
| Unraveling | S; infusion-applied effects are not added |
| Vampiric | S; infusion-applied effects are not added |
| Armor-Piercing | S; no alternative-set comparison; active rule changes may already affect base estimates |
| Pure Flame | S; no alternative-set comparison; active rule changes may already affect base estimates |
| Cluster Arrows | S; current special handling can stop it only as blocked-blast recovery |
| Chain Arrows | S; no per-blast configuration comparison |
| Metakinesis Empower | M; active damage metamagic understood, alternative cost/effect set not evaluated |
| Metakinesis Maximize | M; active damage metamagic understood, alternative cost/effect set not evaluated |
| Metakinesis Quicken | M; action type comes from RuntimeActionType, alternative configuration not evaluated |
| Gather Power modes | CombatMath.cs:735 calls WillGatherPower for the player's current mode; TacticalEngine.cs:548 rejects TB candidates needing more than one gather, movement, or a missing Move action. No competing mode search |
| Standalone Gather Power ability | Discovered abilities enter general scoring (TacticalEngine.cs:2474). IsCleanse is descriptor removal (AbilityProfile.cs:187); ContextActionRemoveBuff is collected at :935. The reported cleanse/unknown-buff classification does not prove this action wins a decision; no gameplay log or blueprint dump is available to test that |
| Flame Shield | D; exact bearer attack-trigger effects need verification |
| Jagged Flesh | D; exact bearer attack-trigger effects need verification |
| Searing Flesh | D; BurnOf includes AbilityAcceptBurnOnCast, so unread effects do not get the unlimited-use fallback |
| Shroud of Water | D; readable stat bonuses can already contribute; selectable forms/values need blueprint inspection |
| Enveloping Winds | D; unsupported components are not evidence of a correctly valued defence |
| Elemental Bastion | D; exact granted features/defences need blueprint inspection |
| Aerial Evasion | D; no dedicated Evasion case found in BuffPartsReader |

### Existing blast behavior to preserve

- Elemental Overflow: CombatMath.cs:763-799 reads the active components, but currently reconstructs some class/Overflow arithmetic locally. BurnCost (TacticalEngine.Kinetic.cs:103) also uses burn times character level, an existing day-cost coefficient, and an Overflow lookahead. These are baseline facts, not permission to copy more game formulas. Later work must avoid expanding these approximations; a runtime rule preview is preferable where feasible.
- Archer damage: CombatMath.cs:649 queries IsArcherBlast and RuleCalculateWeaponStats.
- Kinetic Blade: CombatMath.cs:303 attaches BladeBlast to each enumerated attack line; :414 obtains the blade blast, checks activation burn and estimates damage/resistance. :230 includes it in each hit's expected damage. Extra attacks enter through EnumerateAttacks; no fixed attack-count table should be added. Infusion trigger effects are missing from those lines.
- Burn recovery: TacticalEngine.Kinetic.cs:314 queries CalculateAbilityBurnCost, but the hypothetical disabled-infusion estimate at :244 manually subtracts positive modifier values. That is insufficient for the requested general preview because runtime handlers may change the aggregate cost.
- CoverageReport.cs:66 reports level, accepted/remaining burn, blast availability/cost, Overflow, weapon/blade and gather mode/action. It does not report evaluated alternative infusion sets.
- Existing [Kinetic] reporting (TacticalEngine.Kinetic.cs:278) concerns blocked-blast recovery, not every infusion/gather choice.
- Wall, Cloud, Deadly Earth, Torrent, Spindle, Cyclone, Detonation, Fan of Flames, Chain and Extended Range use discovered ability variants and shared damage/area scoring. AbilityProfile.SpawnedAreas (:398), ScoreAbility (:2474) and its per-recipient friendly-fire logic (:2600 onward) are the integration points. Correct Wall geometry and tick-level effects are not established by merely discovering the variants.

## Rules checked in the supplied game assembly

1. AbilityKineticist.CalculateAbilityBurnCost delegates to CalculateBurnCost. CalculateBurnCost builds the initial cost from component fields then raises IKineticistCalculateAbilityCostHandler through EventBus. The final cost must be queried through the runtime method; subtracting a modifier's declared Value bypasses handlers.
2. CalculateCost accounts for the current quiver; IsAbilityRestrictionPassed is the restriction authority. Preserve both daily and round limits; a raw cost is not an affordability verdict.
3. AddKineticistInfusionDamageTrigger is in Kingmaker.UnitLogic.Mechanics.Components. Its fields include Actions, TriggerOnDirectDamage, TriggerOnStatDamageOrEnergyDrain, CheckWeaponType, CheckSpellDescriptor, CheckSpellParent and AbilityList. ApplyInternal checks the delivery context and relevant filters before running actions. RunAction suppresses the same target at the same game time using m_AffectedThisFrame. Do not multiply every infusion by every damage packet or assume one trigger per hit.
4. AddKineticistBurnModifier is a separate component. Its applicable-ability checks and dynamic cost handlers are distinct from the infusion trigger's delivery filters.
5. KineticistController.WillGatherPower compares current/target gather ranks and uses CalculateBurnCost and TotalWithoutGatherPowerAndMetakinesis. Gather choice cannot be reduced to subtracting a constant from total burn, particularly for metakinesis.
6. TryRunGatherPowerAction schedules UnitUseAbility before the blast with CustomHandlerData.ExecuteBefore. TurnController.CalculateKineticistPrediction consumes available Move first, then Standard, and marks overuse beyond the action budget.
7. These methods do not establish every mode's real-time duration, interruption/provocation behavior, or blueprint action type. Those remain a Phase 2 investigation, not an assumption that real-time gathering is free.

## Sequential implementation design

### Phase 1: per-blast substance and metakinesis sets

- Extend the existing effect-reading path to accept the trigger's Actions with the correct originating fact context, branch guards, save, delivery filter, and target. Reuse AbilityProfile's action walker and the existing buff/control/damage readers instead of creating a name-based parallel reader.
- Score maneuvers through ManeuverStrength (TacticalEngine.Martial.cs:46) and CombatMath.ManeuverOdds (:1065), including the game's restrictions and target immunity. Score debuffs with the shared ValueModel.DebuffShare and effect duration/saves. Count overlapping control once and trigger effects at their actual cadence.
- Generate legal modifier configurations from learned/runtime abilities and group rules, not a fixed infusion list or level table. Include the current configuration. Each option must have complete blast damage/control/healing, runtime burn, restrictions, targets and action cost on the existing scoring scale.
- A preview must not execute toggle commands, spend resources, fire gameplay actions or alter the player's durable state. Establish a safe runtime preview mechanism before enabling alternative-set search. Do not silently estimate counterfactual cost by subtraction if it cannot be safely queried. Rebuild/clear relevant attack, ability and metamagic caches between states.
- With toggle management disabled, evaluate the player's current active effects only and issue no switches. With it enabled, protect PlayerOwnsToggle and existing per-round switch guards, including implicitly replaced group members.
- Carry the intended configuration with the blast candidate. At execution revalidate availability, ownership, cost and command state; after any switch re-evaluate instead of casting with a stale score.
- New pure value-combination logic belongs in ValueModel with behavioral tests (cost/effect break-even, immune targets, no double-counting). No priority tree, infusion preference constants, copied burn/CMB/SR math or legacy-engine edits.

**Dispatch boundary found in Phase 0:** ExecuteToggle (TacticalEngine.Toggles.cs:446) currently sets IsOn directly and observes a game-created UnitActivateAbility. Its header explicitly says it does not use CommandDispatcher. CombatMemory.SwitchOffAiToggles (:135) also restores state directly, with safeguards for command/resource-spending toggles. The user's dispatcher requirement takes precedence: new kineticist switching must have a dispatcher-owned path that distinguishes passive state switches from command-producing activation and avoids issuing the activation twice. Determine the native setter/activation lifecycle before implementing it. Preserve RecordToggle, player overrides, AiManagesToggles and end-of-combat restoration, including switches off. Do not merely remove ModifierGroups exclusions and treat every modifier as a generic buff.

### Phase 2: Gather Power

Only after Phase 1 is sound, enumerate current/available gather modes with their actual runtime action costs, reductions and restrictions. Compare complete blast plans on the same scale, including what the spent action could accomplish and gathering's own attacks of opportunity/interruption exposure. Do not charge a lost action both as a score penalty and again through a competing plan. Verify the real-time cooldown path and mode blueprints before implementing it. Prevent standalone Gather Power from earning unrelated cleanse/unknown-buff value by recognizing its actual role, not its localized name. No pre-combat routine.

### Phase 3: exposed blast options

Only fix demonstrated gaps. Reuse lasting-area horizons/occupancy and friendly-fire pricing, apply trigger cadence and saves per recipient, verify Wall's line geometry against the real delivery component, and include blade infusion effects across the game-enumerated routine without counting suppressed triggers twice. Read defensive talents through existing buff/on-hit parts; unsupported effects get zero value rather than a guess. The optional maneuver/free-attack extension remains out of scope until phases 1-3 are complete and verified.

## Verification performed

- Baseline game-independent tests: 104 passed, 0 failed, 0 skipped. Command: dotnet test tests/CompanionAI.Tests. The initial sandboxed runner could not create its local socket; the permitted rerun with --no-restore passed. Initial restore emitted NU1900 because the proxy could not fetch vulnerability data; that warning is separate from the test results.
- Baseline full build: dotnet build CompanionAI_Pathfinder.csproj -c Release --no-restore with ManagedPath/UmmPath pointing to the supplied reference directory: succeeded, 0 warnings, 0 errors. First restore-free attempt lacked restored net48 reference metadata; restoring from the cache with NuGetAudit=false resolved it. Disabling the network-dependent vulnerability audit does not substitute for checking package vulnerabilities.
- No new pure logic was added in Phase 0, so no new unit tests are appropriate for this documentation change.
- ProfileAudit: not run; Kineticist blueprint dumps are absent. No before/after verdict change is claimed.
- In-game testing: not run. No test package or stable release produced.

## Inputs and acceptance evidence for later phases

Needed next: the game's Kineticist blueprint dump (including referenced buffs, areas, activatable abilities and features), and any referenced shared blueprints outside that folder. Existing ProfileAudit output for that dump would supply a reproducible before-state. A full Blueprint dump is acceptable if collecting dependencies individually is awkward. Additional installed-mod blueprint/component dumps are needed to verify DarkCodex/Tabletop Tweaks-specific behavior.

After implementation, ProfileAudit should identify applicable infusion actions and metakinesis modifiers rather than unknown standalone buffs; Gather Power should no longer earn a removal/unknown-buff verdict. Static recognition alone must not be labelled tactical competence: candidate comparisons need runtime log evidence.

Planned [Kinetic] decision log fields: blast/target or area; active and proposed modifier set; gather mode/count; damage/control/defence value; burn and burn value; action/opportunity cost; total value; ownership/restriction rejection reason. These fields describe intended future logs, not logs emitted by this audit.

In both turn-based and real-time combat, record the exact tested commit and package checksum:
1. Level-2 Orvath: blasts remain usable and burn is not wasted.
2. Bowling/Entangling: useful eligible groups versus immune or unprofitable targets.
3. Empower/Maximize/Quicken: net benefit beats the complete competing plan, with costs and action slots logged.
4. Gather Power: selected only when the complete plan wins; no unpriced provoking gather; verify real-time cooldown/interruptions separately.
5. AI manages toggles off: no setting changes; active infusion effects still counted.
6. End of combat: restore pre-fight configuration except protected player edits; check implicit group replacements and settings handoff.
7. Blade extra attacks and area ticks: damage/effects counted at actual game cadence; allies and Wall orientation checked.

Phase 0 cannot verify individual blueprint wiring, actual per-character scores, patched runtime costs, live toggle restoration, gather opportunity attacks or stability in either mode. These remain explicit gates for subsequent implementation and integration.

## Data-intake follow-up: 24 October 2026

Received `codex_kineticist_data.zip`; SHA-256
`f05424406a72a58519ae34df94b930ab653d9590457ffa61da49a50bd68dcfb6`.
The README and supplied ProfileAudit outputs were read. The nested archive actually contains 4,098 unique JBP files,
including 794 under bp_kin, rather than the README's 4,104 and 800. Traversing the supplied Kineticist roots through
four reference links independently reproduces 159 missing GUIDs. References from the fourth layer lead beyond the
exported closure; those additional absent references should not be confused with the 159 within the chosen depth.
Game data remains outside the repository.

Blueprint inspection confirms Bowling's trigger contains ContextActionCombatManeuver with UseKineticistMainStat,
Entangling's trigger contains a Reflex save and target-dependent branches, and metakinesis contains both
AddKineticistBurnModifier and AutoMetamagic with explicit applicable ability lists. Rare Metal is a damage trigger;
Pure Flame instead changes spell-resistance handling directly. A reader must not assume every substance infusion
is a trigger with a fixed damage bonus or evaluate every effect using the blast's context: the infusion's own
ContextCalculateAbilityParamsBasedOnClass and ranks matter.

Two absent action-effect references were identified in the selected gather blueprints:

- `1ffba707590b49db8b1c5cf43660fbfc`: a conditional applied buff in GatherPowerBuffI.
- `d579a8faad5c469abfd700428364b9ee`: conditional applied buffs in GatherPowerBuffII and GatherPowerBuffIII.

Their effects must be inspected before asserting all gather-related side effects are understood. Their names and
behavior cannot be inferred from GUIDs. The workspace has reference DLLs, not the game's resource packs or a game
installation from which to export the missing assets. Request these blueprints and their dependencies from the author.

### Reproducing the offline audit

The baseline harness built but could deserialize only 19 files; 4,079 failed because
`UnityEngine.SharedInternalsModule.dll` is absent. The original harness nevertheless returned status 0 and printed
`abilities 0, errors 0`. That is not a successful rerun of the author's report. The supplied report remains
third-party baseline evidence, not a locally reproduced result.

Fixed tools/ProfileAudit/Program.cs to return status 1 when any blueprint fails to deserialize or no blueprints load,
and replace any stale report with an INCOMPLETE diagnostic. Verified the real supplied dataset, an empty directory,
and malformed JSON: all three returned 1 and replaced a planted stale success report. Existing unit tests: 104 passed,
0 failed, 0 skipped on the exact task worktree. Harness compilation succeeded with the pre-existing missing
Assembly-CSharp-firstpass.dll reference warning; it is not a warning-free full mod build. No production mod source,
toggle execution, version, package, or release was changed.

Needed next for local blueprint verification: UnityEngine.SharedInternalsModule.dll from the same game installation.
Assembly-CSharp-firstpass.dll is also referenced by ProfileAudit but absent locally; include it if that installation
ships it. Further missing dependencies may become visible once the first loader failure is resolved. This gate is
about reproducing reader behavior before enabling modifier management, not about claiming the CLR cannot compile
new C# without these files. Safe hypothetical state evaluation, command dispatch, and phases 1-3 remain outstanding.
