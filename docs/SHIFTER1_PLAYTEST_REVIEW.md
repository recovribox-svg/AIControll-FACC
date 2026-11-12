# Shifter1 mixed-mode playtest review

12 November 2026. Starts at PR #42 `20137ba` over refreshed development `1f58150`.
The startup module is **875cf339-de55-4d04-89f9-d5235ec7ecbe**, exactly the supplied shifter1 test.
[Input identities and finding ledger](competence/SHIFTER1_PLAYTEST_INPUTS.json). Raw logs are private inputs,
not copied into the repository. The author reports enemy spawning and resting during combat; exact manual
interventions are not fully timestamped, so HP/resource/encounter changes alone cannot prove an AI reset bug.
Logged RandomReinforcements spawns are not all attributed to ToyBox.

There are nine combat captures, 465 action/wait summaries (246 TB, 219 RT, including eight verbose waits),
and 27 separate no-option diagnostics. The AI log contains no AI exception. This is a broad review, not
just validation of the newly added classes.

## Fixed here: campaign flag blocks major-form planning

AI 414 recognises Griffon Shape. AI 428 explicitly says `FlagUnlocked not evaluated - both branches counted`.
The actual exported Griffon ability chooses distinct form buffs with that condition. The new form model
correctly refuses to guess a unique form, but its safe-condition list omitted this native read-only predicate.
No Griffon Shape cast or Fury selection appears; the minor Griffon aspect is started eight times across
fights and restored afterwards. Wenduag is a level 8 Shifter in the combat header, so this is actual class
coverage, not the older Ulbrig/v0.3.27 hearsay report.

`FlagUnlocked.CheckCondition` reads the flag's IsUnlocked and optional Value; the supplied native manager
uses Dictionary.ContainsKey/TryGetValue. It does not unlock, set, reset or trigger a quest action. Add only
the exact inspected condition type to the existing allowlist. Native Condition.Check retains Not and
specified-value whitelist/blacklist semantics. Current per-frame caster/target/ability checks and context
handling remain; no flag value is assumed or retained across frames. Unknown mod subclasses stay excluded.
This also makes other occurrences of this same safe native predicate evaluable.

Form appraisals now report one bounded row per ability/unit/fight, including zero values and the reason.
The existing fight-note ledger caps rows; no extra rule, context, actor, target/path scan or shape quote is
performed by that diagnostic. A known but unprofitable form remains a legitimate decision. Existing forms,
player/pending Fury selections, Pure Mage/caster limits and automatic OOC exclusion remain.

21 new offline checks cover actual base/9/14 Griffon guard paths, exact-type admission/subclass rejection,
native flag-store unlocked/locked/zero-value reads without mutation, and compiled native/diagnostic routes.
They do not instantiate Game/Player, evaluate a live campaign, initialize an actor or cast a form. All
inherited 48 Shifter graph/scalar checks remain; native scene use still needs replay.

## Other findings and follow-ups

| Evidence | Diagnosis / action |
| --- | --- |
| AI 2959: Scorching Ray, two samples, 33.5 dealt versus 15 estimated | The ranged Spellstrike volley shares one arrow roll. DamageObserver merges identical-roll packets while its projectile comparison expects one ray. This is a separate sampling correction; do not multiply real damage estimates from this diagnostic. |
| AI 1359–1365, 1471–1475: Bandit ends turns after three selected routes fail, with high raw attack values remaining | The in-place/step assumption can suppress advance generation before selected path validation rejects those options. Raw positive scores do not prove a reachable attack or safe move. The author explicitly authorized resolving this fallback gap during this task; implementation is a separate focused change. |
| AI 433: 2008 ms, weapons/modes 1937.6 ms, weapons heap −234584 KB; Griffon Shape 4.3 ms | Collection overlaps the slow stage. This is not evidence that form scoring itself took two seconds. |
| AI 2566 and 3905: 1301/1256 ms, heap drops of −342368/−301880 KB | Same collection overlap. No GC.Collect/forced-unload call exists in this mod's source; the marker records GC.CollectionCount changing, not a claim that AI initiated collection. |
| 45 slow-decision rows, median 314 ms among rows over 250 ms; 3 GC-marked | This is a thresholded sample, not the median of all decisions or a matched before/after benchmark. Warm ability stages still reach about 15 MB live-heap growth, with repeated Fusion/AoE recipient calculations. Broad caching or allocation changes need state-equivalence evidence; no unsafe cross-target/context memo is added here. |
| AI 2706: arcan concentration 16 versus DC 19 | Native failed cast is identified and excluded from silent-cast counting. |
| AI 3981: one fused spell has no observed effect on aimed Minotaur (1/2) | One observation amid mode/intervention changes does not establish a broken fusion or justify a blanket blacklist. Other fusion casts and actual damage/control are recorded. |
| AI 1042: Alchemist's Fire, three hits at mean 7 versus estimate 9.5 | Small unmatched packet sample; leave estimates unchanged pending stronger evidence. |
| AI 4044: WrathPatches ContextRankConfig owner warning on ChannelPositiveHarm/Isekai | External blueprint-component ownership warning, not an AI exception. No evidence that the read-only AI changed its owner. No other mod is patched. |
| 14 Quickspider Swarm `Has no weapon in hand` verbose rows | The native ordinary-attack inspector cannot enumerate that body's weapon. Existing swarm-space/round-strike analysis is separate; this warning alone is not proof that swarm danger was zero. |

The source log has 484 main Tactics rows; some are diagnostics rather than decisions. Parse RTn as well as
Rn, and do not count repeated verbose option text as extra actions. No actual Light cast is selected. There
is no recorded Faerie Fire/Profane Invocation use that would establish new acceptance evidence for those
features. Drow Magus does exercise native Ranged Spell Combat and follow-ups; ordinary ranged attacks remain.
Healing/potions are used during the later RT damage, including native move-action item opportunities.
Native interruptions, concentration failure, crowded routes and the author's interventions are distinguished
from AI exceptions; high raw candidate values do not bypass path, action, safety or retry checks.

## Authorized movement follow-up contract

The observed Bandit R2/R3 case has `SMWF5` available. FullAttackApproach intentionally does not mark a
normal Move action as spent, but AttacksFromHere only tests NeedsMove. AddMovementCandidates can therefore
assume an attack is already possible and omit ordinary advance candidates. Selected preflight then rejects
the step/approach endpoints (1.6–1.8 m short in the quoted turn); its three-check budget withholds other
unproven approaches. END TURN follows. Those guards must remain.

The approved repair is a **lazy fallback**, only when ordinary selection has no useful action after those
rejections: offer an existing-policy closing move if no actual in-place action is usable. Reuse cached
weapon quotes and existing advance safety/valuation/native dispatch, keep the selected attack path-check
limit at three, and perform no all-target full-path search or second sweep of all tactical actions. A
native move may still be refused when no safe path exists. Do not change retreat, target commitment,
global pathfinding or stuck cancellation. Tests must cover a valid step winning, an ordinary in-place
attack, no movement budget, a held five-foot-step, hazards/unreachable targets, and the three-failure case
in both TB and shared RT. The exact integration point will be reviewed against the current source before
that separate implementation.

## Replay

Repeat Wenduag/Ulbrig in human form with the native campaign state unchanged. `[Profile] ... FlagUnlocked
not evaluated` should disappear; the bounded `[Shifter] ... form appraisal` should show the actual branch's
core attack/AC comparison or a precise hold reason. A useful form may cast during combat, then Fury can
compete on its native generated choices. Do not force forms over stronger actions. End combat, safe-zone
suppression/return, manual/pre-existing forms and near-empty resources retain the original guards.

Test both TB/shared RT and retain the new startup MVID. Compare ordinary encounters separately from
spawn/rest stress cases; include the matching combat log. Actual scene execution and matched Unity timing
of this correction remain unplayed. Original raw-corpus enum loading remains separately incomplete;
compatible/supplementary audit scopes are recorded distinctly in the receipt.
