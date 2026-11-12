# Review5: do not re-create an already-refused recovery move

This focused task starts at PR #47 package `cee7d48`, which includes integrated development `44b224f` and the
paired form-reference correction. The [broad review](REVIEW5_PLAYTEST_REVIEW.md) and
[input manifest](REVIEW5_PLAYTEST_INPUTS.json) identify the played review5 logs and confirmed combat spawns/rests.
It maintains the previously authorized bounded advance-recovery behavior; it adds no pursuit or route policy.

## Reproduction and cause

RT 2041–2044 rejects Seelah's Move+Attack, Advance and Aid Another approaches to Vermlek. The selected-preparation
budget reaches three. At 2045 the idle recovery builder creates a new Advance candidate. RT 2050–2051 shows that
same plan rejected at issue because its path still enters Corrosive Bomb. Native safety worked; the reattempt was
wasted work. The subsequent decision waits after another existing alternative fails.

`SelectedApproachFilter` removes refused candidate **objects**. The lazy factory can create another object with
the same existing `move:adv:<target>` key. Its eligibility checks include the driver's failure memory, but selected
preparation deliberately does not globally blacklist these plans/targets. That leaves no decision-local identity
check on the newly created fallback. See `TacticalEngine.ApproachValidation.PreflightMainCandidates` and
`IdleAdvanceCandidates`, `SelectedApproachFilter.Apply`, and the final `CommandDispatcher.Run` safety path.

## Change

The bounded filter optionally accepts a recovery identity selector. Actual failed plain moves contribute their
existing plan keys to a lazily allocated ordinal set, capped by the unchanged **three** selected checks. A newly
created recovery candidate with that same key is withheld before selection/issue. No additional native check is
needed to establish that it was already refused in this decision.

The production selector recognizes exactly the existing plain-move shape (`Kind=Move`, no `NeedsMove` or
`MoveFirst`) and returns its current key; it creates no new identity string. Other plans for the same enemy remain
eligible. An omitted, untested option is not a failed path. Missing keys are not global failures. The set dies with
the invocation, so the next decision can reconsider the same plan after geometry or world state changes.

Only the recovery output is filtered. Existing useful main/quick/weak/cantrip actions, the offensive-refusal
trigger, native action/status/Pure Mage/RT-cooldown gates, three-target pricing and final dispatcher safety remain.
The small existing fallback pricing pass may still occur; this removes re-issue/path preparation of a known failed
plan, not all pricing work. Fresh fallback moves still receive normal issue validation. There is no total-native-
path-query promise: the cap of three refers to selected preparation, as before.

Four unit scenarios cover a newly allocated same-key fallback after three actual refusals; an unchecked plan;
a fresh invocation with changed readiness; and different/unidentified recovery plans. Four added actor-free native
candidate checks bind the actual plain-move selector (Idle Advance now 27 total). These test scheduling and identity,
not scene actors, native paths or measured Unity performance. Existing form-reference 14, step 13 and other native
regressions remain separate.

## Replay and boundaries

In both modes, block a selected approach with an area/body/long path and provide (a) no safe plain move,
(b) a safe alternative, and (c) useful in-place/quick actions. When a plain advance already fails preflight, it
must not immediately become an issued recovery move under the same key. A later decision can try it again after
the path changes. Cases where early step/full intent suppressed ordinary advances should still recover a new
safe valuable advance. Verify no forced movement, no fourth selected check and unchanged hazard/AoO refusal.

The same supplied native assembly and inspected path/endpoint contracts documented in
[the approved recovery design](REJECTED_APPROACH_ADVANCE.md) apply. No dispatcher, native pathfinder, target
commitment, retreat, stuck cancellation, hazard cost, movement budget or action value changes. No full-decision
retry or per-turn/per-fight unreachable mark. The expected benefit is fewer redundant native issue/path attempts;
matched gameplay timing remains pending. Manifest stays 0.3.30; Claude reviews/integrates, no main/release changes.
