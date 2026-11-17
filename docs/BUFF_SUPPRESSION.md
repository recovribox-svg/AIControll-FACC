# Suppressed buffs (slice C)

17 November 2026, Claude. Slice C of [BURST_AIM_AND_TACTICS_PLAN.md](BURST_AIM_AND_TACTICS_PLAN.md) (Feature 1 of the
author's feature document, rewritten). The author asked for slice C before slice D. This slice changes valuation only,
in both modes. Branch `claude/buff-suppression`, stacked on slice B (`claude/cast-disruption`) and PR #81
(`claude/burst-ground-aim`). **Not played.** Manifest stays 0.3.60; no `main`, tag or release change.

Checks: [BUFF_SUPPRESSION_CHECKS.json](competence/BUFF_SUPPRESSION_CHECKS.json). Package:
[suppress1](../test-builds/AI-Control-FACC-v0.3.60-dev-suppress1-3e70345-UMM-test.zip), startup MVID
`00a9466f-e43d-49c8-baef-d00f502399e0`. It contains slices A and B too.

## What changed for the player

Some effects switch other buffs off while they last:
- Polymorph forms (Wild Shape, Shifter forms, Beast Shape, Elemental Body, dragon forms) switch off Enlarge Person,
  Reduce Person and similar buffs. Other forms also switch off Haste, Mirror Image, Displacement or Mage Armor.
- Ameliorating hexes switch off sickened, fatigued, shaken, dazzled, poison, curses or blindness, depending on the hex.

A switched-off buff does nothing until the effect that switches it off ends. The game then switches the buff back on
by itself.

The AI now knows this:
- **C1.** A buff that the recipient would switch off as it arrives is worth nothing. The AI does not cast it on that
  recipient. Instead it buffs another ally, or does something else. This covers spells and abilities, the grants of
  toggles and auras, and the upkeep of buffs out of combat. The log says why, once per fight and recipient:
  `[Buff] Camellia: Enlarge Person on Wenduag withheld: EnlargePersonBuff would be suppressed by Beast Shape I`.
- **C2.** A harmful buff that is switched off for the rest of its time is not worth curing. A dispel counts only the
  rounds such a buff would act after its suppressor ends.

## The game's rules

Read from the supplied assembly:

| Rule | Native source |
| --- | --- |
| A unit's suppressions are four sets: buff blueprints, descriptor flags, schools, and exclusions | `UnitPartBuffSuppress` |
| A buff is suppressed unless excluded, when its blueprint is listed, one of its context's descriptor flags is, or its context's school is | `UnitPartBuffSuppress.IsSuppressed` |
| Every buff is tested as it is attached, so recasting a suppressed buff does not help | `Buff.OnAttach` |
| A suppressed buff is not enabled | `Buff.IsEnabled` |
| When the suppressing fact ends, its buffs are released and switched back on | `SuppressBuffs.OnDeactivate`, `ApplyChanges` |
| A buff context's descriptor is the parent's plus the buff's own component; its school is its own, else the parent's, else its `AddSpellSchool` | `MechanicsContext` constructor |
| An ability's context takes the ability's school (a Magic Fusion's own) | `AbilityExecutionContext` |

Haste and Slow do not suppress each other: they remove each other, which the engine already handles.

## Implementation

| Part | Where |
| --- | --- |
| Pure rules: the native order, outlasting, active rounds | `Tactics/SuppressionRules.cs` (unit-tested) |
| Game side: the part's four sets read through cached field references; the buff context's descriptor and school | `Tactics/BuffSuppression.cs` |
| C1, spells and abilities | `TacticalEngine.ScoreAbility`, buff recipients |
| C1, toggles and auras | `TacticalEngine.ToggleValue`, the grants that reach each ally |
| C1, out-of-combat upkeep | `RealTimeController.IsWorthKeepingUp` |
| C2, cleanses | `AbilityProfile.WouldRemoveSomething` |
| C2, dispel fallback | `TacticalEngine.HasSomethingToDispel` |
| C2, dispel worth (Unraveling and other dispels) | `TacticalEngine.DispelValue` |

**Details.**
- C1 drops the suppressed buffs from a recipient's list. A recipient left with none gives the cast no value, so a plain
  buff does not fall through to the fallback for unread effects.
- C2, cleanses: a suppressed harmful buff counts only if it would outlast its longest suppressor. A permanent suppressor
  (a feature) means never. Follow-up F3 ([CLEANSE_HORIZON.md](CLEANSE_HORIZON.md)) values it by the rounds it acts within
  the horizon instead.
- C2, dispels: the worth counts `min(horizon, rounds left) − min(horizon, rounds suppressed)`.
- A suppressed buff whose suppressor cannot be found keeps its old value.
- The Shifter form appraisal is unchanged. It already counts the buffs a form switches off (`ShifterFormProjection`).

**Bounds.**
- A recipient without the part (nearly all) costs one part lookup.
- A recipient with it costs three set lookups per buff.
- Suppressors are scanned only for the log line and for C2's time left, on a buff the game already marks suppressed.
- No buff, context, actor or command is created.
- If a game update renames the fields, the rule turns itself off and logs that once.

## Checks

On checked source `3e70345`, real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 982 pass (17 new in `SuppressionRulesTests`) |
| Inventory, ledger and supplement script tests | 27 + 18 + 3 pass |
| ProfileAudit | exit 0; `BUFF-SUPPRESS` 25 checks, 0 failures; every other line equal to slice B's run; Fusion coverage and summon detail identical |
| Native harnesses | 98 / 14 / 10 |
| Added game references (relative to slice B) | 10, all exercised on the real assembly; none removed |

`BUFF-SUPPRESS` fills a real `UnitPartBuffSuppress` from each of the 337 exported suppressors, using the game's own
public `PrepareForSuppression` calls, and runs the mod's reader against it:
- all 695 named buffs read as suppressed;
- both exclusions win over every flag;
- all 19 descriptor suppressors catch a buff through its own descriptor or through the ability's;
- no unrelated buff reads as suppressed.

Named cases: Beast Shape I suppresses Enlarge Person and leaves Haste alone, and the major Ameliorating hex suppresses a
poison.

## Replay

Record the startup MVID `00a9466f` and keep `Log.txt`.

1. Put a companion in Wild Shape or a Shifter form (Wenduag), then fight with a caster who knows Enlarge Person (or
   Haste, against a form that suppresses it). Expect the caster to skip that companion, with a
   `[Buff] ... withheld: ... would be suppressed by ...` line, and to buff others.
2. Turn-based and real-time both.
3. Out of combat, with buff upkeep on, a formed companion should not receive the suppressed buff again and again.
4. A companion under an Ameliorating hex that covers its condition: expect no cleanse spent on that condition, unless
   it lasts longer than the hex.
5. Regression: without forms or hexes, buffing is unchanged.

## Limits

- **Not played.** Offline checks create no unit, buff instance or context.
- The log line and C2's time left read live facts; they are not exercised offline.
- A suppressor's school or descriptor added at run time by another mod is read from the live part for C1, but the log
  may then name "a suppressing effect".
