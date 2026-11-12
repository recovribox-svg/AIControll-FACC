# Review5 TB/RT playtest review and paired form estimates

This task starts at refreshed integrated development **`44b224f`**, preserving Claude's integration. PR #46 is a
separate planning handoff, not a dependency or an implemented summon-control feature. Original files, hashes,
telemetry and manual-test context are recorded in [the input manifest](REVIEW5_PLAYTEST_INPUTS.json).

The TB startup MVID is exactly review5 `b3359daf-c8e5-4bb6-9245-d6be013a4d15`. RT lacks a startup header; its
identification as review5 comes from the author. The author confirmed combat spawning/rests. RT also logs Isekai
Dimensional Reinforcements. Do not interpret every reset, tiny initial enemy count or interrupted cast as an AI defect.

## Findings and disposition

| Finding | Evidence | Disposition |
|---|---|---|
| The old Griffon branch blocker is resolved; form/Fury execution is now exercised. | TB 202 appraises the form; 1282/2286 issue it and end with `acted=True`. TB 2654 selects Fury; its following routine has five attacks. RT issues four forms, three with `acted=True`; the first ends before acting (RT 183). | Positive gameplay coverage of review5, not proof of every forecast or native outcome. Existing ownership/restore and future-form scope remain. |
| New-form comparisons can use different reference enemies. | `OffenceOf` selects a nearby enemy, but stores its `Enemy` only for damage > 0. `AppraiseShifterForm` uses the first context enemy when that field is null. TB 2283 logs 0→38.7; RT 71 logs 0→37.5 at value 174.5 despite other weapon routines > 40. | **Fixed here.** Old logs omit quote-target identity, so the exact contribution to those numbers cannot be proven. The inconsistent code branch is independently demonstrable. |
| Lazy recovery retries a plain advance already rejected in the same decision. | RT 2041–2051: three selected checks reject two attacks/marks and `Advance->Vermlek`; recovery re-creates that advance, and issue rejects its Corrosive Bomb path again. | Separate focused follow-up within the approved recovery scope. Reuse actual refusal keys only for this decision; preserve fresh decisions and other plans. Dispatcher safety worked. |
| Some idle turns are correctly avoiding known harm. | TB 1300/1405/2028: all listed attacks/advances enter Corrosive Bomb at negative value. RT 2050 rejects the actual unsafe path. | No hazard/range relaxation or forced pursuit. Positive raw attack value before safety is not a reachable safe action. |
| Swarms are recognized, while a native weapon inspector still reports no hand weapon on Quickspider. | Swarm/SwarmAudit records are present; TB 2706–2707 rejects unsafe casting paths; RT 1359 prices an exit below zero. The combat log contains Diminutive weapon immunity and spell immunity. | Do not infer zero swarm threat from `EnumerateAttacks ... Has no weapon`. Dedicated native swarm damage/space readers remain active. Lack of a profitable exit is not evidence that leaving was never considered. |
| Aeiluvetar appears as a hostile target in RT. | RT 2208–2209 includes attacks/control against that display name. Combat 28658/29580 records failed Dominate saves for `EnigmaticCoConspiratorCompanionUnit[4A0A]`; later saves succeed. | Native domination is a supported explanation, not proof of an ally-filter bug. Actor/faction timestamps are incomplete; do not rewrite allegiance checks from display names. |
| Interrupt result codes are often normal completed actions. | Numerous attacks/spells end `Interrupt acted=True`; the unacted RT cast examples are explicitly excluded by the cast observer. | Keep acted/command evidence. The log contains no AI exception; RT Camellia's Grease has one `Fail acted=True` at 2039, which alone does not identify a planner defect. |
| Some base Alchemist Bomb previews return zero. | RT 74–77: raw fire 8/9.5, TTT estimate 0 against Bear/Vescavor; other consumable bombs are the selected actions. | Not validated by a comparable actual base-bomb damage sample in these logs. Do not substitute raw damage for a legitimate possible defense result. A controlled base-bomb cast/state capture is a follow-up if this estimate needs investigation. |
| Known class/special coverage remains incomplete. | TB 1701 explicitly withholds Death from Above and Release Grapple; TB 244 withholds Focused Shot. Fused Sleep combinations remain explicitly unsupported for shared HD selection. | These are visible support limits, not silently successful abilities. Death from Above needs a separate native-command/movement proposal; no guessed utility value or automatic signature expansion here. |

No `DmgCheck` mismatch row appears in either AI file. That is not a numerical validation of every ray/bomb forecast;
review5 deliberately excludes ambiguous shared-arrow samples. Ranged Magus spell/weapon actions are exercised;
this is not a melee Magus Spellstrike acceptance test. No Faerie Fire/Profane Invocation execution is established.

## Correction: preserve the assessed target and current quote

`TacticalEngine.Buffs.OffenceOf` now retains the existing `WeaponAttackEstimate` and its target independently of
the fields used for profitable offensive buffs. The latter remain unchanged. A new-form appraisal requires that
pair and uses the quote's damage **and ranged flag** against that same enemy. Known zero damage remains valid;
an unavailable current quote stays unknown instead of becoming an invented zero against a different enemy.

This does not pick an easier enemy, scan more targets, change damage/defense rules, add a modelled form benefit,
or compare alternative best-target routines. It preserves the existing conservative reference strategy. Fury's
positive-routine/in-range admission, native selection and restoration remain unchanged. New-form notes now name
the reference enemy so future zero-baseline cases are auditable.

The same supplied `Assembly-CSharp.dll` is used (SHA-256
`2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`, MVID
`90a9869c-2792-4c7b-bfb7-5a8b33da7c82`). Existing live-body estimates already run native attack/weapon/AC/damage
rules and target defenses (`CombatMath.ReadRoutineCore`, `ReadHitOnTarget`); the repair reuses their result.
It stores two references in the existing frame-scoped appraisal record, makes no extra forecast/context/rule,
and keeps the eight-form and existing Fury budgets. No actor/buff/equipment state mutation is added.

Fourteen new actor-free `FORM-REFERENCE` checks use the production estimate type with synthetic scalar values and distinct reference-only native identities:
known zero/positive, ranged zero, missing current quote/target, no first-enemy substitution, producer ordering,
form call route and unchanged Fury admission. They do not initialize actors, execute native rules/commands,
or measure Unity timing. Existing Shifter/campaign-flag checks remain separate.

## Performance and limits

Five TB fight summaries total **351 measured decisions**, with fight medians **49.7–108.2 ms**, p95
**298.2–386.8 ms**, maximum **1390.3 ms**. Six RT summaries total **941 measured decisions**, medians
**34.9–79.5 ms**, p95 **130.5–236.0 ms**, maximum **1470.3 ms**. The retained RT decision summaries are fewer
than measured decisions; repeated/quiet rows are suppressed. There are 65 slow rows and four marked GC overlaps.
These are different spawned/rested encounters, not a matched performance regression benchmark. Heap stage deltas
remain live-heap changes rather than allocation measurements. No global scoring/cache/budget retune is justified.

General movement, commitment, retreat, native pathfinding and stuck cancellation are unchanged by this form fix.
The already-approved recovery path's redundant reattempt is a separate follow-up. The observed RT blocked walks
remain subject to the existing path/body/hazard checks; this report does not promise a route where none is safe.

Replay a transformed and untransformed Shifter near (a) an immune swarm and ordinary enemy together, (b) a single
normal enemy, and (c) a temporarily unreadable/viewless enemy. Verify both before/after quotes name the same target,
known zeros remain zeros, unavailable bases withhold, and useful forms/Fury still execute. Regress both modes,
current-form preservation, owner/player toggle restoration, buffs, weapon attacks and items. Native game replay and
matched timing of the corrected build remain pending. Manifest stays 0.3.30; Claude reviews/integrates.

Checked source `4392e51`, [receipt](competence/REVIEW5_PLAYTEST_CHECKS.json) and [review6 test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-review6-4392e51-UMM-test.zip), startup MVID `3f9c3355-3c65-4477-b059-dd370eb20d2b`. 664 unit + 27 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 0/0 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
