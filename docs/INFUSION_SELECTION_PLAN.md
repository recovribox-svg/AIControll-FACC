# Infusion selection and Test[2] review

The author widened the task after Test[2]: choose and activate useful infusions,
cover substance effects beyond direct maneuvers, retain budget-laptop performance,
and repair evidenced log defects. Test[2] felt **smooth overall**. Its excerpts
begin mid-session without a startup marker, so binary identity is not independently
proven by these logs.

Branch `codex/kineticist-infusion-selection` starts at remote development
`f51189f` and includes pending PR #7 (`6f9f021`) as an explicit dependency.
PR #7 itself includes PR #6. No integration into main/development is authorized.
This is new work, not restoration of the withdrawn configuration-preview system.

## Evidence and approach

- Previous scope intentionally excluded selecting/starting an infusion. General
  toggle discovery excludes blast modifiers; the kineticist helper only recovers
  when burn blocks all blasts. That explains inactive infusions remaining off.
- Stock substance infusions are native free-command activatables. Effect/cost
  installation is proved by their actual active buff, not an IsOn request alone.
  Duplicated activatable entries must not duplicate scoring or benefits.
- Use the actual buff/trigger blueprint and native context/parameter/rank queries
  for its damage, saves and control. Read hidden wrapper activation actions and
  periodic effects; never run a gameplay action while appraising it. Reuse shared
  condition, reduction, buff/debuff and damage-equivalent valuation helpers.
- Compare individual legal substance changes against already-scored blast plans,
  their delivery risk and the best ordinary alternative. Include retaining/off
  configurations. Bound complete effect comparisons and native cost queries; do
  not multiply all spells, targets, infusions and metakinesis combinations.
- Copy one actual native burn-cost value per sampled blast and adjust only exact,
  constant, recognized infusion modifiers. The native cost object's methods and
  Total own reduction/gather arithmetic. This is a restricted marginal estimate,
  not an arbitrary-fact native preview. Unknown modifiers fail closed; native
  current availability/cost is checked again after the real transition. No fake
  facts, global patches, actor mutation during scoring or cross-decision native
  context/rule cache is introduced.
- Initial activation ordering was corrected after Test[3]: native command
  deduplication is Judgment-specific, so pre-queuing an infusion command fails.
  Request the real toggle inside CommandDispatcher.Run and adopt the single
  native-created command without constructing/reissuing another. Wait for
  completion and actual buff installation before considering a blast. Respect global management,
  player-owned competing toggles, native group capacity and per-round switch locks;
  record displaced states for ordered end-of-fight restoration.
- Form infusions mainly grant already exposed blast variants. Continue valuing
  those variants by the shared engine; do not invent a second form search. Record
  unsupported delivery/effect shapes explicitly rather than assigning generic
  utility credit or assuming full content coverage.

## Log defects and performance

- Camellia's separate touch approach can arrive in range yet be cancelled as stuck;
  recording the parent spell as refused then blocks the useful revive. Verify
  current native follow-up reach/LoS before recording that movement failure.
- Warm RT decisions while standard actions cool down still score unavailable
  healing/buffs. Investigate a narrow early gate, preserving offensive movement,
  held charges and deferred preparation. Do not restore the withdrawn broad slot
  pruning that lost legitimate future actions.
- Investigate low-HP withdraw/advance loops with consistent risk appraisal; only
  change behavior supported by both log and source evidence. Logged GC overlap
  does not establish GC as the sole cause of a long decision.

## Verification and handover

Build Release against supplied real game/UMM references; run all unit tests and
focused pure policy/valuation tests. Validate real infusion blueprints, native API
signatures and supported/unsupported paths with isolated offline tools. Keep game
assemblies, rewritten dependencies and fixtures out of commits/packages. Inspect
actual toggle lifecycle, group ownership, no-thrashing, burn affordability and both
combat modes. Publish a new immutable flat UMM ZIP, MVID/checksums, clear support
matrix and gameplay checklist in a draft PR into development. Compilation and
offline checks are not live gameplay or a performance guarantee.

## Implemented selection and ownership

`TacticalEngine.Infusions.cs` considers up to four already-scored, affordable,
reachable own-blast aims, each with at most eight complete recipients. It compares
off first, then individual learned substance choices; it does not search pairs of
new toggles. The incumbent setup is the ordinary scored blast, and the competing
main action can be a spell, weapon attack, heal or another useful plan. Shared
`ValueModel.InfusionPlanGain` subtracts replaced effects and burn, adds proposed
effects and burn, and pays the blast's existing delivery discount once. A positive
infusion effect alone is insufficient when another main action is better. Existing
free-action thresholds still apply. Up to four zero-value in-reach blast prospects
can expose an SR/material improvement without making those unmodified blasts usable.

The search stops after 24 complete comparisons or at an 8 ms cooperative deadline.
Metadata eligibility is checked before spending a comparison. One native call can
overrun the deadline; active-effect scoring in ordinary blast appraisal is outside
this search limit. These are bounded search choices, not an overall frame-time
promise. More than eight recipients rejects that aim rather than valuing a favorable
prefix. Individual action graphs have a 128-node/10-depth evaluation guard and a
three-round horizon. No new portrait/UI/out-of-combat route or Harmony hook exists.

An installed infusion requires the activatable's actual active, turned-on
`AppliedBuff` owned by this caster. Duplicate blueprint/buff entries are deduplicated.
The initial pre-queue/IsOn ordering was wrong: the native deduplication predicate
only applies to Judgment, not an infusion's own command. **Test[3] exposed five
failed activations.** The corrected `CommandDispatcher.Run` overload requests
native `IsOn` once, then adopts/tracks the exact current/queued command the game
creates, without issuing another. Existing drivers wait for all pending commands. The next ordinary decision reads
actual installed effects and current native affordability; no forced blast commitment
or cached native context/rule crosses decisions. Real transitions invalidate affected
same-frame estimate/condition caches.

Native group capacity/weight are queried. Any protected player toggle in the group
prevents a competing AI change; a group switch is limited to once per combat round.
All displaced AI states are recorded, including a partially refused request.
End-of-combat restoration stops AI-enabled alternatives before restoring original-on
states and preserves player-owned groups. Turning management off hands state back
without new switches/restoration. Existing burn-block recovery remains available
and routes modifier stops through the dispatcher too.

## Effect coverage in the supplied data

The inventory contains **20 distinct substance buffs**, including three Sharpshooter
ones. Eighteen have recognized read-only effect layouts. This count describes data
coverage, not 18 live-tested activations or complete valuation of every native rider.
Unknown subclasses/graphs add no invented utility and explain withheld selection.

| Infusion | Valued effect and boundary |
| --- | --- |
| Bowling | Native CMB/CMD-based Trip odds and shared control value; skips prone/getting-up targets. |
| Pushing | Native Bull Rush odds and existing movement-loss convention; RT push utility remains zero. |
| Burning | Hidden carrier's initial Reflex save, native fire dice/reduction and recurring save/remove survival. |
| Bleeding | Carrier's Fortitude save, native periodic damage/property and short horizon; later healing/removal is not forecast exactly. |
| Chilling | Native Fortitude save and condition/debuff parts; no duplicate existing effect. |
| Synaptic | Native Will save and stagger/debuff parts. |
| Gut-Wrenching | Native Fortitude save and nauseated control/immunity. |
| Entangling | Native nested guard, Reflex save and target control buff. |
| Dazzling | Native Will save and shaken/debuff parts. |
| Flash | Native Will save and blindness control/immunity. |
| Foxfire | Native Will save and target concealment removal, limited to existing removable-miss evidence. |
| Magnetic | Actual matching weapon subcategory / own blast descriptor, weighted by current allies; no extra party spell scan. |
| Grappling | Native no-existing-fact guard and Reflex save/control buff; its null weapon filter accepts non-weapon damage. Break-free control is conservatively credited for one interval. |
| Pure Flame | Marginal actual spell-resistance/immunity bypass; does not bypass attack rolls or unrelated damage reductions. |
| Rare Metal | Selects the real blast's fact-guarded damage branch and native material/reduction queries, without inserting a fact. |
| Vampiric | Future native healer burn saving only with a discovered matching heal and injured ally. A non-stacking caster benefit is counted once, conservatively using the first recipient's delivery chance. |
| Cluster Arrows | Native repeated rank additions, remaining rank headroom and real per-rank AC penalty. |
| Armor-Piercing | Marginal physical DR bypass from the actual native component; energy bypass is not credited. |
| Unraveling | **Automatic selection withheld.** Supplied native dispel sets its DC and resolves gameplay inside its execution path; no safe read-only success query was established. |
| Chain Arrows | **Automatic selection withheld.** Changes secondary delivery/recipients; no supported read-only recipient query was established. |

Direct exposed form variants continue through ordinary discovery, target selection
and scoring. Chain/area secondary forecasting, weapon/blade delivery, shadow blasts,
new form-toggle optimization, metakinesis and Gather Power optimization are outside
this selector. Existing Gather state is retained; increased accepted burn requiring
a different Gather schedule is withheld. No name/GUID priority list selects effects.

Saves use the active/proposed buff's native parameter/rank context, not the blast's
DC or metamagic. Native allowed ability/parent matching and condition checks are
read under that context. No gameplay action or saving throw is executed. Current
damage delivery probability is reused, so half-on-save blasts conservatively
underprice infusion triggers which can also fire on successful-save damage.
Future periodic HP damage is summed before capping against HP left after the blast;
it receives no repeated per-tick kill bonus. Control is discounted for expected
immediate death and native immunity/existing conditions. Retained effects are
repriced after an SR/material/DR change, not left at the old delivery probability.
Conditional save bonuses and other mods' live handlers are not fully forecast.

## Native cost adapter

`KineticInfusionCost` takes one actual `AbilityKineticist.CalculateBurnCost` result
per sampled blast. Only exact constant positive `AddKineticistBurnModifier`
contributions of recognized installed/proposed buffs are replaced. Their actual
applicability lists are read; context-dependent candidate modifiers are rejected.
Specialization, other existing modifiers and actual Gather reduction are retained.
Readonly base fields are reconstructed through the native constructor and native
`Increase`, `Decrease`, `IncreaseGatherPower` and `Total`, without copied burn math.

The native private instance quiver getter is bound once. For ordinary no-wild-talent
blasts, its allowance is applied to the detached native cost's reduction pool;
486 native value-type vectors checked equivalence to the native accepted-cost
payment order. A missing getter or unknown hybrid wild cost retains a conservative
upper bound. Native remaining-burn limits are queried. Additional on-cast burn
components are excluded from planning. This is a restricted marginal estimate:
another mod could add a handler dependent on the presence of the proposed fact.
The post-transition native availability/cost is authoritative. No fact spoofing,
handler patching, direct component invocation or runtime field writes implement it.

## Verification outcome

- Full Release build against the supplied real game/UMM references: zero warnings
  or errors. Pure suite: **154 passed**, zero failed/skipped. New tests cover whole
  plan competition, cost/effect replacement, off choices, periodic HP capping,
  cooperative deadline/comparison bounds and retained cooldown dependencies.
- An isolated local harness loaded 4,097 actual exports, with one known unrelated
  Achaekek load failure. The run is **partial**, with unresolved references retained
  as a data-completeness limit. All 20 native substance buff layouts were checked:
  18 recognized and the two explicit exclusions above. Native constant cost shapes,
  486 reconstruction/quiver vectors, unchanged source cost structs, the private
  quiver binding, real Rare Metal branch and cone metadata passed.
- Ordinary ProfileAudit produced 1,220 rows, zero reader errors; normalized verdict
  and detail rows match the pending PR #7 baseline exactly. This validates ordinary
  reader compatibility, not actor-level selection or coverage of every effect.
- Review checked activation ordering, duplicate ownership, restoration, no round
  thrash, retained-effect repricing and both existing command-driver paths. Native
  actor/rule subscribers, activation outcomes, save DCs with installed mods, burn
  spending, UI selection and frame times remain **not tested in game** here.

See [Test[2] evidence and replay checklist](INFUSION_SELECTION_TEST2_REVIEW.md) for
the touch-approach, cooldown-work and retreat-loop fixes. Keep the PR draft pending
review and both-mode gameplay testing. Use the uniquely identified download in
[test-builds](../test-builds/README.md); metadata remains 0.3.21 until release.

Test[3] correction and native-predicate validation: [review](TEST3_REVIEW.md).
