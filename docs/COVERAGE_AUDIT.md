# Ability coverage audit (0.3.21)

What share of the game's abilities the AI can value, and which game building blocks stop it for the rest. Offline,
from the blueprint dumps (Spells, Classes, Mythic, Kineticist, Buffs, Arcanist: 16 840 blueprints, 3 328 abilities),
with `tools/CoverageAudit/coverage_audit.py`.

## Method and its limits
The AI reads an ability in `Tactics/AbilityProfile.cs` (`HasDetectableEffect`): it walks every action list, follows
referenced abilities (touch deliveries, cast-spell actions) and areas, and values damage, heals, kills, summons,
maneuvers, dispels, removals, buff durations, enchantments, and buffs - a buff by its components
(`Analysis/BuffParts`, `BuffEffectAnalyzer`) or as control (conditions, control descriptors). The script does the same
walk over the blueprint files, with the type names those readers handle (`tools/CoverageAudit/known_types.txt`,
taken from the code). It is an approximation:
- it sees the blueprints as shipped. Mods that change or build blueprints in code at startup (Tabletop Tweaks,
  DarkCodex) are not in the files - their source or a dump from a modded game is needed for those;
- "valued through a buff" means the buff has at least one component the AI reads, not that its whole effect is read;
- the in-game `[Coverage]` lines remain the ground truth for one party.

## Result (2 981 abilities, cutscene copies left out)

| | 0.3.21-test3 | 0.3.21-test7 | share now |
|---|---:|---:|---:|
| Valued: damage, heal, summon, maneuver, dispel, removal, weapon enchantment, or by the rules (Charge, Vital Strike, Cleave, Devastating Blow, melee attack actions) | 1 958 | 2 000 | 67.1 % |
| Valued through a buff or an area | 739 | 780 | 26.2 % |
| Valued as a plain debuff only (flagged harmful, aimed at an enemy; its buff unread) | - | 7 | 0.2 % |
| Applies only buffs the AI cannot read (worth 0, never cast) | 155 | 73 | 2.4 % |
| Effect not understood (never used) | 129 | 121 | 4.1 % |

About 93.5 % are valued. In the first count, the plain-debuff row was mixed into "worth 0": Command and Poison were
cast, but valued only by that guess. Of the rest, most are out of combat or not for the AI to use: Consume Spells, item
bonds, teleports, Dimension Door, resurrection, Find Traps, Atonement, Dismiss, wandering hex choice.

With the Equipment, Weapons, Items, Feats, full Spells, Units and creature-ability folders added (3 766 abilities, item,
companion, pet, summon and enemy abilities included), 92.6 % are valued. Every companion ability is read. The rest is
almost all out of combat (manuals and tomes, Pearls of Power, the shadow-plane lantern) or enemy-only (teleports, golem
attacks, test spells).

The script judges an ability by its components only. It cannot see that the game's intent flags kept the ally hexes from
being cast at all (TACTICAL_AI.md §57). The in-game `[Coverage]` lines show that.

## Combat-relevant gaps, in suggested order

| # | Gap (game building block) | Abilities | Effect of adding it |
|---|---|---|---|
| 1 | Attack modes in the base game's trigger form (`AddInitiatorAttackWithWeaponTrigger` → effect buff) and `AttackTypeAttackBonus` | Power Attack, Deadly Aim, Piranha Strike, Combat Expertise | **Done in 0.3.21** (TACTICAL_AI.md §55) |
| 2 | Invisibility (`BuffInvisibility`) | Greater Invisibility, Vanish | **Done in 0.3.21-test4** (TACTICAL_AI.md §56) |
| 3 | Protection from Energy (`ProtectionFromEnergy`) | Protection from Fire/Cold/Acid/Electricity/Sonic, communal | **Done in 0.3.21-test4** |
| 4 | Extra damage on hit (`AdditionalDamageOnHit`), crit range (Keen Edge), dice size (`IncreaseDiceSizeOnAttack`), weapon enhancement (`EquipmentWeaponTypeEnhancement`, Magic Weapon/Fang) | Scaled Fist Draconic Fury, Keen Edge, Lead Blades, Magic Fang, Greater Magic Fang | **Done in 0.3.21-test4**; Keen Edge and Magic Weapon through their weapon enchantments |
| 5 | Melee attack actions (`ContextActionMeleeAttack`, `AbilityCustomMeleeAttack`) | Two-Handed Fighter Piledriver, Devastating Blow, Blade Whirlwind, Death Attack | **Done in 0.3.21-test4** |
| 6 | Flee / approach control (`SetFleeOrApproachLogic`) | Command (Flee, Approach) | **Done in 0.3.21-test4** (hard control) |
| 7 | Poison stat damage (`BuffPoisonStatDamage`) | Poison, Poison Breath, Hoarfrost hex | **Done in 0.3.21-test4** (Constitution damage as lost HP) |
| 8 | Misfortune-style triggers (`AddTargetBeforeAttackRollTrigger`, `AddAbilityUseTargetTrigger`) | Protective Luck hex | **Done in 0.3.21-test6**, with every ally hex (§57) |
| 9 | Teamwork feat grants (`AddFactsFromCaster`, `AddFeatureIfHasFact`) | Cavalier / Vanguard / Forester Tactician | **Done in 0.3.21-test6** for Precise Strike, Outflank, Back to Back, Shake It Off |
| 10 | Next-spell metamagic (`AutoMetamagic`) and Magus arcana | Empowered / Maximized / Quickened / Reach Arcana, Bomber's Eye | **Done in 0.3.21-test6** for Empower, Maximize, Persistent on the next spell |
| 11 | Mythic | Aeon Edicts (`AddPhysicalImmunity`), Golden Dragon Might / Pride / Thousand Bites, Azata Joy of Life / Second Breath | **Done in 0.3.21-test7** for the Edicts and the Golden Dragon buffs; the rest in TACTICAL_AI.md §58 |

Each is a reader for one more game component; none needs play-testing a whole class first. What is left is listed
in TACTICAL_AI.md §58.

## Verification with the real readers (0.3.21-test8)
The script above guesses from component names. `tools/ProfileAudit` instead loads the blueprint dumps with the game's
own loader and runs the mod's real readers on each ability (`AbilityProfile.Get`, `BuffPartsReader.Read`,
`BuffEffectAnalyzer.Analyze`, `TacticalEngine.ModeOf` / `ProfileOf`), then mirrors the engine's target sides and value
paths. Verdicts:
- **valued**: by damage, heal, control, maneuver, cleanse, dispel, removal, summon, weapon enchantment or read buff parts;
- **GUESSED**: offered, but worth comes from a fallback (old buff quality, flat 0.25 debuff, 0.15 mark, spell-level
  enchant guess, 0.05 marker);
- **nothing**: offered but worth 0 (effect not understood, or an unreadable buff on a limited-use ability);
- **NEVER OFFERED**: no target side matches, so the engine never considers it.

Party abilities (enemy, NPC and cutscene folders left out):

| | first run | after the fixes |
|---|---:|---:|
| valued | 2 420 | 2 541 |
| GUESSED | 507 | 415 |
| nothing | 302 | 267 |
| NEVER OFFERED | 113 | 41 |

All folders (4 801 abilities, 0 read errors): 2 929 valued, 480 guessed, 365 nothing, 52 never offered, 18 kinetic
blade internals. Toggles: 138 valued, 84 guessed, 74 nothing, 661 not managed by design (player choices, hidden,
started by the game). The 52 never offered are almost all correct: self-damage costs (Vicious), teleports, Dismiss,
Magic Hack internal slots, Raise Dead, enemy-only charges and breath weapons. The fixes and what is still unread:
TACTICAL_AI.md §59.

## Re-running
```
python3 -I tools/CoverageAudit/coverage_audit.py <dump folders...> tools/CoverageAudit/known_types.txt out.txt
```
Update `known_types.txt` when a reader is added (the type names from the code). For a modded game, run it on a blueprint
dump made with the mods loaded. The script follows buffs, areas and touch deliveries, not item enchantments: an ability
with `EnhanceWeapon` counts as valued, as the engine values it (from its enchantment, or by spell level when the
enchantment is unread).

Real readers (needs the game's `Wrath_Data/Managed` folder and a Release build of the mod; .NET 8):
```
tools/ProfileAudit/run.sh <Managed dir> bin/Release/CompanionAI_Pathfinder.dll <work dir> out.txt <dump folders...>
```
