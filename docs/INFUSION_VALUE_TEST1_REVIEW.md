# Infusion-value Test[1]: budget-laptop log review

## Scope, identity and result

The author supplied the complete 1,528-line `Realtime Mode v0.3.22-Test[1].txt` and
18,614-line `Turn Based Mode v0.3.22-Test[1].txt`, then reported that this build felt
**smooth overall** on the budget laptop (previously identified as an i5-8365U).
This review follows the attached Bugfixer protocol: correlate numerical choices,
outcomes and source before changing behavior. Physical line references below are
prefixed RT or TB and refer to those original uploads.

The tested runtime is `0a7ed97`, packaged at `e4e820b`. TB112 confirms the expected
module MVID `4abd89c4-7b64-4e6f-8ae5-5d96f61ed556`. The RT excerpt begins at combat
and has no build marker; its binary identity cannot be independently verified from
that excerpt. There are four complete fights in each actual mode: 255 issued RT
commands and 191 issued TB commands. No AI Control exception/error is recorded.
This is useful functionality evidence, not a guarantee that every calculation is right.

**The new infusion value was not exercised.** No Bowling/Pushing contribution or
`[Kinetic]` added-value line appears. TB16792/17934 and RT1118 show Burning Infusion
off/not-started; the accompanying coverage reports level 2, burn 0/3, gather Off
and Fire Blast burn 0. Burning is outside the new slice even when active. These
logs do not establish either success or failure of active Trip/BullRush valuation.
That requires a character with the supported infusion already active and affordable.

## Performance: measured decision work, not frame rate

| Fight | Summary line | Decisions | Median ms | p95 ms | Maximum ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| RT1 | 389 | 234 | 39.5 | 105.2 | 163.3 |
| RT2 | 755 | 174 | 48.1 | 199.4 | 1259.7 |
| RT3 | 1076 | 113 | 38.7 | 182.8 | 212.5 |
| RT4 | 1518 | 146 | 34.7 | 218.0 | 319.9 |
| TB1 | 17424 | 106 | 35.4 | 188.6 | 503.4 |
| TB2 | 17860 | 77 | 51.2 | 138.9 | 177.2 |
| TB3 | 18130 | 45 | 26.1 | 83.0 | 87.2 |
| TB4 | 18599 | 91 | 42.5 | 116.7 | 132.0 |

RT totals 667 decisions / 40,346 ms of summed work and 459 cooldown skips; TB
totals 319 / 15,782 ms. Do not average the fight medians or percentiles into an
overall percentile. The per-decision slow logger only prints details above 250 ms.
The logs do not provide all per-actor timings, stage durations, FPS, CPU usage or
selection/UI timings. The author's smooth-play report is retained as such.

- RT547's 1.26-second event is **Aeiluvetar choosing nothing**, with garbage
  collection and enemy assessment during the interval, zero new profiles. The
  flags do not identify which stage or allocation caused how much delay.
- TB16713's 503 ms event is **Camellia's first Grease decision**, with 31 newly
  constructed profiles and enemy assessment. Later TB maxima are 87–177 ms.
- Six RT Orvath decisions exceed 250 ms, including DC boosters, Sleep, Color Spray
  and an allied healing item (RT555,1195,1199,1224,1231,1341). This does not identify
  an infusion-search bottleneck: no such search exists in this slice.
- `BuildContext` calls `EnemyThreat.Refresh`; its shared enemy assessment includes
  native attack/ability/heal estimates. The first actor to trigger refresh can pay
  that work regardless of class. The GC/assessment event is a profiling lead, not
  a reason to change threat math or add a cache without measurements.
- Older supplied stable logs also have cold-profile/GC spikes and overlapping
  fight timing ranges. Hardware/settings/encounters are not matched; neither a
  regression nor a speedup is established by comparing their medians.

No performance algorithm, decision budget, target cap, animation speed or cache
was changed in this review. The user reports smooth play, and these logs do not
isolate a justified performance fix.

## Confirmed diagnostic bug: fixed

RT33/780/1122 label Progressive policy messages `(TB)` despite RT decisions and
commands. `TacticalEngine.ApplyProgressive` hard-coded both `"TB"` and
`enemiesComplete: true` when calling `ProgressiveGate.HeldBack`.

The correction derives both from `CombatMemory.IsRealTime`:

```csharp
bool realTime = CombatMemory.IsRealTime;
var held = ProgressiveGate.HeldBack(ctx.Unit, ctx.Enemies, items,
    realTime ? "RT" : "TB", enemiesComplete: !realTime);
```

This also stops filtered RT enemy lists from producing misleading complete-list
boss-presence transitions. Source review confirms `enemiesComplete` gates only
`LogBossTransitions`; `CombatMemory.ActiveBosses` is diagnostic bookkeeping, with
no selection reader. Actual boss bypass is computed separately for each option.
No score, tier-policy formula, eligibility, command or ownership rule changes.

Validation: full Release build against supplied real game references, zero
warnings/errors; all **110 tests passed**, zero failed/skipped. No new test was
added merely to mirror this diagnostic branch. In-game confirmation of the new
labels remains pending. This is not a fix for the model gaps below.

## Pre-existing decision/model findings: unresolved

### 1. Sleep can over-credit a shared Hit Dice budget

At TB16915, Sleep scores **112.9** against Color Spray **34.8** and an attack
**18.6**. A DC booster increases Sleep to **120.1** at TB16920. TB16925 records
other recipients reached rather than the aimed necromancer. RT833 similarly
chooses Sleep **83.7** over Color Spray **52.8**; RT847 records a different reached
recipient. These are log correlations, not a reconstruction of the correct score.

The source gap is confirmed: `ScoreAbility` values each area recipient independently.
`AbilityProfile.CheckFor`/`WithSharedValues` evaluates the initial shared-HD range
for a recipient; it does not consume the native pool in recipient order through
the supplied Sleep graph's shared-value subtraction. Thus several recipients can
be credited against the same limited allowance, also inflating the boost's value.

Repeated Sleep on Cambion (TB17462–17471) is not by itself a simple retry bug: one
cast completed before another, the first spent Swift and left Standard available,
and the later score can seek other area recipients. Native pool consumption by
earlier/already-affected recipients still needs a specific ordered forecast.

**Next focused task:** verify native selection order and shared-value consumption,
including resistance, saves, existing effects and installed-mod parameters; then
implement a narrowly tested budget-aware score. Do not restore the withdrawn
forecasting system or substitute a flat Sleep penalty. The exact corrected score
and complete installed runtime budget were not reconstructed in this review.

### 2. Exact-path failures cause avoidable replanning and can follow a spent booster

All 12 TB dispatch rejections occur in fight 2. At TB17540–17546 Orvath spends a
DC booster for Color Spray, then its path endpoint lacks line of sight; he instead
summons Cambion. At TB17570/17573/17577, Camellia successively tries Flare, an
approach attack and Demoralize against Dretch; exact path/LoS/reach checks reject
them before issuing a command. Inspired Rage is selected afterward. RT815–818
and 837–853 also show obstructed shots cancelled and different plans selected.

`EvaluateAbility` and weapon scoring use approach estimates; the dispatcher checks
the real path endpoint and movement budget. `RecordRejected` suppresses a failed
candidate key and marks no-sight/out-of-reach targets for movement handling, but
other action/range pairs can still be considered. The safeguards work; repeated
failed plans still cost work, and a booster can precede an infeasible follow-up.

**Next focused task:** evaluate a narrowly scoped chosen-follow-up preflight or
failure memo keyed by positions/range/LoS with proper invalidation. Do not run
full pathfinding for every option or globally blacklist an enemy: another range,
action or no-LoS ability may be valid. No path or booster policy was changed here.

### 3. Energy protection is valued without preserving its energy type

RT1209–1211 values Protection from Cold at **10.7** raw, **5.3** after item policy,
using an incoming-damage appraisal. `BuffPartsReader` maps ProtectionFromEnergy and
AddDamageResistanceEnergy to EnergyResist without preserving the energy identity.
`AppraiseParts` pools that category and assumes a 15% share of incoming damage.
Different energy protections can therefore receive the same modeled benefit
without evidence of matching incoming energy.

This is a source-confirmed approximation/coverage limit, not proof this particular
cast was useless: the logs do not list every enemy's cold-capable offense.
**Next focused task:** retain native energy identity and base benefit on relevant
threat/observed damage. A generic coefficient change would not repair the mismatch.
Shared buff readers and weights were left unchanged in this slice.

### 4. Color Spray lacks cone-recipient appraisal

The supplied Color Spray has a native Cone projectile, but no AbilityTargetsAround.
The stable reader recognizes its control and no-attack-roll behavior; it does not
turn that cone into an area-recipient model. It primarily values the aimed target,
omitting the cone's complete beneficiary/friendly-fire geometry. RT824–826's single
miss after a walk is not enough to prove that gap caused that command's failure.

**Next focused task:** one native cone-delivery audit and reproducible affected-unit
cases before changing geometry. No new angle search or general area-form work was
introduced here. This limitation predates the active-infusion change.

## Functionality that the logs do support

- Blasts compete with attacks rather than being unconditionally skipped: TB17324
  prefers Fire Blast **8.8** to crossbow **5.6**; TB17242 prefers crossbow **10.9**
  to Fire Blast **9.4**. No flat blast preference is justified by these examples.
- Boost/cast coupling is numerically coherent in some sequences: RT53–58 values
  a +1 DC boost at **2.7**, then Color Spray rises **40.7 -> 43.5** and is issued.
- Healing and revival take danger/opportunity cost into account. TB18236–18241
  chooses attack **16.4** over heal **15.6** at 53% HP, then heals after the attack.
  RT565 rejects a negative-value dangerous revive; later conditions can improve
  it. These are not proof of universal optimality, but not a missing-heal bug.
- Many `UnitAttack result=Interrupt` entries say `acted=True`; they must not be
  counted as failed attacks. Repeated same-round casts/items are not automatically
  illegal when native Swift/Move/Free mechanics apply. `Item:` does not identify
  whether an ability is a potion, scroll, wand or another item.
- Failed/ineffective commands trigger suppression and replanning. In RT70–76 a
  failed Demoralize is not counted as cast and a different option follows.
- Area escape, withdrawal, attack modes, buff restraint and post-fight toggle
  restoration are visible. The Fire Blast `IsEqual` warning does not establish
  double damage: the existing damage reader handles mutually exclusive branches.
- Cambion/Quasit are sometimes explicitly left to native game control. Their
  unlogged choices cannot be attributed to this mod; enemies can share those names.

## Compatibility, delivery and stopping point

TB276–278 records WrathPatches' failed selected-character patch; startup also
contains external blueprint/integration errors. They are external-mod evidence,
not a proven cause of a new AI Control regression. No global hook or dependency
change is proposed on that basis.

This review keeps PR #6 draft. The diagnostic correction and this report are the
only code/review changes; larger source-confirmed model limits are explicitly not
marked fixed. Keep the smooth, ordinary-play evidence and a supported-infusion
positive test separate. A new diagnostic test ZIP uses a new filename/MVID while
retaining the original package, so current test1 logs remain traceable. Installing
that diagnostic update is optional: it changes reporting, not tactical choices.
