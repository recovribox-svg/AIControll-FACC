# Blueprint metadata inventory

This standalone Python tool lets you share a class/archetype/progression inventory without uploading the whole exported game. It reads local `.jbp` files, derives metadata and reference counts, and writes a JSON report. It never loads game assemblies, executes blueprint actions, evaluates conditions or changes the export. It is an offline audit tool and is not shipped in the UMM package.

You need Python 3.8 or newer. There are no additional packages to install. Keep this script and its output outside the exported blueprint directory.

## Run it

On Windows, with the Python launcher:

```powershell
py -3 "C:\Tools\blueprint_inventory.py" "D:\Blueprints" --output "D:\Reports\blueprint-inventory.json"
```

On Linux/macOS, from the repository root:

```sh
python3 tools/BlueprintInventory/blueprint_inventory.py /path/to/export --output /path/to/reports/blueprint-inventory.json
```

The output directory must already exist. An existing report is preserved unless you explicitly supply `--force`. Output inside an input directory, or overwriting an input file, is refused even with `--force`.

You can supply several export directories or individual `.jbp` files. Overlapping input paths read the same physical file once:

```sh
python3 tools/BlueprintInventory/blueprint_inventory.py /path/to/base-export /path/to/mod-export --output /path/to/reports/blueprint-inventory.json
```

Send the derived report, the current game patch/DLC, the exporter/source used and the enabled content-mod names/versions. Say whether the export covers the entire current blueprint cache or only selected folders. If the exporter dumps stock assets rather than the initialized cache patched by mods, this report cannot establish the modded graphs. A runtime kit/settings/mod manifest is still a separate input.

## What the report means

- `classes` contains actual exported `BlueprintCharacterClass` assets, prestige/mythic/visibility flags, progression, spellbook and archetype references.
- `archetypes` contains added/removed feature levels, replacement spellbook/related flags and parent classes linked by exported class assets. A missing parent link is unknown within this scope, not proof that the archetype has no parent.
- `progressions` contains actual level entries and class/archetype/alternate-class links. Features are referenced by GUID with a filename-derived name when available; names are asset identities, not localized display names.
- Relevant prerequisite component types, every scalar gate field (for example `Stat`/`Value`, `Alignment`, `RequiredSpellLevel`/`IsArcane`, `Level`, `Amount`, proficiency lists) and blueprint references are included; a nested component (such as a prerequisite's condition) is named by its type only. Full serialized component data, action graphs, localized prose and game DLLs are not emitted.
- `summary` separates loaded files, parse/read/shape failures, duplicate IDs and skipped symlinks. Missing/zero/invalid `AssetId` or a missing/non-object `Data` is a failed load. Valid UTF-8 with or without a BOM and case-insensitive `.jbp` extensions/GUIDs are accepted. Other file extensions are ignored. No `.jbp` files is an input error.
- Duplicate IDs remain visible and references to them are `ambiguous`. The tool does not guess which stock/mod copy wins at runtime.
- `reference_closure` counts syntactic nonzero `!bp_GUID` strings across all loaded files. It reports missing IDs and exact occurrence/source-file counts. Other asset encodings are outside that closure test. Missing references need not have been traversed by an AI reader. References in malformed files cannot be counted.
- Source examples per referenced GUID are bounded by `--reference-examples` (default 5); occurrence/source-file counts remain exact. Use `--reference-examples 0` to omit examples.
- `sources` records relative `input-N/...` paths, byte hashes and load status. No absolute input path is emitted. The corpus SHA-256 hashes the canonical ordered source records, so repeated runs with the same roots/order/relative paths/bytes give the same digest. It is an export identity, not an installed game version.

`status: partial` means missing references, failed files, duplicate IDs or skipped symlinks were found. `closed-within-export-scope` means those checks found no gaps **among the supplied files**. Neither status establishes that the export is the full installed playable roster, DLC availability, current actor abilities, mod compatibility, AI competence or successful gameplay.

By default, exit 0 means a report was produced; inspect its status/counts. `--strict` returns exit 1 for a partial report. Invalid arguments, missing inputs, an empty JBP scope or output errors return exit 2. Without `--output`, JSON goes to stdout.

## Roster summary

`roster_summary.py` condenses an inventory report into the roster the competence matrix plans from - classes with flags,
archetypes and main-progression levels, prestige gates, archetype add/remove deltas by level, and the other progressions
(bloodlines, domains, selections, pets). Names, levels and gates only.

```sh
python3 tools/BlueprintInventory/roster_summary.py inventory.json --json roster.json --markdown ROSTER.md
```

`docs/competence/ROSTER.md` / `roster.json` were made this way from the base-game Classes and Mythic exports.

## Cost and checks

The tool reads each discovered physical input file once per invocation. Work scales with sorted file discovery, JSON bytes, references and roster metadata; it does not perform dependency downloads or native queries. It retains source/asset identities, a reference counter with bounded examples, and the three roster types' metadata until output is written. State is discarded when the invocation ends. There is no game decision cache, tactical search cap or Unity performance claim.

Run the synthetic tests from the repository root:

```sh
python3 -m unittest discover -s tools/BlueprintInventory -v
```

Tests exercise linked class/progression/archetype metadata, prerequisite extraction, BOM/case/zero references, malformed files, duplicate ambiguity, reference example bounds, digest determinism, overlapping inputs, symlink/empty input handling and output preservation. Test fixtures are synthetic and contain no exported game data.
