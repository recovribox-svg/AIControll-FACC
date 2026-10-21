# AI Control - Fully Autonomous Combat Companions v0.3.18

Also contains everything from 0.3.17, which was not released on its own.

## Highlights
- **Many more active abilities are used, and used well:**
  - Performances, Rage and spirits are started when they pay off and stopped when nobody is left to benefit.
  - Power Attack, Deadly Aim, Fighting Defensively and Combat Expertise are chosen per attack, Mobility per move.
  - Demoralize and Dazzling Display follow the Intimidate rule.
  - Combat maneuvers are judged by CMB against CMD.
  - Vital Strike, Cleave, Pounce, metamagic (Empower, Maximize, Extend, Persistent) and chain spells are used.
- **Walking around harmful areas:** melee characters approach around Grease, Entangle and clouds instead of through
  them. In turn-based mode a step past your own Grease is weighed as the chance of a fall, so nobody idles beside it.
- **Real-time combat** checks the walk the game will actually take against harmful areas and against an ally's area
  spell that is about to land.
- **Calibrated to the fight:**
  - Expected fight length and enemy danger come from the damage actually dealt in the fight.
  - Kills get a bonus, and healing follows one continuous curve.

## Fixes
- **Spells that did nothing:**
  - An ability that reaches nothing on its target is dropped for that target after two tries.
  - Sleep is no longer cast again and again at an enemy its Hit Dice never reach.
  - An area aimed at an immune creature is no longer chosen for it.
- **Real time:**
  - Touch spells walk into reach before casting. The game used to chase a moving target with the held charge for over
    a minute.
  - Units walking without getting any nearer stop and decide again.
  - No more duplicate Demoralize or Coup de Grace on one enemy.
  - Interrupted commands no longer count as failures.
- **Turn-based:**
  - A 5-foot step must really be one. A longer path had become a normal move that provoked.
  - A prone unit stands up after one turn at most, also when it lies in or beside an area.
  - Refused 5-foot steps try another direction.
- **Consumables:**
  - None are used on your own summons.
  - An item is only used when it is worth a real action (no healing potions for a sliver of HP).
- **Buffs:**
  - A stat penalty is no longer counted as a benefit (Enlarge Person's −2 Dexterity).
  - Strength-only buffs go to the characters who fight with Strength.
- **Soul Power** is not recast while nothing has changed.

## Smarter decisions
- **Spell resistance, spell immunity and buff immunity** are checked in the game's own order. Spell attack rolls use
  the game's attack rule.
- **Damage, heals and kills** of custom abilities come from the game's own preview.
- **Ranged attacks provoke:** shooting or casting a ray next to an enemy is priced, and archers step out first when
  that is worth it.
- **Summons:**
  - Point-target summons are placed near the fight.
  - A summon that brings an area which also hits the party (a Dretch's Stinking Cloud) is preferred less while you have
    melee fighters.
- **Area effects that outlast the area** (Stinking Cloud's nausea) are priced for how long they last.
- **Sleeping enemies** are not woken by pointless hits while others are still fighting.
- **Area spells** apply "main target only" effects (a bomb's direct hit) to the target they are aimed at.

## New settings
- **AI may start performances, rage and similar abilities** (on by default).
- **AI may switch combat modes (Power Attack, Fighting Defensively, Mobility)** (on by default).

## Log and diagnostics
- At the end of each fight:
  - `[Perf]`: decision timing.
  - `[Calib]`: expected against actual fight length and damage.
  - `[Coverage]`: which action-bar abilities the AI understands.
- `[Save]` for every saving throw against a party member's ability.
- `[Cast] ... did nothing to its target`, with the target's level and Hit Dice limit.

## Compatibility
- Existing settings files keep working; the two new settings default to on.
- The old real-time engine is still selectable in the settings.
