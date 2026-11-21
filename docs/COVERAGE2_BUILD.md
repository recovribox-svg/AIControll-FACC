# coverage2: the build to play next

20 November 2026, Claude. This is one cumulative test package with everything done since the #81–#91 integration. It
includes only what could be done soundly without a playtest. The new log lines then provide what the next round needs.

**Package:** [coverage2](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage2-72acfd1-UMM-test.zip)
- startup MVID `5cfd7765-2f1c-487d-8945-dde2c78a643b`;
- UMM 0.3.60;
- checks: [COVERAGE2_CHECKS.json](competence/COVERAGE2_CHECKS.json);
- compiler and references: [COVERAGE2_BUILD_PROVENANCE.json](competence/COVERAGE2_BUILD_PROVENANCE.json).

**Not played. Nothing merged.**

## What is in it

| Part | Branch / PR | Page |
| --- | --- | --- |
| Fusion C1: Molten Orb's main target, damage over time in fusions | `claude/fusion-coverage-programme`, draft PR #92 | [FUSION_MAIN_TARGET_DOT.md](FUSION_MAIN_TARGET_DOT.md) |
| R2: no burst landing wait or shared landing spot is ever dropped | `claude/review-r-items` | [R_ITEMS_PLAN.md](R_ITEMS_PLAN.md) |
| R5: a started attack chasing its target is forecast walking into a burst | `claude/review-r-items` | [R_ITEMS_PLAN.md](R_ITEMS_PLAN.md) |
| Evidence logging for R1, R3 and R4 | `claude/review-r-items` | below |
| Fusion C2: Hideous Laughter's Best Jokes recast (arcan's Explosive Runes / Hideous Laughter) | `claude/fusion-coverage-c2-c5` | [FUSION_COVERAGE_PROGRAMME.md](FUSION_COVERAGE_PROGRAMME.md) |
| Fusion C5: ability damage and negative levels in the joint value | `claude/fusion-coverage-c2-c5` | [FUSION_COVERAGE_PROGRAMME.md](FUSION_COVERAGE_PROGRAMME.md) |
| Damage over time on ordinary spells | `claude/fusion-coverage-c2-c5` | [PERIODIC_DAMAGE.md](PERIODIC_DAMAGE.md) |

**Magic Fusion coverage** (15,494 configurations):

| | Development | C1 | C2 | C5 (this build) |
| --- | --- | --- | --- | --- |
| Offered | 8,709 | 9,209 | 9,587 | **9,655** |
| Offered with the joint value | 4,521 | 5,039 | 5,321 | **5,651** |

No configuration is lost at any step. All eight fusions on arcan's bar are now offered with the joint value.

## Replay (both modes)

Record the startup MVID `5cfd7765`. Send `Log.txt` and `combatLog.txt`, and say where you spawned enemies, rested, or
gave manual orders.

1. **arcan's bar.**
   - Explosive Runes / Hideous Laughter should now be cast. Its `[FusionValue]` line appears once a fight.
   - Incendiary Runes / Frigid Touch's value line shows "damage over time".
2. **Molten Orb and Acid Arrow, fused and on their own.**
   - Fused: the value line shows "damage over time fail/pass".
   - Aimed at a unit, that unit takes Molten Orb's direct hit.
   - On their own: they are valued with their ticks, not a guessed debuff.
3. **Bursts in real time.** A melee ally keeps chasing the enemy an Explosive Runes fusion is aimed at. The burst should
   be cancelled ("walks into it") or not aimed there.
4. **Ordinary fights.** No change is expected elsewhere.

## Evidence the next round needs

At the end of each fight, the log has up to three summary lines. Please make sure they are in the logs you send.

| Line | Example content | Decides |
| --- | --- | --- |
| `[Burst] fight summary` | cast went off X s from the forecast; anchor inside where it landed N of M, D m from the point; walks of started attacks chasing their target; ally walk-ins with no control price | the burst lead and walk forecast; R3 |
| `[Timing] fight summary` (two lines) | first melee swing after the start, gaps between swings by routine size, ranged gaps; attacks issued on a casting enemy: forecast against actual first attack and cast end, broken / went off | R4: the 0.6 s / 0.7 s / 6 s ÷ N / 30 m/s constants |
| `[Flank] fight summary` | flank forecast right / forecast but not flanked (of them by a second engager, with the height between the units) / flanked but not forecast | R1: whether the origin-aware engagement quote is worth building |

For a useful sample, include:
- a real-time fight where melee companions attack a casting enemy (disruption);
- several melee companions on one enemy (flank forecasts);
- an Explosive Runes fusion against moving enemies.

## Not in this build

- **C3, Disintegrate.** Its "turn to dust" marker kills a downed creature outright. A fused Disintegrate that catches an
  ally risks a permanent death, and how heavily to weigh that against a knockout is the author's call.
- **The death-trigger proposal.** Set aside at the author's request.
- **R6 and R7.** See [R_ITEMS_PLAN.md](R_ITEMS_PLAN.md).
