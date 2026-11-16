# Profane Invocation transfer ledger (I1)

**16 November 2026, Codex: gaps2 evidence follow-up.** See [the new review](GAPS2_PLAYTEST_REVIEW.md) for the supplied
gaps2 pair and all 32 completed invocations. The original tables below remain historical I1 evidence; their
receipts are not rewritten. The updated tool has 18 tests, scopes state by caster and appraisals by fight/decision,
and distinguishes direct ability IDs from explicitly marked holder inference. It will not reuse an earlier fight's
identical label/value. A missing detailed row can use the current **chosen** decision's net and one unique logged
source holder to infer the tier; gross and slot remain unknown. An ambiguous tier stays unread, and older state
for that stat becomes uncertain. JSON rows include `caster`, `identity_source` and `value_source`; the
`fight_start_holders` keys now also name their caster. Cross-tier equal final coverage is labelled a **coverage
alternative**, requiring earlier values/timing, not automatically a wasted cast. In gaps2 the apparent shortcut's
earlier holder loss makes it unattractive before the later recipient's value rises.

14 November 2026, Claude. Tooling and documentation only: no gameplay source, manifest or package change. Section 164
of `docs/TACTICAL_AI.md` summarises it; the receipt is `docs/competence/INVOCATION_LEDGER_CHECKS.json`.

**Question.** Lann's Demonic Overlord can move twelve Profane Invocation buffs between allies at no cost. Earlier reviews
counted many casts a fight and proposed that greedy, one-cast-at-a-time placement wastes casts. The planning checkpoint
named one sequence (v0.3.51 rows 2032 and 2067) as a likely redundant pair and proposed I2, a joint tier/recipient
assignment, if this ledger confirmed redundant casts.

**Answer.** It does not. Across both played logs, 29 casts were issued and 28 acted. None of them is redundant. The
named pair was the better path, because the one-cast alternative would have removed another buff. I2 is not warranted
on this evidence, and no runtime change is made.

## The game's rules (exported blueprints and the decompiled game, read-only)

- Twelve abilities under two parents (`ProfaneAscensionAbility1` / `2`), six stats each. Every one is a **free action
  with no resource**, Medium range, friends and self. It applies a **permanent** buff.
- Every buff is `AddContextStatBonus` **+2 Profane** on one stat, `UniqueBuff`, `StayOnDeath`.
- `UniqueBuff`: `UnitPartUniqueBuffs.NewBuff` removes the caster's previous copy of the same buff, wherever it is. One
  holder per buff and caster. On area unload, copies on units in another holding state are removed.
- Each ability removes the **five other buffs of its own tier** from its target, then applies its own (its siblings).
  The tiers are independent: one recipient can hold one buff of each tier.
- Profane is not in the game's default stacking set (`ModifierDescriptorHelper.DefaultStackingDescriptors`), so tier 1
  and tier 2 of the same stat on one recipient give +2, not +4.

## What the engine does (development `e1ae334`)

`TacticalEngine.ScoreAbility`, the helpful-buff block. It is the same path in turn-based and real-time combat.

- For each recipient (at most five), the buff's worth for that recipient comes from `AppraiseBuff`, which reads the
  stat's uses on that recipient.
- A sibling it removes is judged by both appraisals: `BuffSwitchGain` needs a 1.25x margin.
- A holder elsewhere (`UniqueBuffHeldElsewhere`, over the engine's ally list) costs that holder's worth without the buff
  (`AppraiseHeldBuff`), again with the margin.
- A buff put there by this caster within 3 rounds of this fight stays unless the move gains 5 or more
  (`ValueModel.UniqueMoveAllowed`); never in the same round.
- A replaced sibling is settled by its own source cast (`UniqueReplacementAllowed`).
- The ally list excludes the dead, the unconscious and those out of combat. A holder in that state is not seen, so the
  move is priced with no holder loss and no settling.
- The first note per fight and ability says where a buff stays and why. The chosen cast's `[Buff]` row gives net,
  gross, holder and sibling losses and the ability id.

## Method

`tools/InvocationLedger/invocation_ledger.py` reads the mod's own rows of a `Log.txt`.

- **Rows read:** the fight starts, the `[Tactics]` decision rows, the once-a-fight "stays on" notes, the chosen
  `[Buff]` row, and `ISSUED` / `ENDED`.
- **Holder state:** it keeps who holds each of the twelve buffs. A holder changes only on an `ENDED` row that acted.
- **Classes:** it classifies every cast and checks sequences: cross-tier pairs, chains, returns, and re-placements
  in the next fight.
- **Cross-tier check:** for a cross-tier pair, it asks whether the tier-a recipient held another buff of the other
  tier, which the one-cast alternative would have removed.
- **Safety:** it executes nothing and needs no game files. Eleven tests use synthetic rows in the exact templates.

Classes: `first-placement`, `sibling-replacement`, `move-settled`, `move-override` (moved within 3 rounds on a gain of
5 or more), `move-under-settle` (the same with less: should never occur), `holder-unseen` (the ledger knows a holder the
engine did not see), `not-applied`. Flags: `sibling`, `low-loss` (holder lost 0.3 or less), `last-enemy` (one enemy or
none left at the decision), `same-stat` (the recipient already held the other tier of this stat).

## Inputs

| file | session | bytes | lines | SHA-256 |
|---|---|---|---|---|
| `Log.txt` | v0.3.51 stable, MVID `e31e626d`, six fights (matches `V0352_PLAYTEST_INPUTS.json`) | 937,531 | 4,608 | `e661b9d82254fa8b2062a518eb4e27ddca1df461a5eb0b04ff49ec048bad7cbb` |
| `Log.txt` | v0.3.52 stable, header MVID `981a7b77-a654-4da1-9a22-68bc1c6a77bd`, two fights | 488,015 | 2,318 | `4a89f31702a23792f6b53498bf2f006d225279cd331acff50c69ea2bd7ba2f61` |
| `combatLog.txt` | the game's own log of the v0.3.52 session (two combats) | 317,218 | 8,033 | `90e946ad48684332117ea24f3f161f3c7588370c666ed60c50e5b3154768ae04` |

The author supplied the v0.3.52 pair and noted that it includes Grabber use in both modes. The raw logs are private
inputs and are not committed; the tables below are derived from them.

## Results

| | v0.3.51 | v0.3.52 | both |
|---|---|---|---|
| fights | 6 (TB 2, RT 4) | 2 (TB 1, RT 1) | 8 |
| issued / acted | 18 / 17 | 11 / 11 | 29 / 28 |
| first placement | 2 | 2 | 4 |
| move, settled | 11 | 8 | 19 |
| move, override (net 9.6 and 7.2) | 2 | 0 | 2 |
| holder unseen (holder down or not in combat) | 2 | 1 | 3 |
| not applied (interrupted) | 1 | 0 | 1 |
| move under settle | 0 | 0 | 0 |
| same-stat (would not stack) | 0 | 0 | 0 |
| redundant cross-tier pair | 0 of 2 | none | 0 |

Flags: a sibling removed 7 times, the holder lost 0.3 or less 4 times, one enemy or none left 3 times. Duration:
25 of 28 acted casts ended at 0.0 s. The other three, each the first real-time cast of its fight, ended after
2.5–4.7 s; each was among the first real-time casts of its fight. That points to waiting for an action already under
way, not a cost of the cast, but it is not shown.

### The named pair (v0.3.51, fight 4, real time RT2)

- Row 2032: tier-1 Dexterity moved Lann → Tuvel (Tuvel 17.6, Lann 7.9 lost, net 9.6).
- Row 2067: tier-2 Dexterity moved the unnamed ally → Lann. Lann's worth was 29.8 by then; 6.1 was lost on the unnamed
  ally and 1.2 for Lann's own tier-2 Constitution, net 22.5.
- The proposed shortcut was tier-2 Dexterity straight onto Tuvel. But Tuvel held tier-2 Charisma, worth 9.4 there (the
  fight's note), and that cast would have removed it.
- Priced the engine's way with the values the log prints in that round, the shortcut nets about 2.1: 17.6 less 9.4
  for the Charisma, then less 6.1 on the unnamed ally. The tier-1 move netted 9.6. The shortcut does not appear among
  the options at 2032.
- So the two casts were the better path. The other cross-tier pair (fight 5, 3203 → 3267) is the same: Valmot held
  tier-1 Strength.

### Other sequences

- **Chains, the same buff moved twice within one round, three:**
  - Aeiluvetar → Lann → Tuvel: settled, then override.
  - Seelah → Camellia → Lann: both holders were down. Seelah needed a revive at RT3; Camellia made no decision from RT3
    until RT9, at 2 % HP.
  - Valmot → the summon Abrikandilu → Bloodragers, override 7.2 on Bloodragers' 221.5 damage a round.
- **Each chain followed a change in the fight between its two decisions**: a holder went down, or a recipient's worth
  rose within the round. The log does not show that one move at the first decision would have scored as well.
- **One return:** tier-1 Strength came back to Tuvel at RT5 of fight 4, four rounds after it left.
- **One re-placement:** at the end of fight 4, with one enemy left, tier-1 Strength on Tuvel removed his tier-1
  Dexterity, then worth 0.0 (net 1.2). Fight 5 cast tier-1 Dexterity again, on Seelah, worth 11.4 there. Nothing in
  the log shows a cost from the end-of-fight removal: the next fight placed that buff on Seelah, not on Tuvel.

### What this means

- **Cost:** a cast costs a free action and no resource. The only costs the logs show are a later decision and log
  rows.
- **No wasted casts:** no cast was wasted on an end state one cast fewer would have reached.
- **Settling held:** no move broke the 3-round settling without the override's margin.
- **No stacking error:** no cast put a tier on a recipient already holding the other tier of that stat.
- **Moves followed state:** they tracked the fight, such as allies going down, enemies switching targets and summons
  arriving.
- **Decision time:** Lann's two slow rows in v0.3.52 (631 and 689 ms) carry the memory clean-up marker; the 631 ms row
  names tier-1 Strength at 602.6 ms. Lann's one slow row in v0.3.51 (251 ms) names the same ability at 58.6 ms, with
  no clean-up. Tuvel and Orvath account for most slow time in both logs. Invocation scoring is not shown to be a steady
  cost.

## v0.3.51 ledger

| line | fight | mode | round | enemies | buff | recipient | net | gross | engine holder (loss) | sibling (loss) | outcome | class | evidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 271 | 1 | TB | R0 | 12 | Constitution 1 | main character (name key missing) | 1.2 | 2.4 | (unnamed ally) (1.2) |  | Success 0.0 s | move-settled |  |
| 1773 | 4 | RT | RT1 | 10 | Dexterity 1 | Lann | 2.0 | 4.6 | Aeiluvetar (2.5) |  | Success 3.8 s | move-settled |  |
| 1945 | 4 | RT | RT1 | 13 | Strength 1 | Valmot | 1.6 | 5.8 | Tuvel (4.2) |  | Success 0.0 s | move-settled |  |
| 2032 | 4 | RT | RT2 | 11 | Dexterity 1 | Tuvel | 9.6 | 17.6 | Lann (7.9) |  | Success 0.0 s | move-override | placed 1 round(s) ago, net 9.6 |
| 2067 | 4 | RT | RT2 | 9 | Dexterity 2 | Lann | 22.5 | 29.8 | (unnamed ally) (6.1) | Constitution 2 (1.2) | Success 0.0 s | move-settled [sibling] |  |
| 2099 | 4 | RT | RT2 | 9 | Constitution 2 | Bloodragers | 1.2 | 1.2 |  |  | Success 0.0 s | first-placement |  |
| 2487 | 4 | RT | RT5 | 1 | Strength 1 | Tuvel | 1.2 | 1.6 | Valmot (0.4) | Dexterity 1 (0.0) | Success 0.0 s | move-settled [sibling, last-enemy] |  |
| 2648 | 5 | RT | RT1 | 11 | Dexterity 1 | Aeiluvetar | 2.4 | 2.4 |  |  | Interrupt 1.0 s | not-applied | Interrupt after 1.0 s |
| 2843 | 5 | RT | RT1 | 12 | Wisdom 1 | (unnamed ally) | 1.8 | 2.8 | Ulbrig (0.3) | Constitution 1 (0.8) | Success 2.5 s | move-settled [sibling, low-loss] |  |
| 2940 | 5 | RT | RT1 | 12 | Dexterity 1 | Seelah | 11.4 | 11.4 |  |  | Success 0.0 s | first-placement |  |
| 3098 | 5 | RT | RT3 | 9 | Strength 1 | Valmot | 1.2 | 3.9 | Tuvel (2.7) |  | Success 0.0 s | move-settled |  |
| 3132 | 5 | RT | RT3 | 7 | Dexterity 1 | Camellia | 2.0 | 2.0 |  |  | Success 0.0 s | holder-unseen | ledger holder Seelah: last decision RT2 at 50% HP (line 2996) |
| 3203 | 5 | RT | RT4 | 7 | Dexterity 2 | Valmot | 2.2 | 4.0 | Lann (1.9) |  | Success 0.0 s | move-settled |  |
| 3267 | 5 | RT | RT4 | 6 | Dexterity 1 | Lann | 3.3 | 3.3 |  |  | Success 0.0 s | holder-unseen | ledger holder Camellia: last decision RT3 at 58% HP (line 3148) |
| 3674 | 5 | RT | RT10 | 1 | Constitution 2 | Lann | 2.5 | 2.8 | Bloodragers (0.3) |  | Success 0.0 s | move-settled [low-loss, last-enemy] |  |
| 3837 | 6 | TB | R1 | 1 | Constitution 1 | (unnamed ally) | 1.0 | 2.2 | main character (name key missing) (0.6) | Wisdom 1 (0.6) | Success 0.0 s | move-settled [sibling, last-enemy] |  |
| 4166 | 6 | TB | R4 | 3 | Strength 1 | Abrikandilu | 1.2 | 2.8 | Valmot (1.6) |  | Success 0.0 s | move-settled |  |
| 4284 | 6 | TB | R5 | 2 | Strength 1 | Bloodragers | 7.2 | 9.8 | Abrikandilu (1.7) | Charisma 1 (0.9) | Success 0.0 s | move-override [sibling] | placed 1 round(s) ago, net 7.2 |

Sequences:
- cross-tier (fight 4, lines 2032 -> 2067): not redundant: Tuvel held Charisma tier 2, which tier-2 Dexterity on Tuvel would have removed
- cross-tier (fight 5, lines 3203 -> 3267): not redundant: Valmot held Strength tier 1, which tier-1 Dexterity on Valmot would have removed
- chain (fight 4, lines 1773 -> 2032): Dexterity 1 moved Aeiluvetar -> Lann -> Tuvel within one round (move-settled, then move-override; values change between decisions, so not by itself a redundant cast)
- chain (fight 5, lines 3132 -> 3267): Dexterity 1 moved Seelah -> Camellia -> Lann within one round (holder-unseen, then holder-unseen; values change between decisions, so not by itself a redundant cast)
- chain (fight 6, lines 4166 -> 4284): Strength 1 moved Valmot -> Abrikandilu -> Bloodragers within one round (move-settled, then move-override; values change between decisions, so not by itself a redundant cast)
- re-placed (fight 4, lines 2487 -> 2940): Dexterity 1 removed from Tuvel with one enemy or none left (worth 0.0 then), cast again in fight 5 on Seelah
- return (fight 4, lines 2487): Strength/1 back on Tuvel, who held it earlier in fight 4

## v0.3.52 ledger

| line | fight | mode | round | enemies | buff | recipient | net | gross | engine holder (loss) | sibling (loss) | outcome | class | evidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 334 | 1 | TB | R1 | 15 | Dexterity 1 | main character (name key missing) | 6.2 | 13.7 | Aeiluvetar (7.5) |  | Success 0.0 s | move-settled |  |
| 340 | 1 | TB | R1 | 15 | Dexterity 2 | Lann | 4.9 | 11.3 | (unnamed ally) (5.2) | Constitution 2 (1.1) | Success 0.0 s | move-settled [sibling] |  |
| 982 | 1 | TB | R5 | 5 | Strength 1 | Bloodragers | 5.8 | 8.9 | Tuvel (2.3) | Charisma 1 (0.9) | Success 0.0 s | move-settled [sibling] |  |
| 1544 | 2 | RT | RT1 | 12 | Dexterity 1 | Tuvel | 3.8 | 8.4 | main character (name key missing) (4.6) |  | Success 4.7 s | move-settled |  |
| 1768 | 2 | RT | RT1 | 12 | Charisma 1 | Camellia | 1.3 | 1.3 |  |  | Success 0.0 s | first-placement |  |
| 1777 | 2 | RT | RT1 | 12 | Dexterity 2 | Valmot | 7.9 | 17.6 | Lann (9.7) |  | Success 0.0 s | move-settled |  |
| 1788 | 2 | RT | RT2 | 12 | Constitution 2 | Bloodragers | 1.3 | 1.3 |  |  | Success 0.0 s | first-placement |  |
| 2103 | 2 | RT | RT4 | 7 | Dexterity 1 | Lann | 12.5 | 12.6 | Tuvel (0.1) |  | Success 0.0 s | move-settled [low-loss] |  |
| 2115 | 2 | RT | RT4 | 7 | Dexterity 2 | Phirry | 2.0 | 2.2 | Valmot (0.2) |  | Success 0.0 s | move-settled [low-loss] |  |
| 2127 | 2 | RT | RT5 | 7 | Strength 2 | Valmot | 2.2 | 2.2 |  |  | Success 0.0 s | holder-unseen | ledger holder Ulbrig: last decision RT2 at 34% HP (line 1859) |
| 2180 | 2 | RT | RT6 | 4 | Constitution 1 | main character (name key missing) | 1.3 | 2.1 | (unnamed ally) (0.8) |  | Success 0.0 s | move-settled |  |

Sequences: none

Names: "main character (name key missing)" is the unit the log prints as `[unknown key: d2885ae4-…]` (a Magus 20).
"(unnamed ally)" is a player-faction ally whose name is empty. The combat log lists it as a level-6 Warpriest; it is
not in the active party, and AI Control does not drive it.

## Limits

- Two logs: 29 casts in eight fights, one caster. Values are the engine's estimates, read from its rows, not measured
  outcomes.
- The ledger knows a holder only from the engine's rows. The two Intelligence buffs never appear: no candidate had
  positive worth for them, and that check comes before the holder check. Their holders are unknown.
- "Holder unseen" is an inference: the engine priced no holder where the ledger had one. The evidence column gives what
  the log shows (a revive considered, the last decision and HP). It does not prove the holder was unconscious rather
  than out of combat.
- A cross-tier pair is judged on the holdings the ledger knows. A pair whose sibling holder is unknown would read as
  redundant, but none of the two did.
- Whether a different, joint assignment would have scored more overall is not computed. The ledger checks only the
  one-cast alternative for each pair.

## Reproducing

```
python3 -I tools/InvocationLedger/invocation_ledger.py <Log.txt>            # Markdown, as above
python3 -I tools/InvocationLedger/invocation_ledger.py --json <Log.txt>     # every field
python3 -I tools/InvocationLedger/test_invocation_ledger.py                 # 11 tests
```

## Recommendation and next

- **I2 (joint assignment): not warranted** on this evidence. Revisit only if a later ledger shows a redundant pair or a
  move under settle.
- **No runtime change.** A smaller diagnostic gap remains: a unit with an empty name logs as nothing ("stays on  ").
  A display-name fallback in the unique-buff notes would help future ledgers; it is low priority.
- **Next slice: G1, grapple state awareness.** The v0.3.52 logs are its first played evidence:
  - The player switched Grabber on for Ulbrig (row 769).
  - The combat log shows six grapple maneuvers by Ulbrig, five of them successful (rows 1978–5742).
  - In turn-based play the grapple turns were the player's: rows 742–1103 read "AI is off for this character" until
    R6.
  - In real time, Ulbrig made no AI Control decision from RT3 to RT15. Whether the player or the game drove those
    grapples is not established from this log.
  - G1 needs at least one grapple turn with AI Control on, so that the engine's behaviour while grappling, and its
    missing Release, can be seen.
