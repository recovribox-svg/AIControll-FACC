# v0.3.50 playtest review and v0.3.51 follow-up

The two uploads are from the **v0.3.50 stable package**, startup MVID
`45ab748a-b0e9-4db9-b62a-3de2f4eff6fb`. The author confirmed **rests between encounters** and
requested the next build be **0.3.51**. Do not reinterpret these as mid-fight rests or v0.3.51 gameplay.
This task starts at development `c92b607df59c8b19176e376fa7f356ec8a8c9733`, after PR #54; no open
PR existed at task start. Necessary movement improvements, stable packaging and development PR integration
are author-authorized. Public main/tag/GitHub Release publication is separate.

[Input hashes, counts and physical-row convention](V0351_PLAYTEST_INPUTS.json).
[Ranked Claude master prompt](CLAUDE_MASTER_PROMPT_V0351.md).
[Native attack evidence](competence/V0351_NATIVE_REVIEW.json).

## Findings across the logs

Four fights contain **203 measured TB and 839 measured RT decisions**. Retained tactics summaries are
203 TB and 324 RT: quiet RT decisions account for the different counts. There are 372 issued-command
rows and no observed AI Control exception row. Native interrupts with `acted=True` do not establish
failed behavior; Griffon Shape's native +4 natural armor is present at combat row 13357.

| Area | Evidence in original AI log unless marked combat | Conclusion and action |
| --- | --- | --- |
| Profane Invocation | 28 issues, distributed 8/5/10/5 by fight; 27 acted successes, one unacted interrupt at 1528–1531 with rollback. | Initial independent tier placements, sibling changes and transfers all occur. Existing same-round/three-round settling and gain overrides remain. Count alone cannot identify waste; detailed utility work is handed to Claude below. |
| Particularly useful Invocation witnesses | Dexterity/Intelligence swap at 2128/2288; Strength tier 1 to Tuvel at 2660, tier 2 to Bloodragers at 2666, tier 1 Charisma at 2691, tier 2 Strength back to Ulbrig at 2698. | Do not conflate identically named tier variants. Last transfer is priced 5.3 net (2696), above the existing early-transfer threshold. That proves the model's stated reason, not actual benefit. |
| Death from Above | 534: 2 endpoint attempts/0 weapon quotes; 1228: 0/0; 1954: 8/0; 3895: 0/0. Zero issued leaps. | Native recognition works, but there is no live execution acceptance. Earlier logging conceals which eligibility/endpoint guard refused the candidate. Add bounded refusal witnesses; preserve the guards. A separate native queued-Pounce forecast mismatch is corrected. |
| Unused turn / approaches | Ulbrig 535–540: three proposed five-foot full attacks each need 3.3 m of native path; budget exhausts; turn ends despite positive *unvalidated* options. | Investigate the lazy plain-advance fallback and actual remaining movement/settings. The log does not prove another route is legal. Do not issue an unchecked attack or raise path-query limits. |
| Other end-turn rows | Bloodragers 1353–1354 has negative priced options; 1564–1569 has already moved before a sight refusal. | These are different cases. Positive raw options in a refusal report are not executable choices. The former end turn has an explained cost; the latter needs remaining-state evidence. |
| Swarms / areas | Recognized native Rat/Vescavor cast consequences; 22 issued Gibber-area exits. Bloodragers 583 sees danger 36 but exit worth 0; Aeiluvetar 3239 sees mixed swarm danger 55.2, exit -6.6. Lann 749 prices a swarm exit 11.2 against a chosen Gibber exit 12.0. | Both movement options and cost competition are active. No dedicated direct-swarm exit was issued. Danger is HP-equivalent, not necessarily avoidable HP: do not invent HP reserve for poison/control/stat damage or remove path/opportunity costs. Zero-benefit move-only case remains a replay question. |
| Drain/stat danger | 1946 ranks Ghoul Stalker x2.0, including Constitution/Dexterity/control; 3034/3846/3882/3917 retain stat-damage threat. | Existing beyond-HP priority is exercised. No demonstrated new omission of the already-supported graph in these rows. This does not establish every enemy/mod graph is understood. |
| Summons | Abrikandilu 1621–1623 issues Sunder Armor after approach; Vermlek 1736–1738 moves/attacks; native turn servicing continues. RT cohorts report 3 driven summons. | Basic takeover/actions are exercised. No observed summon-turn exception/freeze; only a small pack was played. Expansion and paired-state competence remain substantial work. |
| Reveal / damage / burns | Faerie Fire issued 2685, acted success 2721. No bomb issue, `[DmgCheck]` discrepancy or named `burn consequences unknown` line. | Reveal casting is exercised, but the log does not isolate before/after invisibility/concealment for numerical acceptance. Missing samples cannot close bomb, burn-provider or damage-accuracy coverage. |
| Other unsupported effects | Release Grapple at 533; Greater Stunning Barrier at 3703; Shadow Step at 3704; coverage lists Sword Saint / weapon-property modes outside the recognized contracts. | Explicit support boundaries, not evidence that every listed effect should be used in combat. Preserve exclusions until their benefit, cost and lifecycle are modelled. |

## v0.3.51 correction: native queued-Pounce forecast

The prior `GriffonCanPounce(unit, ctx.CanFullAttack)` uses ordinary full-round affordability. Native
`AbilityGriffinAttack.Deliver` instead creates a default `UnitAttack`, sets `IsCharge`, calls
`IgnoreCooldown`, initializes it and places it first in the queue. Its own `InitAttacks` and
`IsFullAttackRestricted` decide the routine. This is established from the supplied real assembly, not
from the absent leap gameplay sample.

The forecast now uses the inspected native branches:

- Pounce is required. Native TB surprise-Pounce precedes the usual staggered/movement restrictions.
- In ordinary TB, ignored cooldown bypasses spent-action history, but restricted movement still forbids full attacks.
- RT retains the current native recent-move restriction; it does not use TB-only move restriction or ordinary full-round availability.
- Staggered forbids full attacks outside the native surprise exception. No speculative credit is given for a
  movement cooldown that might expire during the flight.

`GriffonAttackRules` keeps this narrow scalar decision testable. The adapter reads actual native state; both
pricing and final issue use it. The ability must still afford its actual **full-round TB / standard RT** cost.
No attack, buff, actor-state preview, queue or flight is executed to obtain this scalar forecast. Existing
weapon quotes still supply the weapons, attacks, damage and defenses. No prospective charge-AB/proc credit
is added. Current state can change during flight, which remains a gameplay limit of any forecast.

In plain terms: the AI compares the leap using the attack routine the native follow-up is allowed to make,
rather than treating that follow-up as an ordinary new full-round action. This improves its comparison
with walking, spells and ordinary attacks; it does not guarantee that a leap is useful or safe.

## Diagnostic correction: explain withheld leaps

The old line says no safe attack exists while giving only attempts/quotes. It also logs only the first
such line per ability/fight, even if the refusal later changes. The new line says **none found within the
budget**, distinguishes native-target and range/sight refusals, and names the first sampled endpoint failure:
endpoint query, dangerous area, target body, another body, swarm, missing/nonpositive weapon quote or
weapon reach. Available endpoint witnesses include actor/target/landing coordinates and blocker ID.
An admission note distinguishes candidates that passed safety but lost the final value comparison.

The first sampled refusal is **not** a histogram or proof of the only problem. Reports use the existing bounded
shared `ReportOnce` memo, keyed by reason as well as ability/fight. Messages are built lazily. No node, path,
target, weapon quote or actor is re-queried for logging; no actor reference is retained in a new long-lived
diagnostic cache. The existing **8 endpoint / 4 weapon-quote** limits, body/area/swarm/reach guards, native
approach prohibition, final checks and dispatcher route remain. Admission counts describe this bounded sample.

## Performance review

There are 43 slow-decision rows, none carrying a GC marker. Tuvel 3713 takes 1084 ms: abilities 988.7 ms,
258 abilities scored; discovery/filter 28.9 ms and pre-score checks 9.0 ms are included within that stage.
Heap grows 29,060 KB, including 28,012 KB in abilities. Rows 3876/4217 likewise spend most time in scoring.
These are **live heap deltas, not measured allocations**. Native contexts/damage/defense appraisals are better
profiling targets than assuming profile discovery still dominates. No broad pruning/cache or numerical
speedup claim is justified by these unmatched encounters.

Summon control checks total 7,593 and 102.8 ms over the three cohorts summaries, outside the decision timer.
Those figures do not establish performance for a large summoned pack. First-decision cost is separate from
later decisions; compare matched fights and retained startup identity before an optimization claim.

## Validation and focused replay

Twelve new scalar cases cover native queued-Pounce branches. ProfileAudit extends the actual leap/charge graph
and compiled-native-route checks, including the shared pricing/issue adapter; existing command, safety, query
budget and unrelated-system regressions remain. Final exact checked source/identity and results follow below.
Audits require the missing-only charge fixtures in [REVIEW2_CHARGE_INPUTS.json](competence/REVIEW2_CHARGE_INPUTS.json).
The raw corpus retains its one named enum-load failure; the compatible scope excludes that unchanged file and
the supplementary scope labels stock fixtures. These are separate results, never a raw full-corpus PASS.

Replay the packaged v0.3.51 in both modes, retaining startup MVID:

1. Griffon form, distant ordinary enemy, open landing: see a safe candidate/admission or a specific refusal;
   if chosen, observe the leap, native queued attack, correct action expenditure and normal completion.
2. Native Pounce with no recent movement, then RT recent movement; TB ordinary and surprise turns, restricted
   movement/staggered where the ability itself remains legal. Compare the forecast with actual native attacks.
3. Crowded, Grease/Web and swarm endpoints: retain safety refusals, read the first witness, and confirm ordinary
   weapon/movement/spell choices still work. No fourth selected-path query or ordinary walk is added for the leap.
4. Rest between fights. Record controlled Invocation holders and independent tiers, plus failed-cast rollback.
   Use the master prompt's evidence protocol before changing settling or inventing a cast cap.
5. Repeat the idle Ulbrig and nauseated swarm cases with saved settings, native movement remaining and absolute
   HP/DR/status state. A high raw score is not permission to bypass a native path or hazard refusal.
6. Summon control off/on, three-creature basic pack and TB/RT mode transitions. Large-pack timing and grapple
   maintenance/release are outstanding, not claimed fixed by this build.

New v0.3.51 behavior is not present in these v0.3.50 logs. Stable package preparation is author-requested;
automated validation and author-confirmed corrected gameplay remain distinct.

Checked source `882c082`, [receipt](competence/V0351_PLAYTEST_CHECKS.json) and [v0.3.51 stable ZIP](../release/CompanionAI_Pathfinder_v0.3.51.zip), startup MVID `e31e626d-9c4b-4536-8b26-63433d3c7425`. 772 unit + 27 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 1/1 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
