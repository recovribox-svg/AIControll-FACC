# Fusion coverage: the remaining groups (lines, dispels, nested saves, lingering areas, Vampiric Touch)

21 November 2026, Claude. The author asked for the remaining Magic Fusion groups to be implemented, and approved any
addition needed for quality, such as moving to line up enemies for a line spell. Both modes are covered. Not played.

**Package:** [coverage6](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage6-b32057b-UMM-test.zip)
- startup MVID `b63b47f2-f445-4007-8b04-8722dfeb4598`;
- UMM 0.3.60;
- checks: [COVERAGE6_CHECKS.json](competence/COVERAGE6_CHECKS.json);
- compiler and references: [COVERAGE6_BUILD_PROVENANCE.json](competence/COVERAGE6_BUILD_PROVENANCE.json).

It is relief1 plus these changes, on `claude/fusion-coverage-rest`, stacked on `claude/relief-spells` (draft PR #98).
Nothing is merged.

## Result

15,494 configurations:

| | relief1 | coverage6 |
| --- | --- | --- |
| Offered | 10,077 | **13,197** |
| Offered with the joint value | 6,367 | **8,635** |
| Lost | — | 0 |

| Slice | Offered | With the joint value |
| --- | --- | --- |
| C4 lines (Lightning Bolt, Fire Snake, Acidic Spray) | +842 | +410 |
| C7 dispels (Dispel Magic, Greater Dispel Magic, Arcana Theft) | +80 | +504 |
| C11 Vampiric Touch | +266 | +208 |
| C9 nested saves (Phantasmal Killer, Banshee Blast) | +948 | +420 |
| C10 lingering areas (Obsidian Flow, Haunting Mists) | +984 | +726 |

Every structural refusal is gone. 2,297 configurations stay withheld:
- 1,189 by the outcome gate: their effect lands on a passed save, and a support, reveal, form or poison carrier keeps the
  joint value out (Reduce Person, Enervation's undead branch, Touch of Slime and others). Each needs a reader of its own.
- 1,108 by the shared Hit Dice gate. 528 are Circle of Death: in a fused cast its pool is the slot's own 2d6 Hit Dice
  (verified in F7), so it would kill only a few weak creatures. A kill pool is not worth its code yet. The rest are
  Sleep and Rainbow Pattern in cone and line deliveries, whose native order the pool forecast does not read.

## C4: lines (both modes; `ProjectileLine`, `TacticalEngine.LineAim.cs`)

**Before.** An ordinary Lightning Bolt was valued on its clicked target only: no other enemy, and no ally it would hit.
A fused cast delivered by a line was withheld ("line delivery geometry").

**Native contract (verified in the assembly).**
- The line leaves the caster's eye, moved forward by its body, towards the aimed point (a point aim only sets the
  direction). It travels its Length, at most its projectile's MaxRange.
- Every unit that is not dead and not the caster is handed to the payload once when `WouldTargetUnitLine` holds. That is
  the target rule (Selective) and `WouldTargetPointLine`: in front, before the length plus the body, and no further to the
  side than half the width (5 ft) plus the body.
- A line has no sight test. It goes through walls, unless its projectile follows the terrain.

**Change.**
- A line is an area now. Its recipients come from the game's own `WouldTargetUnitLine`, bound like the cone reader's.
  Damage, control and friendly fire count every enemy and ally on it, for ordinary and fused casts.
- From where the caster stands, a line is also tried turned past the allies it catches, or between the aimed enemy and
  another one. The better aim is offered.
- **Lined-up walk (author-approved movement).** A line may walk first to a spot within this move that lines two enemies
  up.
  - The spot is checked like an approach spot: free, walkable, no harmful area or swarm, the target in range and sight,
    priced by the walk's risk.
  - It is taken only when it beats casting from here by 15 % and 0.5, with the walk paid for.
  - One such walk a round. The line is cast on the next decision, from the new spot.
- Not read (stays single-target, and withheld when fused): a custom or controlled line, several projectiles, or a
  projectile that follows the terrain. That is 6 of the 114 loaded line deliveries.

## C7: fused dispels (both modes)

**Before.** A fused dispel was offered on the simpler value only.

**Change.** A fused dispel joins the joint value when the dispel order reads it, and it cannot take off a buff the same
cast applies:
- it runs before them (Spell 1's actions run before Spell 2's); or
- it takes only enemies' buffs (Arcana Theft: `OnlyTargetEnemyBuffs`).

It is priced on every unit it reaches (relief1's `TryDispelValue`). A dispel that runs after the cast's own buff could
take that buff off again; it stays on the simpler value (206 configurations).

## C11: Vampiric Touch (both modes)

Its caster-side payload is temporary HP equal to the damage dealt (`TemporaryHitPointsFromAbilityValue`, its shared
value). It no longer withholds the fusion. As for an ordinary Vampiric Touch, it is not credited. Any other healing or
caster payload stays withheld.

## C9: nested saves (both modes, ordinary spells too)

**Native.** A `ContextActionSavingThrow` under a declared root save rolls its own save at the cast's DC. In a fused cast
it rolls only with `MakeSaveAlsoForMagicHack`. A `ContextActionConditionalSaved` inside it reads that save.

**Change.** Each action inside such a save is weighted by that save's fail or pass chance:
- the damage of Phantasmal Killer's passed Fortitude save, and its kill on a failed one;
- Banshee Blast's fear behind its failed Will save.

Ordinary spells gain this too: Phantasmal Killer's kill was priced on its Will save alone. It reaches 13 abilities
(Phantasmal Killer, Weird, Banshee Blast, Hellfire Ray, some breath weapons).

Not read (fused: withheld as before): a nested save with a DC of its own, one inside another, a result read twice, or
half damage on the nested save.

## C10: lingering areas (both modes)

**Native.** After the delivery, a fused cast runs each source spell's clicked-target payload once, at the main target.
For Obsidian Flow and Haunting Mists that payload is one area spawn. Their `AdditionalAoeBlueprint` only shapes targeting
(the shown radius, `CanTargetAlly`, `CanTargetEnemies`); the recipients come from the deliver spell.

**Change.**
- The area is recorded instead of withholding the fusion. Haunting Mists needs no RunAction of its own.
- The area is not credited: the fused cast is worth what its payloads do to their recipients.
- The cast is refused when the area would cover an ally, when a melee ally stands at its edge or fights an enemy inside
  it, or (real time) when an ally walks into it.

## Checks

On `b32057b`:
- Release build: 0 warnings.
- Tests: 1,072 unit (6 new) + 27/21/3 script tests.
- ProfileAudit exit 0:
  - new `LINE` 21 checks. The pre-filter agrees with the game's `WouldTargetPointLine` on 4,000 of 4,000 samples.
  - new `FUSION-REST` 27 checks.
  - `FUSION` 39 and `FUSION-OUTCOME` 33: their line checks now expect the line to be read.
  - Every other line, and the summon detail, are identical to relief1 on the same inputs.
- Native harnesses: 98/14/10.
- 7 game references added, all compiled against the supplied assembly: the line's length and width, `FollowTerrain`,
  the projectile list's enumerator, and `AddIncomingDamageTrigger.Actions`.

## Replay (coverage6)

Record the startup MVID `b63b47f2`. Send `Log.txt` and `combatLog.txt`, and say where you spawned enemies, rested, or
gave manual orders. Summon control on, as you planned.

1. **Lightning Bolt (or Fire Snake, Acidic Spray), ordinary or fused,** with enemies in a row and an ally in melee.
   - The decision row should count every enemy on the line and any ally it hits ("turned … past …" or "along …").
   - A "[walks first: lines up X and Y …]" order may appear; the line should follow from the new spot.
2. **A fused Dispel Magic or Arcana Theft** against a buffed enemy: its row shows "dispel +N".
3. **Phantasmal Killer**, ordinary or fused: it should be chosen less against enemies with a good Fortitude save.
4. **Haunting Mists or Obsidian Flow fused:** never cast where the area would cover an ally ("lingering area on …").
5. **Vampiric Touch fused** with an area spell: offered and valued by its damage.

## Limits

- A lined-up walk is cast on the next decision; in real time the enemies may have moved by then.
- The lingering area's harm to enemies is not credited.
- The C6 carriers and the shared Hit Dice pools above remain. (Both done later: [FUSION_COVERAGE_C6_C8.md](FUSION_COVERAGE_C6_C8.md).)
