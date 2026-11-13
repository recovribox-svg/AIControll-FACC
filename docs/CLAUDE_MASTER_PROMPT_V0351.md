# Claude master prompt: AI Control outstanding work after v0.3.51

Continue development of **AI Control – Fully Autonomous Combat Companions** in
<https://github.com/recovribox-svg/AIControll-FACC>. Independently review the current implementation and this
evidence, then implement feasible improvements with carefully integrated, performant code. Priorities from
the author are **summons competence**, **Profane Invocation decision quality**, and **grapple initiation,
maintenance and release** so Grabber and comparable mechanics can be supported. Use your judgment about
architecture and work order. The suggestions here are leads, not a mandatory implementation recipe.

Fetch the latest `claude/dreamy-pasteur-b9h3rn` and inspect open PRs before editing. The starting development
revision for this review was `c92b607` (PR #54 integrated); v0.3.51 is the follow-up on
`codex/v0351-playtest-review`. Use the final source commit/MVID in
[the v0.3.51 identity](../release/CompanionAI_Pathfinder_v0.3.51.identity.json) and
[validation receipt](competence/V0351_PLAYTEST_CHECKS.json), then reconcile later development. Do not rebuild
from an old 0.3.30/C06 checkout or assume a packaging/merge commit identifies a different gameplay DLL.

## Evidence and status

Read `AGENTS.md`, `CONTRIBUTING.md`, the **newest** `CLAUDE.md` handover, `docs/TACTICAL_AI.md` §160 and
[the v0.3.51 review](V0351_PLAYTEST_REVIEW.md). Earlier handovers and class matrices are historical evidence;
an old “unsupported” entry is not proof that a later implementation is absent. The old `LIVE_CODE_AUDIT.md`
is orientation, not an up-to-date defect register.

The two original uploads, `Log.txt` and `combatLog.txt`, are the **v0.3.50 stable** playtest, startup MVID
`45ab748a-b0e9-4db9-b62a-3de2f4eff6fb`. Their hashes/row convention are in
[V0351_PLAYTEST_INPUTS.json](V0351_PLAYTEST_INPUTS.json). **Rests were between encounters**, explicitly confirmed
by the author. Do not explain within-fight transfers as rests. ToyBox spawning occurred in older tests;
its exact use in these particular uploads was not separately reconfirmed. Ask only for missing evidence
that materially affects a conclusion; continue independent work meanwhile.

There are four fights, 203 measured TB / 839 measured RT decisions, and no observed AI Control exception.
Only 324 RT tactics summaries were retained; quiet decisions are still included in the measured count.
Compilation, static/metadata checks, native scalar harnesses and actual in-game acceptance are different
levels of evidence. v0.3.51's corrected behavior is **not** played in these logs.

Use **confirmed** for observed log facts or directly inspected native/source contracts. Use **hypothesis**
for proposed causes/solutions not demonstrated in a controlled replay. “Not exercised” means missing evidence,
not working or broken. Every topic below is still open to some degree; none authorizes a speculative shortcut.

## Outstanding topics, ordered by estimated difficulty

Difficulty is a planning estimate, not measured effort or priority. Investigations can reveal a smaller fix
or a much larger dependency. The author priorities above may justify starting with topics 5, 7 or 8.

| Rank | Topic | Estimated difficulty | Current limit |
| --- | --- | --- | --- |
| 1 | Complete reproducible audit inputs / provenance | Low–medium | One unchanged export cannot load against the supplied enum; stock and runtime-mod graphs must stay distinct. |
| 2 | Death from Above landing admission and live execution | Medium investigation | No issued leap in the played logs; v0.3.51 adds refusal witnesses and corrects queued-Pounce pricing, but needs replay. |
| 3 | Refused approaches, idle recovery and move-only swarm decisions | Medium–high | A positive unvalidated option can end in no action; actual native movement/settings/known-HP state are missing. |
| 4 | Specific unsupported effect/mode contracts | Medium–high, mechanism dependent | Several actual kit components remain outside readers; usefulness and full lifecycle must be established individually. |
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

**Confirmed:** AI rows 534/1228/1954/3895 report endpoint attempts 2/0/8/0, with **zero weapon quotes**;
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

**Confirmed:** Ulbrig AI 535–540 exhausts three selected path checks on five-foot full-attack candidates,
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

**Confirmed:** current coverage explicitly excludes Release Grapple (533, handled in topic 8), Greater Stunning
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

**Confirmed:** 28 issued casts, **27 acted successes + one unacted interrupt**, by fight **8/5/10/5**.
The native two independent six-stat tiers allow separate placements; variants remove same-tier siblings on
the recipient, while `UniqueBuff` moves the caster's same-blueprint copy from another recipient. Duplicate
localized names are different abilities. Use GUIDs/source context/caster/recipient identities.

Key original rows:

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

**Confirmed:** 43 slow rows, none with a GC marker. AI 3713: Tuvel 1084 ms, abilities 988.7 ms, 258 scored;
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

Read [SUMMON_CONTROL_PLAN.md](SUMMON_CONTROL_PLAN.md), its native evidence, §156–157, `SummonControl`,
the TB summon driver and shared tactical engine. Basic support already exists and is **opt-in/default off**.
Preserve direct linked-summon ownership, fresh final authority/fact rechecks, weak/temporary settings,
player blocklists, no saved summon IDs, no party-item/material spending, and no recursive summoning. Pets and
manual orders are separate policies. Do not redefine every friendly or ToyBox-controlled creature as eligible.

**Confirmed:** Abrikandilu's TB Sunder Armor (1621–1623) and Vermlek's move/attack (1736–1738) are serviced;
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
  startup MVID; do not reuse a played package's identity. The next requested build in this task is 0.3.51;
  coordinate later versions rather than silently overwriting it.

Begin with a short evidence-backed assessment of what current development already fixes, which author
priorities you will tackle first, and what can proceed without more gameplay data. Then carry the selected
work through implementation and validation. If a topic cannot safely be resolved, leave a precise reason,
minimal missing evidence and concrete next check; do not promote a hypothesis into a fix or a PASS.
