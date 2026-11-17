# Review of PRs #86–89

**Playtest update:** review2 has now been played; see [REVIEW2_PLAYTEST.md](REVIEW2_PLAYTEST.md) for results and the
review2fix1 package. The original review/receipt below remains the historical pre-playtest record.

17 November 2026, Codex. The four follow-ups and their inherited stack were reviewed against the supplied native
assemblies. **Corrections are implemented; nothing is merged or played.** The new review2 package below is the one to
play. It supersedes cleanse1 for this review; historical packages and receipts have not been replaced.
[Draft correction PR #90](https://github.com/recovribox-svg/AIControll-FACC/pull/90) targets development and follows #81–89.

The largest remaining issue is F1's prospective engagement assumption. A legal attack does not establish native 3D
engagement. That replacement, watch-capacity policy and other larger changes are proposals, not silently added policy.

## Scope and identity

| Follow-up | PR / reviewed head | Individual range |
| --- | --- | --- |
| F1: engagement | #86 / `227e018` | `27e4196..227e018` |
| F2: marginal disruption | #87 / `686af0a` | `227e018..686af0a` |
| Burst paths, control, watch | #88 / `b5a471f` | `686af0a..b5a471f` |
| F3: cleanse horizon | #89 / `32943a6` | `b5a471f..32943a6` |

Task branch `codex/review-86-89` starts at **`32943a665556d83d231340b7a12e86f9410aa891`**. All four PRs were draft,
open and unmerged when inspected. Development remains `ad63ee8`. The correction PR targets
`claude/dreamy-pasteur-b9h3rn` and depends on **#81 → #82 → #83 → #84 → #85 → #86 → #87 → #88 → #89**.
No Claude/development/main branch, tag or release is updated. This work changes valuation and the already approved burst
forecast, not movement orders, pathfinding, target commitment, retreat or stuck cancellation.

Checked source **`f0b609715427ed9d1422df484d234993545640f8`**, startup MVID **`0c600255-e6b8-4788-a5bc-4e0a9ad5f7e9`**. UMM **0.3.60** is unchanged.
Package: [review2 ZIP](../test-builds/AI-Control-FACC-v0.3.60-dev-review2-f0b6097-UMM-test.zip?raw=true),
[identity](../test-builds/AI-Control-FACC-v0.3.60-dev-review2-f0b6097-UMM-test.identity.json), [checksum](../test-builds/AI-Control-FACC-v0.3.60-dev-review2-f0b6097-UMM-test.sha256).
ZIP SHA-256 **`3e8e46b97994ad21281e7e06ef67096a4b21021a13689284316f75ee7bad9b4b`**.
[Checks](competence/REVIEW_86_89_CHECKS.json) and [compiler/reference provenance](competence/REVIEW_86_89_BUILD_PROVENANCE.json)
identify the frozen checked DLL. Later documentation commits do not rebuild or substitute it.

Read with [the follow-up plan](REVIEW_FOLLOWUPS_PLAN.md), [F1](FLANK_ENGAGEMENT.md), [F2](DISRUPT_MARGINAL.md),
[burst follow-ups](BURST_PATH_FORECAST.md), [F3](CLEANSE_HORIZON.md), [the previous review](REVIEW_81_84.md) and
[TACTICAL_AI §198](TACTICAL_AI.md). This review corrects several native-rule claims in those original descriptions.

## Ranked findings and corrections

### F2-D1 — high: later pending attacks borrowed the second attack's statistics

`OthersStill` iterated the native command's pending indices, but every index after the first reused `SecondHitChance`
and `SecondPerHit`. A late iterative or a different hand could therefore be credited with an earlier attack's better
chance to break the spell. That reduced the next companion's incentive to respond to a caster.

Native evidence: `UnitAttack.AllAttacks`, `GetAttackIndex`, `ProvideNextAttack` and `TryToReorder`. The next index can
already be three or later, and animation configuration can reorder the native hand list. An animation index is not an
index into the engine's first/second summary.

**Fixed in `5b6f3ca`:** read at most two pending native attacks and match their weapon object and
`AttackBonusPenalty` to lines of the just-quoted, frame-cached routine. The existing damage kernel supplies that line's
hit probability and damage, including the estimate's defence multiplier. An absent/stale line is unknown and earns no
allied break credit; it never borrows another attack. No extra routine, rule, allocation or actor probe is introduced.
See [CombatMath.OrderedHits.cs](../Tactics/CombatMath.OrderedHits.cs).

Tests: a lower late iterative leaves more marginal value for the responder. Six production/native-layout audit checks
cover a third iterative, an off-hand, unknown and stale lines, the defence multiplier and the production route.

### F2-D2 — high: sequence existence was treated as sequence selection; issued windows were ignored

The original predicate considered a nonempty animation sequence table sufficient to chain the quoted attacks. Native
`TryToReorder` instead checks action count, action/style compatibility and a random acceptance against `Probability`.
Native `OnTick` uses the sequence shortcut only when **both adjacent attacks are sequential and the previous animation
handle is released**; bow style is its separate shortcut. A sequence list establishes none of that.

An issued attack could also have the `IsIgnoreCooldown` window stored by `ConfigureAnimations` (at least three seconds),
while the forecast always used six. These errors can put a second hit on the wrong side of a spell's deadline in either
direction.

**Fixed in `2230bf7`:** future commands claim the known bow shortcut only; other future sequence timing remains an
explicit ordinary-window estimate. Issued commands read their configured `m_AnimationsDuration` through one cached,
read-only FieldRef and inspect each actual transition's selected/released flags. Invalid/unavailable timing proves no
allied hit. `ConfigureAnimations`, `TryToReorder`, animation creation and RNG are never executed by scoring.
See [TacticalEngine.Disrupt.cs](../Tactics/TacticalEngine.Disrupt.cs).

Tests: the three-second/six-second deadline witness and eight additional audit checks, including executing the production
transition/window readers on inert native command layouts. The native sequence-selection RNG is inspected, not run.
These checks do not play an animation or prove future release latency.

### F2-D3 — medium: queued attacks counted before native readiness, or with the wrong delivery

An issued command is not necessarily ready to start. Native
`UnitCommandController.ShouldStartCommand` checks action state, hand animation, equipment updating, movement waits and
cooldowns; `UnitCombatState.HasCooldownForCommand` can be blocked by a separate running move. A command's
`TimeSinceStart` starts after approach and native start, not when the order was issued. Running iteratives have already
paid their action cooldown. The original quotation did not distinguish these cases.

The original code also reused the first weapon's ranged flag for other hands and used its reach rather than the issued
command's `ApproachRadius` for approach time. `UnitAttack.GetApproachRadius` includes both bodies, and native `OnTick`
reads the pending hand's weapon style.

**Fixed in `0fc6e01`, `8c5c3f8`, `49a27b5`, `f0b6097`:** queued attacks account for their actual standard cooldown,
overlapping it with approach (`max`, not a sum). Cheap native blockers with unknown completion time supply no timely
hit credit. Running routines do not wait for their own cooldown a second time. Each pending hand supplies its own
weapon delivery; own-action estimates carry the second weapon explicitly. Issued approach uses the command's radius;
missile flight uses 3D distance. This reads state only and never starts, reserves, replaces or interrupts a command.

Tests: cooldown and overlap deadline witnesses, a mixed-hand delivery witness and native/production-route checks for
readiness, cooldown overlap, equipment/hand/movement gates, per-hand delivery and the issued approach radius. Native
actor transitions are not executed offline. Mount coordination, future LOS/path time and future cancellation are still
approximations; this is not a complete native command simulator.

### A-D1 — medium: a nearby path end did not identify the command's requested route

`AddNativePath` accepted any path whose terminal was within `ApproachRadius + 1 m` of the command's aim. An old or
different route could pass that test. The tolerance came from stuck-walk detection; it is not native request identity.

Native evidence: `UnitMovementAgent.PathTo` compares destinations, while `ABPath.originalEndPoint` retains the original
requested destination. Real-time long-range approach uses `XPath` (derived from `ABPath`) with `LosPathCondition`; its
vector-path terminal may be short of that destination. Checking only the terminal also confuses a valid shortened
route with a different request.

**Fixed in `96ef867`:** match the original request to the command's current approach point with native Vector3 equality,
then validate the path terminal using native mechanics distance and the actual radius. Read the path once. Unknown,
forced, partial, mismatched or oversized paths retain the existing straight fallback; no path search or command change.
Same-destination native path reuse is allowed. This identifies the request, not a unique command instance.

Tests: six production/native-layout audit checks cover an unrelated nearby end, a matching ABPath, a shortened XPath,
an out-of-range terminal, native height distance and production routing. No Seeker, path pool or navmesh is run.

### A-D2 — medium: the stop used an extra 10% margin and omitted native height

The route was trimmed at `ApproachRadius × 0.9` using XZ distance. Native `IsUnitCloseEnough` uses the full radius and
`GeometryUtils.SqrMechanicsDistance`, plus required sight. Its distance ignores height through 1.5 m; above that it is
`max(XZ², Y²/4)`. A route on another floor can be horizontally close without being distance-eligible. The smaller radius
also forecast travel past the point at which a clear-sight native command would stop.

**Fixed in `e0eee11`:** intersect each segment with the actual horizontal circle and native vertical band, keeping its
earliest distance-eligible point. Double intermediates prevent cancellation on long segments with small radii. The
straight fallback retains its previous endpoint model. **LOS remains unproved and can delay the true stop**; this is
distance correctness, not a claim that the entire native stop is simulated.

Tests: existing route tests were updated; four new tests cover separate floors, descent into the height band, native
height forgiveness and a long/small-radius segment. Three audit fixtures execute the production stop glue and compare
with the actual native scalar geometry method. Bounded work remains at most 64 existing path points.

### A-D3 — medium: a frame-cached roster could retain a removed or torn-down actor

The watch gathered live/in-combat actors once per frame, then checked only `Descriptor.State.IsDead` before using a
retained actor. A summon removed later in the same frame could still be priced, and an incomplete descriptor could
abort that re-check through its outer exception path.

**Fixed in `be5d594`:** cheaply revalidate retained `IsInGame`, descriptor/state, death and combat membership; retain the
caster exception for a caster not yet in combat. This does not scan the scene again. Four production/native-layout
checks cover missing references and the route. New actors after the frame snapshot still wait until the next roster;
that remaining latency is explicit below.

### F1-D1 — low correction, high remaining design issue: invented range slack and an overstated proof

The direction is correct: native `UnitCombatState.IsEngage(target)` reads **this unit's `m_EngagedUnits`**. `Engage`
updates that collection and the other unit's `EngagedBy`. It must not be reversed. Native concealment's stock
distance-only gates remove concealment while closing **under fixed facts and weapon context**; the broad future-state
claim needs that qualification. Range-type concealment can use the current attack-rule weapon or the first weapon,
and later facts are not held fixed by the engine.

The fallback added 0.01 m to threat range without a native rule for it. Both ranges are native values; an additional
centimetre changes the predicate rather than repairing demonstrated conversion noise.

**Fixed/qualified in `37b6e78`:** remove the tolerance; qualify comments and audit language. Add a sub-centimetre unit
witness, audit the direction of `IsEngage`, and execute the native mechanics-distance witness below.
**The prospective engagement model itself is retained as a proposal, not declared solved.**

### F3-D1 — low: unknown suppression time could revive a known-expired debuff

`ActiveShare` returned 1 for unknown suppression before checking known zero/negative life or horizon. Unknown
suppression does not create future activity after a known end. Ordinary positive-life unknowns still need their
conservative fallback, as explicitly designed.

**Fixed in `4d23a52`:** for a suppressed buff, honor known end/horizon first, both in the pure rule and before scanning
live suppressors. Unsuppressed buffs keep F3's legacy share of 1. Three new theory cases and an audit witness cover
expired/zero-horizon unknowns versus a positive-life unknown. No ordinary weight or denominator is retuned.

## What was verified and retained

- **F2 marginal arithmetic is sound for its stated independent-hit model.**
  `P(broken by anyone) − P(broken by others) = othersSurvive × ownBreak`, regardless of ordering. At most two pending
  weapon attacks per ally are considered; no ally is reserved for a future action. Own attack-mode repricing from #85
  is retained. No double-counting defect in that multiplication was found.
- **A3 shares the existing control rule.** `AllyControl` is filled from `wouldBeCaught` and the same cost used for
  current allies. Its fusion guards, removal/control outcomes, immunity and shared-HD eligibility route remain in
  place. Sleep's enemy-only component protection is retained; mixed-fusion damage is assessed separately. There is no
  new blanket friendly-control veto. This is a decision-time table, with the limitations below.
- **F3 is the described relative discount.** Permanent suppressors give zero, positive-life unknown suppression keeps
  the conservative fallback, and the dispel fallback skips suppression lasting through the horizon. Unsuppressed short
  debuffs intentionally retain legacy full cleanse value. Attached-hazard value remains separate.
- **Logging and lifetime:** the disruption and burst diagnostic caps remain 64 per fight, plus one burst summary.
  Watch/refusal state clears with the fight. No unbounded new log or scene query was introduced. The summary counters
  distinguish watched/peak/re-check/path/fallback/eviction/unwatched work; actual large-party timings need gameplay.
- The fixes do not contradict #85's independent fusion damage, per-mode disruption repricing, native aura context,
  suppression filtering or shared partner-damage kernel. Full fusion and summon detail comparisons are below.

## Proposals and limits left in the code

Ordered by practical priority. These are not fixes claimed complete, and their unmeasured impact is not presented as
gameplay evidence. No additional movement/commitment policy is authorized by this report.

### R1 — high: an origin-aware engagement quote (author decision required)

The F1 fallback still infers future engagement from range compatibility and current concealment. Native
`UnitAttack.GetApproachRadius` includes both bodies and attacking range, but native attack readiness uses **mechanics
distance plus sight**; `IsAttackOfOpportunityReach` uses **strict 3D distance plus LOS**, then engagement rejects total
concealment. A dispatcher-legal attack does not prove the latter.

Concrete native counterexample: attacker/target at the same XZ and 4 m apart vertically, with total reach 2.5 m.
Native mechanics squared distance is 4 (inside 6.25); 3D squared distance is 16 (outside). Equal weapon/threat ranges do
not reconcile those metrics. The current audit executes that scalar witness; it does not place actors on two floors.

This matters to party attacks **and enemy-danger, defence, held-buff and leap quotes** sharing routine caches. Approve a
single explicit origin/assumption API first: distinguish current native engagement from projected arrival; carry the
projected origin into threat reach/LOS/concealment and routine/cache identity, with a bounded shared read per
actor/target/origin and unknowns reported honestly. Do not add per-candidate raycasts or reuse a projected routine for
current danger. Include strict-boundary, differing bodies/reaches, height, invisibility/blindsight, TTT and both-mode
native/play tests. This is larger than the supported range-slack correction and may affect movement-related danger.

### R2 — medium: watch overflow also owns TB landing waits and pending-danger registration

`MakeRoom` preserves every pre-act watch, as approved, and evicts the earliest `LandsAt` post-act entry. **That entry may
still be waiting for a TB burst to land.** `PendingLanding` reads the same list; a spot left in `AoeDangerAnalyzer` is not
the same as retaining the TB landing wait. If all 64 entries are pre-act, `Track` returns before the RT
`RegisterPendingBurst` call, so the newly unwatched burst also lacks that A4 registration. The documented “TB unaffected”
claim is therefore too broad at saturation, even though the forecast/cancel logic is RT-only.

This is a verified capacity-path consequence, not proof the author's party ever reaches 64 watches. Keep the approved
policy until a replacement is chosen. Proposal: separate minimal post-act/TB landing metadata from expensive pre-act
watches, register pending danger independently of watch admission, define a bounded overflow policy for each, and pin
cap/expiry/failed-cast cases in tests. Use the new fight-summary peak/eviction/unwatched counts before choosing sizes.
Changing a safety/turn-wait contract deserves a design decision, not an arbitrary larger cap in a review fix.

### R3 — medium: freshness of ally-control and damage snapshots

`AllyControl` preserves the decision's rule and price for allies known then. `DamageTo` caches a damage estimate,
including zero, for each ally for the whole watch. An ally gaining/losing immunity or a relevant caster change between
re-checks can stale either snapshot. A newly created ally also has no decision-time control entry. No current playtest
in this task proves how often that happens.

Proposal: select a real invalidation contract or a bounded selected-watch refresh through the same guard/pool/fusion
predicate, preserving the aim and decision context. Include zero-damage-then-vulnerable, new summon, loss of immunity,
Sleep-only ally protection and independent fused damage. Do not invent a generic BuffCount key: resource/state/context
changes are not all buff-count changes. Likewise, the once-per-frame roster deliberately delays later additions until
next frame; removals are now revalidated, but this is not an instantaneous scene snapshot.

### R4 — medium: timing remains a forecast, not the native attack timeline

First swing (0.6 s), chained spacing (0.7 s), missile speed (30 m/s) and approach time remain estimates. An already
late animation can still delay an apparently due hit; a selected but unreleased sequence can release before its
ordinary fallback estimate. Native `OnAction` can deliver additional attacks when animation handles are absent, and
`OnStart` can rebuild a queued attack's list. Already-launched missiles are not represented once `GetAttackIndex` has
advanced; other allies' damaging spells are not read. Mounted synchronization, curved approach, future LOS, cancellation
and retargeting are not simulated. Expected damage substitutes for a damage distribution; kill/disruption overlap is
not a full joint terminal outcome. The pre-mode diagnostic tag can differ from the repriced value, an older limit.

Collect actual cast/attack/delivery timelines before calibration or expansion. A read-only bounded per-target record
could cover launched projectiles, but a generic “has projectile” boolean is insufficient for target, damage and arrival.
Never call native animation selection or roll damage to score an alternative. No reservation/target-commitment change
is proposed as part of F2's arithmetic correction.

### R5 — medium: path and started-command forecast limits

Request identity is now checked, but no future replan, avoidance, acceleration, collision or LOS is forecast. The native
stop needs sight; this review models only earliest distance eligibility. Route time is horizontal arc length at current
speed, with interpolated Y. Unknown/forced/mismatched paths still use the straight fallback. `WalkingCommand` does not
return already-started pursuing attacks, although native `UnitAttack.CanMoveAfterStart` allows them to move. Changing
that shared helper can affect other movement/spot readers: propose an isolated burst-only read and approval first.
Do not advertise path forecasts as guarantees of ally safety.

### R6 — performance proposals, profiling needed before redesign

- A3 currently fills a control table for **each ground-burst candidate**, iterating uncounted allies and invoking the
  existing native-aware guard/immunity/control logic, sometimes allocating a dictionary per candidate. The final table
  is useful only for the chosen command. Proposal: prepare it after selection through a shared predicate with the
  exact chosen aim/pool context. Do not retain whole tactical contexts or reuse an aim-independent pool result. This
  needs equivalence tests and actual candidate/allocation timings; no measured regression is asserted here.
- F3's live suppressor search can repeat across removable buffs, actions and facts. Its division is cheap; the scan is
  not necessarily so. A decision/preview-lifetime memo with real state invalidation is preferable to a persistent actor
  cache if profiling supports it. No arbitrary cross-frame cache is introduced.
- F2 remains one allies pass per casting enemy per decision, at most two pending hits per ally, using cached routines.
  The new per-hit line match is bounded by that routine's attacks; it does not trigger new rules. Burst routes remain
  at most 64 points and one scene roster per frame. Offline checks are not a Unity performance benchmark.

### R7 — low: relative cleanse share is not an absolute-active-time dispel model

For a three-round horizon, two rounds of debuff life and one round of suppression, F3 gives share `1/2`. Multiplying
the legacy three-round cleanse value yields 1.5 rounds' base worth; `DispelValue.ActiveRounds` gives one actual active
round. The definition in #89 intentionally preserves the unsuppressed model, so this is a design difference, not a
wrong division repaired with a new constant. If absolute remaining-time valuation is desired, redesign cleanse and
dispel together, including attached hazards and simultaneous removal of a suppressor and its suppressed debuff. The
existing conservative arrival rule still gives zero to a buff suppressed on arrival even if it would resume soon.

TTT's conditional fortification/rerolls also remain the earlier D-R2 limitation; no no-roll provider is added here.
Shared weak cast-info state is read sequentially in the present main-thread decision route. No reentrant defect was
established; future nested evaluation should use decision-local pending-hit state rather than assuming the same cache
is safe under arbitrary interleaving.

## Validation

Untouched #89 `32943a6` was built and checked before edits on the **same available inputs** as the reviewed build.
Its local rebuilt MVID is `86591a18-906d-43d9-8512-ff454837841f`; that is not claimed byte-identical to Claude's
historical cleanse1 DLL. Both baseline and final use SDK **8.0.426**, Roslyn **4.11.0-3.25569.22**. Compiler, build
properties, reference hashes, native identities and exact Source Link revision are recorded in provenance.

| Check | Untouched #89 | Reviewed build |
| --- | --- | --- |
| Real-reference Release | 0 warnings / errors | 0 warnings / errors |
| Unit tests | 1,032 | **1,044**, 0 failed/skipped |
| Inventory / ledger / supplement scripts | 27 / 18 / 3 | 27 / 18 / 3 |
| ProfileAudit A / B / C / D | 48 / 28 / 34 / 32 | **61 / 48 / 35 / 34**, 0 failures/skips |
| Native ToggleCosts with Psionics / TomeBurn / ToyBoxSpend | 98 / 14 / 10 | 98 / 14 / 10 |
| Supplementary Magus | 44 | 44 |
| Native signatures versus #89 | baseline | **7 added / 3 removed**, all resolved |

The strict corpus loads **44,640** graphs / **4,808** abilities; full stock adds the missing-only historical
607-blueprint supplement and native Magus fixtures, loading **45,248** / **4,849**. Both have zero load/reader errors
and no prerequisite skips. This count is four above Claude's reported 45,244; four native Magus fixtures are available
here. The comparison is against our untouched #89 build, not a claim to reproduce a historical report from counts alone.
The 607-GUID/name/type/raw-hash manifest exactly matches its historical receipt; export graphs are not overwritten.

**Every report row** was compared for strict and full-stock inputs. Only the four A–D header lines differ: increased
check counts, plus A/B wording accurately describing the new inert native-layout fixtures. All other lines, both
complete Fusion coverage files (**8,709 accepted / 15,494 configurations**) and the **summon-kit detail** are
byte-identical. Exact before/after headers and artifact hashes are in the receipt. Summon detail equality is not inferred
from its summary counts. SigDump includes added **and removed** references, each verified against the corresponding DLL.

The unmodified raw-export negative check still fails on the known `AchaekekSacredWeaponFeature` enum mismatch
(`SawtoothSaber` versus native `SawtoothSabre`): exit 1, one failed input. The explicitly compatible audit corpus excludes
that input; it is not rewritten or reported as a complete raw-input pass. Earlier 45,251-graph receipts had three
additional unidentified graphs; this review does not reproduce that older entire corpus or claim its historical hash.
The current #89 comparison uses exactly the same inputs on both sides.

Audit execution uses private runnable Unity/formatting shims. Production scalar readers and inert command/weapon/path
layouts are executed; **no live actor setup, native command/cast/animation, navmesh, dice, Harmony combat or Unity timing
is executed**. The deployable ZIP contains only the frozen, real-reference-built mod DLL and unchanged Info.json, with
no shims, game/mod binaries, decompiles, blueprint dumps or raw logs. No cross-machine byte-reproduction or in-game
stability claim is made. This is a development test, not a release.

## Author replay: use review2 only

Install **`AI-Control-FACC-v0.3.60-dev-review2-f0b6097-UMM-test.zip`**, UMM **0.3.60**; confirm startup **module `0c600255`** before each session.
Keep both **Log.txt and combatLog.txt**, and say which fights used spawns, rests or manual orders. Rest between fights
for the ordinary baseline; if intentionally testing a mid-combat rest, identify it separately. No manual movement is
required to make the engine choose an ability. Spell/shape/Grabber settings from the earlier fusion/grapple replay remain
under engine control unless deliberately testing an override.

1. **RT disruption, ordinary and sequenced/bow attacks.** Fight a caster with a melee unit having at least three
   iteratives, then repeat with a bow user and a creature using its native sequence. Compare actual attack timestamps
   and cast deadlines; do not assume every natural attack is sequenced. First hits should remain useful; a late
   iterative should not be given an earlier hand's chance. The second responder's added break value should drop only
   for allies with an order on that caster and a timely quoted hit. Also capture a responder while another ally is
   still cooling down or changing equipment/animation. No command should be reassigned or reserved by this valuation.
2. **TB disruption regression.** Repeat an ordinary multiattack/caster encounter. No RT disruption bonus or `[Disrupt]`
   line should appear. Turns, attacks and existing action spending should proceed normally.
3. **RT burst paths and stops.** Use an eligible delayed ground burst in open space, then near a wall/bend and on a
   slope or different level. Check lead/recipient choices and any `along its path` lines. A recently changed goal may
   use the straight fallback. Verify allies walking in before the cast acts trigger the existing friendly-fire rule;
   no extra movement/path order should be introduced. Height/LOS is a deliberate diagnostic case, not a guarantee of
   perfect future placement. Send the `[Burst] fight summary` line after each fight.
4. **RT friendly control and fusion.** Exercise an ordinary harmful control burst and a mixed Sleep + damaging burst,
   with a companion/summon walking into range after selection. Sleep's component should continue to spare allies while
   independent damage can still threaten them; an ally immune to damage can still matter when a different native
   control component can affect it. Compare Avoid Friendly Fire on/off. Control/immunity changes after selection and
   newly summoned allies are useful evidence for R3; they are not claimed fixed here.
5. **TB delayed landing / ordinary burst regression.** Fire a delayed burst, including mixed-fusion damage. The caster's
   turn should retain the ordinary pending-landing wait. We have not stress-played 64 simultaneous watches; report any
   eviction/unwatched counter. R2 explains why saturation still needs a policy decision.
6. **TB and RT flanking.** Add a second melee attacker against a visible enemy, then an invisible enemy without a
   matching concealment counter, then with See Invisibility/appropriate counter. Check native combat-roll flank bonuses
   against `[flanks]` valuation. If available, compare reach hands and a vertical engagement: F1's remaining geometry
   assumption is deliberately not asserted correct. Keep the original partner sneak/critical/fortification regressions.
7. **TB and RT suppression.** A harmful buff suppressed beyond the fight horizon should earn no cleanse share; a
   suppressor ending soon can earn a partial share; ordinary unsuppressed cleanse remains unchanged. Permanent and
   positive-life unknown suppressors retain the documented fallback. The zero-life edge is pinned offline because a
   live expired buff can disappear before a decision.

After these targeted cases, play an ordinary mixed encounter and include the full logs, not just named events. Replay
the unchanged A–D scenarios from [the earlier review](REVIEW_81_84.md) as applicable. No merge or release should be
inferred from offline green checks; remaining design choices and actual gameplay evidence still matter.
