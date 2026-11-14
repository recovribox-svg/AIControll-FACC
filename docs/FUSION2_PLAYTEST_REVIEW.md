# Fusion1 playtest review and Fusion2 valuation

14 November 2026. Task `codex/fusion1-playtest-review`, based on integrated development `ab1b6a7` (including
reviewed F0/F1, I1 and compiler provenance). [Input hashes and row references](competence/FUSION2_PLAYTEST_INPUTS.json).
Both AI logs identify fusion1 MVID `2e2896c3-5f13-42e1-bb9b-9626f6157228`, source `7a56bbb`. The author confirms
ToyBox spawns, rests **between** encounters and manual Grabber activation with AI Control enabled for Ulbrig.
Log 1 covers five encounters; Log 2 covers three. Combat-log fights 1–5 / 6–8 align with those groups by party,
enemy composition and named casts. This is not an exact timestamp alignment of every real-time event.

## Evidence and scope

- The mode summaries measure 258 turn-based and 1,475 real-time decisions. There are 74 slow-decision rows; warm
  ability appraisal still costs time, and signed heap deltas are not allocation measurements or a matched benchmark.
- Log 1 has 400 issued commands and 400 terminal rows. Log 2 has 303 issues and 302 terminals; the unmatched issue
  at row 2629 is out-of-combat `ApplyPermanentBuffsOutOfCombat` immediately before the uploaded file ends.
- No AI Control exception is observed. The 14 OwnerBlueprint errors are emitted by **WrathPatches**, including
  Incendiary/Explosive Runes rank-component warnings; these retain their external provenance.
- Created fusions use Runes area delivery or Snowball delivery. No selected-cone **Fusion** cast is established.
  Ordinary Color Spray finds a turned aim past Orvath (Log 2, rows 1209–1210); no native cone-query warning appears.
  The F1 selected-cone path still needs its focused gameplay checkpoint.
- The source list in Log 2 has ten named created configurations. Two Sleep combinations are explicitly withheld
  for shared-HD selection; eight are read. These are character/configuration observations, not all-combination coverage.

## Confirmed Fusion valuation defects

**A surviving effect borrowed another effect's severity or duration.** The old control block selected applicable
buffs, then used the whole profile's strongest control and `EffectRoundsOn` across applications. It also skipped
the recipient's entire control contribution if *any* applicable buff was already present.

The supplied native graphs give an especially concrete example: Incendiary Runes applies `BurningBuff` for one
minute, while Snowball staggers for one round. Burning has an old `AddBuffActions` component that deals periodic
HP damage and may remove itself after a save; it does not deny actions. The old forecast could lend its ten-round
duration to the stagger (capped to the engine's three-round horizon). Runes/Color Spray similarly lets the longer
carrier inflate a high-HD recipient's one-round stun. The logs establish these configured spells and their use
(Log 2 rows 105–118, 122–124 and 960–962); they do not isolate each term in the displayed 333.6/404.7 totals.

**Root-save outcomes were averaged before nonlinear utility.** `EstimateAbilityDamage` already weights each
native action by the chosen shared save. Passing that mean to HP/kill utility can make a rare lethal outcome look
like a likely kill, and adding full control separately credits future actions a dead recipient cannot take.
This is a confirmed code defect; the logs do not provide a numerical counterfactual for every cast.

**A saved-result branch needs a saving throw.** Native `ContextActionConditionalSaved.RunAction` logs an error and
returns without either branch when `MechanicsContext.SavingThrow` is absent. Treating that state as a failed save
in a Fusion forecast invents effects. The reader now withholds a saved-result configuration with no declared root
save; a payload creating its own independent save retains the existing independent-save refusal instead.
This is a native negative contract, not evidence that the player created an invalid no-save configuration.

## Implementation

The existing reader now retains each target buff's native apply action, source/read order, guards, direct conditions
and own duration. The Fusion-only control path filters active copies, native buff/condition immunity and actual
recipient branches individually. It uses a bounded envelope of the strongest surviving denial over time: one round
of hard control plus three rounds of soft control is 1.8 round-equivalents, not three hard rounds or two full effects
added together. Ordinary control policy and its shared value scale remain.

Burning's exact inspected damage/remove-self callback shape is identified structurally. It gets **no generic
control value**. Its periodic HP benefit is omitted in this slice; ally-harm checks still treat the carrier as
potentially harmful. Different/extended carriers retain the existing forecast rather than inheriting this exception.

The direct HP estimator collects conditional failed/passed-save amounts from the same already-mitigated action
parts. There is no second damage estimate, action execution or synthetic source touch roll. Qualified outcomes
reach shared pure value math before HP caps and kill utility. Delivery/SR/attack chance and the one root save are
each paid once. Failed-save control gets no double reward for the probability that the same outcome downs the enemy.
The existing regeneration discount, damage calibration, role weights and wake caution remain.

Qualification is deliberately narrower than profile admission. Unknown guards/durations, unsupported composite
damage, shadow copies, source-state dependencies and damage/sleep interactions keep the existing forecast with
a bounded diagnostic. Configurations still refused by F0 remain refused. Active copies receive no speculative
stacking or refresh credit. Helpful/caster-side expansion, nested saves, shared-HD pools and secondary areas remain
later slices; this does not declare F2/F3's entire planned scope complete.

Metadata qualification and condition references are cached per configuration; dependency checks compare native
GUIDs without loading unrelated facts. Direct condition arrays are compiled once. Scalar duration reads reuse the
existing synchronous ability scope and invalidation version, keyed by actual cast, recipient and native application.
No new actor/context cache survives that scope. The control scratch holds at most 32 scalar intervals, with no new
enemy/world/path search. Native-input checks measure the warm scratch's allocations, not Unity decision performance.

The first qualified recipient per configuration prints `[FusionValue]` with shared-save and conditional-damage
figures. A separate first-only fallback row names retained legacy valuation. No per-recipient stream is added.

## Other findings and unresolved gaps

**Grapples.** The combat log contains 11 successful Ulbrig grapple maneuver checks plus one failure, and 11 successful
Wenduag checks. These include maintenance rolls; they are not counts of independent newly started grapples. Six
victim break-free attempts also appear. Native code maintains the pair, pins after successful maintenance and queues
a free natural damage-only attack. The initiator loses movement, standard actions and attacks of opportunity;
the pinned victim loses standard actions. Ending a turn while holding an enemy can therefore be useful.

The native restrictions are consistent with Wenduag's real-time state at Log 2 row 2059 (`CantMove` /
`CantUseStandardActions`); a swift Fury action remains available. The logs do not identify native part ownership on
every decision, and the turn-based Ulbrig decisions do not establish a maintained grapple still present at his next
decision. Do not infer that every idle/no-standard turn is broken or that all holds should be released on a timer.
Release Grapple is still unread (Log 1 rows 530/544). **Automatic initiation and comparative maintenance/release are
not implemented here.** G1 remains the next separate change: read reciprocal native pair/pin state, distinguish
initiator/victim and other concurrent restrictions, and issue the actual free Release ability only for a positive
reason. Its forecast must account for the lost automatic natural attack and enemy pin, and must not assume removing
one grapple restriction restores a standard action already spent or forbidden by another condition. Do not roll a
native maneuver, temporarily release a pair or move either actor to preview it. Preserve player toggle ownership.

**Invocation.** The existing ledger reads 17 issues / 16 acted in Log 1 and none in Log 2: twelve settled transfers,
one override, three unseen-holder placements and one unacted issue. Its low-loss/sibling flags require the full
holder losses; they do not establish redundant casts or justify a new cap. See the existing I1 investigation.

**Approaches/swarms.** There are 18 path-end refusals in Log 1 and one in Log 2. The new end-point witnesses include
sight and vertical reach cases. Free-side approaches and swarm/area exits are exercised, but this is not a matched
crowding benchmark and does not justify widening the search or changing commitment. Enemy counts can rise after
ToyBox spawns. Movement, pathfinding, retreat and stuck cancellation are untouched by this slice.

**Recognition.** Sleep/shared-HD fusions remain explicit refusals. Eight read configurations do not prove every
source pair works. Pure support/caster benefits, periodic damage, custom secondary effects, refresh/stacking and
general post-effect state projection remain gaps. The full configuration audit reports metadata qualification
separately from admission and still labels live aim/value/execution unassessed.

## Checks and next playtest

Exact source, toolchain, test results and DLL/package identity are in the accompanying Fusion2 receipt. The package
is a development test of new valuation; the manifest stays 0.3.52. Compilation/offline fixtures do not establish
gameplay acceptance or a Unity speedup. The original fusion1 ZIP and historical receipts remain intact.

In both modes, keep the startup MVID and supply the AI log plus matching combat log:

1. Runes/Snowball and Runes/Color Spray against mixed-HD enemies: confirm useful casts, `[FusionValue]` rows and
   one-round stagger/high-HD stun behavior. Include a target already carrying one component, and a target immune to
   one control condition while another remains useful. Compare actual outcomes, not just candidate scores.
2. A low-HP target and a strong shared-save damage/control fusion: verify ordinary lethal cleanup still competes,
   alongside a target that often saves. Include an unknown/complex case and its named fallback/refusal.
3. Allies at the footprint edge, downed/moving allies and Avoid Friendly Fire both on/off. Periodic carriers must
   never be assumed harmless because their generic control credit was removed.
4. Create a supported **selected cone** fusion (for example Burning Hands + Snowball delivered as a cone), and test
   the F1 direct/turned/obstructed/Selective cases. Existing Runes/Snowball casts do not cover that route.
5. For G1, keep Grabber manually on and AI Control enabled. Capture a turn-based decision while the grapple actually
   remains held and the matching real-time case; current tests already establish native automatic maintenance.
6. Compare the same encounter, party and settings for warm median/tail decision times. Treat cold profiles, GC,
   native property reads and signed heap figures separately; do not infer a speedup from different encounters.
