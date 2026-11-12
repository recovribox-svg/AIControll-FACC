# Advance recovery after rejected attack approaches

The author explicitly authorized this movement repair on 12 November 2026 after the shifter1 log review.
This focused task starts at PR #44 `fe2caa7`, including PR #43 and refreshed development `1f58150`.
[The broad review](SHIFTER1_PLAYTEST_REVIEW.md) and [input manifest](competence/SHIFTER1_PLAYTEST_INPUTS.json)
preserve the log identities and the author's manual enemy-spawn/combat-rest context.

Bandit's R2/R3 turns start with actions available (AI 1359–1365 and 1471–1475). The selected attack approaches
fall short of native endpoint reach (R2 1.6–1.8 m; R3 0.1–2.8 m). Three checks are used, unchecked attack approaches are withheld,
and the turn ends. Positive raw attack scores do not establish reachable attacks. The code gap is more specific:
a proposed step/full attack has `NeedsMove=false`, so early movement generation treats it as an attack from here
and skips ordinary advances. Later rejection removes that attack without creating the suppressed advance.
Keeping already-generated plain moves cannot repair a candidate that was never generated.

After selected preparation actually rejects an **offensive approach**, the bounded scheduler may lazily recover
plain advances if no useful main, weak-weapon fallback, quick action or Pure Mage cantrip remains. A refused heal
or plain move alone cannot initiate combat closing. A valid step, in-place attack, heal or useful quick action
keeps the existing action. There is no retry of the entire decision, spell enumeration or boost coupling.

The factory reuses the existing closing builder: native single-attack quotes, damage/distance ranking, at most
three target approaches, future payoff at 35%, the lost opportunity to attack the enemy already in reach, caster
stand-off range, and current area/swarm/AoO/body-block checks. Existing real-path evidence reprices the candidates.
It retains action budget, native movement permission, prior step, remaining movement, nausea, Pure Mage, policy
and failed-key gates. Real time still waits on genuine in-place offense during its cooldown. An unproven step
is not such an in-place wait.

Only the new fallback candidates are priced and filtered. They precede remaining sub-threshold options so a
first-main selection cannot hide a useful advance. The original three-check selected-approach cap is unchanged;
the lazy plain move is checked when issued through `CommandDispatcher.Run`, as existing unchecked plain moves are.
No rejected attack path is reused as a supposedly safe move: endpoint rejection can occur before hazard checks.
The existing dispatcher exempts `UnitMoveTo` from the attack endpoint-reach test, allowing partial progress,
while still rejecting a path that goes nowhere and enforcing route hazards and opportunity-attack prices.
Recovery errors retain the already-filtered options; unexpected preparation errors also withhold unvalidated steps.
No forced move or guaranteed route is claimed. If safe movement is unavailable or its payoff is below the action
bar, ending the turn remains valid.

Native evidence is the supplied `Assembly-CSharp.dll`, SHA-256
`2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`, MVID
`90a9869c-2792-4c7b-bfb7-5a8b33da7c82`. As documented in [step validation](MOVEMENT_STEP_PREFLIGHT.md),
native `AiAction.ScorePath` checks the actual endpoint with `UnitCommand.IsUnitCloseEnough`; the dispatcher also
checks remaining movement, hazards and priced opportunity attacks. This repair supplies a missing existing-policy
candidate to that path. It changes no native pathfinder, general retreat, commitment or stuck-walk cancellation.

Unit scenarios exercise refusal/budget scheduling, useful-action preservation, a valid step after rejection,
sub-threshold ordering, fresh decisions and recovery with no safe move. Native offline checks invoke actual
candidate/status guards and inspect compiled builder/repricing routes. Neither suite initializes actors or
executes native paths, commands, clocks or Unity timing. The rare recovery does one existing estimate/ranking
pass and at most three approach plans; ordinary valid/no-refusal decisions run no fallback builder. Native
routine/approach caches remain decision-local. This is a bounded work contract, not measured game performance.

Replay TB near allies and corners: valid step/full attack; selected approaches short; all approaches blocked;
no safe route; harmful area or swarm between actor and target; opportunity attacks that outweigh closing;
useful in-place attack, heal or quick action; a previously spent step; no movement left; staggered and nauseated
actors; ranged noncasters and Pure Mage. The recovery note is `[Approach] ... rejected approaches left no useful
action; N plain advance options priced (walk checked at issue)`. A safe valuable advance should replace the idle
turn, while native refusal stays a valid result. Regress shared RT in-place cooldown waiting and selected walks.
Matched party/fight timing and fresh logs are needed to confirm gameplay behavior and performance.

Manifest remains 0.3.30. Claude reviews and integrates; no main/development push, merge or release.

Checked source `45c3b4d`, [receipt](competence/REJECTED_APPROACH_ADVANCE_CHECKS.json) and [review5 test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-review5-45c3b4d-UMM-test.zip), startup MVID `b3359daf-c8e5-4bb6-9245-d6be013a4d15`. 664 unit + 11 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 0/0 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
