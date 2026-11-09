# C06 swarm investigation and movement proposal

**Status: approved by the author on 9 November 2026 and implemented on `codex/c06-swarm-coverage`.** See [C06_SWARM_IMPLEMENTATION.md](C06_SWARM_IMPLEMENTATION.md) for the bounded native rules, performance work, validation and replay. The original proposal below is preserved as its approval scope; unrelated movement, commitment, retreat, pathfinding and cancellation changes still require approval.

## Evidence

The C06 AI logs contain Corrupted Rat Swarm (TB log) and Thin Bhoga Swarm (Log 2), but neither file has an existing `[Swarm]` area/exit diagnostic. Log 2 shows Tuvel at 21%, 12% and later 2% HP continuing approaches/attacks on Thin Bhoga; those labels alone do not reveal exact positions, accessible exits or what another command would achieve. There is no basis to declare every approach a mistake.

The provided native blueprints distinguish the mechanisms. Corrupted Rat Swarm has a damage buff with AddAreaEffect, a registered-target effect buff using ContextActionSwarmTarget, and NewRound ContextActionSwarmAttack/ContextActionOnSwarmTargets. That shape is already recognizable offline, so its missing live diagnostic needs actor/area/cache evidence. Thin Bhoga's supplied class/brain includes a damaging AbilityTargetsAround ability; the code cannot assume every swarm acts through the same registered-target aura. Native ability target selection, cadence, caster/target bodies and immunities must determine its affected space.

Two concrete current reader limits warrant investigation: RefreshSwarms requires an in-game, non-ended, non-null-view area with a matching caster and a readable round source; SourcesOf caches active-fact sources by fact count and primary-weapon ID, which does not encode an active-state transition at unchanged count. The logs do not show which condition failed. These are candidate causes, not an attributed live root cause.

## Added passive evidence

`[SwarmAudit]` records changed coverage signatures on the existing enemy assessment: current active swarm-round facts, already-cached source count (without rebuilding it), own live/recognized/viewless area counts, already-selected area/cache freshness and up to three nearby-cast blueprint hints. It uses native swarm-resistance components rather than a display-name list. It does not calculate ability parameters, refresh actor/source caches, create parts, activate anything or select a move. The three-name display limit is diagnostic only.

Capture this alongside the normal decision/native damage lines in TB and RT. In particular, compare cached round sources with current raw active facts; identify a missing live area/caster/view, versus a distinct nearby-cast delivery.

## Proposed implementation after approval

1. Correct detection/invalidation only for a demonstrated native graph: use actual active-source changes for the source cache, and use the real live area/target-selection contract for the encountered swarm. Add compiled graph/transition regressions. Do not infer a radius or periodic damage merely from the word Swarm.
2. Feed that verified affected space and native round/cast harm into the existing swarm danger and exit appraisal. Check the actual native body's contribution to inclusion and the ability's cadence. Where the present short-exit samples cannot cross that boundary, propose or implement only approved native-boundary-aware exit sampling within the unit's legal movement.
3. Keep native movement permissions, attacks of opportunity, available actions, opportunity costs, target commitments, paths, retreat and cancellation rules. No emergency walk override, new forced target or hidden-information movement. Any further policy/search change requires a separate proposal.

Likely affected files after approval: CombatMath.Swarms.cs, source-cache handling in CombatMath.Afflictions.cs, and only the necessary integration in TacticalEngine.Swarms.cs. Confirm that scope against the new live diagnostics before editing.

## Paired acceptance

For each encountered swarm, test already inside, entering, just outside, another ally at its centre, multiple overlapping swarms, zero/one move remaining, immunity/high versus low HP, and a useful attack/spell that should beat leaving. Include legal exits and blocked exits in both modes. Observe native damage/target inclusion and the final issued outcome. Verify that a unit does not immediately walk back into the same danger for a stale appraisal. The author subsequently approved this scope; implementation and pending gameplay checks are documented in the linked task handover.
