# AI Control - Fully Autonomous Combat Companions v0.3.14

## Highlights
- **Real-time combat now uses the same tactical AI as turn-based.** Ranged characters stay at range, weapon attacks
  are used again, and options are weighed with the game's own rules. The old real-time AI can still be chosen in the
  settings ("Real time: use the old decision engine").
- **Companions react to their own conditions.** They stand up when knocked prone, walk out of Stinking Cloud when
  nauseated, and no longer try actions that being staggered, held or webbed rules out.
- **Smarter about danger.** Attacks of opportunity are judged along the whole path, including the chance of being
  dropped in one blow, and badly hurt characters are more careful.
- **Better spell judgement.** Spell resistance, energy resistance and immunities are weighed with the game's rules, and
  the AI learns which enemies shrug off which damage.

## Turn-based
- **Prone companions stand up.** Before, a tripped character, one on Grease, or one healed back from unconscious lost
  every turn until the fight ended. They now stay down only when standing up next to enemies would clearly be worse.
- **Nauseated companions walk out of the cloud** instead of losing their turns inside it.
- **Staggered, held or webbed companions** are no longer offered actions the game would refuse (for example moving and
  then attacking while staggered).
- **5-foot steps out of melee** only when the action that follows is worth it.
- **A unit no longer ends its turn with nothing to do** because the game listed no attack against one enemy.

## Real time
- **Same decision engine as turn-based**, with the game's real-time cooldowns as the actions left.
- **Combat memory survives mode switches.** It used to reset on every switch (dialogue, menus, cutscenes); 62 resets
  were seen in one session, some mid-fight.
- **Commands that are legitimately waiting are no longer cancelled as "stuck".** HP is read correctly, melee reach is
  respected, and area spells cast at a point (Entangle, Grease) are aimed correctly.
- **A melee character next to an enemy waits for its next swing** instead of walking off.
- **Nauseated companions walk out of clouds**; orders that can't start while nauseated are dropped.
- **Escaping harmful areas works reliably.** Only areas that still exist count, a real way out is found, and the escape
  is no longer re-issued every half second.
- **Backing away from an enemy fighting only you now counts its attack of opportunity.** It always gets one, and the AI
  used to treat that back-off as safe.
- **A waiting order is dropped after heavy damage**, so the character can heal or withdraw at once.
- **Much smaller log.**

## Both modes
- **Attacks of opportunity along the whole path**, by the game's own movement rule. Charging past a big enemy is no
  longer treated as safe. Each attack is judged by its chance to drop the character in one blow, not just its average
  damage.
- **Extra caution below half HP** when moving to where more enemies can strike.
- **Reviving downed allies:**
  - A heal too small to get the ally up counts as progress, so healers finish the job.
  - A revive is worth only as much as the ally's chance of staying up. A tiny heal next to an enemy that will strike as
    the ally stands up loses to a bigger heal, or to dealing with that enemy first.
- **Harmful areas:**
  - New "Leave <area>" move, weighed against the area's save, the character's immunities, how long the area lasts and
    any attacks of opportunity on the way.
  - Walking into harmful areas is priced.
  - The party's own lasting areas count for allies who would walk in.
  - Selective metamagic follows the game's rule, including Tabletop Tweaks' change for lasting areas.
- **Spell damage estimates:**
  - Spell resistance and penetration by the game's rules: caster level, Spell Penetration feats including the mythic
    one, Allied Spellcaster, and abilities that ignore spell resistance.
  - Energy resistance, damage reduction and both kinds of energy immunity.
  - What each enemy actually took from each damage type is remembered: no more repeated Fire Blasts at a fire-immune
    Quasit.
  - Alternative damage branches are counted once, not added (Fire Blast was overrated about 2×).
- **Concentration:** spells, potions and scrolls cast while entangled are valued by the chance the concentration check
  succeeds.
- **Abilities are found the same way in both modes**, and the Progressive spell policy treats them the same way too.

## Log and diagnostics
- Decision lines show the character's conditions, e.g. `{nauseated | nauseated: move only}`.
- "Cannot act", "stays down" and "no option" lines say why.
- Party members the AI doesn't control are named, with the reason.
- `[Damage]` lines report resistances and immunities seen in combat.

## Compatibility
- Existing settings files keep working; the only new setting is the real-time engine choice.
- Tabletop Tweaks: Selective metamagic, condition fixes and Staggered speed are read from the game's (patched) rules.

## Known limits
- Attacks of opportunity are judged along a straight line to the destination; a real path that bends around obstacles
  can differ.
