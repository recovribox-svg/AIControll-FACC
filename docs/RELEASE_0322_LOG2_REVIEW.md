# v0.3.22 follow-up: second playtest review and observer corrections

The author reports **7.5/10 smoothness**, with subtle, infrequent slowdowns.
Reviewed both new Release Version Log 2 AI excerpts and the combined native
CombatLog: **nine TB encounters followed by eight RT encounters**. Native
references below are raw LF physical lines; mixed CR/CRLF formatting changes
universal-newline numbering. AI files have 3,088 TB and 2,251 RT LF terminators;
the native file has 23,272. The old corpus's encounter numbering does not apply.

Neither AI excerpt includes startup Build/MVID. The new TTT estimate/immunity
and Reach messages prove those pathways ran; they do not independently identify
the exact installed module. New build acceptance remains distinct from reviewing
these supplied logs.

## 1. Fixed: an interrupted approach teaches a false target-failure strike

RT965–983 records the actionable sequence:

1. Orvath is issued Color Spray on Corrupted Mongrel Ranger[A057], requiring
   an approach. His earlier Grease produces saves while he is walking.
2. At RT977–980 the existing stuck detector cancels the approach: Interrupt,
   **acted=False**, moved **0.1m** after **6.8s**. Its spatial stuck memory is set.
3. RT981 nevertheless says Color Spray did nothing to that target **(1/2)**.
   RT983's independent cast ledger correctly says the order never acted.
4. The matching native encounter is RT4, lines12856–13824. No Color Spray effect
   appears. Grease and later Sleep effects do; native13257–13265 is a genuine
   passed Sleep save, not an unreported Color Spray execution.

`CastOutcome.Settle` previously discarded an ended, unexecuted/unacted known
command only if its broad Evidence flag was also false. `RuleSavingThrow` calls
Mark for the caster without source correlation; older Grease can set Evidence.
The target-specific MarkReached path correctly excludes other lasting areas,
so the unrelated save leaves TargetReached false. Settle then calls Finish on
broad evidence, and Finish counts the separate target non-reach despite its
success argument. After two such events a valid spell/target could be blocked
for the fight; this replay demonstrates **one** false strike, not an actual block.

The correction removes only Evidence from that pre-cast discard condition. A
finished, known command that neither executed nor acted was not a tried cast,
regardless of unrelated activity by its caster. Still-running orders stay
pending. Null-command fallback, real execution/acted casts, held-touch stages,
native refusal/concentration evidence and genuine silent/no-reach handling retain
their existing paths. No movement, stuck cancellation, command ledger or scoring
policy is changed. This shared observer serves TB and RT; the observed case is RT.

Broader source correlation of all evidence remains outside this narrow correction.
It is not a claim that every historical observer attribution is now exact.

## 2. Fixed: miss splash is compared with a full direct-hit forecast

TB1235 issues one Holy Water throw at Dretch. Native2942–2945 records a natural 1
critical miss, then2955–2958 and2960–2963 record **one Holy splash damage each**
to Dretch and Skeletal Champion. TB1487 misleadingly reports:

```text
Lann's Holy Water: 2 hits dealt 1 a hit, estimated 5
```

`DamageObserver.NoteEstimate` excluded critical multipliers, save halves and
periodic contexts, but unmodified miss splash could still enter whole-hit
calibration. This is not evidence that the full direct-hit estimate should be 1.
Holy Water's generic Checks feed that diagnostic only; its nonkinetic entries
do not change the learned BlastFactor or its action scoring.

Native delivery code supports a generic correction: the projectile delivery
retains its attack roll; ApplyEffect installs ContextAttackData before dispatching
AbilityEffectMiss, including selected recipients; ContextActionDealDamage copies
that same roll into RuleDealDamage. NoteEstimate now skips packets whose native
AttackRoll exists and **IsHit is false**. IsHit reads the existing result; it does
not roll another attack. No item name/GUID, splash formula or resistance table is
introduced. Normal hits and AoE with no attack roll retain eligibility.

The guard lives only in calibration. OnEventDidTrigger still observes every
actual damage packet's immunity/reduction/decline/vulnerability before calling
NoteEstimate; its compiled body is unchanged. Real splash damage is not erased
from defense memory or fight results. Successful-hit secondary splash or packets
without a missed roll remain separate provenance limits; this is not a universal
splash classifier.

## 3. Previous resistance and infusion changes are working in these logs

| Sampled route | TB | RT |
| --- | ---: | ---: |
| TTT estimate | 107 | 48 |
| TTT immunity | 13 | 5 |
| No vanilla/optional part | 96 | 138 |
| Observed | 0 | 1 |

There are **155 optional estimates and 18 immunity reads**; no optional binding
or read failure appears. Missing-part rows can be unprotected targets and are not
alone evidence of a defect. Every fight fills the 24-row energy sample, so later
missing trace rows are not proof of skipped appraisal.

Positive cases include TB44 fresh Sharpshooter Fire 7.5→2.5, TB48 Cambion Fire 7.5→0,
and TB56 Cambion Jolt immunity 0. Native819–830 independently confirms a zero Shock
packet against that Cambion through Electricity Immunity. Fresh Dretch/Quasit
Fire forecasts become 0; no Fire Blast is issued against those targets in these
AI excerpts. Useful control/weapon alternatives remain. RT1787 correctly reads
Large Water Elemental Cold immunity while its Fire preview remains 7.5.

Five automatic Burning activations complete Success/actedTrue and correlate
with native **DC19** Burning saves:

| AI completion | Native effect evidence |
| --- | --- |
| TB494 | Skeleton:1080–1094, failed Burning save and 5 direct Fire |
| TB727 | Cutthroat:1539–1553, failed Burning save and 5 Fire |
| TB1737 | Champion:4856–4872, failed Burning save and 10 Fire |
| RT263 | Cultist Champion:10399–10416, failed Burning save and 6 Fire |
| RT1993 | Water Elemental:19728–19745, failed Burning save and 5 Fire |

Cultist Champion takes real Burning ticks of 5 and 3 at10527–10539 and 10641–10653;
a later passed save ends it. Off restoration and burn-block recovery are also
visible. Only Burning is exposed by these saves' discovery coverage; other
supported substance families are not gameplay-validated here. Small positive
gains below the existing free-toggle threshold are not failed activations.

RT2032's observed Fire flat1 comes from Searing Flesh/Cosmic Phase Gate at
native20447–20451, not a failed Burning/direct Fire Resistance10 prediction.
Keep capped-evidence semantics and the TTT adapter unchanged. Its preview still
approximates full stacking/priority/pool execution; mixed-provider and bypass
limits remain. These reads are positive live compatibility evidence, not proof
of every third-party handler or full damage parity.

## 4. Other choices and reach evidence

Healing has native results: examples include TB self potions healing14/8/7HP,
Camellia healing Seelah5/6HP, and RT touch Cure healing Seelah5HP at14629 after
arriving in native range. Interrupted unacted potions are not successful silent
heals. A two-point potion result at12579 is the actual die roll, not proven waste.

Sleep group choices remain useful. TB2434 chooses a Sharpshooter centre43.9 over
Cultist28.1; native7146/7157 confirms Sharpshooter and Dretch saves. The Wizard is
explicitly immune at7155: HD-eligible recipient IDs are not guaranteed benefit.
TB2561 chooses a different group instead of the already-controlled old centre.
Repeated Sleep on the Goblin follows attacks waking it, rather than proving a
re-sleep loop on an untouched sleeper. Later passed saves are genuine outcomes.
No new recipient bonus, weight, save blacklist or priority tree is warranted.

Both native concentration checks are enemy successes (7446–7449 and20190–20193).
There is no live failed-concentration case in this corpus. No CompanionAI
exception or completed Fail command is demonstrated. WrathPatches OwnerBlueprint
warnings are foreign-mod blueprint warnings. Acted TB attack Interrupt results
with real native attacks are not failed unacted orders.

There are 20 real-time Reach rows, all with explicit unknown current foe intent.
The fixed native Raw array has four slots, including nulls: four inspected slots
does not mean four real attacks. Source checks find no proven target/type filter
bug. Unknown is retained without inventing reach, pursuit or a safe retreat.

This Hand fight is **RT6/native15**, lines14907–16297. Unlike the earlier replay,
Hand's only recorded glaive hit targets the summoned Cambion and deals 10HP at
16162–16180. It does not hit Seelah/Camellia here. Companions attack other enemies,
use utility, and later choose a priced hazardous approach. The new-area Grease
check withholds spells while Seelah approaches. Do not transplant the earlier
91-damage standoff into this replay. The author-approved diagnostic-only boundary
remains; there is no movement or diagnostic-filter change in this follow-up.

## 5. Performance: infrequent spikes, no justified quality cut

| Mode | Fights | Decisions | Total decision ms | Mean ms | Fight median range ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| TB | 9 | 454 | 21,911 | 48.26 | 25.0–51.6 |
| RT | 8 | 908 | 36,749 | 40.47 | 14.3–33.7 |

RT records 617 cooldown skips. These are decision samples, not FPS. Different
encounters, spawns, states and counts prevent a controlled regression/speedup
claim against earlier aggregate means. The current 7.5/10 report is the author's
actual feedback for this corpus.

Three decisions exceed 250ms:

- TB77:908ms, cold 61-profile build; context 416.8ms, abilities 311.6ms.
- TB1211:252ms, warm abilities 189.5ms; infusion search/active read 0.3ms each.
- RT1497:1109ms, warm Camellia context/threat 1081.1ms, zero new profiles and GC
  overlap; abilities 23.6ms, infusion search/active read 0.0ms, finish 0.2ms.

Context/threat is inclusive of context construction, refresh, calibration,
settlement and other bookkeeping. A changed assessment counter and overlapping
collection do not identify the owner of all 1081ms or measure GC duration.
Selecting Inspired Rage did not itself cost that whole context stage. Reach
formatting occurs in finish and is not implicated by this outlier's 0.2ms finish.

Source inspection confirms existing per-round/set refresh and coverage gates;
blindly sharing lists would change actor-relative target/stealth/downed semantics.
There is no proven safe duplicate-work fix here. Preserve native appraisal,
infusion scope, local caching, budgets, decision quality and game speed. No new
performance speedup is claimed. Matched native profiling or narrower context
substage timing would be needed to attribute the remaining occasional slowdown.

## Verification and replay handover

[Draft PR #14](https://github.com/recovribox-svg/AIControll-FACC/pull/14) targets development.
Branch `codex/release0322-log2-fixes` starts at remote development `0a8b8f2` and
includes pending PR #13 at `8bb6a3c`. Its own runtime diff is only the two observer
hunks above. Do not merge #13 or this follow-up without review/integration
approval. Main, stable 0.3.22 ZIP/tag and earlier archives remain unchanged.

Final integrated module: `793a9fe8-cc9d-49fc-b535-b323a25148f3`.

- `dotnet test tests/CompanionAI.Tests`: **164 passed**, zero failed/skipped.
- Full Release against supplied game/UMM references: **zero warnings/errors**.
- Actual compiled Settle gate: **64 boolean control-flow vectors /406 gate+shape
  assertions** pass, including broad evidence, native-refusal WatchTarget,
  running/executed/acted/null-command variants. The prior DLL reproduces the
  evidence=true regression; the new DLL discards it. This interprets actual IL
  with scalar inputs, not a duplicated condition or manufactured game actor.
- Miss-sample filter: **20 compiled/native assertions** pass. Explicit misses
  cannot reach raw forecast/calibration/learning; hit and null-roll paths retain
  eligibility. Per-packet observer IL matches PR13 and defense stores precede
  NoteEstimate. Native delivery/context/miss-effect/roll transport is checked.
- Retained concentration/Selective/HD, activation/observed-resistance, memo,
  Grease gates, 20 substance predicates, Free/Swift, 18 recognized/two withheld
  layouts, 486 native burn/quiver vectors, Rare Metal, cone binding/collection pass.
- Actual supplied Core 0.7.15: 23 compiled/native, 9 absent, 24 binding/empty-detached
  provider assertions and 8 each for three mismatched API shapes pass. Original
  supplied Core bytes remain unchanged; no hard mod dependency is introduced.
- Reader: 4,097 loaded exports/one known failed Achaekek export; 1,220 ability rows,
  zero reader errors, all normalized details unchanged against release. Partial
  export coverage, not every game blueprint.
- Independent review finds no blocking regression in either observer change.

Offline tools/rewritten copies are not deployed or committed. These checks do
not run initialized actors, native events, commands, live touch charges, TTT
settings/handlers or Unity/Mono timing. Supplied logs validate preceding
pathways; **the new two-fix DLL still needs live replay**.

Focused TB/RT replay:

1. With an old Grease ticking, interrupt an approaching spell before it casts.
   Expect ended-before-cast/not-counted and no target (1/2)/(2/2) strike for it.
   Spatial stuck avoidance should still work. Genuine executed silent failures
   must retain their safety handling; real native refusals must stay non-silent.
2. Throw Holy Water and miss. Keep actual splash damage/resistance observations;
   do not count those missed-roll packets as full direct hits in DmgCheck. Also
   check normal hit damage and ordinary AoE with no attack roll.
3. Preserve live TTT estimates/immunity, useful Burning/DC/burn/off restoration,
   management/player ownership, susceptible/resistant targets and group Sleep.
4. Replay reach/Grease with baseline movement. Include actual native attacks;
   an unknown Reach snapshot is not proof that the foe cannot hit.
5. Compare same-save/settings/mods ordinary companions, Orvath/summons and idle
   selection. Include complete startup/MVID, exact ZIP filename, AI logs and
   native CombatLog. Do not infer a performance improvement from these two fixes.

## Downloadable test identity

- Runtime source: `649ed232b0b2ae5ac6075f5e2b4086a6c62bfba0`.
- Package commit: `1cffbc094abf07cdb9cd7d0776ed3c61116d4a1b`.
- [Download UMM test ZIP](../test-builds/AI-Control-FACC-log2-fixes-649ed23-UMM-test.zip?raw=true)
  and [install/checklist](../test-builds/README.md).
- UMM remains 0.3.22. Confirm complete startup:
  `[Build] v0.3.22 module=793a9fe8-cc9d-49fc-b535-b323a25148f3`.
- ZIP SHA-256: `1210e2192caa21d0702e1cad8105c75f6124a834fbc989a2fea6b676fe05e995`.
- DLL SHA-256: `4fe88688b7005ba758f266b39d5b0d2fe1dac41525fc43797b5efb5046122ebf`.
- Archive contains only the checked DLL and unchanged manifest. All earlier test
  archives/identities and published stable package bytes are retained.
