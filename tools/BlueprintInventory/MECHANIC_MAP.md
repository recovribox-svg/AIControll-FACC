# Progression and mechanic map

`mechanic_map.py` reads exported JBP files and the existing roster, derives bounded feature graphs, and indexes typed uses in the current production readers. It writes derived metadata only. Python **3.9+**, standard library; no game DLLs, native casts, conditions or mod initializers are executed.

```sh
python3 tools/BlueprintInventory/mechanic_map.py /exports/Classes /exports/Mythic /exports/Feats /exports/Spells /exports/Buffs \
  --roster docs/competence/roster.json --source-root . --out /reports/competence --max-depth 6
```

Use all supplied export roots for the recorded run. Keep outputs outside those roots. Overlapping physical paths are read once; identical copies of a GUID unify, conflicting definitions are quarantined rather than assigned a guessed load order. Input file identities contribute to the deterministic corpus hash. Class category uses exported mythic/prestige flags and the seed roster's folder grouping; it does not reconstruct the missing native playable-class whitelist.

The traversal starts at actual class progression features, explicit archetype additions/removals, and selection/progression interactions. Level-specific removals are applied before additions; repeat ranks at other levels survive. Grant/effect references follow an explicit field list. Prerequisites, checked facts, filters, removal targets and unclassified references remain visible without being treated as granted facts. Cycles, missing assets and depth frontiers are recorded. Depth is an offline analysis bound, not a tactical search budget.

Selection paths are alternatives. Counts without selection edges and potential selection exposure are separate; neither proves an actor owns the feature, satisfies its gates or is permitted to use it. Parent inheritance, explicit archetype additions and removals have separate prevalence columns.

Outputs:

| File | Contents |
| --- | --- |
| `CLASS_MATRIX.md` | Every exported class/archetype, roots/unlocks, mechanic families, reader flags and register links |
| `class-matrix.json` | Normalized class/archetype rows, levels/deltas, effective kits, interaction deltas and signature membership |
| `mechanic-catalog.json` | Component/action/resource/trigger shapes, source citations, fact identities/edges and bounded root results |
| `feature-mechanics.csv` | Every class/archetype/progression feature with level, role, direct types, reader flags, choice counts and register |
| `signature-prevalence.csv` | Every exposed signature, parameters, class/archetype/choice/add/remove counts and reader citations |
| `PREVALENCE.md` | Compact register/signature view for prioritization; complete figures remain in the CSV |

Numeric signature arrays index `mechanic-catalog.json.signatures`. Prevalence membership integers index the corresponding `classes`, `archetypes` or `interaction_deltas` arrays in `class-matrix.json`. The stable signature hash is retained as `signatures[i].id` and in the CSV. Each normalized record occupies one JSON line to keep generated files smaller; the Markdown/CSV are the human review views.

Typed `case`/`is`/`GetComponent` uses provide **partial reader/query evidence**, not semantic coverage. The evidence can be an existing-state native query rather than a prospective buff/form forecast. Explicit model boundaries are curated in `EXPLICIT_GAPS`; unmatched types remain unreviewed, not automatically unsupported. Conditions are applicability metadata, not a control effect. No coverage percentage or gameplay/performance certificate is produced. Native semantics, admitted fields, stacking, target subjects and production wiring still need their mechanism contracts.

Work scales with input bytes, local graph nodes and bounded per-root reachability; root results are memoized only within the invocation. Choice fanout is not silently capped. State is discarded when the tool exits. This is not a Unity performance measurement.

## Static assembly index

The optional .NET 8/Cecil tool reads supplied mod assemblies (including PE `.cache` files) as metadata. It never loads them into the CLR or runs their code:

```sh
dotnet run --project tools/BlueprintInventory/AssemblyIndex -- --self-test
dotnet run --project tools/BlueprintInventory/AssemblyIndex -- /reports/mod-sites.json /mods/Example.dll
```

It records module identity/hash, registered component type IDs and bounded IL construction hints. Supported factory patterns are `CreateBlueprint...` and configurator `New`; other helpers, clones, loops and runtime mutations can be missed. Literal candidates use 24 preceding instructions; component generic arguments come from the same method and do not prove assignment to the created object. A site is **not** proof of creation, enabled settings, full progression, compatibility or actor ownership. Retain per-mod source/settings inspection separately. Do not commit supplied assemblies, symbols, decompiled code or settings.

## Checks

```sh
python3 -m unittest discover -s tools/BlueprintInventory -v
dotnet run --project tools/BlueprintInventory/AssemblyIndex -- --self-test
```

Synthetic checks cover graph cycles/frontiers/missing targets, conditional versus granted facts, removal targets, selection alternatives, level-specific replacement, duplicate/conflicting GUIDs, parameter signatures, normalized indexes, source evidence and progression-only feature rows. Assembly self-checks inspect synthetic IL for factory detection, typed components, lookup exclusion and type-ID extraction. No game data is embedded in the fixtures.
