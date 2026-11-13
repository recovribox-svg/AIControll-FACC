# Claude master prompt: AI Control outstanding work after the v0.3.52 review

Continue development of **AI Control – Fully Autonomous Combat Companions** in
<https://github.com/recovribox-svg/AIControll-FACC>. Independently assess the current implementation and evidence,
then implement feasible, integrated, performant improvements. The author's priorities remain **summons competence**,
**Profane Invocation decision quality**, and **paired grapple initiation/maintenance/release** for Grabber and similar
mechanics. Use your judgment about architecture and ordering; this is evidence and guidance, not a rigid recipe.

## Latest review takes precedence

Fetch current `claude/dreamy-pasteur-b9h3rn` and inspect open PRs. This follow-up starts at `00ab4e52`, after merged
PR #55, on `codex/v0351-live-review`. The author selected **v0.3.52 as the next stable package** and additionally
requested a logging audit. The filename of this master prompt stays unchanged so earlier GitHub links keep working.
Read `AGENTS.md`, `CONTRIBUTING.md`, the newest `CLAUDE.md` handover and `TACTICAL_AI.md` §161.

Current evidence and package:

- [Latest broad review and replay](V0352_PLAYTEST_REVIEW.md).
- [Latest inputs, hashes, physical-row convention and Invocation GUID trace](V0352_PLAYTEST_INPUTS.json).
- [v0.3.52 identity](../release/CompanionAI_Pathfinder_v0.3.52.identity.json) and
  [validation receipt](competence/V0352_PLAYTEST_CHECKS.json). Use its checked source/MVID; reconcile later development.
- [Earlier v0.3.50 review / v0.3.51 fixes](V0351_PLAYTEST_REVIEW.md) and
  [earlier native evidence](competence/V0351_NATIVE_REVIEW.json) remain historical context.

The latest two uploads, `Log.txt` and `combatLog.txt`, are **played v0.3.51**, MVID
`e31e626d-9c4b-4536-8b26-63433d3c7425`. The author confirms **swarm spawns during encounters and rests between
encounters**. They contain six fights, 205 measured TB / 746 RT decisions, 343 issued commands with terminal rows,
and no observed AI Control exception. Retained tactics summaries are fewer than measured decisions. These logs do
not play v0.3.52. Raw logs, game assemblies/mod binaries and full exports are private inputs, not committed source:
request the exact hashed files if unavailable, and check which native/BubblePrints resources actually exist locally.
Codex's private workspace paths are not portable. Independent source/native work can proceed meanwhile.

**Changes already made for v0.3.52:** the strictly recognized native leap now uses the existing owned/allowed attack-
mode chooser at its landing, from its exact cached quote. This closes the comparison asymmetry visible at AI 2359
(walk gets Fighting Defensively off credit; leap does not). Combined charge/mode AC, existing management/ownership/
blocklist/hysteresis/dispatch rules and query limits are retained. Mode repricing makes no extra native own-weapon
quote. Bounded diagnostic change memos reduce repeated SpellPen/blocked/empty-enemy messages; quiet discarded
summaries and disabled verbose math avoid formatting. Idle-recovery state witnesses add no path query. Inspect and
regress this implementation rather than implementing it again. No leap execution or measured speedup is claimed.

**Current unresolved evidence:** safe leaps are admitted but none issued; Invocation 18 issues / 17 successes still
needs marginal-utility review; several idle turns follow real path refusals; four no-effect Dazzling Display casts
need native-selector/skill/immunity/event investigation. Six of 34 slow rows carry cleanup markers; other warm
scoring is costly. Two summons are driven in the final mixed-mode fight; its shared 997-check/27.3ms control total
is printed in both mode summaries and must not be counted twice. Group Channel healing affects multiple allies;
full HP on its caster alone does not establish waste. Eleven external WrathPatches OwnerBlueprint warnings must
retain foreign provenance; do not mutate other mods' graphs as a speculative repair.

Use **confirmed** for observed facts or inspected native contracts; label unproved causes/solutions **hypotheses**.
Missing execution/numerical samples establish neither working nor broken behavior. Older class matrices and
`LIVE_CODE_AUDIT.md` are orientation, not current defect registers. Historical row numbers below are explicitly
labelled; use the latest review/input receipt for the new logs. Do not mix the two playtest identities.

## Outstanding topics, ordered by estimated difficulty

Difficulty is a planning estimate, not measured effort or priority. Investigations can reveal a smaller fix
or a much larger dependency. The author priorities above may justify starting with topics 5, 7 or 8.

| Rank | Topic | Estimated difficulty | Current limit |
| --- | --- | --- | --- |
| 1 | Complete reproducible audit inputs / provenance | Low–medium | One unchanged export cannot load against the supplied enum; stock and runtime-mod graphs must stay distinct. |
| 2 | Death from Above live execution and mode comparison acceptance | Medium investigation | v0.3.51 admits safe candidates; v0.3.52 fixes mode-comparison asymmetry. No issued leap yet. |
| 3 | Refused approaches, idle recovery and move-only swarm decisions | Medium–high | A positive unvalidated option can end in no action; actual native movement/settings/known-HP state are missing. |
| 4 | Specific effect/mode contracts and no-effect Dazzling Display | Medium–high, mechanism dependent | New no-effect casts plus existing unread components need native mechanics/recipient evidence. |
| 5 | Profane Invocation transfers and decision quality | Medium–high | Current model explains bursts, but does not establish all transfers improve actual combat outcomes. |
| 6 | Dense-kit scoring cost and matched performance | High | Warm scoring still dominates some 0.4–1.1 s decisions; live-heap deltas are not allocation measurements. |
| 7 | Expand summons tactical competence | High | Basic driving/actions played with three summons; diverse kits, richer policy and pack-scale acceptance remain. |
| 8 | Paired grapple initiation / maintenance / pin / release | Very high | Self action/movement costs, victim constraints and native automatic maintenance are not modelled together. |

### 1. Audit inputs and native/mod provenance

**Confirmed:** [C06_NATIVE_COMPATIBLE_SCOPE.json](competence/C06_NATIVE_COMPATIBLE_SCOPE.json) identifies the
unchanged `AchaekekSacredWeaponFeature` (`c37574188b0242d2ac04a454a0430dd5`): serialized weapon category
`SawtoothSaber` differs from the supplied native enum `SawtoothSabre`. The raw offline load fails; its attempt
to report the parse problem through Unity produces a secondary ECall exception. The compatible audit excludes
this **one** file with provenance; it does not normalize input or count the raw corpus as passing.

The mandatory leap/charge check needs the two explicitly missing stock charge graphs documented in
[REVIEW2_CHARGE_INPUTS.json](competence/REVIEW2_CHARGE_INPUTS.json); three already supplied graphs were preserved.
Creature affliction, Rat/Bhoga swarm and Swarm Cling checks now have required graph inputs in the existing
compatible scope. Do not silently return to earlier runs that skipped them.

**Next:** reconstruct these named scopes and real-assembly bindings. If the environment lacks an input,
request the exact asset/GUID and its referenced closure. For the enum mismatch, inspect a fresh live export
and the actual installed assemblies before changing the loader or declaring a gameplay mismatch. A safe
loader diagnostic may be feasible; changing game enums or deleting failed evidence is not a correction.

The author supplied the **BubblePrints program ZIP**, which includes a stock 2.7.0x database. It is useful
for GUID lookup, graph references, exact native fields and exporting small missing stock closures. The
wrath.bubbleprints.dev website is not needed. BubblePrints stock graphs do not prove installed mod mutations
or current actor state. Keep copyrighted game DLLs/full graphs/decompiled sources out of commits/packages.

Read [INSTALLED_MODS.md](competence/INSTALLED_MODS.md) / JSON for installed versions/settings, rather than
substituting old public mod source. DarkCodex is 1.9.6, Mind Shield disabled. The supplied `.dll.*.cache`
files are processed assemblies, **not live blueprint graphs**; GUID registries identify assets, not effects.
The exporter is unknown, and some generated mod IDs are absent despite export-after-initialization being
reported. Older Isekai source is orientation only. Psionics source is unavailable; the installed binary can
still be inspected. Request a small runtime graph closure only when its actual patched fields are necessary.

**Acceptance:** all required checks execute on labelled, hashed scopes; raw failures remain explicit;
no “full corpus PASS” with an allowed/skipped file disguised as success.

### 2. Death from Above: admission, native attack and timing

**Latest v0.3.51 evidence:** AI 467/1501/2358 admit 3/1/1 safe candidates; other sampled refusals identify body,
hazard, native-target or range/sight guards. AI 468/1503/2359 shows ordinary attacks winning. The mode-comparison
gap at 2359 is corrected in v0.3.52 using the shared chooser and cached scalar quote. Preserve its query/cache,
combined-AC and ownership/dispatch constraints. Actual leap/queued-attack completion remains unexercised.

**Earlier v0.3.50 evidence:** AI rows 534/1228/1954/3895 report endpoint attempts 2/0/8/0, with **zero weapon quotes**;
no Death from Above was issued. Native eligibility or landing guards rejected all sampled candidates.
This does not prove which guard was too strict. The first guard can mask later possible refusals.

v0.3.51 records the first refusal/witness and an admission note, without new geometry/attack queries or
larger budgets. It also fixes the forecast to match the native queued charge/Pounce restriction branches;
see [V0351_NATIVE_REVIEW.json](competence/V0351_NATIVE_REVIEW.json). Do not repeat that fix or claim this
playtest validates its runtime effect.

Read `TacticalEngine.Griffon.cs`, `CombatMath.Griffon.cs`, `AbilityProfile.Griffon.cs`, `GriffonAttackRules.cs`
and the actual `AbilityGriffinAttack`, `UnitAttack.InitAttacks`/`IsFullAttackRestricted`. Native landing is
nearest-node projection of the body-touch endpoint. Ordinary walking paths are not flight paths. Existing
body overlap allows native touch spacing; it does **not** reject every body-touch landing. The private
helper is already bound. No guessed damage multiplier or unused griffon buff is needed.

**Next:** obtain one controlled open landing in form, then body-blocked/hazard/swarm endpoints in both modes.
Compare current native target restriction, landing coordinates/corpulence, expected attack routine and
actual queued attacks. A refusal witness is one sampled reason, not a full histogram. Inspect native mode,
range, mount/stealth/charge and ability action eligibility if no endpoint is attempted.

**Hypothesis:** nearest-node projection, an occupied landing, or an area guard can explain some refusals.
None is currently established as erroneous. Correct an identified predicate or native model; do not remove
body/hazard/swarm checks, pick an endpoint the actual ability will not use, raise budgets, or force a leap.
Positive charge attack bonus and charge-only procs remain uncredited because a six-second buff begins before
the flight; animation-sensitive valuation needs evidence, not an invented timing schedule.

**Acceptance:** legal useful leaps can compete and complete; native queued attacks/turn completion/actions
are correct in TB and RT; unsafe/changed endpoints are refused; ordinary movement/attacks still function.

### 3. Idle approaches and move-only swarm behavior

**Latest evidence:** Aeiluvetar 4225–4227 ends after 13.2m paths exceed its 9.1m one-move budget; Abrikandilu
4323–4327 has three endpoints 0.5m short. Valmot 4337–4342 gets an advance, but its native path goes nowhere.
These are not proofs that unchecked alternatives are legal. v0.3.52 adds a verbose, bounded idle gate witness
with existing movement/step/policy/range/role/unreachable state and no extra path query. Use it to distinguish
native movement, settings/goal-radius behavior and remembered unreachable targets before changing policy.

**Earlier v0.3.50 evidence:** Ulbrig AI 535–540 exhausts three selected path checks on five-foot full-attack candidates,
each requiring 3.3 m of native path, then ends with SM/full movement flags displayed. Ten unchecked walking
options were withheld; raw scores 56.8/37.3/29.3/20.7 are **not validated actions**. No lazy-recovery note appears.
Bloodragers 1564–1569 is a separate case after an actual move and a sight refusal. By contrast 1353–1354
has all-negative options and an explained end turn.

Read `SelectedApproachFilter.cs`, `TacticalEngine.ApproachValidation.cs`, closing candidates in
`TacticalEngine.cs` and `TacticalEngine.Spots.cs`, and the dispatcher. The lazy recovery already
exists and can offer plain advances: it is gated by actual remaining movement/step, permissions, weapon
policy, failed keys, positive value and pending in-place attacks. It is not missing wholesale.

**Next:** capture the gating values only when an otherwise idle recovery is suppressed: native remaining
movement, step taken, role/range/Pure Mage settings, rejected offense and the three target/advance outcomes.
Check whether a requested ranged/support standoff makes an advance appear already satisfied for a melee
form. **Hypothesis:** such a settings/goal-radius interaction, native movement state or another gate explains
Ulbrig's idle result; the current logs do not choose among them. Do not add a fourth path probe, a full second
decision or unchecked move-and-attack. The author has authorized necessary movement fixes, subject to evidence.

For swarms, AI 583 has a nauseated full-HP Bloodragers with danger 36 but exit worth 0; 3239 has an exit -6.6.
AI 749 prices a direct swarm escape 11.2 against a chosen Gibber-area exit 12.0; 22 Gibber exits were issued.
The existing HP reserve covers only **known native HP-only cast damage avoided**, once, at the existing 0.3 rate.
DR can remove direct HP while poison/control still contribute HP-equivalent danger. Capture absolute HP,
DR, source-specific HP at both points, status costs, path/risk/ally/opportunity costs and endpoint safety.
**Hypothesis:** a residual valuation gap exists in move-only turns; this log alone does not establish one.

**Acceptance:** a legal positive plain advance/escape is considered when nothing useful remains, with native
movement/action limits and hazard checks retained; a costlier/unsafe move or useful in-place action can still
justify staying. Verify TB, RT, nauseated, five-foot-step and ranged noncaster settings.

### 4. Specific effect and mode support boundaries

**Latest additional problem:** Dazzling Display produces four no-effect observations, AI 1237/1332 and
3931/4057, with current backoff after two per fight. Check actual native recipients/LoS, Intimidate outcome,
existing buff/immunity filters and observer coverage. The current scorer already calls BuffImmune and
HasCCImmunity: do not assume immunity handling is absent. Failed skill checks, selector differences and missing
event evidence are competing hypotheses, not established causes. Avoid merely imposing another cast cap.

The latest log also has eleven external WrathPatches OwnerBlueprint warnings for IncendiaryRunes,
ExplosiveRunes and a ChannelPositiveHarm/Isekai context. Trace installed component ownership and actual patched
graphs before a numerical claim. They are not AI Control exceptions; no speculative blueprint mutation is justified.

**Earlier v0.3.50 examples (the same boundaries still appear in the latest coverage):** Release Grapple (533, handled in topic 8), Greater Stunning
Barrier (`9e33ed477ddd4fad9d90fb64b1a6f732`, `ContextActionApplyBuffRanks`, 3703), Shadow Step
(`f51451385b1545bebfcbbb36b7da0220`, custom teleport, 3704), and some Sword Saint/arcane weapon-property modes
(AI 127/132–145). “Recognized carrier” does not mean every nested component is priced. Tooltips and localized
names are insufficient evidence. Out-of-combat dialog/store abilities need no invented combat utility.

**Next:** select mechanisms with a demonstrated useful scenario. Read installed provider code and complete
graphs, including reflected damage/charges/ranks, action/resource costs, group exclusivity, expiration and
cleanup. Existing Magus, shifter, Focused Shot, paired form, joint-cost and reveal implementations already
exist: extend their contracts instead of introducing parallel engines or assuming whole classes unsupported.

Some Magic Deceiver fusion shapes remain intentionally unsupported: line/additional-area delivery, caster
or healing payloads, nested independent saves, secondary spell/creature payloads and shared-HD limitations.
Inspect current `AbilityProfile.Fusion.cs`, `CombatMath.Fusion.cs` and shared-HD readers before defining scope.
No fusion/bomb numerical sample or named unknown burn-consequence line occurs in this playtest. Faerie Fire
was cast successfully (2685/2721); before/after concealment acceptance still needs a controlled target.

**Hypothesis:** narrow new readers may unlock useful actions; do not generalize one component into all custom
teleports, retaliatory barriers, attack-trigger modes or fusion geometries. Per-mechanism difficulty varies.
**Acceptance:** positive and structural-negative graph fixtures, exact live costs/limits, useful-vs-waste
competition and normal cleanup in both modes. Unknown effects remain explicitly unsupported.

### 5. Profane Invocation: intentional bursts versus waste

**Latest v0.3.51 evidence:** 18 issues, 17 acted successes plus one unacted interrupt; per-fight 1/0/0/6/8/3.
Use the GUID trace in V0352_PLAYTEST_INPUTS.json. Tier-1 Dexterity (`9d1b94bea3984ee3922d201ef49a9163`)
goes to Lann in fight4 R1 (1773), then Tuvel R2 (2032); tier-2 Dexterity to Lann (2067) is independent.
Tier-1 Strength (`4ff5f23901104b77a481eac095fc8967`) goes to Abrikandilu R4 (4166), then Bloodragers R5 (4284).
Spawns during fights can change danger/opportunities; rests were between fights. Actual benefit still needs
counterfactual review. v0.3.52 does not add a blanket cap or retune settling based on these counts.

**Earlier v0.3.50 evidence:** 28 issued casts, **27 acted successes + one unacted interrupt**, by fight **8/5/10/5**.
The native two independent six-stat tiers allow separate placements; variants remove same-tier siblings on
the recipient, while `UniqueBuff` moves the caster's same-blueprint copy from another recipient. Duplicate
localized names are different abilities. Use GUIDs/source context/caster/recipient identities.

Earlier v0.3.50 rows (not the latest upload):

- 84/92 and their following profiles: distinct Strength tier GUIDs `4ff5f23901104b77a481eac095fc8967`
  (tier 1) and `8ea302645ae341e9bcca4929d1822c77` (tier 2).
- 2128: tier-2 Dexterity `4d25cab4db9f4816a710265dfee532e4` replaces Intelligence on the unnamed Magus.
  2288: Intelligence `037b267ffad54f9dbeb8a9a145b57acd` replaces that Dexterity. These are real within-fight switches.
- 2660: tier-1 Strength to Tuvel; 2666: tier-2 Strength to Bloodragers; 2691: tier-1 Charisma
  `9ff93194eea84d569b9fdbe6bd284da3` to Bloodragers; 2698: tier-2 Strength back to Ulbrig.
  The 2696 explanation is 6.6 gross minus 1.3 holder loss = 5.3 net, above the current early-transfer threshold.
- 1528–1531: unacted interrupted transfer rolls back cast memory. Do not count it as a successful placement.

The existing implementation already uses actual removed-buff source identities, same-round locks, a
three-round settling period and gain>=5 override; cached native held-weapon loss, raw integer AC thresholds
and removal urgency were corrected before this playtest. Inspect `TacticalEngine.BuffReplacement.cs`, buff
scoring in `TacticalEngine.cs`, `TacticalEngine.Buffs.cs`, `CombatMath.HeldBuffs.cs`, `ValueModel.UniqueBuff.cs`
and the performed-cast ledger. Do not replace this with a name blacklist or blanket one-cast-per-fight cap.
Exact cached held-weapon repricing is limited to the inspected constant-stat, same-weapon/current-routine
contract. Complex buffs, mixed weapons and stale/unavailable quotes retain the earlier approximation;
the prior fixes did not remove all counterfactual/stat-link/defense-source limitations.

**Next:** reconstruct each transfer by ability/buff tier, real recipient ID, incoming gross components,
same-recipient sibling loss, remote holder loss, placed age and settling override. Log only the selected
transfer's already-computed breakdown where possible; no fresh native estimate just for an explanation.
Compare predicted gain with actual beneficiary opportunities/danger over the next rounds. Test stable
targets, near/beyond hit caps, changing threat, independent tiers, low-impact free actions, interrupted casts
and rests between fights.

**Hypotheses:** strongest-source defense sampling, approximate stat links/caster DC value, HP-changing
Constitution effects or changing enemy/ally opportunities can make transfers unstable. Repeated free actions
can also consume real decision/animation time despite zero game action cost. No one cause is established here.
Correct a demonstrated valuation/counterfactual issue first; any added hysteresis/opportunity cost needs a
general, bounded rationale and positive emergency-transfer cases. Do not hide useful initial placements.

**Acceptance:** explain bursts from identities and marginal utility; stable situations do not oscillate for
illusory gains; significant danger changes can justify a transfer; failed/unacted casts do not poison memory;
both tiers and both modes remain functional with no extra recipient/rule explosion.

### 6. Warm dense-kit decision performance

**Latest v0.3.51 evidence:** 34 slow rows include six cleanup markers (396/2192/2902/3197/3449/3820).
Large negative heap deltas accompany several roughly one-second pauses; do not attribute an entire sample to
its named ability or discovery without separating GC time. Warm Tuvel 2881 remains 649ms, abilities585.4,
157scored, setup11.8+5.1included, live heap+40,968KB. These are not exact allocation measurements.

The author additionally requested logging optimization. v0.3.52 addresses 261 duplicate +4 SpellPen messages,
blocked/empty-enemy repeats, six unguarded verbose math-formatting sites and discarded quiet-summary formatting.
Three diagnostic-only memos have 128 IDs each, reset by fight, retain scalar/string state and suppress overflow
without clearing/replaying old entries. Read V0352_PLAYTEST_REVIEW.md and regress debug off/on, real state changes,
recovery/recurrence, chosen commands and native outcomes. No global logger replacement, action throttling or
numerical performance improvement is asserted. Do not infer all dense-kit allocation/GC cost was logging.

**Earlier v0.3.50 evidence:** 43 slow rows, none with a GC marker. AI 3713: Tuvel 1084 ms, abilities 988.7 ms, 258 scored;
discovery/filter 28.9 ms, pre-score 9.0 ms; live heap +29,060 KB. Rows 3876/4217 retain scoring dominance.
Stage heap KB is **not exact allocation data**. Setup timers are included, not extra total time. Existing
first-index conversion discovery and per-decision native memoization are already implemented.

**Next:** profile a matched saved encounter, same MVID/settings/party/kit, warm/cold separately, with normal
spawning/rests documented. Attribute native contexts, buff/defense appraisal, mitigation, saves/SR, target
enumeration and temporary collections. Track allocations with a suitable runtime profiler if available.
Separate summon control-check cost, first summon decisions and later decisions. Current totals for the three
cohort summaries: 7,593 control checks / 102.8 ms; three summons per fight is not a stress test.

**Hypothesis:** decision-local target/branch/context reuse or an exact cheap inapplicability gate could save
time. Cache keys must include actual ability/conversion/fusion and actor/target/state dependencies, with
existing preview/invalidation boundaries. Do not cross-decide cache dynamic values, mutate actors for speed,
drop low-level spells globally, cut kit size arbitrarily or claim milliseconds from an unmatched log.

**Acceptance:** selected candidate/value/availability equivalence for deterministic fixtures where appropriate;
no stale state after mode/buff/shape/rest changes; bounded memory; matched median/p95/worst and allocation
evidence where available. State tooling limits rather than inventing a numerical speedup.

### 7. Summons competence expansion

**Latest evidence:** two summons in fight6; Abrikandilu Sunder Armor 4008/4009, Vermlek adoption4185 and
mixed TB/RT service/end-turn traces. Abrikandilu4327 ends with refused native approaches, not an exception.
The shared control total997checks/27.3ms appears twice (4590/4592); count it once. This supplements earlier
three-summon evidence, not large-pack or diverse-kit acceptance. Native execution/ownership policies are unchanged.

Read [SUMMON_CONTROL_PLAN.md](SUMMON_CONTROL_PLAN.md), its native evidence, §156–157, `SummonControl`,
the TB summon driver and shared tactical engine. Basic support already exists and is **opt-in/default off**.
Preserve direct linked-summon ownership, fresh final authority/fact rechecks, weak/temporary settings,
player blocklists, no saved summon IDs, no party-item/material spending, and no recursive summoning. Pets and
manual orders are separate policies. Do not redefine every friendly or ToyBox-controlled creature as eligible.

**Earlier v0.3.50 evidence:** Abrikandilu's TB Sunder Armor (1621–1623) and Vermlek's move/attack (1736–1738) are serviced;
RT later-decision cohorts include 162 and 26 decisions. These are basic positive cases, not full diverse-kit
competence or large-pack acceptance. AI 1742 releases a summon because the game prevents control; a status-
driven handback is not automatically a bridge defect.

**Next:** inventory actual summoned kits in the installed game/mod setup, classify each missing mechanism
and model the most useful supported cases through shared readers/math/dispatch. Useful candidates may be
caster summons, innate control/buffs/heals, native natural routines, dispels or limited resources—but verify
their actual graphs, target restrictions, costs, ownership and opportunity before choosing scope.
Determine whether default summon preferences need a bounded policy rather than blindly inheriting a
summoner's ranged/Pure Mage behavior. Current temporary summon settings already separate some of this.

**Hypothesis:** richer shared mechanism support plus small summon-specific policy may suffice; a second
decision engine is not justified. Demonology and generated mod summons need actual linked runtime actors/
graphs where missing; factory/source presence alone is not compatibility acceptance.

**Acceptance:** TB turn starts/acts/ends on useful and idle/refused turns; normal quick/main/move sequencing;
no native turn-status writes or repeated decisions while queued; off/on, owner disabled/unconscious, summon
expiry/death, save/load and RT/TB switches hand control back correctly. Verify representative melee/caster
summons, blocked abilities, no recursive summons/items/materials, and a matched four-to-eight-creature pack.
An unsupported creature should fall back cleanly, not stall combat or steal another owner's command.

### 8. Paired grapple, maintenance, pin and release

**Confirmed:** no Grabber/release sequence was exercised; `Release Grapple`
`38a291cd1c714ae684c922bd571eb1d6` is explicitly unsupported. Prior inspected native code applies
`CantMove`, `CantUseStandardActions`, disabled AoOs and rotation restriction to the initiator, with -2 AB/-4 Dex;
the victim has a different restriction set. Native controller/initiator attack parts handle maintenance,
pin and natural attacks; movement can release a grapple. See [the prior review](REVIEW1_SWARM_PLAYTEST.md),
then independently inspect the installed `ContextActionGrapple`, `UnitGrappleController`,
`UnitPartGrappleInitiator`, `UnitGrappleInitiatorAttack`, `ContextActionReleaseGrapple` and Grabber buff graphs.
Do not assume tabletop prose or one debuff describes both sides.

**Next:** first map the full native state machine: acquire/failed acquire, paired identities, initial and
recurring CMB/CMD checks, automatic damage/pin, escape/expiry/death/unload, initiator/victim action permissions,
movement release, free release target semantics and action/cooldown timing. Record which steps are automatic
and which actually need an AI command. Establish this for both TB and RT before adding initiation value.

Then design a paired-state value: enemy actions prevented and native future grapple attacks versus the
initiator's lost standard actions/movement/AoOs/defense and alternatives. Continuing must compete with
release and available useful actions. Use the actual source/holder/target relationship and native command;
never synthesize maintenance rolls, clear buffs directly, or pretend Grabber is a free generic hard CC.
Release needs a positive reason and must not loop or repeatedly release an absent/stale grapple.

**Hypothesis:** a small explicit paired-state adapter integrated with shared action permissions/value may
cover Grabber and structurally similar native mechanics. Generic all-grapple support is not established;
different mod implementations may need separate inspected adapters. No profitable timing/escape thresholds
can be inferred from the current logs alone.

**Acceptance:** controlled initiate/maintain/pin/escape/release scenarios in both modes; benefit versus a
strong or weak target; danger to initiator; target death, invalid pair, owner/control changes and interrupted
commands; native actions/automatic attacks counted once; no stuck turn, forbidden cast/move, resource leak
or oscillating grab/release cycle. Add graph/scalar tests and fresh gameplay evidence before broad admission.

## Working principles and required deliverables

- **Evidence first:** trace the installed game's decompiled rules and enabled mod patches; record assembly
  identities, exact graph closure and tested scenario. Unknown is not zero. Descriptions/older sources are
  leads. Keep confirmed facts, hypotheses and missing evidence separate in code review and handover.
- **Shared integration:** one `TacticalEngine.Decide` for TB/RT, all commands through
  `GameInterface/CommandDispatcher.Run`, no new decision while commands/queue are pending. Preserve native
  action/turn completion, final authority, reservations, user preferences/blocklists and resource accounting.
  Never write native turn-controller status or drive attacks by manual rule events.
- **Mechanics over names:** identify inspected native types/structures/GUIDs, not localized strings or broad
  class labels. Ranged preference also serves noncasters. A recognized graph and class catalogue entry do
  not imply full tactical competence. Respect structural negatives and graceful fallback for unknown shapes.
- **Performance by construction:** bounded native/path queries, cheap gates first, reuse existing quotes,
  decision-scoped precise caches and explicit invalidation. No global roster scans inside spot loops, unbounded
  target/context expansion or extra native actor/rule queries solely for diagnostics. Measure matched results.
- **Safe forecasts:** do not activate buffs, teleport actors, execute casts or modify live facts to appraise
  speculative effects. Preserve existing narrowly documented preview boundaries; do not expand them casually.
  Model native costs/consequences and alternatives; do not force a favorite ability or add arbitrary cast caps.
- **Focused branches:** one coherent change per task branch/PR into current development; preserve other work
  and record the base. The author has delegated necessary improvements including movement and development
  integration, so routine implementation does not need another permission cycle. Public main/tag/GitHub
  Release publication follows the repository's separate author-authorized release process.
- **Required verification:** all unit tests, relevant inventory tests, **Release against real game assemblies
  with 0 warnings/errors**, ProfileAudit with required affliction/swarm/Cling/charge graphs and inherited native
  harnesses. Resolve every added foreign reference by exact signature. A skipped/incomplete check stays named;
  compatibility scope and supplementary stock fixtures remain distinct from the raw corpus. Harness stubs
  are offline tooling only and must never enter the deployable ZIP.
- **Durable handover:** each PR adds a new `docs/TACTICAL_AI.md` section and updates the newest `CLAUDE.md`
  handover with problem, evidence, final behavior, query/cache bounds, checks, residual limits and focused
  TB/RT tests. Link the checked source and final package. Any ZIP needs identity JSON, SHA-256 and a unique
  startup MVID; do not reuse a played package's identity. The author-requested stable follow-up in this task is 0.3.52;
  coordinate later versions rather than silently overwriting it.

Begin with a short evidence-backed assessment of what current development already fixes, which author
priorities you will tackle first, and what can proceed without more gameplay data. Then carry the selected
work through implementation and validation. If a topic cannot safely be resolved, leave a precise reason,
minimal missing evidence and concrete next check; do not promote a hypothesis into a fix or a PASS.
