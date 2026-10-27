# Test[5]: concentration failures, Sleep aim evidence and preserved performance

Reviewed the ordinary TB and RT logs, the miniboss log, the native CombatLog and
the author's playtest note. The author rates smoothness **8/10**, with definite
improvement over Test[4]. Preserve that improvement and the working decision
features; these findings do not justify broad pruning or new priority weights.

Native references below use **raw LF-delimited physical lines**. Mixed CR/CRLF
formatting gives different numbers in universal-newline readers. Native order is
seven ordinary TB encounters, nine RT encounters, then the miniboss. The miniboss
AI log starts briefly in RT before switching to TB. All three AI excerpts omit
the startup MVID. Stage timings and committed-approach vetoes confirm the new
pathways are exercised, but do not independently establish the exact DLL identity.

## 1. Fixed: visible concentration failure incorrectly taught a silent failure

Two correlations establish the defect:

- RT376: Seelah's Cure Light Wounds ends `Fail`, `acted=True`. RT393 counts
  `did nothing ... game never finished executing it (1/2)`. Native CombatLog
  14770–14773 explicitly records her **failed concentration check, 5 vs DC15**,
  after an opportunity hit.
- RT2113: Orvath's Sleep on Mongrel ends `Fail`, `acted=True`. RT2114–2115 counts
  a target-specific no-reach strike `(1/2)`. Native CombatLog24998–25001 explicitly
  records **failed concentration, 13 vs DC18**, following a longsword critical.

These are reported native-rule failures, not evidence that the spell cannot
work on that recipient. The two-strike safety net could wrongly stop selecting a
valid spell/target after another such failure. These excerpts demonstrate first
strikes; they do not demonstrate that a second-strike blacklist actually happened.

`CastOutcome` already distinguishes known RuleCastSpell refusals from silent
failure, but did not observe `RuleCheckConcentration`. Native UnitUseAbility can
fail before the RuleCastSpell refusal route supplies evidence. Settlement then
sees no spell effect and charges a silent or no-reach failure. Merely recording
general evidence is insufficient: `Finish` judges watched-target reach before
its final success/evidence check.

The existing CastOutcome subscriber now observes completed native concentration
checks. For a **failed** check matching the pending caster and root, touch-delivery
or ConvertedFrom ability, it sets refusal evidence and turns off target-reach
judgment. It does **not** claim the target received an effect. A concise `[Cast]`
line reports the native result/DC and explains that no silent strike is charged.
Successful, null and unrelated checks leave the pending watch unchanged. Genuine
silent-cast detection remains in place.

This uses the native event's completed values, triggers no rule or gameplay
action, and changes no casting-risk formula, resource, command or movement policy.
Native signatures and both damage/difficult-casting call sites were inspected.
Actual live event ordering and settlement remain replay checks.

## 2. The two Quasit Sleeps are real; a better cluster aim is not yet proven

The author's observation matches ordinary TB fight7, starting TB1623 / native
CombatLog6712. The actual Quasit is `CR2_QuasitStandardSeelah[17890]`.

- TB1681–1684: Sleep on Quasit scores **32.1**, then Quasit passes Will13+6 vsDC19.
- TB1686–1693: a DC boost scores2.7; the next Sleep scores **34.8**, then Quasit
  passes19+6 vsDC20. Native CombatLog6955–6962 and6968–6975 confirm both saves.
  No other Sleep save occurs between these casts.

Both occur in Orvath's R1. The first completes immediately and the native action
state still permits a standard action afterward; the second is a normal 3.1s cast.
This is not merely one command logged twice. The character's installed native/
modded extra-action behavior was not established by this target-choice review;
no action-economy repair is inferred from that trace alone.

The current engine considers all distinct enemy centres for ordinary harmful
point areas. Sleep has no first-enemy or infusion-search aim cap. Each centre is
scored using native reach, the shared finite HD pool in native recipient order,
SR/save chances, eligible recipients and current state, then normal risks and
policy. Recipient value is weighted by threat and urgency, not just headcount.
The Quasit was assessed as a dangerous Cause Fear source on Seelah; nearby humans
were not necessarily equally valuable, all in the same legal radius, within the
remaining pool or free of existing control. Camellia's preceding Grease also
changed the nearby pack's state.

The supplied log retains only the best aim for each ability. Human-centred Sleep
values/rejection reasons, precise positions and complete weighted contributions
at that instant are missing. Thus **the cluster may have been better, but these
files do not establish its legal expected value exceeded32.1/34.8**. Two successful
saves alone do not establish bad targeting or warrant a repeated-save blacklist.

Working multi-target behavior is visible elsewhere: TB173–179 catches a
Sharpshooter and Wizard; TB839–844 reaches two Mongrels from an Evoker centre;
TB1114–1118 produces two Fighter saves. In fight7 itself later actions use other
centres. No fixed Quasit preference is present.

### Bounded diagnostic, with scoring unchanged

A new `[HD-Aim]` explanation accompanies an actually chosen ordinary shared-HD
control action. It samples up to **twelve** already-appraised centres, recording
existing value/rejection reason, reach/walk and scored-risk information, and the
existing HD-eligible count with at most three recipient identities per centre.
The output explicitly distinguishes eligibility from positive benefit or a
guaranteed failed save. Rows are labelled **after scored risks, before selected-
path preparation/policy**; the selected candidate's current value is separate.

The cap limits diagnostic storage only: every remaining centre still receives
ordinary scoring. Names/strings are formatted only when a real action is selected.
There is no second appraisal, native selector/rule/context query, pathfinding or
recipient scan of the battlefield. Preview and counterfactual captures remain
off; cones remain outside this explanation. No scoring weights, HD math, cluster
bonus, minimum-recipient rule or target blacklist is added.

## 3. Infusions remain functional; initial fire optimism remains unresolved

All **five** logged Burning activations complete `Success`, `acted=True`:
TB1372–1376, TB2056–2060, RT72–81, RT2303–2310 and miniboss174–181. Low gains
exercise the existing costless-Free threshold; subthreshold prospective gains
remain off. Native CombatLog5435–5438 shows3 Fire from Burning on Cultist Champion;
CombatLog8873–8885 shows a Skeletal Champion fail DC19 and take3 Fire from Burning.
Passed saves elsewhere are not activation failures.

Stopping Burning restores usable blasts at TB2291, RT239/2508 and miniboss697;
original-off restoration appears at TB1610. No thrashing or exceeded burn cap is
established. Other recognized infusion families and player-ownership/management-
disabled interactions still lack sufficient live coverage for blanket validation.

Fire estimates remain optimistic in several resistant-target examples. TB1538–1542
values infused Fire Blast11.6 on Dretch, while CombatLog6352–6360 records0 Fire
against resistance10. TB2292–2295 values an ordinary blast15.5; CombatLog10022–10030
records another zero packet. Miniboss173–181 adds Burning+4.3, but the Dretch saves
and takes0 direct Fire at CombatLog31253–31267. Conversely, actual critical blasts
can penetrate resistance: CombatLog23326–23337 deals8 Fire and kills a Quasit.
A blanket enemy/type blacklist would discard real outcomes.

No native-reduction exception diagnostic fires in these excerpts. Source/native
inspection confirms supplied Fire Blast branches reach the shared helper, native
registration matches its part lookup, chunks initialize with native values, and
final calibration cannot turn a zero result positive. Missing parts, previously
uncapped observations, raw input inflation and optimistic native results remain
unresolved routes; no new damage formula is justified.

A bounded `[Math] energy estimate` trace now reports **existing** raw, observed,
native remaining and final remaining values, plus the return route: observed,
immunity, missing part, native estimate or exception. Direct and infusion reads
are labelled separately. It adds no defence query and preserves the existing
return calculation, including successful-hit bypass evidence and the earlier
censored-observation correction. ID-only keys are cleared with DamageObserver at
combat boundaries; at most **24 rows per fight** are emitted. Other energy reads
can consume that sample cap, so it does not promise every later decision is shown.
The resistance discrepancy is **not claimed fixed**.

## 4. Existing spell safety is exercised; no movement-policy expansion

RT40/642/819/1410/1947/1952 contain real committed-approach Grease vetoes. In the
last pair, both Orvath and Camellia withhold a new circle while Seelah approaches
Dretch. This establishes the new gates are not wholly inert. It is six guarded
candidate appraisals, not six proven prevented knockdowns. The conservative scope
from §69 remains: whole-arrival containment, known fixed circles and equal
heights; partial overlap, slopes and unknown geometry retain existing appraisal.

The author's Selective question was checked against the existing native-rule
adapter. The guard receives the harmful-area result after
`SelectiveSparesAllies`; that method requires actual Selective metamagic and the
possibly mod-patched native area's `CheckSelective` behavior. When the game says
allies are protected, the new veto is bypassed. A toggle label alone does not
establish every lasting area is safe. No new Selective rule or blanket exception
is added.

There are genuine RT stuck approaches and emergency/status/target-death
interruptions. Existing movement, target commitment, navigation and cancellation
restrictions remain; this review does not authorize or implement broader changes.
In-range touch-approach cancellation still leads to cast reconsideration, as
RT370–376 demonstrates. Its subsequent concentration failure is the separate bug
fixed above. TB `Stand up` commands ending immediately Interrupt/acted=False are
the deliberate short-walk trigger; following decisions are upright.

Partial cures still make revival progress: miniboss253/257 changes needed HP11→4.
Do not cap those legitimate accumulating heals. A separate Aid Another first
no-reach strike after an unacted interruption (RT2707/2753) remains an event-ordering
lead, not a claimed correction. Other-mod WrathPatches blueprint-owner errors at
TB827/828/1674 are not AI Control exceptions and do not justify rewriting those
blueprints.

The author's second-element report is **closed**: the grant failed in the other
mod and the blast was unavailable manually too. No ability-discovery, quickbar or
second-element support fix is made from that corrected observation.

## 5. Performance feedback and samples support preserving the current approach

| Samples | Decisions | Total decision ms | Arithmetic mean ms | Per-fight median range ms |
| --- | ---: | ---: | ---: | ---: |
| Ordinary TB | 404 | 16,675 | 41.27 | 18.1–29.6 |
| Ordinary RT | 1,222 | 36,307 | 29.71 | 9.2–52.5 |
| Miniboss TB | 112 | 3,662 | 32.70 | 22.3 |
| Miniboss RT opening | 1 | 61 (rounded) | 61.3 | 61.3 |

Ordinary RT also records 841 cooldown skips. These measure decision work, not FPS
or whole-session latency; overall medians/p95 cannot be recovered by averaging
fight percentiles. Test[4]'s ordinary means were 65.1ms TB and 62.4ms RT, but the
encounters, counts, states and work differ. The lower samples and 8/10 smoothness
feedback are compatible with improvement; they do not prove a controlled speedup
factor or assign it to a particular memo.

Seven warm >250ms records all overlap collection, landing in weapons/modes or
ordinary ability appraisal. Nested active-infusion time is 0.0–0.1ms in those
spikes; neither it nor infusion search dominates them. A separate cold 388ms TB
sample builds 20 profiles and spends 225.5ms in context/threat without a collection
flag. Collection overlap does not establish allocation ownership or time caused.
No slow stand-up-preview report occurs; that is not proof such previews have no
cost. The inclusive stage totals and nested labels are consistent.

No additional performance tuning is justified by this pass. Preserve the prior
local reuse, full decision scope and the improved live result; fix the confirmed
outcome-classification bug and gather bounded evidence for the unresolved leads.

## Verification and handover

Branch `codex/test5-logic-fixes` starts at remote development `f51189f` and includes
pending PR #10 head `09249af` (including PR #9/#8/#7/#6) as explicit dependencies.
Main/shared development remain unchanged. Release metadata stays 0.3.21 for intended
v0.3.22. Runtime commit, exact module and package identity will be recorded in the
[test-build handover](../test-builds/README.md) after final validation.

- **164 existing pure tests pass**, zero failed/skipped. No new boolean-helper
  test is added for the game-coupled observer change.
- Full Release with supplied real game/UMM references: **zero warnings/errors**.
- Existing native checks pass: thirteen memo identity assertions, five actual
  Grease structural gates, twenty activation metadata cases, Free/Swift thresholds,
  eighteen recognized/two withheld layouts, 486 native cost/quiver vectors,
  Rare Metal and cone metadata/bindings. Compiled activation-adoption and
  censored-resistance guards pass.
- New compiled checks pass for concentration refusal gates/flag writes, the
  Selective guard chain and HD diagnostic capture/selection boundaries. Source
  review confirms the energy trace preserves return values and native query
  order/count. Six detached native Grease assertions verify unpatched Selective
  off/on/off means affects/spares/affects allies. No actor, rule, spawn,
  AutoMetamagic or Tabletop Tweaks handler was run by those fixtures; live event
  ordering, actual cast parameters and command outcomes remain unverified.
- Ordinary blueprint-reader audit remains partial: **4,097 loaded**, one known
  unrelated Achaekek export failure, **1,220 rows / zero reader errors**. Every
  normalized verdict/detail row matches Test[4].
- Validated module MVID: `5bc34728-8614-4cd9-a9ef-046868de2d4f`.
- **No in-game run of this new build.** Supplied Test[5] outcomes concern the
  preceding build; new observer settlement and diagnostics require replay.

Focused replay, same save/settings/mods: verify exact startup identity; repeat the
Quasit/human-cluster case and include `[HD-Aim]` lines, native saves/SR and positions
if practical. Check another useful multi-enemy/immune-centred Sleep to preserve
working behavior. Provoke an ordinary failed concentration check and verify the
new `[Cast]` explanation without a silent/no-reach strike; also retain a genuinely
silent-effect case if reproducible. Compare resistant-target Fire/Burning before
and after observations, including `[Math] energy estimate` routes and native
packets. Recheck both-mode infusion activation/burn/restoration and the existing
FF/Selective boundary, without expecting broader movement protection.

Continue providing complete AI logs and native CombatLog, ZIP filename and
smoothness observations, including Orvath, summons and ordinary companions. The
native file remains essential for separating failed game rolls from broken effects.
