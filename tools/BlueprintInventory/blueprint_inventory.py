#!/usr/bin/env python3
"""Read exported JBP metadata without loading game assemblies or running game code."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

_GUID = re.compile(r"^[0-9a-fA-F]{32}$")
_REFERENCE = re.compile(r"^!bp_([0-9a-fA-F]{32})$")
_ZERO = "0" * 32
_ROSTER_TYPES = {"BlueprintCharacterClass", "BlueprintArchetype", "BlueprintProgression"}
# Component bookkeeping, not gates. Blueprint references are reported separately; nested objects (localized UI text,
# condition graphs) are not copied, only the type of a nested component.
_NON_GATE_FIELDS = {"$type", "name", "m_Overrides", "m_Flags", "PrototypeLink", "Comment"}


def blueprint_type(raw):
    """Understand GUID,type JBP tags, and ordinary namespace,assembly tags."""
    if not isinstance(raw, str):
        return "unknown"
    parts = [part.strip() for part in raw.split(",")]
    name = parts[1] if len(parts) > 1 and _GUID.fullmatch(parts[0]) else parts[0]
    return name.rsplit(".", 1)[-1]


def walk_references(value, path="$", include_zero=False):
    if isinstance(value, dict):
        for key in sorted(value):
            yield from walk_references(value[key], path + "." + key, include_zero)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from walk_references(child, f"{path}[{index}]", include_zero)
    elif isinstance(value, str):
        match = _REFERENCE.fullmatch(value)
        if match and (include_zero or match[1] != _ZERO):
            yield match[1].lower(), path


def _asset_id(raw):
    return raw.lower() if isinstance(raw, str) and _GUID.fullmatch(raw) and raw != _ZERO else None


def _discover(inputs):
    """Ignore symlinks and read an overlapping physical file only once."""
    seen = set()
    files, skipped, roots = [], [], []
    for index, raw in enumerate(inputs, 1):
        path = Path(raw)
        if not path.exists():
            raise ValueError(f"input {index} does not exist")
        if path.is_symlink():
            raise ValueError(f"input {index} is a symlink; choose the actual export directory")
        roots.append(path.resolve())
        candidates = sorted(path.rglob("*")) if path.is_dir() else [path]
        for candidate in candidates:
            label = f"input-{index}/" + (candidate.relative_to(path).as_posix() if path.is_dir() else candidate.name)
            if candidate.is_symlink():
                skipped.append(label)
                continue
            if candidate.suffix.lower() != ".jbp" or not candidate.is_file():
                continue
            resolved = candidate.resolve()
            if resolved not in seen:
                files.append((label, candidate))
                seen.add(resolved)
    if not files:
        raise ValueError("no regular .jbp files found in the selected inputs")
    return sorted(files), sorted(skipped), roots


def _ref(guid, assets):
    entries = assets.get(guid, [])
    result = {"asset_id": guid, "resolution": "missing" if not entries else "resolved" if len(entries) == 1 else "ambiguous"}
    if entries:
        result["candidates"] = [{"name": entry["name"], "type": entry["type"], "source": entry["source"]} for entry in entries]
    return result


def _refs(value, assets):
    return [dict(_ref(guid, assets), field=field) for guid, field in walk_references(value)]


def _levels(value, assets):
    if not isinstance(value, list):
        return []
    result = []
    for entry in value:
        if isinstance(entry, dict):
            result.append({"level": entry.get("Level"), "features": _refs(entry.get("m_Features", entry.get("Features", [])), assets)})
    return result


def _scalar(value):
    return isinstance(value, (str, bool, int, float, type(None))) and not (
        isinstance(value, str) and _REFERENCE.fullmatch(value))


def _gates(component):
    """Every scalar gate of a prerequisite (Stat/Value, Alignment, RequiredSpellLevel/IsArcane, Level, Amount, ...)."""
    gates = {}
    for key in sorted(component):
        value = component[key]
        if key in _NON_GATE_FIELDS:
            continue
        if _scalar(value):
            if value is None or not (isinstance(value, str) and value.startswith("!bp_")):
                gates[key] = value
        elif isinstance(value, list) and value and all(_scalar(item) for item in value):
            gates[key] = value
        elif isinstance(value, dict) and isinstance(value.get("$type"), str):
            gates[key] = {"type": blueprint_type(value["$type"])}
    return gates


def _prerequisites(data, assets):
    result = []
    components = data.get("Components", [])
    if not isinstance(components, list):
        return result
    for index, component in enumerate(components):
        if not isinstance(component, dict):
            continue
        kind = blueprint_type(component.get("$type"))
        if "Prerequisite" not in kind:
            continue
        gates = _gates(component)
        result.append({"component_index": index, "type": kind, "gates": gates,
                       "references": _refs(component, assets)})
    return result


def inventory(inputs, example_limit=5):
    if example_limit < 0:
        raise ValueError("reference example limit must be nonnegative")
    files, skipped, _ = _discover(inputs)
    assets, metadata, errors, sources, reference_counts = {}, [], [], [], {}
    occurrences, zero_occurrences, loaded = 0, 0, 0
    for source, path in files:
        try:
            raw = path.read_bytes()
        except OSError:
            errors.append({"source": source, "error": "file could not be read"})
            sources.append({"source": source, "status": "unreadable"})
            continue
        file_hash = hashlib.sha256(raw).hexdigest()
        source_info = {"source": source, "bytes": len(raw), "sha256": file_hash}
        sources.append(source_info)
        try:
            obj = json.loads(raw.decode("utf-8-sig"))
            if not isinstance(obj, dict) or not isinstance(obj.get("Data"), dict):
                raise ValueError("expected an object with Data object")
            guid = _asset_id(obj.get("AssetId"))
            if guid is None:
                raise ValueError("missing, zero or invalid AssetId")
        except (UnicodeError, json.JSONDecodeError, ValueError) as exc:
            if isinstance(exc, json.JSONDecodeError):
                message = f"invalid JSON at line {exc.lineno}, column {exc.colno}"
            elif isinstance(exc, UnicodeError):
                message = "input is not UTF-8"
            else:
                message = str(exc)
            errors.append({"source": source, "error": message})
            source_info["status"] = "failed"
            continue
        loaded += 1
        data = obj["Data"]
        kind = blueprint_type(data.get("$type"))
        record = {"asset_id": guid, "name": path.stem, "type": kind, "source": source, "sha256": file_hash}
        assets.setdefault(guid, []).append(record)
        source_info.update(status="loaded", asset_id=guid, type=kind)
        if kind in _ROSTER_TYPES:
            metadata.append((record, data))
        in_file = set()
        for target, field in walk_references(obj, include_zero=True):
            if target == _ZERO:
                zero_occurrences += 1
                continue
            occurrences += 1
            stats = reference_counts.setdefault(target, {"occurrences": 0, "source_files": 0, "examples": []})
            stats["occurrences"] += 1
            if target not in in_file:
                stats["source_files"] += 1
                in_file.add(target)
            if len(stats["examples"]) < example_limit:
                stats["examples"].append({"source": source, "field": field})

    classes, archetypes, progressions = [], [], []
    parents = {}
    for record, data in metadata:
        if record["type"] == "BlueprintCharacterClass":
            for guid, _ in walk_references(data.get("m_Archetypes", [])):
                parents.setdefault(guid, set()).add(record["asset_id"])
    for record, data in metadata:
        entry = dict(record, prerequisites=_prerequisites(data, assets),
                     duplicate_asset_id=len(assets[record["asset_id"]]) > 1)
        if record["type"] == "BlueprintCharacterClass":
            entry["flags"] = {key: data.get(key) for key in ("PrestigeClass", "IsMythic", "m_IsHigherMythic", "HideInUI", "HideIfRestricted")}
            entry["progression"] = _refs(data.get("m_Progression"), assets)
            entry["archetypes"] = _refs(data.get("m_Archetypes", []), assets)
            entry["spellbook"] = _refs(data.get("m_Spellbook"), assets)
            classes.append(entry)
        elif record["type"] == "BlueprintArchetype":
            entry["parent_classes_from_export_links"] = [_ref(guid, assets) for guid in sorted(parents.get(record["asset_id"], set()))]
            entry["explicit_parent_links"] = {key: _refs(data[key], assets) for key in ("m_ParentClass", "m_CharacterClass", "m_Class") if key in data}
            entry["add_features"] = _levels(data.get("AddFeatures"), assets)
            entry["remove_features"] = _levels(data.get("RemoveFeatures"), assets)
            entry["replacement_spellbook"] = _refs(data.get("m_ReplaceSpellbook"), assets)
            entry["flags"] = {key: data.get(key) for key in ("RemoveSpellbook", "BuildChanging", "ChangeCasterType", "ReplaceStartingEquipment", "ReplaceClassSkills", "IsDivineCaster", "IsArcaneCaster")}
            archetypes.append(entry)
        else:
            entry["level_entries"] = _levels(data.get("LevelEntries"), assets)
            entry["classes"] = _refs(data.get("m_Classes", []), assets)
            entry["archetypes"] = _refs(data.get("m_Archetypes", []), assets)
            entry["alternate_classes"] = _refs(data.get("m_AlternateProgressionClasses", []), assets)
            entry["flags"] = {key: data.get(key) for key in ("HideInUI", "HideInCharacterSheetAndLevelUp", "ForAllOtherClasses", "GiveFeaturesForPreviousLevels")}
            progressions.append(entry)
    duplicates = [{"asset_id": guid, "sources": entries} for guid, entries in sorted(assets.items()) if len(entries) > 1]
    missing = [dict(asset_id=guid, **stats) for guid, stats in sorted(reference_counts.items()) if guid not in assets]
    ambiguous = [dict(asset_id=guid, **stats) for guid, stats in sorted(reference_counts.items()) if len(assets.get(guid, [])) > 1]
    digest = hashlib.sha256(json.dumps(sources, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()
    return {
        "schema_version": 1,
        "scope": "Derived metadata from supplied export files only; not the installed playable roster, DLC availability, mod compatibility, actor kit or AI competence.",
        "status": "partial" if errors or duplicates or missing or skipped else "closed-within-export-scope",
        "corpus_sha256": digest,
        "input_count": len(inputs),
        "summary": {"files_discovered": len(files), "loaded": loaded, "failed": len(errors), "skipped_symlinks": len(skipped),
                    "unique_asset_ids": len(assets), "duplicate_asset_ids": len(duplicates),
                    "classes": len(classes), "archetypes": len(archetypes), "progressions": len(progressions)},
        "reference_closure": {"kind": "Syntactic nonzero !bp_GUID references; other asset encodings are not counted. Missing references need not have been dereferenced by a reader.",
                              "distinct_references": len(reference_counts), "occurrences": occurrences,
                              "zero_reference_occurrences_ignored": zero_occurrences,
                              "unresolved_distinct": len(missing), "unresolved_occurrences": sum(x["occurrences"] for x in missing),
                              "ambiguous_distinct": len(ambiguous), "example_limit_per_reference": example_limit,
                              "unresolved": missing, "ambiguous": ambiguous},
        "classes": classes, "archetypes": archetypes, "progressions": progressions,
        "duplicate_assets": duplicates, "errors": errors, "skipped_symlinks": skipped,
        "sources": sources,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", nargs="+", help="one or more exported JBP directories or .jbp files")
    parser.add_argument("--output", type=Path, help="write derived JSON outside the input directories; default stdout")
    parser.add_argument("--force", action="store_true", help="replace an existing output report (never an input file)")
    parser.add_argument("--strict", action="store_true", help="exit 1 for parse failures, duplicate IDs, missing references or skipped symlinks")
    parser.add_argument("--reference-examples", type=int, default=5, help="maximum source examples per referenced GUID; counts remain exact (default 5)")
    args = parser.parse_args(argv)
    try:
        if args.output:
            output = args.output.resolve()
            for raw in args.inputs:
                root = Path(raw).resolve()
                if output == root or (root.is_dir() and root in output.parents):
                    raise ValueError("output must be outside all input directories and different from all input files")
        result = inventory(args.inputs, args.reference_examples)
        rendered = json.dumps(result, indent=2, ensure_ascii=True) + "\n"
        if args.output:
            with args.output.open("w" if args.force else "x", encoding="utf-8", newline="\n") as stream:
                stream.write(rendered)
        else:
            sys.stdout.write(rendered)
    except (ValueError, OSError) as exc:
        parser.error(str(exc))
    return 1 if args.strict and result["status"] == "partial" else 0


if __name__ == "__main__":
    sys.exit(main())
