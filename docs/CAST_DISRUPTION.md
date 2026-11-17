# Cast disruption (slice B)

17 November 2026, Claude. Slice B of [BURST_AIM_AND_TACTICS_PLAN.md](BURST_AIM_AND_TACTICS_PLAN.md) (Feature 4 of the
author's feature document, rewritten). The author asked for the next appropriate improvement after slice A. This slice
changes valuation only, in real time only. Branch `claude/cast-disruption`, stacked on PR #81 (`claude/burst-ground-aim`).
**Not played.** Manifest stays 0.3.60; no `main`, tag or release change.

Checks: [CAST_DISRUPTION_CHECKS.json](competence/CAST_DISRUPTION_CHECKS.json). Package:
[disrupt1](../test-builds/AI-Control-FACC-v0.3.60-dev-disrupt1-6845b83-UMM-test.zip), startup MVID
`549869b4-4b34-4e97-91e4-6acc4657e53d`. It contains slice A too.

## What changed for the player

In real time, an enemy caster is vulnerable for about 2 s while its spell is being cast, and for 6 s with a full-round
spell such as a summon. Any hit in that window forces a concentration check. A failed check wastes the spell.

The AI now values that:
- **Weapon attacks** count their first one or two attacks: a full attack, a single attack, or a walk and an attack.
- **Damaging abilities** count one hit on each casting enemy they reach.

For each hit that can land before the spell goes off, the AI adds the chance the hit breaks the cast times what the
cast would cost the party.

The candidate label shows `[breaks <spell> N%]` for weapon attacks, and the reasons of an ability show `disrupt`.

Turn-based mode is unchanged: an enemy casts on its own turn, when the party cannot act.

## The game's rules

Read from the supplied assembly:

| Rule | Native source |
| --- | --- |
| Damage to a caster whose Standard cast runs and has not gone off queues a check, if the spell can be broken by damage | `UnitConcentrationController.OnEventAboutToTrigger` |
| Spells cast from a spellbook can be broken; held touch charges and items other than scrolls cannot | `AbilityData.CanBeInterruptedWithDamage` |
| d20 + concentration against **10 + spell level + damage / 2** | `RuleCheckConcentration.OnTrigger` |
| A failed check ends the cast and spends the spell; one failure is enough | `UnitUseAbility.FailIfConcentrationCheckFailed`, `MakeConcentrationCheck` |
| Time left until the cast goes off: the running command's `m_CastTime` less its time so far | `UnitUseAbility`, `UnitCommand.TimeSinceStart` |

Stun, daze and the like also stop a cast (`!CanAct` / `!CanCast` in `UnitCommandController`), but the enemy keeps its
spell. The existing control value already counts the turns those take, so this slice adds nothing for them.

## Implementation

| Part | Where |
| --- | --- |
| Pure rules: fail chance, combining hits, timing, stakes, value | `Tactics/DisruptRules.cs` (unit-tested) |
| Casting enemies (once per decision), each cast's parameters and stakes (once per cast) | `Tactics/TacticalEngine.Disrupt.cs` |
| Stakes: the spell's harm on its victim, using the same reading as the enemy threat | `EnemyThreat.CastHarm` (shares `AbilityHarm` with `BestAbility`; identical results) |
| Fallback stakes: the caster's worth for a round | `EnemyThreat.RoundWorth` |
| Second attack's hit chance and damage | `WeaponAttackEstimate.SecondHitChance` / `SecondPerHit` |
| Weapon bonus | `AddWeaponCandidates` (full, single, walk and attack) |
| Ability bonus | `ScoreAbility`, per enemy hit |

**Timing.** All times are from the decision. Only hits that land at least 0.25 s before the cast goes off count.

| Hit | When it lands |
| --- | --- |
| Weapon, first attack | 0.1 s (command start) + any walk + 0.6 s |
| Weapon, second attack | 0.7 s after the first |
| Arrows and bolts | add the flight at 30 m/s |
| Abilities | 0.1 s + any walk + their own native cast time (2 s, so mostly against full-round casts) + their delay and flight |

The swing times are estimates for the replay to measure.

**The bonus.** For a hit with hit chance `h` and expected damage `d`:

    fail = (10 + spell level + floor(d) / 2 − concentration − 1) / 20, clamped

The chance a cast still goes off is `Π (1 − h × fail)` over the hits in time. The bonus is
`(1 − that chance) × stakes`. Spell level and concentration come from the game's own `RuleCalculateAbilityParams`.

**Stakes.**
- The stakes are the spell's harm on the party member it is aimed at, or the member nearest where its area lands:
  damage times chance, poison, control and area. This is the reading `EnemyThreat` uses for the enemy's best spell.
- An unread spell (a summon, a buff, a heal) is valued at the caster's worth for a round.
- The bonus is kept apart from the damage that the friendly-fire rule weighs.

**Bounds.**
- One command read per enemy per decision.
- A cast's parameters and stakes are read once, kept weakly by its command.
- A few multiplications per candidate aimed at a casting enemy.
- No dice-rolling rule, actor or context preview, or persistent actor cache.

**Diagnostics.** One `[Disrupt]` line per cast seen: the caster, the spell, its level and concentration, the time
left, and the stakes and their source. At most 64 lines per fight.

## Checks

On checked source `6845b83`, real game references, SDK 8.0.428 / Roslyn 4.11.0-3.25569.22:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Unit tests | 965 pass (12 new in `DisruptRulesTests`) |
| Inventory, ledger and supplement script tests | 27 + 18 + 3 pass |
| ProfileAudit | exit 0; `CAST-DISRUPT` 15 checks, 0 failures; `BURST-AIM` 28; every other line equal to slice A's run; Fusion coverage identical |
| Native harnesses | 98 / 14 / 10 |
| Added game references (relative to slice A) | 1 (`UnitCommands.Standard`), resolves |

All 1,892 exported spell blueprints can be broken by damage when cast from a spellbook.

## Replay (real time)

Record the startup MVID `549869b4` and keep `Log.txt` and `combatLog.txt`.

1. Fight an enemy caster with a melee companion beside it. When it starts a spell, expect a `[Disrupt]` line, and the
   companion's attack showing `[breaks <spell> N%]` in its decision. The combat log should show a concentration check
   for the caster after the hit.
2. Against an enemy summoner (a full-round cast, 6 s), expect ranged attacks and even spells to gain the bonus.
3. A cast that is about to go off (less than about 0.95 s left for a melee hit) gains nothing.
4. Note how long the companions' first hits actually take after the order. The 0.6 s and 0.7 s estimates should be
   corrected from that.
5. Turn-based regression: no `[Disrupt]` line.

## Limits

- **Not played.** Offline checks run no actor, damage, roll, command or Unity timing.
- The swing and flight times are estimates; the replay measures them.
- Expected damage stands in for the damage roll in the DC (the rule halves the actual damage dealt).
- Several party members can each be credited for breaking the same cast; only one needs to.
- Summons and buffs are valued at a round of the caster, not at their full effect.
