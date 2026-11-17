# Flank prediction from the game's engagement rules (review follow-up F1)

**Codex review correction (17 November 2026):** this page records the original F1 implementation/receipt.
Range compatibility does not prove native 3D engagement; native attack readiness uses a different distance metric plus
sight. The concealment monotonicity claim assumes fixed facts and weapon context. The unsupported 0.01 m slack is now
removed, and the origin-aware replacement remains a proposal. See [REVIEW_86_89.md](REVIEW_86_89.md) (F1-D1/R1).
Use its **review2** package/replay, not engage1. Historical checks below are preserved.

17 November 2026, Claude. This is follow-up F1 of [REVIEW_FOLLOWUPS_PLAN.md](REVIEW_FOLLOWUPS_PLAN.md) and answers D-R1
in [REVIEW_81_84.md](REVIEW_81_84.md). It changes valuation only, in both modes. Branch `claude/flank-engagement` is
stacked on PR #85, which is stacked on #84 → #83 → #82 → #81; nothing is merged. **Not played.** UMM stays 0.3.60.

Checks: [ENGAGE_F1_CHECKS.json](competence/ENGAGE_F1_CHECKS.json). Compiler and references:
[ENGAGE_F1_BUILD_PROVENANCE.json](competence/ENGAGE_F1_BUILD_PROVENANCE.json). Package:
[engage1](../test-builds/AI-Control-FACC-v0.3.60-dev-engage1-7f749bb-UMM-test.zip), startup MVID
`ac0cde8e-a80a-4b29-a3d7-fc093836b517`. It contains slices A–D and the PR #85 corrections.

## The problem

Slice D predicts that an enemy will be flanked when an attack lands, if the attacker becomes its second engager. It
assumed that any melee attacker with a threat hand becomes an engager.

In the game, a unit engages an enemy only when all of these hold (`UnitCombatEngagementController.TickUnit` →
`UnitEngagementExtension.IsEngage`):
- it can act and has a threat hand;
- the enemy is within its threat reach: both bodies plus the threat hand's range, measured in 3D, with line of sight
  (`UnitHelper.IsAttackOfOpportunityReach`);
- the enemy is not totally concealed from it (`UnitPartConcealment.Calculate`).

So the prediction credited flanks that never form. Examples:
- a melee companion without See Invisibility attacking an invisible enemy;
- an attack with a reach weapon from beyond the threat hand's range.

The prediction runs in every attack routine: party attacks, enemy danger, held buffs and leaps. These false credits
therefore inflated enemy danger as well as the party's attack values.

## The rule

`SneakRules.WillEngage` (pure, unit-tested), called from `CombatMath.AttackerEngages`. The attacker becomes an engager
when the attack lands if either:

1. **The game says it engages the target now** (`UnitCombatState.IsEngage`). The engagement controller has already
   checked reach, sight and concealment, so no geometry is needed.
2. **Or, for an attack still to come**, all of these hold:
   - it can act and has a native threat hand and range;
   - **its threat range covers the attacking weapon's range.** An attack lands inside its approach radius, which is
     both bodies plus the weapon's range (`UnitAttack.GetApproachRadius`). Engagement reach is both bodies plus the
     threat range, so the attack lands inside engagement reach.
   - **the target is not totally concealed from it now.** In `UnitPartConcealment.Calculate`, total concealment comes
     from targeting concealment, invisibility, the attacker's blindness or a total concealment entry. Those are either
     independent of position or gated by `DistanceGreater`. Blindsight removes concealment within its range, and
     weather adds only partial concealment for ranged weapons. Concealment therefore never grows as the attacker
     closes in: absent now means absent on arrival. If total concealment is present now, it may lift on arrival, so no
     credit is given; that is the conservative choice.

Ranged attacks still rely only on the native flank state, as before.

**Not changed:**
- the native flank count and the mode rules (`OtherEngagers`);
- the partner gain from PR #85;
- routine caching;
- movement, targeting and the dispatcher.

**Cost.** For an attacker that is not already engaging the target, one part lookup and one native concealment
calculation per routine read. Routine reads are cached per attacker, target and frame. There is no new cache key, path,
raycast or world scan.

## Checks

On checked source `7f749bb`, real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 1,008 pass (8 new) |
| Inventory, ledger and supplement script tests | 27 + 18 + 3 pass |
| ProfileAudit | exit 0; `SNEAK` 32 checks (7 new), 0 failures; every other line identical to a PR #85 build on the same inputs; Fusion coverage and summon detail byte-identical |
| Native harnesses | 98 / 14 / 10 |
| Added game references | 1 (`UnitCombatState.IsEngage`), resolved by the audit's route check; none removed |
| Supplementary Magus corpus | not run here (inputs not in this environment); F1 does not touch Magus code |

The new `SNEAK` checks cover:
- the native chain `TickUnit` → `IsEngage` → `IsAttackOfOpportunityReach` and `Calculate`;
- that threat reach reads both bodies and line of sight;
- that the approach radius reads the weapon's `AttackRange`;
- the structure of `Calculate`: distance-gated entries, blindsight, targeting concealment;
- the compiled route;
- the pure rule.

## Replay (turn-based and real time)

Record the startup MVID `ac0cde8e`. Keep `Log.txt` and `combatLog.txt`.

1. **Invisible enemy.** A front-liner fights an enemy under Invisibility. A melee companion without See Invisibility
   attacks it: expect no `[flanks]` tag on its attack, and no flanking bonus in the combat log. Repeat with See
   Invisibility or True Seeing: expect the tag and the bonus.
2. **Reach.** A companion attacking with a reach weapon while its threat hand is shorter. This is rare: note it if the
   party has one.
3. **Regression.** An ordinary second melee attacker on an engaged, visible enemy still shows `[flanks with <ally> +N]`.
   The A–D and PR #85 replays still apply.

## Limits

- **Not played.** Offline checks create no unit, engagement or attack.
- At a future arrival point, line of sight and height (3D reach) are taken from the attack being possible there, as the
  dispatcher's path-end check requires. They are not re-proved. Codex's full origin-aware geometry quote stays a
  proposal that needs the author's approval.
- Real time: an ally still counts as a second engager only while its current command aims at the enemy, as natively.
