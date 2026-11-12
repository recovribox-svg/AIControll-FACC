# Exit1 TB / real-time playtest review

**Cumulative follow-ups:** the [native-bound exit repair](MOVEMENT_EXIT_BOUNDS.md) and [friendly treatment coordination](FRIENDLY_TREATMENT_COORDINATION.md) address the scoped follow-ups identified below. The original review findings and their gameplay limits remain recorded.

12 November 2026. `codex/exit1-playtest-review` starts at played PR #37 head
`dc5235f8dc8096d4db86ea6bbf819d940328846e`, containing fetched development `1f58150` and PR #33–#37
prerequisites. Those unmerged PRs are not integrated into development by this task.

## Inputs and accounting

[Input identities](EXIT1_PLAYTEST_INPUTS.json) identify the author's captures. AI line references use original
LF-delimited `TB + RT Log.txt` (6,803 lines); combat references use LF-delimited `combatLog.txt` (28,840 lines),
stripping display tags only. Private logs, game binaries and decompiled sources are not committed.
AI line 3 identifies exit1 MVID `905bb172-799c-44cb-a46a-ff208a31dab4`.

- Eight fights, 900 decision summaries: 376 TB / 524 real-time, eleven actors.
- All 658 issued commands have end records: 477 Success/acted, 100 Interrupt/acted, 77 Interrupt/unacted,
  four Fail/acted. No explicit issue refusal or CompanionAI exception observed. An interruption after acting
  does not mean an attack/cast never occurred.
- The 244 error-tagged lines are external WrathPatches component-copy ownership errors. ToyBox warnings are
  separately attributed; neither proves an AI failure or that another mod's error is harmless.

## Correction: avoid unnecessary debuff contexts

Ability scoring dominates expensive non-cleanup decisions. AI 160, 3054 and 5858 include Appraise Foe with
14–24 contexts, as well as costly Eruption/Fan of Flames appraisals. `DebuffShareOf` constructed a native
`AbilityExecutionContext` per recipient even when every numerical part was constant.

The new `BuffAmountNeedsContext` follows `PartAmount`'s reads: non-simple ContextValue, dice, pool or caster-level
formula retain source binding. Empty numerical graphs retain their unknown result. Fixed/simple amounts omit
the source context only when the actual caster is the decision actor, preserving caster-fact filters' giver.
Live recipient level/BAB and conditional weighting remain. There is no actor simulation, shared mutable rank
context or persistent recipient cache. This is a narrow context/allocation reduction, not a claim that
Appraise Foe's generated graph is constant or that its whole appraisal is now fast.

`StaticDebuffCheck` uses actual exported Bane/Crushing Despair parts, compiled scalar amount evaluation, dynamic
classification and the production call path without running a native actor/context factory. Its 10,000-read
allocation check measures only the metadata predicate offline, not Unity decision time.

## Movement evidence and remaining defect

Selected step/full-attack preflight is active: AI 479 rejects an over-budget step, 954–955 and 1847–1853 reject
approaches through harmful areas, and 1927–1930 shows the three-check ceiling. Alternatives remain eligible.
There are 13 approach notices, with no explicit later dispatch refusal. Different fights are not a matched benchmark.

Sixteen area-leave commands have 12 successful and four interrupted endings. Ground projection now gives
inspected endpoints matching requested ground destinations. However, Orvath still takes four successive smoke
exits (3586–3640), and Camellia repeats shorter exits (4462–4503). Projection alone did not establish correct exits.

Native `AreaEffectEntityData.UpdateUnits` calls `EntityBoundsHelper.FindUnitsInShape`: cylinders/boxes query the
separate entity-bounds physics scene. `ShouldUnitBeInside` explicitly skips `Shape.Contains` for those shapes.
Units have actual `SphereBoundsCollider` objects maintained by `EntityBoundsController`. Exit1 instead used
`Shape.Contains(point, Corpulence + clearance)`. Its route tests did not cover this membership distinction.
The next focused repair must match native capsule/box versus actual sphere-bound geometry, retain bounded
projection/path checks, and distinguish registered membership from current geometry in read-only diagnostics.
Brimorak smoke is attached to a moving creature: pursuit, recapture and update lag can also produce repeated exits.
The captures lack collider/area snapshots to attribute every repeat. General pursuit/commitment/retreat/stuck
timers are not retuned here.

## Other decisions checked

| Concern | Evidence and conclusion |
| --- | --- |
| Magus modes | AI 274/276 identifies Drow Magus as Eldritch Archer, explicitly excluded from C07's melee automation. No mode-state preparation is expected. This remains a support gap; these logs cannot accept melee C07 behavior. |
| Magic Fusion | arcan issues 30 fused spells: 10 TB successes, 11 RT successes, nine RT unacted interrupts. Combat 6554–6588 and 9147–9183 contains cold/electric Snowball/Shocking Grasp payloads with per-packet resistance/vulnerability. This supports those combinations, not all combinations. |
| Fusion no-effect notices | AI 4085/6478 names Large Earth Elemental/Hell Hound for Explosive Runes/Hideous Laughter. Target-specific notices do not establish no area recipient was affected. Native Explosive Runes has delayed delivery; other fused recipients have outcomes. Do not globally blacklist or invent compensating damage. |
| Shared HD | Unsupported Sleep combinations remain explicitly diagnosed; no speculative shared-HD support is added. |
| Scorching Ray | AI 1309 reports four hits averaging 7.8 versus 14.5 forecast. Combat 1147, 1175, 1851, 1878 records 7, 7, 9, 8 damage with low 4d6 rolls and applicable Point-Blank Shot bonuses. Four low rolls do not justify another projectile multiplier or global calibration. |
| Bombs | 20 Corrosive Bomb / 18 Stun Bomb selections over eight fights. The named elemental can be an area aim rather than its sole valued recipient: AI 1547–1555 includes Minotaur/Schir and multiple elemental saves. Counts/centre names alone do not prove waste. No estimates are fitted to a few rolls. |
| Healing | 61 Cure Critical Wounds item selections include actors below half HP (449, 1019, 3172). Missing HP, scoring and command endings were checked; repeated healing across a long fight is not itself duplicate healing. |
| Failed casts | Four acted failures: Divine Zap, Grease, Displacement, Command Approach. Walking/provoking casts explicitly price exposure/interception. Native failure after acting is not an issue-refusal loop. |
| Affliction coordination | Five Treat Affliction selections. At 6722–6737 Lann and arcan start it on Seelah before either finishes; arcan is interrupted before acting. Existing in-flight same-target guarding covers enemies only: friendly dispel coordination is a concrete remaining gap, not two consumed cures. |
| Dangerous enemies | Monitor Lizard/Schir stat-damage threat is present (641, 1393); Quickspider poison has attached risk (6100). Combat contains four Strength and one Dexterity damage packets, no negative-level packet. Existing paths engage; this does not establish every enemy's coverage. |
| Swarms | Eight SwarmAudit records describe native delivery. Two chosen swarm exits occur (6101, 6444); 6720–6721 reports no free short exit. A missing ordinary swarm weapon is expected. This exercises swarm scoring, not permanent avoidance of pursuing swarms. |
| Buffs | Eight Arcane Weapon Enhancement choices, approximately once per fight, use the existing unread-effect fallback. Exact marginal weapon-property selection remains a competence gap. Haste/Displacement do not demonstrate a same-round loop here. |
| Older priorities | No named unknown-burn line appears. No Profane Invocation/Faerie Fire casts occur; casts-per-fight/reveal acceptance remains untested. |

## Performance and verification limits

54 thresholded slow samples: median 338.5 ms, maximum 1,356 ms, eleven explicit cleanups. They are not the timing
distribution of all 900 decisions. Stage heap KB is net managed-heap change, not exact allocation; cleanup can
make it negative. The warm first decision includes 322.8 ms context/threat work. Orvath 5858 spends 868.6 ms in
abilities and about 12.9 MB net in that stage; movement/path stages are small in those rows. The context skip
removes one avoidable cost. Unity speedup needs the same party/combat/settings/logging and warm-up.

The receipt records the checked source, real-reference Release and full unit/inventory/native suite. Strict native-
compatible scope excludes one separately fingerprinted input enum mismatch (`SawtoothSaber` / supplied
`SawtoothSabre`). The original full corpus remains incomplete, with no ability verdicts; it is not counted as a
pass. Supplementary hidden Magus buffs come from supplied BubblePrints stock 2.7.0x data, not installed mod state.

## Replay

1. TB and shared RT: compare constant/rank-based debuff penalties and caster-fact eligibility, retaining stage/context
   counters. Ordinary ranged weapons, Fusion, healing and native Magus contracts must remain functional.
2. Smoke: capture the forthcoming native-bound diagnostic. Follow the approved exit repair's checklist; a successful
   move record alone does not prove the area stopped affecting the actor.
3. Retain startup MVID and combat log. C07 acceptance requires a melee Magus; Eldritch Archer needs a separate native
   ranged contract. Reveal/Profane Invocation acceptance still needs relevant casts.

Checked source `318b52f`, [receipt](competence/EXIT1_REVIEW_CHECKS.json) and [review1 test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-review1-318b52f-UMM-test.zip), startup MVID `ffce03c5-bfe3-414f-b6bd-4a946d673dbf`. 607 unit + 11 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 0/0 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
