# Progressive spell-resource policy — audit, design and plan

Status: **implemented in v0.3.7** (operator decisions: lowest *usable* tier; boss bypass from the game flag only).
Implementation map at the end (§I). Revision 2: updated after the operator's *Progressive Reference
Synthesis* (§R below summarises what changed and why). Written against the working tree at `ba81d7c` (mod v0.3.6,
branch `claude/dreamy-pasteur-b9h3rn`, clean). Line numbers refer to that commit.

Legend: **[V]** verified in this repository or in the decompiled game assembly (`Assembly-CSharp`, ILSpy) ·
**[I]** reasonable inference · **[U]** unverified / needs in-game evidence.

---

## 0. Sources and what they could support

| Source | Accessible? | Used for |
|---|---|---|
| This repository (`ba81d7c`) | yes | all implementation facts |
| Decompiled `Assembly-CSharp` (game) | yes | all game-mechanics facts below marked [V] |
| Play logs from this project (v0.3.0-v0.3.5) | yes | current behaviour of the casters (Orvath, Camellia) |
| Nexus page (mod 1228) | **no** (DNS failure from this sandbox) | not used |
| cRPG Bro video | **no** (youtube.com unreachable, no transcript) | not used |
| Reddit guides (Kingmaker) | **no** (fetch blocked) | not used |
| Fextralife "Hard" page | **no** (DNS failure) | not used |
| github.com/seyelive5/CompanionAI_Pathfinder | yes, README only | context: original README mentions roles and "resource conservation" but nothing about spell tiers |

| *Progressive Reference Synthesis* (operator-supplied summary of the above) | yes | strategic/design context only; labelled **[R]** where used |

Nothing below relies on the inaccessible sources directly; where the operator's synthesis of them shaped a decision,
it is marked **[R]** and kept separate from verified facts. Strategy statements (e.g. "save high slots for hard fights") are
the operator's stated intent, not claims from those guides.

The Nexus page describes the *AI Control* fork; this workspace **is** that fork's current source (the operator's
release line, v0.3.x). I did not assume that the Nexus version number matches it.

---

## Phase 1 — Codebase audit and feasibility

### 1.1 Decision pipeline (turn-based, primary)

[V] `GameInterface/CustomBrainPatch.TurnControllerTick_Postfix` → `Core/TurnBased/TurnBasedController.ProcessTurn`
→ `Tactics/TacticalEngine.Decide` (`TacticalEngine.cs:86`) → `TacticalEngine.Execute` (`:1028`) →
`GameInterface/CommandDispatcher.Run`.

Inside `Decide`:

| Stage | Where | What |
|---|---|---|
| Discovery | `GatherAbilities` `:340` | `unit.Abilities` + every spellbook's `GetAllKnownSpells`, `GetAllCustomSpells` (metamagic-applied spells), `GetSpecialSpells(level)`; variant parents expanded with `GetConversions()` |
| Availability | `AddAbilityCandidates` `:388` | `if (!ability.IsAvailable) continue;` — the game's own check (below) |
| Classification | `AbilityProfile.Get` (`Tactics/AbilityProfile.cs`) | what the ability does: damage, heals, buffs, control, removals, summons… per target branch |
| Targeting + value | `EvaluateAbility` `:517`, `ScoreAbility` `:629` | best target per ability, one value in damage-equivalent points; `Offensive = why ∈ {dmg, cc, debuff}` `:596` |
| Filtering | `Decide` `:100` | `usable = candidates.Where(Value > 0 && Affordable && !memory.Failed.Contains(Key))` |
| Selection | `Decide` `:105-110` | best free/swift option if ≥ `MinQuickValue` (2), else best main option if ≥ `MinMainValue` (0.5) |
| Execution | `Execute` `:1028` | builds `UnitUseAbility`; `CombatMemory.RecordCast` `:1066` (anti-spam memory only) |
| Learning | `TurnBasedController` | a candidate key that failed is not retried this turn |

**One candidate per concrete `AbilityData`** (its best target) — so each prepared/metamagic variant is a separate
candidate with its own key. That is exactly the granularity the tier gate needs.

**Narrowest integration point:** between the `usable` filter and the selection at `TacticalEngine.cs:100-110`.
Everything upstream (legality, targeting, immunity/SR, action economy, repeat prevention, utility) has already run;
nothing downstream changes. No Harmony patch is needed.

**Real-time** [V]: `Core/DecisionEngine/UnifiedDecisionEngine.DecideAction` builds `ActionCandidate`s, filters them
(`FilterByActionAvailability`, `:110` / `:179`), scores them (`_scorer.ScoreAll`, `:127` / `:184`) and takes the
highest `HybridFinalScore`. The same gate fits after `ScoreAll`, before the `OrderByDescending`.

### 1.2 Spell/resource state — what the game says

All [V], from `Kingmaker.UnitLogic.Abilities.AbilityData` and `Kingmaker.UnitLogic.Spellbook`:

- **Effective slot level** = `Spellbook.GetSpellLevel(AbilityData)` (`Spellbook.cs:909`):
  `(MagicHackData?.SpellLevel ?? SpellLevelInSpellbook ?? GetMinSpellLevel(bp)) + (MetamagicData?.SpellLevelCost ?? 0)`.
  `AbilityData.SpellLevel` returns exactly this for spellbook spells (`AbilityData.cs:412-446`). It already includes
  metamagic, heightening and favourite-metamagic reductions (`RuleApplyMetamagic`).
- **Spending** — `Spellbook.SpendInternal`: level `< 1` → **returns true without spending anything**; spontaneous
  books decrement `m_SpontaneousSlots[level]`; prepared books consume a matching available `SpellSlot`
  (`spell.Equals(spellSlot.SpellShell)`).
- **Remaining casts** — `Spellbook.GetAvailableForCastSpellCount`: level 0 → **-1 (unlimited)**; prepared: count of
  available memorised slots of that spell (halved for opposition schools/descriptors); spontaneous: remaining
  slots of that level.
- **Availability** — `AbilityData.IsAvailable` = `IsAvailableInSpellbook && IsAvailableForCast && !TemporarilyDisabled`;
  `IsAvailableForCast` → `GetAvailableForCastCount() != 0` (`AbilityCastRateUtils`), caster restrictions,
  stagger/full-round, `CanCast`… This is authoritative and **already** applied by the engine (`:388`).
- **Completely Normal Spell** — `Metamagic.CompletelyNormal` has `DefaultCost() == -1` (`MetamagicHelper.cs:31`) and
  is exempt from the `Math.Max(0, cost)` clamp (`RuleApplyMetamagic.cs:61`). A level-1 spell with it has effective
  level 0 → free and unlimited by the rules above; `Spellbook.IsMemorized` also treats a `SpellLevel < 1`
  CompletelyNormal spell as memorised (`Spellbook.cs:763`). A level-3 spell with it costs a level-2 slot (not free).
  So the rule is "effective level 0", never "it is a Trickster spell".
- **Mythic books** — `Spellbook.IsMythic` (`m_Type != Normal`); mythic spells have their own slots and levels
  (1-10). Merged mythic books (Angel/Lich/etc.) put mythic spells into the class book [I].

**Current mod state**: the engine keeps **no** slot counters; it reads `IsAvailable` every decision. The only
spell-cost logic today is a soft multiplier (`TacticalEngine.cs:900`): levelled spellbook spells (not items) ×
`clamp(0.55 + 0.45·difficulty, 0.55, 1)`. It discourages slots against weapons/cantrips in easy fights but treats a
level-1 and a level-6 spell alike — it never prefers lower tiers. That is the gap Progressive fills.

### 1.3 State lifecycle

[V] Per decision: `TacticalContext` (enemies, allies, action economy, difficulty), all candidates, all values.
Per turn: `TurnMemory` (failed keys, last command). Per combat: `CombatMemory` casts/uses, reset on combat end and
when the round counter goes backwards (`CombatMemory.Sync/ResetCombat`). Per session: `AbilityProfile` cache
(blueprint-keyed, immutable data). Settings: `settings.json` via Newtonsoft (`Settings/ModSettings.cs`).
Nothing is saved into game saves.

Progressive can be **stateless**: the slot level and availability are read from the game at each decision, so
rests, scroll learning, level-ups, respec, save/load and map changes cannot desynchronise it.

### 1.4 Roles and preferred range

[V] `CharacterSettings.Role` ∈ {DPS, Tank, Support} and `RangePreference` ∈ {Mixed, Melee, Ranged}
(`Settings/ModSettings.cs`) weight values (`RoleWeight`, `PreferenceWeight`) and positioning. They are not resource
policies. **Progressive is orthogonal**: a per-character *spell resource policy* (`Standard` | `Progressive`) that
composes with any role/range. No UI trade-off: a third selector in the character detail panel
(`UI/MainUI.DrawCharacterAISettings`, `:406`), next to Role and Range.

### 1.5 Paths Progressive must not touch

Buffs, heals, cleanses/dispels, summons, burn relief, movement, weapon attacks/charges, consumables (scrolls, wands,
potions, thrown), spell-like/supernatural abilities and resource-pool abilities (no `Spellbook`), kineticist blasts,
Smite/Judgement-style marks, the after-combat buff routine, `ConsumablePolicy`. In scope only:
**offensive spellbook spells** — `Kind == Ability`, `Ability.Spellbook != null`, `Ability.SourceItem == null`,
`Offensive == true` (damage, hard/soft control, debuff — existing classification `:596`).

Ambiguous categories, decided explicitly ([R] synthesis §3.4-3.6, §6.1):
- *Hexes, spell-like and supernatural abilities, class features with their own uses*: not spellbook spells → out of
  scope. Their own limits (e.g. a hex usable once per day per target) are enforced by the game's `CanTarget` /
  availability checks, which the engine already applies [I — to confirm in play, T14].
- *Scrolls, wands*: separate resource economy, governed by the existing `ConsumablePolicy` → out of scope.
- *Damage+heal spells* (Cure Wounds on undead enemies): offensive when aimed at an enemy (`dmg`) → in scope.
- *Marks* (Aid Another, Appraise Foe…): not spells or not "debuff" → out of scope.
- *Summons*: out of scope (requirement lists offensive/debuff only).
- *Buff spells that also hurt* (none identified): follow `Offensive`.

### 1.5b Prerequisite gap: area-effect spells are not analysed

[V] `AbilityProfile` follows actions and `BlueprintAbilityReference` fields, but not
`ContextActionSpawnAreaEffect.m_AreaEffect` (`BlueprintAbilityAreaEffectReference`). The area's effects live in its
components: `AbilityAreaEffectRunAction` (`UnitEnter`, `Round`, `UnitMove`, `UnitExit` action lists) and
`AbilityAreaEffectBuff` (`m_Buff`). So Grease, Web, Stinking Cloud, Cloudkill and similar spells are currently seen as
"unverifiable" (once-per-combat fallback), are **not** classified as offensive, and would escape Progressive.
[R] The synthesis uses Grease as its central example of high-value control (§3.1). Extending the walker to area
effects (and their size for friendly-fire) is therefore step 0 of the roadmap; it also improves today's valuation of
these spells in both modes.

### 1.6 Boss/elite data

- [V] `BlueprintUnit` has an `Experience` component (`Kingmaker.Blueprints.Classes.Experience.Experience`) with
  `EncounterType Encounter` ∈ {Mob, **Boss**, QuestMain, QuestNormal, ChallengeMinor, ChallengeMajor, SkillCheck}
  and `int CR`. The game's own code treats `Experience.Encounter == EncounterType.Boss` as "boss unit"
  (`BlueprintUnit.cs:626`).
- [U] **Coverage**: whether Owlcat set this on every story boss is a data question; it cannot be checked without
  blueprint data. Mod-added enemies may not set it.
- [V] `BlueprintUnit.CR` (`:536`), `Progression.CharacterLevel`, HP, conditions — already used by `CombatMath.Threat`.
- [V] The engine already computes an encounter difficulty: total enemy threat ÷ party power
  (`TacticalEngine.cs:200`), clamped to 0.1-2.
- No verified "elite" flag exists.

### 1.7 Performance

[V] `Decide` runs once per action (a few times per turn per AI unit), not per frame; candidate counts are tens to a
few hundred. The gate is O(candidates) with no allocation beyond one small dictionary; `GetSpellLevel` is an
arithmetic lookup. Boss detection is O(enemies) with a `GetComponent<Experience>` per blueprint — cacheable per
blueprint (immutable), invalidated never (blueprints do not change in a session). Estimated cost: negligible against
the existing per-candidate rule evaluations (`RuleCalculateAttackBonus`, context creation).

### 1.8 Settings migration

[V] Newtonsoft fills missing properties with the C# defaults, so a new `SpellPolicy` property defaulting to
`Standard` loads old `settings.json` files unchanged; nothing changes unless the operator opts in.

### 1.9 Compatibility / patch risk

No new Harmony patch. Mods that change metamagic costs or slot rules (Tabletop Tweaks metamagic, Mystical Mayhem)
act through `RuleApplyMetamagic` / `Spellbook` / `AbilityData.ActionType`, which the design reads — so they are
honoured automatically.

### 1.10 Confidence

| Aspect | Confidence | Why |
|---|---|---|
| Architectural fit | **high** | single, already-filtered candidate list per decision in both modes; one-candidate-per-AbilityData |
| Build feasibility | **high** | only verified members (`Spellbook.GetSpellLevel`, `AbilityData.Spellbook/SourceItem/SpellLevel`, `Experience`, `EncounterType`) |
| In-game semantic correctness | **medium** | slot math is game-authoritative; boss-flag coverage, the 25% success floor and area-effect analysis need play-testing |
| Regression risk | **low** | off by default; gate only removes in-scope candidates; existing fallback untouched |

### 1.11 Highest-risk assumptions

1. **Boss flag coverage** [U]: are WotR bosses actually tagged `EncounterType.Boss`? → log it per enemy at combat start.
2. **Viability floor**: the engine's absolute floor (0.5) is not a meaningful-utility test. Revision 2 adds a
   success-chance floor (25%) instead of a relative-value one; tune from logs.
4. **Area-effect spells** are not analysed today (§1.5b) — must be fixed first or they bypass the policy.
3. **Prepared variants**: each memorised metamagic variant is its own `AbilityData`; assumed (from `GetAllCustomSpells`
   and `SpendInternal`'s `spell.Equals(SpellShell)`) to carry its own effective level [I].

---

## Phase 2 — Architecture and algorithm

### 2.1 Product semantics

**Progressive** (per character, off by default): among the character's *offensive spellbook spells*, only the
**lowest effective slot level that has a viable option** competes in the normal decision; higher levels compete
only when every lower level has no viable option, or when the emergency bypass applies to that cast. Everything
else (weapons, cantrips, buffs, heals, items, movement…) competes exactly as today. The final choice is still the
normal best-value choice, so a weapon attack or a buff can still win.

Definitions:
- **Effective level** `L(c)` = `c.Ability.Spellbook.GetSpellLevel(c.Ability)` (game, includes metamagic).
- **Free** = `L(c) == 0` (cantrips, orisons, Completely-Normal level-1 spells): exempt, still scored normally.
- **Resource scope** = the spellbook (`Spellbook` instance): each book is its own ladder (a wizard's level-1 slot
  does not "block" a mythic level-1 slot, nor a second class's book).
- **Available** = already guaranteed: only `IsAvailable` abilities become candidates.
- **Viable** = in the post-filter `usable` list (positive value, affordable now, not failed this turn; legality,
  range/line of sight, immunity, already-active effects and SR immunity already applied) **and**
  `Value ≥ threshold(slot)` (the engine's own `MinQuickValue` / `MinMainValue`) **and**
  `SuccessChance ≥ 0.25` — the probability that the effect lands on its primary target, from the evaluator's own
  terms: spell-resistance pass × failed-save chance (× hit chance for attack-roll spells). **[R]** "prefer an effect
  with a credible chance of working" (synthesis §3.1-3.2, §4). This replaces revision 1's relative factor
  (`ρ · best spell value`), which let a higher spell escalate *because it scored higher* — contrary to the core rule.
  The success chance does not depend on what higher spells exist, so a weak-but-working low spell still blocks a
  stronger high one (that is the point of conserving), while a low spell the target will almost surely shrug off
  (high save, high SR) does not.
- **Gap noted**: the engine's absolute floor `MinMainValue = 0.5` is not a meaningful-utility test; the success-chance
  floor is the conservative addition. 0.25 is a starting value to tune from logs.
- **Exhausted for this decision** = no viable candidate at that level in that book.

### 2.2 Algorithm (inserted in `Decide` after `usable`, before selection)

```
GateProgressive(ctx, usable):
  if ctx.Settings.SpellPolicy != Progressive: return usable
  inScope = usable where Kind==Ability && Ability.Spellbook != null && Ability.SourceItem == null
                      && Offensive && L(c) > 0
  if inScope empty: return usable
  removed = {}
  for each book in inScope.GroupBy(Spellbook):
      viable = book where Value >= Threshold(c.Slot) && SuccessChance(c) >= 0.25
      if viable empty: continue                       // nothing in this book is worth a slot: leave it as is
      floor = min L(c) over viable
      for c in book where L(c) > floor:
          if Bypass(ctx, c): continue                 // emergency: competes normally
          removed += c
  log once per decision if removed non-empty (debug-gated detail: candidates per tier, why a tier was skipped):
      "[Progressive] <unit>: tier L<floor> (<book>) - held back: <labels>"
      "[Progressive] <unit>: L1 skipped (Daze: 12% chance vs Will) -> tier L2"
  return usable - removed
```

Selection then runs unchanged on the gated list. Cases:
- *No level-1 offensive spell available*: no level-1 candidates exist (`IsAvailable` false) → floor is the next level.
- *Level-1 spell illegal / immune / out of range / unaffordable*: it never reached `usable` → not viable → escalate.
- *Level-1 spell legal but unlikely to work* (success chance < 25%, e.g. a Will-save spell vs a high-Will target) or
  below the engine threshold: not viable → escalate.
- *Level-1 spell viable but a weapon attack is better*: higher spells held back; the weapon attack wins → slot conserved.
- *Low-level non-offensive spell*: out of scope, never a floor.
- *Multiple books*: independent ladders.
- *Metamagic*: uses the effective level (Quicken Magic Missile = level 5).
- *Completely Normal / cantrips*: level 0 → exempt.
- *Execution failure*: no state is kept, the next decision re-reads the game; `TurnMemory.Failed` already stops retrying.

### 2.3 Emergency bypass

Evaluated per candidate, per decision, from live state (no stored flag → nothing can leak across encounters,
maps, save/load or modes):

```
Bypass(ctx, c) = setting ProgressiveBossBypass && BossAffected(ctx, c)
    // c's target, or an enemy inside its area, is a qualifying boss

QualifyingBoss(e) = e ∈ ctx.Enemies (alive, conscious, targetable, not stealthed)
                    && e.Blueprint.GetComponent<Experience>()?.Encounter == EncounterType.Boss   // verified game flag
// Missing component / any other value / exception -> not a boss (fail conservatively). [R] synthesis §5.8

ShadowSignals(ctx) (logged only, never bypass in this version):
    - an enemy with blueprint CR >= party average level + 3
    - encounter difficulty (existing metric, TacticalEngine.cs:200) >= 1.25
```

**[R]** The synthesis (§5, §6.6) warns against arbitrary CR/HP/AC thresholds that have not been checked against real
WotR data, and against comparing base CR with difficulty-scaled statistics. Revision 1 used two such thresholds as
live triggers; revision 2 keeps only the verified game flag active and logs the heuristic signals ("would bypass:
CR 9 vs party 5") so that play logs can show whether they would add true positives before anyone enables them.
`BlueprintUnit.CR` is the base CR (unaffected by difficulty) [V]; the party level is character level — the
comparison is only a candidate heuristic, not an equivalence.

- Scope: the bypass only frees spells that would hit the boss — no Fireball on the mooks because a boss is in
  the room.
- Lifecycle: active while a qualifying enemy is a valid hostile in `ctx.Enemies`; ends when it dies, is disabled,
  flees or combat ends — automatically, because nothing is stored.
- It only re-admits candidates to normal scoring; it never forces the highest spell.
- Logging (gated by the existing per-combat memory so it prints on transitions, not every decision):
  `[Progressive] boss bypass ON: <enemy> (Experience=Boss)` / `... OFF: <enemy> no longer a valid target` /
  `[Progressive] would bypass (not active): <enemy> CR 9 vs party 5`. At combat start (debug log) each enemy's
  `Experience.Encounter` and CR are listed — the evidence needed for §1.11-1.

### 2.3b Operator decision required: strict exhaustion vs lowest *usable* tier

[R] Synthesis §6.4: the rule above ("lowest tier with a viable candidate") differs from *literal* exhaustion
("cast every level-1 slot before any level-2 slot, whatever the target"). Strict exhaustion would cast a level-1
spell even when it has almost no chance to work, or wait (weapon/cantrip) instead of escalating; it trades
competence for maximum slot conservation. **Recommendation: lowest usable tier** (default). Strict mode, if wanted,
is a one-line variant (drop the success-chance test and treat any in-scope candidate as viable).

Rejected alternatives: a stored "current tier" per caster (stale after rests/respec/save-load), a parallel
decision engine, Harmony patches on `Spellbook` (unneeded, conflict risk), spell-name lists (fragile, multilingual).

### 2.4 Integration points (all verified unless marked *new*)

| Location | Change |
|---|---|
| `Settings/ModSettings.cs` `CharacterSettings` | *new* `SpellResourcePolicy SpellPolicy = Standard`; *new* enum `SpellResourcePolicy { Standard, Progressive }` |
| `Settings/ModSettings.cs` `ModSettings` | *new* `bool ProgressiveBossBypass = true` |
| `Settings/ModSettings.cs` `Localization` | EN/KR strings for the selector and descriptions |
| `UI/MainUI.DrawCharacterAISettings` (`:406`) | *new* `DrawSpellPolicySelection()` beside Role/Range; global bypass toggle in the combat settings section |
| `Tactics/AbilityProfile.cs` (walker) | follow `ContextActionSpawnAreaEffect.AreaEffect` into `AbilityAreaEffectRunAction` lists and `AbilityAreaEffectBuff`; area size for AoE radius (step 0) |
| `Tactics/TacticalEngine.ScoreAbility` / `Candidate` | *new* `Candidate.SuccessChance` (primary target: SR pass × failed save × hit chance — terms already computed in `ScoreAbility`) |
| *new* `Tactics/SpellTierPolicy.cs` | pure gate over plain records `(key, bookId, level, inScope, value, threshold, bypass)` → set of keys to hold back. No game types → unit-testable |
| `Tactics/TacticalEngine.Decide` (`:100-110`) | build the records from `usable`, call the policy, drop held-back keys, log |
| *new* `Tactics/ThreatAssessment.cs` | boss detection from `Experience.Encounter` (cached per blueprint); shadow signals (CR, difficulty) logged only |
| `Core/DecisionEngine/UnifiedDecisionEngine.DecideAction` (`:184`) | same policy after `ScoreAll` (real-time); in-scope = ability candidates of attack/debuff type from a spellbook |
| *new* `tests/CompanionAI.Tests` | xUnit project (net8) referencing only `SpellTierPolicy.cs` |
| `docs/TACTICAL_AI.md`, README | behaviour and settings |

---

## Phase 3 — Implementation roadmap and verification

### 3.1 Steps

0. **Area-effect analysis** in `AbilityProfile` (prerequisite). *Done when* `[Profile]` lines show Grease/Web
   with their buffs/conditions and these spells score as control (`cc`) in the decision log; out-of-Progressive
   behaviour change is intended and noted in the changelog.
1. **Pure policy** `Tactics/SpellTierPolicy.cs` + unit tests. *Done when* tests T1-T5 (data-level) pass with
   `dotnet test`. Failure mode: grouping/floor bugs — covered by tests.
2. **Settings + localisation + UI**. *Done when* the mod builds, old `settings.json` loads with `Standard`, the
   selector persists (T11).
3. **Turn-based integration** in `Decide`. *Done when* builds, SigDump shows only verified new references, a debug
   log shows `[Progressive]` lines (T1-T7 in game).
4. **Boss bypass (flag only) + shadow signals + transition logging**. *Done when* combat-start log lists, per enemy,
   `Experience.Encounter` and CR — the coverage evidence for §1.11-1 (T8-T9) — and shadow signals log "would bypass".
5. **Real-time integration** (shared policy). *Done when* T12 passes.
6. **Docs, version, package.**

### 3.2 Test matrix

Automated (unit, pure policy — runnable here):

| ID | Case | Expected |
|---|---|---|
| T1 | L1 (8), L2 (20), L3 (40), weapon (10) — one book, no bypass | L2/L3 held back; final pick weapon or L1 by value |
| T2 | no L1 candidates; L2 (20), L3 (40) | floor L2; L3 held |
| T3 | L1 success chance 0.12; L3 0.7 | L1 not viable → floor L3; nothing held |
| T3b | L1 success chance 0.6 but value far below L3 | L1 viable → L3 held (no escalation on score alone) |
| T4 | L0 cantrip + L1 + L3 | cantrip never held, never a floor |
| T5 | Completely-Normal L1 (effective 0) + L2 | treated as level 0 |
| T6 | two books each L1+L3 | each book keeps its own L1 floor |
| T7 | out-of-scope (buff, heal, item, SLA) at any level | never held |
| T8 | bypass flag on L3 candidate | L3 kept |
| T9 | strict-mode variant (if chosen) | L1 kept as floor even at 0.12 chance |

In-game (proposed, not run — no game runtime here), with debug logging on:

| ID | Setup | Evidence |
|---|---|---|
| T1-T3 | Progressive on a wizard/sorcerer with L1-L3 damage/control spells vs. trash | `[Progressive] tier L1 … held back: Fireball` until L1 slots spent; then `tier L2` |
| T5 | Trickster with Completely Normal Magic Missile | it is never held back and never forms a floor |
| T6 | prepared (Wizard/Cleric) and spontaneous (Sorcerer/Oracle), plus a mythic book | separate ladders in log |
| T7 | Camellia (Shaman) buffs/heals/Spirit Weapon; Lann/Seelah unchanged | decisions identical to v0.3.6 for out-of-scope actions |
| T8 | a story boss fight | `boss bypass ON: <boss> (Experience=Boss)` — or `CR` heuristic — and a higher spell aimed at the boss allowed |
| T9 | trash fight after a boss / reload mid-fight / switch TB↔RT | no bypass; `OFF` logged at the boss's death |
| T10 | interrupt a cast (AoO) | next decision re-reads slots; no stale tier |
| T11 | start with an old settings.json | all characters `Standard`; toggling persists |
| T12 | same scenarios in real time | same `[Progressive]` lines |
| T14 | witch/shaman hex usable once per target per day | hex not offered on an already-hexed target (game restriction), unaffected by Progressive |
| T15 | Grease/Web after step 0 | classified `cc`, gated like other levelled offensive spells |
| T16 | toggle Progressive mid-campaign; save/load mid-fight | takes effect next decision; nothing stale |
| T13 | 10+ enemies, long fight | decision time unchanged in practice (log timestamps); no growth in caches (blueprint-keyed only) |

### 3.3 Regression control

Off by default; the gate only removes in-scope candidates; out-of-scope paths are not edited; logging is
transition-based or behind `EnableDebugLogging`; SigDump/compile check every step; each in-game claim reported as
run/not run.

---

## Final recommendation

**Go.** The feature fits the existing pipeline at one point per combat mode, needs no Harmony patch and no
persistent state, and the slot arithmetic can be taken entirely from the game (`Spellbook.GetSpellLevel`,
`IsAvailable`), which already handles metamagic, Completely Normal Spell, prepared/spontaneous and mythic books.

Minimal design: per-character `Standard | Progressive` setting; area-effect analysis first; a pure, unit-tested
tier gate on the already-filtered candidate list with a success-chance viability floor; a per-cast boss bypass from
the game's `Experience.Encounter == Boss` flag only (CR/difficulty signals logged, not active); all derived from live
state each decision.

Open uncertainties: boss-flag coverage in real encounters (measure with the combat-start log), the 25% success
floor (tune from logs), whether the shadow CR/difficulty signals deserve activation, and in-game confirmation for
prepared metamagic variants and hex restrictions. Operator decision pending: lowest-usable-tier (recommended) vs
strict exhaustion (§2.3b). None of these block implementation; all are observable in the debug log.

---

## §R — Revision 2: what the Reference Synthesis changed

| Synthesis point | Revision 1 | Revision 2 |
|---|---|---|
| §3.1-3.2, §4: value is not raw damage; prefer effects with a credible chance of working | viability = engine threshold + 30 % of best spell value | viability = engine threshold + **success chance ≥ 25 %** (SR × save × hit); no score-based escalation |
| §3.1: Grease-type control is central | area spells not considered | **area-effect analysis added as step 0** (verified gap) |
| §5, §6.6: no invented CR/HP thresholds; fail conservatively | boss flag **or** CR ≥ party+3 **or** difficulty ≥ 1.25 | **boss flag only**; CR/difficulty logged as "would bypass" for evidence |
| §3.5, §6.5: unlimited ≠ unrestricted; hexes have per-target limits | hexes implicitly out of scope | explicit: out of scope; game restrictions still apply; test T14 |
| §3.6: scrolls are a separate economy | out of scope | unchanged, stated explicitly |
| §6.4: strict exhaustion is an explicit choice | not discussed | §2.3b: operator decision, lowest-usable-tier recommended |
| §7: logging of tier, per-tier counts, skip reasons, bypass transitions | basic line | skip reasons and transitions logged; detail behind debug setting |
| §7 tests 4, 13 | — | T14 (hex restriction), T16 (toggle, save/load) added |

---

## §I — Implementation (v0.3.7)

| Step | Files | State |
|---|---|---|
| 0 Area-effect analysis | `Tactics/AbilityProfile.cs` (`WalkAreaEffect`), `Tactics/TacticalEngine.cs` (control friendly fire) | done |
| 1 Pure tier rule + tests | `Tactics/SpellTierPolicy.cs`, `tests/CompanionAI.Tests` (15 tests: T1-T8 + edge cases) | done, `dotnet test` passes |
| 2 Settings/UI | `Settings/ModSettings.cs` (`SpellResourcePolicy`, `CharacterSettings.SpellPolicy`, `ModSettings.ProgressiveBossBypass`, EN/KR strings), `UI/MainUI.cs` (`DrawSpellPolicySelection`, bypass checkbox) | done |
| 3 Turn-based | `Tactics/TacticalEngine.cs` (`Candidate.SuccessChance`, `ApplyProgressive` in `Decide`), `Tactics/CombatMath.cs` (`EffectChance`, shared) | done |
| 4 Boss bypass + logs | `Tactics/ThreatAssessment.cs`, `Tactics/ProgressiveGate.cs`, `Tactics/CombatMemory.cs` | done |
| 5 Real-time | `Core/DecisionEngine/UnifiedDecisionEngine.cs` (`ApplyProgressive`, threshold = end-turn score) | done |
| 6 Docs/package | README, CLAUDE.md, this file, `docs/TACTICAL_AI.md` §12 | done |

### §I.1 Review fixes (before release)

An independent review of the first implementation found these; all are fixed in the released 0.3.7:

| # | Finding | Fix |
|---|---|---|
| 1 | Variant spells (Command, Bestow Curse...) read level −1 from `GetSpellLevel` and escaped the gate | `ProgressiveGate.EffectiveSlotLevel`: a variant spends its parent's slot (`ConvertedFrom`); a held touch charge (`StickyTouch`, set only by `UnitPartTouch`) has already spent its slot: 0 |
| 2 | Control-area friendly fire counted every ally in the radius | only allies the effect would reach (per-target conditions, CC immunity, already affected); Selective metamagic spares allies (the game skips them for every area type), damage areas included |
| 3 | `AbilityAreaEffectBuff.Condition` was ignored | it guards the area buff like a `Conditional` |
| 4 | Area `Size` was used as a radius for every shape; the last area decided the targets | the blueprint's own `AoERadius` first; else `Size` for a cylinder, `Size/2` for a wall, nothing for "all area"; ally/enemy reach is the union over areas |
| 5 | Turn-based kept only the highest-value target per spell, so a viable target or a boss-hitting one could be lost to the gate | with Progressive on, also the best target with ≥ 25 % chance and the best boss-hitting one are candidates |
| 6 | Area damage counted on enter, every round and on movement | per-round damage only (on-enter when there is no per-round damage); on-move damage depends on the target and is not counted |
| 7 | Buffs applied by an area counted as long / persistent / day-long | an area's buffs last while the area does |
| 8 | Logging: repeated lines, boss OFF without a name, boss transitions from distance-filtered real-time lists | outcome-based dedupe, named OFF, transitions logged only from turn-based decisions; log caches are bounded |
| 9 | Real-time threshold | documented: the end-turn score (0), so any positive score is "worth it" there |
| 10 | Self-centred offensive areas used chance 1 | chance = best chance among enemies inside the area (0 if none) |
| 11 | Success chance computed for every candidate | only with Progressive on |
| 12 | Same spell with different metamagic merged by the ability dedupe | metamagic mask and heighten level are part of the dedupe key and the candidate key |
| 13 | A throw in the gate would abort the decision | the turn-based call is wrapped; the gate itself never throws (nothing held back on error); unused `SpawnsArea` removed |

In-game tests T1-T16 (§3.2) have **not** been run — there is no game runtime here. Log lines to look for:
`[Progressive] <unit> (TB|RT): <book>: tier L1 (lower not viable, still selectable: ...) - held back: ...` (before
0.3.12: "lower skipped"),
`[Progressive] boss bypass ON/OFF`, `[Progressive] would bypass (not active): ...`, and with debug logging on,
`[Progressive] enemy <name>: Encounter=<type>, CR=<n>` once per enemy per combat.

## §A — Expansion audit (v0.3.11) and what v0.3.12 changed

The full audit (scope per category, coverage taxonomy and gaps, expansion matrix, architecture options, roadmap, test
matrix) was delivered as a separate report. Its conclusion: keep Progressive spell-slot-specific; fix classification
and diagnostics first; a policy for limited-use non-spell abilities (per-day resources, pools) is a separate, opt-in
policy to be prototyped in shadow mode. Buffs, heals, cleanses, toggles, movement and weapons get no tier gate.

Two questions the audit settled:

- **Viability floor vs selectable options.** A lower tier that is not viable does not set the floor but is never held
  back: the gate only removes levels above the floor, so the choice is never worse than Standard and a cheap spell that
  still wins on score spends the cheaper slot. Intended; the log now says "lower not viable, still selectable" and a
  test (`LowerNonViableTiersAreNeverHeld`) pins it.
- **Ladder key.** A unit has one `Spellbook` per blueprint (`UnitDescriptor.m_Spellbooks`), but the key was the
  blueprint *name*; two different books with the same name would have merged. 0.3.12 keys by the blueprint's
  `AssetGuid` and keeps the name for the log (`BooksWithTheSameNameButDifferentKeysAreSeparateLadders`). Merged mythic
  spells live in the class book and share its slots, so one ladder is correct for them.

v0.3.12 scope corrections that affect Progressive:

| Change | Effect on Progressive |
|---|---|
| A buff that is flagged harmful or imposes a controlling condition (`AddCondition`, `BuffStatusCondition`) on its target makes the ability harmful (`AbilityProfile.HarmfulEffects`), unless the ability is flagged helpful to allies or is personal | Spells like Entangle are aimed at enemies, valued as control, and so become **in scope** (they were "not offensive") |
| "Offensive" is the exact reason tokens `dmg`, `cc`, `debuff` (`TacticalEngine.IsOffensive`), not substrings | none for current reasons; no accidental matches later |
| `HasCCImmunity` tests each CC flag (spells carry `Sleep \| MindAffecting` etc.) | immune targets no longer offer viable control options |

