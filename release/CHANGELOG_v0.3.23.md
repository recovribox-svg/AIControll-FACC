# AI Control - Fully Autonomous Combat Companions v0.3.23

Everything since v0.3.22, from the author's play-test logs and player reports.

## Highlights

- **Tabletop Tweaks damage reduction is read.** With Tabletop Tweaks installed, energy resistance and damage
  reduction come from its own rules (the game's part is replaced there); blasts and spells no longer count damage a
  resistance would stop.
- **Sleeping enemies are left asleep.** Archers no longer shoot a sleeping enemy awake while others still fight. The
  last sleeper is left for a melee ally who can reach it for a Coup de Grace; with no such ally near, the archers shoot.
  Real time now walks to a Coup de Grace.
- **Revives that last.** A downed companion is brought back only when the heal outlasts the attack of opportunity for
  standing up and the next blows, taken one after another.
- **Real-time front-liners stay on the enemy front line** instead of running past it to the archers and casters: a
  walk is priced by the time it takes and by the enemies who can step into the way.
- **Smarter 5-foot steps.** A caster steps out of melee only for a spell it then casts, never away from a revive or
  another option it would walk to, and a cone caster (Color Spray) keeps its targets when it steps.
- **Summons.** No summon after summon while the caster's creatures already fight, and daily summons, spell slots and
  bombs are kept for fights that need them.
- **Alchemist.** Grenadier Alchemical Weapon counts the flask it uses up, follows "use thrown items", and is used only
  right before a weapon attack. Force Bomb's knockdown is understood.
- **Dreadful Carnage.** An attack likely to kill counts the free Dazzling Display the kill fires, so a manual Display no
  longer wins over a likely kill.
- **Fewer false failures.** An interrupted walk, a cast that never started, a failed concentration check or a 20 %
  arcane spell failure no longer teaches the AI that an ability "does nothing" on that target. Arcane spell failure is
  now priced into a spell's worth.
- **Other fixes.** Holy Water is not thrown at enemies it cannot hurt; a missed splash weapon is not judged as a
  direct hit; a tie between acting in place and walking into an area goes to acting in place; no orders during the
  get-up animation in real time.

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.23.zip` through Unity Mod Manager. UMM displays
**0.3.23**. The mod ID and settings location remain `CompanionAI_Pathfinder`, so existing settings are kept. The
startup log identifies this package:

```text
[Build] v0.3.23 module=09a7ab12-2cc2-477c-a688-74765960ea97
```

## Known limitations

- The changes from the last log review (5-foot step, sleepers far from a Coup de Grace ally, summons) and the Alchemist
  and Dreadful Carnage changes have been checked against the game's code and rules but not yet seen in play.
- Kinetic Sharpshooter: no change (in the game the arrows carry no blast damage); a log line explains the choice.
- Fast Bombs: a bomb order is valued as one throw.
- Melee allies still will not walk into their own side's Entangle or Grease to reach enemies standing in it.

Details: the reviews in `docs/` (`RELEASE_0322_*`, `RELEASE_0323_BUILD*_REVIEW.md`, `TASK_BOMBS_CARNAGE_QUIVER.md`).
