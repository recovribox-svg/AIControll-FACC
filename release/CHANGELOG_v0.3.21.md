# AI Control - Fully Autonomous Combat Companions v0.3.21

## Highlights
- **Works alongside Wrath Tactics (real time).** When the Wrath Tactics mod is installed, its rules get the first say
  each time a companion it manages becomes free. If no rule fires, AI Control acts at once (the handoff takes a frame
  or two, at most about 0.6 s). Wrath Tactics' commands are never cancelled as "stuck". Without Wrath Tactics, or with
  the new setting off, nothing changes.
- **Power Attack, Deadly Aim, Piranha Strike and Combat Expertise** are switched on and off per attack. The base
  game's form of these feats was not recognised before, so they stayed as the player had left them.
- **Many more abilities are understood**, each read from the game's own components and checked offline against the
  game's data:
  - invisibility (concealment, +2 to hit, flat-footed targets, sneak attack), See Invisibility and True Seeing;
  - Protection from Energy pools, weapon buffs (Magic Fang, Lead Blades, energy damage on hit);
  - weapon enchantments (Magic Weapon, Keen Edge, oils, Holy Sword, domain powers, Bane);
  - melee attack actions (Devastating Blow, Piledriver with its trip or bull rush, Death Attack, Blade Whirlwind);
  - Command, poisons (Constitution damage as lost hit points);
  - ally hexes (Ward, Fortune, Protective Luck, Fury and others were never cast), Tactician teamwork feats;
  - mythic buffs (Aeon Edicts, Golden Dragon Might, Thousand Bites, Dragon Pride);
  - auras carried by buffs (Archon's Aura, Aura of Greater Courage, Frightful Aspect, domain and blessing powers,
    Judgment Aura and more), claws for empty hands, harmful performances such as Dirge of Doom;
  - stat changes when a buff starts (Ray of Enfeeblement), spell DC, caster level and spell penetration buffs, for
    casters only;
  - enemy regeneration: the damage that stops it, and why a downed regenerating enemy still matters;
  - on-hit effects (a debuff or a kill on a failed save, as with True Judgment), Quicken and Extend for the next spell,
    Shadow Evocation / Conjuration by the disbelief rule, Haunting Mists and other effects at the clicked spot.
- **Debuffs are worth what they take away.** Penalties such as Crushing Despair's are valued by how much they reduce
  the enemy, not a flat guess. Symbols aimed at the party by mistake are fixed.

## Fixes
- A weapon attack from where the companion stands is taken when nothing else is worth doing, instead of ending the
  turn beside the last enemy (not on a sleeping or held enemy, which it would wake).
- Buffs that pass on the caster's own powers (a Skald's Inspired Rage, a Judge's judgement aura, Tactician feats) count
  only the powers the caster really has.
- Regeneration types are matched as the game does (cold iron and silver both count). Downed enemies no longer shorten
  the estimated fight. First-hit-only damage was halved twice. Shield Wall stacks with a shield.

## New settings
- **Real time: let Wrath Tactics act first; AI Control fills the gaps** (on). Only matters when Wrath Tactics is
  installed; the settings page shows whether it is. A Wrath Tactics rule whose action is "Do nothing" gives no command,
  so AI Control cannot see it and acts anyway. Turn-based combat is not affected.

## Log and diagnostics
- `[Modes]` lines mark modes applied with each attack.
- The offline verification tool (`tools/ProfileAudit`) runs the mod's own readers on the game's blueprints; its
  results are in `docs/COVERAGE_AUDIT.md`.

## Compatibility
- Existing settings files keep working.
- The old real-time engine is still selectable in the settings, but is no longer maintained and will be removed.
- The design notes for this release are in `docs/TACTICAL_AI.md` §53-64.
