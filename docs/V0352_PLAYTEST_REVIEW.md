# v0.3.51 live playtest review and stable v0.3.52 follow-up

The two uploads match packaged **v0.3.51**, MVID `e31e626d-9c4b-4536-8b26-63433d3c7425`.
The author confirms **swarm spawns during encounters** and **rests between encounters**, requests a
logging audit, and selects **v0.3.52 for the next stable package**. Development base `00ab4e52` includes
merged PR #55; no open PR existed at task start. Standing necessary-improvement/movement and development
integration authorization applies. Public main/tag/GitHub Release publication is separate.

[Input hashes, physical-row convention and identities](V0352_PLAYTEST_INPUTS.json).
[Updated Claude master prompt](CLAUDE_MASTER_PROMPT_V0351.md).
[Short GitHub kickoff prompt](CLAUDE_KICKOFF_PROMPT.md).

## Broad findings

Six fights contain **205 measured TB / 746 RT decisions**; retained tactics summaries are 204 TB / 295 RT.
All **343 issued commands have matching terminal rows**. No AI Control exception is observed. Interrupts
with acted=True, including all five form casts, are not automatically failed actions.

| Area | Original log evidence | Finding |
| --- | --- | --- |
| Death from Above | AI 467/1501/2358 admit 3/1/1 safe candidates. 468/1503/2359 compare them with ordinary attacks. Zero leap issues. Other refusals identify other bodies, hazards, native targeting or range/sight. | The new diagnostics establish that landing guards do not exclude every target. There is a concrete comparison gap: ordinary attacks can optimize owned attack modes, the leap cannot. Correct that gap; preserve safety and require actual execution replay. |
| Profane Invocation | 18 issues by fight 1/0/0/6/8/3, 17 acted successes, one unacted interrupt (2648/2770). Actual GUID sequence is retained in the input receipt. | No cast cap/settling retune is justified by counts. Within-fight transfers remain: tier-1 Dexterity to Lann R1 (1773), then Tuvel R2 (2032); separate tier-2 Dexterity to Lann (2067). Late tier-1 Strength goes Abrikandilu R4 (4166) to Bloodragers R5 (4284). Marginal benefit still needs independent review. |
| Hidden-only reveal | 998 issues Faerie Fire at the vanished Cultist Conjurer's last location; 1001 acted success; 1002 sees one enemy after the prior zero-enemy decision. | Positive live trace for the hidden-only reveal route. Temporal association is not isolated numerical/causal acceptance of every reveal/concealment case. |
| Stat/level threats | 1745 rates Wyvern x2.0 with Constitution harm; 2019 rates Howling Spectre x2.0 including four negative levels. | Existing beyond-HP prioritization is exercised. No demonstrated new omission of these inspected graphs. |
| Idle approaches | Aeiluvetar 4225–4227: paths 13.2 m exceed one move 9.1 m, then idle. Abrikandilu 4323–4327: three endpoints 0.5 m short, then idle. Valmot 4337–4342: offered advance is refused because the native walk goes nowhere. | Raw positive scores are not executable alternatives. Some advance generation exists, so do not describe it as wholly absent. More actual gate/settings/movement evidence is needed; add a bounded existing-state witness without changing movement policy or path budgets. |
| Dazzling Display | No-effect observations 1237/1332 and 3931/4057; existing backoff stops each ability after its second no-effect cast in the fight. | Four observed full-round no-effect casts deserve investigation. Native recipient geometry/LoS, failed skill checks, immunity and event coverage are competing hypotheses; not a proven immunity bug. Preserve that distinction in the Claude prompt. |
| Group healing | 13 Channel Heal issues. Combat 5865 onward shows healing multiple companions, including Seelah/Camellia/Tuvel/the Magus. | Full HP on the caster does not make a group heal pointless. This trace does not establish every use optimal, but no blanket self-HP gate is appropriate. |
| Summons | Abrikandilu Sunder Armor at 4008/4009, Vermlek adoption at 4185 and both TB/RT service traces. Two summons in fight 6. | Basic service/action/turn-end coverage expands. Idle refused-path cases remain; diverse kits/grapple/large-pack support is not established. Cohort rows 4590/4592 repeat the shared 997 checks / 27.3 ms control total; count it once. |
| External warnings | Eleven WrathPatches OwnerBlueprint warnings: IncendiaryRunes, ExplosiveRunes, one ChannelPositiveHarm/Isekai context. BuffIt2TheLimit also logs combat hooks. | Record foreign provenance. Do not repair other mods' blueprints or attribute these warnings to an AI Control exception without tracing ownership/native behavior. They may matter to later numerical audits. |

No bomb issue or `[DmgCheck]` mismatch and no named `burn consequences unknown` line occurs. Missing samples
do not establish correctness. Spawns and different encounters prevent a controlled speed comparison.

## Correction: give the native leap the shared attack-mode comparison

At AI 2359 the planner compares a walking attack **28.2**, including **Fighting Defensively off +4.2**, with
a leap **24.5** lacking that option. Native `AbilityGriffinAttack.Deliver` queues an ordinary `UnitAttack`
with `IsCharge`; the recognized attack-mode effects also apply to that attack. Merely forcing the leap to
win would conceal the missing comparison. v0.3.52 instead extends the existing chooser to the strictly
recognized leap route.

Each admitted candidate passes its current native quote, landing and original offense factor directly to
the shared chooser while the quote is still cached. `RepriceGriffonModes` requires the exact actor/target/full
key, frame and routine identity; stale/evicted quotes return unavailable. Mode alternatives use the existing
`ExpectedWeaponLine` scalar kernel and captured concealment, native per-weapon lines, DR/physical factors,
criticals and separate payloads. They do not request another native own-weapon quote, activate a buff or
preview an actor. The returned comparison quote contains the fields consumed by `DamageValue`; it is not a
new Magus/held-spell delivery plan.

Mode defense uses the **landing** and **combined native charge-armor plus mode delta**. Marginal improvement
is the difference between states at that same baseline, retaining the original charge cost. Equal AC totals
are memoized within that candidate. Existing per-decision defensive routines/position caches remain. Ordinary
weapon damage pricing is unchanged. The shared **at most 32 mode sets**, management setting, player ownership,
blocklists, group constraints, within-round locks, availability and hysteresis remain. Existing ability-mode
dispatch applies a chosen set only after the cast command is accepted; refused commands spend no toggle.

The leap retains **8 endpoint attempts / 4 own-weapon quotes**, native ability action costs, current Pounce
forecast, body/area/swarm/reach and final issue guards. Positive charge AB/procs remain uncredited because
their lifetime begins before flight. No ordinary walking path is substituted for flight. Failed mode appraisal
restores the original candidate and reports once rather than losing a valid native estimate.

The old 24.5/28.2 comparison motivates the correction; rounded log values do not prove the exact counterfactual
winner. Actual leap/queued-attack completion still requires gameplay. This is not a promise to prioritize it.

### Shared combat-mode AC and automatic swarm damage

Static review found a remaining inconsistency: `DefenseValue`, used by combat modes and the leap's charge-AC
cost, iterates total incoming parts and can use a swarm's nominal weapon quote to price AC. Recognized swarm
casts/round effects deal damage directly, without an attack roll. The buff-defense reader already excludes
those known sources from attack mitigation. Apply the same `SwarmSpaceOf` source exclusion before weapon
quotes in the shared mode-defense loop. Total danger stays in the denominator and drop-risk baseline; ordinary
attack shares retain their existing AC calculations. This also avoids a redundant nominal swarm weapon query.
It is a native-contract consistency repair, not proof that a particular observed stance was wrong. Unknown
graphs are not blanket-excluded by creature name/type. Regress both ordinary attacks and leap landings in
pure/mixed swarm fights: AC-only modes must not claim to stop the known automatic share.

## Logging audit and optimizations

The uploaded AI log has 4,608 physical lines / 937,531 bytes. Major AI Control categories by UTF-8 line bytes:
Tactics 705 rows / 179,636 bytes; Cmd 688 / 104,092; Walk 330 / 99,529; Coverage 150 / 65,370;
HitChance 510 / 54,888. These counts exclude newline bytes and other-mod rows. High-volume command/path/
coverage evidence is useful and is retained. The actual logger is UMM's logger; no buffering/thread/file
replacement is introduced.

Concrete avoidable work:

- The identical +4 SpellPen line appears **261 times**, across five fights. The already-computed bonus is now
  observed by actor ID: first positive value and changed values report, duplicates do not. A silent zero
  observation allows a later positive change to report again. The calculation itself still runs normally.
- RT repeats Prone and no-reachable-enemy messages while the state persists. Report first/changed blocked
  reasons and unavailable-enemy states; ready/available observations permit a later recurrence to report.
  This observes only states seen at the existing checks, not unobserved transitions between ticks.
- All three diagnostic memos hold only string IDs and scalar/string values, at most **128 identities each**,
  lazily reset by fight epoch. Overflow suppresses new identities until the next epoch; it does not clear the
  table and cause a repeat burst. Existing tracked identities still report changes. Off-mode calls do not
  populate the memos. No game calculation or actor reference is cached by them.
- Six HitChance formatting sites now check verbosity before names/result strings are built. Quiet RT decision
  summaries return before allocating a StringBuilder/ranking/formatting options that were previously discarded.
  Selected actions and TB turn summaries keep the existing output policy.

Command issue/terminal/rejection rows, exceptions/errors, build MVID, selected action/value, calibration,
performance, cast-outcome backoff and first native refusal evidence remain. New idle-recovery witnesses are
verbose-only, lazily formatted through the existing bounded ReportOnce memo, once per actor/fight/reason;
they include existing movement/step/weapon-policy/range/role/unreachable state, with **no extra path query**.
No exact allocation, milliseconds saved or final log-size reduction is claimed without replay: legitimate
state changes can produce new messages, and the trace does not contain every silent state transition.

## Performance and outstanding work

34 slow rows include **six cleanup markers**, at 396/2192/2902/3197/3449/3820. Four roughly one-second stage
spikes have large negative heap deltas; elapsed time includes the collector pause. Do not conclude that
`Command — Fall` itself takes 1089 ms or discovery alone causes the 971 ms spike from these samples.
Warm scoring still matters: Tuvel 2881 takes 649 ms, abilities 585.4, 157 scored, discovery/filter 11.8 and
pre-score 5.1 included; heap +40,968 KB is **live heap**, not exact allocation. Logging changes reduce known
redundancy without claiming to solve native-context/scoring allocation or collector pauses.

Claude's priorities remain summons, Invocation marginal utility and paired grapple maintenance/release.
The updated master prompt adds current evidence and the Dazzling Display/foreign-warning questions, distinguishes
landing admission from execution, and keeps older v0.3.50 evidence labelled historical.

## Verification and focused gameplay

Game-independent tests cover diagnostic transitions, resets, overflow and the quiet-summary policy. New native
audit fixtures exercise the actual compiled cached-routine mode kernel, mixed weapons/iteratives/caps/DR/
concealment/criticals/payloads, cache/query/ownership/dispatch routes and compiled logging guards. Required
affliction/swarm/Cling/charge inputs remain mandatory; the raw enum-load failure remains separately named.
The initial new logging audit exposed an offline verifier limitation: generic field/method tokens were resolved
without their type/method arguments. `SaveBoostCheck.ReferencedMembers` now supplies the actual generic context;
this repairs inspection of the compiled memo without changing gameplay source or weakening the check.
Exact committed source/build/package results follow validation below.

Replay both modes with the new MVID:

1. Compare an open leap with a walking attack while Fighting Defensively/Power Attack are on/off; verify shared
   mode labels, actual native landing/queued attacks, normal action use and completion. Check AI mode management
   off, player-owned/blocked modes, and a refused/changed landing: no unauthorized switch or extra walking cast.
2. Repeat the blocked idle cases with verbose logging. Read the new gate witness; preserve native path/movement
   limits rather than expecting an unvalidated positive option to execute.
3. With debug off, retain chosen actions/commands/errors/MVID and slow summaries; with debug on, first/changed
   SpellPen/blocked/wait states report, recovery and later recurrence report, steady-state repeats stay quiet.
   Compare matched quiet and dense-kit fights before assigning a timing/size improvement.
4. Regress hidden-only Faerie Fire, group healing, summon TB/RT handback/idle completion, Invocation tiers and
   failed-cast rollback. Record spawns; rest between encounters. Grapple remains unsupported pending paired work.

v0.3.52 changes are not present in these played v0.3.51 logs. Stable packaging is author-requested; automated
validation and corrected gameplay acceptance remain distinct.
