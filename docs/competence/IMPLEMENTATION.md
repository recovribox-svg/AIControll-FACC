# Selected implementation stage and validation

Runtime scope: C01 save-boost probability, C02 supported active burn consequences, C03 area-grant path identity and its duplicate-buff consumer guard. These are shared component/rule corrections, without a class rotation or new movement behavior. Runtime source `4f8a8b72581ff783fc8a7d8f54005bdb3f189a9c`; [candidate package identity](../../test-builds/AI-Control-FACC-v0.3.30-dev-competence1-4f8a8b7-UMM-test.identity.json); the manifest remains 0.3.30.

## Combined checks performed

| Check | Final result | What it establishes |
| --- | --- | --- |
| Full game-independent suite | **518 passed, 0 failed, 0 skipped** | Existing 493 plus 21 save/shadow and 4 burn valuation cases |
| BlueprintInventory suite | **9 passed** | Synthetic metadata, malformed/duplicate/partial scope, deterministic identity, output/input preservation |
| Actual-reference Release build | **0 warnings, 0 errors** | Compiles against supplied real game assemblies, no compile stubs |
| Strict ProfileAudit | **803 loaded, 0 failed; 287 rows; 0 reader errors** | Supplied partial graph and current production reader/fixture checks |
| Save boost compiled routes | **13 passed** | Production callers reach shared math/actual-DC and disbelief routes; no initialized actor/native outcome |
| Area grant production reader | **14 passed** | Synthetic native-layout graph collection; the same fixture on the preserved baseline failed 8 of 14 |
| Burn production reader | **26 passed; 2/2 actual feature fixtures** | Supplied ordinary/Psych native graphs, rank/cap/missing-held-buff transitions, repeated quotes, fail-closed conditional/custom getter/minimum/restriction shapes |
| External reference inspection | **1,658 signatures; 11 added, 0 removed versus baseline** | New references bind against the supplied native assemblies; reader reflection bindings are exercised where fixtures apply |
| Export completeness | **147 distinct unresolved nonzero references; 695 occurrences** | Separate syntactic inventory; does not mean every reference was traversed by ProfileAudit |
| Gameplay / Unity performance | **Pending** | No live command/native outcome, party competence, Mono allocations, GC or decision-latency acceptance claimed |

ProfileAudit explicitly skips AFFLICTION and SWARM-AREA on this corpus; Enervation is absent, on-hit creature facts have no positive sample, and STAT-LINKS has zero positive linked-fact samples. No optional mod-provider DLL was supplied. Its harness build succeeds with **2 pre-existing MSB3106 warnings** for its missing unused `Assembly-CSharp-firstpass.dll` reference; this is separate from the zero-warning deployable mod build. Harness-only native rewrites/stubs never enter the package.

The final full test restore refreshed a stale NU1900 advisory result through the approved proxy context; the full suite then ran without that warning. Sandbox testhost TCP denial required the approved outside-sandbox test retry. These are environment/tooling outcomes, not game runtime results.

Commands used (with local .NET 8, supplied managed directory and bounded build concurrency):

```sh
dotnet test tests/CompanionAI.Tests --no-restore -p:UseSharedCompilation=false -m:1
python3 -m unittest discover -s tools/BlueprintInventory -v
dotnet build CompanionAI_Pathfinder.csproj -c Release --no-restore -p:ManagedPath=/path/to/managed -p:UmmPath=/path/to/managed -p:UseSharedCompilation=false -m:1
```

ProfileAudit was built against that exact Release DLL, then run with its normal scratch-only RunnableStubs copies and the Kineticist/supplemental folders. This is equivalent to the `tools/ProfileAudit/run.sh` pipeline, with compiler diagnostics captured separately. Actual output/check identities are summarized in the package identity JSON. Inventory `--strict` correctly exits 1 while writing the partial-corpus report, rather than treating missing references as closed scope.

## Acceptance and performance contract

Use the positive/negative/transition cases below and the broader [TB/RT matrix](MECHANIC_CONTRACTS.md#focused-positive--negative--transition-replay-matrix). Capture startup MVID, actual actor kit/settings/resources, selected and competing values, issued/acted commands, native effects and lifecycle transitions. A correct decline and unspent resource are acceptance evidence. A passed fixture or native miss/save does not substitute for this chain.

For each selected batch, actual Unity CPU/allocation/latency acceptance remains pending a matched baseline with enough repeated cold/warm and sustained RT decisions to establish variance. Use the same save, encounter, actor kit and settings; compare existing diagnostics/native-query counts, aggregate party time and upper-tail latency/GC. Establish any numerical acceptance range from those measured distributions; no fabricated millisecond limit or new search cap is introduced. Only the explicitly permitted marginal forecast/path differences below should change tactics. Revert the corresponding bounded code/fixture portion if its runtime contract fails; no settings migration or save mutation is involved.

## C01: save-dependent next-spell boost contract

Batch implemented locally at the programme branch based on71ca3de. Files: TacticalEngine.Boosters.cs, CombatMath.cs, new ValueModel.Saves.cs, SaveBoostTests.cs, tests project link, and ProfileAudit SaveBoostCheck registration. No manifest/version/cache/Quicken changes.

### Before and after

Pre-change BoostGain added .05 per DC point to EffectChance(damage:false), a composite of spell resistance pass, shadow share, attack success and save failure. At 50% fail and 50% SR pass it predicted .25 -> .30 (20% relative gain); actual adjusted save is .50 -> .55 and composite .25 -> .275 (10%). It also multiplied this relative error by .5 for half-on-save damage, which is not the fraction of expected damage controlled by the save. At fail .50 -> .55, ordinary half damage rises .75 -> .775, a 3.333% gain rather than5%; Evasion leaves .50 -> .55 (10%), Improved Evasion plus Evasion .25 -> .275 (10%). A DC already far below the natural1 floor may gain nothing from +1DC; adding .05 to its clamped probability would invent a gain. Persistent applies after the actual adjusted d20 save: .75 -> .7975, not .80. At capped persistentfail .9975 furtherDC gains nothing.

Current production BoostGain calls CombatMath.DcBoostShare. It compares the combined ordinary-save outcome and shadow disbelief factors from the actual DC+bonus, using ParamsOf, actual save stats, observed contextual bonus/AutoPass, d20 caps, Persistent and the existing native-fact Evasion/Improved Evasion reader. Ordinary save contributes1 when absent. A shadow spell has a separate Will disbelief save that also changes with DC; at20% reality and .50→.55fail for both saves, combined .50*.60=.30→.55*.64=.352 (17.333% gain). Shadow-only spells gain .60→.64 (6.667%) even without ordinary save. CombatMath EffectChanceCore shares ValueModel.SaveEffectFactor and ShadowEffectFactor. Only SR and attack factors remain independent and cancel from the relative DC gain. Existing factor<=0 no-reduction behavior is preserved; fullreality contributes1 and no disbelief gain.

### Cost and lifecycle

Scenario: B boost candidates coupled to chosen main spell, target with F facts; supported half-damage case requires one existing EvasionOf traversal per boost; save-negates/control does not traverse facts. Each ordinary-save DC forecast has two read-only save queries (before/after) using decision-local ParamsOf and existing SaveObserver lookup. A partial-reality shadow copy reuses the ordinary Will before/after scalar pair when that is the same save, otherwise adds two Will-save queries; its existing Factor.GetInt query is evaluated once per boost, reused for both DCs. Ordinary/no-save nonshadow abilities do not make disbelief saves. Arithmetic is O(1), fact scan O(F), total O(BF) worst case for half-damage; ordinary forecast remains its existing O(F) for half-save/evasion. No actor/target cross-product, additional ability scoring, native cast, arbitrary condition execution, mutable context or persistent memo is introduced. No cache key/invalidation is needed: these outputs are not cached across decisions. Existing call-local SpellBoost descriptions and decision-local parameter reuse are retained.

Permitted tactical difference: a booster may win/lose its existing comparison because the modeled save-dependent marginal gain changes. SR/hit is not overcome by DC; DC affects shadow reality only through the modeled actual Will disbelief save. Approach-first, minimum main value, spell/item applicability, resource cost, blocked ability and action legality gates remain as before. This batch adds no search limit/time cap or reduced quality tradeoff.

CPU/GC/latency acceptance: Unity/Mono matched gameplay baseline unavailable; no millisecond or allocation claim is made. Replay same actor/spell/target/encounter/settings in TB and sustained RT; compare identical non-DC choices and only expected booster marginal differences, recording existing per-decision diagnostics and profiler timing/allocation distributions. Target/save/AutoPass/Persistent/evasion changes must update the next decision. Harness test duration is not Unity performance evidence. Rollback: revert only these save/booster/helper/test/fixture changes; no settings migration or save-state mutation exists.

### Limits preserved

The marginal share still scales the chosen main.Value; mixed effects, unaffected portions, net resource price and AoE recipients are not separately decomposed/rescored. Combined newPersistent+DC is still the existing additive MetamagicGain, without postDC cross-term. Caster-level/Empower/Maximize/duration shares and multiple independent boost-group semantics remain approximations. Do not call C01 complete general mixed-effect booster correctness. Selection/issue/native outcome/consumption/observer feedback and gameplay acceptance remain pending. Native Evasion/Improved flag identification is reused, not broadened. No class/GUID assumptions or current class prevalence are inferred.

## Selected batch C02: component-derived burn consequences

Starting source: `71ca3de04d97d8a4ce14f7ab5bb75bcc84488504` (0.3.30 test 2). Implemented locally, no commit/package by this subtask. This note is a handover for the current programme report.

### First failed boundary and native proof

The old `BurnCost` assumed `addedBurn * characterLevel` nonlethal HP loss for every native kineticist part. `UnitPartKineticist.AcceptBurn` in the actual supplied Assembly-CSharp increments AcceptedBurn, updates round state/gather state and raises burn-value handlers; it contains no unconditional health mutation. `AddKineticistBurnValueChangedTrigger` executes the active fact's Action. Thus the active native effect graph is the consequence contract.

The restored normal BurnFeature applies a permanent Replace BurnEffectBuff. Its `AddContextStatBonus(DamageNonLethal)` reads a CustomProperty ContextRankConfig; the sole `KineticistBurnPropertyGetter` multiplies AcceptedBurn by character level. At character level11, two added burn correctly add22 nonlethal.

The restored PsychokineticistBurnFeature replaces that trigger and applies a permanent Replace PsychokineticistBurnBuff. The property reads AcceptedBurn without a level multiplier; native MultiplyByModifier rank progression has StepLevel2. AddContextStatBonus multipliers -1 apply to Will and three skills. Two added burn add no HP damage and -4 Will. A low-HP actor must not be declined on an invented knockout threshold. Skill utility is deliberately still absent from the inherited combat model.

Native AddContextStatBonus includes optional Restrictions, HasMinimal and attribute-only Powerful Change handling. The reader rejects nonempty conditional Restrictions and HasMinimal; supported stats are nonattributes, so attribute Powerful Change is outside the supported shape. No arbitrary restriction/property evaluator is invoked.

### Implementation and alternatives

`TacticalEngine.BurnConsequences.cs` reads active blueprint features with direct permanent replacement buff actions. Only explicit native KineticistBurnPropertyGetter/custom-rank/AddContextStatBonus shapes are recognized. It rejects conditional actions, unsupported components/arithmetic/getters, nonreplacement stacking, restrictions, minimal-value behavior, intensified-dependent caps, and missing native bindings. No class, archetype, ability name, main stat or GUID decides runtime semantics.

The native rank progression/cap methods are pure for the admitted shape. Both before/after rank totals are evaluated; property class/character-level multipliers are honored. Runtime compares the future total against the actual held buff rank snapshot; an externally missing buff has current amount0, so the next burn prices reapplying the full accumulated penalty/nonlethal total. Missing current context becomes explicit unknown.

`BurnCost` uses real HP, preserved HP day cost and existing drop-risk calculation for actual nonlethal loss; actual weighted saves use the existing recipient policy (single save one-third, incoming*0.025, PreventionValue over the existing <=4-round horizon). It credits existing Overflow once afterward and keeps the existing knockout early return. No new day-pool scarcity formula, class preference or arbitrary multiplier was added. Daily burn scarcity beyond actual HP cost remains limited to inherited resource/restraint policy; skill losses and long-term non-HP penalties are not fully valued.

Unknown graphs retain the existing HP fallback with an explicit diagnostic, rather than being labeled free burn or certified modeled. Removing all burn cost for a non-Constitution part, recognizing an archetype GUID, and invoking its native action during scoring were rejected alternatives. Native ability affordability/MaxBurn/quiver/infusion/dispatch ownership are unchanged.

User reports DarkCodex v1.9.6 with Mind Shield disabled (report/source verification belongs to the consolidated validation). Public DarkCodex Mind Shield can replace this property getter with PropertyMindShield and fact-dependent arithmetic. An enabled/custom getter is explicitly unsupported by this reader and follows the visible legacy fallback. This batch is not blanket installed-mod certification; actual runtime component graphs decide admission.

### Performance and lifetime contract

Scenario dimensions: actor active facts/components, direct burn trigger actions/buffs/stat parts, distinct quoted burn totals, candidate/target count, and repeated TB/RT decisions. One TacticalContext owns one lazily read active-fact graph plus scalar AcceptedBurn/ClassLevel/CharacterLevel/current-held-rank snapshots. Initialization is O(active components + admitted graph); no target scan is added. Only requested burn totals are memoized within that decision. First quotes do bounded pure rank/cap reflection per costed stat (skills are structurally recognized but skipped during cost math); repeated quote lookup is O(1). No new tactical/search cap is introduced.

The dictionary cannot survive a decision. All actor facts, equipment/level, accepted burn, resource, current buff/context and policy changes invalidate naturally on the next Decide; there is no fight/round global model, callback or mutable context sharing. Native component/config references are read only; current live rank is copied as an integer. Existing KineticPriceBook continues to reuse complete prices. No native cast, buff/equipment mutation, live property evaluator or condition evaluator occurs during scoring.

Acceptance: structural work scales with actual active graph/unique quoted burn values and does not repeat for every aim/target. Matched Unity CPU/GC/latency remains unmeasured and needs the same save/kit/settings in TB and sustained RT; no invented millisecond threshold or allocator result is claimed. Revert this batch's reader/helper and BurnCost/TacticalContext wiring for rollback; no settings/schema migration is involved.

### Focused acceptance replay

Both TB and RT: normal feature at multiclass level (classlevel differs from totallevel), low real HP with temporary HP present, one/two burn alternatives and near-knockout; compare actual nonlethal change, selected alternatives and preserved restraint. Psych native shape: low real HP, dangerous Will-save encounter vs no incoming danger; confirm no invented HP loss and actual -2Will/burn. Check unchanged and multiple accepted-burn quotes, buff absent/reapplied, rest reset, fact/progression replacement and next-decision invalidation. With enabled Mind Shield/custom property, verify explicit unsupported diagnostic/fallback rather than native-shape certification. A player-blocked/unavailable ability must remain unavailable; no command mutation or native price override should appear.

## Area-grant path identity batch (C03)

### Before/after and evidence

The first failing boundary is the shared production action reader, before beneficiary appraisal. `CollectGrants` retained a buff when no earlier grant had the same buff and guard-count. This collapsed true/false branches, unrelated checker objects at equal depth, and save/no-save/type distinctions. Native `AbilityAreaEffectRunAction` executes each graph's actual branches. Native `ContextActionSavingThrow` supports custom/conditional/inherited DC, so two same-type wrapper objects cannot be assumed identical.

The correction compares the buff, ordered checker object identities and their true/false polarity, save enum and nearest saving-action object identity. It retains distinct modeled paths, and still deduplicates an identical enter/round path. No condition or native game action is executed. SaveAction is retained only for identity: custom DC, nested outer saves, succeed branches, probability composition, followSaves=false for toggles, unknown guard weights and continuing costs remain separate limits, not repaired claims.

Native action graphs and private production reader are exercised by `tools/ProfileAudit/AreaGrantPathCheck.cs`. The same compiled fixture run against the preserved baseline DLL gives 14 cases / 8 failures, and against the changed DLL gives 14 cases / 0 failures. Cases cover opposite branches, same-depth unrelated checkers, repeated identical branches, ordered nested guards, repeated unguarded enter/round actions, distinct buffs, different saves, separate same-save wrappers, repeated identical wrapper, unsaved/saved distinction, unchanged followSaves=false exclusion, unchanged ToCaster exclusion, and retained guard/save field identities. No actors, contexts, native condition evaluator, casts or blueprint cache mutations are involved. These are synthetic native-layout fixtures; they do not establish affected class prevalence or live behavior.

The additional path entries can repeat one buff in an aura's reaching list. Helpful aura/toggle appraisal already uses distinct buff identities. `AuraValue` now also passes distinct buff identities to ally-harm and enemy-harm `HarmShare` calls, preserving their set semantics: `DebuffShareIn` sums the supplied parts and must not multiply a single buff's penalty because it has several grant paths. Code inspection verifies both harmful consumer call sites; max guard/save weighting is unchanged and remains C05. No broad per-buff weighted sum was introduced because it can double-count nonstacking effects.

### Performance / lifecycle contract

Scenario dimensions: number of area apply nodes, ordered conditional depth, repeated enter/round paths, number of recipients and overlapping same-buff grants; both TB and RT consume the same cached profiles. The existing depth limit (>4 returns) and candidate/search budgets are unchanged. The prior O(G) dedup scan for each apply node becomes O(G*d), with d bounded by the existing traversal depth; cold profile construction is O(G^2*d) for retained grant count G. Retained data is O(G*d), bounded by visited apply nodes in the immutable blueprint graph. The path filter may correctly retain more grants than the broken reader, so recipient iteration can increase accordingly; no tactical cap or quality reduction compensates for it.

No cache was added. Existing `ToggleProfiles` / `s_AuraGrants` remain keyed by blueprint identity and last for the process's blueprint lifetime; the new field holds an already-referenced immutable blueprint action. No actor, mutable MechanicsContext, target snapshot or condition result is retained. Blueprint mutation after an existing profile is cached is a pre-existing invalidation limitation. The extra Distinct calls match the existing helpful-consumer discipline and prevent duplicated parts. Unity latency/allocation/GC acceptance is unmeasured; require matched save/party/settings on current baseline and test package, with cold/warm graph construction and sustained RT, rather than inventing timing thresholds from this harness.

Permitted tactical change: a buff path previously discarded may now be considered when its actual guard is the one applicable to the recipient; duplicate paths do not duplicate buff parts. Permissions, acceptance and execution remain existing pathways. No movement, command, class-label or GUID logic changes. Rollback: revert the C03 path comparison/SaveAction field and its two harmful-aura consumer Distinct additions, plus its fixture/registration, independent of booster/burn batches.

### Replay contract

Obtain a complete actual area graph with opposite/independent conditional paths applying the same buff, including its carrier, recipient acceptance/exemption facts and save/resource graphs. In TB and RT: positive recipient matching the previously dropped path; negative recipient rejecting that path and not receiving the buff; transition acceptance/fact/position or save context changes at the next decision. Verify discovery/permissions, per-recipient guards, stacking and native applied buff, then compare cast/activation against attack/spell alternatives and continuing resource cost. Mixed helpful/harmful/saved and unknown-guard behavior remains a separate C05 acceptance track. Do not increase spell-permission or switch player-owned acceptance to make the fixture pass.
