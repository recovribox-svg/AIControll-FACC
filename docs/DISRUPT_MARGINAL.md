# Cast disruption after allies' ordered hits, on the native attack schedule (review follow-up F2)

**Codex review correction (17 November 2026):** the sequence table alone does not establish native chaining;
selection includes matching and RNG, and the issued shortcut requires adjacent sequential/released flags. The checked
correction reads issued windows, quotes each pending hand/iterative, respects cheap start/cooldown gates and preserves
per-hand delivery. Timing still has explicit animation/projectile/approach approximations. See
[REVIEW_86_89.md](REVIEW_86_89.md) (F2-D1–D3/R4). Use its **review2** package/replay, not disrupt2; receipts below are
historical, and natural attacks must not all be presumed sequenced.

17 November 2026, Claude. This is follow-up F2 of [REVIEW_FOLLOWUPS_PLAN.md](REVIEW_FOLLOWUPS_PLAN.md). It answers B-R1
and part of B-R2 in [REVIEW_81_84.md](REVIEW_81_84.md). Real time only, valuation only. Branch `claude/disrupt-marginal`
is stacked on F1 (PR #86), which is stacked on PR #85 → #84 → #83 → #82 → #81. Nothing is merged. **Not played.**
UMM stays 0.3.60.

Checks: [DISRUPT_F2_CHECKS.json](competence/DISRUPT_F2_CHECKS.json). Compiler and references:
[DISRUPT_F2_BUILD_PROVENANCE.json](competence/DISRUPT_F2_BUILD_PROVENANCE.json). Package:
[disrupt2](../test-builds/AI-Control-FACC-v0.3.60-dev-disrupt2-bcb46df-UMM-test.zip), startup MVID
`9a4d8a66-96f1-4613-8357-34d8743df828`. It contains slices A–D, the PR #85 corrections and F1.

## The problems

1. **Several allies credited for one break (B-R1).** When an enemy starts casting, each ally's attack on it was credited
   with the full chance of breaking the cast. One failed concentration check is enough, and breaks are independent.
   The party's combined value was therefore overcounted, and every nearby attacker was pulled onto the caster.
   - Example: two 50 % breaks of a cast worth 100 are worth 75 together, but each was credited 50.
   - Codex's numbers: three allies were credited 150 in total for a combined value of 87.5.
2. **The second hit came too early (part of B-R2).** Slice B placed a routine's second hit a fixed 0.7 s after the
   first, which let most full attacks break a 2 s cast twice. In real time the game spaces a routine's attacks over a
   6 s window instead (`UnitAttack.OnTick`).

## The game's rules

| Rule | Native source |
| --- | --- |
| A routine's attacks share a 6 s animation window. Attack *k* of *N* starts *k* × 6 / *N* seconds into the command. With `IsIgnoreCooldown` the window is the standard cooldown, at least 3 s. | `UnitAttack.Init`, `UnitAttack.OnTick`, `ConfigureAnimations` |
| A bow-style weapon's next attack starts as soon as the previous one acts. So does an attack in an animation sequence (`IsSequential`, from the unit's animation set). | `UnitAttack.OnTick` |
| A command's time counts from its start, after any approach. `GetAttackIndex` is the next attack to deliver. | `UnitCommand.Start`, `Tick`; `UnitAttack.GetAttackIndex` |
| Each damage packet before the cast acts makes its own concentration check, and one failure ends the cast | slice B: `UnitConcentrationController`, `RuleCheckConcentration` |

Turn-based mode compresses iterative attacks differently (`MaxDelayBetweenIterativeAttacks`). It is not affected:
disruption is valued only in real time.

## The change

- **Native spacing.** The second hit of a routine lands `6 s / N` after the first (`DisruptRules.SwingSpacing`); a
  chained attack keeps the 0.7 s estimate. A three-attack routine's second hit (2 s in) now misses a 2 s cast, and the
  first hit still counts.
- **Allies' ordered hits.** Once per casting enemy per decision, the engine reads the allies whose Standard command is
  a `UnitAttack` on that enemy that has not finished:
  - an attack that is running is placed on its own schedule (`DisruptRules.RunningHitSeconds`), from the command's
    time and next attack index;
  - an attack whose unit is still approaching adds the walk;
  - hit chance and damage come from the ally's cached routine estimate;
  - at most two hits per ally are counted, and only those in time;
  - the result is the chance the cast survives them.
- **Marginal value.** This unit's own disruption value becomes `others-survive × own-break × stakes`
  (`DisruptRules.MarginalValue`), whichever hit lands first. With no hits ordered it is the old value.
- The tag reads `[breaks <spell> N% more, after K allied hits already ordered]`.
- Weapon attacks, their attack-mode alternatives (PR #85's repricing) and damaging abilities all share the change.
- Nothing is reserved for actions not yet chosen, and no command is changed. The value only counts orders already
  given, as `CombatMemory.CastPendingByOther` does for treatments.

**Cost.** One pass over the allies per casting enemy per decision, plus a cached routine read for each ally already
attacking it. No rule is triggered for non-attacking allies, and no world scan or new cache is added.

## Checks

On checked source `bcb46df` (the audit-tool follow-up `b0d5f86` does not change the mod), real game references,
SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 1,017 pass (9 new) |
| Inventory, ledger and supplement script tests | 27 + 18 + 3 pass |
| ProfileAudit | exit 0; `CAST-DISRUPT` 28 checks (9 new), 0 failures; `SNEAK` 32 (F1); every other line identical to the F1 and PR #85 runs on the same inputs; Fusion coverage and summon detail byte-identical |
| Native harnesses | 98 / 14 / 10 |
| Added game references | 6 (`UnitAttack.IsAttackFull`, `GetAttackIndex`, `ItemEntityWeapon.GetAnimationStyle`, the animation manager, set and sequences), all resolved by the audit's route checks; none removed |
| Supplementary Magus corpus | not run here (inputs not in this environment); F2 does not touch Magus code |

The audit runs the production aggregator. An ally's ordered hit that leaves the cast going half the time halves this
hit's value (4 → 2) and its added chance (4 % → 2 %); with none ordered, the value is unchanged.

## Replay (real time)

Record the startup MVID `9a4d8a66`. Keep `Log.txt` and `combatLog.txt`.

1. **Two allies on one caster.** Two melee companions near an enemy caster. When it starts casting, the first ally's
   attack shows `[breaks <spell> N%]`. Once that attack is under way, the second ally's attack on the same caster shows
   `[breaks <spell> M% more, after K allied hits already ordered]`, with M below what it would be alone. The second ally
   may now prefer another target.
2. **Full attacks against a short cast.** A companion with three or more iterative attacks next to a caster starting a
   2 s spell: its full attack's break chance counts only the first hit. Compare with the combat log: the second attack
   comes about 6 / N seconds after the first.
3. **Bow and natural-attack sequences** keep the shorter spacing. Note the actual times if a bow user or an animal
   companion attacks a caster.
4. **Turn-based regression.** No `[Disrupt]` line and no `[breaks ...]` tag.

## Limits

- **Not played.** Offline checks create no unit, command, attack, animation or timing.
- The first swing (0.6 s from the command's start) and a chained attack's 0.7 s spacing remain estimates. The replay
  measures them.
- Damaging spells allies have already cast at the caster do not count as ordered hits; only weapon attacks do.
- An ally's ordered attack may still be cancelled or retargeted before it lands. Its hits count while the order stands.
- Expected damage still stands in for the damage roll in the DC.
