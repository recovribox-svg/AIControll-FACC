# AI Control: Magic Fusion research and implementation plan

Revision 2 — 14 November 2026. Research record; no implementation was made during the planning stage. Current implementation status is tracked in [FUSION_IMPLEMENTATION.md](FUSION_IMPLEMENTATION.md).

This is the complete revised plan. It incorporates verified improvements from the independent review: explicit shared-math boundaries, time-based marginal control, a separate simple-support slice, dependency-safe optimizations and an earlier gameplay checkpoint. The review's second-aim/action, save-independent damage and missing-metamagic claims were checked against native/current code rather than adopted. [Assessment and evidence for each critique](MAGIC_FUSION_PLAN_REVIEW.md).

## Recommendation

Expand the existing shared tactical engine. Keep each player-created fusion as **one native cast**, retain its ordered effects and their conditions, and evaluate what each actual save outcome accomplishes against each recipient. Select the aim with the greatest useful combined result after friendly fire, redundancy, resource costs and casting risks.

Do not add a general “fusion power” bonus or create an independent scoring engine. The immediate opportunities are specific: restore selected-delivery cone evaluation, stop one already-active debuff from suppressing another useful effect, preserve damage/control correlations, and replace blanket exclusions with narrowly verified support for additional structures.

Recognition, legal delivery, useful valuation and successful execution are separate acceptance requirements. A profile that contains two effects is not sufficient evidence of competent casting.

Recommended first implementation sequence: **F0 coverage/contracts → F1 selected-delivery repair → F2 effect-level valuation → F3 outcome-based utility**. Then expand mechanisms using the ranked F4–F8 slices below. Tests, diagnostics and performance work belong in every slice.

## 1. Scope and evidence

I fetched the repository and checked open PRs. Both `main` and `claude/dreamy-pasteur-b9h3rn` were at **`6cf42818ade856814003ac0cab8872795d884ffd`**, with no open PRs at the check. This is the current research baseline; fetch again before implementation because Claude may advance development.

The v0.3.52 package's checked gameplay source is `30cfa2c`; later package/documentation commits do not change its C# source. The supplied `0.3.52 Log.txt` matches its startup module **`981a7b77-a654-4da1-9a22-68bc1c6a77bd`**.

Evidence used:

- Direct inspection of the active discovery, Fusion readers, geometry, damage/control scoring, policies, observers and command path.
- Targeted decompilation and direct IL inspection of the supplied real `Assembly-CSharp.dll`: SHA-256 `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`, module `90a9869c-2792-4c7b-bfb7-5a8b33da7c82`.
- The supplied blueprint exports, including `MagicDeceiverSpellList`, native slot blueprints, payloads and referenced buffs. The declared list contains **60 levelled source spells**, ten at each level 1–6, plus six cantrips. The source inventory is structural evidence, not 60 successful Fusion forecasts.
- All eleven newly supplied logs, with hashes, package matching and Fusion observations recorded separately. The proposal is advisory material; its instructions and claims do not override the user's research-only request or native mechanics.

Companion artifacts:

- [Evidence, input hashes, native contracts and pinned source references](competence/FUSION_RESEARCH_EVIDENCE.json).
- [60-source metadata inventory](competence/FUSION_SOURCE_INVENTORY.csv).
- [Independent review assessment](MAGIC_FUSION_PLAN_REVIEW.md). The original revision remains in the private research artifacts.

No new game session, casts, Rulebook execution, full compilation, unit-test run or complete executable combination audit was performed for this planning task. Existing validation receipts describe their own tested revisions. They do not validate the proposed implementation.

### What the logs establish

| Input | Identity and relevant observations |
| --- | --- |
| `TB + RT Log.txt` | 6,803 lines; **exit1**, module `905bb172…`, checked source `e806f4d`. Ten Fusion diagnostics: eight configurations reach the supported-profile diagnostic; two Sleep combinations are explicitly withheld. There are 27 named fused `UnitUseAbility` issues, 20 acted Success endings and seven unacted endings in this file. |
| `TB Mode Log.txt` | 2,952 lines; **review5**, module `b3359daf…`, checked source `45c3b4d`. The same eight supported configurations and two Sleep exclusions are visible. Twelve named fused spell issues have acted Success endings. |
| `RT Mode Log2.txt` | 2,840 lines; **startup absent**. Sixteen named fused spell issues, 15 acted Success endings and one unacted ending. Do not assign an exact build from chronology or its filename. |
| `Real Time Log.txt` | 2,604 lines; startup absent. No identifiable named fused casts or Fusion diagnostics. Its hash matches the historical caster1 review input. |
| `TB Mode Mixed Log.txt`, `exra Log TB+RT.txt`, `RT Mode Swarm only Log.txt`, `RT Mode Mixed log.txt`, `TB Mode Swarm only Log.txt` | These identify **v0.3.50 development review1**, module `25818a75…`, rather than the eventual v0.3.50 stable DLL. No identifiable fused casts/diagnostics in these inputs. |
| `Log v0.3.5 test.txt` | Despite its filename, startup identifies **v0.3.50 development review1**, module `25818a75…`. No identifiable fused casts/diagnostics. |
| `0.3.52 Log.txt` | 2,318 lines; exact v0.3.52 startup match. No identifiable fused casts, Fusion diagnostics or `arcan` rows. It cannot establish current Fusion judgment quality. |

Counts are per file, not a pooled count of unique encounters. Identical previous uploads were not counted again; potentially overlapping or headerless sessions were not combined. Spell names in these historical logs were associated with the explicit native configuration diagnostics where available, rather than used as a general recognition mechanism.

Useful witnesses:

- `TB + RT Log.txt:404–416`: actual source pairs, selected delivery, chosen save and level; Sleep exclusions at 405 and 413.
- `TB + RT Log.txt:419/422/425`: Explosive Runes/Hideous Laughter chosen, issued and acted successfully. At 576–583 a different fusion wins. The engine does distinguish alternatives.
- `TB + RT Log.txt:4085/6478`: two “did nothing to its target” observations for Explosive Runes/Hideous Laughter. These justify investigation; they do not prove all such casts are wasteful, or establish the latest build's behavior.
- `TB Mode Log.txt:962–968`: a fusion can be selected after an approach and re-evaluation. Future support must preserve that shared execution path.
- `RT Mode Log2.txt:149/151/266`: Incendiary Runes/Color Spray is valued at 298.6, aimed at a Vescavor Swarm and completes. **An area centred on an immune or low-value creature can still be useful against other recipients.** The label alone does not establish a bad target choice.
- Several save rows name `arcan`'s spell as an empty string, for example `TB + RT Log.txt:455–459`. Observers report the empty slot blueprint name, making component attribution difficult.

Performance also needs care. `TB Mode Log.txt:416` reports 138 contexts and 130 damage estimates for one Fusion option. This supports reducing repeated recipient work. However, the 1,470 ms decision in `RT Mode Log2.txt:150` lists **Blindness at 1,182 ms** and a cleanup marker. Selecting a fusion does not establish that Fusion evaluation caused the stall. Heap deltas are not allocation measurements or matched performance comparisons.

## 2. How the current implementation works

### Discovery and identity

[`AbilityDiscovery.Gather`](https://github.com/recovribox-svg/AIControll-FACC/blob/6cf42818ade856814003ac0cab8872795d884ffd/Tactics/AbilityDiscovery.cs#L138) already enumerates `Spellbook.GetAllCustomSpells()`. Native UI-created fusions are custom spells. General discovery is therefore present; this is not a missing enumeration of every possible source pair.

The fixed slot blueprint alone has empty effects and targeting metadata. [`AbilityProfile.Get(AbilityData)`](https://github.com/recovribox-svg/AIControll-FACC/blob/6cf42818ade856814003ac0cab8872795d884ffd/Tactics/AbilityProfile.cs#L463) recognizes `MagicHackData` and builds a special profile using the selected delivery and both source payloads.

[`AbilityProfile.Fusion.cs`](https://github.com/recovribox-svg/AIControll-FACC/blob/6cf42818ade856814003ac0cab8872795d884ffd/Tactics/AbilityProfile.Fusion.cs#L28) uses a weak cache keyed by the native configuration object and checks a snapshot against both sources, holder, delivery, additional area, level, school, save, target type and touch flag. Discovery and `CombatMemory` option keys include this configuration. Preserve this useful foundation.

**Identity is not consistently complete downstream.** `CastOutcome` failure/area-miss keys, learned save observations and the cast-bonus cache still use blueprint-based identity in identified paths. These can conflate edited configurations or observations. Ordinary separate Fusion slots have different holder GUIDs, so this is not evidence that every pair currently shares one failure counter. No supplied log proves slot editing caused the reported judgment problem.

### Payload interpretation

The reader walks `Spell1` then `Spell2`, using their native `AbilityEffectRunAction` components with the sticky-touch fallback. It retains both sources' damage and buffs. It does not reduce the entire fusion to one “damage” tag.

However, the resulting general profile separates damage, buffs and other lists and stores aggregate control information. That loses information required to judge the ordered combination precisely: which application has which duration, what remains useful after another effect, and which outcome/position produced the value.

The current Fusion gate intentionally refuses several structures, including lines, additional areas, clicked-target secondary actions, shared-HD pools, independent nested saves, healing/caster-side payloads, secondary spells, and some buffs/kills outside the failed-save branch. These are real coverage gaps, not proof that the engine never discovered those spells. The single `FusionUnsupported` string can also be overwritten by later findings; an inventory should retain distinct reasons and their locations.

### Damage, control and policy

[`CombatMath.Fusion.cs`](https://github.com/recovribox-svg/AIControll-FACC/blob/6cf42818ade856814003ac0cab8872795d884ffd/Tactics/CombatMath.Fusion.cs#L16) reads the actual spellbook's per-action dice cap and weights damage by the shared save path, `HalfIfSaved` and Evasion. `EffectChance` avoids applying a second global damage-save discount to Fusion. The projectile reader correctly avoids multiplying a fusion by an original source's missile count.

Save-independent damage is already distinguished: an action with no failed/passed restriction and no applicable `HalfIfSaved` receives a factor of 1. Existing Shocking Grasp/Fireball and negative-level fixtures cover this. `Both` means “not restricted to one root outcome,” not “always ignore every save.” Keep branch eligibility separate from the action's damage-reduction flags.

Maximize/Empower are also already read through native `AbilityData.HasMetamagic`, including actual special-metamagic state. The count cap is applied before the dice result. The fixed slot's `AvailableMetamagic = 0` does not establish the absence of active mod-granted metamagic. Do not infer normal player-UI metamagic availability from a synthetic fixture or read an invented custom blueprint for those flags.

[`TacticalEngine.EvaluateAbility/ScoreAbility`](https://github.com/recovribox-svg/AIControll-FACC/blob/6cf42818ade856814003ac0cab8872795d884ffd/Tactics/TacticalEngine.cs#L3114) handles admission, target/aim creation, geometry, damage, buffs/control, policy, delivery and approach/casting risks. It returns competitive candidates to the same `Decide` used by TB and RT. Native action availability, slot payment, Conservative/Standard/Aggressive policy, blocklists and Pure Mage rules continue to apply.

Next-spell boost purchasing is explicitly disabled for fused candidates because the ordinary boost estimator cannot price their capped, correlated payloads. Existing active native modifiers are a separate matter. Do not remove that gate until marginal fused boost value and actual costs are supported.

### Selection and execution

Target-centred area aims and existing cone alternatives compete by score. Pending-cast coordination primarily matches the same option on the same target, and its early exclusion deliberately does not suppress damage spells. Consequently, a mixed damage/control fusion can retain full control credit despite overlapping control already being cast by another option.

The winner goes through the existing preparation and `CommandDispatcher.Run` route. The AI does not cast the source spells separately. Preserve this ownership, action economy, command lifecycle and fresh final validation.

## 3. Native mechanics that constrain the design

The evidence JSON records native members as N1–N12. These are the essential contracts:

1. **One chosen root save is rolled before the two ordinary payloads run.** `AbilityEffectRunAction.Apply` then executes the concatenated actions in source order. They normally see the same saving-throw result.
2. **There are explicit exceptions.** `ContextActionSavingThrow` makes an independent save when `MakeSaveAlsoForMagicHack` requests one, or when no root saving throw exists. The exported Phantasmal Killer payload explicitly requests another Fortitude save. A general two-save model is wrong, but a permanent ban on every nested save is unnecessarily restrictive.
3. **School and DC belong to the actual fused cast.** Native `AbilityData.SpellSchool` returns the selected school; the current `ParamsOf` calls native `CalculateParams`. The ordinary sources do not each get their original school/DC/save.
4. **Descriptor domains must be distinguished.** `AbilityData.SpellDescriptor` unions the source descriptors. Context-based spell/buff immunity checks also consume context descriptors and buff data. Do not substitute two independent ordinary-spell immunity checks, or assume every context automatically receives the same union. Verify each native predicate and applicable installed patches.
5. **The native UI chooses the possible configurations.** Source level zero and already-fused sources are excluded from ordinary selection; conflicting selections are swapped. Level is the maximum of the two actual source levels. The native utility determines offered schools, saves, shapes and which source provides delivery, including chain/non-SR/shape/range preferences. Arbitrary Cartesian products of spell names and invented shapes are not a legal-combination audit.
6. **Source execution context is not recreated.** The real fused execution context is rooted at its slot blueprint. Reusing source actions does not copy every original rank, shared value, target checker or projectile behavior. Never import a source spell's ordinary HD pool or rank formula merely because it would make the fusion useful.
7. **The dice limit is per action.** The supplied spellbook gives 5, 7, 10, 15, 20, 100 dice at levels 1–6. Level six is not literally unlimited. Energy drain has a distinct native calculation path.
8. **Order can matter without changing the root save.** Damage can kill a recipient, later damage can break newly applied sleep, and a genuine later independent roll can see earlier changes. Model verified dependencies rather than an unconditional synergy bonus.
9. **Extra effects can have distinct footprints under one aim.** `GetDeliverBlueprint(true)` exposes optional additional-area metadata. The inspected execution process uses the selected primary delivery and separately applies source clicked-target actions to the existing context. `AdditionalAbilityEffectRunActionOnClickedTarget.Apply` uses `MainTarget`, which is `ClickedTarget`; this does not provide a second player-chosen target or a second command/action payment. Do not pretend all effects share one circle, invent a two-aim search, or globally deduplicate distinct native applications.
10. **A native query is not automatically safe to call during a forecast.** Chain `CheckTarget`, for example, can roll for Blinking. Use verified read-only predicates or bounded scalar equivalents; never run delivery, actions, dice rolls or buff application to score a candidate.

One useful negative finding: the current swarm area bonus follows the inspected native handler's unusual distinction between selected projectile shape and `AbilityData.Blueprint`'s `AbilityTargetsAround`. Replacing every holder-blueprint reference with delivery would change that native rule. Fix the confirmed cone lookup; audit other references individually.

## 4. Review of the supplied proposal

| Proposal | Verdict and replacement |
| --- | --- |
| Retain both effects and execution order | **Useful, partly already present.** Add provenance and effect occurrences; do not construct two independent ordinary spell candidates. |
| Keep save type and school per component | **Incorrect for the ordinary fused cast.** Keep the chosen cast-level school/root save, plus explicit nested save nodes where native code requires them. |
| Spell A reduces the save before Spell B's saving throw | **Not the general rule.** The shared root save happens first. Only a genuine later independent roll can benefit. Feeblemind's exported buff changes Intelligence/Charisma, not a general Will/Fortitude penalty; Slow's shown save penalty is Reflex −1. Mind Fog is not in this supplied 60-spell list, though a mod could add it. |
| Check each payload's immunity independently | **Needs a narrower interpretation.** Separate damage-energy reductions, buff/condition immunity and native conditional branches, while retaining genuine whole-cast immunity/delivery rules. Source targeting restrictions cannot automatically be reapplied to a different fused delivery. |
| Fix school/DC through `CombatMath.CastBonus.cs` | **Misidentifies the responsibility.** Actual parameters already come from the native fused `AbilityData`. Audit parameters and relevant cache identity; `CastBonus` primarily forecasts cast-time damage bonuses. |
| Add damage and control, cap redundant control | **Useful direction, incomplete mathematics.** Use conditional outcome utility, per-effect duration and survival/wake/overlap rules. Generic additive EV already exists and should not be added twice. |
| Friendly-fire checks for both components | **Valid requirement; checks already exist.** Correct effect/geometry granularity and veto reporting rather than add another independent damage discount. |
| Reserve high-tier fusions for opening rounds; penalize AoEs with 1–2 targets | **Reject blanket rules.** Same-level spells share slots; there is no separate “AoE slot” pool. One dangerous boss can justify an AoE. Compare actual marginal value, remaining encounter opportunity and the player's existing spell policy. |
| Extend `UtilityScorer`, `AttackScorer`, `DebuffScorer`, `CombatPhaseDetector` | **Wrong integration target.** Those belong to the retired real-time engine. Use shared `TacticalEngine`, `CombatMath`, `ValueModel`, policies and readers. `AoeDangerAnalyzer` remains relevant to shared safety, but is not the main Fusion utility aggregator. |
| Expand audit and judgment tests | **Accept.** Native-shaped fixtures, complete coverage accounting, outcome/cost comparisons, both modes and measured performance are needed. Profile extraction alone cannot establish tactical judgment. |

## 5. Confirmed gaps and their significance

### A. Selected cone delivery is lost during appraisal — high priority

`AbilityProfile` reads the selected delivery's cone. `ScoreAbility` enters its cone branch. But [`ProjectileCone.TryCreate`](https://github.com/recovribox-svg/AIControll-FACC/blob/6cf42818ade856814003ac0cab8872795d884ffd/Tactics/ProjectileCone.cs#L100) queries `ability.Blueprint`, the empty native holder. Its component lookup fails and the candidate is discarded.

This is a confirmed static path, not a reproduced in-game failure. Seven source spells in the supplied list have cone projectile components, including Burning Hands, Color Spray and Cone of Cold. This does not mean every fusion containing them chooses a cone; a fusion delivered through a different source can already work.

Repair the selected-delivery lookup through the shared geometry adapter and verify native filter/range/origin behavior. Also ensure a friendly-fire veto records enough information for the existing turned-cone alternative to be attempted: some early veto returns currently leave `FriendlyFireNote` unset, while the retry checks that note.

### B. One existing buff suppresses another useful control effect — high priority

At [`TacticalEngine.cs:4164–4172`](https://github.com/recovribox-svg/AIControll-FACC/blob/6cf42818ade856814003ac0cab8872795d884ffd/Tactics/TacticalEngine.cs#L4164), `buffs.Any(b => enemy.Buffs.HasFact(b))` skips the recipient's entire control value.

For a two-effect fusion, one existing debuff does not establish that the other adds nothing. Filter/value each application separately; include replacement/stacking/refresh rules and remaining duration. Damage must remain independently competitive.

### C. Surviving effects inherit aggregate strength and duration — high priority

The profile carries aggregate `ControlStrength`/CC flags. Scoring uses these after filtering buffs. `EffectRoundsOn` returns a duration across applications, not the duration of each surviving effect. Consequently a weak surviving penalty can inherit a stronger effect's classification, or a short hard control can inherit a longer companion effect's horizon. Condition immunity currently tests the aggregate condition set rather than pricing every surviving application.

This is a current-code limitation. The logs do not quantify how much of any displayed score it caused. Remedy: effect-specific eligibility, strength, duration and incremental benefit, followed by overlap-aware aggregation.

### D. Shared-save expected damage is fed into nonlinear kill value — high priority

Damage is first averaged over the shared save inside `EstimateAbilityDamage`. [`DamageValue`](https://github.com/recovribox-svg/AIControll-FACC/blob/6cf42818ade856814003ac0cab8872795d884ffd/Tactics/TacticalEngine.cs#L2354) then estimates kills from that mean. A mean damage is not enough to value a save-or-nothing distribution. Separate additive control also ignores that the same failed save may already result in death.

Synthetic example using the existing `RiskMath` approximation: 20% chance of 100 average damage, otherwise zero, against 15 HP. Passing mean damage 20 with delivery chance 1 produces a 75% kill estimate. Evaluating the two branches first produces 20%. This illustrates the model defect; it is not a claim that a particular supplied cast had those numbers.

Preserve the shared outcome until after nonlinear utility. When the target dies in an outcome, do not also credit preventing its future actions through control in that outcome. Retain the existing dice-spread approximation where appropriate and label it as an approximation.

### E. Explicit support boundaries exclude useful combinations — medium to high priority

The exported list includes real mechanisms behind current exclusions:

- Three line sources: Lightning Bolt, Acidic Spray and Fire Snake. Native `WouldTargetUnitLine` exists.
- Sleep/Rainbow Pattern/Circle of Death families needing HD/shared-value interpretation; two Sleep combinations are explicitly withheld in the logs.
- Phantasmal Killer's explicit additional Fortitude save.
- Enervation's conditional buff/negative-level branches and Disintegrate's damage plus HP-dependent finishing behavior.
- Haunting Mists and Obsidian Flow's clicked-target/additional-area structures.
- Touch/caster-side and support effects, such as Vampiric Touch/Arcana Theft, needing correct subjects and marginal effects.

These are mechanisms to audit, not a claim that all their pairings can be safely enabled at once. **Do not ignore apparent markers:** exported `DisintegrateBuff` activates a kill action, while `EnervationBuff` contains temporary HP. Neither is automatically harmless visual metadata.

### F. Timing/coordination does not value the residual mixed payload — medium priority

The current exact-option pending-cast guard is useful but incomplete for mixed fusions and different options that apply overlapping control. Suppressing the whole fusion would also discard useful damage. Price the remaining marginal control, retaining damage and respecting the chance/timing of the pending cast. A reservation is not a guarantee of success.

### G. Configuration identity and attribution remain incomplete — medium priority

Discovery/profile identity is substantially implemented. Failure memory, save observations and cast-time bonus caching need a consumer-by-consumer review. The original design document's broad statement that all cast/failure keys distinguish configurations is stronger than the current `CastOutcome` code supports.

Preserve slot-level user blocklist semantics. Snapshot a configuration when issuing a cast; a later edit must not relabel old events or carry invalid observations into the new configuration. Do not treat an interrupted, unacted command as ineffective spell evidence.

## 6. Proposed architecture

### 6.0 Shared numerical primitives, specialized native orchestration

The Fusion-specific layer may interpret configuration, delivery and ordered occurrences and arrange native save/dependency outcomes. Damage dice, reductions, save probabilities, marginal buff/control value, healing, friendly-fire policy, costs and risk use shared `CombatMath`/`ValueModel`/tactical helpers. A specialized adapter for a real Fusion-only cap or shared-save rule is appropriate; copying the general damage/save/control formulas into a separate Fusion scorer is not.

Extract a shared primitive where the existing API exposes only an aggregated result. Route equivalent ordinary operations through it without changing their arithmetic during extraction. A new marginal-effect helper should be reusable by ordinary and fused effects with sufficient metadata; ordinary abilities do not all need conversion to a new graph interpreter first. Keep an intentional common bug fix separate from a supposedly behavior-preserving extraction.

Add a small deterministic regression corpus for representative ordinary damage, save, control, healing, buff and friendly-fire cases. Expect unchanged results for a pure extraction; document and test deliberate native-backed changes. Do not freeze known errors behind indiscriminate golden-score equality or claim a full decision can be replayed from logs that omit its world state.

### 6.1 Compile native metadata once per configuration

Extend the existing weak Fusion profile cache with a compact, immutable description of:

- Actual holder/configuration identity and selected delivery; separate additional/secondary deliveries.
- Ordered **effect occurrences**, including source index, action position, subject, selector/footprint reference, guards, shared/independent save node, damage type, duration, native stacking/replacement metadata and known activation/trigger dependencies.
- Effect-specific capability and unsupported reason codes, including graph location. Keep all relevant reasons rather than whichever string was assigned last.
- Completeness of recipient/harm interpretation, separately from completeness of beneficial value.

An occurrence needs an identity beyond a `GameAction` reference: one referenced action can occur in more than one execution path. Do not clone/mutate game actions or make hypothetical ordinary `AbilityData` casts to represent components.

Keep ordinary profiles and shared reader helpers. The Fusion evaluator consumes the richer occurrence information inside the existing tactical pipeline; it is not another driver. Metadata caches contain no actors, native execution contexts or per-target results. Use existing profile-clear lifecycle and configuration checks; do not hash blueprint graphs every frame.

Keep one canonical configuration snapshot/equality definition, but choose key representation by domain. Cast observations need configuration plus the relevant caster/target/book/payer/metamagic context and a frozen issued identity. Synchronous resistance reads already have exact ability references and invalidation epochs; preserve that distinction. The existing configuration suffix string is cached on profile build, not rebuilt by every `FusionIdentityOf` call. Benchmark any replacement for the surrounding string keys rather than mandate one wide `FusionKey` struct everywhere.

F0 should classify each Fusion-relevant `ability.Blueprint` use as **holder/command owner**, **selected delivery**, **actual cast parameters**, **source payload**, or **observation identity**. A holder read can be correct under native mechanics. Never replace all such references mechanically.

### 6.2 Forecast each actual recipient with explicit outcomes

Read native cast parameters and live target facts, evaluate the real delivery/whole-cast gate, then construct a small scalar forecast. For a usual fusion:

`target value = delivery pass × [P(root failure) × utility(failure) + P(root success) × utility(success)]`

The ordinary root save is per recipient. Do not form an exponential joint tree of every target's saves or apply one global save outcome to the entire area. Add per-recipient expectations directly when separable; effects sharing an HD pool or caster-side cap need their own bounded shared-state model. Linearity permits summing linear expected effects even when correlated. Retain outcomes specifically where kill/overkill, survival, overlap, later conditions or another nonlinear consequence requires them.

Cast-level action/resource costs are outside the per-recipient sum and are paid once at their existing native pricing sites. Do not subtract a slot cost per target or condition that payment on failed enemy saves. Caster-side benefit must follow actual execution multiplicity and stacking: a once-per-cast benefit is not credited once per enemy, while a true per-recipient application must not be silently collapsed to one occurrence.

Within each branch:

- Run the ordered **model**, not the actual actions. Retain only verified scalar dependencies.
- Apply the actual per-action cap, energy/stat/drain rules, conditional eligibility and buff/condition immunity at their proper scopes.
- Combine HP damage before overkill/kill valuation. Account for explicit kill effects without also granting a second removal reward.
- Keep HP, affected ability scores and negative levels as distinct scalar quantities. Do not turn every drain-equivalent value into raw HP damage. Effects on the same stat/level pool need their actual combined consequence; alternative lethal mechanisms share one enemy-removal reward.
- Value control/debuffs only on surviving recipients and for the effective remaining duration. Sleep that later damage breaks earns no full-duration control credit.
- Bound overlapping action denial by the recipient's available harmful actions over the horizon. Two hard controls do not prevent the same action twice. Distinct useful penalties can still contribute.
- Respect native stacking/replacement rules through existing readers and the game's descriptor helpers. Existing effects, refreshes and pending effects contribute only their marginal difference.

For an actual independent save, add a node with its native type/DC/condition. Do not add an independent roll to ordinary payloads. Initially support nested nodes whose preceding effects do not require an unimplemented change to the later roll; add bounded, verified scalar changes separately. Never alter the real target to preview a debuff.

If a branch cannot be modeled, distinguish an unreachable branch, missing optional benefit and unknown harm. It can be reasonable to omit an unpriced benefit only when all harm/cost/delivery is understood. Unknown harmful reach must not become “zero cost” or proof of safety. Never restore the generic utility fallback to make a partially parsed fusion castable.

**Control redundancy is a time-based marginal calculation.** Build a decision-local coverage view from the recipient's active effects/remaining durations and existing live pending commands. Clip to the shared encounter horizon, partition at relevant expiries and compare baseline versus baseline-plus-candidate action denial/penalties. Within an interval, overlapping hard denial cannot exceed the same available harmful action. A soft effect can still add value after a stronger existing effect expires. Native replacement/refresh rules can also remove value; do not assume adding a buff is always beneficial.

For example, a soft effect lasting three rounds is not worthless because the target is stunned for the next round: only the overlap is saturated. Use a consistent recoverable-threat basis from shared helpers so the current disabled discount is not applied again to future recovered actions. Do not invent expiry for an intrinsic condition or an effect whose duration is unknown. Read native effect timing once and keep the model bounded; do not poll or simulate every future round. The existing worth formulas remain estimates, not proof of the enemy's future actions.

Pending coverage is derived from committed casts, recipient sets and their modeled chance/timing. It is not a persistent “controlled this round” flag, and it is not guaranteed to land. Reuse existing pending/execution observations, auditing interruption, invalidation, expiry, combat end and mode transitions. A command ending is not necessarily the effect landing: preserve the in-flight window until reliable execution/effect evidence or a bounded timeout resolves it. Unknown landing time must not justify withholding immediately needed control. A current-round ledger alone cannot represent RT timing or multi-round effects. The shared helper may improve ordinary multi-effect control too; document those deliberate score changes.

### 6.3 Use one selected recipient set across the decision

Pass the actual native geometry/recipient result into damage, control, friendly-fire checks, Conservative viability, boss bypass and execution validation. Do not score one footprint and use a different approximate footprint for policy.

Distinguish membership, native selection order, effect applicability, shared allocation and execution multiplicity. Repairing a cone's membership does not unlock a shared-HD or nested-save payload; those retain their own gates. Additional/clicked effects can derive another origin/footprint from the same native clicked target. Expand the candidate representation only for a verified native choice; different footprints alone do not require independent aims or another action. Count actual native effect applications on overlapping recipients, rather than assuming either universal deduplication or universal repeated damage.

Repair cones first. Then add line support using the inspected native line/range predicates and the same fail-closed geometry discipline. Audit chain filters and additional-delivery behavior separately. Native chain filtering can roll dice, so its full selector is unsuitable as a generic read-only forecast call.

Preserve existing target-centred aims and safe approach behavior. Once valuation is correct, compare the existing turned-cone aims by **useful combined value**, rather than enemy headcount alone. Any added ground-centre search should be a small deterministic extension from the current position, with native legality and complete ally checks. It does not require a new movement planner.

### 6.4 Decide timing through opportunity and native cost

Compare useful immediate damage/removal/control with legal alternatives, using the current encounter horizon, threat—including existing non-HP harm—and settings.

- High-value clusters can justify an early expensive fusion because of their actual benefit.
- A dangerous single enemy can justify the same cast; do not apply a blanket “only two targets” penalty.
- On cleanup, dead/disabled/overlapping effects contribute little or nothing. A cheaper cast or weapon can then win naturally.
- Use actual `Spellbook.GetSpellLevel`, remaining slots/availability and the shared spell policy. Charge one native cast, not two component casts.
- Keep cast/approach duration, concentration, arcane failure, provoking and action opportunity costs applied once through existing paths.

Fusions use the same scoring scale as other actions. Do not introduce a priority override, mandatory opener, guaranteed success assumption, per-spell-name bonus or a second resource policy.

## 7. Implementation slices, order and acceptance

Complexity estimates describe relative engineering difficulty, not promised timelines. Each independently reviewable change should have its own task branch/PR into the latest development branch. Rebase this plan on current code and avoid overlapping Claude's summon/invocation/grapple work.

**Support is included in scope.** Separate simple helpful/caster-side cases from the complex secondary-area slice. Start only with mechanisms present in the declared list or the player's actual created configurations. Reuse existing recipient-specific heal/buff/temporary-HP value and ally-harm safeguards; support utility is not a symmetric sign flip of enemy damage/control. Mixed support/offense must still account for harmful companion effects on that ally. A caster benefit whose amount or stacking couples multiple recipients belongs to the bounded shared-state work, rather than being credited independently for every target.

| Slice | Work and dependencies | Complexity / acceptance |
| --- | --- | --- |
| **F0: executable coverage and contracts** | Extend offline audit to enumerate declared source-pair/configuration templates using native metadata utilities, and optionally inventory actual created fusions without executing them. Produce separate recognition/delivery/effect/policy classifications. Establish native descriptor/context/rank and save-node fixtures. | **Medium; first.** Every input has a denominator, provenance and reasons. No silent skipped audit prerequisites. No gameplay DLL change for tooling-only work. |
| **F1: selected-delivery geometry repair** | Fix cone holder lookup and veto-to-turned-aim handoff using the shared geometry path. Keep actual native cast context, selected component, current approach/final issue behavior. | **Small–medium; high benefit.** A native-slot cone fixture reaches the same recipient query as its selected delivery; no synthetic extra source touch roll; allies at edges/behind walls/downed/moving are handled correctly. Ordinary cones regress identically. |
| **F2: effect-level control valuation** | Add occurrence metadata; filter already-present/immune/irrelevant applications individually, bind strength/duration to each application and cap overlapping denial. Depends on F0; keep ordinary helpers shared. | **Medium–high.** Slow already present does not erase useful Confusion; a surviving minor effect cannot inherit hard control; one-round hard plus long soft control does not become three rounds of hard control. |
| **F3: shared-outcome combined utility** | Keep root pass/fail outcomes through kill/overkill and residual control valuation; preserve action ordering and supported wake/death interactions. Uses F2 metadata. | **High.** Closed-form branch comparisons pass; no second save/SR discount, no death-plus-full-control reward, and lethal cleanup competes correctly with control alternatives. |
| **F4: additional buff/conditional branches** | Replace broad “outside failed-save branch” refusal with explicit application semantics. Normalize repeated wrappers around the same shared save and prune contradictory paths without inventing rolls. Start with living/undead/immune Enervation branches and ordinary unconditional/pass-branch effects; handle Disintegrate's post-damage HP/kill dependency as a separate sub-slice. | **Medium to high by mechanism.** Safe positive and structural-negative fixtures; helping an enemy is priced negatively/refused as appropriate. Never discard activated kill/temporary-HP behavior as a marker. |
| **F4S: simple support/caster effects** | Qualify actual same-recipient helpful effects or a known caster-side benefit through existing buff/heal appraisal, including subject, amount, timing, stacking and execution count. Depends on F2/F3; may proceed before complex areas when F0/player configurations justify it. | **Medium–high by dependency.** No source-name assumptions, inverted enemy utility or duplicated caster reward. Caster heal/temporary HP never exceeds its native marginal consequence; mixed harmful components remain guarded. Keep complex coupled cases explicitly deferred to F8. |
| **F5: line and verified chain delivery** | Add the native line footprint and bounded orientation selection. Audit existing chain target count, filters, dead/used recipients and order. Can follow F1; use corrected F2/F3 values. | **Medium–high.** Actual native membership, Selective/friendly-fire/obstacle behavior and policy all agree. No enumeration that rolls RNG or launches projectiles. |
| **F6: genuine nested saves** | Support an explicit independent-save node, starting with the actual Phantasmal Killer graph; retain root-save reuse elsewhere. Later support preceding save-changing effects only after their native changes are modeled at that point. | **High.** Root and explicit Fortitude outcomes are distinct; custom/conditional DC or unmodeled stat transitions remain named exclusions. No default “save shred” bonus. |
| **F7: shared-HD/shared-value allocation** | Adapt existing bounded allocator to the selected Fusion delivery and real fused context initialization. Cover relevant Sleep/Rainbow Pattern/Circle of Death structures separately. | **High.** Establish whether the native fused context actually supplies a usable pool before assigning benefit. Preserve target order, shared mutations and cross-payload dependencies; do not give every recipient a full pool or copy the ordinary source's initializer. |
| **F8: secondary areas, clicked actions and caster/support payloads** | Model separate delivery/application occurrences and lasting effects, including Haunting Mists/Obsidian Flow. Separate Vampiric Touch/Arcana Theft subjects and benefit/cost paths. Split by mechanism rather than one universal secondary-spell interpreter. | **High–very high.** All harmful footprints and native repeated/overlapping applications are accounted for. Persistent danger registration, ally future paths, duration and context provenance agree with the forecast. Unknown structures remain explicit. |

**Cross-cutting requirements:** configuration-safe observations and pending-effect valuation enter the relevant earlier slices, not a final cleanup PR. Paid boost planning is a later optional extension: compute its marginal gain through the same outcome model and current native costs. Keep it disabled while those requirements are unresolved.

The preparatory occurrence schema and shared-math extraction can be validated separately. Do not enable an intermediate Fusion valuation that simply adds independent effect scores before F3 handles its nonlinear dependencies. Straightforward separable effects may retain the existing tested path; new coupled cases stay withheld or diagnostic until their combined model qualifies.

F0's 60-source baseline has **3,540 ordered distinct source-pair templates before school/save/delivery choices**. This is a useful audit size, not a support percentage or proof every character can create every template. The native UI disallows normal selection of a cantrip/already-fused source; actual known spells, DLC, archetype/provider changes and runtime availability further constrain a character. Identify injected mod sources separately and retain invalid/synthetic configurations as negative fixtures rather than adding them to the legal denominator.

For each blocker, report both **present among reasons** and **sole remaining blocker**, including co-occurrence counts. Separate source-pair, full-configuration and actual-player denominators. Rank expansions by newly safe/useful player configurations, tactical value, implementation risk and runtime cost; raw template frequency alone is insufficient. A missing live aim/value assessment is “not assessed,” not a false rejection. A catalogue may read cached metadata/observations, but must not run a combat-scoring sweep on every UI repaint.

## 8. Performance design and measurement

The runtime evaluates **only the player's actual created spells**. Exhaustive pair/configuration enumeration belongs offline, never in `Decide`, a UI repaint or a per-unit tick.

Required structure:

- Compile graph metadata once per unchanged Fusion configuration. No per-recipient reflection or action-graph reconstruction.
- Read native parameters once per actual ability/decision scope. Reuse existing resistance memoization and state-preview invalidation.
- Hoist raw dice counts/caps/metamagic/base amounts only after proving their dependencies are recipient-, aim- and state-independent. Existing `ContextIsTargetFree`/`DamageContext` reuse is a starting point, not a proof for an unfamiliar provider. `Target*` values, target-buff ranks, native provider previews, Point-Blank/distance bonuses and shared-value mutations need their actual context. Keep recipient reductions and conditional bonuses outside any invariant cache.
- Reuse SR and save probabilities per **actual ability/recipient and distinct save node/DC/state**. An independent nested Fortitude save or a post-effect changed save is not the same cached query as the root Will save. A scalar amount cache also invalidates with relevant preview/configuration changes.
- Keep target forecasts decision-local. Reuse one recipient forecast across aims only when the effect is proven independent of aim/origin/earlier allocation. Shared-HD and ordered secondary-delivery cases do not automatically qualify.
- Reuse the geometry result across all scoring/policy consumers. Avoid repeated full recipient-list construction and context creation for each effect.
- Ordinary non-Fusion actions should not allocate Fusion bookkeeping or enter extra geometry work.
- Preserve current path/preflight limits. No nested `Decide`, full engine rerun, extra weapon quote or speculative native action execution to value a fusion.
- Prefer indexed immutable arrays and decision-local scratch structures in measured hot loops; do not perform a blanket LINQ rewrite. Initially use only proven zero-benefit exits after checking every relevant subject/benefit. Nonzero dominance pruning must preserve Conservative viable-tier candidates, boss candidates, support value, safety and native availability; being below the current highest score is not sufficient proof of irrelevance.
- Bound complex outcome/aim extensions explicitly. An initial ceiling of eight outcome states and eight **additional** aim candidates per decision is a design starting point to profile, not an existing rule or a license to discard effects. Exceeding a correctness bound produces an honest unsupported result, not optimistic partial scoring. Existing baseline aims are not silently removed.
- Shared-HD retains the existing allocator's explicit limits (budget 64, recipients 128, evaluations 8,192) unless a separate measured change justifies revising them. Do not nest that work independently inside every ordinary recipient forecast.

Measure cold metadata setup separately from warm decisions. Benchmark the same saved encounters/party/settings in TB and RT, including many created fusions, dense mixed targets, moving allies and supported/withheld complex cases. Record decision median/p95/p99, contexts, damage/save/SR reads, geometry calls, new profiles and measured allocations where the harness permits them. Attribute GC separately. Set the implementation's numerical acceptance thresholds from this baseline; no speedup percentage can be justified by the supplied logs alone.

When diagnostics are enabled, use bounded aggregate counters/timers for profile setup/cache reuse, geometry, recipient reads, effect valuation and outcome combination; report them within existing decision stages without double-counting their enclosing time. Avoid per-effect/per-target stopwatches, heap probes, strings or extra native queries in normal operation. Detailed logging must not become the optimization's dominant cost.

## 9. Diagnostics and verification

### Coverage reporting

For every audited configuration, distinguish:

1. Discovered with complete native configuration.
2. Metadata/effects understood, partially understood or refused, with paths/reasons.
3. Delivery/recipient forecast available or refused.
4. Currently available and permitted by character policy/blocklist.
5. Useful now, selected or outscored, with value components.
6. Issued/acted, and observed recipient/effect evidence.

A blocked Pure Mage touch or exhausted slot is not “Fusion not recognized.” A declared-profile pass is not a successful cast. Never claim all modded combinations are supported from stock/exported coverage alone.

### Logging

Extend the existing bounded/change-based diagnostic system. Capture a short configuration identity, ordered source GUIDs, delivery, chosen school/save/level, and reason codes once per configuration or relevant change. Fix empty observer names using actual cast identity. Freeze identity at issue time.

Diagnostic mode should explain the winning fused cast and a bounded runner-up: recipients, damage/removal, each marginal control effect, overlap/immunity/active-effect deductions, ally harm, slot policy and delivery risk. Reuse already-computed values; no extra rule/context/path queries merely for logging. Keep ordinary command/failure evidence, avoid per-frame/per-target spam, and clear bounded fight/session state through existing lifecycle hooks.

An optional bounded **one-decision capture** can include considered configurations, reasons and already-computed outcome details, with explicit omission counts. This is passive tracing of the normal evaluation, not a second “shadow” `Decide`, a parallel AI or an unbounded all-target dump. Use it to compare human expectations with scores before enabling a new interpretation. Missing details must not cause hidden extra scoring work to fill a table.

Keep three distinct observations: command completed, some payload reached someone, and a particular modeled effect succeeded. Do not infer both payloads succeeded from `acted=True`; do not count unacted interruptions as ineffective spell evidence. Damage-plus-control success must not teach that the control landed if only damage did.

### Automated and in-game acceptance

| Scenario | Required result |
| --- | --- |
| Native UI-shaped burst damage+damage | Both payloads, correct per-action caps, one native slot/cast, no original missile multiplier. |
| Burst damage+control, selected non-original save/school | Actual fused DC/school/save; root probability applied once. |
| Unrestricted damage plus a saved effect | The unrestricted action remains unrestricted; half-on-save and failed/passed branch restrictions remain separate. Use native-listed sources for positive legality coverage. |
| Actual Maximize/Empower/special-metamagic state | Actual cast flags, dice cap first, correct native behavior for separate/combined flags and no stale active-state reuse. Normal UI availability is recorded separately. |
| Cone/line holder has no projectile component | Selected delivery supplies real geometry; missing bindings withhold safely. |
| One companion debuff already active | Only its incremental value is removed; other useful effect survives. |
| Strong control expires before a candidate's weaker effect | Credit the useful tail after expiry, without double-counting overlapping denial or the current disabled-target discount. |
| Hard component immune, softer component eligible | Score the actual surviving effect; no inherited hard classification. |
| Short hard + long soft control | Correct separate durations and bounded overlapping denial. |
| Damage kills on failed root save | No additional future-control value in that lethal outcome. |
| Sleep before/after damaging payload | Correct verified wake behavior and source order. |
| One energy damage type immune/resisted | Per-damage reduction, while respecting genuine whole-cast and buff immunity gates. |
| Feeblemind/Slow or other penalty | No fictional benefit to an already-rolled root save; target-relevant penalty value only. |
| Phantasmal Killer explicit nested save | Actual additional Fortitude node; root reuse in ordinary nested wrappers. |
| Repeated same-save wrappers | The same outcome is reused and contradictory branches are unreachable; no extra independent-save probability. |
| HP damage plus ability/level loss | Each affected pool is evaluated in its proper units, with one shared removal reward if more than one mechanism would eliminate the recipient. |
| Shared-HD effects | Real fused pool/source initialization, target order and mutations; no full-pool-per-target fiction. |
| Enervation on living/undead/drain-immune targets | Correct branch, benefit/harm and level-loss valuation. |
| Disintegrate finishing buff | Post-damage HP/activation dependency accounted for; not discarded as cosmetic. |
| Ally immune to damage but vulnerable to control | Friendly-fire guard still protects the ally. Repeat for downed/moving allies and actual Selective rules. |
| Overlapping pending control, mixed damage fusion | Discount residual control appropriately; useful damage remains; interruptions release reservations. |
| Command ends before its projectile/effect resolves | Pending coverage does not disappear prematurely or persist after an interrupted/invalid cast; unknown arrival time cannot count as certain present control. |
| Simple support or caster-side effect | Native subjects, heal/buff curves, execution multiplicity and stacking; accompanying harm remains visible. Cast-level resources are charged once, with no extra cost per recipient. |
| Secondary/clicked-target area | Uses its native derived point/subject and application schedule; no invented second aim/action and no universal overlap deduplication. |
| One boss versus four weak enemies; cleanup versus opening | Choose actual net value, not a fixed AoE target-count or opener rule. |
| Conservative/Standard/Aggressive and depleted slots | Existing player policy/native resource behavior remains coherent. |
| Ordinary ranged noncaster and Pure Mage | Existing weapon/touch/blocklist rules preserved. |
| Edited/replaced configuration | Profiles, parameter-dependent cache entries, failure/save observations and diagnostics do not become stale or cross-contaminate; intentional blocklist scope remains. |
| Unknown child/caster-side/secondary harm | Named bounded refusal, never a utility fallback or assumed-safe missing effect. |

Pure tests should exercise outcome utility, correlation, marginal control and cost comparisons through extracted shared production formulas. Native-input audits should use real slot/delivery/payload graphs and structural negatives. Include an integration check through candidate delivery, not only `AbilityProfile.Get`: the existing 35 Fusion checks did not catch the cone handoff gap.

Before each gameplay PR: all applicable unit tests, zero-warning Release against the real game references, full relevant ProfileAudit with explicit input scope and no silent prerequisite skips, affected native-contract checks, a new `TACTICAL_AI.md` section and updated `CLAUDE.md` handover. Tooling/documentation-only work should clearly identify that the gameplay DLL is unchanged. Any test ZIP needs its identity JSON, checksum and unique startup MVID. A stable release requires the normal integration and gameplay acceptance; this plan does not create one.

Then play focused TB and RT scenarios, checking actual saves, damage, buffs, slot payments, ally effects, casts interrupted before action, mode switches, rests and configuration changes. Record ToyBox spawning/rest timing where used. Compare a new package with its exact baseline under the same conditions.

**Early gameplay checkpoint:** as soon as the author can supply play, qualify the current baseline and F1 in both modes with a supported mixed fusion, known immunity/resistance, ally-catching refusal, and a named unsupported case. Observe natural native casts/events and compare the recorded prediction; do not trigger Rulebook rolls or actions inside scoring to validate itself. Repeat the focused check for each later mechanism before claiming gameplay acceptance. Offline F2/F3 research, tests and reviewable preparation can continue while gameplay is unavailable; unplayed work remains clearly marked rather than silently treated as qualified.

## 10. Remaining inputs and uncertainties

**No additional file is needed to begin the recommended audit and first fixes.** Existing assemblies and exported source/payload graphs are sufficient for F0–F3 research and initial implementation.

For current gameplay diagnosis and acceptance, the useful additional material is:

- A matched **AI `Log.txt` and `combatLog.txt`** from the same current-build session with the Magic Deceiver controlled. Include the startup module identity.
- The created fusions' actual configuration: both ordered sources, chosen delivery/shape, school, save, level and relevant caster settings. A small runtime configuration export is strongest; names alone are insufficient because names can be customized.
- One or two particularly poor choices with approximate encounter/round context and the desired tactical alternative. This helps distinguish an incorrect forecast from a reasonable cast that failed its roll.

Only if an audited configuration refers to an absent generated/modded source, request that source GUID's minimal blueprint closure—payloads, delivery, buffs and required features/properties. Another whole-game export, unrelated mod cache or mod source-code archive is not a prerequisite. BubblePrints remains useful for a labelled missing **stock** graph; it does not supply the user's created configurations or prove runtime mod patches.

Open questions to resolve in F0/F4–F8 include exact context descriptor propagation and applicable patches; real fused shared-value initialization; effect-specific native save/immunity behavior; post-damage finishing triggers; and bounded interpretation of separate or persistent deliveries. These are explicit research obligations, not assumptions that the proposed implementation already handles them.

The practical goal is broader **verified** coverage and better decisions among useful casts. A fusion whose native effects cannot help the current targets should be recognized and declined. A fusion with an unknown harmful consequence should remain visibly unsupported until that mechanism is understood.

END OF FUSION PLAN — REVISION 2
