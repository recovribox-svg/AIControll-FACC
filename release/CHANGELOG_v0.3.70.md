# AI Control - Fully Autonomous Combat Companions v0.3.70

Everything since v0.3.60:
- Companions use potions and scrolls by what they do, and hand an adjacent ally a potion.
- Summons are driven by default and use their own abilities.
- Teleports and leaps are used to reach enemies, escape, and find a casting spot.
- Mixed-range characters switch between their melee and ranged weapon sets.
- Ranged characters reposition with movement they have left.
- Roles and ranges are suggested from each character's build.
- Large fights decide faster.

Both turn-based and real-time combat, unless noted.

## Highlights

- **Potions and scrolls.**
  - Potions are valued by their effect, not only their healing. A buff potion is held back in an easy fight, like a
    buff spell.
  - A spell the character also has prepared or known is cast from its slot before an item copy of it is spent.
  - A companion can hand an adjacent ally one of its quick-slot potions (the game's own action).
  - Items now account for sight, movement immunity, spell resistance and typed protections.
- **Summons.**
  - Control Summoned Creatures is now **on by default**. An older settings file is switched on once; if you switch it
    off later, that choice is kept.
  - Summons use their own abilities by what they do: Screech, Drone, Touch of Chaos, Song of Discord, studied targets,
    smites, Contagion.
  - A summon decides whether a costly Inspired Rage is worth it to it, and declines it when its own spells are worth
    more. Companions do the same.
  - Each summon spell is priced by what it brings:
    - the number of creatures, how long they stay, and their strength against these enemies;
    - their measured damage.
    So variants of one spell differ.
- **Teleports and leaps.**
  - Dimension Door, Shadow Walk and similar teleports and leaps are offered to reach an enemy. The group teleports of
    Vrocks and Omoxes carry the party's summons too.
  - A risky escape walk (out of an area, out of reach, out of a swarm) can be a teleport instead, also to safe spots
    beyond walking reach.
  - Touch, cone and line spells can teleport to their casting spot.
  - Item teleports (Evading Steps) follow the item rules.
- **Weapon sets (Mixed range).** A character switches to its ranged set when nothing is in reach, and to its melee set
  when an enemy stands next to it. It uses the game's own move-action command. Every ranged weapon type counts: bows,
  crossbows, darts, javelins, throwing axes and sling staves.
- **Positioning (turn-based).** A ranged character with only its move action left takes a safer spot that keeps an enemy
  in range and sight.
- **Spell bans.** A spell ban (Inspired Rage under Tome of the Firebird / Mystical Mayhem) costs each recipient what its
  spells are worth.
- **Better judgement.**
  - A companion half or more drained of its levels counts as badly hurt: it can withdraw, and walks into danger cost it
    more. The game kills at as many negative levels as character levels.
  - A Magus spell the game delivers through a weapon is never aimed at a dominated ally.
  - Real time:
    - after an HP loss, a self-heal or walk away already under way is left running;
    - a provoking action waiting to start is decided again when a newcomer threatens the unit;
    - one ally walks to a helpless enemy for a Coup de Grace, not three.
- **Performance.** Decisions in large fights are faster: walk prices and spell estimates are read once per decision.
  The previous build had four decisions over 1 s in fights with over 40 enemies. In the last playtest (9–11 enemies)
  the slowest took 457 ms.
- **Settings panel.**
  - The panel follows Unity Mod Manager's UI scale.
  - **Suggest from build** sets a character's role and range from its weapons, feats and spellcasting. Characters seen
    for the first time get this automatically, and saved choices are never changed unless you press the button.

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.70.zip` through Unity Mod Manager. UMM displays
**0.3.70**. The mod ID and settings location remain `CompanionAI_Pathfinder`, and existing settings are kept, with the one
exception above: Control Summoned Creatures is switched on once. Startup line:

```text
[Build] v0.3.70 module=670ef6c7-58c0-4bc5-ba40-add2fcb55ba7
```

The ZIP contains only `CompanionAI_Pathfinder.dll` and `Info.json`.

## Known limitations

- **Played:** most of this work was played in five v0.3.70 development builds. The last was features6: 14 fights,
  no AI Control error. Its two corrections (the weapon-set comparison and the drain rule) passed the build, 1,193 unit
  tests and the offline blueprint audit, but have not been played.
- **Death effects:** enemies that explode or cast when they die (Risen Followers) are not yet foreseen.
- **Summons:** Unholy Nimbus is not used: its expiry blast cannot yet be forecast safely. Shadow-spell variants are
  not yet seen in play.
- **Unchanged from v0.3.60:**
  - companions can still block each other in tight crowds;
  - deaths from ability damage or level drain are valued approximately;
  - an occasional game memory clean-up can make one decision take about 1 s.
