# Summon-kit inventory (S1)

13 November 2026, Claude. Tooling and documentation only: no gameplay source, manifest or package changes; the
v0.3.52 stable package is untouched. Branch `claude/summon-kit-inventory`, from development `5138990`.

The question: when AI Control drives a summoned creature (opt-in, `docs/TACTICAL_AI.md` §156), what does that creature's
kit ask of the engine, and which parts of it do the mod's readers not fully recognise? Until now the answer rested on
two creatures seen in play. This inventory reads every summon the loaded game data can produce.

**What a "read" means here.** A reading is the verdict of the mod's own blueprint readers (`AbilityProfile`,
`BuffPartsReader`, the engine's mode and toggle readers), the same verdicts as ProfileAudit's ability rows. It says a
reader recognises what the blueprint does. It is **not** evidence that the engine uses the ability at the right time,
on the right target, or at all in a fight. Tactical competence needs play; the inventory only says where to look.

## How it is read

`tools/ProfileAudit/SummonKitCheck.cs` (section `SUMMON-KITS` of the audit report):

1. **Sources.** Every loaded blueprint whose own object graph holds a `ContextActionSpawnMonster` - the only native
   route that gives a creature the summon part (`RuleSummonUnit`), which is what makes it drivable. The game's own flat
   element list is counted independently and must agree. `ContextActionSpawnUnlinkedMonster` and
   `SpawnMonsterAfterInitialized` create units without the summon part: counted, never driven.
2. **How a party member could come by the source** (static references only): a spell on a spell list; an ability a
   feature or an item gives and no creature blueprint names; an ability a creature blueprint or brain names; a
   triggered source (buff, feature, area, enchantment); an ability nothing names. "Party-plausible" is the first two.
   It is a reference heuristic, not proof that a party member has the ability.
3. **Creatures.** The unit each spawn action names, deduplicated. A spawn action that does not link the creature to
   its caster (`DoNotLinkToCaster`) never produces a driven summon.
4. **Kit**, from the creature's blueprint, following the game's unit setup (`UnitDescriptor`): the unit's ability
   components, its facts, templates, race, class levels (progression entries up to the level, archetype changes,
   named selections, named spells), and what those facts give in turn - `AddFacts`, `AddFeatureOnClassLevel` (against
   the creature's whole level, an upper bound), `AddFeatureIfHasFact` (settled once the rest is known). Body weapons,
   the brain's cast actions and a few mechanism markers (swarm body, on-hit trigger, aura, grapple on a hit) are read
   alongside.
5. **Classification** of every active ability and switch, in the engine's order: the summon policy
   (`SummonRules.AbilityRefusal`: summons nothing, spends no material), what the action bar offers (hidden abilities
   are not offered), then the readers' verdict with their reason.

Kept apart, so they do not distort the counts:

- **Universal actions.** The game gives every creature the root blueprint's Charge ability and the basic feats
  progression (`UnitDescriptor`, `UnitProgressionData`): Fight Defensively, the skill actions, the Inspire Rage switch.
  They are classified once, not once per creature. The root blueprint is not in the dumps, so they are found by the
  two ids the stock 2.7.0x root names; the report says so.
- **Brain-only abilities.** What a creature's brain blueprint casts but no blueprint fact gives. Brains are shared (one
  air elemental brain names every size's Whirlwind), so these are listed and not counted.

## Inputs and provenance

| Input | What | Count |
| --- | --- | --- |
| Dump folders | The author's blueprint exports (twenty folders, all earlier uploads) | 44,645 blueprint files; 44,644 load |
| Supplementary fixtures | Missing-only stock graphs from the BubblePrints 2.7.0x database (236,660 records) | 607 blueprints |
| External unit list | Creatures the Demonology mod summons, read from its binary | 29 creature ids |
| Loaded in all | | 45,251; one known failure |

- The one load failure is the known `AchaekekSacredWeaponFeature` enum mismatch; the run allows exactly one failure
  and is therefore PARTIAL in the audit's terms, as before.
- **Supplement** (`tools/ProfileAudit/summon_kit_supplement.py`): the stock closure source -> creature -> kit, written
  only for blueprints no dump folder supplies; `--supplement` in ProfileAudit enforces the same rule, so a dump
  folder's blueprint is never replaced. Two passes: the second is seeded with the first pass's "unloaded kit
  references", the Demonology creature ids and the two universal ids. Manifest SHA-256
  `b53d18553529dafaac14f3434798ecc2a4ca8e3117d5c2f49f8d0084d5405555` (private; game content is not committed).
  What the supplement contributes to the inventory itself is small: **6 of 323 sources, 1 of 253 creatures, 2 of 456
  distinct kit abilities**. Its main effect is that kits resolve their references: without it 232 creatures have
  unloaded kit references and 95 name a brain that is not loaded; with it 3 creatures have one unloaded reference each.
- **Demonology** (`docs/competence/SUMMON_KITS_DEMONOLOGY_UNITS.txt`): the played v0.3.51 summons ("Demonic Art II -
  Summon Abricandilu / Vermlek") come from this mod, version 0.1.5, DLL SHA-256 `041b5426...`. It builds its abilities
  in code at game start by copying a stock linked spawn action and pointing it at **stock** creature ids, so the
  creatures' kits are readable although the abilities' graphs exist only in a running game. Its after-spawn ritual
  buff is the mod's own and is not read.

## Coverage

All figures are from the final run (detail SHA-256 `65f39e54...`, `docs/competence/SUMMON_KITS_DETAIL.txt`).

**Sources: 323 blueprints, 459 spawn references.** This is every stock 2.7.0x blueprint with a spawn action.

| Category | Sources | Party-plausible |
| --- | --- | --- |
| Feature ability | 117 | yes |
| Spell-list spell | 102 | yes |
| Triggered by a buff | 33 | no |
| Ability nothing names | 20 | no |
| Creature ability | 18 | no |
| Item ability | 16 | yes |
| Triggered by a feature | 14 | no |
| Triggered by a weapon enchantment / an area | 3 | no |

235 are party-plausible. Four abilities hold a spawn action the profile does not read as summoning because it is
nested (`SoullesCommandAbility`, `SummonAnotherAbility`, `RiftcarverAbility`, `TricksterSummonAbilityTier2`): a summon
with one of them would not be stopped by the "summons nothing" rule. None is in a party-reachable kit. Thirteen further
blueprints use a spawn route without the summon part.

**Creatures: unique against repeated.**

| | Count |
| --- | --- |
| Spawn references | 459 |
| ... that resolve to a loaded creature | 452 |
| ... to a creature id that is not loaded | 7 (6 ids) |
| **Unique creatures from loaded sources** | **233** |
| Repeated references | 219 |
| Creatures with more than one source | 88 |
| Linked to the caster by at least one source | 191 |
| Only ever unlinked (never driven) | 42 |
| Reachable from a party-plausible linked source | 146 |
| Demonology creatures | 29 (9 already among the 233, 20 new) |
| **Unique creatures in the inventory** | **253** |
| **Party-reachable, not a swarm** (the set the gaps are ranked on) | **165** |

**Missing and dynamic cases.**

- **Six creature ids are named by a spawn action and exist nowhere in the stock database**: the game's own dangling
  references (`UndeadSummonAnimate`, `SoulsCloakSummonAstradaemon`, `SummonRatSwarm_Grimoire`, `WarpaintedSkullSpell`,
  `TestSummonAbility`, `KnurlyWitchSummon`). Two of the sources are item abilities. Nothing can be read for them.
- **Three kit references are absent from the stock database** (`DemonicHungerBalor`, `LanternKing`, `WyvernCharoite`).
- **107 creatures have a part the blueprint leaves to the running game.** Among the 165: a feature selection with no
  choice named (42), a brain that casts abilities no blueprint fact gives (32), a spellbook with no spell named (5:
  Paladin, Bloodrager and mythic books), difficulty-gated facts (2).
- **Not visible to any static reading**: facts and buffs a summoner's feats add at the spawn (`RuleSummonUnit` takes
  them from its listeners), a spawn action's after-spawn buffs, level-up automation, and other mods' patches and
  code-built summons. A mod that builds its summons in code and does not point at stock creatures needs a runtime export.

**Calibration against play.** The played v0.3.51 log lists the live action bars of the two driven summons. The
creatures' own kits match exactly: Abrikandilu - Sunder Armor, Cause Fear, Power Attack (3 of 3; Sunder comes through
Improved Sunder's conditional fact, now a check of the tool); Vermlek - Mass Inflict Light Wounds, Abandon Flesh (2 of
2). The policy refusal the log shows ("Treat Affliction not offered - a summoned creature spends no material
component") is the one the inventory gives. The universal list is **incomplete**: of the ten universal entries on the
live bar the static reading finds six; Coup de Grace, the two Aid Another actions and Use Weapon One Handed come from
somewhere the tool does not read (the game outside these two routes, or another mod). Two creatures are a small sample.

## What the kits are

Of the 165 party-reachable, non-swarm creatures:

- **All carry weapons; 157 fight in melee, 8 with a ranged weapon.** 27 have no active ability or switch at all.
- **80 have every active ability read** (or on by default, or refused by policy); **58 have at least one gap**.
- **108 have a brain that casts something**; 57 use a brain with no cast action.
- **117 have an on-hit trigger and 24 an aura; none grapples on a hit.** The on-hit riders are *not classified* in this
  slice - whether the weapon model values them is open.
- 14 creatures in the whole inventory are swarm bodies: the summon policy refuses them and the game's brain keeps them.

Readings of their 672 creature-ability pairs (261 distinct abilities):

| Reading | Pairs |
| --- | --- |
| Read | 502 |
| Read with a guessed part | 48 |
| Guessed | 31 |
| Unread | 13 |
| Not offered | 24 |
| On by default (no decision) | 44 |
| Refused by the summon policy | 10 |

178 of the pairs are cast by the creature's own brain; 37 of those are not fully read. The brain is the designers'
statement of how the creature plays, so those 37 are where a driven summon may do less than the game's own AI.

## Gaps, ranked

Ranked by affected coverage (brain-cast pairs first, then how many ways the party reaches the creatures) and then
re-ordered by my judgement of gameplay value. The first column is the tool's; the judgement is mine and is not measured.

| # | Gap | Coverage | Why it may matter | Evidence class |
| --- | --- | --- | --- | --- |
| 1 | **Closing to melee** (not a reader gap) | 157 of 165 creatures are melee; 27 can do nothing else | The played log refuses Abrikandilu's approaches ten times ("path ends out of reach", 0.3-0.7 m short); 4 of its 9 decisions end the turn. Companions show the same refusal (Valmot 9, Camellia 7), so this is the shared idle-approach topic, not a summon defect | Confirmed occurrence, cause open |
| 2 | **Position changes the readers do not understand** | 9 abilities, 11 creatures, 15 ways; 3 brain-cast pairs | Demon Teleport, Dimension Door, Greater Teleport, Liquid Leap, Overrun: the game's brain uses them to reach a target; the engine has no value for them, so a driven demon walks. Shadow Conjuration / Evocation and Prismatic Surge share the "effect not understood" verdict but are different mechanisms | Confirmed reader gap; value is a hypothesis |
| 3 | **Self-effects with a condition and a combat trigger** | 6 abilities, about 8 creatures | Stunning Screech, Unholy Nimbus, Song of Discord, Touch of Chaos, Derakni Drone, Implosion are valued by the old quality guess. Several belong to Demonology creatures the author plays (Vrock, Nalfeshnee, Derakni) | Confirmed reader gap |
| 4 | **On-hit riders** | 117 creatures | Not classified here. Poison, trip and drain on a hit change what an attack is worth | Missing classification |
| 5 | **Marks bound to one target** (Smite Evil, Fiendish and Half-Fiend Smite Good, Aura of Justice) | 4 abilities, 8 creatures, 11 ways; 4 brain-cast pairs | Guessed as a debuff at blueprint level. The engine couples own-attack marks with the attack target at run time, which this reading cannot see | Blueprint verdict; live behaviour unknown |
| 6 | **Whirlwind** (air elementals) | 5 abilities, 6 creatures, 7 ways; 6 brain-cast pairs | Read as an aura with a harmful part; only a presentation component is unread. Whether entering it near allies is wise is a play question | Mostly read |
| 7 | **Burrow / Unburrow** | 4 abilities, 3 creatures | An untargetable state the readers guess | Confirmed reader gap, narrow |
| 8 | **Enervation's guessed part** | 1 ability, 8 creatures; 7 brain-cast pairs | The damage is read; the guessed part is temporary hit points for undead targets | Low |
| 9 | **Switches that start off and are not managed** | 5 switches, 16 creatures, 29 ways | Critical-feat and Point-Blank switches. Whether the game switches them on for a creature is not established here | Unknown |

Not gaps: 44 switches the game turns on by itself; 10 policy refusals (a driven summon summons nothing - one of them
is an ability its own brain would cast, an intended difference from the game's AI).

**Grapple.** No summonable creature in the stock data grapples on a hit. Paired grapple support (Grabber) is a Shifter
and enemy matter and has no dependency on summon work.

**Demonology creatures** (the author's actual summons): 29 creatures, all loaded, none weapon-only; 15 have every
active ability read, 14 have a gap. They account for much of rows 2 and 3 above.

## Limits

- Static blueprint reading; no unit, spawn, cast, command or Unity execution. See "Missing and dynamic cases".
- Class-level gates are read against the creature's whole level, so a multiclass creature may be given a feature it
  would not have. Conditional facts are settled in one direction (a fact given because another is absent stays given).
- "Party-plausible" is a static reference heuristic. A feature ability that only an enemy ever has still counts.
- The universal list is known to be incomplete (calibration above).
- The ranking's second step is judgement. Coverage counts creatures and sources, not how often a player summons them.
- The dump folders are one author's exports; another installation's mods change the answer.

## Reproducing

```sh
# 1. missing-only stock fixtures (private output); second pass seeded with the first run's unloaded references
python3 -I tools/ProfileAudit/summon_kit_supplement.py [--seed seeds.txt] <stock.jsonl> <supplement dir> 6 <dump folders...>
# 2. the audit, with the inventory's detail file
tools/ProfileAudit/run.sh <Managed> <mod dll> <work dir> <report.txt> --allow-failures 1 \
    --supplement <supplement dir> --summon-unit-list docs/competence/SUMMON_KITS_DEMONOLOGY_UNITS.txt \
    --summon-kit-report <detail.txt> <dump folders...>
# 3. the script's own tests
python3 -I tools/ProfileAudit/test_summon_kit_supplement.py
```

Without `--supplement` and `--summon-unit-list` the section still runs on whatever is loaded and reports what is
missing; with no spawn source loaded it reports SKIPPED rather than a pass.

## Checks

Receipt: [competence/SUMMON_KITS_CHECKS.json](competence/SUMMON_KITS_CHECKS.json).

- `SUMMON-KITS` 8 checks pass. Two runs give byte-identical report and detail.
- Every other audit section is unchanged against a baseline build of development's tool on the same inputs: 38
  sections with identical check counts (959 checks, no failure), and reports identical line for line apart from
  one fixture label that depends on load order.
- The stock `ChargeBuff` and `ChargeBuffEffect` graphs the leap check requires come with the supplement; their raw
  SHA-256 equal the ones recorded in `docs/competence/REVIEW2_CHARGE_INPUTS.json`. Without the supplement the audit
  fails on that named requirement, as it does on development.
- 787 unit tests, 27 inventory tests and 3 new script tests pass. Release build against the real game assemblies: 0
  warnings, 0 errors. ToggleCosts 98, TomeBurn 14, ToyBoxSpend 10 native checks pass (they compile the audit sources).
- Gameplay source, `Info.json` and `release/` are unchanged from development `5138990`.

## Played-log inputs

The author supplied the v0.3.51 logs for the later Invocation ledger (I1); this slice uses them only for the
calibration above.

| File | Bytes | SHA-256 | Against `docs/V0352_PLAYTEST_INPUTS.json` |
| --- | --- | --- | --- |
| `Log.txt` | 937,531 (4,608 lines) | `e661b9d8...ad7cbb` | **matches** |
| `combatLog.txt` | 533,176 (13,450 lines) | `cca3419d...15bc3de` | **does not match** (recorded: 484,524 bytes, 13,608 lines, `27b61816...`) |

The uploaded combat log is byte-identical to the one recorded in `docs/V0351_PLAYTEST_INPUTS.json`: the earlier
stable v0.3.50 session's file (four fights), not the v0.3.51 session's (six fights). The AI log carries the decision
scores I1 needs; the matching combat log is needed only to cross-check outcomes.

## Next

Recommended: **S2 - approach end points** (row 1). The evidence is in the supplied log, it touches nearly every summon
and several companions, and it is the difference between a summon that acts and one that ends its turn. Start with a
bounded reading of the refused paths (which reach the pre-issue check uses against the creature's real reach and
body) before any change. Then I1 (the Invocation ledger, now unblocked by `Log.txt`), then rows 2-3 for the Demonology
creatures, which need play to confirm value.
