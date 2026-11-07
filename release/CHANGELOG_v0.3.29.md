# AI Control - Fully Autonomous Combat Companions v0.3.29

Everything since v0.3.28. Companions get stuck in crowds far less often, real-time walks end when they should, cones no
longer catch companions walking into them, and turn-based turns are no longer lost to walks that cannot happen.

## Highlights

- **Crowds and body blocking (real time).**
  - A companion walking up to an enemy now takes a free side when another companion stands on its spot, or is walking
    there. With no free side left, it does something else instead of pushing into the crowd.
  - Spots near enemies were often wrongly read as unreachable when a companion stood slightly off the game's walking
    mesh. That made "no free side" far too common and also blocked walking round harmful areas. Fixed.
  - A companion boxed in where it stands (two walks in a row from the same spot got nowhere) stops trying to walk for
    6 s and acts from where it stands. Getting out of a harmful area or a swarm is still allowed.
  - A walk pushed on the spot for 2 s ends as blocked. A stop-start walk at a dead end now ends too: each stop used to
    restart that 2-second clock.
- **Walks that end on time (real time).**
  - A walk to touch range ends as soon as the touch reaches. A walk to a free side ends once the weapon reaches.
  - A walk the game already counts as arrived ends after 0.4 s, so companions no longer circle next to enemies.
  - A walking companion decides again when an enemy it can hit back engages it near where it is going. It may fight
    that enemy instead of taking an attack of opportunity to walk on.
- **Cones and bursts spare companions walking into them.** A companion whose walk takes it into a cone or burst while
  it is being cast now counts as caught. Fan of Flames had hit Seelah and Camellia as they walked in.
- **Turn-based.**
  - A turn is no longer left unused when every walk-then-attack is just over one move away: an advance is offered.
  - A walk that goes nowhere (other companions hold the squares ahead) is refused at once, and the next best action is
    taken. Seelah had lost three turns this way, one of them a heal for Camellia.
- **Danger read better.**
  - A companion's chance of going down now also counts the melee enemies' real hits, not only an average.
  - An enemy whose several hits can each paralyse (Ghoul Creeper) counts the paralysis once a round, not once per hit.
    It was rated about twice as dangerous as it is.

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.29.zip` through Unity Mod Manager. UMM displays
**0.3.29**. The mod ID and settings location remain `CompanionAI_Pathfinder`; existing settings are kept. Startup line:

```text
[Build] v0.3.29 module=b1371c3f-bb68-4d1c-b881-9753eb663685
```

## Known limitations

- **Played:** v0.3.29 test builds 2, 4, 6, 7 and 8 were played in more than 40 fights with no mod errors. This release
  is test 8 with the door-opening work taken out. That removal itself was not played; it puts those parts back exactly
  as they were in test 6, and the build, 479 tests and the offline blueprint audit pass.
- **Doors:** companions do not open doors. Test builds 7 and 8 tried. Their logs showed that the door in the prologue
  room is one the game itself does not let anyone open during combat. The work is kept for a later version.
- **Unchanged from v0.3.28:**
  - companions can still block each other in tight crowds (support for the Ardub mod is on hold);
  - melee companions are reluctant to walk into the party's own non-Selective Grease or Entangle, which is intended;
  - an occasional game memory clean-up can make one decision take up to about 1 s.
