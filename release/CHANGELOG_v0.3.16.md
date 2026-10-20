# AI Control - Fully Autonomous Combat Companions v0.3.16

## Highlights
- **Casters stop walking into a crowd to cast.** Reading a scroll or casting among enemies is weighed by the game's own
  rule: the defensive-casting check and an attack from every enemy around if it fails.
- **Touch spells (Inflict/Cure Wounds and similar) work reliably in turn-based mode**, fixing the 0.3.15 issue where
  Camellia could lose whole turns.
- **Movement checked against the game's real pathfinding:** characters no longer end a walk on an ally's square, and
  real-time escapes only go to walkable spots.

## Fixes
- **Turn-based touch spells:**
  - The walk into reach now ends where the game agrees the touch can be made (it follows stairs and goes around
    obstacles).
  - No more repeated 0-metre "approach" orders. In 0.3.15 they could repeat until the turn ended: Camellia lost three
    turns in one fight.
  - The spell is cast after the walk, instead of being dropped for the turn.
  - A target out of reach only because it stands on a ledge (well above or below) is no longer chosen.
- **No movement options after a 5-foot step** (the game forbids them; Orvath had Move+Attack and Fire Blast refused).
- **Paths no longer end on an ally's square** (turn-based). The check existed but never worked.
- **Real-time area escapes** only go to walkable spots. The walkability check also never worked before.

## Smarter decisions
- **Casting in melee:** reading a scroll or casting a spell while enemies are adjacent is valued by the chance the
  defensive-casting check fails, and then by how likely every adjacent enemy's attack is to drop the caster. The old flat
  15 % penalty ignored how many enemies there were. Potions never provoke and are not penalised.
- **Walking up to cast** also counts the danger of standing among those enemies afterwards, at any HP.

## Log and diagnostics
- `[casting provokes N]` on options that would provoke, with the chance of being dropped.
- When a character with its actions unused finds nothing worth doing, the log names its best options and why each was
  not taken.

## Compatibility
- Existing settings files keep working; no new settings.
