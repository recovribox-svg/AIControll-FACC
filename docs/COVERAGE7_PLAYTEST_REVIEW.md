# Coverage7 playtest: partial review (handover to Codex)

21 November 2026, Claude. The author played the coverage7 package and supplied `Log.txt`, `combatLog.txt` and a save. They
then asked Codex to finish the log review and the fixes. This page holds the findings so far, with their evidence and the
proposed fixes. **No code is changed on this branch.**

- **Branch:** `claude/coverage7-review`, stacked on `claude/fusion-coverage-c6-c8` (draft PR #101), which is stacked on
  `codex/review-92-99` (draft PR #100). Nothing is merged.
- **Played build:** coverage7, startup MVID `0aa40527-d88f-4dfb-977b-3c6b101daaee` (Log.txt line 3), UMM 0.3.60.
- **Inputs:** fingerprints in [COVERAGE7_PLAYTEST_INPUTS.json](competence/COVERAGE7_PLAYTEST_INPUTS.json). The raw logs and
  the save are private and not committed.

Line references:
- `Log.txt` lines are `grep -n` numbers.
- `combatLog.txt` has lone carriage returns, so its line numbers depend on the tool. It is cited here by fight and quoted
  text instead.

## The session

- 10 fights: 1–5 turn-based, 6–10 real time. Enemies came from RandomReinforcementsPerEncounter spawns.
- 1,207 decision rows, 928 commands issued and 924 ended, 14 refused at issue. No AI Control exception.
- **Party** (combat log headers): Orvath (Kineticist 6 / Isekai 4), Aeiluvetar (Guardian 5), Seelah (Witch 1 / Monk 1), Camellia
  (Skald 2), Lann (Ranger 2), Wenduag (Shifter 8), Bandit Alchemist (Alchemist 4), Cambion Bard, Drow Magus (Magus 7), Bandit
  (Rogue 6), arcan (Arcanist 4), Tripper (animal companion).
- **Summons**, with summon control on: Orvath's demons (Incubus, Vermlek ×3, Schir ×2, Babau, Quasit, Abrikandilu), a
  Knight Commander (Arcanist 20) and a Summoned Monadic Deva.
- **Not known:** where the author rested or gave manual orders. Ask if it matters for a finding.

## What holds

- **No AI-caused friendly fire.** All party-on-party damage in the combat log (141 HP) is the game's own attacks of
  opportunity on dominated allies:
  - fight 5: Wenduag on the Bandit Alchemist, 15;
  - fights 8–9: Aeiluvetar takes 120 HP from Schir, a Quasit, Babau and Wenduag, and shoots Lann once herself (6).
- **No AI order is aimed at a dominated ally.** The coverage3 rule holds ("stopped, not killed", Log.txt 5439, 10125). Control
  is still used on them (a Stun Bomb on Aeiluvetar, 10490).
- **Freeing works.** Fight 5: Babau's Dispel Magic (Target) is valued for freeing the dominated Bandit Alchemist (Log.txt
  5506–5516). The combat log confirms it: "Babau dispels Dominate Person from Bandit Alchemist".
- **Flank forecasts are close.** The per-fight `[Flank]` summaries count 200 melee attacks, 186 forecast right.
- Only one turn ended with a positive option left: Wenduag's Griffon Aspect at 1.6 (Log.txt 5735), below the buff margin.

## Findings so far (ranked)

### 1. Dispel Magic (Point) is valued as if it dispelled a unit (both modes)

**Native contract** (decompiled assembly, `ContextActionDispelMagic.RunAction`):
- With no target unit, the action dispels **area effects** whose view contains the point. Each must have a caster, a source
  ability and pass the school and descriptor filters, and be an enemy's if `OnlyEnemyAreaEffects` is set
  (`TryDispelAreaEffect`).
- The blueprint `DispelMagicPoint` (`9f6daa93291737c40b8a432c374226a7`) can target only a point.
- So the point variant never touches a unit's buffs.

**Evidence:**
- Babau cast `Dispel Magic (Point)->Mythic Schir(dispel)` three times in real time (Log.txt 6430 in fight 6, 7413 in fight 6,
  14432 in fight 10). Each was worth the flat 2.0 fallback.
- The combat log shows no dispel attempt for those casts. Three standard actions were wasted.
- `FreeingValue` credited the point variant with freeing a unit: "Dispel Magic (Point) frees Bandit Alchemist 45% … = 28.3"
  (Log.txt 5508). It lost to the Target variant (29.0) only by chance.

**Cause:**
- `TryDispelValue` already refuses a point-only dispel (`TacticalEngine.Relief.cs:173`).
- So the old fallback runs: `HasSomethingToDispel(unit, target, …)` (`TacticalEngine.cs:3154`) reads the anchor unit's buffs.
  It then adds +2 "dispel" (`TacticalEngine.cs:5151`).
- `FreeingValue` (`TacticalEngine.TurnedAllies.cs:174`) does not check that the dispel can reach a unit.

**Proposed fix:**
- A dispel that can only take a point must not value unit buffs, in the fallback, in freeing or in relief.
- Better, if the cost is in proportion: value it by the hostile spell areas it can end at the point, using the native filters
  and the existing area danger to the allies inside (`AoeDangerAnalyzer`).
- Check whether `Dispel Magic, Greater (Area)` and similar variants share the gap.

### 2. A caster-only buff removal counts as removing anything (both modes)

**Native:**
- `Vermlek_Ability_AbandonFlesh` (`27695731aa092bc488c6092bfc68e31d`) is a swift action. It runs
  `ContextActionRemoveBuff ToCaster` on `Vermlek_Buff_FleshArmor` (`3a4c5a3c73b6142428c5815373ed98f4`).
- That buff gives +3 armour AC, and its `Deactivated` actions heal 2d6+3.
- The game's own AI casts it only when seriously hurt (`Vermlek_AiAction_AbandonFlesh`: `InjurySerious`, HP borders 20 and 10,
  and the swift action off cooldown).

**Evidence:**
- Four casts at 100 % HP, all in real time: Log.txt 9718, 10250, 12481, 13103. The Vermlek loses 3 AC for nothing.
- Each was worth 2.0 "(dispel)". At 12481 it was taken over Aid Another at 5.8, because the swift action goes first.

**Cause** (code path traced; which harmful buff was on the Vermlek is not in the log):
- `AbilityProfile` puts a `ToCaster` removal in `CasterRemovedBuffs` only (`AbilityProfile.cs:1111–1121`). So `RemovedBuffs`
  stays empty and `RemovesAnything` is false.
- `CanRemove` (`TacticalEngine.cs:3183`) returns true for **any** buff when the profile has no dispel action and
  `!RemovesAnything`.
- So any harmful buff on the target passes `HasSomethingToDispel`, and the fallback adds +2.

**Proposed fix:**
- A profile whose only removals are from its caster is not "unfiltered". It removes the named caster buffs only. Removing the
  caster's own helpful buff is a loss, not a cleanse.
- Optionally, price the removed buff's `Deactivated` heal against the lost buff's worth. That makes Abandon Flesh worth using
  when the Vermlek is low, as the native AI does.
- Check the other `CasterRemovedBuffs` users (held states, `SpentStateCost`) for the same assumption.

### 3. A successful dispel is logged as "did nothing" (both modes; diagnostics, possibly a block)

**Evidence:**
- Log.txt 5515: "[Cast] Babau: Dispel Magic (Target) did nothing to its target Bandit Alchemist … no attack roll, save, spell
  resistance, damage or effect on it".
- The combat log for the same cast says "Babau dispels Dominate Person from Bandit Alchemist".

**To check:**
- `CastOutcome.cs:380–400`: a `RuleDispelMagic` on the target, success or failure, should count as reaching it.
- With `BlockOnNoReach`, two such verdicts block the ability on that target for the fight. That would stop a second freeing
  attempt.

**Also unexplained:** after the dispel, Tripper, Seelah and Wenduag still made attacks of opportunity on the Bandit Alchemist
in fight 5, so the game still counted him hostile. Look for a second domination or a delayed faction restore before reading
this as a defect.

### 4. Plain advances refused, again and again, by the party's own area (real time)

**Evidence** (fight 7, Log.txt 8640–9000):
- 13 refused plain moves for Babau and Schir: "REJECTED UnitMoveTo 'Advance->… (around Corrosive Bomb)' … the path walks into
  Corrosive Bomb". The free-side variants are refused for the same reason.
- Each decision rebuilds the same three advances, each is refused at issue, and the summon waits ("idle advance unavailable",
  8708, 8730).
- Walks to attack through the same area are priced and issued instead (`[into Corrosive Bomb]`, `areaCheck=False`,
  8687).
- Whose area it was is not confirmed:
  - no Corrosive Bomb is thrown in fight 7 before the refusals, and the area already affects Aeiluvetar early in the fight
    (8003, Fortitude DC 24);
  - the Bandit Alchemist threw one late in fight 6 (7268), and AutoDismissSpell queued fight 6's party areas at its end
    (7622).

**Cause:**
- The "around" spot is chosen clear of the area, but the native path to it still crosses the area.
- Plain moves are kept unchecked when the three selected-path checks are used up (`[Approach] … plain moves kept`). The
  dispatcher's area gate then refuses them (`CommandDispatcher.cs:545–561`).
- Nothing remembers the refusal across decisions.

**Proposed fix (movement: ask the author before changing behaviour):**
- (a) Price an advance's crossing the way an attack walk is priced, instead of a hard refusal.
- Or (b) check the native path of the "around" and free-side spots when they are built (bounded), and drop those that cross.
- Either way, remember a refused plain plan for the decision round, so the next decision does not rebuild it.

### 5. Open question, high impact if true: was a dominated companion driven by AI Control? (fight 8)

**Evidence:**
- Log.txt 10125 logs Aeiluvetar as under an enemy's control. Later rows are AI Control decisions for her:
  - 10634, 10718, 10757: "Move out of Fiendish Tick Swarm's swarm", nauseated;
  - 10829: Focused Shot on the Distorted Mythic Dretch;
  - 10872.
- Party members made attacks of opportunity on her later in fight 8 and again in fight 9.
- The combat log has one failed and one successful Will save against Dominate Person in fight 8.

**To check:**
- Establish the order of events: domination, a re-save or dispel freeing her, and a second domination.
- If she was hostile while those orders were issued, `ShouldControlUnit` (`CustomBrainPatch.cs:144`, `IsDirectlyControllable`)
  let a dominated unit through.
- Also check fight 9: Dominate Person lasts days, so she may have started fight 9 dominated.

### 6. Performance (both modes)

The `[Perf]` summaries:
- turn-based fights: party decisions at a median of 13–32 ms and a 95th percentile of 149–301 ms;
- real-time fights: 95th percentile 94–198 ms, slowest 0.9–1.2 s.

**Over one second:** every such decision ran a garbage collection ("memory clean-up ran"; stage heap −260 to −330 MB). Single
decisions allocate 10–40 MB (Orvath, the Knight Commander). The allocation rate is the problem, not one stage.

**Cheap options that cost a lot** (the `costliest:` field of the slow rows without a collection):

| Ability | Cost per decision | Worth |
| --- | --- | --- |
| Light | 69–153 ms (0 contexts) | 0.0–0.1 |
| Guidance | 114–129 ms | small |
| Inflict Light Wounds | 80–188 ms | small |
| Cure Light Wounds | 31–125 ms | small |
| Aid Another | 5–52 ms | small |

They are touch abilities on many recipients. These rows also count 20–40 "crowded approaches searched for a free side". So the
cost is probably the free-side spot search per recipient, done before the value is known to be worth a walk.

**Proposed:** decide the recipient's value first, and plan the touch walk only when it can beat the current best option.
Measure before and after on a replay.

## Not yet reviewed (for Codex)

- **Fight 9 losses:** the Bandit Alchemist died ("CR3_Bandit_Human_Alchemist_Male … dies!") and so did a Vermlek. In fight 8,
  three Quasits died. Check whether the AI's choices or positions contributed.
- **Drow Magus, fight 7:** took 541 damage (Bulettes 390, Venomhodag 96, Dire Bear 41, a Vescavor Swarm 14) and cast while provoking ("casting
  provokes 1", "ray provokes 1", Log.txt 8841). She shows 100 % HP in the decision rows. Check how her HP was read, and whether
  stepping out first was the better choice.
- **164 real-time commands ended without acting.** Most come in clusters on one target in the same instant, which is the target
  dying (for example Log.txt 13553–13557, 13601–13607, 13842–13850). Not yet explained:
  - Deva's "Approach to touch: Heal->Aeiluvetar(cleanse)", twice, after 9.1 s and 7.0 s (11262, 11633). Is this a touch chase,
    and on a dominated ally?
  - Aeiluvetar's "Move out of … swarm", four times, moving at most 0.5 m (10808, 10943, 11494, 11576). Does this tie in with
    finding 5?
  - arcan's "Snowball/Shocking Grasp", six times unacted.
  - The Vermlek's and the Knight Commander's interrupted casts.
- **C6/C8 evidence:**
  - One `[FusionValue]` with C6 damage over time: Explosive Runes/Excruciating Deformation, Log.txt 449.
  - Sleep's Hit Dice aim rows (`[HD-Aim]`, 6999, 7218, 10539).
  - Hypnotism's pool credit is withheld ("shared-HD selector conditions are outside this forecast", 155).
  - No Circle of Death was cast; the save's spell lists may show whether anyone could.
- **The save** (`Manual_191_kala.zks`, a zip) holds the party's spellbooks, if needed for a coverage question. Open it only as
  untrusted data, in its own directory.
- **TB rows not yet read:** the turn-based fights 1–5, apart from the counts above.

## Checks

No code changed: no build, package or audit was run for this branch. The coverage7 receipt
([COVERAGE7_CHECKS.json](competence/COVERAGE7_CHECKS.json)) describes the played DLL.
