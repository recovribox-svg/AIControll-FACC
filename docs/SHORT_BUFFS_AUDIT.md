# Short combat buffs - Phase A audit (as-is), 0.3.19-test10

Branch `claude/dreamy-pasteur-b9h3rn`, commit 560c376 (0.3.19-test10). Game references: decompiled Assembly-CSharp in
the dev environment; no runnable game here, so nothing below is in-game verified. Build: `dotnet build` (0 errors);
tests: `dotnet test tests/CompanionAI.Tests` (53 pass). Evidence: the 12 playtest logs from 0.3.18-test9 to 0.3.19-test8
(3 159 decisions, 396 of them a buff). Installed content mods seen in the logs: Tabletop Tweaks, Expanded Content, Tome of
the Firebird, Mystical Mayhem, IsekaiMod, Demonology.

## 1. Decision map

| Step | Where | State |
|---|---|---|
| Available ability | `AbilityDiscovery.Gather` (both engines), `AddAbilityCandidates`, `IsAvailable`, blacklist | correct |
| What it applies | `AbilityProfile` (walks actions, conditionals, areas, AddFacts): `TargetBuffs`, `CasterBuffs`, `BuffAppliesTo` per recipient | correct |
| Duration | `AbilityProfile.ExpectedRounds` (fixed values only; rounds per level = unknown), `HasLongBuff` (minutes or longer), `BuffsLastOneRound` (fixed <= 1 round), `BuffsSpentByAttack` / `ByRoll` / `ByCast` (test9) | **approximate**: the game's own duration (caster level, Extend) is never computed; self-buffs (`ToCaster`) keep no application record at all |
| What it does | `BuffEffectAnalyzer.Analyze` -> `CombatValue` 0..1 | **wrong scale**: the largest single component's class value; the bonus size is never read (+1 = +4), several components ignored, penalties only for Dexterity |
| Stacking | `BuffStackingAnalyzer.Analyze` (game's `ModifierDescriptorHelper.IsStackable`) | **bug**: a bonus computed from a rank (ContextValue) counts as +1, so any existing bonus of that type >= 1 marks it "superseded" (Shield of Faith on someone with a +1 deflection item would never be cast); no partial increment |
| Recipient fit | `BuffQuality`: Strength-only x0.2 for ranged/no-Str weapons, casting-only = 0 without spellbooks | **thin**: nothing else about the recipient (its attacks, hit chance, what hits it) |
| Value | `ScoreAbility` buff block: `BuffPerRound(level) x rounds x quality x (engaged ? 1 : 0.6) x buffScale`, buffScale = `(Round <= 2 ? 1 : 0.6) x RoleWeight("buff") x min(1.2, 0.4 + Difficulty)` | **not damage-equivalent**: spell level stands in for effect size; the round-2 discount double-counts `ExpectedRounds` (already the rounds left); difficulty double-counts danger |
| Competition | `Decide`: quick (free/swift, bar 2; costless free bar 1) first, else best main (bar 0.5); same scale as attacks, spells, heals | sound structure; no tie rule for a Standard-action buff against an immediate action |
| Issue / execute | `TacticalEngine.Execute` -> `CommandDispatcher.Run` | correct |
| Outcome | `TacticalDriver.LearnFromLastCommand` (failed this turn), `CastOutcome` (silent failures), `CombatMemory.RecordCast` / `LastCastRound` (same round) | correct: every interrupted buff in the logs had a real cause (combat end, area replan); no retry loops found |
| Re-evaluation | fresh value each decision; "already active" by buff fact; enchantments by `EnchantmentActive` | correct in principle; repeats come from the value, not from state |
| Real time | `RealTimeTactics.Act` -> same `Decide` / `ScoreAbility` | shared; the legacy engine (`Scoring/Scorers/BuffScorer.cs`, name-based Mage Armor/Shield checks) is separate and out of scope |
| Out of combat | `BuffWorth` (pre-buff button, RT keep-up) shares `BuffQuality` | out of scope (unchanged) |

## 2. Evidence from the logs

| Ability | Chosen | Value (median) | Best non-buff option (median) | Note |
|---|---|---|---|---|
| Arcane Reservoir - Increase CL | 94 | 2.8 | 27.5 (free action) | fixed in test9 (next-spell boost) |
| Perfect Strike | 86 | 2.7 | 7.4 (free action) | works: cast after attacking, spent by next round's first attack |
| Spirit Weapon Enchantment | 63 | 8.6 | 5.1 | recast after expiry (1 minute); fine |
| Enlarge Person | 49 | 8.6 (10.1 cap common) | 5.1 | round 1 of almost every fight at the same value for any recipient; -2 Dex, -1 attack/AC from size ignored |
| Guidance | 21 | 2.3 | 1.7 | 6 casts on herself in one fight (Camellia, RT5-RT21) at full HP; test9 made it one roll |
| Item: Bull's Strength | 18 | 9.2 | 3.2 | potions drunk in round 1 |
| Light of the Angels | 13 | 18.5 | 15.5 | temporary HP valued 10.6-22.9 at 55-96 % HP, over real attacks; 4 casts in one fight |
| Item: Shield of Faith | 8 | 7.9 | 1.2 | |

## 3. Why the safeguards do not give the wanted behaviour

1. **The value does not measure the effect.** `quality` is one number per buff, the best component's class (AC 1.0, attack
   1.0, temp HP 0.7...), with no size: Guidance's +1 equals a +4 and Light of the Angels' temporary HP counts the same at
   full health as near death. `BuffPerRound(level)` then turns spell level into points: a level-1 buff is worth 2.5 a
   round whoever gets it.
2. **Nothing about the recipient**, except two flags (Strength without a Strength weapon, casting boosts without
   spellbooks): an attack bonus is valued the same for an ally who hits on a 2 and one who cannot reach an enemy; AC the
   same for an ally nothing attacks.
3. **Double counting of time and danger**: `ExpectedRounds` already shrinks as the fight goes on, and the value is cut
   again by 0.6 after round 2 and by the difficulty factor.
4. **Durations are guesses**: rounds per level -> 3 rounds, minutes -> the rest of the fight; the real duration (the game
   computes it from caster level and metamagic) is not used; self-buffs keep no duration at all.
5. **Stacking reads rank-based bonuses as +1** (false "superseded"), and gives no partial increment.
6. **No tie rule**: a Standard-action buff that is only as good as an attack is cast as often as the attack.

Repeats in the logs (Guidance, Light of the Angels) are re-evaluations with an inflated value, not stale state.

## 4. Plan (phases B-E)

- **B. Mechanics.** Effect parts with sizes read from the buff's components (attack, damage, AC by type, saves, stats,
  temp HP, concealment / miss chance, mirror images, DR / energy resistance, extra attacks, size), evaluated with the
  caster's real context (`MechanicsContext` of the buff under the ability's execution context, as the game builds it;
  the same mechanism `TacticalEngine.Modes` uses for combat modes). Effective duration from `DurationValue.Calculate`.
  Stacking: increment over the recipient's existing bonus of the same type (game rule), no "+1" placeholder.
- **C. Recipient value in damage-equivalent points.** Offence parts from the recipient's own weapon estimate against its
  likely target (`CombatMath.EstimateWeaponAttack`: hit chance, hits, damage per hit); defence parts from what actually
  comes at it (`IncomingDamage`, observed damage included) and the enemies' hit chance against it; turned into points the
  way heals and risks are (HP kept + the lower chance to drop). Useful window = min(real duration, rounds left, the
  trigger: one attack, one roll, one cast). Unknown parts keep a conservative share of today's formula and are reported.
- **D. Discipline.** Drop the round-2 and difficulty double counts (keep the role weight). A Standard / full-round action
  that only buffs must beat the best immediate main action by a margin (about 15 %); otherwise the immediate action wins.
  Free and swift buffs unchanged.
- **E. Diagnostics and tests.** One bounded line per chosen or narrowly beaten buff (parts, recipient, window, value,
  best alternative). Pure formulas in `ValueModel` with unit tests, including a Standard-action buff that should win and
  a free/swift one that should win.
