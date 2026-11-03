# AI Control - Fully Autonomous Combat Companions v0.3.27

Everything since v0.3.26: smoother turn-based pacing, and companions that move more sensibly in a fight.

## Highlights

- **Turn-based: no more sped-up projectiles.** A companion's turn now ends the way the game ends its own units'
  turns: after its projectiles have landed and a short pause. Spells and arrows no longer fly at triple speed or
  vanish as the next turn begins.
- **Front-liners protect, squishy companions stay safe.**
  - Walking into danger now has a price: how much likelier the companion is to go down where it ends up.
  - That price is reduced by how much safer it makes the rest of the party, so a front-liner stepping in front of a
    heavy hitter that is attacking a squishy companion pays little or nothing.
  - A caster or support companion walking up to a heavy hitter alone pays a lot, and stays back.
  - Enemies are assumed to go for the easier-to-hit companions (lower AC) more often.
- **Real time: walks rethought when a new enemy gets in the way.** If an enemy that was not counted when a companion
  set off reaches it on the way, or joins the fight close to its path (reinforcements), the companion decides again,
  so it no longer walks past a newcomer and takes a free attack. It may still keep its target if that is clearly worth more.
- **Real time: no Grease on an ally's path.** A caster holds back Grease, Entangle and similar areas when a companion
  is walking into the circle, not only when it is standing there.
- **Archers behind cover:** in turn-based too, an archer in range but without a clear shot walks to a spot with one,
  instead of "advancing" nowhere. An archer already in range no longer steps closer after shooting.
- **Real time: blocked walks end sooner.** A companion that has reached its target but is boxed in by bodies acts after
  about 2 s instead of 6 s.
- **Smaller fixes:**
  - no summons or buffs while every enemy left is asleep or held;
  - summons close to vanishing are no longer healed as if they would fight the whole battle.

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.27.zip` through Unity Mod Manager. UMM displays
**0.3.27**. The mod ID and settings location remain `CompanionAI_Pathfinder`; existing settings are kept. Startup line:

```text
[Build] v0.3.27 module=ae5d4340-5d51-454f-bec5-2888b74cbed6
```

## Known limitations

- The quicker end of blocked walks (the last change) has not been played yet; everything else was seen working in two
  mixed turn-based/real-time play-tests.
- Companions can still block each other's path in crowds (body-blocking); support for the Ardub mod is on hold.
- Melee companions are reluctant to walk into the party's own non-Selective Grease or Entangle. This is intended.
- A large caster still takes about 0.3 s per decision, and an occasional game memory clean-up can make one decision
  take up to about 0.8 s.
