# coverage4 follow-ups: freeing a dominated ally, swarm exits, Fusion C6a

21 November 2026, Claude. The author asked for three things after the coverage3 review
([COVERAGE3_REVIEW.md](COVERAGE3_REVIEW.md)). They are in the coverage5 test package. Both modes are covered. Not played.

1. Value freeing a dominated ally with a dispel.
2. Movement changes for current deficiencies, such as Seelah finding no way out of two swarms.
3. Any Fusion coverage programme work that can be done now.

**Package:** [coverage5](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage5-2b9d8ed-UMM-test.zip)
- startup MVID `3d6cdb2b-74b2-40dd-9e5c-4cae541b1d7f`;
- UMM 0.3.60;
- checks: [COVERAGE5_CHECKS.json](competence/COVERAGE5_CHECKS.json);
- compiler and references: [COVERAGE5_BUILD_PROVENANCE.json](competence/COVERAGE5_BUILD_PROVENANCE.json).

It is coverage4 plus these changes, on `claude/coverage4-followups`, stacked on `claude/coverage3-review` (draft PR
#96). Nothing is merged.

## 1. Freeing an ally under an enemy's control (both modes)

**Before.**
- Coverage4 stopped the party from killing a dominated ally, but did not try to free it.
- Dominate Person's buff is flagged only `IsFromSpell`, not harmful. A Dispel Magic aimed at the ally got only the
  flat 2-point "dispel" fallback, once a fight.

**Native (verified in the assembly and the exports).**
- Dominate Person (`c0f4e1c2`) carries `ChangeFaction`.
- Each round its bearer rolls a Will save. A pass removes the buff (`ContextActionSavingThrow`, then
  `ContextActionRemoveSelf`). Removing it stops the faction switch, and the ally is back.
- `ContextActionDispelMagic` tries the target's buffs sorted by caster level, highest first. It stops after its count.
- Dispel Magic (targeted) removes one spell buff on a caster-level check. It does not skip the party's own buffs on the
  ally (`OnlyTargetEnemyBuffs` is off). A higher-level Haste on her can therefore be dispelled instead of the Dominate.
- Protection from Evil neither suppresses nor ends it.
- Break Enchantment is a caster-centred area for allies only, so it does not reach a unit on another side.

**Change** (`TacticalEngine.TurnedAllies.cs`: `FreeingValue`, `FreedWorth`, `RoundSavePass`; `DispelValue`).
- A dispel or a removal that can end the hold is worth what freeing gives back. For each round the hold would last, the
  enemy loses a fighter (its control worth, as a hard control would take) and the party has its ally back (its worth per
  round). That worth is less while the ally cannot act anyway, and half for a summon.
- The rounds the hold would last weigh round k by the chance every re-save before it failed, (1 − pass)^k, up to six
  rounds and its time left (`ValueModel.HeldRounds`, unit-tested).
- **Dispels** use the shared dispel order (`DispelValue`, from Unraveling): caster-level order, check chance, count.
  - A buff the party gave the ally counts as a loss.
  - A dispel with no check removes what it takes.
- **Removals.** A removal by name or descriptor of the hold itself counts in full (Optimistic Smile).
- Both are multiplied by the chance the cast reaches the ally.
- The log shows "frees X N% from an enemy's control", once per fight, caster and ability.

## 2. Swarm exits: leaving one of several swarms (movement, author-approved)

**Evidence (coverage3, fight 10).**
- Seelah was nauseated inside a Locust Swarm and a Vescavor Swarm. Her attack was dropped, because a nauseated unit can
  only walk.
- The exit search found "no free spot out of its space within a step or a short move". She did nothing more that fight.

**Cause.**
- A cast-style swarm's space is its burst around any party member it can pick. In a crowded melee the two swarms'
  spaces overlap.
- The search wanted a spot clear of every swarm that strikes the unit, within a move. There was none.

**Change** (`TacticalEngine.PartialSwarmExits`).
- When no full exit is found and more than one swarm strikes the unit, the search tries spots that leave one of them:
  - twelve directions, using that swarm's own native exit circles;
  - kept only where fewer swarms strike (native `SwarmStrikes`), and where the area and walk tests pass;
  - the four with the fewest swarms left, then the shortest walk, are priced like any other way out: gain, move risk,
    and what the move gives up.
- The label reads "Move out of X's swarm (still in Y's)".
- The full exit is tried first. Nothing changes when it exists.

## 3. Fusion coverage C6a: ranked damage over time

**What was left.**
- The largest remaining bucket is C6, "support, reveal, form and fact carriers": 1,028 configurations withheld and
  2,966 on the simpler value.
- The spells behind it are Caustic Eruption, Excruciating Deformation, Touch of Slime, Phantasmal Putrefaction,
  Enervation, Reduce Person, Serenity, Curse of Magic Negation and the three Peer Through spells.
- Caustic Eruption is plain damage over time: 1d6 acid per two caster levels a round, at most 10. Its dice come from a
  caster-level rank on the buff itself. The carrier reader only took fixed dice.

**Change** (`CombatMath.IsPeriodicDamageCarrier`, `PeriodicRank`).
- A tick's dice may come from a rank config on the buff that reads only the caster level. No stat, class, buff rank or
  property counts.
- The rank is read as the game reads it: in a buff context whose parent is the cast's context, so it gets its caster
  level, the fused spellbook and metamagic. It is read once per cast, buff and frame.
- Fused and ordinary casts share the reader. An ordinary Caustic Eruption's carrier sits behind its save, and stays on
  the older rule (as before).

**Result** (15,494 configurations):

| | coverage4 | coverage5 |
| --- | --- | --- |
| Offered | 9,999 | **10,077** |
| Offered with the joint value | 5,995 | **6,367** |
| Lost | — | 0 |

All the gain is Caustic Eruption's (78 offered, 372 joint).

**What stays in C6, and why.**
- **Phantasmal Putrefaction:** its tick nests a Will save and a Fortitude save with nausea behind a condition.
- **Touch of Slime:** Constitution damage that spreads.
- **Acidic Spray:** its damage lands when the buff ends, not each round.
- **Reduce Person and Peer Through:** size and resistance changes.
- **Enervation:** heals undead.

Each needs its own reader. They are listed in the programme page.

## Checks

On `2b9d8ed`:
- Release build: 0 warnings.
- Tests: 1,066 unit (1 new) + 27/21/3 script tests.
- ProfileAudit exit 0:
  - new `FREEING` 12;
  - `FUSION-DOT` 47 (5 new: ranked ticks, the actual Caustic Eruption acid, the rank read);
  - `SWARM-ESCAPE` 32 (3 new);
  - `BUFF-SUPPRESS` 35, reading the full dispel overload.
  - The `PERIODIC` export line now lists Caustic Eruption among the carriers behind a save.
  - Every other line, and the summon detail, are identical to coverage4 on the same inputs.
- Native harnesses: 98/14/10.
- 0 game references added or removed (2,057).

## Replay (coverage5)

Record the startup MVID `3d6cdb2b`. Send `Log.txt` and `combatLog.txt`, and say where you spawned enemies, rested, or
gave manual orders.

1. **A dominated ally, and a caster with Dispel Magic** (both modes).
   - The log shows "… frees X N% from an enemy's control".
   - Dispel Magic on the ally should beat a weapon attack, and no one should hit the ally with weapons (coverage4).
2. **Several swarms around a unit**, ideally a nauseated one.
   - A "Move out of X's swarm (still in Y's)" order may appear when no spot clears them all.
3. **Caustic Eruption, fused or on its own.**
   - A fused one shows "damage over time" in its `[FusionValue]` line.
