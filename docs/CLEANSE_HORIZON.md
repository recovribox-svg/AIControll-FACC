# A suppressed debuff's cleanse, valued within the fight horizon (review follow-up F3)

17 November 2026, Claude. This is follow-up F3 of [REVIEW_FOLLOWUPS_PLAN.md](REVIEW_FOLLOWUPS_PLAN.md). It answers C-R1
in [REVIEW_81_84.md](REVIEW_81_84.md). Both modes, valuation only. The author asked for it on 17 November.

Branch `claude/cleanse-horizon` is stacked on the burst follow-ups (PR #88) → F2 (#87) → F1 (#86) → #85 → #84 → #83 → #82
→ #81. Nothing is merged. **Not played.** UMM stays 0.3.60.

Checks: [CLEANSE_F3_CHECKS.json](competence/CLEANSE_F3_CHECKS.json). Compiler and references:
[CLEANSE_F3_BUILD_PROVENANCE.json](competence/CLEANSE_F3_BUILD_PROVENANCE.json). Package:
[cleanse1](../test-builds/AI-Control-FACC-v0.3.60-dev-cleanse1-02163d7-UMM-test.zip), startup MVID
`2799ef2d-f330-4e9f-b654-af448b5bac4d`. It contains everything in the open stack, and it is the one to play.

## The problem

A harmful buff that is suppressed does nothing until its suppressor ends; the game turns it back on then
(`UnitPartBuffSuppress`). Examples are a curse under an Ameliorating hex, or a debuff under a polymorph.

Slice C counted such a buff for a cleanse whenever it would outlast its suppressor. The cleanse then earned the whole fight
horizon, `min(expected rounds, 3)`. Codex's example: 61 s left under 60 s of suppression passed, and earned three full
rounds in an 18 s horizon, although the buff acts for one second and only after the horizon.

The dispel worth (`DispelValue`) already counts only the rounds a buff acts after its suppressor ends.

## The change

`SuppressionRules.ActiveShare` (pure, unit-tested) gives the share of removing a harmful buff that falls within the
horizon:
- a buff that is not suppressed keeps all of it, so the cleanse's value is unchanged;
- a suppressed one counts its rounds left within the horizon after its suppressor ends (`ActiveRounds`), over its rounds
  left within the horizon;
- a permanent suppressor gives 0; an unknown time (no suppressor found) keeps all of it, as before.

How it is used:
- **Cleanse value.** `AbilityProfile.RemovedActiveShare` takes the best share among the harmful buffs the ability
  removes, using the native removal filters (`BuffsRemovedFrom`). An unsuppressed one gives 1. The ordinary cleanse value
  is multiplied by it. It replaces `WouldRemoveSomething`.
- **Label.** A partial cleanse is labelled `cleanse (N% after suppression)`.
- **Dispel fallback.** `HasSomethingToDispel` skips a buff suppressed for the rest of the horizon.
- **Unchanged.** Attached-hazard removal keeps its own value: `AttachedDamageOf` already skips suppressed buffs. The
  arrival rule (C1) is unchanged.

`BuffSuppression.StillMatters` and `SuppressionRules.StillMatters` are replaced by `ActiveShare`.

**Cost.** One more division per removable harmful buff, in the pass the cleanse already made. No new query, cache or
reference.

## Checks

On checked source `02163d7`, real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 1,032 pass (the eligibility tests are replaced by share tests, including Codex's example) |
| Inventory, ledger and supplement script tests | 27 + 18 + 3 pass |
| ProfileAudit | exit 0; `BUFF-SUPPRESS` 34 checks, 0 failures; every other line identical to the burst follow-up run; Fusion coverage and summon detail byte-identical |
| Native harnesses | 98 / 14 / 10 |
| Signatures | none added or removed |
| Supplementary Magus corpus | not run here (inputs not in this environment); no Magus code is touched |

## Replay (both modes)

Record the startup MVID `2799ef2d`.

1. **A cursed or poisoned ally under a suppressor.** An ally has a debuff with a few rounds left, and a Witch's
   Ameliorating hex (or a polymorph that suppresses it) with most of a minute left. Expect no cleanse of that ally while
   the suppressor outlasts the debuff or the next few rounds.
2. **Suppressor ending soon.** Let the suppressor have one round left while the debuff has several. The cleanse may be
   offered, labelled `cleanse (N% after suppression)` with N below 100.
3. **Regression.** An ordinary, unsuppressed debuff is cleansed as before, with the plain `cleanse` label.

## Limits

- **Not played.** Offline checks create no unit, buff or suppression.
- The arrival rule (C1) stays conservative: a buff that would be suppressed on arrival is still worth nothing, even if its
  suppressor ends soon.
- An action that removes the suppressor but not the debuff, so that the debuff resumes, is not priced.
- An unsuppressed debuff still earns the whole horizon even when it has less time left; that is the existing cleanse
  model, not changed here.
