# Tactical improvements inspired by WorldCrawl and CompanionAI v3

16 November 2026, Codex. Research and planning handover; no gameplay implementation or new build.

## Recommendation

The most worthwhile larger project to plan is **native tactical mobility shared by companions and summons**:
use a suitable owned teleport or other supported movement ability when it opens a better legal action or avoids
danger at an acceptable cost. Start with one verified self-only native delivery, rather than every teleport.
This adds a capability the engine currently lacks and can reuse its existing action values, safety checks and
bounded candidate selection. It is more substantial than changing role multipliers.

The strongest alternative is **more complete weapon-outcome forecasting**, starting with verified damage on a
miss and then carefully bounded effects that change over a multiattack routine. It improves the value of both
our attacks and enemy danger estimates. It can start without changing movement policy.

**Summon signature abilities** remain worth developing, especially delayed effects such as Unholy Nimbus.
However, the old inventory overstates some gaps: its label for personal area control can disagree with the
runtime. Fix that evidence gap before choosing which abilities need new forecasting.

These are design inspirations, **not a WorldCrawl compatibility programme**. No WorldCrawl dependency, custom
component adapter, encounter rebalance, installation change or Rogue Trader subsystem port is proposed.
The [planning prompt](TACTICAL_EXPANSION_PLANNING_PROMPT.md) asks the advanced coder to verify and plan one focused
project, and permits a better-supported choice.

## Scope and evidence

Research branch: codex/tactical-expansion-investigation, initially from development **96dabaa**. The candidate
source inspected is **9a02565**, handover head **6efc13c**, in [PR #73](https://github.com/recovribox-svg/AIControll-FACC/pull/73).
Before publication, Claude integrated #65–73 into development. This task branch is reconciled with **980cf26**;
its gameplay source equals the inspected candidate (no C#/project difference). The task adds no gameplay edits.
The supplied gaps2 gameplay used **96108ba**, MVID **9ce04a64-f9a0-43ce-8275-5f1b0c7bbe73**.

Evidence levels used below:

- **Verified:** an inspected source/IL route, serialized graph or counted log event.
- **Inference:** the likely consequence of those routes, without a live reproduction.
- **Proposal:** a useful design to assess; no claim of an implemented improvement or measured benefit.

[Input and inspection receipt](competence/TACTICAL_EXPANSION_RESEARCH.json) identifies the inputs and selected
graphs. Raw uploads, game assemblies, blueprint graphs and decompiled third-party code remain private.

WorldCrawl's upload contains version **0.594**, manifest GameVersion **2.5.0n**. Its DLL and packaged cache are
byte-identical. The DLL was decompiled for static inspection; it was never loaded as a running game mod.
CompanionAI v3's uploaded source identifies **3.123.1** for **Warhammer 40,000: Rogue Trader**. Its older README
version is not the archive's current manifest. Neither mod's gameplay quality or performance was benchmarked here.
The Wrath reference assembly and stock BubblePrints data are the retained, identified inputs used for earlier work;
19 selected stock records were decoded across four dependency lookups. That is not a whole-database validation.

## What WorldCrawl actually does, and what is worth learning

### Contextual, curated actions rather than a replacement planner

Verified in WorldCrawl.AIActionSeries.CreateSeriesAction:

- Copy a native BlueprintAiCastSpell and replace its ability, base score and per-combat count.
- Set native cooldown fields; select actor and target considerations.
- Optionally prefer areas with more enemies, avoid allies, or require an available swift action.
- Usually add the native ChaoticBehaviour random consideration.

Hold variants add checks for an existing Hold/Icy Prison buff. Magic Missile adds a Shield check. Some dispels
avoid a threatened caster. Heal variants use an injury consideration. GeneralAIActionEdits limits one Glabrezu
Mirror Image action to one use. These are concrete contextual choices, not evidence of multi-turn planning.
AIActionHandling.HandleAIActions and NewConsiderations.CreateNewConsiderations are empty in this binary; their
names are not implemented features.

Selected stock data clarifies the semantics: the referenced random consideration scores **0.8–1.0**;
AoEChooseMoreEnemies has MinCount 2 but **BelowMinScore 0.2**, not a prohibition on one target. The no-threat and
swift-cooldown considerations can score zero. Thus even this mod's apparent casting triggers are a mixture of
preferences and actual gates.

**Transfer the idea:** ask what a particular ability changes in the current fight, and refuse it only for a
verified constraint. Keep player policies, resource costs and marginal utility on the engine's shared scale.
Do not import NPC combat quotas, randomness, a blanket two-target AoE gate or a new emergency HP threshold.
AI Control already compares area utility, resource policy, healing urgency and current alternatives.

### Kits are paired with the actions that use them

FeaturePackReplacement.CreateFeaturePackWithActions pairs granted facts with a list of native cast actions.
MainReplacements.AddFeaturePackWithActionsToUnit adds both the feature pack and the corresponding brain actions.
For example, Fey caster packs grant actual spells and the actions intended to use them; warrior packs combine
combat features with suitable spell actions.

**Transfer the idea:** test a creature as a complete kit. Can AI Control discover the owned ability, interpret its
delivery and payload, price its real action/resource costs, and issue it in the right situation? S1 already provides
the inventory foundation in [SUMMON_KIT_INVENTORY.md](SUMMON_KIT_INVENTORY.md). Extend that work with concrete
behavioral acceptance, rather than another set of inferred creature roles.

Brain actions are useful evidence of an intended kit, not automatic authority to manufacture an AbilityData or
copy enemy decision priorities. Native brain counts/cooldowns and actual spell resources are distinct: the
inspected AiAction.IsActorValidInternal checks CombatAiData, whereas AbilityData.IsAvailable is a separate check.
That distinction is a design lesson about availability, not an authorization to impose NPC quotas on companions.

### Attack consequences beyond a successful hit

WorldCrawl.OnMissDamageByLevel reacts to a failed RuleAttackWithWeapon and damages that attack's target. It reads
the initiator's level and facts to choose its damage. It is **not a proximity aura**, and a CR-based penalty for
standing within five feet would model a different mechanic.

The useful general lesson is to forecast the relevant outcomes of an attack. Wrath already has a native example:
DestructiveShockwaveMythicFeat, **263a65c1e92dd964192ba7d41a76b4e3**, uses a melee OnMiss trigger, a shared Strength
value and a one-second target buff. The current on-hit reader excludes OnMiss; the weapon quote calculates a
successful damage bundle and weights it by hit chance. A miss-trigger contribution needs its own verified route,
not another multiplier on successful-hit damage.

WorldCrawl also adds stock FixedObsessionEnchantment, **aec23b4820f2efc48beca7acfb0c8883**, to Wild Hunt weapons.
The selected stock graph applies a stacking buff **to the attacker**, capped at ten ranks and removed next round.
It contains no same-target requirement. Moreover, the extra-dice component retains a ThrowingAxe weapon filter,
which differs from the three Wild Hunt weapon types inspected. The description alone therefore does not prove
that the advertised additional force damage works on those weapons in the supplied mod.

**Transfer the idea:** bounded, source-verified changes during a routine can matter. Do not turn this into a
flat team focus-fire bonus or assume one creature's proc is shared by its allies. Any new routine model must
respect weapon filters, proc order, hit/miss/critical branches, caps, immunities and current buff ownership.

## What CompanionAI v3 contributes

The uploaded source includes actual callers, not just architectural diagrams:

| Mechanism inspected | Useful idea for Wrath | Why the implementation should not be copied |
| --- | --- | --- |
| TurnStrategyPlanner.EvaluateInternal and KillSimulator.FindKillSequence | Compare a small number of meaningful action sequences, including preparation plus attack | Rogue Trader AP, Run and Gun and repeated attacks do not describe Wrath's standard/move/swift/full-round rules |
| TacticalOptionEvaluator, called from BasePlan.PostValidation | Value movement together with the action it enables | Its four strategy scores include fixed hittable-target weights and grid quality; Wrath already has integrated approaches, exits and leap quotes |
| ActionExecutor validation and GroupFailurePolicy | Recheck a plan at issue; make failure recovery explicit | Reuse Wrath's dispatcher and drivers rather than adding a second command owner |
| CombatCache and PredictedEnemyMoves | Reuse proven repeated work; distinguish missing prediction from a safe position | Its game supplies grid move variants; that service is not established in Wrath, and cache identities/invalidation need the actual Wrath inputs |
| EnemyTargetingMap | Relate threat to the particular ally exposed to it | Wrath already has victim-aware incoming damage and drop estimates; a full enemy-by-ally matrix may multiply expensive forecasts |
| IssueDetector and CombatReportCollector | Combine decisions and outcomes into reviewable evidence | Unspent AP, low HP without a heal and movement without an attack are not automatically mistakes, especially during native grapple maintenance |

KillSimulator explicitly distinguishes its optimistic IsConfirmedKill property from KillProbability. Its
single-attack calculation uses a uniform min/max damage approximation. TurnStrategyPlanner still has a caller
that reads IsConfirmedKill. That is a useful warning against treating a class/property name as a probability
guarantee, not a model to import.

Likewise, comments claiming sub-millisecond or zero-allocation planning are not our measurements. The inspected
paths include lists, LINQ and diagnostic formatting. Their performance claims do not establish a budget for Wrath.

The archive also contains LLMScorer/LLMCommander, HTTP requests and optional model-driven directives. Those are
not recommended for AI Control's combat loop. The useful transferable concepts are bounded alternatives,
state validation and outcome evidence; they do not require model calls, a local inference installation or
nondeterministic instructions during combat.

## Corrections to the supplied WorldCrawl investigation

The supplied investigation is an idea list, not an authoritative audit of the present tactical engine.

| Claim or recommendation | What the current source establishes |
| --- | --- |
| Enemy threat is only a flat CR estimate | EnemyThreat also reads weapons, usable harmful spells and heals, observed offence and urgency; CR still supplies the overall worth scale |
| No invisibility or Mirror Image awareness | CombatMath.Defences reads native concealment and mirror images; existing reveal work must be preserved |
| The historical never-cleared immunity cache is still a current tactical defect | CombatRulesAnalyzer's caches are frame-scoped and cleared at combat end; the older TargetImmunityChecker also has a five-second duration and is used by legacy paths |
| Fix the historical 1.85x fight-length bias by adding observed DPR | ValueModel.PartyDamagePerRound already blends observed completed-round damage with a prior; ExpectedRounds is bounded 1–6. Old audit figures cannot diagnose the present estimator |
| Add a weapon-primary state when spells run low | Weapon and spell candidates already compete; a sticky mode could suppress a subsequently useful spell |
| Add focus-target state to TeamBlackboard | It already has SharedTarget in the legacy architecture; the tactical path separately has pending-cast coordination and attack/kill values. Adding a second authority would not solve marginal team value |
| Add a desperation phase below 25% HP | The legacy detector already has Desperate; the live engine prices danger, healing and exits. A new threshold needs an unmet case |
| Require two targets for high-level AoEs | Native WorldCrawl considerations do not establish that rule; a single dangerous enemy may justify the spell |
| Penalize nearby high-CR enemies to represent miss damage | This loses the actual attacked recipient, attack frequency, outcome and mitigation |
| Buff spell-level tiers solve superior-buff overlap | Existing stacking and recipient-specific buff appraisal already compare actual effects; spell level is not a stacking rule |
| Add controller/sniper subroles for intelligent behavior | Kit-aware forecasts are preferable to another layer of arbitrary weights without evidence |
| WorldCrawl only changes enemies and never overlaps party mechanisms | Its data edits, global mechanics and summon kits also reach party gameplay; this fact does not create a compatibility task here |

The historical [AI_TUNING_AUDIT.md](AI_TUNING_AUDIT.md) remains useful history. Read its live/legacy distinction
and later implementation sections before treating an old finding as new work.

## Ranked development candidates

Impact and complexity below are engineering judgments, not measured playtest improvements.

### 1. Native tactical mobility for companions and summons

**Benefit:** a capable creature can use its own repositioning ability to make a worthwhile attack possible or
reach safety, rather than only walking or waiting. This can improve summon competence and companion builds
through one mechanism.

**Evidence:** the current summon inventory and gaps2 audit still report DemonTeleport, Vrock Greater Teleport
and DimensionDoorHell as unread. The old nine-ability/eleven-creature gap family also includes non-teleport
abilities; do not present that whole count as teleport coverage.

The supplied graphs establish materially different cases:

- DemonTeleport, **b3e8e307811b2a24387c2c9226fb4c10**: Move action, native AbilityCustomTeleportation.
- DimensionDoorCasterOnly, **a9b8be9b87865744382f7c64e599aeb2**: Standard action, native Dimension Door radius zero.
- DimensionDoorHell, **637e9c2a9d121584fb8b3f3ec799eb1e**: Standard action, radius ten feet.
- Vrock Greater Teleport, **c9a902658593a2b4d9c24b1d108a0ccc**: Standard action, enemy anchor, radius five feet.

Native AbilityCustomTeleportation moves only its caster in the inspected settings, but can offset a unit-anchored
destination or follow an existing roaming path. Native Dimension Door can collect nearby group members, add
mounts, adjust placement and apply its transport buff. Target checks include navmesh, fog and disabled-island
rules unless an explicit native exception applies. These are not interchangeable jump-to-point commands.

**Feasible first slice:** qualify one self-only delivery and a small bounded set of destinations derived from
already-valued actions. Compare the enabled legal action and safety against existing options, including the
ability's real action/slot/resource cost and remaining permissions. Ask the native delivery/placement rules;
do not temporarily move an actor or assume a Standard teleport permits an immediate full attack.

Reuse the existing candidate competition, danger/area checks, ownership/blocklists and dispatcher. Keep the
Griffon leap as its existing native attack delivery. Never convert every custom teleport into that leap.
Group transport, swaps, path-following deliveries and cross-level destinations should remain separate until
their additional recipients and restrictions can be valued.

**Complexity:** high, but bounded and feasible with retained native inputs. **Movement implementation needs
the author's specific approval**; the present request authorizes investigation and a plan.
Acceptance needs both modes, same-level blocked approaches, unsafe versus safe landings, no remaining actions,
native refusal, manual ownership, and a 4–8 summon pack. No plan should claim an unseen next-turn path is certain.

### 2. Weapon forecasts that preserve hit, miss and proc state

**Benefit:** better comparison of attacks, modes, target damage and enemy danger when a successful hit is not the
only relevant outcome. This serves martial companions, natural-attack builds and summons through shared math.

**First slice:** a verified native miss-damage contract such as Destructive Shockwave. Trace its actual shared
value, buff application and damage mitigation. Keep miss damage separate from hit chance, critical multiplication
and sneak attack; an effect on the attacker must not become target damage.

**Later slice:** one bounded self-stacking proc, only after checking weapon filters and native order. Reuse
cached routine lines; carry finite scalar proc state, not mutable actors or MechanicsContext objects between turns.
Existing grapple truncation must stop later proc/attack credit. Current facts and already-counted damage must not
be added again. Unknown handlers or arbitrary trigger scripts remain unsupported.

**Complexity:** moderate for the first verified contract, high for a general routine extension.
No WorldCrawl-specific handler is required or proposed. The performance gate is unchanged forecasts for
unaffected routines and a measured, bounded cost for affected ones.

### 3. Summon signature abilities: correct the evidence, then value timed effects

**Benefit:** creatures use their distinctive kit with an appropriate target and timing, rather than merely
being driven by the same controller. There is already summon control and an S1 inventory; neither should be rebuilt.

**Important correction:** the old S1/gaps2 report calls Vrock Stunning Screech and Derakni Drone generic buffs.
However, runtime ScoreAbility collects recipients around a personal AoE and values ControlStrength even when
HarmfulEffects is false. Audit.Classify's personal-area branch omits ControlStrength in its enemy-side test.
Thus those labels do **not** prove that runtime lacks their stun/confusion value. The runtime's possible
beneficial-buff branch and per-recipient immunity/marker behavior still deserve focused verification; no live
Screech/Drone cast appears in gaps2.

First reconcile the audit with compiled runtime routes. Vrock's graph checks enemy allegiance and demon immunity;
Drone uses an around-caster selector plus per-recipient save/immunity state. Avoid blanket personal-means-helpful
or every-personal-control-is-hostile rules, which could break self buffs and forms.

The clearer extension is Nalfeshnee's Unholy Nimbus:
**c5b6fdcee6ff4e843a02dabc247210ca** applies delay buff **ec14c8e821c460b42bb925a2320ddf0c**;
its Deactivated actions cast the blast **ba3e6e4f918d15f4b8858758610e0e2e**.
An immediate generic buff estimate cannot represent that event's timing and changing recipients.
Plan a qualified delayed-payload contract with native trigger/expiry semantics, useful control horizon,
friendly-fire safety and conservative spatial uncertainty. Do not execute deactivation actions during forecast
or generalize this to arbitrary recursive buff scripts.

**Complexity:** moderate for truthful classification; moderate/high for a first delayed effect.
Use the existing periodic/attached-damage readers where their contracts match. They already handle selected
recipient-owned effects; this is not a proposal to add damage-over-time support from scratch.

### 4. Bounded marginal alternatives and team coordination

CompanionAI v3's short sequence templates are a useful inspiration. A Wrath version could improve preparation
versus immediate action and later the holder's real opportunity cost, rather than adding a focus bonus.
Existing Fury/form, marks, boosts, modes, Magus and pending-cast work already covers specific parts of this problem.

A full turn tree or squad planner is not the next safe step. Start only with a proved misdecision that existing
terms cannot express. Reuse existing scalar quotes, define action/permission transitions, retain uncertain outcomes
and revalidate at issue. A hold's alternative cannot be obtained by removing grapple parts or calling full Decide
again; it would create circular pricing. RT commands already in flight need marginal effect accounting, not an
assumption that all reserved damage will land. No such new general model is justified by the gaps2 Invocation
count: its corrected review found no confirmed spam requiring a cast cap.

**Complexity:** high/very high; broad potential benefit, less certain immediate return than candidates 1–3.
It should follow a smaller measured implementation rather than replace the tactical engine.

### Existing Fusion work remains a useful alternative

If movement is not the next chosen project, completing **per-save-outcome control** is another strong candidate
from the existing engine work. PR #69 documents 168 saved Frigid templates withheld because their control occurs
outside the model's supported failed-save branch. The graphs and joint-outcome foundation already exist.
Extending that model can make more created spells usable without inventing critical rates or adding another
save/attack roll. This is a continuation of the Fusion plan, not an idea borrowed from either supplied mod.
See the [#73 assessment](https://github.com/recovribox-svg/AIControll-FACC/blob/6efc13c6e6a349b7cfbe3ebe80bce2a80fef36fc/docs/GRAPPLE_GAP_ASSESSMENT.md).

## Coup de Grace report: what the gaps2 evidence establishes

The AI log has **zero issued Coup de Grace commands** and **zero recorded Summon/SummonTurn takeover messages**.
Its 27 case-insensitive Coup mentions are coverage, classifier or slow-decision descriptions. The paired combat
log has no Coup de Grace event. Consequently this pair does not confirm a summon executing it at range; it also
cannot disprove the incident seen in another session. ToyBox spawns and rests between encounters are retained
as context, not treated as the cause.

There is a **concrete possible mismatch in the native execution chain**:

1. Stock ability **32280b137ca642c45be17e2d92898758** has Range Weapon and requires a melee weapon, allowing limbs.
2. Native AbilityData.GetApproachDistance takes the first weapon's AttackRange for Weapon range, plus both bodies.
3. Stock ContextActionMeleeAttack uses GetThreatHandMelee. Its SelectTarget returns an explicitly supplied target
   without a second distance check when SelectNewTarget is false.
4. The supplied TTT Base fix replaces the effect with TTT Core's CoupDeGraceTTT but does not change the range.
   That delivery uses GetThreatHand, whose native choice can include a ranged weapon with Snap Shot.
5. AI Control follows native ability range, while EstimateCoupDeGrace currently prices PrimaryHand.MaybeWeapon.
   Those choices need not identify the same legal melee attack in a mixed weapon/limb body.

This is a source-verified mismatch to test, **not a reproduced explanation of the author's particular incident**.
Large bodies and legitimate melee reach can also look like a ranged animation, so a fixed visual distance limit
would be wrong.

**Proposed fix:** one shared Coup execution contract for the supported native/TTT delivery. Select the actual legal
melee weapon, use it consistently for damage and reach, and cap the AI command's approach radius to that weapon's
native melee reach including bodies. Apply the same rule to appraisal and final dispatch, preserving a stricter
ability restriction. A native delivery that would choose a ranged weapon or an unread replacement must not be
silently rewritten into a different attack. Preserve helplessness, full-round affordability, AoOs and the normal
approach safeguards. Scope it to AI-issued Coup commands, not global game/player behavior.

Required regression cases: melee and natural-only creatures; bow plus melee limbs/offhand; Snap Shot; reach
weapons and different body sizes; height/LoS; target changing state before issue; both modes; stock and supplied
TTT delivery. The live replay needs an actual Coup issue/end and matching combat event, with distance, selected
weapon and allowed radius captured without additional path searches. If a new report involves another replacement,
inspect its live Coup blueprint before claiming support. **No runtime fix is committed in this research task.**

## Leaps and Frigid Touch: how to test

Leave the companion under AI Control. A supported available ability should be selected automatically when it
wins the normal action/resource/safety comparison. Manual casts can establish a native baseline, but do not
establish that the engine chose, targeted and dispatched the action correctly.

For a leap, provide the correct form/ability, an eligible enemy, a legal safe landing and enough action budget.
For Frigid, ordinary Frigid or a supported no-save Runes/Frigid configuration exercises the currently read minimum
stagger. The saved configurations explicitly withheld by #69 will not automatically cast until that model is extended.
An available but losing option is not itself a bug; the appraisal and refusal reason determine what to investigate.

## Performance and handover

The gaps2 review records 62 slow decisions; warm Orvath ability stages around 450 ms remain material. None of these
proposals earns permission to multiply that work by every unit, destination and hypothetical next turn.

Any chosen plan should specify cached immutable metadata, the exact scalar inputs reused within a decision,
caps on native quotes/destinations and invalidation after state changes. No persistent actor/context forecasts,
repeated full Decide calls, new exhaustive path search or arbitrary candidate pruning. Measure the affected stage
and total decisions in TB and RT, with a mixed kit and a 4–8 summon pack; signed heap deltas are not allocation totals.

Relative to reconciled development, this branch changes research, planning prompt and handover documentation only. No game behavior, movement policy,
build/package, version or binary changes. Static evidence and log counts were checked; third-party runtime and
new gameplay acceptance are not claimed. Implementation must use current development, inspect the open stack,
follow CONTRIBUTING/CLAUDE/AGENTS, keep changes on task branches and provide the normal native build/audit,
tests, receipt and focused gameplay handover.
