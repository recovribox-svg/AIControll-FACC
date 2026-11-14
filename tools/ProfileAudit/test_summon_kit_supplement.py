"""Tests for summon_kit_supplement.py: python3 -I tools/ProfileAudit/test_summon_kit_supplement.py"""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import summon_kit_supplement as supplement  # noqa: E402


def guid(n):
    return f'{n:032x}'


def record(n, name, kind, refs=(), spawn=None):
    body = {'$type': f'x, {kind}', 'refs': [f'!bp_{guid(r)}' for r in refs]}
    if spawn is not None:
        body['Actions'] = [{'$type': f'{supplement.SPAWN_TYPE}, ContextActionSpawnMonster', 'm_Blueprint': f'!bp_{guid(spawn)}'}]
    return {'guid': guid(n), 'name': name, 'type': f'Kingmaker.Blueprints.{kind}', 'raw': json.dumps(body)}


class SupplementTests(unittest.TestCase):
    def run_tool(self, records, dumped=(), depth=6, seeds=None):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        stock = os.path.join(tmp.name, 'stock.jsonl')
        with open(stock, 'w', encoding='utf-8') as handle:
            for r in records:
                handle.write(json.dumps(r) + '\n')
        dump = os.path.join(tmp.name, 'dump')
        os.makedirs(dump)
        for n in dumped:
            with open(os.path.join(dump, f'{n}.jbp'), 'w', encoding='utf-8') as handle:
                handle.write('{"AssetId": "%s", "Data": {}}' % guid(n))
        out = os.path.join(tmp.name, 'out')
        argv = ['tool']
        if seeds is not None:
            seed = os.path.join(tmp.name, 'seed.txt')
            with open(seed, 'w', encoding='utf-8') as handle:
                handle.write('\n'.join(f'Unit | BlueprintFeature:{guid(s)}' for s in seeds))
            argv += ['--seed', seed]
        with open(os.devnull, 'w') as quiet:
            stdout, sys.stdout = sys.stdout, quiet
            try:
                code = supplement.main(argv + [stock, out, str(depth), dump])
            finally:
                sys.stdout = stdout
        self.assertEqual(code, 0)
        with open(os.path.join(out, '_manifest.json'), encoding='utf-8') as handle:
            manifest = json.load(handle)
        with open(os.path.join(out, '_summary.json'), encoding='utf-8') as handle:
            summary = json.load(handle)
        return {m['guid'] for m in manifest}, summary, out

    def test_missing_only_and_closure(self):
        records = [
            record(1, 'SummonSpell', 'BlueprintAbility', spawn=2),
            record(2, 'Creature', 'BlueprintUnit', refs=(3, 4)),
            record(3, 'CreatureAbility', 'BlueprintAbility', refs=(5,)),
            record(4, 'CreatureBrain', 'BlueprintBrain'),
            record(5, 'AbilityBuff', 'BlueprintBuff'),
            record(6, 'Unrelated', 'BlueprintAbility'),
        ]
        written, summary, out = self.run_tool(records, dumped=(1, 3))
        self.assertEqual(written, {guid(2), guid(4), guid(5)})          # never a blueprint a dump folder supplies
        self.assertEqual(summary['stock_spawn_sources'], 1)
        self.assertEqual(summary['stock_spawn_sources_in_dumps'], 1)
        self.assertEqual(summary['stock_units_referenced'], 1)
        self.assertEqual(summary['written_missing_only'], 3)
        with open(os.path.join(out, 'BlueprintUnit', f'Creature.{guid(2)[:8]}.jbp'), encoding='utf-8') as handle:
            wrapper = json.load(handle)
        self.assertEqual(wrapper['AssetId'], guid(2))
        self.assertEqual(wrapper['Data']['$type'], 'x, BlueprintUnit')

    def test_leaf_types_and_near_expansion(self):
        records = [
            record(1, 'SummonSpell', 'BlueprintAbility', spawn=2),
            record(2, 'Creature', 'BlueprintUnit', refs=(3, 7)),
            record(3, 'CreatureClass', 'BlueprintCharacterClass', refs=(4,)),
            record(4, 'ClassProgression', 'BlueprintProgression', refs=(5,)),
            record(5, 'ClassFeature', 'BlueprintFeature', refs=(6,)),
            record(6, 'FeatureAbility', 'BlueprintAbility'),
            record(7, 'WholeList', 'BlueprintSpellList', refs=(8,)),
            record(8, 'ListedSpell', 'BlueprintAbility'),
        ]
        written, _, _ = self.run_tool(records)
        self.assertIn(guid(6), written)        # class -> progression -> feature -> its ability
        self.assertIn(guid(7), written)        # the list itself is reached
        self.assertNotIn(guid(8), written)     # ... but never expanded into every spell

    def test_dropped_types_dangling_units_and_seeds(self):
        records = [
            record(1, 'SummonSpell', 'BlueprintAbility', spawn=2),
            record(3, 'DanglingSummon', 'BlueprintAbility', spawn=99),
            record(2, 'Creature', 'BlueprintUnit', refs=(4,)),
            record(4, 'Barks', 'BlueprintUnitAsksList'),
            record(10, 'OutOfReach', 'BlueprintFeature', refs=(11,)),
            record(11, 'OutOfReachAbility', 'BlueprintAbility'),
        ]
        written, summary, _ = self.run_tool(records)
        self.assertNotIn(guid(4), written)                                   # presentation is never written
        self.assertEqual(summary['stock_units_referenced_absent_from_stock'], [guid(99)])
        self.assertNotIn(guid(10), written)
        written, summary, _ = self.run_tool(records, seeds=(10, 77))
        self.assertTrue({guid(10), guid(11)} <= written)                     # a second pass supplies what was named
        self.assertEqual(summary['seed_ids_absent_from_stock'], [guid(77)])   # ... and names what the stock lacks


if __name__ == '__main__':
    unittest.main()
