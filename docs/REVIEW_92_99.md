# Review of PRs #92–99

21 November 2026, Codex. Corrections on `codex/review-92-99`, stacked after #99. **Nothing merged.**

This review covers the eight draft PRs as one interacting stack, its native contracts, offline checks and receipts.
The checked correction source is `131309f620c6adcb495c58200dd214de7ff78283`. The complete test package remains UMM **0.3.60**, module **`7ad87f85-5833-4798-baa5-4a5c9f0a9ffc`**.
It is unplayed. The coverage2/3/4 discussions were read as documented evidence; their raw logs were not replayed here.

## Reviewed revisions

Development was `0019856805605c9b74e27ffbdfa6b8dd9c20b0f2`. Every PR targeted that development branch and was
draft/open/unmerged when inspected. Their ancestry is linear; this correction preserves all eight branches' work.

| PR | Reviewed head | Main scope |
| --- | --- | --- |
| [#92](https://github.com/recovribox-svg/AIControll-FACC/pull/92) | `09b1315` | Fusion main-target guards and damage over time |
| [#93](https://github.com/recovribox-svg/AIControll-FACC/pull/93) | `816c136` | Burst capacity/pursuit forecasts and timing/flank evidence |
| [#94](https://github.com/recovribox-svg/AIControll-FACC/pull/94) | `994c6e2` | Enemy recasts, drain utility and ordinary periodic damage |
| [#95](https://github.com/recovribox-svg/AIControll-FACC/pull/95) | `5a80e54` | Ground bursts, touch arrival and measured attack timing |
| [#96](https://github.com/recovribox-svg/AIControll-FACC/pull/96) | `392b273` | Dominated allies, death safety, readiness and arriving engagers |
| [#97](https://github.com/recovribox-svg/AIControll-FACC/pull/97) | `cc862b9` | Freeing dominated allies, partial swarm exits and ranked ticks |
| [#98](https://github.com/recovribox-svg/AIControll-FACC/pull/98) | `c0b1c59` | Dispel, removal and suppression relief |
| [#99](https://github.com/recovribox-svg/AIControll-FACC/pull/99) | `22ded8a` | Lines, fused dispels, nested saves, lingering areas and Vampiric Touch |

Read this review with [the coverage programme](FUSION_COVERAGE_PROGRAMME.md), [R items](R_ITEMS_PLAN.md),
[coverage2](COVERAGE2_REVIEW.md), [coverage3](COVERAGE3_REVIEW.md), [coverage4](COVERAGE4_FOLLOWUPS.md),
[relief spells](RELIEF_SPELLS.md) and [remaining Fusion groups](FUSION_COVERAGE_REST.md).

## Findings and corrections

### P1 — dust/direct-kill safety could certify unsafe outcomes

1. **A save counted twice (#96).** `PermanentDeathRisk` used ordinary `EffectChance(damage:true)`, which already
   includes the root save, then blended save outcomes again. `DeliveryChance` now supplies resistance and attack
   delivery separately. Normal effect forecasting reuses the same helper and retains its own save/shadow factors.
2. **The wrong HP basis (#96).** `HpBeforeDown` subtracts nonlethal damage and clamps at zero; dust compares native
   lethal `HPLeft`. A below-zero ally with temporary HP could therefore appear protected. The gate now reads actual
   lethal and temporary HP separately, with zero further damage needed when the comparison is already satisfied.
3. **Wrong action/save dependencies (#96/#99).** The old scan summed damage from both sides of a dust gate, including
   a later Fusion payload, and collapsed an independent nested save into the root save's path. Gates now retain their
   preceding-damage count in native Spell1 → Spell2 order. Independent/unread save conjunctions use a conservative
   bound. A compound HP condition, including an OR, cannot prove that low HP is the only way dust applies.
4. **Unbounded native damage was called exact (#96).** Raw dice omit possible criticals and damage-handler bonuses.
   An average critical factor cannot prove a tail safe. Gates with unread critical/modifier/shared-roll distributions
   are treated as reachable on that outcome. Logs explicitly call the number a **risk bound**. See the precision limit below.
5. **The burst recheck skipped known allies (#96).** `plan.Allies.Contains` ran before its fresh death check. Known
   allies now receive that check whenever the cast can kill permanently; ordinary casts retain the early fast skip.
   The shared roster also retains living original party members outside the combat flag. It still scans the scene once
   per frame and revalidates retained references.

Native evidence: `ContextConditionCompareTargetHP.CheckCondition`, `ContextActionDealDamage.GetDamageInfo`,
`ContextActionConditionalSaved.RunAction`, `AbilityEffectRunAction.Apply`, and the supplied Disintegrate graphs.
Native scalar/layout and compiled-route checks cover these contracts; no actor cast or Rulebook damage was executed.

### P2 — separate utility, actual HP and party allegiance

6. **Drain utility became an HP kill (#94/#99).** `DrainEquivalent` entered the same outcome total used to cancel
   control when an enemy drops. The outcome reader now retains actual HP separately. Stat/level-drain utility still
   contributes value; it cannot manufacture an HP knock-out, remove useful control/dispel benefit, or count as a lethal
   HP packet for concentration. An ordinary mixed drain quote with no separated HP read earns no speculative
   interruption bonus. The first FusionValue row identifies the two quantities when they differ.
7. **Dominated-ally interactions (#96/#99).** Damage over time was missing from their damage cost. Downed dominated
   companions disappeared from both safety sets. The context now retains them for actual area recipients and prices
   them once as original allies. A burst records counted dominated allies as allies; line prefilters and lingering-area
   checks also preserve their original-party status. This does not create an attack target from a downed companion.

### P2 — dispel/relief forecasts did not follow their claimed contracts

8. **Shared rolls, suppressed attempts and ties (#97–99).** Targeted Dispel Magic sets `OneRollForAll`; twenty scalar
   d20 outcomes now preserve that dependence. Fully suppressed, zero-value buffs remain eligible attempts and can
   consume a removal. The complete buff input is sorted before filtering, using the native caster-level comparison;
   an 80-buff inert fixture agrees with the native comparer and detects the previous stable-sort difference. An enormous
   removal limit takes the unlimited path instead of allocating an enormous DP array.
9. **Wrong recipients/save/HP survival (#98/#99).** A displaced anchor is not automatically a dispel recipient. A
   point-only ordinary area dispel retains its existing area fallback. Fused dispels retain their actual root-save path,
   including unconditional effects on both outcomes, and positive benefit excludes an HP-dropped recipient on that
   outcome. One separated damage-outcome read is reused per recipient and is requested only for positive benefit.
10. **Discarded losses and ignored guards (#97/#98).** Selecting the best net dispel action discarded losses from
    earlier actions of the same spell. Positive benefit and own-buff loss are retained separately; multiple actions
    use the best individual benefit minus all priced losses. This deliberately conservative overlap approximation is
    documented below. Freeing an ally can retain a negative net cost. Removals use `BuffsRemovedFrom` with its guards
    and filters; suppressors require an applicable, non-immune occurrence and readable duration. Empty buff kits need
    no new context.

Native evidence: `ContextActionDispelMagic.RunAction`/`TryDispelBuff`, `RuleDispelMagic`, the actual Dispel Magic,
Greater Dispel Magic, Break Enchantment, Remove Fear and Unbreakable Heart graphs, and the suppression-part contracts.

### P2 — admission, error handling and work bounds

11. **Enemy-only recast anchors do not prove ally safety (#94).** An enemy-selected Fireball can splash allies.
    The exemption now structurally proves a narrow native target-only control chain, including Best Jokes' self-recast
    and removal-only control buffs. Area effects, changed casters and uninspected action/component types fail that
    proof. Actual Hideous Laughter configurations keep their coverage; a Fireball-recast negative fixture is refused.
12. **Unknown periodic harm became zero (#92/#94/#97).** A failed duration/rank/recipient/tick read cannot prove an
    ally safe. Unknown benefit is withheld; unknown harm carries a conservative sentinel capped by the existing
    friendly-fire HP/worth consumers. Failed ranked-context creation stays unread. Ranked child contexts now expire
    with their parent parameter cache, including same-frame previews. An isolated native-layout failure fixture
    exercises the asymmetric benefit/harm result.
13. **Limits were checked too late (#96/#99), and the TB capacity edge remained (#93).** Distribution size/work are
    bounded before allocation/convolution, oversized counts cannot be truncated into a smaller safe roll, and cached
    distributions have a cell budget. A full RT tracking table cannot discard the active TB landing wait. Line search
    validates at most eight native spots per ability/decision; the existing two-offer/one-walk-per-round policy stays.
    Turned point aims now use the same eye-origin direction as their prefilter.

### P3 — diagnostics

Only damage-related concentration failures count as observed damage interruptions; defensive/entangled failures no
longer contaminate that evidence (#93). A missing line projectile now reports **projectile blueprint unavailable**,
separately from an actual `FollowTerrain` refusal (#99).

The touch-arrival fix, measured timing constants, approved partial swarm exits, native line query, nested-save admission
limits and Vampiric Touch's omitted beneficial caster payload were reviewed and retained. No independent expansion
of movement, retreat, pathfinding or target-commitment policy accompanies these corrections.

## Verification and reproducible input scope

[Checks](competence/REVIEW_92_99_CHECKS.json) · [toolchain/reference provenance](competence/REVIEW_92_99_BUILD_PROVENANCE.json).

- Untouched #99: zero-warning real-reference Release, 1,072 unit tests and native harnesses passed on completed inputs.
- Corrected source: **1,096 unit tests**, **27/21/3 script tests**, zero-warning/error Release against the supplied game
  assemblies; native ToggleCosts/TomeBurn/ToyBoxSpend **98/14/10**, including Psionics; supplementary Magus **44**.
- ProfileAudit: **PERMADEATH 35, RELIEF 24, FUSION-RECAST 14, FUSION-DOT 52, LINE 22, BURST-AIM 79, EVIDENCE 16**.
  All other check counts pass. Same-input report/matrix differences are recorded in the receipt.
- The full 60-source, 15,494-configuration matrix retains **13,197 admitted / 8,635 joint**, with zero reader errors.
  These are admission/model results, not live execution coverage. Summon-kit detail is unchanged.
- SigDump: **4** added foreign member signatures relative to the fresh #99 build, all resolved against supplied references.
- No Unity benchmark, live actor cast, Harmony detour or gameplay replay of this package was run.

The first local #99 audit failed six line/fusion checks because projectile blueprints were absent. That was an input
failure, not proof that C4 was broken. The existing BubblePrints reader selected 194 referenced stock projectiles from
the retained 2.7.0x database. Missing-only loading adds 174 to the strict exported scope and preserves all supplied
blueprints. With those inputs the untouched stack passes and reproduces 13,197 admitted configurations. The strict
scope loads **44,814** records; full stock/summon scope loads **45,295**. The raw exports' separate known Achaekek enum
mismatch remains an explicitly failing input control.

[Projectile GUIDs](competence/REVIEW_92_99_PROJECTILE_TARGETS.json) and the existing
[BubblePrints instructions](../tools/BubblePrintsData/README.md) reproduce this supplement without committing raw
blueprints. Use the reader's selected raw records, wrap each as `{"AssetId":"GUID","Data":<raw object>}` in a private
directory, and add it with ProfileAudit's `--supplement`. Native references, dumps, decompiles and raw logs remain private.

## Precision and remaining limits

1. **Death-risk precision:** retain the 1% veto and summon exemption. Unread critical/modifier/shared-roll/nested-save
   cases use upper bounds and can refuse a cast even around a healthy ally. A full conditional native damage
   distribution would be required to relax this safely. This guard covers scanned dust/direct-kill paths; ordinary
   friendly damage still has the engine's existing cost model.
2. **Several dispels in one spell:** overlap and buff-dependent cross-action transitions do not have a full joint
   state model. The conservative aggregate can overprice repeated own-buff losses or omit additional useful removals.
   Contextual bonus handlers beyond the read caster-level/DC fields also need native evidence before claiming an exact
   chance. The single-action shared-roll/order model is tested independently of those utility approximations.
3. **Drain incapacitation:** actual Constitution/other-stat zero and level-zero outcomes are not a separate kill/control
   distribution here. They earn the existing drain-equivalent utility, with no invented HP death credit.
4. **C10 areas and residual coverage:** lingering-area benefit stays omitted; future movement after the cast remains
   the active-area system's responsibility. The inherited 1,189 outcome-gated carriers and 1,108 shared-HD configurations
   remain withheld. No exhaustive runtime-mod/generated-asset coverage is claimed.
5. **Inherited forecasts:** arriving-engager reach/path assumptions, animation/arrow estimates and ordinary watch
   damage/control snapshot freshness remain approximate. Permanent-death checks are fresh; this does not make the
   other watch tables fresh. The RT tracking cap still degrades explicitly past 256 tracked bursts.

The author-set-aside death-trigger proposal remains set aside. These limits are review handover items, not permission
to begin another feature or merge this stack.

## Focused TB/RT replay

Use [the complete review package](../test-builds/AI-Control-FACC-v0.3.60-dev-review92-99-131309f-UMM-test.zip?raw=true), not a mixture of the earlier stage DLLs.
Startup module must be `7ad87f85-5833-4798-baa5-4a5c9f0a9ffc`; ZIP SHA-256 `3406a64691bd47655900e584fcf12e8034ff0623eb293b63dcf35dbbb0c87533`. Keep both `Log.txt` and `combatLog.txt`, including
spawn/rest/manual-order context.

1. Ordinary and fused lines through several enemies with allies between/at the edge: compare the counted recipients,
   in-place turns and bounded lined-up walks. Repeat against blocked spots; no repeated positioning loop is expected.
2. Wracking Ray/control fusions on a low-HP enemy: the first FusionValue row separates drain utility from lethal HP;
   drain utility alone should not erase useful control or earn an HP interruption.
3. Targeted/greater dispels with a high-CL own buff, a suppressed buff and an enemy control effect: compare the removal
   order and the own-buff cost. Also exercise Break Enchantment, Remove Fear and Unbreakable Heart, including an
   inapplicable or immune recipient.
4. A dominated companion, including after it falls: weapons/leaps/Coup remain excluded, its damage is a party cost,
   rescue effects require real applicability, and dust/direct-kill safety stays active. Read a death percentage as a
   bound when the row says so.
5. Delayed RT bursts with an already counted ally whose protection/health changes during the cast: the known-ally skip
   must not bypass the permanent-death recheck. Include a TB/RT mode switch with a pending delivery and retain its wait.
6. Caustic Eruption/other supported periodic fusions and a large summon party: inspect native outcomes, periodic rows,
   and slow-decision stage/time/heap figures. Report exceptions and unexpected vetoes; offline bounds are not measured
   Unity performance improvements.
