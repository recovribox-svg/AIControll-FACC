# Native reachable endpoints for area escapes

The author approved this second movement proposal on 12 November 2026. This branch starts at PR #36 / step1
packaging `ac7d0df`, including magus1/caster2 and fetched development `1f58150`. Step/full-attack validation stays
in its own prerequisite PR. Manifest 0.3.30; Claude reviews/integrates; no main/development push, merge or release.

## Evidence and native contract

The caster1 [review](CASTER1_PLAYTEST_REVIEW.md) records repeated short smoke exits (RT 1899–1971). Several
requested points retain the actor's Y while inspected native ground endpoints differ by 1.8–2.8 m. These are
inspection routes, not recordings of the eventual execution route. The logs do not prove every repeat has one cause.

Native `ObstacleAnalyzer.IsPointInsideNavMesh` compares X/Z only. Its `GetNearestNode` gives a walkable surface;
the hint shortcut can return the supplied position/Y unchanged, so projection must query without that hint.
`TraceAlongNavmesh` uses `NavmeshBase.Linecast` from the nearest node. Native `AreaEffectView.Contains(unit)`
uses the actual position and body radius; `ScriptZoneCylinder.Contains` checks local height and radial/body
overlap. Thus a point at the wrong height can disagree with the position navigation returns.
[Native identities/contracts](competence/MOVEMENT_EXIT_NATIVE.json) fingerprint the supplied assemblies and smoke graph.

## Resulting behaviour

The existing five-ring/sixteen-direction exit search now starts on the actor's native walkable surface. Each
hint is projected through the native nearest-surface query, constrained to the same graph/connected area, and
checked by the same native linecast as the existing navigation helper. It uses the returned point and verifies
clearance both before and after tracing. Existing 2 m projection / 1 m trace tolerances remain bounded; projected
points must stay within the search distance. No global position-validity helper or native pathfinder is changed.

Actual live area shapes receive the unit's native body radius plus the exit command's existing **0.15 m** stopping
radius. A candidate must clear every current source area with that margin; an unknown live shape cannot prove
an exit. Expired areas do not block clearance.
Existing entering-area checks, target-preservation preference, exit-value/AoO/drop competition and search ranking remain.

In the shared engine, an unmounted area-exit candidate additionally carries a decision-local native-path constraint.
Selected preflight and actual issue check the returned endpoint and total route distance against the already-read
movement budget. The 9 m search radius is not mistaken for that action budget: an affordable detour can remain.
A route cut short inside the area, an unaffordable detour or a missing/invalid route cannot earn a verified escape.
A changed live area is read again at issue. The constraint is not retained by game commands or across decisions.

RT uses the existing independent native path inspection; actual execution keeps native steering. That selected
escape is checked even when no nearby-area/AoO gate would otherwise request a route. Missing path answers or
validation exceptions refuse that escape; unrelated moves keep their existing fallback. Native commands with
no remaining approach check current position. TB temporary paths keep their existing claim cleanup.

## Work bounds and scope

At most the existing **80 hints** are projected; the start node is reused for native linecasts. There is no full
path query per hint. Clearance loops allocate no per-area predicate closures. Full route checks use the existing
three-selected-plan preflight budget and the final dispatcher check, reusing each phase's route for risk/clearance.
Ordinary candidates have no constraint and add no route query. No actors, cast contexts or simulated Rulebook events
are created for projection/clearance. Matched Unity time/allocation acceptance remains pending.

The common exit search also supplies the legacy emergency escape's destination. The new per-candidate route
constraint applies to the shared engine's unmounted escapes. Mounted delegation and legacy dispatch are not expanded.
Moving obstacles, areas or an interruption after issue can change live movement; these require gameplay replay.
The bounded search can still find no legal exit. Target commitment, retreat scoring, general approach finding,
swarm-specific exit geometry and stuck cancellation remain outside this patch.

## Validation and replay

Offline checks cover native path samples cut short, an intermediate outside point with an inside endpoint, returned
ground Y, affordable/unaffordable detours, missing/nonfinite data, issue-time clearance changes, the actual supplied
Brimorak cylinder graph and compiled projection/constraint wiring. They do not run actors, transforms, native
navmesh queries or commands. Final receipt/test identity records these and inherited suites, actual-reference
Release, compatible/supplementary ProfileAudit and the original corpus's distinct enum failure.

Replay TB and shared RT with Brimorak smoke/Stinking Cloud, body overlap at the edge, large/small companions, sloping
ground/steps, an ally blocking the endpoint, a route detour longer than one available move and two overlapping areas.
Confirm a successful exit ends outside the live native shape, not just at an unwalkable sampled point. A still-inside
or unavailable route should be withheld/refused with an `exit path ...` reason, leaving an eligible alternative.
Compare commanded and inspected ground coordinates in existing verbose walk logs; capture actual post-walk position
and area membership to diagnose remaining repeats. Check expired/moving areas and effects that linger after leaving.
Regress valid five-foot full attacks, native Magus, Pure Mage, Ranged martials and ordinary movement. Keep startup MVID
and matched party/fight/spell setup with performance logs. Gameplay remains pending.
