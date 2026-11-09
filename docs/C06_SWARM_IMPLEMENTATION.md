# C06R2: approved swarm coverage and exits

The author approved the bounded proposal on 9 November 2026: “you can implement, but remember to make sure its optimised/performant.” Task branch `codex/c06-swarm-coverage` starts at C06R1 `758d7b1` (PR #28), which contains development `1f58150` / runtime `3443d94` and the previously permitted newer C06 fixes. PR targets development; manifest remains 0.3.30. Claude reviews/integrates later.

## Evidence and resulting behavior

The provided Corrupted Rat and Thin Bhoga unit graphs directly grant their damage ability and a native swarm body feature; their brains select those casts. Both abilities use a **Standard action**, **negative custom cast range** and **AbilityTargetsAround on the chosen unit target**. An old Corrupted Rat aura graph existing in the database does not establish that the encountered unit carries it. The former hazard reader required a live registered-target area, so these cast deliveries could not enter that model. Bhoga also labels its ability EffectOnEnemy=None, which excludes it from ordinary harmful-ability discovery despite its damage actions. [Input fingerprints/native rules](C06_SWARM_NATIVE_EVIDENCE.json) identify the exact graphs and supplied native module. This establishes a native delivery gap, not an attributed live actor/cache state for every old-log decision.

The new bounded cast reader requires the actual active swarm body, owned/available ability and inspected structural contract: a near-contact Custom range, single ordinary enemy selector without conditions/dead targets/spread, direct damage and a Standard action. Unknown filters, alternate delivery, multiple competing cast contracts or resource logic remain generic. No display-name/GUID threat rule or arbitrary candidate cap is introduced.

Primary cast reach comes from AbilityData.GetApproachDistance, including the native custom range and both bodies. Collateral damage is centred on an eligible ally's EyePosition, with the native recipient-body radius, target filters and LOS. Unconscious living allies can be anchors. Damage, poison, saves and control use the existing native-context ability readers, once per caster/victim/decision; a Standard cast competes with ordinary offence and is not added as a fictitious periodic aura. Zero known consequence contributes no cast danger for an immune victim. Existing registered auras retain their delivery and use the actual registered UnitPartSwarmAttack targets for the inner-circle gate, including unconscious entries, without creating parts.

The source cache now checks fact identity, Active and component-array identity; count alone cannot certify active sources. It expires between decisions even within one frame. It rereads mutable DC/availability data without calling activation or a mod initializer. Native fact-activation metadata and compiled equal-count activation/deactivation/replacement checks demonstrate this cache defect independently; the old logs do not prove it caused their missing areas.

## Exit integration and performance

Native cast spaces now feed the same incoming-damage, drop, approach and exit appraisals as registered swarm areas. Exit hints use **actual live ScriptZoneCylinder centre/radius**, native primary reach and eligible target-centred blast circles. A pure ray/connected-union solver finds the boundary without sorting or temporary arrays; the native coverage predicate validates each hint. Other shapes keep the existing search.

There are twelve angular directions, as before. Supported spaces need at most one step search and one move search: at most 24 geometry samples instead of the former four rings/48. Movement risk is priced only for each mode's chosen legal spot. The destination clears the boundary by the command's existing 0.3 m stop radius plus 0.05 m tolerance. Legal movement range, native step/path checks, harmful-area checks, refused steps, attacks of opportunity, ally exposure, role tolerance and opportunity-cost competition remain. A useful attack may still beat leaving. General retreat, target commitment, pathfinding, command dispatch and stuck-walk cancellation are unchanged.

Immutable blueprint classification is cached. Weak actor caches reuse source arrays with geometric capacity growth and clear removed references. Negative results are cached too. Victim records are reused; target/range/consequence/LOS reads are shared within a decision and refreshed for the next decision/frame. There is no scan of all world units, no rule/parameter query per exit direction, and no rule calculation in the pure boundary solver. The existing ability parameter/context caches remain in use.

Automated allocation regressions require zero managed bytes for 10,000 warm boundary queries and 10,000 unchanged native fact snapshots. These bound the helpers' behavior; they are not a Unity frame-time or full-decision allocation certificate. The earlier C06 slow heap samples include GC/global-heap effects, so no measured gameplay speedup is claimed.

## Verification and replay

Full unit/inventory suites, a zero-warning Release build against actual game/UMM assemblies, strict explicitly native-compatible ProfileAudit, existing toggle/Tome/viewless checks, added-member reference resolution and a fresh original-corpus strict check are recorded in the final machine receipt. Native swarm checks use the real Rat/Bhoga/Gluttonous Bhoga graphs, actual body features, native fact layouts and native cylinder Contains. They do not construct actors or execute game commands. Smaller audit subsets explicitly skip missing required swarm graphs; this task's full compatible scope must execute every swarm case.

The original corpus's fingerprinted Achaekek enum spelling error remains separately INCOMPLETE; no input is normalized and a subset pass is not a full-corpus pass. The test ZIP contains only the exact checked mod DLL and unchanged Info.json, with a sidecar identity JSON/checksum and unique startup MVID. Gameplay and matched Unity timing remain pending.

Replay **TB and RT** with Corrupted Rat/Thin Bhoga and a registered aura swarm:

1. Confirm the startup MVID and capture Swarm/SwarmAudit, decisions and native damage. Cast swarms should identify a target-centred Standard cast even without a live area; registered swarms should identify their native aura.
2. Test inside, just outside primary reach, outside primary reach but near an eligible ally, an unconscious nearby ally, blocked LOS and overlapping swarms. Compare actual native target selection and damage with the predicted space.
3. Test no movement left, a legal 5-foot step, a move, a boundary between the old sample rings or beyond 6 m, and blocked exits. No legal exit means no invented walk. Check the issued endpoint after the native stop radius.
4. Test an immune/resistant recipient, low/high HP, a useful attack that should beat leaving, and an attack approach that should stay outside the same danger. Preserve costs and confirm the unit does not immediately re-enter for a stale appraisal.
5. Check repeated decisions, reinforcements, activation changes and ordinary fights without swarms. Compare decision-stage timings on the same fights; report whole-decision latency/heap separately from the zero-allocation helper checks.

This implements the authorized detection/geometry/exit scope. Further changes to movement policy, commitment, retreat, paths or cancellation still require separate approval.
