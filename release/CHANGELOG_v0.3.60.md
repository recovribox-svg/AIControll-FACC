# AI Control - Fully Autonomous Combat Companions v0.3.60

Everything since v0.3.52. Magic Deceivers can use almost every fusion. Bursts that land late are aimed where the enemy
will be. Companions break enemy casts, free dominated allies instead of killing them, and end the areas the party stands in.
Grapples, Shifter forms and summons are handled better. Both turn-based and real-time combat, unless noted.

## Highlights

- **Magic Deceiver fusions.**
  - 14,789 of the game's 15,494 fusion combinations can now be chosen; v0.3.52 could choose 7,474.
  - Each fused effect is valued:
    - control for each save outcome;
    - Sleep's shared Hit Dice pool;
    - damage over time (Molten Orb, Acid Arrow, Incendiary Runes);
    - lines, dispels, nested saves, Vampiric Touch and Circle of Death's kill pool.
  - Cone fusions are aimed with the cone they really fire and turned away from companions.
  - A cast that could kill a companion for good (Disintegrate's dust, a kill effect) is never used at 1 % risk or more.
- **Bursts that land late** (Explosive Runes, Fireball-like projectiles, Ice Storm, Flame Strike).
  - They are aimed at the ground where the enemy will be when they land.
  - Real time: the burst is checked again until it goes off, and cancelled if a companion would now be caught. Other
    companions keep out of where it will land.
  - Turn-based: the turn waits for the landing.
- **Enemy casters (real time).** A hit before an enemy's spell goes off can break it, by the game's concentration rule.
  That chance is now worth the spell's harm.
- **Dominated companions.**
  - A companion under an enemy's control is stopped, not killed: no attack, Coup de Grace or harmful area is aimed at it.
  - Freeing it with a dispel or a removal is valued.
- **Relief spells.** Dispel Magic, Break Enchantment, Remove Fear and Unbreakable Heart are valued by what they end:
  confusion, fear, holds, penalties, domination.
  - Dispel Magic (Point) ends the enemy areas the party stands in, by the game's own rules.
  - The party's own buffs a dispel would remove count against it.
- **Grapples.**
  - Holds are valued: the victim held, then pinned, against the grappler's lost turns.
  - The AI decides the Grabber toggle and Release Grapple.
  - Swarms and Freedom of Movement are seen as grapple-immune.
- **Shifter.** Forms and Shifter's Fury are planned together, and a new form's attacks count from the next round.
  Griffon leaps price their grab.
- **Sneak attack.** Fortification is counted. A melee attack that makes its target flanked is credited.
- **Better judgement.**
  - Buffs a recipient's form or hex would suppress (Enlarge Person on a shifted ally) are not cast.
  - Frigid Touch's stagger is counted.
  - On-hit effects count on the attacks that carry them: a Shadow's Strength drain, a Schir's disease.
  - A summoned Vermlek keeps its Flesh Armor until badly hurt, then uses Abandon Flesh for its heal.
  - Coup de Grace uses the reach of the weapon that really delivers it.
- **Movement.**
  - Turn-based: a crowded approach takes a free side of the enemy instead of being refused.
  - Real time: a touch heal is cast once in reach instead of chasing its recipient.
  - Real time: an advance whose path crosses a harmful area is offered priced through it, and only when the game's
    actual path fits that price.
  - A unit caught in several swarms leaves the ones it can.
- **Performance.** Buff appraisals skip work they do not need, and repeated diagnostic lines are bounded.

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.60.zip` through Unity Mod Manager. UMM displays
**0.3.60**. The mod ID and settings location remain `CompanionAI_Pathfinder`, and existing settings are kept. Startup
line:

```text
[Build] v0.3.60 module=a2be2e6f-a510-45cc-a706-7edef3b0e100
```

The ZIP contains only `CompanionAI_Pathfinder.dll` and `Info.json`. It is byte-identical to the coverage8fix1 test build.

## Known limitations

- **Played:** many development builds of this work were played. The last was coverage8 (8 fights, no AI Control
  error, healing at low HP working). This release adds the review corrections from that playtest. They passed the build
  (0 warnings), 1,129 unit tests and the offline blueprint audit, but have not been played.
- **Not yet seen in play:** Dispel Magic (Point) ending an area, Abandon Flesh's heal, a priced crossing through an
  area, and Circle of Death.
- **Death effects:** enemies that explode or cast when they die (Risen Followers) are not yet foreseen.
- **Summon control** stays experimental and off by default.
- **Unchanged from v0.3.52:**
  - companions can still block each other in tight crowds;
  - deaths from ability damage or level drain are valued approximately;
  - an occasional game memory clean-up can make one decision take about 1 s.
