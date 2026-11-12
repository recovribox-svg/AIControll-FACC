# Area exits: native entity bounds

12 November 2026. Approved exit-repair follow-up, `codex/area-exit-native-bounds`, starting at PR #38
`e1e31498a2d899a87c1dd593c68c6fb7a32817da`, containing fetched development `1f58150` and the played
exit1/caster/Magus/step prerequisites. No integration into development/main or release.

## Problem and evidence

[Exit1 review](EXIT1_PLAYTEST_REVIEW.md) and [input identities](EXIT1_PLAYTEST_INPUTS.json) account for eight
fights / 900 decisions. Orvath takes four successive smoke leaves (AI 3586–3640), Camellia three short leaves
(4462–4503), although returned ground endpoints now match requested destinations. This does not establish
that every repeat has the same cause: smoke follows Brimorak, actors can be recaptured, and native membership
can lag. It does show that successful movement alone is insufficient exit acceptance.

The previous forecast used `Shape.Contains(point, Corpulence + arrival clearance)` for all shapes. The native
membership pipeline differs. [Native evidence receipt](competence/MOVEMENT_BOUNDS_NATIVE.json) fingerprints
the supplied assemblies and identifies the inspected methods; private native sources are not committed.

- `AreaEffectEntityData.UpdateUnits` gets `EntityBoundsHelper.FindUnitsInShape(View.Shape)` results.
- Cylinders query a world-up capsule, using world centre, mean X/Z transform scale, `Height * mean` on either
  side of centre and `Radius * mean`. Visual cylinder rotation is not this capsule's orientation.
- Boxes query world centre, `Bounds.extents` scaled by transform lossyScale, and transform world rotation.
- `ShouldUnitBeInside` skips `View.Contains(unit)` for these two shapes. It retains eligibility/LOS checks;
  other shapes still use `Contains`. This repair forecasts the geometric query, not all future eligibility.
- Unit entries come from the separate entity-bounds scene's `SphereBoundsCollider`. Its world radius need
  not equal logical Corpulence. `EntityBoundsController` maintains radius/position, potentially after state changes.

## Resulting behavior

The exit search reads the actual sphere collider's native world bounds and transformed local-centre offset.
World AABB extents give the actual sphere radius; no guessed scale formula or temporary collider is required.
Each live cylinder/box is captured with the native query's world geometry. Numerical closest-point tests
check sphere versus capsule or oriented box, including the existing 0.15 m stopping clearance. Other shapes
retain native `Contains` with logical Corpulence, matching their membership branch.

One snapshot serves all at-most-80 hints in a search. Selected preflight and issue take fresh snapshots before
checking the returned native path endpoint/distance. Unknown bounds/shape/invalid data cannot earn escape
value. A unit whose current body is already geometrically clear gets no new leave credit, even if registered
membership still lists it inside; final selected checks also reject an exit that is no longer needed. The
game's membership list, global danger forecast and status effects are never rewritten.

`[AreaExit]` records registered membership, current overlap/clear status, area instance, volume geometry,
actual sphere radius/offset/observed centre and current unit position. It is at most once per actor/area/status
per fight through the existing globally capped 256-note mechanism. Other shapes say that physics bounds
were not queried. Missing geometry gets a bounded notice. These snapshots aid diagnosis; they are not a
continuous native area trajectory or a recording of every post-issue collision.

## Integration and cost

The same shared exit plan and `Candidate.PathConstraint` reach selected preflight and issue, in TB/shared RT.
Three selected route checks, native ground projection/linecast and native movement-distance quotes remain.
There is no full path per hint, extra world-area scan, physics overlap query, actor/context simulation,
collider/transform creation or mutation, or cross-decision geometry cache. Snapshots are confined to active
exit searches and selected validations. Ordinary attacks/moves add no snapshot/path queries.

Common `FindExit` destinations serve the existing legacy/mounted callers too; the fresh route constraint
remains on shared unmounted escape candidates, as in exit1. General pursuit, target commitment, retreat,
swarm-specific exit search and stuck-walk cancellation are outside this repair. The pre-existing global
shape-based path-danger approximation is documented but not silently replaced.

## Verification and limits

22 game-independent cases exercise body-surface clearance, long/rounded capsules, box faces/edges/corners,
negative/nonfinite inputs, arrival clearance and overflow. 28 native audit checks exercise the compiled
volume implementation (including a rotated/translated box, collider-centre offset and different logical/actual
body size), remaining-shape delegation, snapshot/reference routes, fresh selected callbacks and native
membership calls. No actor, Unity transform/collider query, native physics scene or command executes offline.
In particular, binding an engine-bound snapshot method outside Unity cannot be substituted for a runtime test.
Exact added signatures are resolved against the supplied original assemblies. The final receipt records the
full inherited suite, actual-reference Release and distinct blueprint audit scopes.

Native physics floating-point/contact boundaries, a collider/world update lag, future actor placement above
the sampled navmesh and an area/obstacle moving after issue still require gameplay. Extra stopping clearance
is conservative but does not guarantee all exits. A live pursuing aura can legitimately cover a character
again after a correct exit. No claim of eliminating every smoke repeat or improving Unity frame time is made.

## TB / shared real-time replay

1. Repeat a Brimorak fight. Retain startup MVID, `[AreaExit]`, `[Walk]`, command and combat records. A quoted
   destination must clear the full native sphere footprint plus arrival tolerance and pass the actual path budget.
2. In a stationary harmful cylinder, test a small and enlarged companion at its edge and on a slope. Confirm
   native membership/effects end after arrival; do not infer this only from `result=Success`.
3. Test a rotated box if available, overlapping areas, invalid/unavailable bounds and an area ending/moving
   between selection and issue. No paid move should rely on unknown geometry or an obsolete escape benefit.
4. A registered-but-geometrically-clear snapshot should produce no new Leave candidate. Compare observed
   sphere centre and actor position for update lag; a moving attached aura may still recapture the actor.
5. Verify step/full attacks, ordinary ranged attacks/casts, existing swarm escapes and both combat modes.
   Compare the same party/fight/settings/warm-up for performance; thresholds and net heap are not exact timings.
