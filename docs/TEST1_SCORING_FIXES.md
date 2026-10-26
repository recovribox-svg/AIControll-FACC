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
   Reuse only safely scoped failed-approach evidence; a different range/action,
   changed location or new frame must remain eligible. Never make a target globally
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
