# Relief spells: Dispel Magic, Break Enchantment, Remove Fear, Unbreakable Heart

21 November 2026, Claude. The author asked:
- how the mod treats a confused ally. In the game, confusion does not make the ally an enemy.
- to check why, as a user of the stable 0.3.52 reported, "none of the magic-using characters ever cast Dispel Magic".
- to check that the mod values the spells that help a confused, dominated or frightened companion.

The fix is in the relief1 test package. Both modes are covered. Not played.

**Package:** [relief1](../test-builds/AI-Control-FACC-v0.3.60-dev-relief1-746d7a7-UMM-test.zip)
- startup MVID `5537a531-29f4-466c-9111-41c141e2ad26`;
- UMM 0.3.60;
- checks: [RELIEF1_CHECKS.json](competence/RELIEF1_CHECKS.json);
- compiler and references: [RELIEF1_BUILD_PROVENANCE.json](competence/RELIEF1_BUILD_PROVENANCE.json).

It is coverage5 plus this change, on `claude/relief-spells`, stacked on `claude/coverage4-followups` (draft PR #97).
Nothing is merged.

## What the mod did before

| Spell | How it was valued | Effect |
| --- | --- | --- |
| Dispel Magic, Greater Dispel Magic | A flat 2-point "dispel" fallback, once a fight per target, whatever it would take off | Never beat a real option. This is the user's report. |
| Break Enchantment | The same fallback, and only for effects on its caster | Never cast for an ally |
| Remove Fear | Only its +4 save bonus. Its suppression of fear was unread (`SuppressBuffs`). | Not used to end a fear |
| Unbreakable Heart | Only its +4 save bonus. Its suppression of confusion, compulsion and fear was unread. | Not used on a confused or dominated ally |
| Removals by name or descriptor | A flat heuristic: 2 + 2 × spell level a round | Did not depend on what they remove |

A confused ally stays on the party's side, as in the game. Its confusion was seen as a harmful effect, but ending it was
worth almost nothing.

## Native contracts (verified)

- **Confusion** (`Confusion`, from the Confusion spell): a status condition. It has no per-round save and lasts its rank in
  rounds. The engine reads it as a hard control.
- **Dispel Magic** (targeted) removes one spell effect.
  - It tries the target's buffs sorted by caster level, highest first, with a caster-level check.
  - `RuleDispelMagic` has no protection for a friendly buff, so on an ally it can take off the party's own Haste or
    Heroism before the confusion.
- **Greater Dispel Magic** removes more effects, its count read in the cast's context.
- **Break Enchantment** reaches every ally within 30 ft. It tries every Enchantment and Transmutation effect up to 5th
  level, with no count, so it can also take off the party's own Enchantment and Transmutation buffs.
- **Remove Fear** gives a buff that suppresses Frightened and Shaken.
- **Unbreakable Heart** gives a buff that suppresses Fear, Shaken, Frightened, Confusion, Compulsion and negative emotion.
  Compulsion covers Dominate. It can be aimed at a unit on another side.
- **Suppression:** a new suppressor re-checks the buffs already on its bearer (`SuppressBuffs.OnActivate` →
  `UnitPartBuffSuppress.ApplyChanges`), by the buff context's descriptor and school.
- **Not in the game:** Calm Emotions and Rectify State are tabletop spells. They are in neither the game data, the
  supplied exports, nor the supplied mods. If a mod adds them with these components, the same readers price them.

## The change (both modes; `TacticalEngine.Relief.cs`)

**What ending an effect is worth.** The effect's share of the ally's output a round, times the rounds it would still act.
- The share is its control share (confusion and fear: hard) or its penalty share (shaken, a stat penalty), read from the
  buff (`OwnDebuffShare`).
- The output is the ally's worth per round, half for a summon.
- The rounds are its time left, weighted by its round re-save, minus any suppression, within three rounds.
- The total is never more than the ally's whole output.

**Dispels** (`TryDispelValue`, `DispelValue`) are priced on every unit they reach, in the game's own order and count:
- an enemy's buffs taken off are its loss (as for Unraveling already);
- on an ally, a harmful effect taken off is relief, and a buff the party gave it is a loss;
- an ally under an enemy's control is priced by the release (coverage5).

A Dispel Magic on a confused ally carrying the party's higher-level Haste is therefore worth less than on one without it.
An area dispel over the party is priced by the buffs it would strip.

**Suppressions** (`SuppressionRelief`). A buff that suppresses what hinders an ally is worth that relief while the buff
lasts.
- Unbreakable Heart on a dominated ally frees it for its duration, and it may now be aimed at that ally.

**Removals** by name or descriptor count what they remove (`RemovalRelief`), when that beats the old heuristic.

The old 2-point fallback stays only for a dispel the readers cannot read.

## Checks

On `746d7a7`:
- Release build: 0 warnings.
- Tests: 1,066 unit + 27/21/3 script tests.
- ProfileAudit exit 0:
  - new `RELIEF` 16: native contracts, the actual Remove Fear, Unbreakable Heart, Break Enchantment, Dispel Magic and
    Confusion graphs, and the compiled routes;
  - every other line, the Fusion coverage and the summon detail are identical to coverage5.
- Native harnesses: 98/14/10.
- 0 game references added or removed.

## Replay (relief1)

Record the startup MVID `5537a531`. Send `Log.txt` and `combatLog.txt`.

1. **An ally confused or frightened by an enemy, with a caster who has Dispel Magic, Break Enchantment, Remove Fear or
   Unbreakable Heart.** The caster should end it. The decision row shows "dispel +N", "cleanse" or "suppresses what
   hinders".
2. **Dispel Magic on a buffed enemy** (Haste, Mirror Image, Stoneskin) should be cast when it beats an attack. Its row
   shows "dispel +N (k)".
3. **A dominated ally, with Unbreakable Heart.** "frees X … from an enemy's control".
4. **An ally carrying the party's buffs.** Dispel Magic on it should be rare, and its row shows the loss ("… lose own
   buffs").

## Limits

- A confused ally's chance of attacking the party is not added to its harm; only its lost turns count.
- Dispelling an area effect on the ground (Dispel Magic aimed at a point) still uses the older value.
- Removing one of several hard controls on the same ally is capped at the ally's whole output, not proven exact.
