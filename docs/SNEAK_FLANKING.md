# Sneak attack: fortification and flanking (slice D)

17 November 2026, Claude. Slice D of [BURST_AIM_AND_TACTICS_PLAN.md](BURST_AIM_AND_TACTICS_PLAN.md) (Feature 2 of the
author's feature document, rewritten). Valuation only, in both modes. Branch `claude/sneak-flanking`, stacked on slice C
(`claude/buff-suppression`), slice B (`claude/cast-disruption`) and PR #81 (`claude/burst-ground-aim`). **Not played.**
Manifest stays 0.3.60; no `main`, tag or release change.

Checks: [SNEAK_FLANKING_CHECKS.json](competence/SNEAK_FLANKING_CHECKS.json). Package:
[sneak1](../test-builds/AI-Control-FACC-v0.3.60-dev-sneak1-86f8e82-UMM-test.zip), startup MVID
`e432cb9a-6656-4b8c-9789-28d8c3a4e4dd`. It contains slices A, B and C too.

## What changed for the player

- **Fortification.** Some armour (light, medium and heavy fortification) and some creatures can shrug off a sneak attack
  or a critical hit. The AI now counts this:
  - against heavy fortification (75 %), a rogue's sneak dice and a weapon's critical damage are worth a quarter as much;
  - critical hits of rays and other attack-roll spells are counted the same way;
  - the same goes for enemies attacking party members who wear fortification, so their danger is not overestimated.
- **Flanking.**
  - In this game flanking does not depend on where units stand. An enemy is flanked when two or more units are engaging
    it in melee.
  - In real time, only engagers with a command slot targeting it count (`AnyCommandTargets` checks any non-null slot;
    it does not require an attack, cast or running command).
  - The AI now sees that a melee attack on an enemy one ally is already fighting makes that enemy flanked. Its own attack
    then gets the +2 flanking bonus and, for a rogue, the sneak dice.
  - The ally gets the same for a round. The AI counts that too, so a rogue and a fighter tend to end up on the same enemy.
- The decision shows `[flanks]`, or `[flanks with <ally> +N]` where N is the ally's extra damage, on attacks that make
  the target flanked.

## The game's rules

Read from the supplied assembly:

| Rule | Native source |
| --- | --- |
| A rolled attack that is a sneak attack, a confirmed critical or a precise strike makes one d100 roll against the target's highest fortification; at or under it, all three are lost | `RuleAttackRoll.OnTrigger`, `UnitPartFortification.Value` |
| Fortification facts add their value to that part | `AddFortification.OnTurnOn` |
| Auto-hit attacks (Coup de Grace) make no fortification roll | `RuleAttackRoll.OnTrigger`, auto-hit branch |
| A sneak attack needs the target flat-footed or flanked when the roll is made | `RuleAttackRoll.OnTrigger` |
| Turn-based, an enemy in combat is flanked when more than one unit engages it; otherwise (real time) when more than one engaging unit has a command aimed at it; never with `CannotBeFlanked` | `UnitCombatState.IsFlanked` |
| A unit engages every enemy within reach of its threat hand, with sight and without total concealment | `UnitCombatEngagementController.TickUnit`, `UnitHelper.GetThreatHand` / `GetThreatRange` |
| Melee attacks get +2 when the target is flanked, or when the rule is told it will be | `RuleCalculateAttackBonus.OnTrigger` (`TargetIsFlanked`) |

The supplied feature document asked for a 30-foot limit on ranged sneak attacks and for concealment to block them. Neither
is a rule of this game, so neither is added (plan §2).

## Implementation

| Part | Where |
| --- | --- |
| Pure rules: fortification factor, line damage, flank prediction, partner gain | `Tactics/SneakRules.cs` (unit-tested) |
| Fortification read, other engagers by the mode's rule, attacker engagement, the prediction, the partner's gain | `Tactics/CombatMath.Flanking.cs` |
| Every attack routine is priced flanked when the prediction says so: `TargetIsFlanked` on the attack-bonus rule and the sneak line | `CombatMath.ReadRoutineCore` |
| Each line's sneak and critical extra through fortification | `CombatMath.ExpectedWeaponLine` (shared by attack modes, held buffs and leaps) |
| The estimate's critical chances (first attack, Magus touch) through fortification | `EstimateWeaponAttackCore` |
| Attack-roll abilities, archer blast modes, auto-threat attacks | `AbilityCritFactor`, `ChooseBlastModes`, `OneAttack` |
| The partner's term on full attacks, attacks and walk-and-attack | `TacticalEngine.FlankPartnerBonus` in `AddWeaponCandidates` |

**The prediction.** The target is flanked when the attack lands if:
- it is flanked already; or
- the attack is a melee attack by a unit with a threat hand, so it becomes an engager, and at least one other unit counts
  as engaging the target by the mode's rule.

The routine records which case applies. Only the second makes the attack "flanks as the second engager", and only then
is the other engager named.

**The partner's term.** The partner is the single other engager, if it is not hostile to the attacker. Review correction:
its native `RuleCalculateAttackBonus(TargetIsFlanked = true)` is compared with its ordinary routine through the shared
line kernel, including existing sneak and critical damage. Newly enabled sneak dice are added through fortification,
never against precision immunity. The result is cached only on the existing frame/decision routine. This avoids a copied
bonus table when mods patch the rule. It is still a one-round forecast, not a simulation of the partner's next turn.

The term is valued as damage without a kill bonus, weighted like the attacker's own damage on that target.

**Bounds.**
- One fortification read per routine (per attacker, target and frame).
- One prediction per routine: the native flag, the threat hand, and one pass over the target's engagers.
- The partner's routine is the ordinary cached routine read, and only where this attack creates the flank.
- No position search. Native flanking does not require opposite sides, but **engagement does require reach and sight**.
  The current prospective assumption does not establish those; see [REVIEW_81_84.md](REVIEW_81_84.md#d-r1-proposed-origin-aware-engagement-quote).

## Checks

On checked source `86f8e82`, real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 998 pass (16 new in `SneakRulesTests`) |
| Inventory, ledger and supplement script tests | 27 + 18 + 3 pass |
| ProfileAudit | exit 0; `SNEAK` 20 checks, 0 failures; every other line equal to slice C's run; Fusion coverage and summon detail identical |
| Native harnesses | 98 / 14 / 10 |
| Added game references (relative to slice C) | 3 (`UnitHelper.GetThreatRange`, `UnitCommands.AnyCommandTargets`, `UnitPartFortification.Value`), none removed |

The exports hold 42 fortification facts:
- 40 with a fixed value: 25 % ×11, 50 % ×15, 60 % ×1, 75 % ×10 and 100 % ×3;
- 2 with a value from their context.

The armour features add 25, 50 and 75. 44 facts grant precision immunity.

## Replay

Record the startup MVID `e432cb9a` and keep `Log.txt` and `combatLog.txt`.

1. **Flanking, turn-based.** A front-liner fights an enemy, and a rogue (or any melee companion) starts its turn out of
   reach. Expect its `Move+Attack` or attack on that enemy to show `[flanks with <front-liner> +N]`. The combat log
   should show the flanking bonus and, for a rogue, sneak damage.
2. **Flanking, real time.** The same two on one enemy. Expect `[flanks ...]` while the other is attacking it.
3. **Fortification.** An enemy in fortified armour, or a construct-like foe with fortification: the rogue's attack value
   on it is lower than on a similar unfortified enemy. The combat log shows fortification rolls.
4. **Regression.** An enemy that cannot be flanked: no `[flanks]` tag.

## Limits

- **Not played.** Offline checks run no actor, engagement, attack roll or Unity timing.
- The prediction assumes a melee attacker stands in its threat hand's reach with sight when it attacks. A threat hand
  shorter than the attacking weapon is not modelled.
- Real time: an ally counts only while its current command aims at the enemy, as natively; it may stop before our attack
  lands.
- The partner's gain covers one round. The flanking bonus's extra critical threats are not counted.
- D4 (Shatter Defenses opening sneak attacks after Demoralize) is not done.
