import json
from pathlib import Path
import tempfile
import unittest

import mechanic_map as mm


def asset(guid, kind='BlueprintFeature', **data):
    return {'asset_id':guid,'name':guid[:4], 'type':kind,'source':guid+'.jbp',
            'data':{'$type':kind, **data},'sha256':guid}


def component(kind, **fields):
    return {'$type':kind, **fields}


class MechanicMapTests(unittest.TestCase):
    def test_level_specific_removal_preserves_other_rank_and_additions(self):
        root=lambda level,feature:dict(level=level,feature=feature)
        self.assertEqual([root(3,'a'),root(1,'b')],mm.effective_levels([root(1,'a'),root(3,'a')],[root(1,'b')],[root(1,'a')]))

    def test_conditions_are_dependencies_not_granted_facts(self):
        a=asset('1'*32,Components=[component('ContextConditionHasFact',m_Fact='!bp_'+'2'*32),
                                   component('AddFacts',m_Facts=['!bp_'+'3'*32])])
        edges=mm.edges_of(a)
        self.assertEqual({'dependency'}, {e['kind'] for e in edges if e['to']=='2'*32})
        self.assertEqual({'effect'}, {e['kind'] for e in edges if e['to']=='3'*32})

    def test_removal_targets_are_not_grants_and_archetype_granted_fact_is_followed(self):
        a=asset('1'*32,Components=[component('ContextActionRemoveBuff',m_Buff='!bp_'+'2'*32),
                                   component('AddFactIfArchetype',m_Fact='!bp_'+'3'*32,m_Archetype='!bp_'+'4'*32)])
        edges=mm.edges_of(a)
        self.assertEqual({'removal-target'},{e['kind'] for e in edges if e['to']=='2'*32})
        self.assertEqual({'conditional-grant'},{e['kind'] for e in edges if e['to']=='3'*32})
        self.assertEqual({'dependency'},{e['kind'] for e in edges if e['to']=='4'*32})

    def test_selection_lists_are_unioned_and_choices_separate_from_grants(self):
        a,b,c='1'*32,'2'*32,'3'*32
        assets={a:asset(a,mm.SELECTION,m_AllFeatures=['!bp_'+b],m_Features=['!bp_'+c]),
                b:asset(b,Components=[component('ContextActionHealTarget')]),c:asset(c,Components=[component('Polymorph')])}
        signatures={};local={k:dict(signature_ids=mm.local_signatures(v,{},signatures),edges=mm.edges_of(v)) for k,v in assets.items()}
        r=mm.traverse(a,assets,local,4)
        self.assertEqual([],r['without_selection_signatures'])
        self.assertEqual(2,len(r['selection_alternative_signatures']))
        self.assertEqual([b,c],r['choice_options'])

    def test_cycles_missing_and_depth_frontier_are_explicit(self):
        a,b,c,d=['1'*32,'2'*32,'3'*32,'4'*32]
        assets={a:asset(a),b:asset(b),c:asset(c)}
        edge=lambda target:dict(kind='effect',field='test',to=target)
        local={a:dict(signature_ids=['a'],edges=[edge(b)]),b:dict(signature_ids=['b'],edges=[edge(a),edge(c)]),
               c:dict(signature_ids=['c'],edges=[edge(d)])}
        shallow=mm.traverse(a,assets,local,1)
        self.assertEqual([c],shallow['depth_frontier'])
        deep=mm.traverse(a,assets,local,5)
        self.assertEqual([d],deep['missing'])
        self.assertEqual(['a','b','c'],deep['signature_ids'])

    def test_identical_copies_unify_and_conflicting_guids_are_quarantined(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp);a=path/'a';b=path/'b';a.mkdir();b.mkdir()
            guid='a'*32
            for dest in [a/'f.jbp',b/'f.jbp']:dest.write_text(json.dumps({'AssetId':guid,'Data':{'$type':'BlueprintFeature'}}))
            assets,info=mm.load_assets([a,b]);self.assertEqual(1,len(assets));self.assertEqual(1,info['identical_copies'])
            (b/'f.jbp').write_text(json.dumps({'AssetId':guid,'Data':{'$type':'BlueprintBuff'}}))
            assets,info=mm.load_assets([a,b]);self.assertEqual({},assets);self.assertIn(guid,info['conflicting_ids'])

    def test_source_index_records_typed_use_without_claiming_complete_support(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'Analysis').mkdir();(root/'Analysis/BuffParts.cs').write_text('private static void ReadInto(object c) {\n case AddContextStatBonus bonus: break;\n}\n')
            evidence=mm.reader_index(root);state=mm.classify('AddContextStatBonus',evidence)
            self.assertEqual('partial-reader-present',state['understanding'])
            self.assertEqual('ReadInto',state['reader_evidence'][0]['method'])
            self.assertEqual('unreviewed',mm.classify('UnseenModRule',evidence)['understanding'])

    def test_discovery_citations_use_actual_method_declaration_lines(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'Tactics').mkdir()
            (root/'Tactics/AbilityDiscovery.cs').write_text('// header\npublic static void Gather() {\n  void AddConversions() {}\n}\n')
            evidence=mm.reader_index(root);refs=mm.classify('BlueprintAbility',evidence)['reader_evidence']
            self.assertEqual([2,3],[r['line'] for r in refs])

    def test_effective_archetype_and_interaction_catalog_are_referentially_complete(self):
        cl,pr,ar,f,g,sel=['1'*32,'2'*32,'3'*32,'4'*32,'5'*32,'6'*32]
        level=lambda n,x:dict(Level=n,m_Features=['!bp_'+x])
        assets={cl:asset(cl,mm.CLASS,m_Progression='!bp_'+pr,m_Archetypes=['!bp_'+ar]),
                pr:asset(pr,mm.PROGRESSION,LevelEntries=[level(1,f),level(3,f)]),
                ar:asset(ar,mm.ARCHETYPE,AddFeatures=[level(1,g)],RemoveFeatures=[level(1,f)]),
                f:asset(f,Components=[component('AddStatBonus',Stat='Strength')]),
                g:asset(g,Components=[component('Polymorph')]),
                sel:asset(sel,mm.SELECTION,m_AllFeatures=['!bp_'+g])}
        roster={'classes':[dict(asset_id=cl,export_group='Classes/Test')]}
        report,catalog=mm.build_map(assets,{'corpus_sha256':'synthetic'},roster,{},4)
        self.assertEqual(2,len(report['classes'][0]['feature_roots']))
        shape=next(k for k,v in catalog['signatures'].items() if v['type']=='Polymorph')
        self.assertEqual([ar],report['prevalence'][shape]['archetypes_added'])
        self.assertEqual([],report['prevalence'][shape]['classes'])
        for interaction in report['interaction_deltas']:
            self.assertTrue(set(interaction['signature_ids']).issubset(catalog['signatures']))
        self.assertEqual('unknown',report['archetypes'][0]['actor_availability'])

    def test_parameter_differences_are_distinct_signatures(self):
        signatures={}
        a=asset('1'*32,Components=[component('ActivatableAbilityResourceLogic',SpendType='TurnOn',Amount=1)])
        b=asset('2'*32,Components=[component('ActivatableAbilityResourceLogic',SpendType='NewRound',Amount=1)])
        self.assertNotEqual(mm.local_signatures(a,{},signatures),mm.local_signatures(b,{},signatures))

    def test_unclassified_reference_is_reported_and_not_assumed_to_be_a_grant(self):
        a=asset('1'*32,Components=[component('NewModRule',m_Unexamined='!bp_'+'2'*32)])
        self.assertEqual('unclassified-reference',mm.edges_of(a)[0]['kind'])

    def test_normalized_output_indices_and_csv_preserve_membership(self):
        c,p,f=['1'*32,'2'*32,'3'*32]
        assets={c:asset(c,mm.CLASS,m_Progression='!bp_'+p),p:asset(p,mm.PROGRESSION,LevelEntries=[dict(Level=1,m_Features=['!bp_'+f])]),
                f:asset(f,Components=[component('Polymorph')])}
        report,catalog=mm.build_map(assets,{'corpus_sha256':'synthetic'},{'classes':[dict(asset_id=c,export_group='Classes/Test')]},{})
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);mm.write_outputs(root,report,catalog)
            r=json.loads((root/'class-matrix.json').read_text());s=json.loads((root/'mechanic-catalog.json').read_text())
            self.assertEqual('Polymorph',s['signatures'][r['classes'][0]['signature_ids'][0]]['type'])
            self.assertEqual([0],r['prevalence']['0']['classes'])
            self.assertIn('Polymorph',(root/'signature-prevalence.csv').read_text())
            self.assertIn('Polymorph',(root/'feature-mechanics.csv').read_text())
            self.assertNotIn(b'\r\n',(root/'feature-mechanics.csv').read_bytes())
            self.assertNotIn(b'\r\n',(root/'signature-prevalence.csv').read_bytes())

    def test_interaction_only_progression_features_get_their_own_map_rows(self):
        p,f=['1'*32,'2'*32]
        assets={p:asset(p,mm.PROGRESSION,LevelEntries=[dict(Level=7,m_Features=['!bp_'+f])]),
                f:asset(f,Components=[component('AbilityRestoreSpellSlot')])}
        report,catalog=mm.build_map(assets,{'corpus_sha256':'synthetic'},{},{})
        self.assertIn(f,catalog['feature_roots'])
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);mm.write_outputs(root,report,catalog)
            self.assertIn('interaction progression,7',(root/'feature-mechanics.csv').read_text())


if __name__=='__main__':unittest.main()
