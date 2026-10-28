# Released v0.3.22: resistance-provider compatibility and reach-standoff evidence

Reviewed the TB/RT AI logs, native CombatLog and the author's Hand of Hosilla
observation. All native references below use **raw LF physical lines**: mixed
CR/CRLF formatting produces different universal-newline numbers. The combined
record is **eight TB encounters followed by seven RT encounters**. Combat #13
is RT fight 5, not TB fight 13.

Both AI excerpts omit their startup Build/MVID. The new energy/HD/stage messages
establish the released pathways, but do not independently identify the exact DLL.
The author gave no new smoothness rating here; the earlier 8/10 belongs to Test[5].

## 1. Confirmed compatibility gap: the vanilla reduction provider is absent

The released diagnostic resolves the earlier uncertainty:

- TB has 188 sampled energy reads: 187 `no reduction part`, one `observed`.
  RT has 168: 166 missing part, two observed. Thus **353 of 356 samples** use the
  missing vanilla provider fallback. No native-estimate/exception route appears.
- TB45/47 reads Fire Blast against Dretch/Quasit as raw **7.50**, missing part,
  remaining 7.50. The basic raw blast mean is not inflated in this example.
- TB729 reads the same Quasit's literal Burning tick as raw **3.50**, again with
  no vanilla part. TB730–739 then activates Burning and completes the blast.
- Native CombatLog1775–1811 matches Quasit `CR2_QuasitStandard[17197]`: accepted
  burn 1, hit, Burning Reflex against DC19, then **zero direct Fire**,
  `1d6+4−7`, explicitly **Fire Resistance 10**. Seven absorbed is only a lower
  bound from the packet; it does not imply the actual resistance is seven.

The old first-read discrepancy is therefore demonstrated at the **missing-part
return**, rather than an exception, missing damage-helper call or excessive raw
blast rank. The bounded sample can fill before later reads; an absent later line
is not evidence that appraisal was skipped.

### Why Tabletop Tweaks explains the missing part

Pinned public Core/Base source shows the DR rework replaces native resistance
components with TT components. Their OnTurnOn registers facts through
`Owner.Ensure<TTUnitPartDamageReduction>()`. The replacement is a separate
OldStyleUnitPart, not a subclass of native UnitPartDamageReduction. Querying only
the native type legitimately returns null even though actual damage is resisted.
Energy-immunity registration is also routed to the replacement part.

Relevant pinned source:

- [TTT Core resistance runtime](https://github.com/Vek17/TabletopTweaks-Core/blob/e76594f61b539c5243f74f150bb7afee604e1a48/TabletopTweaks-Core/NewComponents/OwlcatReplacements/DamageResistance/TTAddDamageResistanceBase.cs)
- [TTT Core reduction part](https://github.com/Vek17/TabletopTweaks-Core/blob/e76594f61b539c5243f74f150bb7afee604e1a48/TabletopTweaks-Core/NewUnitParts/TTUnitPartDamageReduction.cs)
- [TTT Base DR rework](https://github.com/Vek17/TabletopTweaks-Base/blob/76c555f1f62f1f9c6fe4b378634c14ffdfec0c7c/TabletopTweaks-Base/MechanicsChanges/DRRework.cs)

The author then supplied the actual **TabletopTweaks-Core 0.7.15** DLL (manifest
requests game 2.5.0). Its MVID is `cdfed43f-53a0-4186-a181-75496eecec8f`; SHA-256
is `aba06b62e8433febb57f9a703bfe5224ad1063676729fca8263644a3b3f535b0`.
Binary metadata and method bodies confirm the same registration and exact APIs:
`TryInitialize()`, `IsImmune(DamageEnergyType)` and
`EstimateDamage(float, BaseDamage, ItemEntityWeapon)` on
`TabletopTweaks.Core.NewUnitParts.TTUnitPartDamageReduction`.
This validates the installed binary's API/read-only preview structure; it is not
live proof of a particular initialized actor's current part/settings or full
10-point preview result.

### Targeted correction

`CombatMath.AfterTargetReduction` now tries the optional provider **only when
the vanilla part is null**. `ModdedDamageReduction` lazily binds exact public
signatures on the already loaded Core assembly and closes native `Get<T>` for
its actual part. It never loads TTT as a new dependency or installs a part with
Ensure/Add/new. The provider's own lazy initialization, immunity and estimate
methods read the existing installed state.

Only reflection metadata/delegates are cached. No actor, installed part,
MechanicsContext, damage object or result survives an appraisal. An inert typed
preview packet is created only after an actual optional part is found and is
not already immune. There is no damage application, pool consumption, custom
chunk reset, gameplay Rulebook trigger or copied resistance/stacking table.

The already observed multiplier is passed once and the provider's remaining
value bounds the existing observed result with the same `Math.Min` policy.
Reduction is not subtracted twice. Earlier uncapped successful-hit evidence
retains its authority, including caster-specific bypass evidence. Vanilla,
absent/unknown optional-provider and error paths keep their existing result;
strict binding/read failures produce bounded diagnostics. New energy routes
identify `TTT estimate` or `TTT immunity`.

**Limits:** the TTT preview itself is approximate: its largest matching chunk
is not the full actual priority/stack/pool calculation. Where both vanilla and TT
parts exist, the old vanilla route remains; this change does not invent a mixed
stacking rule. Contextual/custom bypasses, changing pools, native actor state and
third-party event order require replay. This is a compatibility repair, not a
claim of complete damage-model parity. No TTT/game DLL is committed or shipped.

## 2. Hand of Hosilla: a tactical gap, with diagnostics-only approval

The author's combat #13 is RT1167–1612 / native CombatLog17345–19535. The foe is
`CR4_Cultist_Baphomet_Leutenant[9AED]`; actual attack records confirm its
**Masterwork Glaive**. The exact native foe approach radius is not printed, so
range-gap arithmetic cannot be recovered independently from the old trace.

The central concern is supported, with important detail:

- Orvath's Hand-centred Grease completes RT1238–1243; Hand initially fails DC19.
  Camellia's overlapping Grease also completes, with Hand passing its DC13 save.
- Seelah/Camellia initially attack the Wolf successfully. Later they use heals,
  Demoralize/Flare and some repositioning; they are not literally idle throughout.
- Camellia moves 1.9m around Grease at RT1354–1362, then attempts an advance into
  it at RT1384–1391. The forecast explicitly prices **80% stop chance**, with
  no safer nearer ring found. She advances 1.3m, interrupts and becomes prone.
  Native Grease failures include DC19; do not assume all overlapping areas have
  the same source or diagnose an overlap bug without additional evidence.
- Hand's ordinary glaive hits on Seelah at CombatLog18387–18841 total 46 damage.
  A separately labelled opportunity hit deals 13 at18960–18989. Later hits deal
  Camellia 27 and an Orvath opportunity hit deals 5. The **91 total damage**
  matches the AI end-of-fight tally. This is not just a failed path query or
  every hit being an unmodelled opportunity attack.

The source lacks a dedicated healthy short-reach melee response to a reach foe
holding a harmful-area boundary. Generic advance can be suppressed by usable
utility offense, and low-HP withdrawal is later and values potential movement+
reach threat. A small step outside present weapon reach does not honestly remove
all incoming threat: the foe may follow, retarget or use another action.

A new back-off/lure candidate would need runtime native reach/intent, shared
value/risk scoring and explicit anti-loop behavior. It is a tactical policy
change with retreat/advance oscillation risk, not a proven arithmetic correction.
**The author approved diagnostics only for this case.** No new back-off option,
target lock, retreat threshold, navigation rule or cancellation policy is added.
The existing movement and spell-safety behavior remains.

The new `[Reach]` row samples a possible melee/area standoff in real-time
combat, after the preview early return. It uses one already-scored hazardous
weapon approach (best stored pre-risk value) and declines the sample when a
useful in-reach weapon candidate exists. It prints the chosen action, stored
approach/risk/area costs and any already-priced ordinary retreat. Own weapon
reach is not retained in that candidate, so its arrival tolerance is labelled
accurately rather than presented as weapon reach.

At most eight current foe command slots are inspected for an accepted native
UnitAttack targeting this actor. Where available, its native approach radius,
target-point distance and distance-only reach check are printed; unknown intent
stays unknown. There is no LoS, attack forecast, path or risk recalculation.
One row per actor/round is enforced through weak TurnMemory keys with scalar-only
bookkeeping. TurnMemory is replaced each round and cleared between fights; this
adds no actor/context retention or reset hook. No idle/UI tick is instrumented.
Source/native inspection confirms these reads do not issue commands or mutate
candidates. Use replay evidence before proposing a behavioral response.

## 3. Other decisions and outcomes preserved

The new HD explanations supply useful counterfactual evidence. TB64 rejects
human-centred Sleep aims because of native range/LoS and no affordable approach.
At TB196 the human-group aim scores **46.2**, beating the lone Quasit **12.6**,
and is selected. These examples show legal geometry/HD/weighted-value competition;
they do not support a general preference for single targets. No scoring weights,
cluster bonus, minimum-recipient rule or repeated-save blacklist is added.

Six Burning activations complete Success/actedTrue: TB734/1553/3257 and
RT277/1430/2130. Actual DC19 trigger evidence remains, and off restoration and
burn-block recovery work in the traced cases. A passed save or resisted direct
packet does not by itself mean the infusion failed to activate. Other supported
families and player-management boundaries are not all live-tested here.

No new false silent/no-reach strike or completed Fail command is demonstrated.
Native concentration checks at CombatLog392–395 (enemy Wizard) and10473–10476
(Seelah in TB8) both **succeed**. The latest failed-concentration observer branch
therefore remains unexercised, rather than contradicted, in these sessions.
RT186 correctly forgets interrupted unacted Aid Another; RT524/675 do the same
for Color Spray/Sleep. The earlier event-ordering lead is not reproduced here.

Healing executes: TB299–306's touch approach/Cure maps to a real **10HP** heal at
CombatLog740–746. Repeated revive valuations are not repeated failed casts:
TB1990–2004 actually chooses Mind Spike then Daze before ending the turn, while
the logged Cure is only an unaffordable/future candidate. Existing partial-heal
and native miss/save distinctions remain. RT1809/1863/1908's rejected heal
approaches through Grease occur in RT6/native14, separate from the Hand fight,
and are not spent Cure actions. WrathPatches' OwnerBlueprint warnings at
TB1073/1180 are other-mod blueprint warnings, not AI Control exceptions.

## 4. Performance evidence

| Mode | Decisions | Total ms | Mean ms | Fight median range ms |
| --- | ---: | ---: | ---: | ---: |
| TB, eight fights | 436 | 17,047 | 39.10 | 8.5–38.9 |
| RT, seven fights | 961 | 46,697 | 48.59 | 19.5–35.1 |

RT records 794 cooldown skips. These are sampled decision costs, not FPS or a
matched benchmark. Earlier Test[5] means were 41.27ms TB and29.71ms RT, but
encounters, actor states and work differ; those aggregates do not establish a
regression or controlled improvement. No current-session 8/10 rating is claimed.

One cold decision builds 61 profiles; three warm long events overlap collection
and four more warm events cost 250–294ms without a collection flag. Ordinary
ability work dominates the warm slow reports. Active infusion is at most0.3ms
and infusion search at most0.2ms there. Cutting infusion quality is not justified.
No slow stand-up-preview diagnostic occurs; this is not proof of zero cost.

Energy/HD diagnostics are bounded but not free. All RT fights exhaust the24-row
energy sample; later provider reads may be absent from the log. There is no
per-frame trace or extra battlefield appraisal. Preserve local memoization,
existing budgets and decision quality; no broader optimization is introduced.

## Verification, branch and replay handover

Branch `codex/release-0322-log-fixes` starts at released/current development
`0a8b8f22852ca13a30069b7283ad11ea964e3bb0`; scope plan is `5b28bc2`.
This task uses [draft PR #13](https://github.com/recovribox-svg/AIControll-FACC/pull/13) into development. The previous integration authorization
completed PR #6–#12; it is not an automatic merge/release instruction for this
new task. Info.json stays0.3.22. Do not replace the published release ZIP, move
its tag, update main or publish a GitHub release. Supply a uniquely identified
test ZIP and preserve all older archives/checksums.

Final integrated Release build against supplied game/UMM references passes with
**zero warnings/errors**. `dotnet test tests/CompanionAI.Tests` passes **164 tests**,
zero failed/skipped. Checked module: `91661767-d748-41f6-8822-ec1492a08022`.

Offline verification against that exact DLL passes:

- 23 compiled/provider/native metadata assertions: optional dependency, native
  Get rather than Ensure, delegate shape/order, packet-allocation boundary,
  no gameplay/pool/reset call, vanilla-present and uncapped-observation gates,
  and matching observed/provider minimum bounds.
- Nine absent-Core assertions; 24 actual Core 0.7.15 binding/empty-detached-part
  assertions; eight assertions each for three incompatible API shapes (required
  method private, return type changed, missing part type). Each uses a fresh
  process. The original supplied DLL remains byte-identical. All four actual
  delegates compile; the detached empty provider initializes idempotently and
  preserves scalar amounts 0/3.5/7.5/30. It is never installed on an actor.
- Retained compiled concentration/Selective/HD and activation/observed-resistance
  guards, native cone delegate binding and all-or-none collection checks.
- Retained 13 memo identities, five Grease metadata gates, 20 native activation
  predicates, Free/Swift thresholds, 18 recognized/two withheld infusion layouts,
  486 native cost/quiver vectors, Rare Metal and cone metadata.
- Ordinary reader: 4,097 loaded exports, one known Achaekek export failure;
  1,220 ability rows, zero reader errors, all normalized detail rows unchanged
  against released v0.3.22. This remains partial export coverage.
- Independent source review of the reach hook, weak-key lifecycle and native
  approach/distance reads: no score/state/command change or new LoS/path query.

Offline copied dependencies/API-shape fixtures are not deployment files. None
of these checks runs initialized Unity actors, actual resistance ten, immunity,
stacking, pools, bypass handlers, TTT Base settings, live commands or Mono timing.
No live result for the new adapter or reach diagnostic is claimed.

Focused replay, with complete startup/package identity and both AI/native logs:

1. With TTT DR rework active, check an unobserved resistant Dretch/Quasit before
   the first hit. Expect the new provider route when its actual replacement part
   is installed, and compare direct/Burning values and actual native packets.
   Zero periodic value can correctly keep Burning off on resistant targets.
2. Compare TTT rework off/TTT absent, unprotected targets, immunity/suppression,
   mixed-provider and pool-changing cases. Old native/observed routes should
   remain; do not infer full stacking parity from the approximate provider API.
3. Check useful susceptible-target infusions, real buff/DC/burn, off/replacement,
   player ownership, management disabled and end-of-fight restoration in TB/RT.
4. Replay the Hand reach/Grease scene and record the approved diagnostic plus
   actual native attacks/areas. Movement remains the baseline for this build;
   the diagnostic does not promise a retreat or force the enemy to pursue.
5. Preserve group/immune-centred Sleep, partial revives, genuine native failed
   saves/misses and silent-effect safety. A failed concentration case would
   provide the still-missing live exercise of that observer branch.
6. Compare ordinary actors, Orvath/summons, cold/warm turns and idle selection on
   the same save/settings/mods. Adapter delegate caching is bounded metadata,
   not a claim of zero added work or measured Unity/Mono speedup.

## Downloadable test identity

- Runtime source: `ea20a79082a56ef226446d31ef29d5b4c08277c5`.
- Package commit: `fa291362ad53c3d00b8e6ac12ed284cb1d130bca`.
- [UMM test ZIP](../test-builds/AI-Control-FACC-release0322-fixes-ea20a79-UMM-test.zip?raw=true)
  and [install/checklist](../test-builds/README.md).
- UMM version stays **0.3.22**; fully exit before replacement and confirm startup:
  `[Build] v0.3.22 module=91661767-d748-41f6-8822-ec1492a08022`.
- ZIP SHA-256: `d1378472f5d711a7ed458b942f3db3677872910adbb870889b1580fb356351d5`.
- DLL SHA-256: `35594d2719c3858c54839f191363d18050587d893f59c56502c80d694f165c70`.
- Flat archive contains only the checked DLL and unchanged manifest. Published
  stable and older test archives/checksums are preserved. No game/TTT DLLs,
  rewritten dependencies, API fixtures, private paths or native dumps are shipped.

Keep supplying native CombatLog alongside complete AI logs: it establishes
resistance, real damage/healing, save results and opportunity/ordinary attacks
that cannot be proved from a decision forecast or command completion alone.
