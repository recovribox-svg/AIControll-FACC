#!/usr/bin/env python3
"""Derive bounded progression/mechanic evidence from local JBP exports; never run game code."""
import argparse
from collections import Counter, defaultdict, deque
import csv
import hashlib
import json
from pathlib import Path
import re

from blueprint_inventory import blueprint_type, walk_references

ZERO = '0' * 32
GUID = re.compile(r'^[0-9a-f]{32}$')
CLASS = 'BlueprintCharacterClass'
ARCHETYPE = 'BlueprintArchetype'
PROGRESSION = 'BlueprintProgression'
SELECTION = 'BlueprintFeatureSelection'

# Relationship fields only. Prerequisite/checked-fact and owner/class links are dependencies, not grants.
EFFECT_FIELDS = {'m_Buff', 'm_Buffs', 'm_Facts', 'Facts', 'm_Feature', 'm_Features', 'm_Ability', 'm_Abilities',
                 'm_AreaEffect', 'm_Area', 'm_Spell', 'm_TouchDeliveryAbility', 'm_Variants', 'm_Enchantment',
                 'm_Enchantments', 'm_Weapon', 'm_Weapons', 'm_EmptyHandWeapon', 'm_AdditionalLimbs',
                 'm_Resource', 'm_NewFact', 'm_GrantedFacts', 'm_ApplyBuff', 'm_ActivatableAbility',
                 'm_ExtraEffectBuff', 'm_ExtraEffectBuffs', 'm_BuffsToApply'}
EFFECT_FIELDS.update({'m_PermanentFeature','m_CustomProperty','m_TouchWeapon','m_WeaponBlueprint','m_Enchant','m_EnchantmentBlueprint'})
DEPENDENCY_FIELDS = {'m_CheckedFact', 'm_Fact', 'm_RequiredFact', 'm_RequiredFacts', 'm_IgnoreFeature',
                     'm_CharacterClass', 'm_Class', 'm_Archetype', 'm_Spellbook', 'm_SpellList', 'm_CheckedBuff'}
DEPENDENCY_FIELDS.update({'m_CheckedFacts','m_FeatureList','m_AdditionalClasses','m_AdditionalArchetypes','m_Archetypes',
                          'Archetype','m_Projectiles','m_SummonPool','m_RequiredResource','ResourceCostDecreasingFacts',
                          'ResourceCostIncreasingFacts','m_Spellbooks','m_WeaponType','m_UnitFact'})
SCALAR_FIELDS = {'ActionType', 'Type', 'SpendType', 'Amount', 'Cost', 'IsSpendResource', 'IsFullRoundAction',
                 'Stat', 'Descriptor', 'Multiplier', 'CommandType', 'DeactivateImmediately', 'ActivateWithUnitCommand',
                 'OnlyOnHit', 'OnlyOnCriticalHit', 'OnlyOnFirstHit', 'OnlyOnFirstAttack', 'OnAttackOfOpportunity',
                 'OnCharge', 'OnFullAttack', 'WeaponCategory', 'WeaponRangeType', 'ToCaster', 'Permanent', 'Level',
                 'UseCommandType', 'ResourceCost', 'CostIsCustom', 'MinLevel', 'MaxLevel', 'Rank', 'Not',
                 'm_BaseValueType', 'm_Progression', 'm_UseMin', 'm_Min', 'm_UseMax', 'm_Max', 'm_StepLevel', 'm_Stat'}
SCALAR_FIELDS.update({'HalfIfSaved','IsAoE','Half','HasMinimal','OnlyOnSneakAttack','OnlyOnMeleeAttack','OnlyOnRangedAttack',
                      'OnlyOnFullAttack','OnlyOnAttackOfOpportunity','OnlyOnCharge','RequireWeaponCategory','UseWeaponGroup',
                      'Energy','ValueType','ValueRank','ValueShared','Rate','DiceType'})
NONCOMBAT = re.compile(r'^(Prerequisite|Recommendation|FeatureTags|Recalculate|ContextRankConfig|ContextCalculateSharedValue|AddClassSkill|SpellListComponent|SpellComponent|'
                       r'LevelUp|ClassLevels|AddProficiencies|AddStarting)')
VISUAL = re.compile(r'(Fx$|^AbilitySpawnFx|^Play|^SpawnFx|^AddVisual|^UnitView|^Portrait|^ChangeAnimation|^HideInUI)')

# These are inventory boundaries, not proof a native command is defective or an actor owns the feature.
EXPLICIT_GAPS = {
    'ActivatableAbilityResourceLogic': ('C06', 'Recurring/event/activation spend semantics are the next stage; resource quantity is not a duration.'),
    'Polymorph': ('C08', 'Current body is read natively; prospective replacement body/equipment/casting state is not fully forecast.'),
    'ItemPolymorph': ('C08', 'Prospective replacement-state forecast needs a dedicated contract.'),
    'ShiftersFury': ('C08', 'Generated natural-weapon selection and prospective routine remain partial.'),
    'ShiftersFuryItemAbility': ('C08', 'Generated natural-weapon selection remains a distinct native contract.'),
    'ShiftersFuryItemBuff': ('C08', 'Current native routine can include it; future selection/activation value is partial.'),
    'AddMagusMechanicPart': ('C07', 'Native Magus sequence/delivery opportunity; Claude owns its implementation.'),
    'SetMagusFeatureActive': ('C07', 'Native/player-owned feature state is not a standalone buff rotation.'),
    'AbilityRestoreSpellSlot': ('C15', 'No dedicated slot-restoration value reader found; availability/native execution and useful recovery are separate.'),
    'AbilityMagusSpellRecallCostCalculator': ('C15', 'Recovery costs need a native slot/resource contract; no C07 dispatch edits in this stage.'),
    'AbilityConvertSpellLevelToResource': ('C15', 'Slot-to-resource recovery is distinct from spending feasibility and toggle drain.'),
}
REGISTER_OVERRIDES = {
    'AddKineticistBurnValueChangedTrigger':('kinetic','C02'), 'KineticistBurnPropertyGetter':('kinetic','C02'),
    'Conditional':('hexes / control / debuffs','C05'), 'ContextActionSavingThrow':('hexes / control / debuffs','C05'),
    'ContextActionConditionalSaved':('hexes / control / debuffs','C05'),
    'ContextActionApplyBuff':('healing / support','C14'), 'AddFacts':('held / dependent states','C14'),
    'AddFactsFromCaster':('pets / summons / team','C14'), 'AddFeatureIfHasFact':('held / dependent states','C14'),
}

FAMILIES = [
    ('forms / alternate states', r'Polymorph|Shifter|EmptyHand|AdditionalAttacks|SecondaryAttacks|ChangeUnitSize', 'C08'),
    ('kinetic', r'Kinetic|Burn|GatherPower|ElementalOverflow|Quiver', 'C11'),
    ('alchemy / consumables', r'Bomb|Alchemy|Alchemical|Splash|Consumable|Potion', 'C10'),
    ('held / dependent states', r'Psionic|Focus|Trance|Spend.*Buff|RemoveSelf', 'C14'),
    ('performances / auras / rage', r'AreaEffect|Activatable|Performance|Rage|Aura|Accept', 'C05'),
    ('pets / summons / team', r'Pet|Mount|Summon|Teamwork|Companion|Tactician', 'C14'),
    ('healing / support', r'Heal|Restore|Resurrect|Remove.*(Condition|Buff|Stat)|Dispel|StatBonus|Resistance|Immunity|Armor|HitPoint|FastHealing|Regeneration|SavingThrow', 'C14'),
    ('hexes / control / debuffs', r'Hex|Cackle|Chant|Extend|Condition|SavingThrow|Fear|Confusion|Paralysis|Stun|Poison|Blind|Curse|NegativeLevel|StatDamage', 'C05'),
    ('marks / enchantments / boosters', r'Mark|Enchantment|Enchant|Metamagic|SpellDC|CasterLevel|SpellbookDC|Smite|Challenge|StudiedTarget', 'C01'),
    ('casting / hybrids', r'Spell|Ability|Cast|Spellbook|Resource', 'C04'),
    ('martial / precision / maneuvers', r'Attack|Weapon|Damage|Sneak|Precision|Critical|Cleave|Maneuver|Demoralize|Flank', 'C10'),
]


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def load_assets(inputs):
    assets, copies, conflicts, errors, sources = {}, [], {}, [], []
    paths = sorted({p.resolve() for root in inputs for p in ([Path(root)] if Path(root).is_file() else Path(root).rglob('*'))
                    if p.suffix.lower()=='.jbp' and p.is_file() and not p.is_symlink()})
    for path in paths:
        # Output identity uses the archive/root label plus its relative path, never an absolute user path.
        matches = [(Path(r), path.relative_to(Path(r).resolve())) for r in inputs if Path(r).is_dir() and path.is_relative_to(Path(r).resolve())]
        base, rel = max(matches, key=lambda item: len(item[0].parts)) if matches else (path.parent, Path(path.name))
        source = base.name + '/' + rel.as_posix()
        try:
            raw = path.read_bytes()
            value = json.loads(raw.decode('utf-8-sig'))
            guid, data = value.get('AssetId', '').lower(), value.get('Data')
            if not GUID.fullmatch(guid) or guid == ZERO or not isinstance(data, dict):
                raise ValueError('invalid AssetId or Data')
            record = {'asset_id': guid, 'name': path.stem, 'type': blueprint_type(data.get('$type')),
                      'source': source, 'data': data, 'sha256': hashlib.sha256(raw).hexdigest()}
            sources.append({k: record[k] for k in ['asset_id', 'source', 'sha256']})
            if guid in assets:
                equal = assets[guid]['data'] == data
                copies.append({'asset_id': guid, 'source': source, 'identical_data': equal})
                if not equal: conflicts.setdefault(guid, [assets[guid]['source']]).append(source)
            else:
                assets[guid] = record
        except (OSError, UnicodeError, ValueError, AttributeError) as exc:
            errors.append({'source': source, 'error': type(exc).__name__})
    for guid in conflicts:
        assets.pop(guid, None)  # No guessed stock/mod precedence for conflicting versions.
    return assets, {'files': len(paths), 'unique_assets': len(assets), 'identical_copies': sum(x['identical_data'] for x in copies),
                    'conflicting_ids': conflicts, 'failed': errors, 'corpus_sha256': digest(sorted(sources, key=lambda x: (x['asset_id'], x['source'])))}


def typed_nodes(value, path='$'):
    if isinstance(value, dict):
        if '$type' in value: yield blueprint_type(value['$type']), value, path
        for key, child in sorted(value.items()): yield from typed_nodes(child, path + '.' + key)
    elif isinstance(value, list):
        for i, child in enumerate(value): yield from typed_nodes(child, f'{path}[{i}]')


def reader_index(source_root):
    """Locate typed reader/query evidence, never infer complete semantic support from a code token."""
    found = defaultdict(list)
    files = [Path(source_root) / 'Analysis/BuffParts.cs', Path(source_root) / 'Tactics/AbilityProfile.cs',
             Path(source_root) / 'Tactics/CombatMath.cs', Path(source_root) / 'Tactics/AbilityDiscovery.cs']
    files += sorted((Path(source_root) / 'Tactics').glob('TacticalEngine*.cs'))
    declaration = re.compile(r'^\s*(?:public|private|internal|protected)\s+(?:static\s+)?(?:[\w.<>,?\[\]()]|\s)+\s+(\w+)\s*\(')
    local_void = re.compile(r'^\s*(?:static\s+)?void\s+(\w+)\s*\(')
    evidence = re.compile(r'\bcase\s+([\w.]+)\s+\w+|\bis\s+([\w.]+)\s+\w+|GetComponents?\s*<\s*([\w.]+)\s*>')
    identities=[];method_locations={}
    for file in files:
        if not file.exists(): continue
        identities.append({'file':file.relative_to(source_root).as_posix(),'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
        method = 'type-specific query'
        for line, text in enumerate(file.read_text(encoding='utf-8-sig').splitlines(), 1):
            if text.lstrip().startswith('//'): continue
            m = declaration.match(text) or local_void.match(text)
            if m:
                method = m[1]
                method_locations.setdefault(file.relative_to(source_root).as_posix()+'::'+method,line)
            for hit in evidence.finditer(text):
                name = next(x for x in hit.groups() if x).rsplit('.', 1)[-1]
                item = {'file': file.relative_to(source_root).as_posix(), 'method': method, 'line': line}
                if item not in found[name]: found[name].append(item)
    result=dict(found)
    result['$source_identity']={'files':identities,'sha256':digest(identities)}
    result['$method_locations']=method_locations
    return result


def classify(kind, evidence):
    if kind.startswith('ContextCondition'):
        return {'family':'metadata / applicability', 'register':['C05'],
                'understanding':'partial-safe-condition-reader' if evidence.get(kind) else 'unreviewed-condition',
                'reader_evidence':evidence.get(kind,[]),
                'limit':'A condition gates an effect; it does not itself grant control/debuff utility. Unknown guards remain unknown.'}
    if kind in {'BlueprintAbility', 'BlueprintActivatableAbility'} or re.search(r'LearnSpells|AddKnownSpell|AddSpecialSpellList',kind):
        path = 'Tactics/TacticalEngine.Toggles.cs' if kind=='BlueprintActivatableAbility' else 'Tactics/AbilityDiscovery.cs'
        methods = ['ProfileOf','AddToggleCandidates'] if kind=='BlueprintActivatableAbility' else ['Gather','AddConversions']
        refs=[]
        for method in methods:
            item={'file':path,'method':method}
            line=evidence.get('$method_locations',{}).get(path+'::'+method)
            if line:item['line']=line
            refs.append(item)
        return {'family':'performances / auras / rage' if kind=='BlueprintActivatableAbility' else 'casting / hybrids',
                'register':['C06'] if kind=='BlueprintActivatableAbility' else ['C04'],
                'understanding':'partial-runtime-discovery', 'reader_evidence':refs,
                'limit':'Native actor-kit discovery is present; individual effect, legality and cost shapes are separate.'}
    if NONCOMBAT.search(kind) or VISUAL.search(kind):
        return {'family': 'metadata / presentation', 'register': [], 'understanding': 'not-a-tactical-effect',
                'reader_evidence': evidence.get(kind, []), 'limit': 'Progression, eligibility or presentation; not a missing activation.'}
    family, register = 'unclassified material', 'C12'
    for name, pattern, entry in FAMILIES:
        if re.search(pattern, kind): family, register = name, entry; break
    if kind in REGISTER_OVERRIDES: family,register=REGISTER_OVERRIDES[kind]
    if kind in EXPLICIT_GAPS:
        register, limit = EXPLICIT_GAPS[kind]
        state = 'partial' if kind in {'ActivatableAbilityResourceLogic', 'AddMagusMechanicPart', 'SetMagusFeatureActive', 'ShiftersFuryItemBuff'} else 'no-dedicated-prospective-reader'
    elif evidence.get(kind):
        state = 'partial-reader-present'
        limit = 'Typed reader/query exists; admitted fields, context, branches and production use require per-shape validation. This is not a competence certificate.'
    else:
        state = 'unreviewed'
        limit = 'No typed reader matched by this source index. Native current-state rule effects may still apply; absence of a match does not prove absence of support.'
    if 'Trigger' in kind and not re.search(r'Kinetic|AbilityUse', kind): register = 'C13'
    if 'Resource' in kind and kind not in EXPLICIT_GAPS: register = 'C04'
    if kind in {'AbilityRestoreSpellSlot', 'AbilityMagusSpellRecallCostCalculator', 'AbilityConvertSpellLevelToResource'}: family = 'casting / hybrids'
    return {'family': family, 'register': [register], 'understanding': state,
            'reader_evidence': evidence.get(kind, []), 'limit': limit}


def edges_of(asset):
    data, edges = asset['data'], []
    def add(value, kind, field):
        for guid, suffix in walk_references(value): edges.append({'kind': kind, 'field': field + suffix[1:], 'to': guid})
    if asset['type'] == SELECTION:
        add(data.get('m_AllFeatures', []), 'choice', 'selection.all')
        add(data.get('m_Features', []), 'choice', 'selection.features')
    if asset['type'] == PROGRESSION:
        for entry in data.get('LevelEntries', []):
            add(entry.get('m_Features', []), 'progression', 'level:' + str(entry.get('Level')))
    if asset['type'] in {'BlueprintAbility', 'BlueprintActivatableAbility'}:
        add(data.get('m_Buff'), 'effect', 'm_Buff')
    for kind, node, path in typed_nodes(data.get('Components', []), 'Components'):
        dependency = bool(re.search(r'Prerequisite|Restriction|ContextCondition|Recommendation', kind))
        for key, value in node.items():
            if kind=='AddFactIfArchetype' and key=='m_Fact':
                add(value,'conditional-grant',path+'.'+key)
            elif re.search(r'^(ContextActionRemove|ContextActionDispel|AbilityEffectRemove|RemoveBuff|RemoveFeature)',kind):
                if key!='$type':add(value,'removal-target',path+'.'+key)
            elif key in DEPENDENCY_FIELDS or dependency:
                if key != '$type': add(value, 'dependency', path + '.' + key)
            elif key in EFFECT_FIELDS:
                add(value, 'conditional-grant' if re.search(r'If|OnClassLevel|FromCaster|Pet|Mount', kind) else 'effect', path + '.' + key)
    covered={e['field'] for e in edges}
    for guid,path in walk_references(data.get('Components',[])):
        field='Components'+path[1:]
        if field not in covered:edges.append({'kind':'unclassified-reference','field':field,'to':guid})
    unique = {(e['kind'], e['field'], e['to']): e for e in edges}
    return [unique[k] for k in sorted(unique)]


def local_signatures(asset, evidence, signatures):
    result = set()
    nodes = list(typed_nodes(asset['data'].get('Components', []), 'Components'))
    if asset['type'] in {'BlueprintAbility', 'BlueprintActivatableAbility'}:
        nodes.insert(0, (asset['type'], asset['data'], '$'))
    for kind, node, _ in nodes:
        if kind in {'ContextValue', 'ContextDurationValue', 'ConditionsChecker', 'DiceFormula', 'ActionList'}: continue
        scalars = {key: value for key, value in node.items() if key in SCALAR_FIELDS and isinstance(value, (bool, int, float, str))}
        for field in ['Value','DamageType','DurationValue']:
            if isinstance(node.get(field),dict):
                for key,value in node[field].items():
                    if key in SCALAR_FIELDS and isinstance(value,(bool,int,float,str)):scalars[field+'.'+key]=value
        payload = {'type': kind, 'parameters': scalars}
        key = digest(payload)[:16]
        if key in signatures and any(signatures[key][k] != payload[k] for k in payload): raise ValueError('signature collision')
        if key not in signatures: signatures[key] = dict(payload, **classify(kind, evidence))
        result.add(key)
    return sorted(result)


def feature_levels(data, field='LevelEntries'):
    return [{'level': entry.get('Level'), 'feature': guid} for entry in data.get(field, [])
            for guid, _ in walk_references(entry.get('m_Features', []))]


def effective_levels(base, added, removed):
    remove = Counter((x['level'], x['feature']) for x in removed)
    kept = []
    for item in base:
        key = item['level'], item['feature']
        if remove[key]: remove[key] -= 1
        else: kept.append(dict(item))
    return kept + [dict(x) for x in added]


def traverse(root, assets, local, depth):
    todo, seen, signatures, direct, selected, frontier, missing, choices = deque([(root, 0, False)]), {}, set(), set(), set(), set(), set(), set()
    while todo:
        guid, level, choice = todo.popleft()
        state = guid, choice
        if state in seen and seen[state] <= level: continue
        seen[state] = level
        if guid not in assets: missing.add(guid); continue
        signatures.update(local[guid]['signature_ids'])
        (selected if choice else direct).update(local[guid]['signature_ids'])
        for edge in local[guid]['edges']:
            if edge['kind'] in {'dependency','unclassified-reference','removal-target'}: continue
            if edge['kind'] == 'choice': choices.add(edge['to'])
            if level == depth:
                if (edge['to'], choice or edge['kind']=='choice') not in seen: frontier.add(edge['to'])
            else: todo.append((edge['to'], level + 1, choice or edge['kind']=='choice'))
    visited = {guid for guid,_ in seen}
    return {'signature_ids': sorted(signatures), 'without_selection_signatures':sorted(direct), 'selection_alternative_signatures':sorted(selected),
            'visited': sorted(visited), 'missing': sorted(missing),
            'depth_frontier': sorted(frontier - visited), 'choice_options': sorted(choices)}


def build_map(assets, inventory, roster, evidence, max_depth=6):
    signatures, local = {}, {}
    for guid, asset in sorted(assets.items()):
        local[guid] = {'asset_id': guid, 'name': asset['name'], 'type': asset['type'], 'source': asset['source'],
                       'signature_ids': local_signatures(asset, evidence, signatures), 'edges': edges_of(asset)}
    cache = {}
    def root_info(guid):
        if guid not in cache: cache[guid] = traverse(guid, assets, local, max_depth)
        return cache[guid]
    def summarize(roots):
        ids, direct, selected, missing, frontier, choices = set(), set(), set(), set(), set(), set()
        for item in roots:
            r = root_info(item['feature']); ids.update(r['signature_ids']); missing.update(r['missing'])
            frontier.update(r['depth_frontier']); choices.update(r['choice_options'])
            direct.update(r['without_selection_signatures']); selected.update(r['selection_alternative_signatures'])
        return {'signature_ids': sorted(ids), 'without_selection_signatures':sorted(direct), 'selection_alternative_signatures':sorted(selected),
                'families': sorted({signatures[x]['family'] for x in ids}),
                'families_without_selections':sorted({signatures[x]['family'] for x in direct}),
                'families_selection_alternatives':sorted({signatures[x]['family'] for x in selected}),
                'register': sorted({c for x in ids for c in signatures[x]['register']}),
                'understanding_counts': dict(sorted(Counter(signatures[x]['understanding'] for x in ids).items())),
                'missing_refs': sorted(missing), 'depth_frontier': sorted(frontier), 'selection_options': len(choices)}
    def state():
        return {'actor_availability': 'unknown', 'permission': 'runtime ownership/settings/blocklists/native legality required',
                'decision_execution': 'not-tested', 'validation': ['export-graph', 'typed-source-index'],
                'gameplay': 'pending', 'unity_performance': 'pending'}
    old_groups = {x['asset_id']: x['export_group'] for x in roster.get('classes', [])}
    rows, by_class = [], {}
    for guid, asset in sorted(assets.items(), key=lambda kv: kv[1]['name']):
        if asset['type'] != CLASS: continue
        data = asset['data']; roots = []
        progressions = [ref for ref, _ in walk_references(data.get('m_Progression'))]
        for progression in progressions:
            if progression in assets: roots += feature_levels(assets[progression]['data'])
            else: roots.append({'level': None, 'feature': progression})
        group = old_groups.get(guid, 'outside prior class roster')
        category = 'mythic' if data.get('IsMythic') else 'prestige' if data.get('PrestigeClass') else 'base-candidate'
        if category == 'base-candidate' and (group.startswith('Classes/Monster') or group in {'Classes/NPC','Classes/Pets','Classes/Prototype','outside prior class roster'}): category = 'other-export-class'
        row = dict(asset_id=guid, name=asset['name'], category=category, source=asset['source'], export_group=group,
                   progression_ids=progressions, spellbook_ids=[x for x,_ in walk_references(data.get('m_Spellbook'))],
                   feature_roots=roots, **summarize(roots), **state())
        if row['spellbook_ids'] and 'casting / hybrids' not in row['families']: row['families'].append('casting / hybrids')
        rows.append(row); by_class[guid] = row
    parent_ids = defaultdict(list)
    for guid, asset in assets.items():
        if asset['type'] == CLASS:
            for child, _ in walk_references(asset['data'].get('m_Archetypes', [])): parent_ids[child].append(guid)
    archetypes = []
    for guid, asset in sorted(assets.items(), key=lambda kv: kv[1]['name']):
        if asset['type'] != ARCHETYPE: continue
        added, removed = feature_levels(asset['data'], 'AddFeatures'), feature_levels(asset['data'], 'RemoveFeatures')
        parents = sorted(parent_ids[guid])
        kits = [{'parent': parent, 'roots': effective_levels(by_class[parent]['feature_roots'], added, removed)} for parent in parents if parent in by_class]
        archetypes.append(dict(asset_id=guid, name=asset['name'], source=asset['source'], parents=parents,
                               added=added, removed=removed, replacement_spellbooks=[x for x,_ in walk_references(asset['data'].get('m_ReplaceSpellbook'))],
                               effective_kits=[dict(parent=k['parent'], **summarize(k['roots'])) for k in kits],
                               added_mechanics=summarize(added), removed_mechanics=summarize(removed), **state()))
    interactions = []
    for guid, asset in sorted(assets.items(), key=lambda kv: kv[1]['name']):
        if asset['type'] not in {SELECTION, PROGRESSION}: continue
        # Independent choices/progressions are interaction deltas, never an assertion the actor selected them.
        if any(guid in row['progression_ids'] for row in rows): continue
        info = root_info(guid)
        levels=feature_levels(asset['data']) if asset['type']==PROGRESSION else []
        # Every progression-granted feature is also its own bounded analysis root, including selection-only
        # bloodline/domain/spirit/mythic progressions. Do not lose its final hop to the parent container's depth.
        feature_info=[root_info(item['feature']) for item in levels]
        interactions.append({'asset_id': guid, 'name': asset['name'], 'type': asset['type'], 'source': asset['source'],
                             'feature_levels': levels,
                             'choice_edges': [x for x in local[guid]['edges'] if x['kind']=='choice'],
                             'signature_ids': sorted(set(info['signature_ids']).union(*(set(x['signature_ids']) for x in feature_info))),
                             'missing_refs': sorted(set(info['missing']).union(*(set(x['missing']) for x in feature_info))),
                             'depth_frontier': sorted(set(info['depth_frontier']).union(*(set(x['depth_frontier']) for x in feature_info))),
                             'ownership': 'conditional/selected; not all options owned'})
    prevalence = defaultdict(lambda: defaultdict(set))
    for row in rows:
        for key in row['without_selection_signatures']:prevalence[key]['classes'].add(row['asset_id'])
        for key in row['selection_alternative_signatures']:prevalence[key]['classes_selection_alternatives'].add(row['asset_id'])
    for row in archetypes:
        for kit in row['effective_kits']:
            for key in kit['without_selection_signatures']:prevalence[key]['archetypes_effective'].add(row['asset_id'])
            for key in kit['selection_alternative_signatures']:prevalence[key]['archetypes_selection_alternatives'].add(row['asset_id'])
        for role in ['added','removed']:
            for key in row[role+'_mechanics']['signature_ids']:prevalence[key]['archetypes_'+role].add(row['asset_id'])
    for row in interactions:
        for key in row['signature_ids']:prevalence[key]['interaction_deltas'].add(row['asset_id'])
    categories=['classes','classes_selection_alternatives','archetypes_effective','archetypes_selection_alternatives','archetypes_added','archetypes_removed','interaction_deltas']
    prevalence={key:{role:sorted(value.get(role,set())) for role in categories} for key,value in sorted(prevalence.items())}
    visited = {x for info in cache.values() for x in info['visited'] if x in local}
    missing_union = {x for info in cache.values() for x in info['missing']}
    frontier_union = {x for info in cache.values() for x in info['depth_frontier']}
    report = {'schema_version':2, 'scope':'Roster-backed bounded exported progression mechanics and selection alternatives; no actor/permission/selection/outcome certificate.',
              'source_inventory':inventory, 'max_depth':max_depth, 'classes':rows, 'archetypes':archetypes,
              'reader_source_identity':evidence.get('$source_identity',{'scope':'synthetic/unspecified'}),
              'seed_roster_reconciliation':{
                  'classes_in_seed':len(roster.get('classes',[])), 'archetypes_in_seed':len(roster.get('archetypes',[])),
                  'classes_not_in_seed':sorted({r['asset_id'] for r in rows}-{r['asset_id'] for r in roster.get('classes',[])}),
                  'archetypes_not_in_seed':sorted({r['asset_id'] for r in archetypes}-{r['asset_id'] for r in roster.get('archetypes',[])}),
                  'seed_classes_absent':sorted({r['asset_id'] for r in roster.get('classes',[])}-{r['asset_id'] for r in rows}),
                  'seed_archetypes_absent':sorted({r['asset_id'] for r in roster.get('archetypes',[])}-{r['asset_id'] for r in archetypes})},
              'interaction_deltas':interactions, 'prevalence':prevalence,
              'counts':{'classes':len(rows),'archetypes':len(archetypes),'interaction_deltas':len(interactions),
                        'mapped_fact_nodes':len(visited),'mechanic_signatures':len(prevalence),'missing_reachable_refs':len(missing_union),'depth_frontier_refs':len(frontier_union),
                        'unclassified_relation_occurrences':sum(e['kind']=='unclassified-reference' for k in visited for e in local[k]['edges'])}}
    catalog = {'schema_version':1, 'source_corpus_sha256':inventory['corpus_sha256'],
               'signatures':{k:signatures[k] for k in sorted(prevalence)},'facts':{k:local[k] for k in sorted(visited)},
               'feature_roots':{k:cache[k] for k in sorted(cache)},
               'limits':'Depth frontier/missing refs are explicit; dependency edges are not grants, selections are alternatives, type evidence is partial shape recognition.'}
    return report, catalog


def render(report, catalog):
    names = {r['asset_id']:r['name'] for r in report['classes']}
    def status(ids):
        ss=[catalog['signatures'][x] for x in ids]
        return f"{sum(x['understanding'].startswith('partial') for x in ss)}/{sum(x['type'] in EXPLICIT_GAPS for x in ss)}/{sum(x['understanding'].startswith('unreviewed') for x in ss)}"
    out = ['# Roster-backed class and archetype mechanic map', '',
           'Derived metadata from the supplied exports. Every row is linked to actual progression/delta identities; no actor owns every selection option.',
           'Source/type evidence means a partial reader or current-state query exists, not that every field or class is validated. Full roots/levels/edges are in [class-matrix.json](class-matrix.json) and [mechanic-catalog.json](mechanic-catalog.json).', '',
           f"Scope: {report['counts']['classes']} classes, {report['counts']['archetypes']} archetypes, {report['counts']['interaction_deltas']} interaction deltas; graph depth {report['max_depth']}. Missing reachable references: {report['counts']['missing_reachable_refs']}; depth frontier: {report['counts']['depth_frontier_refs']}.", '',
           'The base/prestige/mythic categories follow exported flags and the prior roster folder classification, not the unavailable native playable-class list. Other/NPC/pet/prototype rows remain visible.', '',
           'P/G/U counts flag partial reader/query evidence, explicit model boundaries, and unreviewed signatures. Flags can overlap; they are not coverage percentages. [Feature-by-feature rows](feature-mechanics.csv) retain owner, level, role, feature identity, direct types, choice counts and registers.', '',
           '## Class progression roots', '', '| Class | Category | Feature roots / levels | Families without selection edges | P/G/U | Register |', '| --- | --- | --- | --- | --- | --- |']
    for r in report['classes']:
        levels=','.join(str(x) for x in sorted({x['level'] for x in r['feature_roots'] if isinstance(x['level'],int)}))
        families=[x.replace(' / ','/') for x in r['families_without_selections'] if not x.startswith('metadata /')]
        if r['spellbook_ids'] and 'casting/hybrids' not in families:families.append('casting/hybrids')
        out.append(f"| {r['name']} | {r['category']} | {len(r['feature_roots'])}; L{levels or '?'} | {', '.join(families) or 'metadata/unresolved'} | {status(r['without_selection_signatures'])} | {', '.join(r['register'])} |")
    out += ['', '## Explicit archetype additions and replacements', '',
            'Effective exposure applies level-specific removals before additions. It still includes unselected option alternatives; added/removed exposure is reported separately in the prevalence CSV.', '',
            '| Archetype | Parent | Added / removed roots; breakpoints | Families from direct additions | P/G/U added | Register |', '| --- | --- | --- | --- | --- | --- |']
    for r in report['archetypes']:
        levels=','.join(str(x) for x in sorted({i['level'] for i in r['added']+r['removed'] if isinstance(i['level'],int)}))
        f=[x.replace(' / ','/') for x in r['added_mechanics']['families_without_selections'] if not x.startswith('metadata /')]
        out.append(f"| {r['name']} | {', '.join(names.get(p,p) for p in r['parents']) or 'parent not exported'} | +{len(r['added'])}/−{len(r['removed'])}; L{levels or '?'} | {', '.join(f) or 'metadata/unresolved'} | {status(r['added_mechanics']['without_selection_signatures'])} | {', '.join(r['added_mechanics']['register'])} |")
    out += ['', '## Reader evidence, prevalence and remaining work', '',
            '[signature-prevalence.csv](signature-prevalence.csv) contains every exposed signature: component/action type, relevant scalar contract, class/effective-archetype/add/remove counts, register, status and reader file/method citations. [PREVALENCE.md](PREVALENCE.md) groups the material signatures for stage ranking.', '',
            'Selections and other progressions (bloodlines, domains, mysteries, spirits, rage powers, mythic paths/feats) are normalized interaction deltas in the JSON. Prerequisite and checked-fact edges are dependencies and are never traversed as granted abilities. Missing edges, traversal limits and unsupported prospective shapes remain explicit.', '',
            'Installed mod identities/settings/IL evidence are separate in [INSTALLED_MODS.md](INSTALLED_MODS.md). A generated GUID table is not proof that every mapped blueprint was created or enabled. Availability, permission, decisions, execution and Unity performance remain independent runtime evidence.', '']
    return '\n'.join(out)


def write_outputs(outdir, report, catalog):
    outdir.mkdir(parents=True, exist_ok=True)
    compact_report,compact_catalog=normalized_outputs(report,catalog)
    def readable(value):
        # One normalized record per line. Human review uses the Markdown and complete CSV, while JSON stays
        # deterministic, ordinary JSON, and avoids megabytes of indentation around numeric index arrays.
        lines=['{']
        for n,(key,child) in enumerate(value.items()):
            prefix='  '+json.dumps(key)+': '
            if isinstance(child,list):
                lines.append(prefix+'[')
                lines.extend('    '+json.dumps(row,separators=(',',':'))+(',' if i<len(child)-1 else '') for i,row in enumerate(child))
                lines.append('  ]'+(',' if n<len(value)-1 else ''))
            elif isinstance(child,dict):
                lines.append(prefix+'{')
                lines.extend('    '+json.dumps(k)+':'+json.dumps(row,separators=(',',':'))+(',' if i<len(child)-1 else '') for i,(k,row) in enumerate(child.items()))
                lines.append('  }'+(',' if n<len(value)-1 else ''))
            else:lines.append(prefix+json.dumps(child)+(',' if n<len(value)-1 else ''))
        return '\n'.join(lines+['}'])+'\n'
    (outdir/'class-matrix.json').write_text(readable(compact_report),encoding='utf-8')
    (outdir/'mechanic-catalog.json').write_text(readable(compact_catalog),encoding='utf-8')
    (outdir/'CLASS_MATRIX.md').write_text(render(report,catalog),encoding='utf-8')
    counts=Counter()
    with (outdir/'signature-prevalence.csv').open('w', newline='',encoding='utf-8') as handle:
        writer=csv.writer(handle,lineterminator='\n');writer.writerow(['signature','type','parameters','family','register','understanding','classes_without_selections','classes_selection_alternatives','base_candidates','prestige','mythic','archetypes_effective_without_selections','archetypes_selection_alternatives','archetypes_added','archetypes_removed','interaction_deltas','reader_file_method'])
        by_id={r['asset_id']:r for r in report['classes']}
        for key,p in sorted(report['prevalence'].items(),key=lambda kv:(-len(kv[1]['classes']),-len(kv[1]['archetypes_effective']),kv[0])):
            s=catalog['signatures'][key];cats=Counter(by_id[x]['category'] for x in p['classes'])
            refs='; '.join(x['file']+(':'+str(x['line']) if x.get('line') else '')+' '+x['method'] for x in s['reader_evidence'])
            writer.writerow([key,s['type'],json.dumps(s['parameters'],sort_keys=True),s['family'],','.join(s['register']),s['understanding'],len(p['classes']),len(p['classes_selection_alternatives']),cats['base-candidate'],cats['prestige'],cats['mythic'],len(p['archetypes_effective']),len(p['archetypes_selection_alternatives']),len(p['archetypes_added']),len(p['archetypes_removed']),len(p['interaction_deltas']),refs])
            for c in s['register']:counts[c]+=1
    md=['# Mechanic signature prevalence','', 'Counts are exported structural exposure, including selectable alternatives; they are not installed actor frequency or competent-use rates. Effective archetype, explicit added and removed counts are separate. Full per-signature counts and reader citations: [CSV](signature-prevalence.csv).', '', '| Register | Distinct exposed signatures | Next boundary |','| --- | --- | --- |']
    for c,n in sorted(counts.items(),key=lambda x:int(x[0][1:])):md.append(f'| {c} | {n} | See current register; prevalence alone does not override Phase D severity/confidence/risk |')
    md+=['','## Frequent material signatures with partial or unestablished readers','','| Type / parameters | Class / effective archetype exposure | State | Register |','| --- | --- | --- | --- |']
    material=[(k,p) for k,p in report['prevalence'].items() if catalog['signatures'][k]['understanding']!='not-a-tactical-effect']
    for k,p in sorted(material,key=lambda kv:(-len(kv[1]['classes']),-len(kv[1]['archetypes_effective']),kv[0]))[:35]:
        s=catalog['signatures'][k];md.append(f"| {s['type']} `{json.dumps(s['parameters'],sort_keys=True)}` | {len(p['classes'])} / {len(p['archetypes_effective'])} | {s['understanding']} | {', '.join(s['register'])} |")
    (outdir/'PREVALENCE.md').write_text('\n'.join(md)+'\n',encoding='utf-8')
    with (outdir/'feature-mechanics.csv').open('w',newline='',encoding='utf-8') as handle:
        writer=csv.writer(handle,lineterminator='\n');writer.writerow(['owner','owner_id','role','level','feature','feature_id','direct_types','partial_readers','explicit_model_gaps','unreviewed','choice_signatures','missing_references','depth_frontier','register','without_selection_signature_ids'])
        def feature(owner,role,item):
            guid=item['feature'];fact=catalog['facts'].get(guid,{})
            info=catalog['feature_roots'][guid];ss=[catalog['signatures'][x] for x in info['without_selection_signatures']]
            writer.writerow([owner['name'],owner['asset_id'],role,item['level'],fact.get('name','missing'),guid,
                             ';'.join(sorted({x['type'] for x in ss})),sum(x['understanding'].startswith('partial') for x in ss),
                             sum(x['type'] in EXPLICIT_GAPS for x in ss),sum(x['understanding'].startswith('unreviewed') for x in ss),
                             len(info['selection_alternative_signatures']),len(info['missing']),len(info['depth_frontier']),
                             ';'.join(sorted({c for x in ss for c in x['register']})),';'.join(info['without_selection_signatures'])])
        for owner in report['classes']:
            for item in owner['feature_roots']:feature(owner,'progression',item)
        for owner in report['archetypes']:
            for role in ['added','removed']:
                for item in owner[role]:feature(owner,role,item)
        for owner in report['interaction_deltas']:
            for item in owner['feature_levels']:feature(owner,'interaction progression',item)


def normalized_outputs(report,catalog):
    """Avoid repeating full GUID membership and source citations in large generated artifacts."""
    signature_ids=sorted(catalog['signatures']);sig_index={k:i for i,k in enumerate(signature_ids)}
    keys={'signature_ids','without_selection_signatures','selection_alternative_signatures'}
    def convert(value,key=None):
        if key in keys:return [sig_index[x] for x in value]
        if isinstance(value,dict):return {k:convert(v,k) for k,v in value.items()}
        if isinstance(value,list):return [convert(v) for v in value]
        return value
    r=convert(report);c=convert(catalog)
    ci={x['asset_id']:i for i,x in enumerate(report['classes'])};ai={x['asset_id']:i for i,x in enumerate(report['archetypes'])}
    ii={x['asset_id']:i for i,x in enumerate(report['interaction_deltas'])}
    r['prevalence']={str(sig_index[key]):{role:[(ci if role.startswith('classes') else ii if role=='interaction_deltas' else ai)[x] for x in rows]
                                       for role,rows in value.items()} for key,value in report['prevalence'].items()}
    r['index_schema']='Signature arrays/prevalence keys index mechanic-catalog.json.signatures; membership integers index this file classes/archetypes/interaction_deltas arrays by role.'
    readers={}
    c['signatures']=[]
    for key in signature_ids:
        entry=dict(catalog['signatures'][key],id=key);refs=entry.pop('reader_evidence')
        if refs:readers[entry['type']]=refs
        entry['reader_evidence_type']=entry['type'] if refs else None
        c['signatures'].append(entry)
    c['reader_evidence_by_type']=readers
    c['facts']={k:{field:value for field,value in row.items() if field!='asset_id'} for k,row in c['facts'].items()}
    c['feature_roots']={k:{field:value for field,value in row.items() if field not in {'visited','choice_options','signature_ids'}} | {'selection_option_count':len(catalog['feature_roots'][k]['choice_options'])}
                        for k,row in c['feature_roots'].items()}
    c['index_schema']='Numeric signature arrays index signatures. Fact GUID keys identify derived names/types/edges. Root visited/choice expansions can be reproduced from those edges and max_depth; counts retained.'
    return r,c


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('inputs',nargs='+');parser.add_argument('--roster',type=Path,required=True)
    parser.add_argument('--source-root',type=Path,required=True);parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--max-depth',type=int,default=6)
    args=parser.parse_args(argv)
    if args.max_depth<0:parser.error('max-depth must be nonnegative')
    for raw in args.inputs:
        p=Path(raw).resolve()
        if not p.exists():parser.error('input does not exist')
        if args.out.resolve().is_relative_to(p if p.is_dir() else p.parent):parser.error('output must be outside inputs')
    assets,inventory=load_assets(args.inputs)
    if not assets:parser.error('no loadable JBP assets in the input scope')
    inventory['mapping_tool_sha256']=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    inventory['inventory_helper_sha256']=hashlib.sha256(Path(__file__).with_name('blueprint_inventory.py').read_bytes()).hexdigest()
    report,catalog=build_map(assets,inventory,json.loads(args.roster.read_text(encoding='utf-8-sig')),reader_index(args.source_root),args.max_depth)
    write_outputs(args.out,report,catalog)
    print(json.dumps(dict(report['counts'],input=inventory),sort_keys=True))
    return 0


if __name__=='__main__':raise SystemExit(main())
