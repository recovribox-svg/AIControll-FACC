# v0.3.22 fixes playtest: native spell failure and Seelah navigation evidence

Author feedback: unchanged **7.5/10 smoothness**, plus brief wall-running by
Seelah early in real-time combat20. Reviewed both complete AI excerpts and the
native CombatLog: **14 TB encounters followed by 7 RT**, explicitly numbered 1–21.
Native references below use raw LF physical lines because mixed CR/CRLF changes
universal-newline numbering. Files have 4,495 TB / 2,354 RT / 29,512 native LF endings.
Neither AI excerpt contains startup Build/MVID. Recent pathways run, but exact
installed module identity is not independently proved by filenames or excerpts.

Ardub integration/body-blocking work remains explicitly deferred. This task adds
no Ardub dependency and changes no navigation, cancellation or movement policy.

## Native failures reveal a missing scoring input

All six completed Fail/actedTrue commands have actual native explanations:

| AI reference | Seelah's action | Native LF | Cause |
| --- | --- | --- | --- |
| TB2006 | Divine Zap, Dretch |5679–5681|20% Arcane Spell Failure |
| TB2377 | Divine Zap, Skeleton |6370–6372|20% Arcane Spell Failure |
| TB3444 | Divine Zap, Skeleton |9827–9829|20% Arcane Spell Failure |
| TB4047 | Divine Zap, Horse |11528–11530|20% Arcane Spell Failure |
| TB3396 | Cure, Camellia |9654–9657|Concentration7 vsDC18 |
| RT1364 | Cure, self |20669–20671|20% Arcane Spell Failure |

These are not silent effects, dispatch failures or evidence to blacklist those
spells. In this loaded game the actual ability sources are affected; do not infer
arcane/divine applicability from the spell name or companion class.

Source appraisal already includes casting concentration and provoking risks,
but omits native arcane failure probability. That can overvalue an affected spell
relative to a weapon or another ability. A failure can remain legitimate after
this correction: pricing an 80% chance of casting does not remove the other 20%.

### Correction and boundaries

`CombatMath.ArcaneCastingChance` asks the same dedicated native calculation used
by the spellbook UI: `RuleCalculateArcaneSpellFailureChance(caster).Result`.
It never previews RuleCastSpell, rolls D100, spends resources or copies armor
percentages/modifier tables. Actual AbilityData.IsAffectedByArcaneSpellFailure
controls eligibility, including source/spellbook/mythic/held-touch exceptions.

`TacticalEngine` lazily retains the caster result only in a fresh TacticalContext.
Each evaluated ability checks eligibility before consulting that cache; unaffected
abilities keep factor 1. Affected spells share at most one calculation per current
context. Current native and inspected TTT modifiers calculate caster armor/fact
state; current DC/metamagic boost simulations do not change that state. A future
armor-changing counterfactual must revisit this cache boundary.

The native percentage discounts common expected spell benefit **before**
ProvokeLoss computes its effect loss and independent opportunity damage/drop
cost. Movement risk costs remain outside the discount. Infusion delivery and
Progressive viability receive the same factor exactly once. Existing booster
gains already use scaled candidate values and are not multiplied again. No
shared raw-damage/EffectChance formula, priority, threshold or spell blacklist
changes. Both combat modes use the common path.

Bounded `[CastChance]` notes explain native failure percent and benefit multiplier
once per actor/ability/round using existing ReportOnce. Counterfactual calls that
disable explanation do not produce ordinary notes. Logger/query/applicability
failures retain the existing factor 1 fallback. Cache stores a nullable scalar,
not actors, contexts, rules or paths beyond a decision.

Native UI, calculator, eligible-source gate and four vanilla handlers were
inspected. The supplied TTT Core 0.7.15 matching handler also modifies only its
temporary rule. Rulebook still dispatches unknown third-party subscribers; this
is the canonical UI preview, not proof of every external handler or live actor.
Generic non-arcane failure mechanisms remain separate from this correction.

## Earlier fixes now have useful live coverage

TB3395 explicitly reports Cure concentration failed 7 vs 18, native9654–9657 agrees,
and no false silent/target strike follows. This is the first supplied live
failed-concentration exercise. Hand hit Seelah for 14 just before it; the later
self potion genuinely heals 7. It is not a pair of silently failed heals.

Five unacted Color Spray/Cure sequences atRT444/459/567/1023/1222 correctly log
ended-before-cast/not-counted. Neither AI file has a did-nothing target/silent
strike. Native979–999 records a missed Holy Water throw and two unmodified 1 Holy
splash packets, the earlier false calibration shape; no DmgCheck appears now.
This supports the prior guards, not independent DLL proof or all splash cases.

Six automatic Burning activations complete with native DC19/direct Fire:
TB345/1466/1887/2444 andRT210/1923. Native packets include758–772,3843–3856,
5173–5187,6592–6605,15149–15162 and24384–24398; periodic ticks also occur.
Off restoration and burn-block recovery remain functional. Only the exposed
Burning family is exercised, not all supported infusions.

TTT sampled routes: TB 128 estimates/9 immunities/196 absent parts/3 observations;
RT 47/3/116/2. No provider failure appears and no Fire Blast command targets Dretch
or Quasit. Every fight fills 24 energy rows, so later absent traces are not skipped
appraisal. Existing approximate stacking/pool/mixed-provider limits remain.

Native packets confirm many actual heals; no zero-effective heal was found.
Sleep retains useful group selection: TB1774 picks the 72-point Sharpshooter/
Mongrel group over 6.7 alternatives; native4730/4739 records one passed and one
failed save. TB216's repeat values the remaining Werewolf while the old Cultist
remains asleep, with reduced group value. No forced recast or headcount-weight
bug is proved. Area-centred casts reaching other recipients remain legitimate.

## Seelah's combat20: diagnostics only

Combat20 is **RT6**, AI1567–1766/native22156–23120. There are two distinct moves:

- Her early attack approach is cancelled after 3.4 logged seconds because she
  enters a harmful area, not because the no-progress timer fires.
- The stronger wall lead is Leave Stinking Cloud atRT1646–1683. The inspection
  query reports 6.2m/~2s, but the actual UnitMoveTo remains pending 7.8s before the
  existing recovery detects 6s without getting nearer, about 2.1m left. It cancels
  and records local escape refusal. She subsequently attacks Brute successfully;
  native damage includes 14 and 8HP. No repeated escape-command loop is shown.

The query route is inspection only; real-time native navigation chooses its own
execution route. The command's 5.5m moved field is start/end displacement, not
traveled distance. No wall geometry or actual waypoints were supplied. Exit
validation tolerates coarser navmesh/trace errors than the 0.15m arrival radius,
but that is only a source hypothesis, not proof of this incident's cause.

The existing Walk row now adds requested goal, start, queried endpoint and 3D
gap, explicitly labelled inspection-only. At an existing stuck cancellation a
single extra row reads current position, command goal/radius, native destination,
existing path end and next waypoint. Private destination/index fields use cached,
type-checked reflection metadata; unknown access cannot affect controller flow.
No path copy, extra search, new loop, per-frame trace, timer, target/cancellation
change, endpoint snap or exit-rule modification is introduced. Raw 3D gaps are
not a new native range/LoS test. Replay must establish geometry before a fix.

## Performance and validation

| Mode | Decisions | Total ms | Mean ms | Fight median range ms |
| --- | ---: | ---: | ---: | ---: |
| TB,14 fights |695|30,774|44.28|19.7–46.4 |
| RT,7 fights |857|45,411|52.99|18.5–36.2 |

RT records 685 cooldown skips. Nine >250ms reports comprise one cold profile build,
seven collection-overlap events and one warm 251ms event. Ordinary ability/weapon
stages dominate; warm active infusion is at most 0.3ms/search 0.2ms. Collection
count overlap does not measure GC duration or identify allocation ownership.
Different encounters prevent controlled comparison with preceding means. Keep
native appraisal, quality and existing budgets. No new speedup is claimed.

Final integrated module: `bccf1618-8aa9-4a3d-9c3f-d68075ebafb8`.

- Full Release against supplied game/UMM references: zero warnings/errors.
- `dotnet test tests/CompanionAI.Tests`: 164 passed, zero failed/skipped.
- 39 compiled/native arcane-chance guards pass: actual applicability/cache CFG,
  canonical UI query, no dice/cast/resource preview, benefit before risk costs,
  exactly-once infusion/Progressive factors and bounded explanation.
- Retained 64 Settle vectors/406 assertions, 20 missed-sample assertions, native
  concentration/Selective/HD, activation/resistance, memo, Grease, substance,
  Free/Swift, 486 cost/quiver vectors, Rare Metal and cone checks pass.
- Actual supplied Core 0.7.15: 23 metadata/native, 9 absent, 24 binding/empty-part and
  8 each of three mismatched-shape assertions pass; supplied bytes unchanged.
- Reader: 4,097 loaded exports/one known Achaekek failure; 1,220 ability rows,
  zero reader errors, normalized details unchanged against release. Partial
  export coverage. Independent arcane integration review found no blocking issue.

Offline checks do not run initialized actors, native chance handlers, commands,
Unity/Mono timing or a prospective wall fix. This new build requires live replay.
Do not ship verification stubs, game/TTT DLLs, native dumps or local checkers.

## Branch and replay handover

Isolated `codex/release0322-fixes-review` starts remote development `0a8b8f2` and
includes pending PR #14 at `9301356`, which includes PR #13. Do not merge/integrate without
review approval. UMM stays 0.3.22; preserve stable package/tag/main/development and
all older archives. Ardub integration remains on the backburner.

1. For affected spells, check CastChance's native percent and changed candidate
   values. At 20% failure expected spell benefit should use factor 0.8. Actual
   random failures remain possible and must not blacklist useful spells.
2. Compare unaffected sources, items, held touch, no armor, armor/shield reducers,
   affected granted spells and current TTT handlers in TB/RT. Native eligibility
   decides, not spell names. Recheck concentration and genuine silent safety.
3. Recheck native Burning/DC/burn/off/player ownership, resistant-target avoidance,
   group Sleep and real healing; decision quality and budgets remain intact.
4. Replay combat20 or the same corridor/cloud escape. Include new queried-endpoint
   and stuck native-route snapshots. No movement improvement is claimed yet.
5. Include startup/MVID, exact ZIP filename, full AI logs and native CombatLog.
   Compare same-save/settings/mods ordinary actors, Orvath/summons and idle
   selection; separate native/mod-handler cost from unmatched fight aggregates.
