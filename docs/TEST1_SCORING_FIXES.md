# Test1 scoring fixes — implementation and validation plan

The author explicitly requested resolving the four open findings in
[the Test1 review](INFUSION_VALUE_TEST1_REVIEW.md). This is a new task on
`codex/test1-scoring-fixes`, started from development `f51189f` and fast-forwarded
to pending PR #6 at `5669369` as an explicit dependency. Main and development stay
unchanged. This work does not reintroduce the withdrawn kineticist preview system.

## Native evidence and chosen fixes

1. **Shared Hit Dice:** the supplied Sleep graph subtracts the recipient's native
   CharacterLevel after its saving throw, on both save outcomes. Spell resistance
   fails before entering that graph. Native AbilityTargetsAround preserves its
   selected unit order. Recognize a restricted shared-budget control graph once
   per blueprint, then forecast remaining-budget probabilities in that native order
   for one real scoring aim. Existing save/SR appraisal prices the current target
   once; the forecast supplies only the probability its effect is reachable given
   earlier consumption. No buff, damage, save or gameplay action is executed.
   Unknown/shared graphs must not regain the old full-budget-per-target credit.
2. **Obstructed approaches/boosts:** reuse the dispatcher's actual path/reach/LoS
   checks as a prepare-only check for the selected follow-up before spending a
   boost. Keep checks bounded to actual selected plans, not every scored target.
   Keep failed preparation local to this decision; a different range/action,
   changed location or new frame remains eligible. Never make a target globally
   untargetable merely because one approach failed. Commands still run only through
   CommandDispatcher.Run, which revalidates before issuing.
3. **Energy protection:** retain native energy identity and distinguish resistance,
   absorption pools and immunity. Reuse expected damage already computed by enemy
   assessment plus existing damage observations, with the existing observed-offence
   prior. Value prevention only against matching incoming energy; unknown energy
   is not treated as a generic 15% threat. Keep per-energy caps/pools and existing
   protection distinct; no new enemy ability scans or native attack estimates.
4. **Cone recipients:** call the game's nongeneric native cone queries for existing
   aim directions, including actual Selective filtering, body radius, range and LoS.
   One ephemeral real context per scored aim; no context survives the query. Use the
   planned cast origin for a walk-then-cast, include downed allies where applicable,
   and use the same recipient set for effect valuation and Progressive viability.
   If native geometry cannot be queried, reject that candidate rather than silently
   dropping allies or reverting to single-target/circular cone scoring.

## Boundaries

- No global hooks, patched fact queries, hypothetical infusion configurations,
  native rule/context caches, toggle writers, priority trees or generic weight
  retuning. No additional class features or unrelated legacy-engine work.
- The budget adapter handles only recognized native data/action shapes. It models
  the known subtraction operation using the recipient's runtime property, not a
  copied level table, and never calls an action's execution method.
- Native cone helpers are bound as ordinary queries; no detour or copied angle
  formula. Geometry query failures must be visible and safe for friendly fire.
- Keep each issue in a separate implementation commit, followed by integration
  evidence. Any unavoidable API coupling is recorded, not hidden in a refactor.

## Verification and delivery

- Run focused pure tests for probabilistic budget depletion, ordered recipients,
  SR/save distinctions, energy matching/caps/pools, and scoped path failure rules.
- Build Release against supplied real references and retain all existing tests.
- Validate real blueprint recognition and native API bindings with local offline
  tools. Check baseline/current reader changes and explain every expected change.
  No withdrawn feature checks, game DLLs, rewritten dependencies or offline tools
  are committed or packaged.
- Review both combat-mode paths, target selection, friendly-fire and delivery-risk
  discounts. Cover boosts, changed target/range and invalidation of failed paths.
- Publish a new, uniquely identified flat UMM ZIP on the feature branch with source,
  MVID/checksums, clear instructions and a PR into development. Preserve old ZIPs.
- In-game playtest remains required: budget-laptop smoothness, native Sleep
  recipients/outcomes, matching/mismatching energy protection, obstructed boosted
  casts, cone enemies/allies and moving-origin casts in both modes. No offline check
  will be represented as live gameplay or a frame-time guarantee.

## Implementation and review outcome

The four component commits are followed by one shared-engine integration commit.
Cone/shared-budget metadata meet in the same candidate and Progressive APIs; path
preparation uses the same final main-action selection as spell boosting. Keeping
that wiring together avoids pretending those shared call sites are independent.
Intermediate component commits were not individually gameplay-tested.

- **Sleep:** a probability distribution tracks remaining HD, retaining native
  selector order. Earlier SR failures preserve the pool; an entered save/debit
  branch spends on either saving-throw outcome. Current SR/save is priced once by
  `EffectChance`. The same eligibility informs control, friendly-fire safety,
  duration applicability, Progressive viability and boss bypass. Friendly-fire
  cost remains conservative when eligibility is uncertain; it is not discounted
  into an assertion of safety.
- **Thresholds stay separate:** `AddSharedValue` alone is not evidence of a pool.
  The reader requires a matching native `SubHD` action. Actual Blasphemy,
  Word of Chaos, Dictum, Holy Word, Arbitrament and Embodiment of Order data were
  checked to retain their ordinary threshold scoring. Shared-HD kill/damage
  appraisal is outside this control-buff adapter.
- **Strict Sleep-like support:** one native around-selector, one dependent buff,
  equivalent duration expressions, a default save followed by an unconditional
  debit, ordinary/one-shot delay delivery, and a deterministic initial budget.
  Custom saves, mutations, repeated deliveries and budget-dependent selectors
  are rejected. Limits are 64 initial HD, 128 recipients and 8,192 state visits;
  an overflow discards the entire forecast, never a favorable prefix.
- **Deep Slumber limitation:** the supplied blueprint uses spreading delivery.
  Native effect arrival depends on live positions, frame delta and pending-effect
  insertion order; no pure order query was found. Its shared control value is
  withheld, so the AI will not choose that otherwise pure-control spell in this
  test. Unsupported/shared graphs log one `[HD]` explanation per blueprint. This
  is a deliberate conservative loss of capability, not verified full class coverage.
- **Approaches/boosters:** selected plans use throwaway commands and existing
  dispatcher rules; at most three preparations per decision, with no whole-engine
  rescoring. Rejected low tiers expose already-scored higher tiers through silent
  Progressive views; final policy diagnostics describe only the resulting list.
  Budget omissions are logged separately from proven refusals. In-reach and quick
  options remain, and the next decision may retry changed geometry. No failed
  preparation writes target-wide unreachable state. Existing real-path provoke
  evidence can still be repriced. Separate walk/touch approaches finish before
  boosting; Quicken's second-action credit requires both actions to need no walk.
- **Read-only preparation:** ordinary native `UnitAttack.Init` configures animation
  handles/speed, so preparation uses its `OnlyForInspect` mode and native approach
  radius query. Native `UnitUseAbility.Init` was inspected for command-local writes.
  Every temporary path is released in `finally`; movement limits and commands are
  applied only by actual `Run`. Preparation is skipped for UI previews and outside
  combat. A feasible moving action is checked again at actual issue, which adds a
  path query; live CPU cost remains a playtest gate.
- **Energy:** type, immunity, resistance and finite pool remain distinct. Matching
  shares reuse the existing assessment and completed damage results. Packet sizes
  come from the existing per-victim cast estimate or completed typed event mean,
  taking the larger; a large spell cannot become many imaginary weapon-sized hits.
  Aggregating multiple packets can underprice resistance. Mixed/custom predictions
  remain unattributed until observed; unknown packet size gives no rate-based
  resistance/pool credit. Conditional/uncertain immunity and overlapping active
  absorption pools are conservatively unpriced. Zero matching danger skips native
  current-protection queries. No additional enemy ability scans were added.
- **Cones:** native nongeneric cone/range delegates supply actual geometry,
  filtering and LoS for existing aim directions, from the planned cast origin.
  The all-or-none recipient set includes relevant downed allies and is reused by
  friendly fire and Progressive/boss checks. A zero-benefit damage component cannot
  inflate success viability. Custom/controlled projectile delivery is excluded.
  No search for new optimal aim directions was added.

## Verification results

- Full Release compilation against the supplied real game/UMM references passes
  with zero warnings/errors. **142 unit tests pass, zero failed/skipped.** Final
  runtime source is `8fb0257`; its unique package and DLL identity are recorded
  in [test-builds](../test-builds/README.md) and draft PR #7.
- Pure tests cover shared budget depletion/order, SR transitions versus saves,
  caps/failure atomicity, matching energy/rates/pools/overlap, bounded selected-path
  checks, fresh decisions and Progressive fallback after a blocked lower tier.
- An isolated ordinary v0.3.21 audit-tool copy reads 1,220 abilities with zero reader
  errors on the supplied partial exports: 4,097 loaded and the same known failed
  Achaekek export. All classification counts match the prior narrow build. After
  normalizing export-root paths, exactly five detail rows change: Angelic Aspect,
  Greater Angelic Aspect, Eaglesoul, Firebelly and Ice Body now expose separate
  energy types/immunity instead of collapsing equal amounts. No other row changes.
- An isolated actual-blueprint parser check accepts stock Sleep and rejects custom
  saves, unknown mutations, shared-budget selectors and chain delivery; it also
  checks the six nonconsuming threshold abilities above. Native cone/range delegates
  bind successfully; empty/failing recipient collection checks are all-or-none.
- These are compilation, arithmetic, data-parser and API-binding checks. No running
  Unity game is available here; native actor geometry/conditions, pathfinding,
  actual casts, third-party interactions and frame-time effects remain unverified.
  No rewritten dependency, game assembly, offline fixture or audit tool is packaged.

## Focused gameplay acceptance

In both TB and RT, use the same save/settings/mods as the smooth Test[1] comparison:

1. Check the package MVID, idle Orvath selection, cold/warm turns and summons on the
   budget laptop. Any new selection stutter or animation-speed change fails this gate.
2. Sleep several low-HD enemies with and without allies in the area; record native
   recipients, SR/save outcomes and AI values. Confirm the pool is not reused.
3. Try blocked and reachable casts, a walk longer than one move, separate touch
   approaches and reservoir boosts. No point should be spent for a rejected next
   cast; a feasible higher spell tier or in-reach action should still be offered.
4. Compare matching/mismatching fire/cold/physical threats, resistance already up,
   depleted pools and immunity. Read the `[Buff]` matching-damage explanation.
5. Aim Color Spray at enemies near cone edges with allies beside/behind/in front,
   including an unconscious ally, Selective, obstacles and walk-then-cast origins.
   Avoid-friendly-fire must use the actual cone and conserve the spell if unsafe.
6. Recheck the earlier active Bowling/Pushing slice and toggle-management boundaries;
   these log sessions did not yet exercise its supported maneuver path.
