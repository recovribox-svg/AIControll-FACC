# AI Control - Fully Autonomous Combat Companions v0.3.15

## Highlights
- **Spells and items that did nothing now work, or are noticed.** Orvath's Fire Blast, touch scrolls and touch spells
  on others in turn-based mode, and stuck potion charges were all fixed or detected.
- **Melee characters stop charging into Grease** to reach enemies lying in it.
- **Smarter crowd control:** each enemy's real duration and real saving throws are used.
- **Tanks hold the line longer.**

## Fixes
- **Fire Blast that did nothing (Orvath / kineticists):** the AI was casting the Kinetic Blade's internal "Fire Blast",
  the damage the blade deals when its weapon hits. Cast on its own it does nothing. The AI now only uses abilities the
  game offers you in the action bar, and never the kinetic blade's internal ones. The log lists what was left out.
- **Touch scrolls and touch spells on others (turn-based):** Inflict/Cure Wounds and similar, from a scroll, wand or
  spell slot, aimed at someone out of reach were cast where the caster stood and then never touched, because the game's
  own walk to the target is cancelled in turn-based mode. The character now walks into reach first, then casts.
- **Held touch charges:**
  - A charge the character already holds is used before another scroll, potion or slot of the same spell.
  - Other spells that would throw a held charge away are valued that much lower.
  - A charge the game cannot deliver to its holder (a potion that healed nothing and left its charge behind) is
    removed, the same way the game's own cleanup removes charges. The next potion then works.
- **Safety net for anything else:** when the game does nothing with a damage, heal, debuff or control ability (no
  attack roll, damage, heal, save or effect), the log says so (`[Cast] ... did nothing`). After two such failures on the
  same target in a fight, the AI stops choosing it there.

## Smarter decisions
- **Harmful areas (Grease, Entangle, clouds) cost what staying in them costs:** every round the character would stay
  inside, doubled when it stops inside to fight (standing up provokes, and prone characters are hit at +4). Passing
  through costs one round. Before, a melee character's charge into Grease was priced as one round's risk.
- **Crowd control by each enemy's real duration:** Color Spray on a 5+ HD enemy is valued as the 1-round stun it is,
  not 3 rounds.
- **Real saving throws:** the AI learns the save bonuses the game actually rolls (bonuses against particular effects,
  automatic passes) and uses them for later estimates.
- **Tank role:** pays 60 % of the damage and exposure a move risks (being dropped outright is still priced in full) and
  only turns careful below a third of its HP instead of half.

## Log and diagnostics
- `[Cast]` lines for abilities the game did nothing with, with the game's own reason and the state of a held touch
  charge. Charges carried into a fight are listed at its start.
- `[Save]` lines for save bonuses beyond the listed stat; `[Abilities]` lines for abilities left out.

## Compatibility
- Existing settings files keep working. The unused setting "use buffs before attack" was removed.
