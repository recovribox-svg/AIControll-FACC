#!/usr/bin/env python3
"""Profane Invocation transfer ledger (docs/INVOCATION_LEDGER.md, docs/TACTICAL_AI.md section 164).

Reads an AI Control Log.txt and lists every Profane Invocation the mod issued: who received it, who held it before,
what the engine counted, whether the cast acted, and how the cast classifies. It reads only the mod's own log rows;
it executes nothing and needs no game files.

    invocation_ledger.py [--json] <Log.txt>

The game's rules the classification rests on (Demonic Overlord's Profane Ascension, read from the exported blueprints):
- twelve abilities, two tiers of six stats; each is a free action with no resource and applies a permanent buff;
- every buff is +2 Profane to one stat, so the same stat from both tiers does not stack on one recipient;
- every buff is unique (UniqueBuff: one holder per caster) and stays on death;
- each ability removes the five other buffs of its own tier from its target (its siblings); tiers are independent.

Classes, per acted cast:
- first-placement: nobody held the buff and nothing was removed;
- sibling-replacement: the recipient's own buff of the same tier was removed (and nobody else held this one);
- move-settled: moved from an ally that held it, the placement settled (not put there by this caster within
  3 rounds of this fight, ValueModel.UniqueSettleRounds);
- move-override: moved within those 3 rounds, on a gain of 5 or more (ValueModel.UniqueSettleOverride);
- holder-unseen: the ledger knows a holder but the engine priced no holder: that ally was not in the engine's ally list
  (dead, unconscious or out of combat). The evidence column says what the log shows about that ally.
Flags: low-loss (the holder lost 0.3 or less), last-enemy (one enemy or none left at the decision), sibling (a same-tier
buff removed), same-stat (the recipient already held the other tier of this stat: the bonus would not stack, the game's
ModifierDescriptorHelper.IsStackable leaves Profane out).

Sequence findings, per fight:
- cross-tier: tier a of a stat left ally P and tier b of the same stat came onto P within one round. The one-cast
  alternative is tier b straight onto the tier-a recipient Q. It is redundant only when Q held no tier-b buff of
  another stat (the alternative would remove it); the ledger says which.
- chain: the same buff moved twice within one round (P -> Q -> R).
- return: a buff came back to an ally that held it earlier in the same fight.
- re-placed: a sibling removed while one enemy or none was left, and the next fight cast that buff again.
Interrupted or unacted casts are listed and change no holder.
"""
import json
import re
import sys

STATS = ('Strength', 'Dexterity', 'Constitution', 'Intelligence', 'Wisdom', 'Charisma')
# Ability blueprint ids -> (stat, tier): identifiers from the game's Demonic Overlord graphs (also printed in the log).
ABILITY_TIER = {
    '4ff5f23901104b77a481eac095fc8967': ('Strength', 1), '8ea302645ae341e9bcca4929d1822c77': ('Strength', 2),
    '9d1b94bea3984ee3922d201ef49a9163': ('Dexterity', 1), '4d25cab4db9f4816a710265dfee532e4': ('Dexterity', 2),
    'cbcfe6f6242d4d588493cfca27fdee58': ('Constitution', 1), '74fde6f81db9434cb94c6f4c2eef9ecc': ('Constitution', 2),
    '3eec5b30dba74e6c8e3364526312c1dc': ('Intelligence', 1), '037b267ffad54f9dbeb8a9a145b57acd': ('Intelligence', 2),
    '6ba52e25e53d4c148ca4c1f780b4551f': ('Wisdom', 1), 'f6b7825f6b2c4b97936d90fd37adaf7e': ('Wisdom', 2),
    '9ff93194eea84d569b9fdbe6bd284da3': ('Charisma', 1), 'ada16c52b3224649990dc84f88a8a341': ('Charisma', 2),
}
SETTLE_ROUNDS = 3        # ValueModel.UniqueSettleRounds
SETTLE_OVERRIDE = 5.0    # ValueModel.UniqueSettleOverride
LOW_LOSS = 0.3

PREFIX = r'^(?:\[CompanionAI_Pathfinder\] )?(?:\[V\] )?'
RE_FIGHT = re.compile(r'Party combat started')
RE_DECISION = re.compile(PREFIX + r'\[Tactics\] (?P<unit>.+?) (?P<round>RT?\d+) HP(?P<hp>\d+)%(?P<rest>.*)$')
RE_REVIVE = re.compile(PREFIX + r'\[Tactics\] (?P<unit>.+?): revive (?P<target>.+?) with ')
RE_KEPT = re.compile(PREFIX + r'\[Buff\] (?P<caster>.+?): Profane Invocation — (?P<stat>\w+) \[(?P<guid>[0-9a-f]{32})\] '
                     r'stays on (?P<holder>.*?) \(unique buff: (?P<why>.*)\)$')
RE_CHOSEN = re.compile(PREFIX + r'\[Buff\] (?P<caster>.+?): Profane Invocation — (?P<label>\w+->.*?) = (?P<net>-?[\d.]+) '
                       r'\((?P<slot>\w+)\) - (?P<gross>-?[\d.]+):.*?ability=(?P<guid>[0-9a-f]{32})')
RE_ISSUED = re.compile(PREFIX + r"\[Cmd\] (?P<caster>.+?): ISSUED UnitUseAbility 'Profane Invocation — (?P<label>\w+->.*)' "
                       r'mode=(?P<mode>TB|RT)')
RE_ENDED = re.compile(PREFIX + r"\[Cmd\] (?P<caster>.+?): ENDED UnitUseAbility 'Profane Invocation — (?P<label>\w+->.*)' "
                      r'result=(?P<result>\w+) acted=(?P<acted>\w+) moved=[\d.]+m after (?P<secs>[\d.]+)s')
RE_LABEL = re.compile(r'^(?P<stat>\w+)->(?P<recipient>.*?)\((?P<reasons>.*)\)$')
RE_FROM = re.compile(r'from (?P<holder>.*?) -(?P<loss>[\d.]+)')
RE_REPLACES = re.compile(r'replaces ProfaneAscension(?P<stat>\w+?)Buff(?P<tier>\d) -(?P<loss>[\d.]+)')
RE_UNKNOWN_NAME = re.compile(r'\[unknown key: [0-9a-f-]+\]')


def name(raw):
    """The log's unit name; a localisation miss and an empty name get stable labels."""
    raw = raw.strip()
    if RE_UNKNOWN_NAME.fullmatch(raw):
        return 'main character (name key missing)'
    return raw if raw else '(unnamed ally)'


def round_number(label):
    digits = re.sub(r'\D', '', label or '')
    return int(digits) if digits else 0


def parse(lines):
    """One pass over the log: fights, decisions, notes and the issued casts with their outcomes."""
    fight = 0
    holders = {}            # (stat, tier) -> unit name, the ledger's view
    placed = {}             # (stat, tier) -> (fight, round number) of this caster's last placement
    held_before = {}        # (stat, tier) -> set of units that held it in this fight
    last_decision = {}      # unit -> (line, round label, hp)
    decisions = []          # (line, fight, unit, round, hp)
    revives = []            # (line, fight, reviver, target)
    caster_decision = {}    # caster -> (round label, enemies, rounds) of its latest decision
    chosen = {}             # label -> chosen [Buff] row
    pending = []            # issued casts waiting for their ENDED row
    casts = []
    fight_start_holders = {}
    for number, text in enumerate(lines, 1):
        if RE_FIGHT.search(text):
            fight += 1
            placed = {}
            held_before = {k: ({v} if v else set()) for k, v in holders.items()}
            fight_start_holders[fight] = {}
            continue
        m = RE_DECISION.match(text)
        if m:
            unit = name(m.group('unit'))
            last_decision[unit] = (number, m.group('round'), int(m.group('hp')))
            decisions.append((number, fight, unit, m.group('round'), int(m.group('hp'))))
            enemies = re.search(r'enemies=(\d+) rounds~([\d.]+)', m.group('rest'))
            caster_decision[unit] = (m.group('round'), int(enemies.group(1)) if enemies else None,
                                     float(enemies.group(2)) if enemies else None)
            continue
        m = RE_REVIVE.match(text)
        if m:
            revives.append((number, fight, name(m.group('unit')), name(m.group('target'))))
            continue
        m = RE_KEPT.match(text)
        if m:
            key = ABILITY_TIER.get(m.group('guid'))
            if key:
                holders[key] = name(m.group('holder'))
                held_before.setdefault(key, set()).add(holders[key])
                fight_start_holders.setdefault(fight, {}).setdefault(key, holders[key])
            continue
        m = RE_CHOSEN.match(text)
        if m:
            chosen[m.group('label')] = dict(line=number, net=float(m.group('net')), gross=float(m.group('gross')),
                                            slot=m.group('slot'), key=ABILITY_TIER.get(m.group('guid')))
            continue
        m = RE_ISSUED.match(text)
        if m:
            caster = name(m.group('caster'))
            label = m.group('label')
            lm = RE_LABEL.match(label)
            row = chosen.get(label, {})
            decision = caster_decision.get(caster, (None, None, None))
            reasons = lm.group('reasons') if lm else ''
            frm, rep = RE_FROM.search(reasons), RE_REPLACES.search(reasons)
            cast = dict(line=number, fight=fight, mode=m.group('mode'), caster=caster, label=label,
                        stat=lm.group('stat') if lm else None, recipient=name(lm.group('recipient')) if lm else None,
                        key=row.get('key'), net=row.get('net'), gross=row.get('gross'), slot=row.get('slot'),
                        round=decision[0], enemies=decision[1], rounds_left=decision[2],
                        engine_holder=name(frm.group('holder')) if frm else None,
                        holder_loss=float(frm.group('loss')) if frm else None,
                        sibling=(rep.group('stat'), int(rep.group('tier'))) if rep else None,
                        sibling_loss=float(rep.group('loss')) if rep else None,
                        result=None, acted=None, seconds=None)
            pending.append(cast)
            casts.append(cast)
            continue
        m = RE_ENDED.match(text)
        if m:
            cast = next((c for c in pending if c['label'] == m.group('label') and c['caster'] == name(m.group('caster'))), None)
            if cast is None:
                continue
            pending.remove(cast)
            cast.update(result=m.group('result'), acted=m.group('acted') == 'True', seconds=float(m.group('secs')),
                        ended_line=number)
            key = cast['key']
            cast['known_holder'] = holders.get(key) if key else None
            cast['holdings'] = dict(holders)
            cast['held_before'] = sorted(held_before.get(key, set())) if key else []
            if key and key in placed and placed[key][0] == cast['fight']:
                cast['rounds_since_placed'] = round_number(cast['round']) - placed[key][1]
            else:
                cast['rounds_since_placed'] = None
            if not cast['acted'] or not key:
                continue
            holders[key] = cast['recipient']
            placed[key] = (cast['fight'], round_number(cast['round']))
            held_before.setdefault(key, set()).add(cast['recipient'])
            if cast['sibling'] and holders.get(cast['sibling']) == cast['recipient']:
                holders[cast['sibling']] = None
    return dict(casts=casts, decisions=decisions, revives=revives, fight_start_holders=fight_start_holders, fights=fight)


def holder_evidence(cast, decisions, revives):
    """What the log shows about the ally the ledger believes held the buff, up to this cast."""
    holder = cast['known_holder']
    own = [d for d in decisions if d[2] == holder and d[1] == cast['fight'] and d[0] < cast['line']]
    revived = [r for r in revives if r[3] == holder and r[1] == cast['fight'] and r[0] < cast['line']]
    parts = []
    if not own:
        parts.append('no AI Control decision by this ally in this fight (not driven by the mod, or down from the start)')
    else:
        line, _, unit, rnd, hp = own[-1]
        parts.append(f'last decision {rnd} at {hp}% HP (line {line})')
    if revived:
        parts.append(f'revive considered for it at line {revived[-1][0]}')
    return '; '.join(parts)


def classify(cast, decisions, revives):
    if cast['acted'] is None:
        return 'no-outcome', 'no ENDED row'
    if not cast['acted']:
        return 'not-applied', f"{cast['result']} after {cast['seconds']} s"
    if not cast['key']:
        return 'unread', 'no chosen [Buff] row with an ability id before the ISSUED row'
    flags = []
    if cast['sibling']:
        flags.append('sibling')
    if cast['holder_loss'] is not None and cast['holder_loss'] <= LOW_LOSS:
        flags.append('low-loss')
    if cast['enemies'] is not None and cast['enemies'] <= 1:
        flags.append('last-enemy')
    other = (cast['key'][0], 3 - cast['key'][1])
    if cast['holdings'].get(other) == cast['recipient']:
        flags.append('same-stat')
    evidence = ''
    if cast['engine_holder']:
        since = cast['rounds_since_placed']
        gain = cast['net'] or 0.0
        if since is not None and since < SETTLE_ROUNDS:
            kind = 'move-override' if gain >= SETTLE_OVERRIDE else 'move-under-settle'
            evidence = f'placed {since} round(s) ago, net {gain}'
        else:
            kind = 'move-settled'
    elif cast['known_holder'] and cast['known_holder'] != cast['recipient']:
        kind = 'holder-unseen'
        evidence = f"ledger holder {cast['known_holder']}: " + holder_evidence(cast, decisions, revives)
    elif cast['sibling']:
        kind = 'sibling-replacement'
    else:
        kind = 'first-placement'
    return kind + (' [' + ', '.join(flags) + ']' if flags else ''), evidence


def sequences(casts):
    """Cross-tier shuffles, chains, re-placements and returns, over acted casts (classified by build first)."""
    found = []
    acted = [c for c in casts if c.get('acted') and c['key']]
    for i, a in enumerate(acted):
        stat, tier = a['key']
        source = a['engine_holder'] or (a['known_holder'] if a['known_holder'] != a['recipient'] else None)
        if not source:
            continue
        for b in acted[i + 1:]:
            if b['fight'] != a['fight'] or b['key'] != (stat, 3 - tier) or b['recipient'] != source:
                continue
            if round_number(b['round']) - round_number(a['round']) > 1:
                break
            q = a['recipient']
            q_other = [k for k, v in a['holdings'].items() if v == q and k[1] == 3 - tier and k[0] != stat]
            if q_other:
                verdict = (f"not redundant: {q} held {q_other[0][0]} tier {q_other[0][1]}, which tier-{3 - tier} "
                           f"{stat} on {q} would have removed")
            else:
                verdict = (f"redundant: tier-{3 - tier} {stat} straight onto {q} gives the same {stat} coverage with "
                           f"one cast fewer" + (f" and keeps {source}'s {b['sibling'][0]} tier {b['sibling'][1]}"
                                                if b['sibling'] else ''))
            found.append(dict(kind='cross-tier', first=a['line'], second=b['line'], fight=a['fight'], verdict=verdict))
            break
    for i, a in enumerate(acted):
        b = next((x for x in acted[i + 1:] if x['key'] == a['key']), None)
        if b and b['fight'] == a['fight'] and round_number(b['round']) - round_number(a['round']) <= 1 and \
                b['recipient'] != (a['engine_holder'] or a['known_holder']):
            found.append(dict(kind='chain', first=a['line'], second=b['line'], fight=a['fight'],
                              verdict=f"{' '.join(map(str, a['key']))} moved {a['engine_holder'] or a['known_holder'] or 'nobody'} -> "
                                      f"{a['recipient']} -> {b['recipient']} within one round ({a.get('kind', '?').split(' ')[0]}, "
                                      f"then {b.get('kind', '?').split(' ')[0]}; values change between decisions, so not by "
                                      f"itself a redundant cast)"))
    for a in acted:
        if not a['sibling'] or a['enemies'] is None or a['enemies'] > 1:
            continue
        again = next((x for x in acted if x['fight'] == a['fight'] + 1 and x['key'] == a['sibling']), None)
        if again:
            found.append(dict(kind='re-placed', first=a['line'], second=again['line'], fight=a['fight'],
                              verdict=f"{a['sibling'][0]} {a['sibling'][1]} removed from {a['recipient']} with one enemy "
                                      f"or none left (worth {a['sibling_loss']} then), cast again in fight {again['fight']} "
                                      f"on {again['recipient']}"))
    for c in acted:
        earlier = [h for h in c['held_before'] if h == c['recipient']]
        if earlier and c['known_holder'] != c['recipient']:
            found.append(dict(kind='return', first=None, second=c['line'], fight=c['fight'],
                              verdict=f"{'/'.join(map(str, c['key']))} back on {c['recipient']}, who held it earlier in fight {c['fight']}"))
    return found


def build(lines):
    data = parse(lines)
    rows = []
    for c in data['casts']:
        kind, evidence = classify(c, data['decisions'], data['revives'])
        c['kind'] = kind
        rows.append(dict(line=c['line'], fight=c['fight'], mode=c['mode'], round=c['round'], enemies=c['enemies'],
                         buff=f"{c['key'][0]} {c['key'][1]}" if c['key'] else c['stat'], recipient=c['recipient'],
                         net=c['net'], gross=c['gross'], engine_holder=c['engine_holder'], holder_loss=c['holder_loss'],
                         sibling=f'{c["sibling"][0]} {c["sibling"][1]}' if c['sibling'] else None,
                         sibling_loss=c['sibling_loss'], result=c['result'], seconds=c['seconds'],
                         rounds_since_placed=c.get('rounds_since_placed'), kind=kind, evidence=evidence))
    flags = {}
    for r in rows:
        for flag in re.findall(r'\[(.*?)\]', r['kind'])[:1]:
            for f in flag.split(', '):
                flags[f] = flags.get(f, 0) + 1
    counts = {}
    for r in rows:
        base = r['kind'].split(' ')[0]
        counts[base] = counts.get(base, 0) + 1
    return dict(fights=data['fights'], issued=len(rows), acted=sum(1 for c in data['casts'] if c.get('acted')),
                counts=counts, flags=flags, rows=rows, sequences=sequences(data['casts']),
                fight_start_holders={f: {f'{k[0]} {k[1]}': v for k, v in h.items()}
                                     for f, h in data['fight_start_holders'].items()})


def markdown(ledger):
    out = [f"fights {ledger['fights']}, issued {ledger['issued']}, acted {ledger['acted']}; classes: " +
           ', '.join(f'{k} {v}' for k, v in sorted(ledger['counts'].items())) + '; flags: ' +
           (', '.join(f'{k} {v}' for k, v in sorted(ledger['flags'].items())) or 'none'), '',
           '| line | fight | mode | round | enemies | buff | recipient | net | gross | engine holder (loss) | sibling (loss) | outcome | class | evidence |',
           '|---|---|---|---|---|---|---|---|---|---|---|---|---|---|']
    for r in ledger['rows']:
        holder = f"{r['engine_holder']} ({r['holder_loss']})" if r['engine_holder'] else ''
        sibling = f"{r['sibling']} ({r['sibling_loss']})" if r['sibling'] else ''
        out.append(f"| {r['line']} | {r['fight']} | {r['mode']} | {r['round']} | {r['enemies']} | {r['buff']} | {r['recipient']} | "
                   f"{r['net']} | {r['gross']} | {holder} | {sibling} | {r['result']} {r['seconds']} s | {r['kind']} | {r['evidence']} |")
    out.append('')
    out.append('Sequences:' if ledger['sequences'] else 'Sequences: none')
    for s in ledger['sequences']:
        lines = f"{s['first']} -> {s['second']}" if s['first'] else str(s['second'])
        out.append(f"- {s['kind']} (fight {s['fight']}, lines {lines}): {s['verdict']}")
    return '\n'.join(out)


def main(argv):
    args = [a for a in argv[1:] if a != '--json']
    if len(args) != 1:
        print(__doc__)
        return 2
    with open(args[0], encoding='utf-8', errors='replace') as handle:
        ledger = build(handle.read().split('\n'))
    print(json.dumps(ledger, indent=1) if '--json' in argv else markdown(ledger))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
