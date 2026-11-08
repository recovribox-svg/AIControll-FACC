# Class competence programme — current register

Started 8 November 2026 from **0.3.30 test 2**, development commit
`71ca3de04d97d8a4ce14f7ab5bb75bcc84488504`, on `codex/class-competence-foundation`.
The manifest stays `0.3.30`. This is the programme's foundation and first improvement stage,
not certification of the game's entire class roster. Historical audit snapshots are preserved.

## State of the programme

| Checkpoint | Result / remaining boundary |
| --- | --- |
| A — establish reality | Current development fetched; no open PRs at intake; isolated worktree. Source, actual supplied native DLLs, restored exports and old gameplay logs inventoried separately. |
| B — broad coverage map | [Class matrix](competence/CLASS_MATRIX.md): 26 provisional base-family planning rows; export-verified Kineticist plus seven archetype deltas and progression breakpoints. Other actual class/archetype/prestige/DLC/mod inventory remains **unknown**, not inferred from a remembered roster. |
| C — material mechanisms | [Modern pipeline, all eleven mechanic families, native hybrid/form/resource contracts and preservation cases](competence/MECHANIC_CONTRACTS.md) inspected. Source-only concerns are distinct from concrete native/export/fixture findings below. |
| D — prioritize | Register below groups work by the first failing boundary, affected mechanisms, confidence, reuse, risk and evidence still needed. |
| E — implement | Selected batches: save-boost probability composition; component-derived burn consequences; area-grant path identity. All three are implemented; [combined validation and per-batch contracts](competence/IMPLEMENTATION.md) record scope and remaining limits. |
| F — combined validation | Fresh baseline and combined checks reproduced: 518 tests, real-DLL build 0 warnings/errors, 803-file reader run 0 errors, 53 new production-reader/route checks, plus 9 inventory tests. Package identity is in the task test-build handover. Gameplay and matched Unity performance remain pending. |

This document supersedes the use of old coverage percentages as a current competence claim.
The older `CLASS_COMPETENCE_AUDIT.md` and `COVERAGE_AUDIT.md` describe their own inputs/revisions.
Many of their findings were already fixed in later development. The [reconciliation table](competence/CLASS_MATRIX.md#historical-claims-reconciled-with-live-code)
records those fixes; this programme does not reimplement them.

## Checked candidate

[UMM test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-competence1-4f8a8b7-UMM-test.zip?raw=true) · [package identity](../test-builds/AI-Control-FACC-v0.3.30-dev-competence1-4f8a8b7-UMM-test.identity.json) · [implementation and checks](competence/IMPLEMENTATION.md).
Runtime source `4f8a8b72581ff783fc8a7d8f54005bdb3f189a9c`, DLL MVID `a22f23e0-64b2-4b76-b6bb-aa274edac6a8`.
Packaged bytes match the checked build; the archive contains only the mod DLL and unchanged manifest.
This task-branch candidate awaits review/integration and paired gameplay/performance acceptance.

## Baseline and evidence

Fresh checks of the unmodified starting source (compact [machine-readable evidence identity](competence/BASELINE.json)):

| Check | Result | Limit |
| --- | --- | --- |
| Game-independent suite | **493 passed, 0 failed, 0 skipped** | .NET fixture validation, not Unity gameplay |
| Release build against supplied real game assemblies | **0 warnings, 0 errors** | Supplied native fingerprint; current installed game patch is unconfirmed |
| Strict current ProfileAudit | **803 loaded, 0 failed; 287 ability/toggle rows; 0 reader errors** | Partial input graph; this is a reader result, not competent selections/execution |
| Independent raw reference closure | **147 distinct nonzero missing `!bp_` references**, 695 occurrences in 214 files | Syntactic references absent from this export; not all necessarily traversed by the readers |
| Current gameplay / CPU / allocation profile | **Not supplied or run** | No current whole-party/runtime/performance acceptance |

Nineteen supplied assemblies were used without compile stubs. `Assembly-CSharp.dll`:
SHA-256 `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`,
MVID `90a9869c-2792-4c7b-bfb7-5a8b33da7c82`, assembly version `0.0.0.0`;
no exposed product version establishes the user's installed game patch. Supplied UMM is `0.33.2.0`.
Restored `UnityEngine.SharedInternalsModule.dll` and `Unity.Mathematics.dll` complete the available native reference set.
The author reports using Tabletop Tweaks (Core/Base), Isekai, Psionics, MysticalMayhem, TomeOfTheFirebird, DarkCodex and Demonology. This is a partial user-reported installed-mod list, not a checked complete startup manifest. DarkCodex is user-reported **v1.9.6 with Mind Shield disabled**; other exact versions, installed DLL identities/settings and an initialized current save are unavailable. Presence is recorded independently of compatibility. [Checked public source leads](competence/MOD_EVIDENCE.md) fingerprint candidate upstream revisions separately; they expose TTT custom spending/Magus changes, DarkCodex Mind Shield, Isekai inherited parts and ranked Psionics focus, without asserting those revisions are installed. Mod-generated/patched roster data requires an export after initialization of the actual modded game.

The supplied export is **800 Kineticist assets plus three supplements** (Color Spray and the DLC3 ricochet buff/property),
803 unique IDs. Export presence does not prove DLC enabled, an actor possessing the feature, or native legality.
ProfileAudit does not itself count unresolved references. Its baseline explicit skips are **AFFLICTION** and **SWARM-AREA**;
Enervation is absent, ONHIT has no relevant sample, and STAT-LINKS reads 704 facts but **zero positive linked-fact samples**.
The four expected named stat-link fixtures are absent. No optional provider DLL was supplied.
Harness-only RunnableStubs enable offline reader loading and are excluded from deployable builds/packages.

The checked existing test-2 ZIP has SHA-256
`e658b0571543a1e141b1090e3bf601333449af1cd9012d1d85fc6323d616a958`, DLL SHA-256
`cc93ff252ac52ff6256852d6f4277cee606affaacf5a2a50a8668785f0fa9940`, MVID
`05361eac-e11f-4c14-a363-3355229923c2`. A fresh baseline build has a different MVID
(`99b8e985-f47d-43e9-8556-1f5d6865f8fd`); do not confuse compiled source validation with that shipped binary.
Restored AI/combat logs identify **0.3.28 test 3**, MVID `09bc562d-a4dc-4c51-a8ff-ebc2e6f1cf3a`.
They are inherited gameplay evidence only, including their old custom classes/mod setup.

## Independent competence states

Keep these columns separate for every actual actor/mechanism/scenario. The machine-readable
[matrix](competence/class-matrix.json) preserves unknowns rather than replacing them with a single supported flag.

| Dimension | Allowed distinctions | Current foundation default |
| --- | --- | --- |
| Actor availability | absent / unavailable / available / unknown | Unknown until the current actor kit is observed |
| Permission | AI-disabled / user-blocked / player-owned / game-automated / AI-selectable | Conditional: actual control settings, blocklists and native ownership apply |
| Understanding | modeled / partial / fallback / unsupported / unknown | Per mechanism; an export or readable buff alone is insufficient |
| Decision / execution | not tested / declined with comparison / selected / issued / acted / native outcome / defective evidence | Current gameplay not tested |
| Validation | source / native / fixture / live / performance / pending | Listed per finding; offline proofs never upgrade live/performance |

Immediate native rage, recipient acceptance and externally automated states are ownership cases,
not evidence of a discovery failure. An absent unlock or prohibited ability needs no score bonus.
Unknown conditions are not established 50% probabilities, even where the inherited model uses half weight.

## Prioritized findings and dispositions

Priority reflects consequence, prevalence confidence, shared benefit, implementation cost and regression exposure;
it is not a class popularity ranking. Class names indicate candidate consumers, not confirmed availability on the current save.

| ID / first boundary | Evidence / affected mechanism | Disposition and next evidence |
| --- | --- | --- |
| C01 — boost probability | Current `BoostGain` adds DC to composite SR/hit/shadow/save probability; +1 DC at save-fail .5 and SR .5 incorrectly predicts .25→.30 rather than .25→.275. Shared next-spell DC boosts; actual consumer roster pending. High-confidence arithmetic/source gap, low bounded repair cost. | **Implemented batch 1.** Adjust the native modeled save roll before caps/Persistent, and compare save outcome factors; preserve other defenses. Test half-save/evasion, Persistent and cap transitions. Shadow disbelief uses its own DC-dependent Will factor; it is part of the before/after comparison, unlike SR/hit chance. |
| C02 — burn consequence | Actual Psychokineticist trigger/buff graph applies burn-dependent Will/skill penalties; native `AcceptBurn` raises handlers and does not unconditionally damage HP. Current generic burn price assumes character-level nonlethal for every native part. Export/native/source confirmed archetype replacement. | **Implemented batch 2.** Read supported active burn-change component graphs and rank deltas, price actual modeled nonlethal/save consequences. Keep unknown graphs explicit; do not infer from class label/main stat. Skill tactical utility remains unmodeled. DarkCodex can replace this property getter. Its v1.9.6 initialization gate skips the entire replacement when Mind Shield is disabled, as the author reports; a session initialized with that setting is expected to retain the native graph. The reader still verifies the actual active shape; installed bytes/post-initialization graph remain unverified. |
| C03 — area-grant discovery | Production reader on synthetic native-layout graphs retains 1 of 2 opposite branches, distinct same-depth checker paths, or distinct save contexts. Identical enter/round paths correctly collapse. Native area actions execute their own paths. High-confidence structural reader gap; actual class prevalence unknown. | **Implemented batch 3.** Deduplicate by the modeled path identity, preserving checker identity, branch polarity and nearest save-action identity. Full custom-DC/nested-save semantics remain partial. No blanket save-follow flag change. |
| C04 — joint setup/follow-up cost | Quicken coupling chooses individually affordable spells but does not reserve shared spontaneous slots/resource costs for boost+cast+second cast. Native spell spending consumes `SpellSlotCost` entries. Shared caster/hybrid mechanism; current save sample absent. | **Scheduled next action/resource stage.** Build read-only joint cost descriptors and replay last-slot/shared-pool cases, including booster cost and main cast. Do not grant the extra action value before all linked costs are feasible. Native conversions/mod resource logic need concrete graphs. |
| C05 — toggle/area appraisal | Source uses `followSaves:false` for toggles, different ally/enemy save weighting for auras, and merges grants under max guard weight. Negative carried-aura value is dropped by a positive-only caller. Flat harmful fallback and unknown guard weights lack effect probability evidence. | **Scheduled composed-grant stage**, after C03. Need complete save-dependent helpful/harmful/ally-harm graph and recipient fixtures; preserve nonstacking comparisons. Simply weighting each buff separately can double-count overlapping bonuses. |
| C06 — recurring cost / stopping | Native activatable `ResourceCount` is resource quantity; drain depends on Start/NewRound/Attack/Judgment/Minute/Hour and cost modifiers. Current toggle horizon/stop path treats resource quantity generically as ongoing rounds/drain. Native/source gap; relevant graph absent from current export. | **Scheduled lifecycle stage.** Obtain performance/judgment resource graphs; separate activation, already-paid and recurring cost, then test expiry/exclusivity/stop/restore. Preserve game-started rage and acceptance ownership. |
| C07 — Magus combination | Native Spell Combat controller reacts to casts/attacks; Spellstrike factory/held-charge and ranged stored-ray rules alter command/weapon semantics. Current independent ability/attack forecasts do not model the complete combination. Sticky-touch initial casts already autoqueue native attacks. | **Scheduled hybrid contract stage.** Need actual class/toggle/charge/bow graphs and runtime state. Use safe read-only native queries, bounded feasible continuation and native command factory where applicable; keep internal command exclusions until execution is proven. |
| C08 — form/enchantment replacement | Current-body native attacks and simple added claw buffs exist; future polymorph replaces attacks/equipment/body/feature/casting state. Weapon enchantment pools have exclusivity and held charge/weapon dependencies. A raw buff sum cannot establish the changed routine. | **Scheduled replacement-state stage.** Obtain representative form and enchantment graphs; build immutable forecast of replacement versus additive effects and return action. No scoring-time transformation/equipment mutation. |
| C09 — mutable cache dependencies | Refill memo caches target-sensitive blocked-blast value by fight/round/ranks; resource/policy/target/death state may change within that key. Stat-use cache also has intra-round fact/equipment dependencies. Source-confirmed omitted dependencies; current live consequence/performance unmeasured. | **Scheduled cache-lifetime stage.** Prove actor transition cases; choose decision-local values or explicit complete revision inputs. Preserve bounded blast search and measure matched RT/TB cost before changing its amortization. Policy-only key additions do not solve the whole dependency set. |
| C10 — Fast Bombs / precision | Bomb diagnostic intentionally models one throw; native multi-throw resource/chain behavior needs a bounded full routine. Native precision dice and critical/fortification/on-hit gates can differ from current approximations. | **Scheduled martial/alchemy stage.** Pair actual native routine/resource/eligibility probes with positive/immune/partial-fortification and low/high chain cases. No invented multiplier for a class label. |
| C11 — Overwhelming Soul / unique kinetic deltas | Actual alternate burn, Mental Prowess reducer/consumption, Elemental Engine thresholds and Blood Kineticist actions differ from base progression. Plain blast support is not sufficient evidence. | **Scheduled unique-mechanism stage.** Native component and actual availability contracts first; C02's supported burn shapes must not be presented as complete alternate-archetype competence. |
| C13 — on-hit eligibility / mutable metadata | Current affliction forecast drops native per-weapon/trigger gates and approximates repeated hit probability; its structural/DC cache can miss fact/context changes. Native/source inspected, current actor prevalence unknown. | **Scheduled shared attack-trigger stage**, alongside C10. Preserve descriptor/provenance and deterministic eligibility; calculate per-attack outcomes using the existing routine. Pair weapon-mismatch/first-hit/repeated-hit and same-round fact/DC transitions; do not trigger native on-hit events during appraisal. |
| C14 — indirect grants / recipient subject | Caster-owned fact recognition exists, but teamwork half credit does not establish an eligible participating partner. Recipient `ForbidSpellCasting.IgnoreFeature` is not retained in shared buff parts; non-Personal caster-heal and pet-redirection appraisal need graph-specific review. | **Scheduled support/dependency stage.** Preserve grant provenance and evaluate real eligible recipients/partners at decision time; test absent/ineligible partner, spell-prohibition exemption, actual caster-side heal and pet subject. Source-level concern is distinct from demonstrated current actor failure. |
| C12 — roster / installed mod evidence | Only one class's class/progression export is present. Prestige classes, other archetypes, selections, current DLC/mod grants and runtime kit are unestablished. | **Evidence pending, active inventory work.** Use the local [BlueprintInventory tool](../tools/BlueprintInventory/README.md) to send a derived metadata manifest, then request only referenced distinct mechanism graphs. Do not request a whole-game upload or claim the 26 planning rows are exhaustive. |

The [mechanism contracts and replay matrix](competence/MECHANIC_CONTRACTS.md) give source/native boundaries,
first failure points and positive/negative/transition cases for all eleven families, including passive marker dependencies
and mod-specific resources. They require individual graph/recipient contracts; none is silently promoted to confirmed class-wide failure.

## Preservation, performance and validation boundaries

The selection keeps the modern TB/RT pipeline, native legality/action types, player blocklists/acceptance,
external automation, stacking/replacement, held-state restoration, marks/riders, compatible weapon follow-ups,
bomb/material policies, observer calibration and kinetic quiver/infusion behavior. Re-inspection found substantial
current support, including Fervor metamagic-different conversions, Cackle/Chant, caster-owned grants, current
natural attack routines, unique-buff old-holder loss, planned-shot reserves/refills, crit/Deadly Aim, defensive rank
increments, Soul Power and decision-local context reuse. Those are preservation results, not fresh gameplay certificates.

Selected changes must use existing safe read-only component/rank/parameter queries. No native cast, arbitrary
condition evaluator, equipment change or shared mutable MechanicsContext is allowed during scoring. No new
class rotation, GUID bonus table, movement/target commitment/retreat/pathfinding/cancellation behavior or speculative
door restoration is included. Ardub remains deferred. Movement proposals still require the author's discussion.

Per-batch contracts cover scenario dimensions, work complexity, cache key/lifetime/invalidation, tactical differences
and rollback in [the implementation notes](competence/IMPLEMENTATION.md). Offline repeated measurements may compare harness arithmetic/reader cost;
they cannot establish Unity decision latency, GC or allocations. No new search cap, time budget, reduced tactical
quality or invented millisecond acceptance threshold is introduced. Matched current Unity baseline/test runs are needed
for performance acceptance, with the same save, actor kit, encounter and settings, both TB and sustained RT.

## Next evidence and gameplay acceptance

1. Current game patch/DLC, versions of the reported mods and any other enabled content mods; current AI settings/control/blocklists. The old log's mod setup is not assumed current.
2. Run the inventory tool against the current local exported blueprint root (or supply only class/archetype/progression metadata). Its derived report establishes the actual export roster and missing links without sending the whole game.
3. For the next selected family, provide only requested referenced features/buffs/areas/resources, plus applicable mod source/DLL/configuration when patched behavior matters.
4. Replay the uniquely identified test build: positive, negative and transition cases in both TB and RT. Capture startup MVID, full AI and native combat logs; observe selected→issued→acted→native outcome, and explain declines using competing values and costs. Use current saves/profile captures when performance is a conclusion.

Use the [paired replay matrix](competence/MECHANIC_CONTRACTS.md#focused-positive--negative--transition-replay-matrix) for broader regression scenarios and the implementation notes for the selected repairs.

The broad audit remains open until the actual roster and distinct high-impact mechanisms have evidence-backed dispositions.
The selected offline improvement stage can complete while those missing inputs and gameplay acceptance remain explicit.
