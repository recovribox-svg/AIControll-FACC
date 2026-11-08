# Phase B mapping checks

Original intake: `fdc5e5e`, manifest 0.3.30. Self-review starts at the author-requested `3443d94b960d14011f7a72e69af4d534eaecaf50`, preserves the original PR #21 head `6b0954c` and reconciles development packaging `1f58150`. Task branch `codex/class-competence-map` changes docs/data and offline generators only; the current reader input is competence3b. Claude retains C07 dispatch/Spell Combat/Spellstrike and C08 forms; C06 is the separate, still-unreviewed PR #22.

## Input and generator checks

| Check | Result / interpretation |
| --- | --- |
| JBP input files | **44,648**, **44,638 unique assets**, ten identical copies, zero conflicting GUIDs, zero JSON/identity parse failures |
| Roster scope | **74 class assets, 213 archetypes**. The original Classes/Mythic roster's 72/212 are retained; two NPC-specific class assets and `Penta_UniqueBard` come from Units. Base candidates remain **27**, prestige **13**, mythic-flagged **15**, other/pet/NPC/prototype **19** |
| Full raw reference closure | **2,056 distinct missing IDs / 37,866 occurrences** across all supplied assets. This is separate from the smaller bounded grant/effect graph; no closed-reference claim |
| Progression/selection handling | Actual level entries and explicit add/remove deltas; removals are level-specific. Choices and prerequisites are separate from grants; removal targets are not granted buffs. Missing links, unknown relations and analysis-depth frontiers remain explicit |
| Python checks | **27 passed**; graph/replacement/normalization cases plus query-versus-grant, amounts/dice/action distinctions and split-reader/enclosing-scope/comment evidence regressions |
| Assembly metadata checks | Four synthetic IL checks pass: blueprint factory, typed component, lookup exclusion, TypeId. Ten supplied modules inspected; no mod code is loaded/executed by this index |
| Cache identity | All **eight** supplied loader-cache assemblies match their corresponding supplied DLL SHA-256 exactly. PDBs are debug-symbol inputs; no generated blueprint records were added by the caches |
| Reader evidence | Typed source references across 42 files, including split `CombatMath*.cs`, and curated explicit gaps. A partial reader/query match is not complete semantic support. Source file hashes are retained alongside citations |
| Regenerated bounded map | **13,576 fact nodes / 4,510 signatures / 9,426 feature rows**. **56** missing reachable IDs, **767** frontier IDs and **6,159** unclassified relation occurrences remain explicit |
| Runtime source | **No runtime change** in this stage relative to reconciled development. No version bump or new test package; the existing competence3b package is the reader input |

A supplementary mod-content website archive provides version-scoped name/GUID/category lookups; [SUPPLEMENTARY_INDEX.json](SUPPLEMENTARY_INDEX.json) keeps its evidence separate from installed binaries and JBP graphs.

The complete per-feature and per-signature tables are [feature-mechanics.csv](feature-mechanics.csv) and [signature-prevalence.csv](signature-prevalence.csv). Numeric data indexes are documented in [the generator guide](../../tools/BlueprintInventory/MECHANIC_MAP.md). No supplied game/mod files, decompiled code or settings are committed.

## Self-review findings

| Finding | Reproduction and correction |
| --- | --- |
| Query references imported unrelated capabilities | `ContextRankConfig.m_Feature`/`m_Buff`, caster-fact gates, ability/spell/weapon filters and `SuppressBuffs.m_Buffs` were followed as effects. A referenced fact's own mechanics could then appear in the class row. Native type/field contracts now distinguish grants/effects from dependencies; unreviewed combinations stay unclassified. Actual `AddFactIfArchetype.m_Feature` is followed conditionally; its old synthetic `m_Fact` fixture was wrong |
| Different values/actions shared one signature | +1 versus +5 bonuses, nested dice counts and Swift versus Standard activatable commands collapsed. Whitelisted amounts, nested context/duration/dice values and the actual serialized activation fields now distinguish them. The signature count rose from 3,071 to 4,510; that is finer structural resolution, not more validated competence |
| Reader citations omitted split files and escaped local scope | The index omitted Claude's `CombatMath.CastBonus.cs`; a local helper could retain later citations belonging to its enclosing method. Split files and scope restoration are now covered. Comments/quoted prose no longer supply typed-code evidence. This remains a partial text index, not a C# semantic analyzer |
| Handover used older runtime/causal scope | Refreshed to competence3b and §126. Elemental Scion's `BurnNumberProperty` replacement affects the Psychokineticist consumer; it does not explain Orvath's separate nonlethal-property fallback. No runtime burn adapter or causal assertion is added |

The pre-correction tests reproduced the reference, value/action and source-index faults. All 27 tests pass after correction. Regeneration uses the unchanged 44,648-file corpus; class/archetype/feature roots remain 74/213/9,426. Normalized memberships, CSV rows, source/tool hashes, local links and the absence of new runtime source changes are checked separately in [PHASE_B_CHECKS.json](PHASE_B_CHECKS.json). External review of PR #21 and PR #22 is still pending.

The author then supplied BubblePrints 2.1.0 in place of the inaccessible website details. The Windows GUI could not run here, but its supplied database engine was used through a private .NET 8 adapter to decode the official **2.7.0x_0** `.binz`: **236,660 records read**, **28/28 selected records found and exactly Data-equal** to the supplied exports, including all fifteen Judgment abilities, the shared watcher and both burn properties. [BUBBLEPRINTS_CHECK.json](BUBBLEPRINTS_CHECK.json) records the program/engine/database hashes and per-record comparisons. The adapter had one metadata-dependency MSB3277 warning; that is separate from the runtime mod build. No game/mod initialization or UI was executed. This corroborates those version-labelled graphs; the installed game patch and mod-generated objects remain unverified. The stock database is supplementary and is not merged into the 44,648-file mapping corpus.

## Actual production reader on the expanded corpus

The supplied-real-reference harness was built against the repository's existing competence3b DLL, source **`3443d94`**, MVID **`740e7ec5-27f8-4575-9187-258b0f61f426`**. ZIP/DLL hashes match its existing identity receipt. Native rewrites remain scratch-only harness inputs.

| Run/check | Actual result |
| --- | --- |
| Strict ProfileAudit | **44,637 unique loaded, 1 failed**, allowed 0; **INCOMPLETE**, no ability verdicts generated |
| Sole strict failure | `AchaekekSacredWeaponFeature`, `Classes/Warpriest/DeityFavoriteWeapon`: `SecurityException: ECall methods must be packaged into a system module`. Its GUID/hash is in the machine-readable check summary |
| Separate diagnostic run, same frozen corpus, `--allow-failures 1` | **PARTIAL**: same 44,637 loaded / same 1 failed; **4,807 ability/toggle rows, 0 reader errors**. This is not a strict pass |
| Burn consequences | **42 checks, 2/2 actual feature fixtures, 8/8 actor-shaped kits, 0 failures**. Includes ordinary L6+ Overflow/Adaptation combinations, not only a single feature |
| Save boost routes | **16 compiled call-path checks, 0 failures**; actor/native outcome not tested |
| Area grant reader | **14 synthetic native-layout checks, 0 failures**; no actor/class-graph outcome claim |
| Claude's competence3 readers | **3 reveal and 2 cast-time bonus checks, 0 failures**; production reader code is inherited unchanged |
| Other positive samples | On-hit damage on 12 facts; Enervation/negative-level reading present; AFFLICTION **4 checks**; SWARM **18 areas**, 18 of 19 carriers modeled; STAT-LINKS **27,012 facts / 53 positive linked facts**, including Divine Grace/Monk/Scaled Fist/Imitator |
| Skipped / unresolved scope | No AFFLICTION/SWARM skip in this diagnostic run. The one load failure, missing graph references, depth frontier and absent generated mod assets remain visible. Optional provider execution was not part of this mapping run; installed mod inspection was static metadata/IL |

The offline tools initially reported cached NU1900 advisory-access warnings where the sandbox could not reach the inherited proxy. The deployable mod was not rebuilt or claimed zero-warning in this docs/data stage. Reader results are bounded to the supplied assemblies/corpus and harness rewrites, not a current in-game patch/party certificate.

## Reproduction and acceptance boundary

Run the generator with all fourteen JBP roots listed in [INPUT_CORPUS.json](INPUT_CORPUS.json), the seed roster and the recorded reader source. Run Python tests with `python3 -m unittest discover -s tools/BlueprintInventory -v`; use the AssemblyIndex self-test and metadata command from its guide. The source identity and archive SHA-256 receipts make input substitutions visible.

The strict reader command is the ordinary `tools/ProfileAudit/run.sh` pipeline with the competence3b DLL and those roots. The separate diagnostic invocation passes `--allow-failures 1`; check that the failed file is still the exact fingerprint recorded here before comparing results. Do not silently broaden the allowance or call that run strict.

The author update / §126 report competence2 played for **6 TB + 8 RT fights**, higher difficulty and eleven party members, with no mod errors. This is inherited evidence; the new raw gameplay logs were not supplied for this self-review. Competence3b and C06 remain unplayed. Current actor ownership, mod initialization and matched Unity CPU/allocation/latency acceptance stay **pending**. This documentation/tooling stage adds no game behavior; PR #22 carries its own runtime checks, identity and paired replay plan.
