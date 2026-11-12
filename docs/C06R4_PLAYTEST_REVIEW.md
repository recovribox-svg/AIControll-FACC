# C06R4 playtest review and C06R5 performance fixes

The uploaded startup marker matches C06R4 `3705efbc-ad31-48e0-a8b7-c01ea9654b1d`, source `736358a`, packaging `ca12a28` / PR #31. [Input fingerprints](C06R4_PLAYTEST_INPUTS.json) identify both logs; raw logs remain private. This task starts at that played C06 chain, over development `1f58150`. It preserves movement, retreat, commitment, paths, dispatch and cancellation policies.

## Observed behavior

There are 19 fights: eleven TB, seven RT and one mixed. The 946 logged decision summaries include waits/end turns; they are not the full driver invocation count. The first party listing contains ten companions, versus the prior five-member C06R2 capture. This is not a matched performance comparison.

No CompanionAI exception is logged. Sixteen external WrathPatches errors concern copied blueprint components with mismatching owners (including Isekai ChannelPositiveHarm, Incendiary/Explosive Runes and copied delivery components). They are kept separate from AI Control failures; their source mods are unchanged.

Four positive attached-risk notices exercise GiantCentipedePoisonBuff and SpiderSwarmPoisonBuff, including changed estimates across later encounters. The native log includes seven stat-damage packets: four Dexterity, two Strength and one Constitution. Cling is absent. This demonstrates the poison path is running, not complete acceptance of Cling, caster-death or energy-removal behavior. There are also 257 unsupported-contract notices for 29 distinct blueprints, largely benign recurring stances/resources.

Swarm metadata selects native target-centred casts, and two actual choices move out of a swarm: Phirry at AI line 1792 and Aeiluvetar at 3636. Other exits appear among candidates, while eleven diagnostics report the best exit did not win the existing competition. Those values/choices do not by themselves prove a path defect or justify retuning movement. No such policy is changed.

Faerie Fire appears in ordinary candidate lists but is never selected in the 946 summaries. The two visible candidates at lines 1471/1478 compete against higher-valued Entangle/Grease; this does not exercise the remembered final invisible-enemy route. Vampire Spawn is explicitly rated x2.0 for negative-level harm at line 634; a chosen target can still lose to another option's whole action value. Fifty choices outside the displayed raw top five pass existing policy/action/kinetic rechecks; rank alone is not classified as a bug. Profane Invocation and the named unknown-burn line are absent. One enemy Alchemist Bomb threat estimate is present, without a friendly bomb-use calibration sample. Spellstrike coverage exists but delivery remains the separate C07 task.

## Performance diagnosis

178 slow samples include 24 explicitly marked **memory clean-up ran**. The largest is Tuvel's 4,034 ms sample at line 4588, including cleanup; the largest without that marker is 2,257 ms at 6808. These are not all native computation pauses: GC/global heap deltas and stage time must remain distinct.

The repeated work is explicit in non-cleanup samples too. At line 5119 Tuvel scores 255 abilities; Slow creates 336 contexts with about 8,328 KB of the selected delta inside hit/save chance work, and Scare creates 504 contexts. At line 4588 Stinking Cloud creates 870 contexts. Actual heap deltas are global observations, not exact thread allocations or guaranteed savings.

`CombatMath.EffectChance` asks `SpellResistancePass` repeatedly for each victim of each area aim and for damage/control branches. Its existing helper creates a target-specific native context when a spell-resistance part is present. The same caster/target/exact AbilityData question is repeated within one synchronous EvaluateAbility call. It does not accept the area aim as an input. Native UnitPartSpellResistance's context queries read that same cast identity, descriptors, school and caster; the full context-based immunity-before-roll rule remains in place.

## Corrections

Spell-resistance results are now reused within one EvaluateAbility call, keyed by **reference identity** of caster, victim and exact AbilityData. Distinct metamagic/item/spellbook copies remain distinct. Both immune zero and full-precision penetration probabilities are retained; the native helper, immunity order, bypasses, ally rules and resistance math are unchanged. Source-context objects are not shared. Unscoped calls retain their native reads, and the special excluded-bypass-source query remains uncached.

The scope disposes on all returns/exceptions. Nested evaluations are isolated and restore the outer scope. ClearAttackCache invalidates every open scope by an epoch; existing infusion/temporary-prone previews use that path, including same-frame transitions. No answer survives into the next ability evaluation/decision. Slow-decision telemetry adds `SR forecasts` and `reused` counts for the wrapper so replay can verify the reduction. No predicate is bypassed to save time.

Buff appraisal now avoids source-context creation when the already-read parts contain no numerical effect and neither weapon nor armour enchantments can add parts. Rank increments are read first; numerical buffs and both equipment carrier paths retain full evaluation. Actual Death Ward has numerical parts and keeps its context. This is a behavior-preserving empty-result optimization, not new valuation of unsupported effects.

Unsupported periodic-contract diagnostics are emitted once per blueprint per session rather than once per fight. Classification, supported poison/Cling rules and rejection boundaries are unchanged; the change removes repeated log work and noise.

## Verification and replay

Checked source `6794275`, startup MVID `64cd2d32-7182-47c5-a7c5-fef817edf58d`. [C06R5 test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-C06R5-6794275-UMM-test.zip) · [identity](../test-builds/AI-Control-FACC-v0.3.30-dev-C06R5-6794275-UMM-test.identity.json) · [receipt](competence/C06R4_REVIEW_CHECKS.json). All 594 unit and 11 inventory tests pass; actual-reference Release has 0 warnings/errors. All 18 native scope/equipment checks and inherited regressions pass. No foreign member reference was added against played C06R4. Strict compatible ProfileAudit and the original full-corpus enum failure remain separately reported; input bytes are not normalized.

Native fixtures use uninitialized identity objects and injected numerical results, plus actual Death Ward, numerical and equipment graphs. They do not execute native resistance queries, actors or commands. Warm reference-memo reads are checked over 10,000 lookups with zero allocations. These checks prove lifecycle/identity boundaries, not complete Unity performance or gameplay.

Replay matching TB/RT encounters with the same party, installed mods and ability list. Compare decisions and the added SR read/reuse counters for Tuvel/Orvath, contexts and hit/save heap deltas, marking cleanup samples separately. Check spell immunity on a spell without SR, normal resistance, bypass and distinct metamagic/item copies, then hypothetical infusion transitions and repeated same-frame decisions. Numerical Death Ward, weapon/armour enchants, healing/poisons and the existing swarm candidates should preserve behavior. C06R5 gameplay and matched latency/allocation results are pending; no measured speedup or complete swarm-safety claim is made.
