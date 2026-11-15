# gaps1 playtest review and Release Grapple repair

15 November 2026 (Europe/London). Development base `6b9be42`; this task is stacked on #70 `d5a1207`
(#69 -> #68 -> #67 -> #66 -> #65) on `codex/gaps1-playtest-review`. Target remains
`claude/dreamy-pasteur-b9h3rn`. Manifest stays 0.3.52. This review does not merge the stack or authorize a release.

## Evidence and scope

The supplied `gaps1-test Log.txt` identifies **gaps1**, source `351b709`, startup MVID
`9aea0261-f5a3-4c3e-a025-502de27a716d`. The author confirms ToyBox spawns, rests **between** fights, and Grabber
left under engine control. Raw logs stay private; [input hashes](competence/GAPS1_PLAYTEST_INPUTS.json) identify
the pair. Row references below count **LF characters** (the combat log also contains 5,035 bare CR characters).
Do not compare encounter lengths, spell replenishment or performance with other builds as a controlled benchmark.

Across nine encounters, all **572 issued commands** (413 RT, 159 TB) have the matching actor, command type and label
at their terminal. There are 421 Success/acted, 76 Interrupt/acted and 75 Interrupt/not-acted terminals, no Fail
terminal, no unmatched or superseded issue. An acted interrupt does not establish a failed spell; Griffon Shape
changes the body during its command. Fight summaries record **261 TB / 1,455 RT measured decisions**, including
quiet waits; only 490 RT decision summaries are printed. Do not use the printed-summary count as the total workload.

One AI Control exception is recorded: Release Grapple's `NullReferenceException` at AI row **2813**. The two
`ChannelPositiveHarm` OwnerBlueprint errors (2718, 4818) are emitted by **WrathPatches**, not this catch. The profile
messages about unread branch conditions are recognition limits, not additional runtime exceptions. The once-only
exception diagnostic cannot establish that the release path failed only once.

## Confirmed defect and repair

Wenduag's attack on Hydra ends acted at 2811. At **2812** the existing hold model says keeping it costs **23.8**:
victim worth 10.5, generic holder worth 28 per round, free natural damage 13.3. The combat log's LF row **18032**
confirms the Hydra grapple. The scorer then returns a positive Release Grapple value with `why = null`.
Candidate construction calls `why.Split('+')` for its pure-buff classification, throws, and discards that ability.
The log says "lets go" even though no release was issued. A potion wins the remaining list at 2817. The file has
**no Release Grapple issue**, so it cannot validate actual release execution or subsequent action recovery.

The repair keeps the existing keep/let-go calculation and half-point margin. Its small scalar offer/reason function
always supplies `release` with a positive offer. Ordinary ability candidate construction now uses a null-safe
pure-buff predicate, matching the existing offensive predicate's missing-reason behavior. Release is neither an
offensive spell nor a pure buff; its own native slot, availability, lists, scheduling and dispatch remain in force.
Its appraisal says **"release offer"**, not that the hold already ended. Only the normal ISSUED/ENDED records establish
execution. The same candidate path serves TB and RT.

There is also a diagnostic discrepancy: the three hold appraisals do not have a matching `holds ...` decision
header. Inspection of the compiled gaps1 DLL shows the nullable reciprocal-reference comparison converts a
`UnitReference` into an entity and resolves its proxy again. The native controller instead compares its stored ID
directly. The header now uses that same direct comparison on both sides, as `ReadHold` already does. This removes
an unnecessary reverse resolution and that potential diagnostic failure. **It does not establish the cause of
the missing live conditions/headers**, or change permission/movement rules. A replay must distinguish a live hold,
its native conditions and their timing; do not force conditions or infer a new movement rule from these rows.

Six unit cases cover the observed negative price, the observed positive prices, the existing margin and its boundary.
Native audit additions exercise the positive offer through the actual offensive/buff classifiers, null/empty and
ordinary buff/form/held-state classifications, and both direct native reciprocal-ID comparisons. The tests do not
instantiate a live unit, execute a release command or simulate Unity.

The runtime repair adds no world scan, rule forecast, path query, long-lived actor/context cache or unbounded
diagnostic. It retains the per-fight/pair/sign appraisal suppression. The scalar reason uses constant strings;
nonempty ordinary pure-buff classification is unchanged. No measured Unity speedup is claimed.

## Other decisions checked

| Area | Evidence | Conclusion / boundary |
| --- | --- | --- |
| Engine-managed Grabber | AI 2271-2274 selects a Brimorak attack with Grabber on; 4400-4403 switches it off; 4497-4500 switches it back on. Combat LF 15049, 15495, 18032, 26982 and other rows record successful grabs. | Initiation is played in both modes, with engine ownership confirmed by the author. This does not validate release. |
| Native maintenance | Combat LF 28149-28173: a successful later roll against Cambion Wizard has **+5 [Grabber]**, followed by 16 natural damage and the victim's death. | The automatic native maintenance/free attack works in this pair. No AI maintenance action is needed. |
| Profitable holds | AI 2303 and 2370 price the Brimorak holds at +29.4 and +19.7. | The existing model prefers keeping these holds. Generic holder cost and missing immobility safety remain the documented limits; this repair does not claim a best-alternative model. |
| Forms and Fury | RT shape issues 1591, 2149, 3886; Fury follows at 1605-1607 and 2163-2164. Material appraisals change as the target/horizon changes. At 4713 the projected form reduces damage 41.9 -> 39.4, appraisal -6.5. | The files no longer establish an unconditional refusal to shift. No TB shape is issued. Form extra facts remain uncredited; do not force a shift because its form has a desirable fact. |
| Cheap late shift | AI 3884-3886: 0.7 for 0.1 future attack rounds, 1.1 defence rounds; only Griffon Aspect 0.9 also appears, and Shape is chosen. | It clears the existing main-action minimum. This is an observed marginal use, not proof of a different native action duration or permission bug. Consider a resource/late-fight policy only with a specified opportunity cost. |
| Leap modes | AI 2624 admits Hydra leap 43.0 with Grabber +32.9. At 2626 a swift aspect is taken first; an ordinary attack is later preferred. Other leap diagnostics give native-target, occupied-landing or hazard refusals. | The repaired mode quote is exercised; **no leap is issued**, so flight/landing/actual grapple are unplayed. Existing four-quote/eight-landing caps remain. |
| Frigid Touch | AI 949/972 describes the common fixed minimum; 973 gives Runes/Frigid Touch **0.40 control round-equivalents**, previously zero. | Recognition/forecast evidence only: neither ordinary nor fused Frigid Touch is issued. The longer critical outcome and saved unconditional-control boundary remain as in #69. |
| Swarm responses | Multiple actual exits are issued (822, 879, 900, 1700, 1724, 1730); incompatible Grabber opening is not claimed on swarms. | No evidence here authorizes changing movement or overriding a blocked landing. Continue crowded/mixed replay. |
| Stun Bomb on a swarm | AI 1608-1609 selects an area stun. Combat LF 11189-11232 records the hit and failed Fortitude saves for Quickspider, Locust and Vescavor swarms, plus another recipient's successful save. | An aimed swarm is not itself proof of a wasted area ability. This is not a single-target grapple immunity rule. |
| High-risk bombs | AI 2080: at 15% HP, Stun Bomb 265.5 with six provoking attacks / 100% forecast down beats heal 71.8; its terminal at 2088 is Interrupt/not-acted. | A concerning risk/reward decision, **not a proven lost-before-delivery scoring error**. Native projectile launch precedes its ranged roll; the engagement controller queues AoOs after the roll. Do not multiply a single already-launched throw by the chance of dying later. The in-log interrupt precedes action, not an observed throw cancelled by its AoOs. A controlled sparse/dense target replay should check the individual control recipients, saves, duration and cost. |
| Invocation / reveal / burn | Invocation ledger: nine fights, zero issued/acted invocation, no flags. No named burn-consequences-unknown line; no issued Faerie Fire or reveal witness. | This test adds no invocation-spam or reveal acceptance. Absence of the burn warning alone is not proof for every kinetic archetype. |

## Performance and follow-ups

There are **70 slow-decision rows** (threshold ~250 ms), median of that subset **329.5 ms**, maximum **1,190 ms**.
The worst ten are heavily affected by collections; at row **3639**, Camellia's 1,190 ms decision reports a collection
and an abilities-stage heap delta of **-258,460 KB**. It would be incorrect to call that number an allocation total,
or to attribute the whole pause to the named Resistance operation. Ordinary warm slow rows still deserve work:
Orvath 2825 spends 328.9 ms in abilities (93 scored); arcan 3631 spends 354.8 ms there (44 scored), and its Rune/Color
Spray quote creates 362 contexts / 108 damage estimates. Those are forecasts for different recipients/aims, not
necessarily duplicate computations.

The next performance slice should first identify exact repeated keys **within a decision** (configuration, recipient,
conditions, outcome, duration and caster modifiers), then reuse scalar/immutable results only where equality is
proved. Preserve world changes between decisions, shared-save outcomes, ally maximum/refresh safety, stateful guards
and damage-reduction handlers. Do not introduce a cross-turn MechanicsContext cache or shrink target/aim coverage as
an unmeasured shortcut. Instrumentation already isolates discovery, native availability, profile reads, scoring and
the sub-stages. No additional logging expansion or gameplay speed claim is made here.

Holder best-alternative cost, holder immobility safety, monster-held allies, form extra facts/KeepSlots, saved Frigid
unconditional control, and actual leap execution remain in the earlier design documents. These logs do not supply
the missing counterfactuals. The bomb case and the missing hold header/conditions should join the next replay;
they are **open questions**, not established reasons to mutate native conditions or movement.

## Validation and next replay

Validation and frozen package identity are recorded after the checked source commit in
`docs/competence/GAPS1_PLAYTEST_CHECKS.json` and `GAPS1_PLAYTEST_BUILD_PROVENANCE.json`. Compilation/offline checks
must remain separate from the supplied **gaps1** gameplay and the **unplayed** repaired package.

Play TB and RT with Grabber left to the engine:

1. Make a grab on a weak living victim while dangerous enemies remain. Observe a **release offer**, then an actual
   Release Grapple ISSUED/ENDED if the offer wins; no `not evaluated` exception. Healing can legitimately win first.
   Confirm native parts/conditions are removed and ordinary actions become available after the free release.
2. Retain a valuable caster/attacker: a positive keep value should not cause a gratuitous release. Look for the
   reciprocal `holds` / `held by` header and actual held/pinned restrictions, not just an appraisal label.
3. In open ground, let an actual leap compete with ordinary attacks. Confirm Grabber mode, landing and native grab;
   leave crowded/hazard landings as regression checks. Do not manually force a leap that the engine refused.
4. Include ordinary and no-save Runes/Frigid Touch, plus shape/aspect/Fury checks from #68. Observe actual stagger
   and ally safety; the one-round forecast does not imply the critical minute or saved variants are supported.
5. For the high-risk bomb question, include both isolated and clustered enemies while healthy and while hurt;
   retain **both** AI and combat logs. Include invocation/reveal users if those regressions need acceptance.

No additional uploaded files are required for the implemented repair.
