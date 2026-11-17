# Follow-ups to the PR #81–85 review

17 November 2026, Claude. This is an assessment of the findings and proposals that Codex left open in
[REVIEW_81_84.md](REVIEW_81_84.md) (PR #85). It selects the follow-ups worth the work and plans them.

**Base.** The base is PR #85's head `27e4196`, which carries the checked source `2d8a9b5`. That PR is stacked on
#81 → #82 → #83 → #84, and nothing in the stack is merged. Each new slice is a task branch stacked on the one before
it, with a PR into `claude/dreamy-pasteur-b9h3rn` that is merged only after #85. UMM stays 0.3.60.

## Assessment

| Item | Verdict | Gameplay benefit | Evidence | Cost and risk | Decision |
| --- | --- | --- | --- | --- | --- |
| **D-R1** engagement assumed without reach, sight or concealment | Valid | High: the flank prediction runs in every attack routine, including enemy danger | Native, verified below | Low: native facts only, no geometry | **F1, selected, implemented first** |
| D-R1, Codex's full origin-aware geometry quote | Partly superseded by F1 | Medium: only the cases F1 leaves | Needs a projected arrival point | Geometry at projected points, which needs approval | Kept as a proposal |
| **B-R1** several allies each credited with breaking one cast | Valid; the overcount is certain whenever two allies decide while one is already attacking | Medium to high in real-time fights against casters | Arithmetic (independent breaks) | Medium: reads already-issued friendly commands | **F2, selected, after F1** |
| C-R1 cleanse eligibility ignores the fight horizon | Valid | Low: needs a party-side Ameliorating hex or a form-suppressed debuff | Native durations | Low | F3, low priority |
| A-R1 straight-line burst prediction | Valid limit | Medium | Needs curved-path replays | Path-based forecast, which needs approval | Proposal only |
| A-P1 scene scan and the 32-watch cap | Valid in principle | Low: a party rarely has more than a few pending bursts | Needs a live count | Low | Measure in play first |
| B-R2 swing and flight timing estimates | Valid limit | Medium | Needs measured hit times | Calibration needs logs | Replay first |
| D-R2 Tabletop Tweaks fortification rerolls | Valid limit | Low: needs a supported reroll component in play | Native and TTT | High: needs a non-rolling provider | Documented limit |
| C-P2 repeated suppressor scans | Valid in principle | Low: only buffs the game already marks suppressed are scanned | No profile | Low | Not selected |

### F2 and the approval boundary

F2 values one action given the attacks allies have **already issued**. It reserves nothing for unselected candidates
and commits no unit to a target. That follows the existing precedent `CombatMemory.CastPendingByOther`, which withholds
a duplicate treatment while an ally's identical one is pending. I treat it as valuation. If the author classifies it
as target commitment, it is self-contained and can be dropped without touching F1.

## F1: engagement from the game's own facts (D-R1, both modes, valuation only)

**Done** on `claude/flank-engagement` (`7f749bb`): [FLANK_ENGAGEMENT.md](FLANK_ENGAGEMENT.md), [receipt](competence/ENGAGE_F1_CHECKS.json), package engage1 (MVID `ac0cde8e`).

**Problem.** `AttackerEngages` assumes that a melee attacker with a threat hand becomes an engager of its target. The
native engagement test (`UnitCombatEngagementController.TickUnit` → `UnitEngagementExtension.IsEngage`) also requires:
- `UnitHelper.IsAttackOfOpportunityReach`: 3D distance under both bodies plus the threat range, and line of sight;
- `UnitPartConcealment.Calculate(...) != Total`.

The assumption therefore credits flanks that never form. An attacker without See Invisibility attacking an invisible
enemy is one case; an attack from beyond the threat hand's range is another.

**Native facts used (verified on the supplied assembly):**
- `UnitCombatState.IsEngage(target)` (`m_EngagedUnits`) is the controller's result of that whole test. When the
  attacker already engages the target, it is the game's own answer and needs no geometry.
- `UnitAttack.GetApproachRadius` is both bodies plus the attacking weapon's `AttackRange`. Engagement reach is both
  bodies plus the threat range of `GetThreatHand`. An attack lands inside its approach radius, so a threat range at
  least as long as the attacking weapon's range puts the attacker inside engagement reach.
- `UnitPartConcealment.Calculate` can only fall as the attacker closes in:
  - the sources of total concealment (targeting concealment, invisibility, blindness, total entries) are
    position-independent or gated by `DistanceGreater`;
  - blindsight removes concealment within its range;
  - weather adds only partial concealment, and only for ranged weapons.

  Total concealment absent now therefore stays absent on arrival. Total concealment present now may lift on arrival,
  so withholding credit in that case is conservative.

**Rule** (`SneakRules.WillEngage`, pure and unit-tested). The attacker engages when either:
- it engages the target natively now; or
- it can act, its threat range covers the attacking weapon's range, and the target is not totally concealed from it now.

**What does not change.** The prediction is computed inside the routine read (per attacker, target and frame). There
is no new cache key, path, raycast or world scan, and no change to movement, targeting policy, routine selection or the
partner gain formula. Ranged attacks still use only the native state.

**What stays open.** At a planned arrival point, line of sight and height (3D reach) are taken from the attack being
possible there, as the dispatcher's path-end check already requires. They are not re-proved.

**Tests.** Unit tests for `WillEngage`. ProfileAudit `SNEAK` gains these checks:
- the native contracts for `TickUnit` → `IsEngage` → reach and concealment;
- concealment monotonicity: the distance branches and the position-independent total sources are in `Calculate`;
- `GetApproachRadius` reads the weapon's `AttackRange`;
- the compiled route: `PredictFlank` → `AttackerEngages` → `IsEngage`, `Calculate`, `GetThreatRange`, `WillEngage`.

**Replay (TB and RT).**
1. A front-liner engages an invisible enemy. A melee companion without See Invisibility attacks it: expect no
   `[flanks]` tag. Repeat with See Invisibility: expect the tag.
2. A melee attacker whose threat hand is shorter than the attacking weapon's range: no prospective flank.
3. An ordinary second melee attacker, seen and in reach: the tag as before.
4. Enemy danger: a second enemy approaching an ally that one enemy already engages is still priced flanked.

## F2: marginal cast disruption after friendly hits already issued (B-R1, real time)

**Problem.** Each ally's attack on a casting enemy is credited with the full chance of breaking the cast, even while
other allies' attacks on it are already under way. Breaks combine as `1 − Π(1 − p)`, so separate credits overcount.

**Plan.**
- For each casting enemy, once per decision, read the allies whose current command is a `UnitAttack` or damaging cast
  on that enemy, not yet finished.
- Estimate the chance the cast survives their next hit. Use their cached routine estimate (hit chance, damage) and the
  slice B fail chance, counting only hits that land before the cast goes off (`DisruptRules.InTime`).
- Price this unit's candidate at `still_others × (1 − still_ours) × stakes`.
- State: computed lazily on the per-decision `CastInfo` view; nothing is kept across decisions, because allies'
  commands change. No reservation, no change to the commands issued.

**Tests.** Unit tests for the marginal value: zero, one and two pending allies, a pending hit that comes too late, and
an already-broken cast. The audit checks the route and the native `UnitCommands` and `UnitAttack` reads.

**Replay (RT).** Two melee allies near an enemy caster: the second ally's `[breaks ...]` share falls once the first
one's attack is issued.

## F3: horizon-aware cleanse of suppressed debuffs (C-R1, low priority)

A suppressed debuff counts for a cleanse only if it acts again within the fight horizon:
`SuppressionRules.ActiveRounds > 0`, instead of "outlasts its suppressor". This is the same horizon the dispel worth
already uses. The arrival rule (C1) stays conservative. To be done after F2 if it still warrants the work.

## Not selected

- The origin-aware geometry quote (D-R1, full), path-based burst prediction (A-R1) and the burst watch overflow
  behaviour (A-P1) wait for the author's decision.
- Timing calibration (B-R2) and the TTT fortification provider (D-R2) need replay evidence or a provider design first.
