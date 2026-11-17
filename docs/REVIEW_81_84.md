# Review of PRs #81–84

17 November 2026, Codex. **Five runtime findings corrected; the stack remains unmerged and unplayed.**
This is a correction branch over #84, not an endorsement of every forecast as a native simulation.
The most important remaining decision is D-R1: whether to commission the origin-aware engagement quote before integration.

## Scope and source

| Slice | PR / reviewed head | Own code range |
| --- | --- | --- |
| A: ground bursts | #81 / `3715193` | `ad63ee8..3715193` (including the approved plan) |
| B: RT disruption | #82 / `f5a511d` | `3715193..f5a511d` |
| C: suppression | #83 / `755ba56` | `f5a511d..755ba56` |
| D: fortification/flanking | #84 / `53e793f` | `755ba56..53e793f` |

All four target development `claude/dreamy-pasteur-b9h3rn`, base `ad63ee8`.
`codex/review-81-84` starts at `53e793f`; only that task branch is updated. No Claude branch is reset or rewritten.
Info.json stays **0.3.60**. No merge, main update, tag or release. No supplied game/third-party mod binaries, raw logs, blueprints or decompiles
are included in these changes; the test ZIP contains only the built mod and its manifest.

Read with the original [plan](BURST_AIM_AND_TACTICS_PLAN.md), [A](BURST_GROUND_AIM.md), [B](CAST_DISRUPTION.md),
[C](BUFF_SUPPRESSION.md), [D](SNEAK_FLANKING.md), §189–193 of [TACTICAL_AI.md](TACTICAL_AI.md), and
[checks](competence/REVIEW_81_84_CHECKS.json) / [compiler provenance](competence/REVIEW_81_84_BUILD_PROVENANCE.json).

## Corrected findings

### A-D1 — defect, medium: future fusion damage intersected with current pool recipients

Evidence: [ScoreAbility:3957](../Tactics/TacticalEngine.cs#L3957) previously intersected `BurstEnemiesInside` with
`sharedHd.Selects`. The first is a landing-time prediction; the second is the current-position native pool selection.
An enemy outside the area now but predicted inside at impact could disappear from the **independent damage** quote.
The underlying pool correctly knows nothing about that future arrival. Native `AbilityTargetsAround.Select` selects
around its actual anchor at delivery; `AbilityDeliverDelay.Deliver` waits, then yields `context.MainTarget`.

**Fixed in `d68bfd5`.** A planned fused burst keeps its predicted damage recipients; pool eligibility/control remains
independently conservative. Ordinary pools and non-planned casts retain their previous selection. No future creature
is inserted into the pool budget. Five production-policy/profile/route checks were added to BURST-AIM.
This does not claim to predict future pool order or native line of sight.

### B-D1 — defect, medium: attack modes retained the original disruption bonus

Evidence: [ChooseAttackModes:644](../Tactics/TacticalEngine.Modes.cs#L644) recomputed weapon damage/AC for each mode set,
but the disruption term added in `AddWeaponCandidates` stayed unchanged. Power Attack and Grabber can change hit chance,
damage and whether a second attack occurs, all inputs to the concentration forecast.
Native `RuleCheckConcentration.OnTrigger` uses actual damage, spell level and concentration.

**Fixed in `c2eff66`.** Every existing mode set reprices disruption from its own estimate and the candidate's original
walk timing. [Separate survival weights:2761](../Tactics/TacticalEngine.cs#L2761) preserve the original walk/provocation
multipliers; damage-role, wake and 5-foot damage discounts are not incorrectly applied to disruption. The new scalar
term participates in both the mode comparison and the already-on/triggered-mode baseline correction. No extra rule,
world pass or formatted label per mode set is added; the running-cast read is reused. Griffon leap behavior is unchanged.

The regression witness is deliberate: against a level-2 cast with +14 concentration, a 90%-accurate 5-damage attack
cannot break concentration; a 40%-accurate 11-damage attack breaks it 4% of the time. At stakes 100, the latter can win
despite slightly less physical damage. A unit test and production route check cover the correction; three further checks execute the production aggregator
against changed damage/hit chance, a late hit and two timely hits.

### C-D1 — defect, medium: aura suppression lost inherited context

Evidence: [ToggleValue:649](../Tactics/TacticalEngine.Toggles.cs#L649) supplied `None/None` as the parent of every grant.
Native `AddAreaEffect.OnActivate` passes its buff context to `AreaEffectsController.SpawnAttachedToTarget`; `Spawn`
clones it for the area, and `MechanicsContext.CloneFor` constructs a child context. A plain grant can therefore inherit
a suppressible descriptor or school even when it has no component of its own.

**Fixed in `5391939`.** Read the already-built toggle context and derive the area's metadata once, outside the ally loop;
pass it to each grant's arrival test. Self-buff applications still have no parent. The actor-free check fills a native
suppression part and proves that a plain real buff changes from unsuppressed to suppressed solely through inheritance.
Additional checks cover the compiled route and native child-context construction.

### C-P1 — performance, low: repeated part/metadata lookups and list construction

Evidence: [ScoreAbility:4641](../Tactics/TacticalEngine.cs#L4641) used `Any`, `FirstOrDefault`, then `Where(...).ToList()`
with the same per-buff arrival predicate. A suppressed recipient could cause two or three native-part/parent reads and
another list. This is inside the per-ability, per-recipient loop.

**Fixed in `d0425e6`.** [FilterOnArrival:50](../Tactics/BuffSuppression.cs#L50) reads the part/parent once, visits each buff
once, retains the input list when unchanged, and copies only from the first suppressed buff. No persistent cache or new
invalidation requirement. Four checks exercise real suppression parts with all/partial/no suppression, exclusions,
order preservation and input-list identity. This is an algorithmic improvement, **not a measured Unity speedup**.

### D-D1 — defect, medium: copied partner bonus and incomplete extra-hit damage

Evidence: [FlankPartnerGain:108](../Tactics/CombatMath.Flanking.cs#L108) added a fixed +2 rather than asking the native
rule. Its extra-hit term omitted existing sneak damage (e.g. an already-flat-footed target) and critical confirmations.
Native `RuleCalculateAttackBonus.OnTrigger` accepts `TargetIsFlanked`; mods can patch that rule. The existing
`ExpectedWeaponLine` already combines native-derived hit damage, sneak, criticals and fortification.

**Fixed in `73d34f7`.** Ask `RuleCalculateAttackBonus(TargetIsFlanked = true)` with the line's original iterative penalty,
compare ordinary and prospective lines through the shared kernel, then add only newly enabled sneak dice. Fortification
is applied once. One result is cached on the existing per-frame/decision routine, so repeated candidates do not trigger
another rule per partner line. Actor state and engagement are never changed. The new unit witness covers extra hits
with existing sneak plus critical damage; audit checks cover native-rule/shared-kernel routing and the cache, and three
production-kernel fixtures exercise heavy/full fortification and newly enabled sneak.
This does **not** repair the engagement assumption in D-R1 or guarantee that arbitrary mod handlers honor the override.

## Test-gap findings

**B-T1 / D-T1 — test gap, medium, corrected in `f4e5697` / `2d8a9b5`.** Existing IL-route checks could remain green
while a timing combination or damage term was wrong. The added checks execute the production disruption aggregator and
attack-line kernel with scalar/native-layout fixtures, rather than another formula copy. They do not execute an actor's
mode switch, native attack-bonus handlers or engagement; the playtest obligations below remain.

**C-T1 — test gap, low, corrected in `34542f8`.** Explicitly pin the verified blueprint-descriptor inheritance contract.
Reflecting the full area-spawn body unnecessarily resolved scene types from unavailable firstpass; the native child-
context contract is now tested directly through CloneFor, with the area-spawn call separately verified in native IL.
No check is skipped or relabelled as an actor test.

## Remaining risks and concrete follow-up proposals

### D-R1: proposed origin-aware engagement quote

**Risk, high; proposal only.** [AttackerEngages:67](../Tactics/CombatMath.Flanking.cs#L67) proves melee weapon, CanAct,
threat hand and a non-null threat range. It never checks the target's distance, sight or total concealment.
Native `UnitEngagementExtension.IsEngage` additionally calls `UnitHelper.IsAttackOfOpportunityReach` (3D reach including
corpulence and LOS) and rejects total concealment. For example, an attacker that cannot see an invisible target can
attempt a melee attack without becoming its engager, while an ally with sight may already engage it. The current
prediction can incorrectly grant a new flank.

This assumption reaches **every** `ReadRoutineCore`, including enemy threat, defence, held-buff and leap quotes, not
just a chosen party Move+Attack. The routine key has no current-vs-projected origin input. Requiring current reach
everywhere would discard the approved future-attack feature, so that is not a suitable correction.

Proposed slice: distinguish current and projected attack geometry in the quote/cache key; use native current engagement
where appropriate; for a planned attack carry the already-planned arrival origin and prove threat reach, sight and
concealment once per pair before any mode sets. Reuse existing path/LOS evidence; do not run a path search per routine.
If proof is unavailable, retain ordinary damage and report the uncertain flank separately. Test mixed threat/attack
reach, total concealment, LOS blockage, walking arrival, enemy-threat origin, both modes and cache separation.
Because this changes geometry assumptions used by movement/danger scoring, **author approval is required before
implementation**. `c0f82a4` corrects the unsupported comments and the RT command wording without changing that policy.

### D-R2: conditional Tabletop Tweaks fortification

**Risk, medium; provider design required.** `FortificationChance` reads the native target part's highest value. This is
correct for the supplied stock `RuleAttackRoll`. The supplied TTT Core replaces its fortification block with
`NewRules.RuleFortificationCheck`; it supports bonuses/penalties and `NewComponents.RerollFortification`, including
reason-specific critical/sneak/precise-strike rerolls. One reroll taking the attacker's better d100 result would change
a stock 75% negation into 56.25%, even without changing the target's part. This is an illustrative supported-component
case, **not evidence that a played character has that component active**.

Do not trigger `RuleFortificationCheck` as a scoring probe: its `OnTrigger` rolls a d100. A follow-up needs a validated
optional, non-rolling probability provider for the installed rule/handlers, with separate outcome reasons where needed,
plus a stock fallback explicitly identified as such. A target-only cache would be insufficient. No ad hoc feat table or
runtime dice simulation was added. The optional TTT audit runs here exercise the existing resistance/Coup provider,
not Harmony-patched combat or this conditional fortification behavior.

### B-R1: several allies credit the same cast break

**Risk, medium; coordination proposal.** [DisruptBonus:134](../Tactics/TacticalEngine.Disrupt.cs#L134) combines this
candidate's first two hits, not attacks already issued to other allies. Two independent 50% breaks of a stakes-100 cast
have combined value 75, while separate full quotes total 100; three total 150 for combined value 87.5.
This can overcommit attacks to a caster. Removing the feature or arbitrarily shrinking its weight would hide the issue.

Propose a short-lived ledger keyed by the **actual running enemy command** and only already-issued friendly commands.
Price the next candidate's marginal break chance after the pending hits, respecting landing windows and command expiry.
Drop entries on interruption/finish/fight end; do not reserve unselected candidates. First collect a replay with two
allies, their actual hit times, the native concentration rolls and the enemy command's terminal. This introduces a new
coordination/commitment policy and should be approved as a separate slice. No such policy was added in this review.

### B-R2: time and damage forecasts remain approximations

**Risk, medium.** The enemy's remaining cast reads native `m_CastTime`. Friendly hit timing still estimates swing,
flight and walk time; `CastSecondsOf` is a partial forecast of `UnitUseAbility.Init`, not a call to it. Native Init has
additional animation/item/held-touch/Eldritch-Archer branches. Expected per-hit damage stands in for a discrete actual
damage packet, so threshold crossings, criticals and dice variance need not match the expected-damage DC. A hit that
kills the caster also ends its cast; independent damage/kill and disruption terms are not a full joint terminal model.
No fabricated time constants or distribution were substituted.

Measure melee/ranged first and second hit times against short and full-round casts before calibrating. Prefer reading
already-running native commands; constructing speculative commands or rolling attacks to discover timing is inappropriate.
The `[breaks ...]` tag remains the pre-mode quote; the mode annotation/gain includes B-D1's revised value. Do not treat that
original percentage as an exact post-mode prediction. This diagnostic limitation is left to avoid adding another mode
read/label pass; it does not leave the selection value stale.

### A-R1: straight-line movement and HP-only re-check

**Risk, medium.** [Burst prediction](../Tactics/TacticalEngine.Burst.cs#L144) advances along the endpoint chord. A path
around an obstruction may occupy a different place at the same time. The uncertainty radius is conservative in some
cases, but cannot establish the path or future orders. A3 also skips a newly exposed ally when estimated HP damage is
zero ([BurstWatch:310](../Tactics/BurstWatch.cs#L310)); it does not independently re-evaluate a control/kill payload.
A damaging/control fusion against a damage-immune ally is a case to verify, **not proof of a particular native Sleep
interaction**. Sleep's native ally exclusion must not be replaced with a blanket friendly-control veto.

Propose a separately approved forecast using a retained native path when available, sampled once per decision with a
measured work bound and no new pathfinding. For re-checking, share the existing native-aware per-component recipient,
guard, immunity and pool rules rather than inventing a second control model. Exercise curved paths and a genuinely
ally-harming non-HP payload before coding a broad cancellation change.

### A-P1: scene re-check cost and finite watch capacity

**Performance/risk, low to medium in large encounters.** [Evaluate:281](../Tactics/BurstWatch.cs#L281) scans scene units
up to four times a second per watched caster. A newly exposed ally with zero damage can be estimated again on later
checks. The original statement that no world scan was added is too broad. The 32-entry cap evicts the oldest entry
([Track:114](../Tactics/BurstWatch.cs#L114)), even if live: that can drop its A3 re-check or A5 wait. Fight clear is wired,
and A4's pending areas have expiry, but these facts do not prove all bursts stay watched under saturation.

No capacity constant was raised. Before changing admission/cancellation, measure active watch count and map-unit count;
consider sharing one current-frame combat roster and never silently evicting a live watch. Define overflow behavior
with the author. Expiry-only area storage and the approved two-cancels/six-seconds cap are not safety guarantees.

### C-R1 / C-P2: binary future cleanse value and repeated suppressor scans

**Risk/performance, low to medium.** [StillMatters:213](../Tactics/BuffSuppression.cs#L213) is an eligibility rule, not a
marginal value. For example, a debuff with 61 seconds left under 60 seconds of suppression passes, even if the expected
fight horizon is only 18 seconds; [ordinary cleanse scoring:4799](../Tactics/TacticalEngine.cs#L4799) still grants the
usual full horizon. Conversely, an effect suppressed until expiry appropriately gives no immediate cleanse benefit.
Dispel's `ActiveRounds` discount is more nuanced. C1 also assigns zero to a buff
suppressed on arrival even when the suppressor will soon expire and the longer buff would resume natively. That
conservative arrival rule is not proof of zero value for the whole buff duration. A broad dispel can also remove a suppressor, so a future joint model
must consider which facts the same action removes; independent discounts do not simulate that terminal.

Propose calculating the removable harmful buff's **active time within the existing horizon**, reusing its actual expiry
and suppression expiry, before changing cleanse value. Preserve separately valued attached-hazard removal. Native
condition/buff targeting still determines what is removed. This needs a focused value model and tests, not a new weight.

`Suppressors` scans active facts/components for each harmful buff and option; N buffs × D dispel/cleanse options × F facts
can repeat N·D·F work. Unknown or mod-added suppressors correctly retain the old value rather than fabricate an expiry.
After profiling, cache the read-only suppression snapshot only within the existing decision/preview lifetime and
invalidate it with previews; do not introduce a persistent actor cache. C-P1 fixes the clear arrival-loop repetition now.

## Native coverage checked and retained

- **A:** native unit-eye versus fixed-point selector, side/Selective filtering, delay yield, simple projectile fields,
  point capability and attack-roll exclusions; ordinary and pooled fusion profiles. `UnitUseAbility.OnAction` spends
  the ability; controller action spending follows acted state. Normal pre-acted `Interrupt` ends the command directly;
  native `IsInterruptible` is true before act. Concentration failure can separately spend then finish the command;
  finished commands are pruned. This is not a refund mechanism. Act events update landing time; approved RT sharing,
  fight clear, TB landing wait and its existing cap remain routed. No additional movement policy was implemented.
- **B:** controller queues damage while Standard is a running, unacted interruptible spell; concentration uses
  `10 + spellLevel + Damage.Result / 2` with integer division, **no natural-1/20 override**. A failed concentration
  check ends/spends the spell; later checks cannot restore it. Non-spells/item/held-touch exclusions are native.
  The bonus stays separate from damage's friendly-fire penalty. TB adds no disruption value.
- **C:** native global exclusion set wins; named, descriptor and school sets work; attach asks the part; suppressed buffs
  are disabled and are re-enabled when native suppression ends. **The questioned blueprint descriptor is correct:**
  the supplied `AbilityExecutionContext` constructs its base from `AbilityData.Blueprint`, overrides school, and does
  not replace the descriptor with `AbilityData.SpellDescriptor`'s fusion union. `MechanicsContext` inherits/ORs descriptor
  and applies its own-school / parent-school / AddSpellSchool precedence. The new check explicitly retains this behavior.
- **D:** stock one-d100 fortification negates sneak, confirmed critical and precise strike together; auto-hit bypasses
  that branch. Base hit damage is retained; the common kernel discounts sneak/critical extra once. `CannotBeFlanked`
  and precision immunity are checked. Native RT `AnyCommandTargets` tests any non-null command slot, not just a running
  attack/cast. The helper matches normal TB/RT counting; native `IsFlanked` also has an army tactical-combat branch that
  the helper does not reproduce. Army tactical combat is not validated here. No opposite-side geometry, tabletop
  30-foot sneak limit or concealment prohibition on all sneak attacks was invented.
- **Mods:** inspected supplied TTT Core/Base, DarkCodex and CodexLib attack-roll patches. Calling the native AB rule
  preserves its patch entry points; a handler that consults only live engagement can still ignore a prospective override.
  The review does not certify every installed mod combination or execute their Harmony combat patches offline.

## Verification, package and replay

Checked runtime source **`2d8a9b561591f5f174a31a83243858a0de896822`**, startup MVID
**`9d4c4d08-9419-41da-b4e1-81c60270fc45`**. SDK **8.0.426**, Roslyn **4.11.0-3.25569.22 (3fb752d4)**.

- Real-reference Release: **0 warnings, 0 errors**. **1,000 unit tests**, scripts **27 / 18 / 3**, native harnesses
  **ToggleCosts 98 / TomeBurn 14 / ToyBoxSpend 10**, supplementary Magus **44**, all pass; no prerequisite skips.
- ProfileAudit: **A 33 / B 19 / C 33 / D 25**, zero failures. Every other report row is byte-identical after excluding
  only these four check-count headers, against an untouched `53e793f` build on the **same inputs**. Full Fusion matrix
  is byte-identical: **8,709 admitted / 15,494 configurations**, zero reader errors.
- Both the existing 44,640-blueprint corpus and a 45,248-blueprint corpus pass. The latter restores the exact historical
  **607-stock-fixture manifest** (`b53d1855…`) from the supplied 2.7.0x database: **40 ground-aim cases**, 1,892 spell
  blueprints. Historical receipts name three more loaded blueprints whose separate files are not recoverable here;
  this does not claim their exact entire input set/report hash was reproduced. The original Achaekek enum mismatch
  is still diagnosed by the separate raw-corpus negative test, not silently repaired. Full input accounting is in the receipt.
- SigDump against `53e793f`: **0 added / 0 removed external native signatures**. Both lists are retained in the receipt.
  The stack's 23 additions versus the pre-stack runtime were separately resolved on the supplied references.
- `34542f8` strengthens the C audit without resolving unrelated scene types from missing `Assembly-CSharp-firstpass`.
  Native area spawning was inspected directly in IL; child-context construction is tested through `CloneFor`.
  These are blueprint/part/scalar/compiled-route checks, **not actor, path, Harmony, timing or gameplay tests**.

Play **[review1 UMM test ZIP](../test-builds/AI-Control-FACC-v0.3.60-dev-review1-2d8a9b5-UMM-test.zip?raw=true)**;
identity/checksum sidecars and the receipt record full DLL/ZIP hashes. ZIP SHA-256 `8989b793a43cbd40652b4d8e645d6c6c71414c7ff2317de09d324c9879621f4a`. UMM must show **0.3.60**, startup module `9d4c4d08`.
The validated DLL is frozen across later documentation commits. This is an unplayed development test, not a stable release.

Keep **Log.txt and combatLog.txt** with the startup identity; note ToyBox spawns/rests and manual orders.

1. **A / RT:** Explosive Runes and its pooled fusion on a walking enemy; check fixed point/lead and recipients at landing.
   Include an enemy initially outside the pooled area that walks into the damaging burst. Do not expect future pool
   control eligibility to be invented. Move an ally into the landing area before act: check cancellation, unspent slot,
   next decision and the approved cancellation cap; with Avoid Friendly Fire off check the cost comparison.
   Verify other units' pending-area avoidance. Enemy-only, Selective and stationary ally-adjacent cases regress.
2. **A / TB:** delayed burst waits for delivery before ending the turn, subject to the existing cap; no RT leading/re-check.
3. **B / RT:** short and full-round enemy casts, melee/ranged companions, first/second hit times and native concentration
   outcomes. Test an available attack mode/Grabber that materially changes damage/hit chance; selection must include
   the revised disruption value. Test two allies to expose B-R1. Near-complete casts gain no late-hit credit. **TB:** none.
4. **C / both modes:** Enlarge on a suppressing form is withheld; Haste under Beast Shape I remains useful; ordinary
   buffing still works without suppression. Verify out-of-combat upkeep and a toggle aura carrying an inherited descriptor
   when such a kit is available. An Ameliorating-covered debuff should not be cured unless it outlasts suppression;
   compare a short and long remainder, keeping C-R1's horizon limit visible.
5. **D / both modes:** second melee attacker/rogue joins one engager; check actual flank/AB/sneak in combatLog, including
   a partner already sneaking against a flat-footed target. Compare fortified/unfortified and CannotBeFlanked targets.
   Include a TTT fortification-reroll kit if available, recording D-R2's limitation. Total-concealment and mixed threat-
   reach cases are **diagnostic for D-R1**, not promised passes. Retain existing leap/Magus/held-buff regressions.

Do not merge #81–84 or this correction PR until the author has reviewed the remaining proposals and played the package.
