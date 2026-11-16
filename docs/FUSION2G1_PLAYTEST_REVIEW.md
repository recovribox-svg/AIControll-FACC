# Fusion2 + G1 playtest review (15 November 2026, Claude)

The author played the fusion2g1 package and supplied one AI Control `Log.txt` and the matching `combatLog.txt`. Raw
logs are private and are not committed. This page records what they show, the one defect found, the fix, and the next
replay. Receipt: [FUSION2G1_REVIEW_CHECKS.json](competence/FUSION2G1_REVIEW_CHECKS.json); `docs/TACTICAL_AI.md` §171.

## Inputs

| File | SHA-256 | Lines |
|---|---|---|
| `Log.txt` (AI Control) | `19a19912b9cefeb3e8040d4f95dafcccdba573614fbfca44f3d5d833623b7ee9` | 4,181 |
| `combatLog.txt` | `752434d811362c71850e4179baf39aa40d743a28aeae1c832fc2be51a5918ea3` | 21,634 |

The startup line names module `e4cb4df9-ad46-409a-a800-b96382c68a10`: the fusion2g1 package (checked source
`883bc9d`). Installed mods named in the log include ToyBox, Buff It 2 The Limit, RandomReinforcementsPerEncounter and
Tabletop Tweaks (its energy route appears in `[Math]` rows). Mystical Mayhem is not named by any log line. It is
established from the Runes patch below and its DLL among the author's earlier uploads.

## Run summary

- 10 fights; 370 commands issued, 370 with a terminal row; 121 ended `Interrupt`.
- The author interrupted often during this session. The real-time interrupts are the player's orders, not a fault.
- 31 slow-decision rows, the same pattern as fusion2. No AI Control exception or error row.

## G1: grapple state awareness works as intended

- **Target choice.** 118 `[Grapple]` rows, one per fight, pair and attack count. Wenduag's hold value is positive
  against casters and dangerous melee (Bandit Illusionist +2.7, Marauder +34.4). It is mostly negative against
  summoned wolves and dogs (21 of 25 wolf rows, all 6 dog rows), and the four positive wolf rows are +0.9: holding a
  weak summon costs her about as many turns as it takes from the summon. Her AI Control attacks went to casters and
  demons: Rotten Skeletal Spellcaster 9, Bandit Conjurer 6, Brimorak 4, Ferocious Schir 4, Marauder Conjurer 3,
  Baphomet 3, Cultist Conjurer 2. One went to a Summoned Dog (line 560). That decision was made before the grab was
  active: no `[Grapple]` row precedes it, and the player switched Grabber on at line 561.
- **The hold.** The combat log has 33 successful grapple maneuvers by Wenduag (starts and maintenance) and one
  critical failure. Victims failed 11 escape maneuvers. One Necromancer Conjurer broke free with a critical success.
- **Two wolf grapples, not from AI Control.** Two of those grapples hit a summoned wolf (combat log lines 1930 and
  2136). Neither claw hit is an attack of opportunity. No AI Control command of Wenduag's targeted a wolf: her
  commands in that fight (lines 750-837) target the Bandit Conjurer. The AI log does not show where those attacks
  came from. The author interrupted often in this session.
- **While holding.** Every decision with `holds X` in its header took only swift, free or move actions:
  - Griffon Aspect at lines 251 and 318;
  - END TURN at line 399;
  - a Cure Critical Wounds potion at line 3901, which the combat log confirms healed her.
- **No move while holding.** The Death from Above leap at line 297 is the first command of fight 3 (lines 292-343).
  The earlier hold ended with fight 2 (line 275). The leap grappled again, and the next decision (line 318) holds.
- **Routine cut.** Full attacks with Grabber on stopped at the grapple, as G1 forecasts. For example, line 247
  issues `FullAttack(4)` against the Cutthroat. It ends `Interrupt` after 1.5 s with `acted=True`, and line 251 holds.
- Release Grapple was never needed in this log (proposed G2, still without a played case).

## Fusion2: every Runes fusion fell back to the old forecast (fixed)

arcan (Magic Deceiver) built seven Runes fusions. All seven logged the same refusal (lines 1221-1237):

> `[Fusion] Incendiary Runes/Color Spray: joint outcome value not qualified for this recipient; existing forecast
> retained (mutable/unread guard ContextConditionCasterHasFact)`

Only Snowball/Shocking Grasp reached the joint outcome model (`[FusionValue]` at line 1226). The Runes fusions were
still cast, valued by the older forecast. Incendiary Runes/Color Spray scored 302.3 and succeeded twice (lines
1682-1850, 2590-2649).

**Cause.** The exported Runes blueprints carry no caster-fact guard, so the offline audit passed. Mystical Mayhem's
Cyphermage patch (`MysticalMayhem.BlueprintPatches.Cyphermage.Patch`) adds one at load time. It appends a
`Conditional` gated on `ContextConditionCasterHasFact` (fact `CyphermageRuneMaster`) to the `AbilityEffectRunAction`
of both Incendiary and Explosive Runes. Its true branch deals 1d4 magic damage per Cyphermage level. PR #64's
whitelist of stable guards did not list `ContextConditionCasterHasFact`, so the whole fused payload was refused.

**Why the condition is stable inside a fused payload.**
- The native check reads only the caster's facts: `Context.MaybeCaster.Descriptor.HasFact(Fact)`.
- A fused payload cannot change them. A caster-side payload is already refused: `LimitFusion("healing or caster-side
  payload")` for any `CasterBuffs`.
- The existing ordinal check still refuses a guard that names a buff an earlier application of the same payload
  gives. Only a payload that hits its own caster could add that buff to the caster, and the check refuses it whoever
  the recipient is.
- The guarded damage still goes through the existing per-caster guard evaluation (`AbilityProfile.AppliesTo`). A caster
  without Rune Master, such as arcan, gets no extra damage.

**Change.** `AbilityProfile.StableFusionGuards` (`Tactics/AbilityProfile.Fusion.cs`) accepts
`ContextConditionCasterHasFact`. Nothing else changes.

**Offline effect.**
- In the stock exports, 422 admitted configurations stopped at this guard: every one pairs Phantasmal Putrefaction,
  whose nested `Conditional` uses it.
- They now stop at their next existing refusal: 382 composite or non-HP damage, 24 dispel/enchantment/extension,
  16 dynamic removal.
- The qualified count stays **3,230**. The change matters where a mod adds the guard, as Mystical Mayhem does in play.

**Checks.** `FUSION-VALUE` grows from 15 to 23 checks:
- Mystical Mayhem's exact shape is rebuilt in memory on Incendiary Runes and Explosive Runes. The guarded Rune Master
  damage is read as a guarded HP part, not dropped. The played Incendiary Runes/Snowball and Explosive
  Runes/Color Spray qualify.
- A caster-fact guard on Incendiary Runes' own Burning buff, which its failed save applies earlier, is still refused
  with "earlier application changes a guard fact".
- The stock action lists are restored afterwards, so later sections and the coverage report see the export unchanged.

## Not changed

- **`[Grapple]` row volume.** Rows are already one per fight, pair and attack count. Two Marauders in one fight logged
  different holds (+38.1 and +1.4, victim worth 31.6 vs 13.2). Keying by name would hide that.
- **Snowball/Sleep and Explosive Runes/Sleep.** These stay withheld ("shared Hit Dice selection"), the documented F2
  boundary.

## Next replay (fusion2g1b package)

1. **Runes fusions.** With arcan's Magic Deceiver Runes fusions on the bar (TB and RT), each Runes pair should log
   `[FusionValue] ... -> <target>` instead of the `ContextConditionCasterHasFact` fallback. Compare the chosen Runes
   pair and its score with this log's rows 1682 and 2590.
2. **Rune Master.** If a party member has Mystical Mayhem's Rune Master, the Runes value should include its extra
   damage. Without the feat it should not.
3. **G1 and fusion2.** Repeat the earlier checks: [GRAPPLE_G1.md](GRAPPLE_G1.md) and
   [FUSION2_PLAYTEST_REVIEW.md](FUSION2_PLAYTEST_REVIEW.md).
