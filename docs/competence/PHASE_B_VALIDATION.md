# Phase B mapping checks

Starting development: `fdc5e5e0906a4afc7a1af22cebbc29c0ed8d7e20`, manifest 0.3.30, reviewed competence2 runtime `7e89a16`. Task branch `codex/class-competence-map` changes docs/data and offline generators only. Claude retains C07 dispatch/Spell Combat/Spellstrike and C08 forms; C06 runtime work starts in a separate PR after this PR opens.

## Input and generator checks

| Check | Result / interpretation |
| --- | --- |
| JBP input files | **44,648**, **44,638 unique assets**, ten identical copies, zero conflicting GUIDs, zero JSON/identity parse failures |
| Roster scope | **74 class assets, 213 archetypes**. The original Classes/Mythic roster's 72/212 are retained; two NPC-specific class assets and `Penta_UniqueBard` come from Units. Base candidates remain **27**, prestige **13**, mythic-flagged **15**, other/pet/NPC/prototype **19** |
| Full raw reference closure | **2,056 distinct missing IDs / 37,866 occurrences** across all supplied assets. This is separate from the smaller bounded grant/effect graph; no closed-reference claim |
| Progression/selection handling | Actual level entries and explicit add/remove deltas; removals are level-specific. Choices and prerequisites are separate from grants; removal targets are not granted buffs. Missing links, unknown relations and analysis-depth frontiers remain explicit |
| Python checks | **24 passed** at the final tool revision; existing inventory/roster tests plus graph, replacement, source-citation and normalization cases |
| Assembly metadata checks | Four synthetic IL checks pass: blueprint factory, typed component, lookup exclusion, TypeId. Ten supplied modules inspected; no mod code is loaded/executed by this index |
| Cache identity | All **eight** supplied loader-cache assemblies match their corresponding supplied DLL SHA-256 exactly. PDBs are debug-symbol inputs; no generated blueprint records were added by the caches |
| Reader evidence | Typed source references and curated explicit gaps. A partial reader/query match is not complete semantic support. Source file hashes are retained alongside citations |
| Runtime source | **No runtime change** in this stage. No version bump or new test package; current competence2 package is the reader input |

A supplementary mod-content website archive provides version-scoped name/GUID/category lookups; [SUPPLEMENTARY_INDEX.json](SUPPLEMENTARY_INDEX.json) keeps its evidence separate from installed binaries and JBP graphs.

The complete per-feature and per-signature tables are [feature-mechanics.csv](feature-mechanics.csv) and [signature-prevalence.csv](signature-prevalence.csv). Numeric data indexes are documented in [the generator guide](../../tools/BlueprintInventory/MECHANIC_MAP.md). No supplied game/mod files, decompiled code or settings are committed.

## Actual production reader on the expanded corpus

The supplied-real-reference harness was built against the repository's competence2 DLL, MVID **`0ed42ac5-d9ce-4f88-a339-388028b036c3`**. Native rewrites remain scratch-only harness inputs.

| Run/check | Actual result |
| --- | --- |
| Strict ProfileAudit | **44,637 unique loaded, 1 failed**, allowed 0; **INCOMPLETE**, no ability verdicts generated |
| Sole strict failure | `AchaekekSacredWeaponFeature`, `Classes/Warpriest/DeityFavoriteWeapon`: `SecurityException: ECall methods must be packaged into a system module`. Its GUID/hash is in the machine-readable check summary |
| Separate diagnostic run, same frozen corpus, `--allow-failures 1` | **PARTIAL**: same 44,637 loaded / same 1 failed; **4,807 ability/toggle rows, 0 reader errors**. This is not a strict pass |
| Burn consequences | **42 checks, 2/2 actual feature fixtures, 8/8 actor-shaped kits, 0 failures**. Includes ordinary L6+ Overflow/Adaptation combinations, not only a single feature |
| Save boost routes | **16 compiled call-path checks, 0 failures**; actor/native outcome not tested |
| Area grant reader | **14 synthetic native-layout checks, 0 failures**; no actor/class-graph outcome claim |
| Other positive samples | On-hit damage on 12 facts; Enervation/negative-level reading present; AFFLICTION **4 checks**; SWARM **18 areas**, 18 of 19 carriers modeled; STAT-LINKS **27,012 facts / 53 positive linked facts**, including Divine Grace/Monk/Scaled Fist/Imitator |
| Skipped / unresolved scope | No AFFLICTION/SWARM skip in this diagnostic run. The one load failure, missing graph references, depth frontier and absent generated mod assets remain visible. Optional provider execution was not part of this mapping run; installed mod inspection was static metadata/IL |

The offline tools initially reported cached NU1900 advisory-access warnings where the sandbox could not reach the inherited proxy. The deployable mod was not rebuilt or claimed zero-warning in this docs/data stage. Reader results are bounded to the supplied assemblies/corpus and harness rewrites, not a current in-game patch/party certificate.

## Reproduction and acceptance boundary

Run the generator with all fourteen JBP roots listed in [INPUT_CORPUS.json](INPUT_CORPUS.json), the seed roster and the recorded reader source. Run Python tests with `python3 -m unittest discover -s tools/BlueprintInventory -v`; use the AssemblyIndex self-test and metadata command from its guide. The source identity and archive SHA-256 receipts make input substitutions visible.

The strict reader command is the ordinary `tools/ProfileAudit/run.sh` pipeline with the competence2 DLL and those roots. The separate diagnostic invocation passes `--allow-failures 1`; check that the failed file is still the exact fingerprint recorded here before comparing results. Do not silently broaden the allowance or call that run strict.

Gameplay decisions, command execution, actual mod initialization, actor-owned selections and Unity CPU/allocation/latency acceptance remain **pending**. No runtime/gameplay test is required to validate a documentation-only change; the next runtime PR must supply its own full checks, package identity and paired TB/RT replay plan.
