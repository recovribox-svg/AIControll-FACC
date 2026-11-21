# Fusion coverage C1: Molten Orb's main target and damage over time

17 November 2026, Claude. This is the first slice of the [coverage programme](FUSION_COVERAGE_PROGRAMME.md). The author
asked for it after the plan. It covers both modes and changes valuation only.

- Branch `claude/fusion-coverage-programme` starts at development `0019856`, which is after PRs #81–#91.
- **Not played.** UMM stays 0.3.60. Nothing is merged.
- Checks: [FUSION_C1_CHECKS.json](competence/FUSION_C1_CHECKS.json).
- Compiler and references: [FUSION_C1_BUILD_PROVENANCE.json](competence/FUSION_C1_BUILD_PROVENANCE.json).
- Package: [fusiondot1](../test-builds/AI-Control-FACC-v0.3.60-dev-fusiondot1-83ac4c1-UMM-test.zip), startup MVID
  `add36407-e766-45d7-a720-fa140c7a1f53`. It contains everything on development (#81–#91) plus this slice.

## The problems

1. **Molten Orb was never cast fused.** It deals 2d6 fire to its main target and 1d6 to the others, with a Reflex save
   for half. The two branches are told apart by "is the main target". That guard was not on the list of stable fusion
   guards, so all 616 Molten Orb configurations were withheld.
2. **Damage over time was worth nothing.** A buff that only deals damage each round was admitted but given no value
   ("omitted DOT benefit"):
   - Molten Orb's burning: 1d6 fire a round for 1d3 rounds.
   - Incendiary Runes' burning: 1d6 fire a round, ended by a Reflex save each round. This is on arcan's bar.
   - Acid Arrow's acid: 2d4 a round, for one round plus one per three caster levels.

   Only one narrow layout, burning with a save, was recognised at all. Acid Arrow's buff, which uses
   `AddFactContextActions`, was treated as an unknown support buff, so 186 Acid Arrow configurations were withheld.

## The game's rules (supplied assembly)

| Rule | Native source |
| --- | --- |
| "Is the main target" compares the cast's main target with the recipient. | `ContextConditionIsMainTarget.CheckCondition` |
| The main target is the clicked target, fixed for the cast. | `AbilityExecutionContext.MainTarget => ClickedTarget` |
| Targets compare by unit, else by point and facing. A ground aim matches no unit unless the unit stands exactly on that point with facing 0. | `TargetWrapper.Equals` |
| A buff's context roots in the cast that applied it. A fused cast's spellbook dice cap therefore applies to the buff's ticks, as do the cast's DC and metamagic. | `MechanicsContext.SourceAbilityContext => Root`, `ContextActionDealDamage.GetDamageInfo` |
| A context's saving throw is the current save scope. A round tick runs outside any scope, so a save in a tick is its own roll against the context's DC, including when it asks for the DC "from the context's save". | `MechanicsContext.SavingThrow`, `ContextActionSavingThrow.RunAction` |
| The first tick comes one interval after the buff lands (turn-based: after the turn's start). The buff ticks before it is removed at an equal end time. | `Buff.OnAttach`, `BlueprintBuff.TickTime` |
| `IgnoreUnitModifiers` only stops flat damage bonuses from being added. Resistance and immunity still apply. | `BaseDamage.AddModifier` |
| `AddBuffActions` is a kind of `AddFactContextActions`, the round-tick component. | type hierarchy |

## The change

1. **Main target.** `ContextConditionIsMainTarget` is a stable fusion guard. Its branches are evaluated per recipient
   against the scored aim, as they already are for ordinary bombs (`AbilityProfile.AimedAt`):
   - aimed at a unit, that unit takes the direct hit and the others the splash;
   - aimed at the ground (a delayed burst, slice A), every recipient takes the splash.
2. **Periodic damage carriers.** `CombatMath.IsPeriodicDamageCarrier` replaces the old narrow test. A buff qualifies when:
   - its whole effect is hit-point damage each round, with fixed dice, possibly ended by a save;
   - it has only round actions and spell descriptors;
   - it has no child buff, poison or activation.

   It is read once per blueprint.
3. **Priced on each save outcome.** `TacticalEngine.TryFusionPeriodicDamage` counts a carrier's expected ticks within the
   horizon (the fight's expected rounds, at most three). It counts them on the outcome that applies the carrier: failed,
   passed, or both. For each tick:
   - the damage uses the fused dice cap, the cast's metamagic, and the recipient's resistance and immunity;
   - a save in a tick ends the later ticks with its chance (`PeriodicWeight`).

   A carrier the recipient already has gets no credit, and neither do guards or immunity that exclude it. In
   `ValueModel.FusionValue` the later damage counts for HP and progress towards a kill, capped at the HP the immediate
   damage leaves. The chance to kill now, and the control a kill now cancels, stay with the immediate damage. Without
   damage over time the value is unchanged to the last bit (unit-tested).
4. **Allies.** The friendly-fire cost of an ally the cast catches adds its expected damage over time, weighted by its
   chance to fail the root save. The burst re-check (`BurstWatch`) does the same for an ally walking in. It also prices
   the ally as the ground-aimed cast has it: nobody is the main target.
5. **The periodic reader** (shared with damage already attached to a unit, C06R4) accepts two flags:
   - `IgnoreUnitModifiers`, because the estimate adds no flat bonuses;
   - `UseDCFromContextSavingThrow`, because a tick has no save scope.

   Both are exact for round ticks. A party member carrying such a buff from an enemy (a burning, for example) now has
   it read as attached damage.
6. **One duration rule.** Control and damage over time read an application's duration through one helper,
   `TryFusionApplicationRounds`, extracted unchanged from the control code.

**Cost.**
- Per fused cast and recipient: one pass over its applications, and nothing more when it has no carrier.
- Per carrier: one cached plan, one memoised duration, and a tick read with no context or rule.
- No new path, world or rule query.

## Coverage

On the same inputs as the #91 run:

| | Before | After |
| --- | --- | --- |
| Offered | 8,709 | **9,209** (500 gained, none lost) |
| Offered and valued by the joint model | 4,521 | **5,039** (518 gained, none lost) |
| Molten Orb (616 configurations) | 0 offered | **338** offered, all with the joint value |
| Acid Arrow (322) | 38 offered, 0 joint | **208** offered, **188** joint |

The gains came from the following reasons:
- 322 from the main-target guard;
- 146 from the carrier reason;
- 32 from the shared Hit Dice pool, whose joint value now qualifies.

The Molten Orb and Acid Arrow configurations that are still withheld have the other spell's reason, for example Reduce
Person's form or a nested save.

arcan's bar is unchanged in what is offered. Incendiary Runes / Frigid Touch now prices its burning. Explosive Runes /
Hideous Laughter stays withheld: that is slice C2.

## Checks

On checked source `83ac4c1`, real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 1,052 pass (8 new) |
| Inventory, ledger and supplement script tests | 27 + 21 + 3 pass |
| ProfileAudit | exit 0; new `FUSION-DOT` 39 checks, 0 failures; `FUSION-OUTCOME` 33 (its Molten Orb "stays withheld" check moved to FUSION-DOT); every other line identical to the #91 run; summon detail byte-identical |
| Native harnesses | 98 / 14 / 10 |
| Signatures | 1 added (`BlueprintBuff.TickTime`, present in the supplied assembly); 1 removed (the `IgnoreUnitModifiers` field read) |
| Supplementary Magus corpus | not run here (inputs not in this environment); no Magus code is touched |

`FUSION-DOT` covers:
- the native contracts in the table above;
- the actual Molten Orb, Incendiary Runes and Acid Arrow buffs read as carriers;
- synthetic non-carriers (stagger, child buff, rank dice);
- the production tick reader on Incendiary Runes' burning;
- six actual fusions offered with the joint value, arcan's Incendiary Runes / Frigid Touch among them;
- the compiled routes (joint value, duration rule, tick read, friendly-fire cost, burst re-check).

## Replay (both modes)

Record the startup MVID `add36407`. Keep `Log.txt` and `combatLog.txt`.

1. **Molten Orb fused.** Give arcan a Molten Orb fusion, for example Molten Orb / Color Spray, or Fireball / Molten Orb.
   - It should be cast.
   - Its `[FusionValue]` line shows "damage over time fail/pass".
   - The main target takes the direct hit when aimed at a unit.
2. **Acid Arrow fused**, for example Fireball / Acid Arrow or Explosive Runes / Acid Arrow. It should be cast, with the
   acid's ticks in its value line.
3. **Incendiary Runes / Frigid Touch** (on the bar). Same behaviour as before. Its value line now shows the burning's
   damage over time.
4. **Friendly fire.** With Avoid Friendly Fire off, a fused burning area that would catch an ally is priced with the
   ally's burning. The label shows the cost ("catches X -N").
5. **Regression.** The other fusions on the bar behave as before.

## Limits

- **Not played.** Offline checks create no unit, buff, cast or tick.
- **No refresh credit.** A carrier the recipient already has earns nothing, even though the native stacking (Replace)
  would restart it.
- **Ground aim and the direct hit.** A ground-aimed Molten Orb fusion gives up the direct hit, and it is not compared
  with aiming at the unit. Slice A's ground aim decides first.
- **Ordinary spells.** The ticks of ordinary (unfused) spells are still unpriced. That is the cross-cutting slice of the
  programme.
- **Fixed dice only.** A carrier whose dice depend on the cast's ranks stays on the older rule.
