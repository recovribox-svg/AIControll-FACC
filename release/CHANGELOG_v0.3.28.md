# AI Control - Fully Autonomous Combat Companions v0.3.28

Everything since v0.3.27: smarter positioning, a much better kineticist and archer, cones that spare the party, and
enemies that drain, poison and swarm taken as seriously as they deserve.

## Highlights

- **Moving with the whole party in mind.**
  - Every move now counts how much more danger it leaves the rest of the party in. A front-liner no longer walks away
    from the enemy it was holding off a caster.
  - Casters and archers may step back from a melee a front-liner is holding.
  - An advance counts the attack it gives up where the companion stands.
  - A melee enemy is dangerous where it can reach this round: its full attack only in reach or a 5-foot step away. A
    companion whose own weapon cannot hit back steps out of that reach at any HP.
- **Defences and survival read properly.** Hit chances count concealment and Mirror Image. Chances of going down count
  temporary hit points. Lying prone is priced as the +4 enemies get to hit.
- **Kineticists and archers.**
  - The Kinetic Sharpshooter's quiver is made before combat and refilled in combat when worth it. Arrows are kept for
    planned shots, and arrows and burn are priced the same way everywhere.
  - Overflow and critical hits count in blast damage. Deadly Aim is used on archer blasts.
  - Enveloping Winds and Shroud of Water are raised sensibly.
  - The Dark Elementalist uses Soul Power by the game's rules and harvests souls after a fight, once you have stopped
    giving orders for a moment.
- **Cones that spare the party.** Cone spells (Fan of Flames, burning hands and the like) are aimed past companions
  standing next to the enemy. When that is not possible, the friendly fire counts against them. A cone that needs a walk
  walks to its casting spot first.
- **Drain, poison and swarms taken seriously.**
  - Ability damage and negative levels are valued by the game's rules: a score taken to 0, or as many negative levels
    as the character level, kills. Enervation, Polar Ray and Wracking Ray are worth what they really do, immunities are
    respected, and Shadows and Nabasus rate as the threat they are.
  - What an enemy's hits carry is read from the game: giant spider poison, a Shadow's Strength touch, poisoned weapons,
    a ghoul's paralysis, disease. Your own poisoned weapons are valued the same way.
  - Swarms (Mandragora, rat, locust, spider...): their round is valued with its nausea and poison. Standing inside a
    swarm is priced as danger, approaches look for a spot clear of it, and a companion caught in one may step out.
- **Smaller fixes and performance.**
  - A walk whose aim point was lost ends at once.
  - Shared damage estimates make large casters' decisions lighter.
  - The log is shorter (repeated coverage and energy rows trimmed, one turn-start line).
  - Pull requests #18 and #19: quiver preparation while exploring, ricochet damage, "Hide blocked" in the
    ability catalogue.

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.28.zip` through Unity Mod Manager. UMM displays
**0.3.28**. The mod ID and settings location remain `CompanionAI_Pathfinder`; existing settings are kept. Startup line:

```text
[Build] v0.3.28 module=794e6682-f29b-426a-a940-680ed813c83c
```

## Known limitations

- **Played:** this exact build was played in 14 fights (8 turn-based, 6 real-time) with no mod errors. Earlier
  v0.3.28 test builds were played in about 70 more.
- **Swarms not yet seen in a fight:** the swarm changes have only been checked offline against the game's data.
- **Real time, melee targeting:** a melee companion walking to a distant enemy keeps walking when nearer enemies engage
  it on the way. An improvement is proposed and waits for the author's decision.
- **Two small issues, already fixed for the next version:**
  - a turn-based turn could end unused when every walk-then-attack was just over one move away;
  - an enemy whose several hits can each paralyse (Ghoul Creeper) was rated about twice as dangerous as it is.
- **Real time, blocked walks:** a walk blocked by bodies is given up only after about 6 s.
- **Unchanged from v0.3.27:**
  - companions can still block each other in crowds (support for the Ardub mod is on hold);
  - melee companions are reluctant to walk into the party's own non-Selective Grease or Entangle, which is intended;
  - an occasional game memory clean-up can make one decision take up to about 1 s.
