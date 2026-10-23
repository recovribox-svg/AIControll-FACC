# AI Control - Fully Autonomous Combat Companions v0.3.20

## Highlights
- **Attacks of opportunity are weighed, and avoided when a better spot exists:**
  - Before walking up to an enemy, the AI asks the game which enemies would attack (those beside the companion and
    those it walks past) and prices each attack by its hit chance, damage, critical range and trip chance.
  - When the straight way provokes, it looks at the spots around the target and takes one that provokes less, for
    example one still beside the enemy it would otherwise leave.
  - A walk that provokes also counts how dangerous the spot where it ends is, at any health.
  - The path the game really walks is counted, not a straight line. A path that passes more enemies is priced again
    before the companion commits to it.
  - Provoking is still allowed when the action is worth it.
- **Enemies are rated by what each one can do:** its weapons by the rules, the harmful spells it can still cast, its
  heals, and the damage it has dealt in this fight. An enemy about to drop an ally comes first. `[Threat]` log lines
  say which enemy counts most and why.
- **Weapon damage by the game's own rule:** swarms' resistance to weapons, immunity to critical hits, damage
  reduction. Sneak attack only counts against a target open to it.

## Fixes
- **Turn-based:**
  - A walk-then-act command must fit one move action. Longer walks used to cost the attack or spell and the turn.
  - After a walk is refused as too long, the companion advances towards the target for the next turn instead of
    standing still.
  - A 5-foot step is offered only where a unit or a wall does not force the game's path around (it was refused three
    times in a row, and the companion then shot from melee).
- **Spells and abilities:**
  - A spell is counted as cast only once the game really cast it. An interrupted cast no longer blocks that ability
    on that target for the rest of the fight.
  - Bane no longer counts as taking an enemy out of the fight.
  - Remove Fear's bonus against fear counts only as far as the enemies can use fear.
  - A hit while casting spoils the spell by the game's concentration check (none for wands or held touch charges).
  - An attack of opportunity that hits no longer counts as stopping a walk. Only a fall or a trip does.
- **Kineticists and archers:**
  - Blast damage is learned from hits the target did not resist (DarkCodex Elemental Scion included).
  - Physical blasts and arrows against swarms are priced by the swarm rules.
- **Revives:** a heal that brings a downed ally only part of the way up is priced like a revive.
- **Items:** the "worth spending an item" bar is checked after the walk and the risks are taken off.
- **Real time:**
  - A companion that waits for its action to come back no longer re-thinks everything twice a second. It wakes as soon
    as something changes (hit, an enemy walks up or falls, a harmful area, an ally down).
  - A companion in range of its target but without line of sight walks to a spot from which it can see it, instead of
    "arriving" where it already stands.

## New settings
- **Steer my own walk orders around spotted traps** (on, as before). When you order a party member to walk in real
  time or out of combat, the walk goes around a trap your party has spotted. Switch it off to walk exactly where you
  click. AI moves and turn-based moves are not affected.

## Log and diagnostics
- `[Threat]` lines: the most dangerous enemies at the start, when the order changes, and who dealt the most per fight.
- `[DmgCheck]`: at the end of a fight, where the estimated damage of an ability was clearly off.
- `[Perf] slow decision` says whether the game's memory clean-up ran during it. The real-time `[Perf]` line counts the
  decisions skipped while waiting.
- Option labels such as `(from a spot that provokes 0, not 1)` and `[its path: AoO 2]` show the new attack of
  opportunity handling.
- Less verbose debug logging.

## Compatibility
- Existing settings files keep working.
- The old real-time engine is still selectable in the settings.
- The design notes for this release are in `docs/TACTICAL_AI.md` §46-52, and the live-code audit in
  `docs/LIVE_CODE_AUDIT.md`.
