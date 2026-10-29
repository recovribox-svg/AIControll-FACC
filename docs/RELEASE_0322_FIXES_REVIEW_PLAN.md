# v0.3.22 fixes playtest review scope

Review the new TB/RT/native logs. Author reports unchanged7.5/10 smoothness and
brief Seelah wall-running early in combat20. Ardub/body-blocking integration is
explicitly deferred. Movement/target-commitment/cancellation changes remain
outside the approved diagnostic-only boundary.

Branch `codex/release0322-fixes-review` starts remote development
`0a8b8f22852ca13a30069b7283ad11ea964e3bb0` and includes pending PR #14 at
`9301356fdd8405743f60958c8afe9678f4edcb81`, including dependency #13.
No merging, stable archive replacement, tag/main/development update or version
bump is authorized here. Supply a distinct0.3.22 UMM test if runtime changes.

## Evidence and hypotheses before code

The corpus contains14TB+7RT encounters. Startup MVID is absent. Previous TTT/
infusion routes run, uncast interruptions remain uncounted, and Holy Water miss
splash no longer produces the earlier false direct-hit check. A real failed
concentration check now exercises the observer successfully.

Six acted Fail commands have native explanations: four TB Divine Zap and one
RT Cure fail20% Arcane Spell Failure; one TB Cure fails concentration7vs18.
These are native outcomes, not broken command dispatch. Source scoring prices
concentration/provoking but has no arcane-failure probability input.

Investigate the native AbilityData.IsAffectedByArcaneSpellFailure gate and the
same RuleCalculateArcaneSpellFailureChance calculator used by the game's UI.
If native-body review confirms it only calculates a temporary chance, use that
runtime result once per decision/caster for affected abilities. Never preview
RuleCastSpell, roll dice, spend resources, copy armor tables or infer a blacklist
from random failure streaks. Discount expected spell benefit before independent
provoking/movement costs and preserve infusion/progressive chance consistency.
Only API/scalar data may be memoized within fresh TacticalContext; native gates
must preserve items/touch/nonarcane exclusions and unknown reads retain fallback.

Combat20=RT6. The stronger wall lead is Leave Stinking Cloud, pending7.8s before
existing stuck recovery; the initial attack was cancelled for a harmful area.
The queried6.2m inspection route is not necessarily the actual native route, and
logged5.5m is displacement, not path length. Missing endpoint/position data
prevents a proven wall cause. Coarse exit validation is only a hypothesis.
If adding diagnostics, use existing queried/live route data and bounded rows;
no new pathfinding or movement/cancellation behavior changes.

## Verification and handover

Retain decision quality and budgets: warm slow reports are ordinary abilities/
weapons with collection overlap; infusion stages are small. No guessed
optimization or controlled performance claim from unmatched fight aggregates.

Run relevant pure tests, full build, compiled/native applicability/query-order/
cost-boundary checks, independent review and retained reader/provider checks.
Record exact runtime/module/package and a focused TB/RT replay checklist. Any
movement diagnostic remains observational. Final draft PR targets development,
records dependencies #14/#13 and distinguishes offline checks from live actors.
