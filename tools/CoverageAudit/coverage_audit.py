# Offline coverage audit: which ability blueprints the AI's ability reader can value (approximation of
# AbilityProfile.HasDetectableEffect + buff valuation), and which game building blocks block the rest.
import json, os, sys, collections, re
roots = sys.argv[1:-2]; known_file = sys.argv[-2]; out = sys.argv[-1]
known = set(l.strip() for l in open(known_file) if l.strip())
BP = {}
for r in roots:
    for dp, _, fs in os.walk(r):
        for f in fs:
            if not f.endswith('.jbp'): continue
            p = os.path.join(dp, f)
            try: j = json.load(open(p, encoding='utf-8-sig'))
            except Exception: continue
            d = j.get('Data') or {}
            t = (d.get('$type') or '').split(', ')[-1]
            BP[j.get('AssetId')] = (t, f[:-4], d, os.path.relpath(p, r))
def tname(o): return (o.get('$type') or '').split(', ')[-1] if isinstance(o, dict) else ''
def ref(v):
    if isinstance(v, str) and v.startswith('!bp_'): return BP.get(v[4:])
    return None
# what the reader values
EFFECT_ACTIONS = {'ContextActionDealDamage','ContextActionKill','ContextActionHealTarget','ContextActionDealWeaponDamage',
  'ContextActionCombatManeuver','ContextActionCombatManeuverCustom','ContextActionDisarm','Demoralize','ContextActionSpawnMonster',
  'ContextActionDispelMagic','ContextActionReduceBuffDuration','ContextActionEnchantWornItem','ContextActionRemoveBuff',
  'ContextActionRemoveBuffsByDescriptor','ContextActionMeleeAttack'}
RULE_COMPONENTS = {'AbilityCustomCharge','AbilityCustomVitalStrike','AbilityCustomCleave','AbilityDeliverAttackWithWeapon',
  'AbilityCustomMeleeAttack'}
# structural / neutral types never counted as blockers
NEUTRAL = re.compile(r'^(Conditional|ContextActionConditionalSaved|ContextActionSavingThrow|ContextActionOnContextCaster|ContextActionOnOwner|'
  r'ContextActionPartyMembers|ContextActionsOnPet|ContextActionOnRandomTargetsAround|ContextActionOnSwarmTargets|ContextActionOnRandomAreaTarget|'
  r'ContextActionSpawnFx|ContextActionSpawnFxOnPoint|ActionList|ContextValue|ContextDiceValue|ContextDurationValue|ConditionsChecker|'
  r'Context.*Condition.*|Has.*|.*Condition|Spell(Component|ListComponent|DescriptorComponent)|CraftInfoComponent|AbilitySpawnFx|'
  r'AbilityResourceLogic|ContextRankConfig|ContextCalculateSharedValue|AbilityTarget.*|AbilityCaster.*|AbilityShowIfCasterHas.*|'
  r'ActionPanelLogic|CantripComponent|AbilityEffectRunAction|AbilityDeliverProjectile|AbilityDeliverTouch|AbilityDeliverChain.*|'
  r'AbilityDeliverDelay|AbilityExecuteActionOnCast|AbilityKineticist|AbilityVariants|AbilityEffectStickyTouch|AbilityAoERadius|'
  r'HideDCFromTooltip|ContextSetAbilityParams|ReplaceAbilityParamsWithContext|PrerequisiteCasterTypeSpellLevel|LocalizedString|'
  r'BlueprintAbility|BlueprintBuff|BlueprintAbilityAreaEffect|SpawnFx.*|Metamagic.*|AbilityIsBomb|AbilityMagicSchool.*|ContextActionRandomize)$')
def walk(o, acts, comps_seen, depth=0, visited=None):
    if visited is None: visited = set()
    if depth > 80: return
    if isinstance(o, dict):
        t = tname(o)
        if t: acts[t] += 0
        if t: acts.setdefault(t, 0); acts[t] += 1
        for k, v in o.items():
            if k in ('m_Buff','m_AreaEffect','m_TouchDeliveryAbility','m_Blueprint') and ref(v) and v not in visited:
                visited.add(v); b = ref(v)
                acts['@'+b[0]] += 1
                for c in (b[2].get('Components') or []): walk(c, acts, comps_seen, depth+1, visited)
            else: walk(v, acts, comps_seen, depth+1, visited)
    elif isinstance(o, list):
        for x in o: walk(x, acts, comps_seen, depth+1, visited)
CC_MASK = 0x20|0x8000|0x10000|0x40000|0x400000|0x1000000|0x2000000|0x4000000|0x100000000|0x200000000000|0x2000000000000
def cc_descriptor(comps):
    for c in comps:
        if tname(c) == 'SpellDescriptorComponent':
            v = ((c.get('Descriptor') or {}).get('m_IntValue') or 0)
            if v & CC_MASK: return True
    return False
def buff_valued(b, seen=None):
    seen = seen or set()
    comps = b[2].get('Components') or []
    if cc_descriptor(comps): return True
    for c in comps:
        t = tname(c)
        if t in ('AddCondition', 'BuffStatusCondition'): return True
        if t in known and not NEUTRAL.match(t): return True
        if t == 'AddFactContextActions' or t.startswith('AddInitiator') or t == 'AddAreaEffect': return True
    return False
def unknown_types(comps):
    acts = collections.Counter(); walk(comps, acts, None)
    return {t for t in acts if not t.startswith('@') and t not in known and t not in EFFECT_ACTIONS and not NEUTRAL.match(t)}
abilities = [(g, v) for g, v in BP.items() if v[0] == 'BlueprintAbility']
stats = collections.Counter(); blockers = collections.Counter(); buffblock = collections.Counter(); ex = collections.defaultdict(list)
rows = []
for g, (t, name, d, rel) in abilities:
    comps = d.get('Components') or []
    ctypes = {tname(c) for c in comps}
    if 'AbilityVariants' in ctypes: stats['variant parent (its variants are counted)'] += 1; continue
    acts = collections.Counter(); walk(comps, acts, None)
    effect = any(a in EFFECT_ACTIONS for a in acts) or bool(ctypes & RULE_COMPONENTS)
    buffs = []
    # buffs it applies, also through a touch delivery ability or an area
    def collect(cs, seen):
        for c in cs:
            for m in re.findall(r'!bp_[0-9a-f]{32}', json.dumps(c)):
                b = ref(m)
                if not b or m in seen: continue
                seen.add(m)
                if b[0] == 'BlueprintBuff': buffs.append(b)
                elif b[0] in ('BlueprintAbility', 'BlueprintAbilityAreaEffect') and len(seen) < 40: collect(b[2].get('Components') or [], seen)
    collect(comps, set())
    area = acts.get('@BlueprintAbilityAreaEffect', 0) > 0
    valued_buff = any(buff_valued(b) for b in buffs) or (buffs and cc_descriptor(comps))
    if effect: cls = 'valued (damage/heal/summon/maneuver/dispel/removal/rule)'
    elif valued_buff or area: cls = 'valued through a buff or area'
    # the engine values a buff on enemies of an ability flagged harmful as a plain debuff (TacticalEngine: 0.25 of the
    # enemy's worth per round) even when it reads none of the buff's components
    # (only one it can aim at an enemy: a harmful-flagged ally hex - Protective Luck - is never cast)
    elif buffs and d.get('EffectOnEnemy') == 'Harmful' and d.get('CanTargetEnemies'): cls = 'valued as a plain debuff (harmful flag only)'
    elif buffs:
        cls = 'buff the AI cannot value (worth 0: never cast)'
        for b in buffs:
            for c in (b[2].get('Components') or []):
                tt = tname(c)
                if tt not in known and not NEUTRAL.match(tt): buffblock[tt] += 1; ex['B:'+tt].append(name) if len(ex['B:'+tt]) < 4 else None
    else:
        cls = 'effect not understood (never used)'
        for tt in unknown_types(comps): blockers[tt] += 1; ex['A:'+tt].append(name) if len(ex['A:'+tt]) < 4 else None
    stats[cls] += 1
    rows.append((cls, rel, name))
with open(out, 'w') as f:
    f.write(f"blueprints read: {len(BP)}; abilities: {len(abilities)}\n\n")
    for k, v in stats.most_common(): f.write(f"{v:6d}  {k}\n")
    f.write("\nTop building blocks in abilities whose effect is not understood (abilities affected, examples):\n")
    for k, v in blockers.most_common(40): f.write(f"{v:5d}  {k}  e.g. {', '.join(ex['A:'+k])}\n")
    f.write("\nTop buff components the AI cannot value, in abilities that only apply such buffs:\n")
    for k, v in buffblock.most_common(40): f.write(f"{v:5d}  {k}  e.g. {', '.join(ex['B:'+k])}\n")
    f.write("\nPer ability (class, file, name):\n")
    for r in sorted(rows): f.write(f"{r[0]} | {r[1]} | {r[2]}\n")
print(open(out).read()[:9000])
