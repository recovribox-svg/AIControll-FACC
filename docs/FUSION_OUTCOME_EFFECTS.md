# Fusion F4: effects on a passed save or on both outcomes (slices A and B)

16 November 2026, Claude. Author-approved ("I agree with fusion first. Begin."). Branch `claude/fusion-outcome-effects`,
based on development `980cf26`. **Not played.** Manifest stays 0.3.52; no `main`, tag or release change.

Plan: [FUSION_OUTCOME_EFFECTS_PLAN.md](FUSION_OUTCOME_EFFECTS_PLAN.md). Checks:
[FUSION_F4_CHECKS.json](competence/FUSION_F4_CHECKS.json). Package:
[outcome1](../test-builds/AI-Control-FACC-v0.3.52-dev-outcome1-f5ccb40-UMM-test.zip), startup MVID
`fbc220a5-e4af-42eb-b386-e72ec1b61247`.

## What changed for the player

A Magic Deceiver's fused spell is now offered when one of its two spells does something on a **passed** save or
**whatever** the save result. Before, every such spell was refused with "buff outside the shared failed-save branch;
unsupported forecast, cast withheld".

| Source spell | What the game applies | Admitted now |
| --- | --- | --- |
| Fear | Frightened on a failed save, shaken for one round on a passed one | yes |
| Boneshatter | Exhausted on a failed save, fatigued on a passed one | yes |
| Frigid Touch | Staggered for one round on either outcome | yes |
| Ray of Exhaustion | Exhausted outright if already fatigued; else exhausted or fatigued by the save | yes (slice B) |
| Molten Orb, Disintegrate, Acid Arrow, Reduce Person, Enervation | Effects the value model cannot price yet | no, still withheld |

- 905 more of the 15,494 fused configurations are admitted: 8,211 instead of 7,306.
- No configuration lost admission, and no already-admitted fusion changed its value.

## What the gaps3 log showed first

The author's log (module `48ece05d`, the gaps3 package; 3 fights, 354 commands issued and 354 ended) lists arcan's ten
fusions.

- Seven are priced by the joint outcome value (`[FusionValue]` rows).
- One, Explosive Runes/Excruciating Deformation, uses the older forecast.
- Two are withheld, both for **"shared Hit Dice selection"**: Snowball/Sleep and Explosive Runes/Sleep.

**None of his present fusions is blocked by the rule this work removes.** This work gives him:

- saved Frigid Touch fusions. He has Explosive Runes/Frigid Touch in the no-save form; a form with a root save, for
  example Incendiary Runes/Frigid Touch with Reflex, was refused and is now offered.
- Fear, Boneshatter and Ray of Exhaustion fusions once he learns those spells.

His known spells include Molten Orb, Acid Arrow and Reduce Person, which the later slices C and E cover. His two Sleep
fusions need the shared Hit Dice slice (F7 in `MAGIC_FUSION_PLAN.md`), which is a separate mechanism.

## The rule in the game

Read from the supplied assembly (the plan's section 5): a fused cast rolls **one** root save, then runs both spells'
actions under it. A buff inside the passed branch applies on a pass, one inside the failed branch on a failure, and one
outside any saved branch on both. Nothing else is rolled.

## Implementation

- **`AbilityProfile.Fusion.cs`.** The refusal became a recorded fact, `FusionOutcomeEffectLocation`: the first target
  buff application outside the failed branch. The path of every application (Failed, Passed, Both) was already
  recorded.
- **`TacticalEngine.FusionWithheld`.** One admission rule, used by `EvaluateAbility` and by the coverage audit. A
  fusion with such an effect is offered only when the joint outcome value qualifies. Otherwise it is withheld with
  `[Fusion] <name>: effect on a passed or either save outcome; joint value not qualified (<reason>); cast withheld`.
  It never uses the older forecast, which multiplies every control by the fail chance.
- **`TryFusionControlRounds`.** It returns two envelopes. An application on the failed branch enters the failed one, on
  the passed branch the passed one, outside any branch both. Share, duration, immunity and already-present tests run
  once per application, as before.
- **`ValueModel.FusionValue`.** Adds the passed outcome:
  `delivery × control worth × (1 − fail) × (1 − killed on a pass) × rounds on a pass`. It is added only when present, so
  other fusions keep their value to the last bit.
- **Allies.** `FusionControlCatches` counts an ally as caught when either outcome leaves an effect.
- **A recipient the joint value cannot price** (unknown guard or duration) gets its damage and no control credit.
- **Slice B, `StableFusionGuards`.** An earlier application in the opposite branch of a `Conditional` that the same
  source spell read once is not a dependency: it cannot have run. Ray of Exhaustion's "already fatigued" branch and its
  saved branch are such a pair. An application by the other source spell still refuses, and so does the same spell
  fused with itself.
- **Log.** The first `[FusionValue]` row of such a fusion reads
  `control envelope X round-equivalents on a failed save, Y on a passed save`.

Not changed: one native cast and one slot; Conservative, Aggressive and Pure Mage policies; the blocklist (read before
admission; checked by the audit); cone re-aim and the friendly-fire veto; ordinary spells; movement.

## Bounds and performance

- No new rule trigger, context, save, path, world or target query. One more scalar envelope per decision context.
- Cache keys and invalidation are unchanged.
- A newly admitted fusion is scored where it was skipped before. That is the feature; nothing is pruned to pay for it.
- Baseline from the gaps3 log: arcan has 8 slow-decision rows, 286 to 539 ms, mean 368 ms, nearly all in the abilities
  stage. With the same ten fusions the new build should show the same figures: their code path differs only by one
  null test per application.
- Offline: 10,000 warm calls of the two-outcome value allocate 0 bytes. This is not Unity timing.

## Checks

On checked source `f5ccb40` (DLL SHA-256 `0728e3aa…`), real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 881 pass (15 new) |
| Inventory, supplement and ledger script tests | 27 + 3 + 18 pass |
| Added game references | 0 |
| ProfileAudit | exit 0; two runs identical; `FUSION-OUTCOME` 34 checks; every other line equal to the integrated baseline |
| Fusion coverage | admitted 7,306 → 8,211; gate-qualified 3,118 → 4,023; 0 lost; 0 reader errors |
| Still withheld by the outcome gate | 1,969, by reason in the receipt |
| Native harnesses | 98 / 14 / 10 |

The plan predicted 638 for slice A. The what-if run counted configurations by their first remaining refusal; with slice
B, 267 Ray of Exhaustion configurations qualify as well, which gives 905.

## Replay (turn-based and real time)

Record the startup MVID `fbc220a5` and keep both logs.

1. In the Magic Fusion window, build a Frigid Touch fusion **with a root save**: Incendiary Runes/Frigid Touch, save
   Reflex. Keep Explosive Runes/Frigid Touch as it is.
2. At the first fight the log should show one `[Fusion] … shared save Reflex …` row and one `[FusionValue] … on a failed
   save, … on a passed save` row for it, and no "cast withheld" row.
3. When it is cast, the combat log should show Staggered on the target whether the save passed or failed.
4. A target that passes the save and is staggered must not produce a "no effect" line for that spell.
5. Put an ally inside the burst with Avoid Friendly Fire on: expect another aim or no cast.
6. Block the new fusion in "Abilities AI must not use": it must not be cast.
7. Repeat with four to eight driven summons in melee with the targets, for caught allies and decision time.
8. Regression: the seven existing fusions keep their `[FusionValue]` numbers; the two Sleep fusions stay withheld for
   "shared Hit Dice selection"; arcan's slow-decision rows stay near the baseline above.
9. If Fear, Boneshatter or Ray of Exhaustion are available (a scroll learned, or a respec), fuse one with a damage
   spell: expect Shaken or Fatigued on a passed save.

## Limits

- Not played. Offline checks run no actor, roll, cast or Unity timing.
- Frigid Touch's longer stagger on a critical hit stays unpriced; allies keep the longer duration.
- 1,969 configurations stay withheld by the outcome gate: Molten Orb's main-target guard (512), Disintegrate's
  hit-point test (418), helpful or periodic carriers such as Reduce Person and Acid Arrow (672), Enervation's drain
  (280) and smaller groups. Each is a later slice.
- The coverage numbers are native UI templates, not any character's spells.
- A source spell another mod has patched with an unread guard fails the gate and stays withheld.
