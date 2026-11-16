# Sleep-fusion review corrections: effective pool and friendly safety

16 November 2026, Codex. Review-found corrections on `codex/fusion-pool-safety`, stacked on PR #77 (`452d8a3`)
and #76; development base `980cf26`. The author authorized reviewing and merging satisfactory open PRs after the
outcome1 log review. **These are static review findings, not incidents observed in outcome1**, which predates F7.
Manifest remains 0.3.52. No new metamagic switch, movement policy or third-party dependency is introduced.

## Findings verified against the game

### Effective metamagic changes the shared pool

Native `ContextValueHelper.CalculateDiceValue` calculates the dice (or maximizes them), adds the bonus, then for a
die above d1 and effective Empower adds integer `total / 2`. `ContextCalculateSharedValue.Calculate` multiplies by
its modifier afterwards and truncates. The context tests its **calculated params**, not just raw AbilityData flags.
Native AutoMetamagic can add flags through RuleCalculateAbilityParams; the supplied `EmpoweredCold` graph
(`0b06a41777524044bf36e9f43b6a6e03`) is one automatic-Empower example. This does not mean arcan has that feature.
Stock fusion holders have AvailableMetamagic=0; ordinary rod/UI applicability is not assumed or enabled here.

PR #77 only used raw AbilityData Maximize and a normal 2d6 distribution. Conditional on effective automatic flags,
that can underestimate the actual pool. The corrected reader uses the already-created context's flags, caches
normal/Empowered distributions in immutable plan metadata, and handles Maximize with either. Native 2d6 becomes
3/4/6/7/9/10/12/13/15/16/18 under Empower; Maximize plus Empower gives 18. A zero-count d6 can still empower its
bonus, whereas d1/zero-die expressions cannot. Normal supported configurations remain supported if an empowered
variant exceeds the existing bound; that runtime variant instead withholds pool credit conservatively.

The public unit-independent math follows the native order. Native audit cases call the **actual supplied native
calculator** on inert params-only contexts, always maximized so no random/actor/Rulebook execution is needed.

### Ordered depletion does not establish friendly safety

Native fused effects run source 1 then source 2 for each recipient. The actual Sleep graph's initial OR condition
skips an unconscious target and therefore its debit. A recipient killed by source 1's damage can leave more pool
for a later ally than a current-state forecast predicts. For example, a four-HD predecessor spends a four-HD pool
while standing; if the damage drops it before Sleep, a later four-HD ally can instead be reached.

Also, ScoreAbility explicitly includes allies walking into a future burst, but PR #77's current native selector
does not include them. Its Eligibility returns zero for an absent recipient. That is useful for current enemy
valuation, but must not mean "this ally is safe". Neither an optimistic depletion path nor a missing entry proves
an ally cannot be affected.

The enemy estimate keeps its existing ordered probability model. Friendly safety now has a separate conservative
upper bound: for each currently selected ally it asks whether the already-read native pool graph reaches the buff
at **any** remaining budget from 0 through the highest possible pool. This avoids assuming arbitrary guards are
monotonic. It reuses the existing per-recipient/per-budget visit cache. An absent or unsupported forecast is
uncertain/unsafe; an IgnoreCaster caster is explicitly safe under the native rule. Existing actual buff immunity,
already-present-buff and Selective handling remain authoritative. This check precedes ordered control valuation
when deciding whether an area catches a friendly unit.

This can withhold a fusion that a particular realized depletion sequence would spare. That is the intended safety
tradeoff: an uncertain earlier death/move cannot justify friendly-fire permission. With Avoid Friendly Fire off,
the existing caught-ally cost policy remains. The correction does not simulate future combat or alter paths.

## Performance and scope

- No additional execution context, rule/selector/path pass or world scan relative to #77. The existing selected set
  supplies ally candidates; one faction check per selected recipient distinguishes allies for the safety work.
- Native guard reads still have the bound `recipients × (highest pool + 1)` because safety and value share the same
  visit memo. For ordinary 2d6 that is 13 per recipient; effective Empower raises it to 19. Additional native reads
  can occur for ally budget states the enemy-value forecast never needed; none are duplicated for the same state.
- The new per-aim friendly map is bounded by the existing recipient limit plus a possible IgnoreCaster entry. It
  is not retained in a profile/static actor cache. Cached plan distributions contain scalars only.
- Ordinary Sleep and the metadata admission set are unchanged. The reader does not grant Empower/Maximize or
  remove the native fusion metamagic UI restriction. No current playtest demonstrates the automatic-meta case.
- Enemy values still assume earlier recipients are standing when the pool graph is visited; the correction makes
  that approximation unsuitable as friendly-safety proof, without pretending it is an exact damage/pool joint model.
- Intrinsic receiver guards use current state, like the surrounding engine. Arbitrary future fact changes and
  third-party dice-rule overrides are not simulated. No Unity speedup or gameplay acceptance is claimed.

## Verification and replay

[Checks](competence/FUSION_POOL_SAFETY_CHECKS.json) and
[compiler/reference provenance](competence/FUSION_POOL_SAFETY_BUILD_PROVENANCE.json) identify the frozen checked DLL.
The eventual integrated package retains the user's existing normal fusion/ordinary-spell and policy regressions.

In TB and RT, play PR #77's ordinary Sleep-fusion cases first. For a damage-first/Sleep-second burst, put an ally
where an earlier enemy could consume the pool, then where the first payload can kill that enemy; with Avoid Friendly
Fire on, possible sleep on the ally must prevent the cast or require another safe aim. Include an ally already
walking into the burst. An unknown/missing recipient cannot establish safety. Compare IgnoreCaster and actual
sleep immunity, without bypassing the game's Selective applicability.

If a legitimate native automatic Empower/Maximize source is available, record the effective state; do not use a
rod the game rejects as evidence. A 13-HD target cannot fit ordinary 2d6, but fits an Empowered pool with probability
10/36 before its own delivery/save; Maximize+Empower can cover 18. The game's actual buff/result and paired logs
are still required for live acceptance. Keep one-native-cast/slot, spell policy, blocklist and ordinary Sleep tests.
