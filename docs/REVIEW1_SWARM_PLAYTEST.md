# v0.3.50 review1: swarm and mixed-combat review

Task `codex/review1-swarm-playtest` starts from development `d466f384`, with played PR #53 head `593d6c6`
included as an explicit dependency (local merge `ecdb7fd`). The author subsequently authorized open-PR merges;
PR #53 is merged at development `1946786`, which this task also incorporates.
The author requests a stable release package; manifest stays 0.3.50. [Input identities and counts](REVIEW1_SWARM_INPUTS.json).

All five AI logs identify review1 MVID `25818a75-297f-47de-aea3-5cf93d0a9d09` at row 3. They contain twelve
combat starts/ends, 248 measured TB and 1,507 RT decisions. The author confirms ToyBox spawns and rests **between
encounters**; mid-combat rests are not reported here. Native/Isekai reinforcements can also change enemy counts.
Rows below refer to the named original file, 1-based. Raw logs/native code/game DLLs remain private.

## Findings

| Area | Evidence | Conclusion |
| --- | --- | --- |
| Earlier swarm protection correction | RT swarm 1018/1629 and TB swarm 497/687/693/707/718 show attack defenses at 0/round while retaining total swarm danger. RT mixed 310/333/351/1805 retains an ordinary-enemy share. | The split is exercised. No Mirror Image cast appears, so this does not verify image-specific live behavior or every numerical forecast. |
| Nonlethal swarm exits | TB swarm 608–621: nauseated Valmot can only move, HP98%, eight rats, danger62.9; a found exit scores0 and the turn ends. RT swarm 424/472/525/1675/2145/2229 has similar zero-value exits. | Code prices only reduced chance to drop, omitting the HP reserve term used elsewhere. Add bounded credit for known direct HP avoided, preserving every movement/opportunity/risk check. Absolute HP and the exact corrected in-game decision are not reconstructed from HP%. |
| Invocation transfers | 59 issues: 58 acted successes, one unacted interrupt. RT mixed fight3 alone has21 issues; Dexterity tier1 moves Aeiluvetar→unnamed ally→Camellia→Aeiluvetar, tier2 Valmot→Aeiluvetar→unnamed ally. | Do not declare spam fully resolved. Current source-based settling works, but its existing gain≥5 override permits early moves. Not every transfer is wasteful: danger, enemies, beneficiaries and durations change. Correct remaining defensive comparison errors rather than ban valid placements/transfers. |
| Held defensive value | RT mixed 1936/2249/2359/2458 show changing AC-bearing appraisals and holder deductions. `ArmorGain` receives an already-capped current chance; held prevention is priced as another improvement below current protected danger. | Preserve native raw AB/AC thresholds and compare held removal against current danger. Static native/scalar cases establish the defect; logs cannot classify every individual transfer as a bad cast. |
| Ulbrig's abilities | RT swarm 344/352/415 explicitly withholds Death from Above/Grabber. Across five AI logs, zero leap/grab/release issues; Ulbrig uses forms, ordinary attacks/moves and items. | These are actual support gaps. Add a narrow native leap route. Grabber remains unsupported because safe automatic grapple maintenance/release is a separate two-sided decision. |
| Other movement / actions | Four explicit swarm-exit issues (RT swarm431; RT mixed1687/1695/1710). Two RT swarm stuck cancellations (561/1973) recover into other actions. RT mixed2677–2683 rejects Grease's path and casts healing. | No new generic path/cancellation defect established. Native acted failures/interrupts are not automatically dispatcher failures. Eleven successful Dazzling Displays and two retrospective empty-target notices do not establish a blanket targeting bug. |

## Corrections

### Defensive buff comparisons

`DefenceOf` retains the already-read strongest ordinary enemy's weapon quote alongside `AttackIncoming`.
Modelled integer AC changes use `CombatMath.HitProbability` at that quote's raw AB/AC endpoints. Incoming
protection adds AC; retaining a held buff prevents the loss when AC is removed. This distinguishes a real
5%→10% hit change from a bonus still beyond the natural limit, and avoids valuing extra AC while an enemy
remains above 95%. The unchanged concealment factor cancels in the relative ratio; only attack-preventable
incoming is scaled. A held loss can exceed current incoming and is not clamped to it.

Held defensive urgency is `-PreventionValue(-kept, …, currentDanger, …)`: removal raises danger from the current
protected state. This corrects the counterfactual; it does not always increase the old value. Adding/removing
the same protection at the same two endpoints now agrees. Total incoming plus attached harm remains the urgency
baseline, while attached/automatic damage gains no AC credit. Fractional/no-quote cases retain their fallback.
Strongest-enemy, stat-link/max-Dex/conditional-effect and HP-effect approximations remain explicit; this is not
a full hypothetical actor rebuild. No extra native rule, attack, target scan or context query is introduced.

### Direct-HP value of leaving swarms

Swarm exits now add the existing `0.3 × HP avoided` reserve value to their original drop/productivity benefit,
once, for the next round. The shared `HpReservePerPoint` preserves the prior prevention formula. New credit
requires recognized cast-style swarm metadata, exact native HP-only damage actions and a known native-mitigated
per-victim quote. General `Harm`/`Extra`, poison, stat damage, drain, attached effects and unquoted aura estimates
cannot buy HP reserve value. Both positions include all eligible cast swarms, so entering another source offsets
the saving. Existing coverage, outside-walk share and calibration are reused; there is no path query per hint.

The final stop multiplier, AoO/area/ally costs, offensive opportunity loss, kept attacks, step/move availability,
exit geometry, arrival clearance, selected-path budget and final dispatcher checks remain. Healthy units still
compare the exit against useful attacks. The correction does not promise every actor must retreat from a swarm.

### Native Death from Above

The inspected `AbilityGriffinAttack` owns a custom leap/translocation and queues an ordinary `IsCharge`
`UnitAttack` with ignored cooldown afterwards. It is not an ordinary walking Charge. Native Pounce determines
the full routine, with conservative action availability; TB costs a full round, RT a standard action.
The seven inspected native components, action/type/range and inspected timing must match. Native availability,
target/min/max range, unmounted/movement/combat restrictions and current-origin range/LoS remain authority.
Pure Mage, held touch/ranged spell payloads, materials/items/fusion and unsupported providers are guarded.

The planner binds the native read-only `GetEndPoint` helper once. Per evaluation it attempts at most **eight
landings and four weapon quotes** among native-eligible targets. It rejects unsafe areas, swarms, body overlap
(including the snapped target), and landings outside the queued attack's reach. Current weapon estimates compete
with other actions, with landing exposure, effects on allies and the inspected charge AC penalty priced. Native
flight suppresses traversal AoOs, so no ordinary walk/interception cost or artificial charge multiplier is used.

The actual runtime `BlueprintRoot.SystemMechanics.ChargeBuff` selects its child through live facts. A strict
structural reader handles the inspected ordinary/Cavalier branches and AC effect. The separately named
`GriffonDeathFromAboveBuff` is not what the native leap applies. Prospective positive charge AB and speculative
charge-only procs/rend are left uncredited: the six-second buff starts before the flight, and a full routine can
outlast its remaining duration. Existing weapon/critical approximations remain.

Immediately before issue, mode/action/ability/target, source and target positions, exact native landing, occupancy,
hazards, reach, charge state and pounce eligibility are checked again. After native command initialization,
`ShouldUnitApproach` must be false; the command still passes through `CommandDispatcher.Run`. No actor move,
forced-path substitute, direct attack event or status/controllability write is added. Drivers already wait for
the native command/queue; early no-effect verification excludes this post-cast attack.

Two absent charge graphs come from the user-provided BubblePrints 2.7.0x database as explicit stock fixtures.
Three overlapping graphs retain the original user exports (their parsed data matches stock). Originals are
unchanged. The runtime reader uses the actual live root; offline fixtures do not prove every installed patch.
The native leap/charge audit requires its input graphs rather than silently skipping them.

### Grabber remains a documented support gap

Native grapple initiation retains rotation prohibition and applies `CantMove`, `CantUseStandardActions` and
`DisableAttacksOfOpportunity` to Ulbrig, plus attack/Dexterity penalties. The victim initially loses movement/
AoOs; pin and automatic CMB maintenance/natural attacks follow their own rules. Movement releases the grapple.
A generic on-hit control bonus would ignore Ulbrig's lost actions and could cause dysfunction. These logs contain
no grapple/maintain/release sequence. Automatic Grabber needs a paired initiation/maintenance/release model and
focused manual TB/RT traces; it is not enabled speculatively here.

## Performance and verification limits

The twelve summaries total 23,095ms TB and 143,076ms RT. Of125 slow rows, ten overlap GC and only two build new
profiles. The review1 setup diagnostics work: extra1090 has Tuvel1440ms, abilities1312.1ms, discovery15.2ms,
pre-score7.2ms; RT mixed1184 abilities1094.5ms vs setup19.3+7.7ms. Most remaining cost is dense-scene scoring,
not discovery/profile construction. Heap deltas are live-heap changes, not allocations; these encounters are
not a matched performance benchmark. No new broad cache, kit pruning, frame cap or claimed speedup is justified.

New unit and native scalar/actual-graph/compiled-route checks cover AC thresholds, symmetric removal, direct-HP
eligibility/escape value and bounded native leap eligibility/dispatch/lifecycle. Full Release and inherited
checks must pass on committed source before packaging. Actor-free checks do not execute live casts, actual
landing queries, movement or Unity timing. The known raw Achaekek enum load mismatch remains separately reported.

Replay both modes: controlled Invocation beneficiaries near and beyond hit limits; failed casts/independent tiers;
healthy and nauseated swarm victims with profitable/unsafe exits; pure HP, immunity, poison/attached harm and
mixed enemies; Ulbrig in griffon form at valid/too-close/too-far distances, blocked/dangerous landings, single/
pounce attacks, native queue completion and ordinary alternatives. Keep saved roster/books/encounter comparable
for timings and note spawns/rests separately. New corrected behavior remains unplayed until that replay.

Checked source `8efbb0e`, [receipt](competence/REVIEW1_SWARM_CHECKS.json) and [v0.3.50 stable ZIP](../release/CompanionAI_Pathfinder_v0.3.50.zip), startup MVID `45ab748a-b0e9-4db9-b62a-3de2f4eff6fb`. 760 unit + 27 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 16/16 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
