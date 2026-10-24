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

| | 0.3.21-test3 | 0.3.21-test6 | share now |
|---|---:|---:|---:|
| Valued: damage, heal, summon, maneuver, dispel, removal, weapon enchantment, or by the rules (Charge, Vital Strike, Cleave, Devastating Blow, melee attack actions) | 1 958 | 1 972 | 66.2 % |
| Valued through a buff or an area | 739 | 792 | 26.6 % |
| Valued as a plain debuff only (flagged harmful, aimed at an enemy; its buff unread) | - | 7 | 0.2 % |
| Applies only buffs the AI cannot read (worth 0, never cast) | 155 | 85 | 2.9 % |
| Effect not understood (never used) | 129 | 125 | 4.2 % |

About 93 % are valued. In the first count, the plain-debuff row was mixed into "worth 0": Command and Poison were
cast, but valued only by that guess. Of the rest, most are out of combat or not for the AI to use: Consume Spells, item
bonds, teleports, Dimension Door, resurrection, Find Traps, Atonement, Dismiss, wandering hex choice.

With the Equipment, Weapons, Items, Feats and full Spells folders added (3 279 abilities, item abilities included),
92.4 % are valued. The items left are almost all out of combat: manuals and tomes, Pearls of Power, the shadow-plane
lantern.

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
| 11 | Mythic | Aeon Edicts (`AddPhysicalImmunity`), Golden Dragon Might / Pride / Thousand Bites, Azata Joy of Life / Second Breath | per path, after the above |

Each is a reader for one more game component; none needs play-testing a whole class first. Next: the mythic
paths (11).

## Re-running
```
python3 -I tools/CoverageAudit/coverage_audit.py <dump folders...> tools/CoverageAudit/known_types.txt out.txt
```
Update `known_types.txt` when a reader is added (the type names from the code). For a modded game, run it on a blueprint
dump made with the mods loaded. The script follows buffs, areas and touch deliveries, not item enchantments: an ability
with `EnhanceWeapon` counts as valued, as the engine values it (from its enchantment, or by spell level when the
enchantment is unread).
