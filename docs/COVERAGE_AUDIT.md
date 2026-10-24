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

| | 0.3.21-test3 | 0.3.21-test4 | share now |
|---|---:|---:|---:|
| Valued: damage, heal, summon, maneuver, dispel, removal, or by the rules (Charge, Vital Strike, Cleave, Devastating Blow, melee attack actions) | 1 958 | 1 965 | 65.9 % |
| Valued through a buff or an area | 739 | 762 | 25.6 % |
| Valued as a plain debuff only (flagged harmful, aimed at an enemy; its buff unread) | - | 7 | 0.2 % |
| Applies only buffs the AI cannot read (worth 0, never cast) | 155 | 122 | 4.1 % |
| Effect not understood (never used) | 129 | 125 | 4.2 % |

About 91.5 % are valued. In the first count, the plain-debuff row was mixed into "worth 0" (Command and Poison were
cast, valued only by that guess). Of the rest, most are out of combat or not for the AI to use: Consume Spells, item
bonds, teleports, Dimension Door, resurrection, Find Traps, Atonement, Dismiss, wandering hex choice.

## Combat-relevant gaps, in suggested order

| # | Gap (game building block) | Abilities | Effect of adding it |
|---|---|---|---|
| 1 | Attack modes in the base game's trigger form (`AddInitiatorAttackWithWeaponTrigger` → effect buff) and `AttackTypeAttackBonus` | Power Attack, Deadly Aim, Piranha Strike, Combat Expertise | **Done in 0.3.21** (TACTICAL_AI.md §55) |
| 2 | Invisibility (`BuffInvisibility`) | Greater Invisibility, Vanish | **Done in 0.3.21-test4** (TACTICAL_AI.md §56) |
| 3 | Protection from Energy (`ProtectionFromEnergy`) | Protection from Fire/Cold/Acid/Electricity/Sonic, communal | **Done in 0.3.21-test4** |
| 4 | Extra damage on hit (`AdditionalDamageOnHit`), crit range (Keen Edge), dice size (`IncreaseDiceSizeOnAttack`), weapon enhancement (`EquipmentWeaponTypeEnhancement`, Magic Weapon/Fang) | Scaled Fist Draconic Fury, Keen Edge, Lead Blades, Magic Fang, Greater Magic Fang | **Done in 0.3.21-test4**, except Keen Edge (needs its enchantment blueprint from the Equipment folder) |
| 5 | Melee attack actions (`ContextActionMeleeAttack`, `AbilityCustomMeleeAttack`) | Two-Handed Fighter Piledriver, Devastating Blow, Blade Whirlwind, Death Attack | **Done in 0.3.21-test4** |
| 6 | Flee / approach control (`SetFleeOrApproachLogic`) | Command (Flee, Approach) | **Done in 0.3.21-test4** (hard control) |
| 7 | Poison stat damage (`BuffPoisonStatDamage`) | Poison, Poison Breath, Hoarfrost hex | **Done in 0.3.21-test4** (Constitution damage as lost HP) |
| 8 | Misfortune-style triggers (`AddTargetBeforeAttackRollTrigger`, `AddAbilityUseTargetTrigger`) | Protective Luck hex | defensive hex |
| 9 | Teamwork feat grants (`AddFactsFromCaster`, `AddFeatureIfHasFact`) | Cavalier / Vanguard / Forester Tactician | grants teamwork feats to the party |
| 10 | Next-spell metamagic (`AutoMetamagic`) and Magus arcana | Empowered / Maximized / Quickened / Reach Arcana, Bomber's Eye | like the next-spell boosters already handled |
| 11 | Mythic | Aeon Edicts (`AddPhysicalImmunity`), Golden Dragon Might / Pride / Thousand Bites, Azata Joy of Life / Second Breath | per path, after the above |

Each is a reader for one more game component; none needs play-testing a whole class first. Next: 8-10, then the
mythic paths.

## Re-running
```
python3 -I tools/CoverageAudit/coverage_audit.py <dump folders...> tools/CoverageAudit/known_types.txt out.txt
```
Update `known_types.txt` when a reader is added (the type names from the code). For a modded game, run it on a blueprint
dump made with the mods loaded.
