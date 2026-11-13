# v0.3.50 integration1 playtest review

Task branch `codex/v0350-playtest-review` starts at development `d466f384acbdca242b214a16afe552d55606411d`.
The preceding integration is complete: PRs #46–#52 are merged into development, whose tree matches the checked
integration1 package. `main` remains `952cc71b`. This task keeps manifest 0.3.50 for another development test.

## Input and gameplay evidence

[Input identities/counts](V0350_PLAYTEST_INPUTS.json). AI line 3 confirms integration1 startup MVID
`0b54fad9-fc28-483c-a87a-3f1e9f0efd62`. There are 22 combat starts/ends, 1,752 retained summaries
(656 TB, 1,096 RT), and 4,044 measured decisions (666 TB, 3,378 RT). Repeated/quiet RT decisions are not all
printed. No AI Control exception/error row was found; external WrathPatches/Isekai errors are separate.
The author confirms ToyBox spawning/rests at unknown times. State changes alone do not establish an AI loop.
Rows below are 1-based in unchanged original AI `Log.txt` or `combatLog.txt`; raw logs remain private.

| Area | Evidence | Finding / action |
| --- | --- | --- |
| Profane Invocation | AI 6683–6889: ten initial casts are distinct stat/tier placements. Across ten fights: 59 issues, 57 `Success acted=True`, two unacted interrupts (8140, 15220). | Not 59 identical recasts. Correct specific held-value, sibling-settling and diagnostic gaps, without a blanket per-fight cap. |
| Sibling switches | AI 9551/9568/10245/15209 remove a named sibling on the recipient. Code settled only the same buff moving elsewhere. | Settle the actual removed unique buff's source cast too. These logs do not prove each switch was inside that window; old placements can belong to prior fights. |
| Held offensive stats | AI 9550/11837/15207 include offensive stat appraisals at 95% first-hit chance. Held appraisal excluded a source from stacking but added its bonus to already-buffed attacks. | Adding +attack at the cap can be worth zero while removing it loses hits. Reprice a verified static-stat contract against the exact cached native routine; bonuses well above the cap can correctly remain worth zero. |
| Misleading buff detail | AI 15207 labels incoming Constitution but describes removed Strength's attack/damage. Both appraisals wrote the incoming diagnostic key. | Restore incoming detail after held appraisal. Include ability identity to distinguish identical names across tiers. Functional scores were separate. |
| Swarm protection | AI 5372–5374: Mirror Image scores 106.5 vs swarm exit 26.1 while Quickspider harm contributes to incoming danger. | Native images require an attack roll; recognized swarm damage is automatic. Remove that component from AC/concealment/image credit. Mixed ordinary enemies remain, so the corrected choice is not predetermined. |
| Working actions | Two successful Faerie Fire casts (7877–7880, 13235–13236); 22 Focused Shot issues, 18 successes; four performed Griffon transformations, with natural armour also at combat 14413/14611/14814/14860. | These features execute. Cast success alone does not validate reveal outcome or every damage estimate. `Interrupt acted=True` is not a failed transformation/attack. |
| Swarms / movement / summons | 37 explicit swarm exits (3 TB, 34 RT). Three plain-path refusals choose alternatives. Incubus advances at 516–520 and attacks at 631–632; its weak first-turn exposed advance is below threshold. | No additional movement/summon-turn defect established. Twenty-six RT stuck cancellations include crowded geometry; an endpoint height difference alone does not establish the cause. |
| Damage / support limits | AI 6309: Snowball two-hit average 24.5 vs estimate 18.5; 6310: Air Blast calibration x1.11 from six hits. Unsupported-effect/branch notices remain. | Small mixed samples do not justify overriding native defenses or retuning bombs/rays. This review does not claim full class support or numerical acceptance from cast counts. |

## Native rules and corrections

The unchanged supplied `Assembly-CSharp.dll` has SHA-256
`2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`, MVID
`90a9869c-2792-4c7b-bfb7-5a8b33da7c82`. Native source and game DLLs stay private.

`UniqueBuff.OnActivate` calls `UnitPartUniqueBuffs.NewBuff`, removing the caster's prior copy of that **same
buff blueprint**. Actual `ProfaneAscensionAbility<Stat>1/2` graphs each remove five same-tier sibling buffs
on the recipient and apply a permanent +2 profane unique buff. They leave the other tier alone. Existing native
descriptor-based stacking still prevents credit for a second same-stat profane bonus that does not stack.

`TacticalEngine.BuffReplacement` reads `Context.SourceAbilityContext.Ability` from the actual removed/held
buff and consults the existing performed-cast ledger. No reconstructed ability/context or second memory.
Same-round unique replacement is withheld; the existing three-round settle and gain-of-five exception apply
thereafter to net gain after recipient/remote-holder losses. Independent tiers, initial placements and buffs
with no matching AI cast this fight receive no new lock. Unperformed casts remain rolled back.

Held weapon loss accepts only a constant `AddContextStatBonus` with empty restrictions, optionally `UniqueBuff`,
a matching live source modifier, supported direct stat links and a single-weapon routine. It compares current
damage with removal from the *same still-cached native routine*: iterative rolls, uncapped attack bonus/AC,
criticals, DR, physical factors, separate sneak/blast damage, attribute parity and damage-multiplier truncation.
A shared scalar kernel preserves ordinary estimate arithmetic. No actor/buff preview or second rule query.
Stale, mixed-weapon and complex-provider cases retain the existing approximation. This is not a general
counterfactual rewrite of defenses, spellcasting or arbitrary fact effects.

`UnitPartMirrorImage.TryAbsorbHit` is reached from `RuleAttackRoll.OnTrigger`. Quickspider's exported
`SpiderSwarmDamageAbility` (`f9b287e4e5d046f2b8e0b545de5e0548`) selects nearby recipients and executes
`ContextActionDealDamage`; native `DealHitPointsDamage` triggers damage without a new attack roll. The existing
swarm reader also supports the inspected automatic round-action form. `DefenceOf` now uses existing threat
parts to exclude only recognized automatic swarms from AC/miss/images, with a separate ordinary-enemy hit/packet
approximation. Total danger still informs HP, healing, saves, physical DR and matching energy protection.
Predictive swarm-only protection is also zero. The ordinary-enemy remainder retains its existing approximation;
not every remaining spell is proven to require an attack roll. Existing frame/decision metadata is reused;
there is no new ability/battlefield scan or general movement-policy change.

## Performance

TB totals 68,445 ms over 666 measured decisions (mean 102.8 ms); RT totals 229,784 ms over 3,378 (mean 68.0 ms).
These include cheap waits, not just active scoring. Of 207 slow rows, 28 coincide with GC; all 23 rows over
1,407 ms coincide with GC. Heap deltas are live-heap changes, **not allocations**; collection cost cannot be
attributed solely to AI Control. Tuvel also has 83 non-GC slow rows, all with zero new profiles (conditional
median 331 ms). AI 8834: 253 scored abilities, 1,407 ms total, abilities 1,127.8 ms, whole heap +60,168 KB;
AI 12102: abilities 1,103.4 ms. AI 8326/12187 show abilities heap +80,212/+85,496 KB, far beyond the costliest
individual callbacks. The old callback timer omitted discovery and availability/profile/policy checks, so no
one callback can be blamed. Active-infusion time is small (8834: 0.3 ms/1,236 calls); cold Treat Affliction
timing alone is not steady-state evidence.

`AbilityDiscovery` rebuilt conversion keys while scanning accumulated results for each conversion. A Gather-local
ordinal index now preserves the first matching index, key, order and native availability-based replacement,
including directly encountered conversions. Book, metamagic, heighten, item, source-level and fusion semantics
remain. No cap, pruning or cross-decision cache. This removes a guaranteed quadratic lookup; current logs cannot
quantify its share or promise a particular speedup. Slow rows now expose discovery/filter and pre-score aggregate
times/counts, with availability/profile sub-times explicitly included. No per-spell log or extra heap sample.

Six summon cohort lines measure 3,752 control checks in 91.9 ms, at most two adoptees per fight on those lines.
That is baseline evidence, not a large-pack stress test or matched timing acceptance.

## Validation and focused replay

Unit cases cover sibling settling, independent tiers, unperformed-cast rollback, swarm-only/mixed/ordinary
protection and invalid inputs. New ProfileAudit checks use all twelve Invocation graphs, native scalar contexts/
modifiers/routine records, structural negatives and compiled production routes. Existing class, native toggle,
Tome, ToyBox, swarm, affliction, movement and summon checks remain. Actor-free fixtures do not execute live
casts, attack rules, movement, Harmony turn lifecycles or Unity timing.

1. Give Lann both Invocation groups. Check distinct initial placements, nonstacking same-stat bonuses, small
   same-tier changes settling, useful urgent changes and failed casts creating no lock. Compare near-cap and
   well-above-cap recipients; inspect incoming detail/ability identity. Use a save without combat rests for
   the primary repetition comparison.
2. Play a swarm-only and a mixed swarm/weapon fight in both modes. AC/images gain no credit against the recognized
   automatic component; ordinary attacks, healing, other protection and useful swarm exits still compete.
3. Repeat the same roster/books/save in both modes; compare setup timings, gathered/allowed counts, slow
   decisions, GC and stage heap, including small/larger supported summon packs. Separately time ToyBox
   interventions. Regress conversions, unavailable sources, metamagic and items.

New behavior and matched Unity timing are unplayed. The original full corpus still has its separately reported
Achaekek `SawtoothSaber`/native `SawtoothSabre` load mismatch; originals are unchanged and not described as a full
pass. The package is a development test, with no main/tag/stable release.
