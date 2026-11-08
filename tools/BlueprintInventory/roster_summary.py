#!/usr/bin/env python3
"""Condense a blueprint_inventory.py report into a compact class/archetype/prestige roster (JSON + Markdown).

The roster keeps names, levels and prerequisite gates only - the planning input of the class competence matrix
(docs/CLASS_COMPETENCE_CURRENT.md). It reads no game files and runs no game code.
"""
import argparse
import json
from pathlib import Path
import sys


def _name(ref):
    candidates = ref.get("candidates") or []
    if ref.get("resolution") == "resolved" and candidates:
        return candidates[0]["name"]
    return f"{ref.get('resolution', 'missing')}:{ref.get('asset_id')}"


def _levels(entries):
    result = {}
    for entry in entries or []:
        names = [_name(ref) for ref in entry.get("features", [])]
        if names:
            result.setdefault(str(entry.get("level")), []).extend(names)
    return dict(sorted(result.items(), key=lambda kv: (len(kv[0]), kv[0])))


def _group(source):
    # "input-1/Classes/Monster/Animal/AnimalClass.jbp" -> "Classes/Monster": the export's folder, not game data.
    parts = source.split("/")[1:-1]
    return "/".join(parts[:2]) if parts else ""


def summarize(report):
    progressions = {p["asset_id"]: p for p in report.get("progressions", [])}
    archetype_names = {a["asset_id"]: a["name"] for a in report.get("archetypes", [])}
    class_names = {c["asset_id"]: c["name"] for c in report.get("classes", [])}
    main_progressions = set()
    classes = []
    for entry in sorted(report.get("classes", []), key=lambda c: c["name"]):
        flags = entry.get("flags", {})
        progression_ids = [ref["asset_id"] for ref in entry.get("progression", [])]
        main_progressions.update(progression_ids)
        levels = {}
        for pid in progression_ids:
            if pid in progressions:
                levels = _levels(progressions[pid].get("level_entries"))
        classes.append({
            "name": entry["name"],
            "asset_id": entry["asset_id"],
            "export_group": _group(entry["source"]),
            "prestige": bool(flags.get("PrestigeClass")),
            "mythic": bool(flags.get("IsMythic")),
            "hidden": bool(flags.get("HideIfRestricted") or flags.get("HideInUI")),
            "archetypes": [archetype_names.get(ref["asset_id"], _name(ref)) for ref in entry.get("archetypes", [])],
            "progression": [_name(ref) for ref in entry.get("progression", [])],
            "levels": levels,
            "prerequisites": [{"type": p["type"], "gates": p["gates"], "references": [_name(r) for r in p["references"]]}
                              for p in entry.get("prerequisites", [])],
        })
    archetypes = []
    for entry in sorted(report.get("archetypes", []), key=lambda a: a["name"]):
        archetypes.append({
            "name": entry["name"],
            "asset_id": entry["asset_id"],
            "parents": [class_names.get(ref["asset_id"], _name(ref)) for ref in entry.get("parent_classes_from_export_links", [])],
            "add": _levels(entry.get("add_features")),
            "remove": _levels(entry.get("remove_features")),
            "replacement_spellbook": [_name(ref) for ref in entry.get("replacement_spellbook", [])],
            "flags": {k: v for k, v in (entry.get("flags") or {}).items() if v},
            "prerequisites": [{"type": p["type"], "gates": p["gates"], "references": [_name(r) for r in p["references"]]}
                              for p in entry.get("prerequisites", [])],
        })
    selections = []
    for entry in sorted(report.get("progressions", []), key=lambda p: p["name"]):
        if entry["asset_id"] in main_progressions:
            continue
        selections.append({
            "name": entry["name"],
            "classes": sorted({class_names.get(ref["asset_id"], _name(ref)) for ref in entry.get("classes", [])}),
            "archetypes": sorted({archetype_names.get(ref["asset_id"], _name(ref)) for ref in entry.get("archetypes", [])}),
            "levels_with_features": len([e for e in entry.get("level_entries", []) if e.get("features")]),
        })
    return {
        "schema_version": 1,
        "scope": ("Derived from a blueprint_inventory.py report of the supplied exports: names, levels and prerequisite "
                  "gates only. Not the installed or modded roster; the game's playable-class list (ProgressionRoot) is not "
                  "read, so export_group is the export's folder, not a game fact."),
        "source_corpus_sha256": report.get("corpus_sha256"),
        "source_status": report.get("status"),
        "counts": {
            "classes": len(classes),
            "prestige": sum(c["prestige"] for c in classes),
            "mythic": sum(c["mythic"] for c in classes),
            "archetypes": len(archetypes),
            "other_progressions": len(selections),
        },
        "classes": classes,
        "archetypes": archetypes,
        "other_progressions": selections,
    }


def markdown(roster):
    lines = [
        "# Class roster (derived)", "",
        roster["scope"], "",
        f"Source corpus `{roster['source_corpus_sha256']}` (inventory status `{roster['source_status']}`). "
        f"Counts: {roster['counts']['classes']} classes ({roster['counts']['prestige']} prestige, "
        f"{roster['counts']['mythic']} mythic), {roster['counts']['archetypes']} archetypes, "
        f"{roster['counts']['other_progressions']} other progressions (bloodlines, domains, selections, pets...).", "",
        "## Classes", "",
        "| Class | Export group | Kind | Archetypes |",
        "| --- | --- | --- | --- |",
    ]
    for c in roster["classes"]:
        kind = "mythic" if c["mythic"] else "prestige" if c["prestige"] else "base/other"
        if c["hidden"]:
            kind += ", hidden"
        lines.append(f"| {c['name']} | {c['export_group']} | {kind} | {', '.join(c['archetypes']) or '-'} |")
    lines += ["", "## Prestige class gates", "",
              "| Class | Prerequisites (type: gates; references) |", "| --- | --- |"]
    for c in roster["classes"]:
        if not c["prestige"]:
            continue
        parts = []
        for p in c["prerequisites"]:
            gates = {k: v for k, v in p["gates"].items() if k not in ("CheckInProgression", "HideInUI", "IsFeatureSelectionWhiteList")}
            text = p["type"].replace("Prerequisite", "") + ": " + ", ".join(f"{k}={v}" for k, v in gates.items())
            if p["references"]:
                text += "; " + ", ".join(p["references"][:4]) + (" ..." if len(p["references"]) > 4 else "")
            parts.append(text)
        lines.append(f"| {c['name']} | {'<br>'.join(parts) or '-'} |")
    lines += ["", "## Archetype deltas", "",
              "| Archetype | Parent | Adds (level: features) | Removes (level: features) |", "| --- | --- | --- | --- |"]
    for a in roster["archetypes"]:
        add = "; ".join(f"{lvl}: {', '.join(f)}" for lvl, f in a["add"].items())
        rem = "; ".join(f"{lvl}: {', '.join(f)}" for lvl, f in a["remove"].items())
        lines.append(f"| {a['name']} | {', '.join(a['parents']) or '-'} | {add or '-'} | {rem or '-'} |")
    return "\n".join(lines) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path, help="JSON written by blueprint_inventory.py --output")
    parser.add_argument("--json", type=Path, required=True, help="compact roster JSON to write")
    parser.add_argument("--markdown", type=Path, help="Markdown summary to write")
    parser.add_argument("--force", action="store_true", help="replace existing outputs")
    args = parser.parse_args(argv)
    try:
        report = json.loads(args.report.read_text(encoding="utf-8"))
        roster = summarize(report)
        mode = "w" if args.force else "x"
        with args.json.open(mode, encoding="utf-8", newline="\n") as stream:
            stream.write(json.dumps(roster, indent=1, ensure_ascii=True) + "\n")
        if args.markdown:
            with args.markdown.open(mode, encoding="utf-8", newline="\n") as stream:
                stream.write(markdown(roster))
    except (ValueError, OSError, KeyError) as exc:
        parser.error(str(exc))
    return 0


if __name__ == "__main__":
    sys.exit(main())
