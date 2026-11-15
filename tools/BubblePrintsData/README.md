# BubblePrints stock data and offline reader

The author requested this database be stored in the development repository on **15 November 2026**, because
Claude's network proxy can fetch this repository but cannot fetch the upstream data release. The five files in
`data/` are ordinary Git blobs, each under 30 MB. A normal clone/checkout contains everything needed to assemble
the database without a network request. Their combined compressed size is about 114 MB.

This is a specific author-authorized exception for these published stock-data archives. Game reference DLLs,
decompiled sources, private logs, live modded blueprint dumps and generated personal Fusion configurations remain
outside the repository. This folder is offline tooling/input data and is excluded from the mod's compile graph.
It is not part of a UMM package or release. No game behavior or version changes accompany this handover.

## Start here

From the repository root on `claude/dreamy-pasteur-b9h3rn`:

```sh
python3 tools/BubblePrintsData/assemble_database.py
```

Python 3's standard library is sufficient. The command checks every archive SHA-256 and expected member, streams
the pieces in manifest order, verifies each piece and the complete database, then writes:

```text
tools/BubblePrintsData/.local/blueprints_raw_2.7.0x_0.binz
```

The output directory is ignored by Git. A correct existing database is reused; a differing existing output is
refused. A failed assembly removes its temporary file. For another destination, pass `--output /path/file.binz`.
`--verify-only` checks the compressed archives and member names without assembling/decompressing the database.
There is no need to extract or concatenate the ZIP files manually, or download anything from another repository.

## Identity and provenance

| Item | Value |
| --- | --- |
| Database label | `2.7.0x_0` |
| Database size | 136,905,792 bytes (130.56 MiB) |
| Database SHA-256 | `03ba6e29782c0c14e0c0383cd739560358641f7b5842f1dcfb690246e73c9c6a` |
| Header and scanned record count | 236,660 |
| Viewer used for decoding | BlueprintExplorer / BubblePrints 2.1.0 |
| Original download | [BubblePrintsData 2.7.0x_0 release](https://github.com/factubsio/BubblePrintsData/releases/download/2.7.0x_0/blueprints_raw_2.7.0x_0.binz) |

The author supplied `bubbleprints-release.zip`; it contains the viewer/libraries, **not the database**. Codex
downloaded the database separately in an earlier environment, retained it privately and is now storing this exact
verified copy at the author's request. The earlier master-prompt wording claiming the database was inside the
viewer ZIP was incorrect and has been corrected. Do not infer that an earlier permitted download can run through
Claude's current proxy.

[Original cross-check receipt](../../docs/competence/BUBBLEPRINTS_CHECK.json) records the program/engine/database
hashes and 28/28 selected Data matches with the supplied JBP exports. That is a selected comparison, not proof
of all installed mod mutations. [Part manifest](DATABASE_PARTS.json), [archive checksums](data/SHA256SUMS.txt)
and [this handover's checks](CHECKS.json) identify the repository copy and verified assembly/reader.

## How Codex read the data without the Windows UI

The included .NET 8 console source references `BubbleprintEngine.dll`, `BinzFactory.dll` and the supplied LZ4
dependencies. It uses `BPFile.Reader`, `CreateReadContext` and `Open` for the actual binary decoding/decompression.
Chunk type 32 contains version/count; chunks of type 33 contain GUID, name, type and raw JSON strings. It does not
run the viewer, network manager, settings, game or mod initialization.

The **viewer ZIP already supplied by the author** is still needed for those libraries; no viewer or game DLLs
are committed here. Extract it and locate the DLL folder, normally `BlueprintExplorer/bin/Release/net8.0-windows`.
With .NET SDK 8 installed:

```sh
dotnet build tools/BubblePrintsData/Reader/BubblePrintsReader.csproj -c Release --ignore-failed-sources -p:NuGetAudit=false -p:BubblePrintsPath="/absolute/path/to/the/DLL-folder"
dotnet tools/BubblePrintsData/Reader/bin/Release/net8.0/BubblePrintsReader.dll tools/BubblePrintsData/.local/blueprints_raw_2.7.0x_0.binz tools/BubblePrintsData/Reader/targets.example.json tools/BubblePrintsData/.local/selected-stock
```

The example selects `TrueJudgmentAbility`. Replace/add GUIDs in a private copy of the dictionary to select records
you need (GUIDs without hyphens; values are descriptive receipt labels). The reader scans all records, checks the
count against the header and writes selected `GUID.raw.json` files plus `receipt.json`, including missing IDs.
A raw JSON record is the blueprint Data object. To create a JBP fixture for ProfileAudit, wrap it as
`{"AssetId":"the GUID","Data":<the parsed raw object>}`. Keep source/hash/version provenance on each fixture.

The helper build has one known MSB3277 metadata-dependency warning from the supplied viewer libraries; it completed
the full scan and selection. This is independent of the deployable mod's zero-warning Release requirement.
The reader is investigation tooling, not a replacement for ProfileAudit or gameplay tests.

## What the stock data establishes

Use it for GUID/reference lookup, comparison with exports and explicitly labelled missing-stock fixtures. Earlier
uses include hidden Magus and charge graphs. Preserve supplied modded exports when a stock duplicate exists; use
ProfileAudit's missing-only supplement workflow where applicable. The stock version label does not establish the
author's installed patch, active mods, runtime-created Fusion spells, actor state or mod-patched blueprint fields.
Actual game rules still come from the supplied game assemblies and runtime evidence.
