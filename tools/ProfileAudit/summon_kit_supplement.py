#!/usr/bin/env python3
"""Missing-only stock fixtures for the summon-kit inventory (docs/SUMMON_KIT_INVENTORY.md, SUMMON-KITS).

The inventory follows spawn source -> spawned unit -> kit (facts, class levels, body, brain, the abilities' own buffs).
A user's blueprint dumps rarely hold that whole closure. This script completes it from a stock blueprint database and
writes ONLY the blueprints no dump folder supplies, so a dump folder's blueprint is never replaced (ProfileAudit loads
the result with --supplement, which enforces the same rule).

    summon_kit_supplement.py [--seed <file>] <stock.jsonl> <outDir> <maxDepth> <dump folders...>

--seed: a text file; every 32-digit blueprint id in it is added to the closure's starting points. Use it for a second
pass with the "unloaded kit references" section of the inventory's detail report: what the first pass's rules did not
reach is then supplied when the stock database has it, and what remains unloaded is absent from the game's own data.

stock.jsonl: one JSON object per line with "guid", "name", "type" (full type name) and "raw" (the blueprint's JSON
text, as BubblePrints stores it) - a line-per-blueprint export of a BubblePrints blueprints_raw_*.binz database.
The output folder and the database are game content: private verification inputs, never committed. The manifest
(_manifest.json: guid, name, type, SHA-256 of the raw text) is what a receipt cites.

Run it with python3 -I; it reads untrusted game/mod data and executes none of it.
"""
import collections
import hashlib
import json
import os
import re
import sys

REF = re.compile(r'!bp_([0-9a-f]{32})')
ASSET = re.compile(r'"AssetId":\s*"([0-9a-f]{32})"')
SPAWN = re.compile(r'ContextActionSpawnMonster"[^{}]*?"m_Blueprint":\s*"!bp_([0-9a-f]{32})"')
SPAWN_TYPE = 'c7a3b2de9c37f154797b063a5730e307'          # TypeId of ContextActionSpawnMonster

# Reached but not expanded: their own references lead out of a creature's kit (every spell of a list, a whole campaign
# area). Near a creature a class, its archetypes, progression, spellbook and race are expanded (NEAR_DEPTH steps), so
# the features a class level gives are part of the closure.
LEAF = {'BlueprintSpellList', 'BlueprintUnitAsksList', 'BlueprintPortrait', 'BlueprintFaction', 'BlueprintRace',
        'BlueprintRaceVisualPreset', 'BlueprintCharacterClass', 'BlueprintArchetype', 'BlueprintProgression',
        'BlueprintStatProgression', 'BlueprintSpellsTable', 'BlueprintSpellbook', 'BlueprintFeatureSelection',
        'BlueprintParametrizedFeature', 'BlueprintUnitType', 'BlueprintCueBase', 'BlueprintDialog', 'BlueprintEtude',
        'BlueprintQuest', 'BlueprintArea', 'BlueprintUnlockableFlag', 'BlueprintSummonPool', 'BlueprintLoot',
        'BlueprintUnitLoot', 'BlueprintTrashLootSettings', 'BlueprintItemEquipmentUsable'}
EXPAND_NEAR = {'BlueprintCharacterClass', 'BlueprintArchetype', 'BlueprintProgression', 'BlueprintSpellbook',
               'BlueprintUnitType', 'BlueprintRace', 'BlueprintSummonPool'}
NEAR_DEPTH = 2
# Never written: presentation and campaign state, which no reader of the inventory uses.
DROP = {'BlueprintUnitAsksList', 'KingmakerEquipmentEntity', 'BlueprintRaceVisualPreset', 'BlueprintEtude',
        'BlueprintArea', 'BlueprintUnlockableFlag', 'BlueprintGlobalMagicSpell', 'BlueprintDialog', 'BlueprintCue',
        'BlueprintPortrait', 'BlueprintQuest', 'BlueprintAreaEnterPoint', 'BlueprintCutscene'}


def dump_guids(roots):
    have = set()
    for root in roots:
        for folder, _, files in os.walk(root):
            for name in files:
                if not name.endswith('.jbp'):
                    continue
                with open(os.path.join(folder, name), 'rb') as handle:
                    match = ASSET.search(handle.read(400).decode('utf-8', 'ignore'))
                if match:
                    have.add(match.group(1))
    return have


def main(argv):
    seeds = set()
    if len(argv) > 2 and argv[1] == '--seed':
        with open(argv[2], encoding='utf-8', errors='ignore') as handle:
            seeds = set(re.findall(r'(?<![0-9a-f])[0-9a-f]{32}(?![0-9a-f])', handle.read()))
        argv = argv[:1] + argv[3:]
    if len(argv) < 5:
        print(__doc__)
        return 2
    stock, out, depth_limit, roots = argv[1], argv[2], int(argv[3]), argv[4:]
    have = dump_guids(roots)
    records, sources = {}, []
    with open(stock, encoding='utf-8') as handle:
        for line in handle:
            record = json.loads(line)
            raw = record['raw']
            records[record['guid']] = (record['name'], record['type'].split('.')[-1], raw)
            if SPAWN_TYPE in raw or 'ContextActionSpawnMonster' in raw:
                sources.append(record['guid'])
    units = set()
    for guid in sources:
        units.update(SPAWN.findall(records[guid][2]))
    depth = {}
    frontier = [(g, 0) for g in units] + [(g, 0) for g in sources] + [(g, 0) for g in seeds]
    while frontier:
        guid, d = frontier.pop()
        if guid in depth and depth[guid] <= d:
            continue
        depth[guid] = d
        if guid not in records or d >= depth_limit:
            continue
        kind = records[guid][1]
        if kind in LEAF and not (kind in EXPAND_NEAR and d <= NEAR_DEPTH):
            continue
        for ref in set(REF.findall(records[guid][2])):
            if ref not in depth or depth[ref] > d + 1:
                frontier.append((ref, d + 1))
    closure = [g for g in depth if g in records]
    missing = sorted(g for g in closure if g not in have and records[g][1] not in DROP)
    os.makedirs(out, exist_ok=True)
    manifest = []
    for guid in missing:
        name, kind, raw = records[guid]
        folder = os.path.join(out, kind)
        os.makedirs(folder, exist_ok=True)
        safe = re.sub(r'[^A-Za-z0-9_.-]', '_', name)[:120]
        with open(os.path.join(folder, f'{safe}.{guid[:8]}.jbp'), 'w', encoding='utf-8') as handle:
            handle.write('{"AssetId": "%s", "Data": %s, "Meta": {"ShadowDeleted": false}}' % (guid, raw))
        manifest.append({'guid': guid, 'name': name, 'type': kind,
                         'raw_sha256': hashlib.sha256(raw.encode('utf-8')).hexdigest()})
    with open(os.path.join(out, '_manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump(manifest, handle, indent=0)
    summary = {
        'stock_records': len(records),
        'dump_blueprints': len(have),
        'stock_spawn_sources': len(sources),
        'stock_spawn_sources_in_dumps': sum(1 for g in sources if g in have),
        'stock_units_referenced': len(units),
        'stock_units_referenced_absent_from_stock': sorted(g for g in units if g not in records),
        'stock_units_in_dumps': sum(1 for g in units if g in have),
        'closure': len(closure),
        'closure_references_absent_from_stock': sum(1 for g in depth if g not in records),
        'written_missing_only': len(manifest),
        'written_by_type': dict(collections.Counter(m['type'] for m in manifest).most_common()),
        'max_depth': depth_limit,
        'seed_ids': len(seeds),
        'seed_ids_absent_from_stock': sorted(g for g in seeds if g not in records),
    }
    with open(os.path.join(out, '_summary.json'), 'w', encoding='utf-8') as handle:
        json.dump(summary, handle, indent=1)
    print(json.dumps(summary, indent=1))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
