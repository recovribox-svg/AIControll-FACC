# Class competence programme — current register

Started 8 November 2026 from **0.3.30 test 2**, development commit
`71ca3de04d97d8a4ce14f7ab5bb75bcc84488504`, on `codex/class-competence-foundation`.
The manifest stays `0.3.30`. The programme now includes the reviewed foundation and a roster-backed mapping stage.
Actor competence and installed-mod compatibility remain separate evidence. Historical audit snapshots are preserved.

## Current mapping stage (8 November 2026, Codex; docs/data only)

Task branch `codex/class-competence-map`, starting from development **`fdc5e5e0906a4afc7a1af22cebbc29c0ed8d7e20`**.
[The matrix](competence/CLASS_MATRIX.md) now uses actual exported progression/delta roots: **74 class assets, 213 archetypes, 994 selection/progression interaction deltas**, with **9,426 feature/level/role rows** and **3,071 signatures**. Categories are 27 base candidates, 13 prestige, 15 mythic-flagged and 19 other/pet/NPC/prototype assets. The extra two classes and Penta archetype come from Units; they do not increase the playable base-class count. The native playable-class whitelist is absent.

| Evidence | Current result / limit |
| --- | --- |
| Inputs | 44,648 JBP files, 44,638 unique assets, ten Data-identical copies, zero conflicting GUIDs/JSON failures |
| Bounded map | Depth 6 per feature root; 13,671 fact nodes. 66 missing reachable refs, 1,089 depth-frontier refs and 6,177 unclassified relation occurrences remain explicit |
| Full raw reference inventory | **2,056 missing nonzero IDs / 37,866 occurrences**; distinct from the smaller bounded grant/effect graph. No closed-corpus claim |
| Real reader | Strict: 44,637 loaded / one Unity-bound Warpriest failure, no verdicts. Separate **PARTIAL** diagnostic allowance of that exact one file: **4,807 rows / zero reader errors**, with 8/8 burn-shaped kits and newly available positive on-hit/affliction/swarm/stat-link samples |
| Offline tooling | **24 Python tests** and four synthetic assembly-index checks pass. No runtime AI source, version or package changed in this stage |
| Installed mods | Supplied DLL/settings identities and all eight byte-identical loader caches inspected. Generated Isekai/TTT Base/Demonology GUID-map assets are absent from the folders; installed-code construction hints/settings are separate from the exported roster |

[Per-feature rows](competence/feature-mechanics.csv) · [complete signature prevalence](competence/signature-prevalence.csv) ·
[normalized catalog](competence/mechanic-catalog.json) · [validation/limits](competence/PHASE_B_VALIDATION.md) ·
[installed configuration](competence/INSTALLED_MODS.md).
Counts without selection edges and possible selection exposure are separate; prerequisite/removal references do not become grants. Typed source matches are partial reader/query evidence, not a coverage percentage.

**Coordination:** Claude owns C07 dispatch/Spellstrike/Spell Combat and C08 forms. Codex's next runtime stage is C06, in a separate PR **after this mapping PR is open**. Gameplay and matched Unity performance remain pending.

## Integration and review follow-up (8 November 2026, Claude)

PR #20 was reviewed against this register, its linked notes, the original programme text and the production callers,
then merged into development (`452c761`) with follow-up commits `cf62263`, `4c63578` and `7e89a16`. Current test package:
[`AI-Control-FACC-v0.3.30-dev-competence2-7e89a16-UMM-test.zip`](../test-builds/AI-Control-FACC-v0.3.30-dev-competence2-7e89a16-UMM-test.zip?raw=true)
(MVID `0ed42ac5-d9ce-4f88-a339-388028b036c3`; [identity](../test-builds/AI-Control-FACC-v0.3.30-dev-competence2-7e89a16-UMM-test.identity.json)).
The review reproduced the candidate's 518 tests, real-DLL build, package bytes and 11 added references, then found:

| Review finding | Evidence | Disposition |
| --- | --- | --- |
| F1 (high) — the burn reader rejected real kineticist kits | `ElementalOverflowProgression` grants `ElementalOverflowEffectFeature` at class level 6: a second burn-change trigger whose buff is fortification plus stat-choice fact actions. The Adaptation talents add energy-resistance triggers. The PR's compiled reader on actual blueprints: BurnFeature alone modeled, BurnFeature + Overflow effect **unknown**; Psychokineticist alone modeled, + Aerial Adaptation **unknown** (phantom HP cost back). The fixture had read each feature alone. | **Fixed** (`cf62263`): benefit-only burn buffs (fortification, energy resistance, fact actions that set shared values and give or take back their own non-harmful buffs) are set aside; anything else stays unknown. ProfileAudit: 8 actor-shaped kits + a damage-dealing negative. |
| F2 (medium) — Psychokineticist burn had no day-long cost | With no nonlethal loss there was no day cost, and the Will term existed only under observed danger, over at most 4 rounds; a unit test asserted 0. The native penalty buff is `RemoveOnRest`, and each burn spends the daily pool. | **Fixed** (`cf62263`): the existing per-rest price of the burn itself (burn × character level × 0.1, as before the reader) is charged for every part, independent of the consequence. Programme §6: existing resource restraint preserved, not silently removed. |
| F3 (low) — next-spell Persistent kept the C01 error | `MetamagicGain` halved the gain for half-on-save damage and ignored Evasion. Native disbelief (`MechanicsContext.TryAffectByShadow`) is `RuleSavingThrow(Will, Params.DC)` with no Persistent re-roll, but the forecast applied Persistent to it. | **Fixed** (`4c63578`): `ValueModel.PersistentBoostShare` through the same save-effect factor; disbelief never Persistent. |
| F4 (low) — "identical enter/round paths collapse" | Identity is the checker object; loaded UnitEnter/Round lists never share one. 17 of 297 area run-actions in the available exports give the same guarded buff on both (Inspired Rage, Inspire Ferocity, Judgement aura, Incite Rage...). Consumers take distinct buffs and the largest weight: no value counts twice; one more condition check per recipient. | **Documented** (`7e89a16`); toggle description lists each grant once. Structural checker comparison deferred: small cost, real risk of a wrong equality. |
| F5 (low) — inventory dropped prerequisite gate values | Arcane Trickster's `PrerequisiteStatValue` / `PrerequisiteCasterTypeSpellLevel` rows had no stat, value or spell level. | **Fixed** (`7e89a16`): every scalar gate field kept; new fixture. |
| Fallback log every round | `ReportOnce` keyed by round at normal log level. | **Fixed**: once a fight. |

Programme comparison (`AI_Controll-Competence-Programme.txt`, supplied by the author after the review): the batches follow its
rules (§2, §8: no movement, ownership, blocklist, version or release change). Gaps against it were the actor-kit evidence of §4/§11
(F1), the per-rest policy of §6 (F2), the Phase B roster (§4, §12 — now [derived](competence/ROSTER.md), see C12) and the
Phase D ordering (§10 — re-ranked below).

## State of the programme

| Checkpoint | Result / remaining boundary |
| --- | --- |
| A — establish reality | Current development fetched; no open PRs at intake; isolated worktree. Source, actual supplied native DLLs, restored exports and old gameplay logs inventoried separately. |
| B — broad coverage map | [Roster-backed matrix](competence/CLASS_MATRIX.md), complete per-feature/prevalence tables and installed-source evidence replace the 26 provisional rows. Available export rows are mapped within declared bounds; generated mod graphs and native actor availability remain open. |
| C — material mechanisms | [Modern pipeline, all eleven mechanic families, native hybrid/form/resource contracts and preservation cases](competence/MECHANIC_CONTRACTS.md) inspected. Source-only concerns are distinct from concrete native/export/fixture findings below. |
| D — prioritize | Register/ranking below, now informed by [direct versus selectable signature prevalence](competence/PREVALENCE.md). Phase D severity/confidence/reuse/risk still outrank raw exposure. C07/C08 belong to Claude; C06 is the next Codex runtime stage. |
| E — implement | Selected batches: save-boost probability composition; component-derived burn consequences; area-grant path identity. All three are implemented; [combined validation and per-batch contracts](competence/IMPLEMENTATION.md) record scope and remaining limits. |
| F — combined validation | Foundation/runtime checks are historical below. This docs/data stage ran 24 Python checks, four assembly-index checks, and strict plus explicitly partial 44,648-file production-reader runs. [Exact checks](competence/PHASE_B_CHECKS.json); no new gameplay/Unity performance acceptance. |

This document supersedes the use of old coverage percentages as a current competence claim.
The older `CLASS_COMPETENCE_AUDIT.md` and `COVERAGE_AUDIT.md` describe their own inputs/revisions.
Many of their findings were already fixed in later development. The [historical reconciliation table](competence/HISTORICAL_RECONCILIATION.md)
records those fixes; this programme does not reimplement them.

## Foundation candidate identity (historical)

**Superseded by competence2** (see the integration section above); kept for the record.

[UMM test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-competence1-4f8a8b7-UMM-test.zip?raw=true) · [package identity](../test-builds/AI-Control-FACC-v0.3.30-dev-competence1-4f8a8b7-UMM-test.identity.json) · [implementation and checks](competence/IMPLEMENTATION.md).
Runtime source `4f8a8b72581ff783fc8a7d8f54005bdb3f189a9c`, DLL MVID `a22f23e0-64b2-4b76-b6bb-aa274edac6a8`.
Packaged bytes match the checked build; the archive contains only the mod DLL and unchanged manifest.
This task-branch candidate awaits review/integration and paired gameplay/performance acceptance.

## Foundation baseline and evidence (historical)

The inputs and absence statements in this section describe foundation intake only. The current mapping-stage inventory above supersedes them.

The following is the original 71ca3de/foundation input snapshot; current expanded inputs and checks are above. Fresh checks of that unmodified starting source (compact [machine-readable evidence identity](competence/BASELINE.json)):

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
| C01 — boost probability | Before the foundation, `BoostGain` added DC to composite SR/hit/shadow/save probability; +1 DC at save-fail .5 and SR .5 incorrectly predicts .25→.30 rather than .25→.275. Shared next-spell DC boosts; actual consumer roster pending. High-confidence arithmetic/source gap, low bounded repair cost. | **Implemented batch 1.** Adjust the native modeled save roll before caps/Persistent, and compare save outcome factors; preserve other defenses. Test half-save/evasion, Persistent and cap transitions. Shadow disbelief uses its own DC-dependent Will factor; it is part of the before/after comparison, unlike SR/hit chance. **Integration (competence2):** the next-spell Persistent path now shares the same factor (F3), and disbelief is never Persistent. |
| C02 — burn consequence | Actual Psychokineticist trigger/buff graph applies burn-dependent Will/skill penalties; native `AcceptBurn` raises handlers and does not unconditionally damage HP. The original generic burn price assumed character-level nonlethal for every native part. Competence2 now retains the per-rest burn price while modeling supported actual consequences. Export/native/source confirmed archetype replacement. Installed settings also enable DarkCodex Elemental Scion, which replaces a burn-value property; its absent generated/post-load graph must be verified before applying native-shape claims to that configuration. | **Implemented batch 2.** Read supported active burn-change component graphs and rank deltas, price actual modeled nonlethal/save consequences. Keep unknown graphs explicit; do not infer from class label/main stat. Skill tactical utility remains unmodeled. Installed v1.9.6 code/settings now show an independent Elemental Scion replacement of the burn-number getter even with Mind Shield disabled; see [installed evidence](competence/INSTALLED_MODS.md). The reader verifies actual active shapes; the expected post-load mutation is absent from these exports and remains unverified in-game. **Integration (competence2):** read on actor-shaped kits, not single features (F1: Overflow from level 6, Adaptation talents); the per-rest burn price is kept for every part (F2). Dark Elementalist and Overwhelming Soul apply the ordinary `BurnEffectBuff` and are modeled; Elemental Engine's conditional Burnout stays unknown. |
| C03 — area-grant discovery | The pre-foundation reader on synthetic native-layout graphs retained 1 of 2 opposite branches, distinct same-depth checker paths, or distinct save contexts. Guarded enter/round copies retain distinct checker identities (F4). Native area actions execute their own paths. High-confidence structural reader gap; actual class prevalence unknown. | **Implemented batch 3.** Deduplicate by the modeled path identity, preserving checker identity, branch polarity and nearest save-action identity. Full custom-DC/nested-save semantics remain partial. No blanket save-follow flag change. **Integration (competence2):** guarded enter/round copies stay two paths (F4); value unaffected. |
| C04 — joint setup/follow-up cost | Quicken coupling chooses individually affordable spells but does not reserve shared spontaneous slots/resource costs for boost+cast+second cast. Native spell spending consumes `SpellSlotCost` entries. Shared caster/hybrid mechanism; current save sample absent. | **Scheduled next action/resource stage.** Build read-only joint cost descriptors and replay last-slot/shared-pool cases, including booster cost and main cast. Do not grant the extra action value before all linked costs are feasible. Native conversions/mod resource logic need concrete graphs. |
| C05 — toggle/area appraisal | Source uses `followSaves:false` for toggles, different ally/enemy save weighting for auras, and merges grants under max guard weight. Negative carried-aura value is dropped by a positive-only caller. Flat harmful fallback and unknown guard weights lack effect probability evidence. | **Scheduled composed-grant stage**, after C03. Need complete save-dependent helpful/harmful/ally-harm graph and recipient fixtures; preserve nonstacking comparisons. Simply weighting each buff separately can double-count overlapping bonuses. |
| C06 — recurring cost / stopping | Native activatable `ResourceCount` is resource quantity; drain depends on Start/NewRound/Attack/Judgment/Minute/Hour and cost modifiers. Current toggle horizon/stop path treats resource quantity generically as ongoing rounds/drain. Native/source gap; the supplied class/mythic resource graphs establish the spend-type prevalence below. | **Scheduled lifecycle stage.** Use the supplied performance/judgment graphs and installed patched spend logic; separate activation, already-paid and recurring cost, then test expiry/exclusivity/stop/restore. Preserve game-started rage and acceptance ownership. **Prevalence (competence2):** in the class + mythic exports 266 activatable abilities spend a resource: 151 `NewRound` (quantity approximates rounds only at one unit per tick), 115 otherwise — `OncePerMinute` 44, `TurnOn` 27, `Judgment` 15, `OncePerTenMinutes` 12, `OncePerHour` 8, `Start` 5, attack-based 4 — across Slayer, Shaman, Warpriest, Inquisitor, Magus, Bard, Skald, Bloodrager, Cleric and Druid. `ToggleValue` (`TacticalEngine.Toggles.cs`) uses `ResourceCount` as rounds for all of them. |
| C07 — Magus combination | Native Spell Combat controller reacts to casts/attacks; Spellstrike factory/held-charge and ranged stored-ray rules alter command/weapon semantics. Current independent ability/attack forecasts do not model the complete combination. Sticky-touch initial casts already autoqueue native attacks. | **Scheduled hybrid contract stage.** Use the supplied class/toggle/charge/bow graphs; actual actor state and replay remain required. Use safe read-only native queries, bounded feasible continuation and native command factory where applicable; keep internal command exclusions until execution is proven. **Confirmed execution gap (competence2):** native `UnitUseAbility.CreateCastCommand` turns a held Magus touch charge into a `UnitAttack` when Spellstrike is active, the spell is on the Magus list, the target is an enemy and a threat hand exists. The mod has no Magus handling and `Execute` (`TacticalEngine.cs`, ability case) always issues `new UnitUseAbility`, so a charge held after a miss is delivered as a touch spell, never through Spellstrike's weapon attack. The cast itself is legal; weapon damage and the weapon's crit range are lost. Magus mechanics come from `AddMagusMechanicPart` (Magus, Eldritch Scion: 8 features). Fix needs the weapon+spell forecast, `UnitAttack` outcome attribution and the single/full-attack rule together. |
| C08 — form/enchantment replacement | Current-body native attacks and simple added claw buffs exist; future polymorph replaces attacks/equipment/body/feature/casting state. Weapon enchantment pools have exclusivity and held charge/weapon dependencies. A raw buff sum cannot establish the changed routine. | **Scheduled replacement-state stage.** Use representative supplied form and enchantment graphs; build immutable forecast of replacement versus additive effects and return action. No scoring-time transformation/equipment mutation. **Prevalence (competence2):** 149 `Polymorph` components in the class + mythic exports (Bloodrager, Druid, Shifter, Slayer, Sorcerer, Witch, Wizard; Demon and Golden Dragon mythic paths). |
| C09 — mutable cache dependencies | Refill memo caches target-sensitive blocked-blast value by fight/round/ranks; resource/policy/target/death state may change within that key. Stat-use cache also has intra-round fact/equipment dependencies. Source-confirmed omitted dependencies; current live consequence/performance unmeasured. | **Scheduled cache-lifetime stage.** Prove actor transition cases; choose decision-local values or explicit complete revision inputs. Preserve bounded blast search and measure matched RT/TB cost before changing its amortization. Policy-only key additions do not solve the whole dependency set. |
| C10 — Fast Bombs / precision | Bomb diagnostic intentionally models one throw; native multi-throw resource/chain behavior needs a bounded full routine. Native precision dice and critical/fortification/on-hit gates can differ from current approximations. | **Scheduled martial/alchemy stage.** Pair actual native routine/resource/eligibility probes with positive/immune/partial-fortification and low/high chain cases. No invented multiplier for a class label. |
| C11 — Overwhelming Soul / unique kinetic deltas | Actual alternate burn, Mental Prowess reducer/consumption, Elemental Engine thresholds and Blood Kineticist actions differ from base progression. Plain blast support is not sufficient evidence. | **Scheduled unique-mechanism stage.** Native component and actual availability contracts first; C02's supported burn shapes must not be presented as complete alternate-archetype competence. |
| C13 — on-hit eligibility / mutable metadata | Current affliction forecast drops native per-weapon/trigger gates and approximates repeated hit probability; its structural/DC cache can miss fact/context changes. Native/source inspected, current actor prevalence unknown. | **Scheduled shared attack-trigger stage**, alongside C10. Preserve descriptor/provenance and deterministic eligibility; calculate per-attack outcomes using the existing routine. Pair weapon-mismatch/first-hit/repeated-hit and same-round fact/DC transitions; do not trigger native on-hit events during appraisal. |
| C14 — indirect grants / recipient subject | Caster-owned fact recognition exists, but teamwork half credit does not establish an eligible participating partner. Recipient `ForbidSpellCasting.IgnoreFeature` is not retained in shared buff parts; non-Personal caster-heal and pet-redirection appraisal need graph-specific review. | **Scheduled support/dependency stage.** Preserve grant provenance and evaluate real eligible recipients/partners at decision time; test absent/ineligible partner, spell-prohibition exemption, actual caster-side heal and pet subject. Source-level concern is distinct from demonstrated current actor failure. |
| C15 — slot/resource recovery effects | `AbilityRestoreSpellSlot`, `AbilityMagusSpellRecallCostCalculator` and `AbilityConvertSpellLevelToResource` have no dedicated prospective recovery model in the indexed modern readers. This is source/shape evidence, not proof an actor owns or can dispatch them. | **Scheduled contract research.** Establish native slot selection, restored benefit, conversion cost and UI/command requirements; separate replenishment from C04 spending feasibility. No speculative recovery value or C07 dispatch change. |
| C12 — roster / installed mod evidence | Current exports provide 74 class assets / 213 archetypes and their bounded feature signatures; installed binaries/settings and static construction hints are separate. Native playable roots, generated mod asset graphs, unknown relations and runtime actor kits remain unestablished. | **Export mapping delivered; remaining scope explicit.** See [matrix](competence/CLASS_MATRIX.md), [per-feature rows](competence/feature-mechanics.csv), [prevalence](competence/PREVALENCE.md), and [installed mod evidence](competence/INSTALLED_MODS.md). No class compatibility or complete mod roster follows from the source/type index. |

The [mechanism contracts and replay matrix](competence/MECHANIC_CONTRACTS.md) give source/native boundaries,
first failure points and positive/negative/transition cases for all eleven families, including passive marker dependencies
and mod-specific resources. They require individual graph/recipient contracts; none is silently promoted to confirmed class-wide failure.

## Re-ranked next stages (programme §10 Phase D order, competence2)

Order: safety/legality/execution defects, then missing class-defining capabilities, then wrong effects/targets, then
inconsistent valuation, resource/action scheduling and performance — each weighed by prevalence, confidence, reuse and risk.

| Rank | Stage | Why here | First step |
| --- | --- | --- | --- |
| 1 | C07 Magus held charge / Spellstrike | Confirmed execution gap in a class-defining mechanic (Magus, Eldritch Scion) | **Claude-owned.** Read-only forecast of the native `CreateCastCommand` result; value weapon + spell delivery; dispatch through the native factory with `IsSingleAttack`/`ForceFullAttack` set and `CastOutcome` attribution for the attack; paired TB/RT replay. Spell Combat continuation after that. |
| 2 | C06 recurring toggle costs | 115 of 266 resource toggles in 10 classes priced on a wrong horizon; start/continue/stop decisions | **Next Codex PR after mapping PR opens.** Read `SpendType` and cost modifiers; separate activation, already-paid and recurring cost; keep game-started rage and acceptance ownership. |
| 3 | C08 forms | 149 polymorph components; Shifter is a whole class | **Claude-owned.** Immutable proposed-form description (attacks, stats, size, casting ban) without `Polymorph.OnActivate`; compare against the current routine. |
| 4 | C04 joint resource feasibility | Coupled boost + cast + second cast can spend one slot twice | Read-only joint cost descriptor over existing coupling. |
| 5 | C10 / C13 precision and on-hit gates | Rogue/Slayer/Knife Master and every on-hit fact; forecast accuracy | Native die/fortification/trigger gates per attack in the existing routine. |
| 6 | C05 composed area grants | Save-dependent and mixed carriers; double-count risk | After C06 shares the toggle lifecycle. |
| 7 | C14, C11, C09 | Narrower or measurement-first | As in the register. |

The exported archetype rows now have bounded mechanic mappings and explicit inherited/add/remove exposure. Generated mod graphs, unreviewed component contracts and missing/frontier relations remain inventoried rather than declared competent. Use the signature tables to choose the next distinct native and actor-kit checks.

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

1. Current game patch/DLC and initialized actor kits/settings/control/blocklists. Installed mod versions/settings/caches are now fingerprinted in [INSTALLED_MODS.md](competence/INSTALLED_MODS.md); successful initialization and actual owned facts still require runtime evidence.
2. The author reports these exports came from modded initialization, but generated GUID-map assets and at least one expected enabled-property mutation are absent. Close that specific gap with a targeted live-object export of generated class/archetype/progression roots and referenced facts. Assembly caches and GUID registries identify code/names, not those object graphs; older mod source is unnecessary.
3. For the next selected family, provide only requested referenced features/buffs/areas/resources, plus applicable mod source/DLL/configuration when patched behavior matters.
4. Replay the uniquely identified test build: positive, negative and transition cases in both TB and RT. Capture startup MVID, full AI and native combat logs; observe selected→issued→acted→native outcome, and explain declines using competing values and costs. Use current saves/profile captures when performance is a conclusion.

Use the [paired replay matrix](competence/MECHANIC_CONTRACTS.md#focused-positive--negative--transition-replay-matrix) for broader regression scenarios and the implementation notes for the selected repairs.

The broad audit remains open until the actual roster and distinct high-impact mechanisms have evidence-backed dispositions.
The selected offline improvement stage can complete while those missing inputs and gameplay acceptance remain explicit.
