# AI Control - Fully Autonomous Combat Companions v0.3.26

Everything since v0.3.25: a list of abilities the AI must not use, and a smaller log.

## Highlights

- **"Abilities AI must not use."** In the settings (Ctrl+F10), each character's panel has a searchable list of its
  spells, abilities, quick-slot items and toggles.
  - **Blocking:** *Block* one for that character or for everyone. Variant spells (Command, kineticist blast forms...)
    can be blocked one variant at a time or all at once. A top-level panel lists the "everyone" entries.
  - **What it covers:** AI Control never starts a blocked entry, in turn-based or real-time combat, in its after-fight
    buffing or with Pre-Buff. It also stops counting on it when it plans: it won't expect an ally to finish a sleeper
    with a blocked Coup de Grace, or to heal with a blocked spell.
  - **Unblocking** takes effect from the next decision.
  - **What it does not touch:**
    - you can still use the ability by hand;
    - other mods (Wrath Tactics) and automatic effects (Dreadful Carnage's free Dazzling Display) are not affected;
    - buffs and toggles that are already on stay on.
  - **Matching:** one entry covers the spell from any spellbook, with metamagic, and from scrolls, wands and potions. A
    spontaneous Cure cast from a Bless slot is not affected by a Bless block.
- **Smaller log.** The normal log is 15-28 % smaller on recent play-test logs; decisions are unchanged. Repeated
  diagnostic lines were trimmed:
  - the ability/toggle report, energy-damage estimates and the Progressive spell-ladder line are no longer repeated;
  - real-time walk inspection and melee-standoff rows now go to the debug log.

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.26.zip` through Unity Mod Manager. UMM displays
**0.3.26**. The mod ID and settings location remain `CompanionAI_Pathfinder`, so existing settings are kept; older
settings files start with empty lists. The lists are saved with the mod's settings, so the shared list applies in every
campaign. Startup line:

```text
[Build] v0.3.26 module=24ddcf26-2006-43f5-ade7-b326abb2d62c
```

## Known limitations

- Played in turn-based mode (6 fights): blocking, unblocking and the shared list worked. Not yet seen in play: real time,
  blocked toggles and attack modes, kineticist infusions, held touch charges, Psychic Warrior trances.
- Toggles the AI never starts by itself can still be listed; blocking them changes nothing.
- A large caster still takes about 0.3 s per decision (Orvath, 77 abilities).
- An archer that cannot see an enemy it is in range of may try an "advance" that goes nowhere (no action is lost). Fixing
  it changes movement and is left for discussion.
