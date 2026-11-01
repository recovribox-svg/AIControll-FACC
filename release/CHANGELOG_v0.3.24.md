# AI Control - Fully Autonomous Combat Companions v0.3.24

Everything since v0.3.23, from the author's play-test logs and a player report about the Psionics mod.

## Highlights

- **Psionics mod: psionic focus is kept.** A Psychic Warrior no longer spends its psionic focus (which also ends its
  trance) on 1-round maneuvers: spending a lasting state that other things depend on now costs what getting it back
  costs. It is still used as a fight's last action. The rule is general and reads the game data, not names.
- **No step out of melee away from a better option.** A caster next to an enemy no longer takes a 5-foot step that
  rules out its best spell (one needing a short walk) or its own melee attack.
- **Enemies out of walking reach are shared.** When one companion finds that the ground does not lead to an enemy
  (an archer on a ledge), the others no longer walk at it in turn; they use ranged options (real time).
- **Sleeping enemies in Grease or Entangle.** The archers shoot when no melee ally can reach them without walking into
  the area (a Selective area is no obstacle).
- **Treat Affliction and other removal abilities** are used only on allies with something they can remove.
- **Summons** (from the v0.3.23 review, first seen in play here): no summon after summon while the caster's creatures
  already fight, and daily summons are kept for fights that need them.

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.24.zip` through Unity Mod Manager. UMM displays
**0.3.24**. The mod ID and settings location remain `CompanionAI_Pathfinder`, so existing settings are kept. Startup
line:

```text
[Build] v0.3.24 module=0ea24da4-70d0-4c77-9046-58f05061174b
```

## Known limitations

- The psionic-focus change has not been played with a psionic character (no such log yet). The AI does not regain
  focus or switch a trance on by itself.
- The two newest changes (the step out of melee, enemies out of walking reach) are checked against the logs and the
  game code but not yet seen in play.
- Melee companions still will not walk into their own side's non-Selective Entangle or Grease to reach enemies in it.
