# F7 review correction: effective shared-pool metamagic

16 November 2026, Codex. Stacked on PR #77 (`452d8a3`) and #76; development base `980cf26`.
The author authorized review and merge of satisfactory open PRs after the outcome1 playtest review. These are
static-review corrections, not observations in outcome1 (which predates F7). Manifest remains 0.3.52.

## Native calculation

`ContextValueHelper.CalculateDiceValue` rolls or maximizes the dice, adds the bonus, then for a die above d1 and
effective Empower adds integer `total / 2`. `ContextCalculateSharedValue.Calculate` applies its modifier afterwards
and truncates. The flags come from calculated context params. Native AutoMetamagic can add flags through
RuleCalculateAbilityParams; the supplied EmpoweredCold graph is one example, not evidence the player owns it.
Stock fusion holders have AvailableMetamagic=0: this change does not enable rods or otherwise-prohibited UI choices.

PR #77 read raw AbilityData Maximize and only the normal 2d6 distribution. The correction reads the already-created
context's effective flags, caches normal/Empowered distributions in immutable plan metadata, and handles Maximize
with either. Empowered 2d6 has budgets 3/4/6/7/9/10/12/13/15/16/18; Maximize plus Empower gives 18. A zero-count d6
can still empower its bonus; a d1/zero-die expression cannot. If an empowered variant exceeds the existing work
bound, only that runtime variant withholds pool credit; normal supported configurations remain admitted.

The audit executes the actual supplied native calculator on inert params-only contexts, always maximized to avoid
RNG/actor/Rulebook setup. It checks 12, 18 and a zero-count d6/bonus case, plus the native distribution and routes.

## Ally-targeting clarification

The author confirmed ordinary and fused Sleep do not put allies to sleep, while a fused damage payload can harm
allies. The earlier proposed extra friendly-Sleep veto was therefore withdrawn rather than imposing an unverified
restriction. No friendly-Sleep incident was present in the logs. Its earlier candidate source/receipts are marked
superseded; no package containing that candidate was supplied or merged.

Native exports directly establish ordinary Sleep's enemy-only selector; the audit also checks that a fused cast
whose delivery is Sleep retains that selector. Explosive Runes has a broader damage-area selector. This task does
not change the existing friendly/damage evaluation or introduce an ally upper map. The exact protection for the
Sleep payload when a different selector supplies the fusion remains a source/gameplay reconciliation item; do not
turn either an external description or the initial source-level inference into a claimed live result. Existing
protection against the damaging portion remains. A controlled native baseline can resolve that distinction.

## Bounds and replay

No additional context, rule, selector, path or world scan relative to #77. Distribution metadata is scalar-only;
per-recipient visit caching/work bounds are unchanged. Ordinary Sleep and normal metadata admission are unchanged.
Enemy forecasting retains #77's current-standing/current-order approximation. Arbitrary custom dice callbacks and
future fact changes are not simulated. No Unity timing or gameplay acceptance is claimed.

[Checks](competence/FUSION_POOL_METAMAGIC_CHECKS.json),
[compiler/reference provenance](competence/FUSION_POOL_METAMAGIC_BUILD_PROVENANCE.json).

Play the normal #77 Sleep-fusion cases in TB and RT. If a legitimate native automatic Empower/Maximize source is
available, record the effective state; do not use a rod the game rejects as evidence. A 13-HD target cannot fit
ordinary 2d6, but fits Empowered 2d6 with probability 10/36 before its own delivery/save; Maximize+Empower covers 18.
Retain ordinary Sleep, enemy-only targeting, blocklist, spell-policy and fusion-damage friendly-fire regressions.
