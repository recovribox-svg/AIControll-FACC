# AI Control - Fully Autonomous Combat Companions v0.3.19

## Highlights
- **Buffs are valued by what they really do for the one who gets them:**
  - Attack, damage, AC, saves, temporary HP, concealment, mirror images, damage reduction, energy resistance, extra
    attacks, bigger size and more are read from the buff itself, with the caster's real level and the real duration.
  - A bonus that does not stack with what the ally already has counts only for what it adds.
  - Penalties count too: Enlarge Person on an archer is weighed with its −2 Dexterity and its bigger weapon dice.
  - A spell that only buffs must be clearly better (15 %) than the best attack or spell available right now.
  - Guidance and other "next roll" buffs count as one roll, not a whole fight.
- **Class abilities:**
  - Performances, spirits and auras that give their bonus through their area (Battle Spirit, Inspired Rage, Aeon gazes,
    Angel Halo) are now understood and valued for each ally they reach, with the ability's own conditions.
  - An ally who accepts a rage aura loses its spells, and that is counted.
  - Rage and Bloodrage bonuses, Bit of Luck / Fortune style rerolls and temporary HP per level are read.
  - Warpriests use their Fervor quickened self-buffs.
  - Witches use Cackle and Chant to keep their hexes going.
- **Kineticists:**
  - Blasts are priced with Elemental Overflow, and Kinetic Sharpshooter blasts with the bow's own damage.
  - Burn is handled as the nonlethal damage it is, and priced.
  - When burn blocks every blast, the Kinetic Sharpshooter creates its quiver, or an infusion that blocks the blasts is
    switched off for the fight.
- **Fallen allies get revived:** a revive is worth what the ally does for the rest of the fight. Your own Grease or
  other areas lying on a downed ally are taken into account.
- **Next-spell boosts** (Arcane Reservoir caster level / DC) are used only right before a spell they actually improve,
  and only the bonus the caster really gets is counted.
- **Ring of the Mad Duelist / Foe Focus marks** go on the enemy that actually threatens the wearer.
- **Buff It 2 The Limit compatibility:** you choose who manages toggles and after-fight buffs (see New settings).

## Fixes
- **Areas:**
  - A buff that makes a character bigger (Enlarge Person) is weighed against the harmful area the bigger body would
    lie in.
  - An area that knocks a unit down at the start of its turn costs that turn in turn-based mode.
  - Grease's save is learned from what the game rolls, so a good Reflex save is taken into account.
- **Real time:**
  - After a walk to see a target gets stuck (no line of sight), that target is left alone for a while instead of being
    walked at again and again.
  - Commands the game puts in front of the AI's own (Gather Power, Kinetic Blade) no longer confuse it.
  - The out-of-combat self-buff check ran every frame. It now runs at the normal decision pace, and one buff that cannot
    be cast no longer blocks the others.
- **Turn-based:**
  - A unit at its last enemies, all in your own Grease, steps in to finish them when nobody is left to fight back.
  - Sleep on the last active enemy counts as one round only (your attacks wake it).
- **Items:**
  - Healing potions follow your potion threshold and the real chance of being knocked down.
  - Other used-up items need a real benefit.
- **Buffs:**
  - Buffs with an unknown effect that spend a limited use or burn are no longer cast as filler (Searing Flesh).
  - Fighting Defensively is used as a combat mode.
  - Toggles the AI switched during a fight go back to how they were afterwards.
- **"Buff party" button:**
  - It skips buffs that last a round or end with the next attack.
  - It stops when a fight starts.

## New settings
- **AI manages activatable abilities (toggles)** (on). This one switch replaces the two 0.3.18 toggle settings; your
  old choice is kept. Switch it off if another mod (Buff It 2 The Limit) or you manage toggles.
- **After fights, AI companions recast their long-lasting self buffs** (on). Switch it off if you buff by hand or with
  a buff mod.
- **Leave a toggle alone for the rest of the fight once you switch it yourself** (on).
- The settings page tells you when Buff It 2 The Limit is active and which setting to switch off. Your settings are
  never changed for you.

## Log and diagnostics
- `[Buff]` lines say why a buff was or was not cast: its value, what it adds for the recipient, and the best other
  option.
- `[Save]` is now one summary line per ability at the end of each fight, instead of a line for every roll.
- Less log bloat: per-turn start lines, repeated trap messages, zero-value boosts, per-attack toggle lines and other
  debug-only lines were removed or moved to the debug log.

## Compatibility
- Existing settings files keep working. The old toggle settings become the new single switch.
- The old real-time engine is still selectable in the settings.
- Skald, Bard, Cleric, Warpriest, Bloodrager and Witch abilities were checked against the game's blueprints, not yet
  in play.
