# outcome2 playtest review and pooled-fusion area safety

16 November 2026, Codex. Development base `acb1003`; played package source `04a7b9f`, startup module
`2f9ef9ef-8d7a-4574-9b98-fc8383b45bee`. The supplied AI log matches the cumulative outcome2 receipt.
The author confirms spawning, rests **between fights**, and Grabber left to the engine. Both modes appear.
Raw logs remain private. Row numbers use physical LF lines, retaining embedded CR characters in the combat file.

[Input identities and accounting](competence/OUTCOME2_PLAYTEST_INPUTS.json),
[derived Invocation ledger](competence/OUTCOME2_INVOCATION_LEDGER.json).

## Findings

| Observation | Evidence and limit |
| --- | --- |
| Eleven fights; 740 issues, 735 matching terminals, no unmatched terminal | 728 issues are in combat, with 724 terminals. Four open summon traces have nearby leave-combat/cancellation activity; the last out-of-combat Soul Power issue is at EOF. Do not invent terminal results. |
| No recorded AI exception | Five native `Fail`/DidAct commands are separate from interrupts. Four failures have native concentration evidence; Incubus's failure remains unexplained. |
| Fusion use expands in play | 30 issued fusion abilities, 18 DidAct, including three Snowball/Sleep, one Explosive Runes/Sleep and two no-root-save Explosive Runes/Frigid Touch. No saved Incendiary Runes/Frigid Touch cast, actual leap or Coup issue. |
| Summon control is exercised | Brimorak, Babau, Schir, Quasit, Incubus, Cambion, Dretch and Vermlek account for 181 issues, 131 DidAct; breath, spells, attacks and movement. This is not acceptance of every summon kit. |
| Grapple releases work in RT | Wenduag's five Release Grapple issues/terminals at 2462/2464, 2516/2517, 5983/5985, 6201/6202 and 6228/6229. New attacks intervene between repeated releases; they are not five retries of one failed release. TB release remains unplayed. |
| Invocation: 52 issues, 50 DidAct | 18 first placements, 21 settled moves, eight holder-unseen, two overrides, one sibling replacement and two unapplied issues. The identified overrides have net gains 7.7 and 6.9, above the existing five-point threshold. No known same-stat or settling violation is established. |
| Path diagnostics are bounded in this pair | 24 detailed native refusals, alongside actual rejection/terminal records. Different fights/geometry prevent a matched speedup or percentage-reduction claim. |

### Friendly damage is present

AI 1453 issues Explosive Runes/Sleep at Vescavor Swarm; terminal 1645 reports DidAct after 8.7 seconds. The aim
diagnostic (1448) refuses nine other centres for friendly fire, but accepts Vescavor Swarm `[35A23]` at 171.6.
Combat physical rows 5185, 5198, 5211 and 5224 record arcan's Force damage to Seelah (15), Wenduag (26), Bandit (29)
and Tripper (32). The wolf subsequently dies; do not infer its entire death was caused by this one hit.

AI 7181/7272 records an Explosive Runes/Frigid Touch cast that walks 3.2 m and acts after 4.3 seconds. Combat
29566-29578 records 28 damage to Wenduag and 39 to Bandit from its two damage components; AI 7285 independently
notes Bandit's 39-HP loss. These are damage observations, not evidence that the Sleep component sleeps allies.

Current safety uses geometry at appraisal plus existing ally walks. Native unit-targeted burst centres can move
before delivery; a new ally order can also start after appraisal. For the Sleep cast, Bandit and Wenduag's logged
attacks move 0.0 m. The logs do not snapshot all actors at both appraisal and delivery, so **do not attribute all
these hits to the repair below, or promise it prevents all real-time friendly damage**.

### Confirmed F7 regression and correction

F7 uses the native selector/current positions to forecast a shared Hit Dice pool. `ScoreAbility` then excluded
**every** supported pool from `AlliesWalkingInto`. That is appropriate for the ordinary pool's ordered control
forecast; a fusion also has an independent damaging payload. The pool's presence must not disable that payload's
existing walking-in ally safety. This source-level regression is confirmed independently of the precise movements
in the recorded hit.

`ForecastWalkingAreaAllies` restores the existing walking-in check for broad-targeted fusions even when their pool
is supported. Ordinary Sleep and enemy-only delivery retain their old selection. Pool eligibility, ordering, dice,
recipient debits, metamagic, source order and native Sleep target/effect rules are unchanged. The added walking
recipients are not inserted into the native pool or assigned future control credit. Existing damage valuation,
immunities, Selective behavior and Avoid Friendly Fire decide the harm normally.

Both modes use the same score path; the existing walking reader returns immediately in TB. This adds no independent path/world/forecast pass or persistent actor cache. It reuses the existing per-decision
body/command snapshot and bounded walk sampling on the newly restored F7 path. Newly recognized walking
recipients undergo the existing damage/immunity valuation and can add those per-recipient forecasts. It is an additional safety check, not a claim of
zero extra work or a measured Unity speedup. Eight ProfileAudit checks invoke the actual policy on real profiles and
verify the compiled route; they do not pretend to execute native actors or walks.

### Other decisions and limits

- Four failures have native concentration evidence: Quasit at combat 22097 (14 vs 15), arcan at 23112
  (14 vs 16; AI 6023), Drow Magus at 24161 (13 vs 16), Seelah at 29419 (15 vs 20; AI 7264). Incubus's AI 2606
  `Fail` has no established cause in this pair. Do not convert native failure or interruption into proof of a bad
  command. Concentration risk already participates in valuation.
- The Invocation ledger's three cross-recipient chains and two same-final-coverage alternatives need earlier
  values/timing, current holders and availability. They are not standalone waste verdicts. No cast cap or new
  assignment rule is justified by the identified overrides and these incomplete alternatives.
- A no-reach report at AI 1646 is in tension with combat 5317, which names damage to Vescavor Swarm `[35A23]`.
  Several identical creature names and delayed effect/event timing complicate attribution. Only one such miss is
  recorded for this aim; no resulting two-miss block is shown. Retain this discrepancy for a controlled observer/
  execution replay; do not alter failure blocking or claim the fusion did nothing solely from that message.
- Saved Frigid is discovered and valued on both outcomes (AI 350-352), but the two delivered Frigid fusions have
  **no root save**. They do not accept the F4 stagger-on-passed-save case. No automatic-metamagic, Coup, actual leap
  or TB release acceptance is inferred.
- Warm ability work remains substantial: 89 slow decisions, including Orvath 22 (258-1381 ms), arcan 13
  (293-1713), Seelah 11 (254-385), Schir ten (251-1323) and Quasit seven (261-475). Summon kits and the larger
  active party matter. Heap deltas are not allocation counters; first-profile work and collections are distinguished
  in the rows. No larger performance repair follows from this small predicate correction.

## Replay and next work

The checked successor package/receipt is recorded in the latest `test-builds/README.md` entry and
`docs/competence/OUTCOME2_PLAYTEST_CHECKS.json`; manifest is author-requested 0.3.60. New repair requires gameplay testing.

1. RT: with Avoid Friendly Fire on, aim a broad damaging Sleep fusion where an ally already has a walk through
   its burst. Expect another aim or no cast, as for the equivalent non-pool damaging area. The corrected branch
   must retain damage immunity and native enemy-only/Selective rules. Ordinary Sleep should behave as before.
2. TB: ordinary and fused Sleep, existing damage fusions, user blocks and spell-use policy must retain behavior.
   A matched manual native cast is the baseline for mixed-delivery Sleep ally protection; do not add a speculative
   Sleep veto or modify native faction rules.
3. Keep actual saved Frigid (including a passed save), TB release, actual leap and corrected Coup on the replay list.
   Leave Grabber to the engine. Pair startup identity with both logs and note spawn/rest timing.
4. **Important remaining project:** real-time burst safety when the target moves after selection. First verify
   the native target-wrapper/delivery anchor, command start/act/cost and interruption phases, then replay matched
   positions at appraisal, cast start and delivery. Prefer a bounded shared safety snapshot; do not rescore every
   ability, run path searches each tick, globally blacklist a moving enemy, or interrupt a paid cast on a guessed
   refund. A design and native evidence are needed before changing this coordination policy.
