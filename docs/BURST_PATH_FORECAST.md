# Burst forecast along the game's paths, control on walk-in allies, and the watch's capacity (review follow-ups A-R1, A3, A-P1)

**Codex review correction (17 November 2026):** proximity of the path end is not ownership. The current
correction matches ABPath/XPath's original request and uses full native approach distance with height handling;
future LOS is not proved. Retained roster members are revalidated. The approved capacity policy can still discard
TB landing metadata or bypass registration of a new unwatched RT burst at saturation, so “TB unaffected” below is
qualified. Damage/control snapshot freshness and candidate-table cost remain proposals. See
[REVIEW_86_89.md](REVIEW_86_89.md) (A-D1–D3/R2–R6). Use its **review2** package/replay, not burstpath1; receipts below
describe the original PR.

17 November 2026, Claude. These are the burst follow-ups of [REVIEW_FOLLOWUPS_PLAN.md](REVIEW_FOLLOWUPS_PLAN.md). They
answer A-R1 and A-P1 in [REVIEW_81_84.md](REVIEW_81_84.md); A-R1 includes the re-check's HP-only gap (A3). The author
approved them on 17 November ("You have my approval for any further work that needs it"). Real time only.

Branch `claude/burst-path-forecast` is stacked on F2 (PR #87) → F1 (PR #86) → PR #85 → #84 → #83 → #82 → #81. Nothing is
merged. **Not played.** UMM stays 0.3.60.

Checks: [BURST_PATH_CHECKS.json](competence/BURST_PATH_CHECKS.json). Compiler and references:
[BURST_PATH_BUILD_PROVENANCE.json](competence/BURST_PATH_BUILD_PROVENANCE.json). Package:
[burstpath1](../test-builds/AI-Control-FACC-v0.3.60-dev-burstpath1-62dede5-UMM-test.zip), startup MVID
`1b3df06e-9d10-41ec-bf9e-8783f7787a50`. It contains slices A–D, the PR #85 corrections, F1 and F2.

## The problems

1. **Straight walks (A-R1).** Slice A predicts where a walking unit will be when a ground-aimed burst lands. It moved the
   unit along the straight line to the end of its walk. The game walks it along a path, round walls and other bodies.
   A path that bends puts the unit somewhere else at the same moment. This matters in three places:
   - the burst's lead point on its anchor enemy;
   - the enemies counted as surely inside;
   - the allies whose walk may take them inside.
2. **HP-only re-check (A3).** Before the cast goes off, the re-check cancels it when an ally the decision did not count
   walks in. It counted an ally only when the burst's estimated damage to it was above zero. A burst whose sleep or
   stagger catches an ally its damage does not hurt (an immunity, or a control-only payload) was let through.
3. **The watch (A-P1).**
   - Every re-check walked the whole scene's unit list, up to four times a second per watched caster.
   - It estimated a newly exposed ally's damage again at every check.
   - At 32 watches, the oldest was dropped even while its cast had not gone off. That silently lost its re-check and its
     turn-based landing wait.

## The game's rules

| Rule | Native source |
| --- | --- |
| A command that must approach asks the unit's movement agent for a walk to its approach point. | `UnitCommand.TickApproaching` → `UnitEntityView.MoveTo` → `UnitMovementAgent.PathTo` |
| The agent follows the path it got back, waypoint by waypoint, from its next waypoint. | `UnitMovementAgent.Path.vectorPath`, `m_NextPointIndex`, `SetWaypoint` |
| It walks at the unit's speed. | `UnitMovementAgentBase.MaxSpeed` = `CurrentSpeedMps` |
| The command stops the walk once the unit is within its approach radius of the approach point, which can be before the path's last point. | `UnitCommand.IsUnitCloseEnough` |

A real-time path is computed to the target's position (`StartPath`), or with a sight ending for long approach radii
(`XPath`, `LosPathCondition`). Either way, the path ends within the approach radius of the approach point, and the agent
asks again when the target moves.

## The change

### A-R1: walks along the game's path

`TacticalEngine.ReadRoute` builds a unit's walk:
- It finds the command the unit walks for (`WalkingCommand`, the test `WalkClaim` already used).
- It reads the agent's current path from its next waypoint, starting at where the unit stands. It accepts that path only
  when it ends within the command's approach radius of its point, plus 1 m (`RealTimeController.NativePathFallsShort`'s
  tolerance).
- The walk stops where it first comes within the approach radius of the point (`WalkRoute.StopWithin`), at the share
  `SpotRules.WalkEnd` already uses.
- Without a usable path, it falls back to the straight walk of slice A. That covers no path yet, another walk's path,
  or more than 64 points.

`WalkRoute` (pure, unit-tested) gives the position after some seconds and whether a walk may be within reach of a point
around the landing moment. It uses the same speed floor, time window and uncertainty as before. The straight walk is
its two-point case, which replaces `BurstRules.At` and `WalkMayBeInside`.

Who uses it:
- **Decision:** the lead point, the enemies inside and the walking allies read each unit's route once per decision
  (`RouteOf`). The plan line reads "N m ahead of X along its path" when the lead follows the path.
- **Re-check:** the same reads, with fresh positions.

### A3: control on an ally that walks in

When a ground-aimed burst is scored, the control part already prices allies it catches. It uses `wouldBeCaught`, with
the recipient, guard, immunity and pool rules, including the shared Hit Dice pool and the fusion outcomes. Now it also
prices every ally the decision did not count, with the same predicate and the same price, and keeps those prices on
the plan (`BurstPlan.AllyControl`).

The re-check adds that price to the damage cost of an ally that walks in. An ally with no damage but a control price
now counts as caught:
- with Avoid Friendly Fire on, the cast is cancelled, as the decision would have vetoed it;
- otherwise the cast is cancelled only when the new allies cost more than the cast was worth.

No second control model is added. Sleep's ally exclusion stays the pool's own rule, which `wouldBeCaught` reads.

### A-P1: the watch

- **One roster per frame.** A re-check reads the units gathered once per frame (in the game, alive, in combat), shared
  by every watch in that frame (`RosterNow`). The caster is read too when it is not in the roster.
- **Damage once per watch.** An ally's damage estimate is kept for the watch (`DamageTo`).
- **No live watch dropped.** The cap is 64, as a memory guard. A full list makes room only by dropping a burst that has
  gone off, the one landing first; its landing spot stays with `AoeDangerAnalyzer` until it lands. With only casts that
  have not gone off, the new burst goes unwatched and says so in the log. It keeps the checks of its decision.
- **Measured.** When a fight ends, one line gives the counts: bursts watched, the most at once, re-checks, the most
  units a re-check read, and walks read along the game's path or straight. Bursts that made room or went unwatched are
  counted too. The cap stays until these counts say otherwise.

### Cost

| Work | Bound |
| --- | --- |
| Route read | Per walking unit: at most 64 points of the agent's existing path. Once per decision; once per re-check in the watch. |
| Searches and queries | None added: no path search, raycast or unit scan in the route read. |
| Walking-command test | The pre-existing `WalkClaim` test, unchanged: the game's `IsUnitCloseEnough`, which includes its own sight test for a command that needs sight. |
| Control prices | One `wouldBeCaught` test per ally the decision did not count, for each ground-aimed burst scored in real time whose area can harm allies. |
| Re-check | Per frame, one pass over the scene's units, shared by every watch. Per watch, one damage estimate for each ally. |

## Checks

On checked source `62dede5`, real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 1,025 pass (8 new route tests; the straight-walk tests now run on the two-point route) |
| Inventory, ledger and supplement script tests | 27 + 18 + 3 pass |
| ProfileAudit | exit 0; `BURST-AIM` 48 checks (15 new), 0 failures; every other line identical to the F2 run on the same inputs; Fusion coverage and summon detail byte-identical |
| Native harnesses | 98 / 14 / 10 |
| Signatures | 1 added: Harmony's field read over `UnitMovementAgent` (the next waypoint), resolved by the audit. 1 removed: `Mathf.Lerp`. No other game member added. |
| Supplementary Magus corpus | not run here (inputs not in this environment); no Magus code is touched |

The new `BURST-AIM` checks cover:
- the native chain `TickApproaching` → `MoveTo` → `PathTo`;
- `SetWaypoint` reading `vectorPath` and setting `m_NextPointIndex` (an `int`);
- the `Path` type, the agent's speed, and the command's close-enough test;
- that the route read starts no path search, raycast or unit scan, and that the next-waypoint read resolves;
- the decision and re-check routes, and the control prices stored and read;
- the per-frame roster, and that `Track` makes room only through `MakeRoom`;
- a production run of `MakeRoom` on a full list: no live watch dropped, and the burst landing first goes;
- the fight's counts at combat end;
- a walk round a wall, caught along its path where the straight line misses it.

## Replay (real time)

Record the startup MVID `1b3df06e`. Keep `Log.txt` and `combatLog.txt`.

1. **A walk round an obstacle.** An enemy walks round a wall or a pillar towards the party while the Magic Deceiver casts
   an Explosive Runes fusion (or Fireball) at it. The `[Burst]` line says "N m ahead of X along its path". Compare where
   the burst lands with where the enemy is then.
2. **An ally walking round to its target.** A melee companion walks round an obstacle and passes the planned point. The
   decision counts it walking in, or the re-check cancels the cast ("walks into it").
3. **Control on an ally the damage does not hurt.** If the party has a fire-immune ally, try a Fireball fusion carrying
   a control payload, for example Snowball/Fireball or a Color Spray fusion, while that ally walks into the point. With
   Avoid Friendly Fire on, the cast should be cancelled ("walks into it"). Sleep fusions follow the pool's own rules: an
   ally the pool would not select does not cancel the cast.
4. **Counts.** At the end of each fight with a ground-aimed burst there is one `[Burst] fight summary` line. Please send
   it: the watch count and the share of walks read along the path decide whether anything else needs changing.
5. **Regression.** Turn-based ground aim and its landing wait behave as before; the slice A replay still applies.

## Limits

- **Not played.** Offline checks create no unit, path, command or Unity timing.
- The route is the path the agent has now. A new order or a repath after the decision changes it. The re-check reads it
  again, with the same uncertainty band as before.
- The agent's acceleration, avoidance steering and warm-up delay are not modelled; the speed is the unit's speed, as
  before.
- A path longer than 64 points, or one that does not end at the command's point, falls back to the straight walk.
- A player's new order after the cast has gone off can still walk an ally into the burst (slice A's limit).
