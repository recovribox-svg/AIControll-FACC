# Active-ability intelligence: audit and plan (analysis only)

This document covers how the AI discovers, understands, values and uses the active abilities a character has, and
what to change. It follows the "Active Ability Intelligence" master task prompt and is analysis and planning only: no
behaviour was changed for it. The separate tuning plan (`docs/AI_TUNING_AUDIT.md`) and its phase 0 diagnostics build
(0.3.17-test2) are referred to where they overlap.

## Evidence base and labels

**Code.** Repository branch `claude/dreamy-pasteur-b9h3rn`, `Info.json` version 0.3.17 (work in progress after the
0.3.16 release), last commit ed7d098. Build and tests pass (31/31). Three read-only audits checked the code; their
full reports are in `docs/audit/ACTIVE_ABILITY_AUDIT_REPORTS.md`, and their conclusions are condensed here:

- the mod's lifecycle for every ability kind;
- the game's mechanics, from the decompiled Assembly-CSharp;
- the content added by the other mods.

**Logs.** The user's 0.3.16 turn-based log (06aebf2d) and the seven earlier logs.

**Other mods.** Source of Tome of the Firebird 1.4.18 and Mystical Mayhem 0.7.0h, plus decompiles of the Isekai mod
and Tabletop Tweaks Core/Base.

**Video.** The WolfheartFPS transcript, used for player-facing intent only.

**Limits:**

- There was no in-game run. Every behaviour claim below comes from code and logs.
- The fextralife wiki could not be reached from the sandbox, so web evidence is weak and marked as such.
- Blueprint data (which components a specific ability has) is not in the code. Where it matters, it is marked
  **[BP?]**, and the plan obtains it at runtime instead of guessing (see AA-0).

Labels used: **confirmed**, **strongly supported**, **hypothesis**, **unknown**, **intentionally unsupported**.

---

## A. Executive summary

### What the AI does today

- Abilities (`Ability` / `AbilityData` used with `UnitUseAbility`) go through one good pipeline:
  - discovery restricted to what the action bar offers;
  - the game's own legality checks (`IsAvailable`, `CanTarget`);
  - effect analysis from blueprint components (`AbilityProfile`);
  - expected-value scoring against weapon attacks and movement;
  - execution through `CommandDispatcher`;
  - outcome checks (`CastOutcome`).

  Damage, heal, buff, control, summon, cleanse, dispel, enchant, Charge, Coup de Grace, marks such as Smite and touch
  spells are all modelled. This works in both turn-based and real-time, because both run the same engine.

### The main limitations, by cause rather than by ability name

1. **Toggles do not exist for the AI.** It never reads, values, switches on or switches off an `ActivatableAbility`.
   - **Covers:** Power Attack, Deadly Aim, Piranha Strike, Fighting Defensively, Combat Expertise, Acrobatics
     (Mobility), Rage, Bloodrage, bardic performances, Raging Song, Inspire Rage "accept", Spell Combat, kineticist
     infusions, judgments, weapon-bond properties, metamagic rods, stances and aspects.
   - **What happens:** whatever the player left on stays on, and whatever was off stays off.
   - **Performances in particular:** they start through a game command, and the game stops them at the end of each
     fight (`UnitActivateAbility`, `Stop()`). So a bard's or skald's song is off in every AI fight unless the player
     starts it by hand.
   - **Label:** confirmed (code); the performance part is strongly supported.
2. **Some kinds of option never reach evaluation.**
   - Spells aimed only at a point that are not harmful get no target at all. That likely includes vanilla Summon
     Monster.
   - Spontaneous conversions are dropped: a cleric's cure or inflict conversion, a druid's summon conversion, shadow
     spells. Only the variants of variant spells are kept.
   - **Label:** confirmed (code); the summon part is strongly supported.
3. **Effects the profile cannot read fall to an "unknown, once per combat, worth 1" fallback.** Custom damage
   and kill actions from other mods are invisible in the same way. The game's own damage-preview interface
   (`IDealDamageProvider`) can close most of that gap generically (section F).
   - **Covers:** Vital Strike, Cleave, combat maneuvers, teleports, Demoralize's debuff, custom-logic abilities, and
     most custom components from other mods.
   - Demoralize and Dazzling Display are blacklisted outright, and the blacklist also hits some class touch powers by
     mistake.
   - **Label:** confirmed.
4. **Conditions the profile cannot evaluate are counted in both branches.**
   - This is how Sleep came to be cast on targets above its Hit Dice limit (log 06aebf2d).
   - It is the same layer that decides whether an ability does anything to a given target, so it matters for every
     spell with a Hit Dice, creature-type or alignment rule.
   - **Label:** confirmed.
5. **Swift and free options need a value of at least 2 to be chosen.** Small but real swift options are never taken.
   - **Label:** confirmed. It is a deliberate guard against spam, so it is kept but refined (AA-3).

### What should not change

- The selection model (one value scale, best option wins).
- The legality checks (the game's own).
- The Progressive spell policy and the area and danger handling.
- "Use the game's own rule functions": several toggles' effects already reach the estimates correctly, because
  `EstimateWeaponAttack` asks the game's attack, AC and weapon rules, which include active buffs.

The work needed is to:

- let the AI see and control toggle *state*;
- close the discovery gaps;
- read more conditions;
- give the most common unread effects a model.

It is not new parallel scoring systems.

### Recommended order

| Step | Content | Behaviour change |
|---|---|---|
| **AA-0** | Runtime inventory and coverage diagnostics | none |
| **AA-1** | Foundations: conditions (Hit Dice with shared values, a few common predicates), point-target summons, conversions, blacklist corrections, per-target "reached nothing" safety net | yes |
| **AA-2** | Toggles that cost an action and help the party (performances, Rage family): candidates that compete like any other option | yes |
| **AA-3** | Attack-mode toggles (Power Attack, Deadly Aim, Piranha Strike, Fighting Defensively, Combat Expertise) decided per attack; Mobility as a movement modifier | yes |
| **AA-4** | Long tail, each on its own evidence: Vital Strike and Cleave, Demoralize, maneuvers, metamagic rods, kineticist infusions, mod content | yes |

**Never changed by the AI:**

- Acceptance preferences (Inspire Rage "accept effect"). They are the player's build choice and are saved.
- Toggles whose effect the profile cannot read.

### What was deliberately not done

Nothing was implemented for this task (analysis only).

### Key validation limits

- Blueprint facts marked [BP?] must be read at runtime: AA-0 adds that dump.
- None of the proposals has been seen in a game yet.

---

## B. Architecture map (live paths)

```
Discovery   Tactics/AbilityDiscovery.Gather (per decision)
              unit.Abilities (class/feat/item abilities, held touch charges) + each spellbook's known, custom (metamagic)
              and special spells; leaves out Hidden / !IsVisible (= action bar), kinetic blade internals, variant
              parents (replaced by their visible variants). Other GetConversions() results: dropped.
            unit.ActivatableAbilities: read only to find kinetic blade internals. Nothing else.
Filters     IsAvailable (game) -> AbilityClassifier.IsBlacklistedForCombat -> ConsumablePolicy / ConsumableFactor
            -> target sides by intent (TacticalEngine.EvaluateAbility) -> AbilityProfile.TargetOn / CanTarget -> range
            / approach -> ScoreAbility > 0 -> Decide: Affordable, memory.Failed, CastOutcome.IsBlocked, Progressive,
            CoupleOwnAttackMarks -> quick >= 2 / main >= 0.5
Effect      Tactics/AbilityProfile (cached per blueprint): RunAction, sticky touch delivery, nested action lists,
model       referenced abilities, spawned areas, AbilityTargetsAround; Conditional guards evaluated per target when the
            condition is in a safe list, otherwise both branches counted
Value       TacticalEngine.ScoreAbility: damage / heal / control / buff / cleanse / summon / enchant / dispel / burn /
            utility fallback; CombatMath (game rules: attack, AC, weapon stats, saves, SR, concentration, UMD)
Special     weapon attacks (UnitAttack), Move+Attack, Charge (the game's ability), Coup de Grace (the game's ability),
            5-foot step / withdraw / leave area / advance / stand up (UnitMoveTo)
Execute     TacticalEngine.Execute -> GameInterface/CommandDispatcher.Run -> CommandTrace; CastOutcome.Begin
Feedback    TacticalDriver.LearnFromLastCommand -> TurnMemory.Failed; CombatMemory (casts, uses); CastOutcome (silent
            failures, held charges); DamageObserver / SaveObserver (observed resistances and saves)
Modes       TB: TurnBasedController -> engine. RT: RealTimeController (own AoE escape, stuck check, cannot-act filter)
            -> RealTimeTactics.Act -> same engine. Legacy RT engine (setting): same discovery, own scoring, no toggles.
Out of      RealTimeController.ApplyPermanentBuffsOutOfCombat and PreBuffController: unit.Abilities.Visible only (no
combat      spellbook spells), ignore the control settings; PreBuff bypasses the dispatcher
```

### Turn-based vs real time

The two modes share discovery, profile, value and execution. Real time differs in four ways:

1. **Actions.** Real-time "actions left" come from the game's cooldowns, and Move-slot abilities always count as
   affordable.
2. **Movement.** There is no 5-foot step; the unit backs off instead.
3. **Failure memory.** A round is 6 seconds, so the failure memory lasts 6 s.
4. **Steps before the engine.** RealTimeController runs its own AoE escape, stuck check and cannot-act filter before
   the engine.

---

## C. Coverage matrix

**Status values:**

- **OK:** supported and evaluated.
- **Partly:** supported, but the evaluation is incomplete.
- **Unread:** discovered, but the effect is not understood.
- **Unseen:** never reaches evaluation.
- **None:** no support.
- **Engine:** the game applies it automatically, and the estimates already include it.
- **Pref:** a player preference.

**Handling values:**

- **Select:** chosen as an action.
- **Toggle:** a state decision.
- **Modifier:** decided together with another action.
- **Leave:** left to the engine or the player.

### General actions

| Family | Status now | Correct handling | Evidence | Plan | Priority |
|---|---|---|---|---|---|
| Weapon attacks (full / single / Move+Attack) | OK (game rules) | Select | confirmed | B1 area-aware approach (tuning plan) | – |
| Charge | OK, offered only when a normal move cannot reach; the game's ability | Select | confirmed (`AbilityCustomCharge`) | Pounce: the charge is a full attack (`Features.Pounce`); the value should use the full-attack estimate; checked against the real line | AA-4, small |
| Coup de Grace | OK (helpless via `CanTarget`; value by the game's crit and Fort rule) | Select | confirmed; TTT makes it provoke | Price the provoke when the profile sees `ContextActionProvokeAttackOfOpportunity` on the caster (TTT), with the attacker model of `CastProvokeRisk` | AA-1, small |
| Treat Affliction (Lore Religion) | Partly: profiled as removal or dispel, valued only when there is something to remove | Select | strongly supported; DC, limit and conditions [BP?] | AA-0 dump, then check that the cleanse value applies to poison and disease only, and that the once-per-day marker is respected (the game's `CanTarget`) | AA-1 |
| Demoralize, Dazzling Display | Unseen (blacklisted) | Select | confirmed (`Demoralize.cs`: DC 10 + target level + Wis bonus; Shaken 1 + (roll − DC)/5 rounds; repeat extends) | Unblacklist; model it as a debuff with the skill-check chance and the debuff buff read from the action's `Buff` field; Dazzling Display as an area version (full-round) | AA-4 |
| Cornugon Smash, Dreadful Carnage, Shatter Defenses | Engine (automatic triggers) | Leave; optionally add the expected Shaken to attack values | strongly supported (features, no commands) | none now; value only if logs show it matters | – |
| Fighting Defensively | None (toggle) | Modifier | confirmed; AC +2 (+3 with 3 Mobility ranks), attack −4; when the AC applies depends on `ActivationType` [BP?] | AA-3 | B |
| Acrobatics / Mobility mode | None (toggle) | Modifier of movement | confirmed: halves speed; Mobility vs the attacker's CMD per attack of opportunity on disengage. **The "two concentration checks" claim is wrong**: only with an extra fact | AA-3 | B |
| Combat maneuvers (trip, disarm, sunder, bull rush, dirty trick) | Unread (utility) | Select | confirmed (`RuleCombatManeuver`: d20 + CMB vs CMD; the Greater feats make the target provoke) | AA-4: model the condition applied, with the maneuver chance from CMB and CMD | C |
| Cleave, Great Cleave | Unread (utility) | Select | confirmed (`AbilityCustomCleave`: primary target, then enemies in reach by distance, stop at the first miss; Cleave caps at 2) | AA-4: value = the first attack + the chance-weighted next attacks | C |
| Cleaving Finish | Engine (on kill, once per round) | Leave | confirmed | – | – |
| Vital Strike family | Unread (utility) | Select | confirmed (custom logic) | AA-4: single attack with extra weapon dice, against the full attack | C |
| 5-foot step, withdraw, stand up | OK (emulated with moves; the game applies the rules) | Select | confirmed | – | – |
| Aid Another | OK (as a mark or debuff) | Select | log | – | – |

### Toggles and stances

| Family | Status now | Correct handling | Evidence | Plan | Priority |
|---|---|---|---|---|---|
| Power Attack, Deadly Aim, Piranha Strike (group `PowerAttack`) | Engine reflects their state; never switched | Modifier (per attack) | confirmed (`WeaponParametersAttackBonus` / `DamageBonus`, the 1 + BAB/4 scaling, two-handed and secondary-attack factors) | AA-3 | B |
| Combat Expertise, style stances (`CombatStyle`, `StyleStrike`) | None | Modifier, when the effect is readable | strongly supported | AA-3 (Combat Expertise with Fighting Defensively); styles AA-4 | C |
| Rage, Bloodrage, Demon Rage (group `Rage`, barbarian stances) | None | Toggle candidate (costs its activation action; resource rounds) | confirmed (mechanics); action type, resource and spell restriction [BP?] | AA-2 | A/B |
| Bardic performance, Raging Song, Azata performances | None; off in every fight unless the player starts them | Toggle candidate | strongly supported (`UnitActivateAbility`, Standard → Move → Swift with Quicken Performance) | AA-2 | A |
| Inspire Rage "accept effect" | Pref (never touched) | Leave (player's choice, saved) | confirmed (saved `m_IsOn`); default [BP?] | Intentionally unsupported. Diagnostic only: note at fight start when a caster accepts while a skald is in the party | – |
| Spell Combat, Spellstrike (Magus) | None | Toggle, set once per fight from build and opponents | unknown interplay with a plain cast | Investigate (AA-0 dump; a Magus test) before any change | C |
| Kineticist infusions (`FormInfusion`, `SubstanceInfusion`), Gather Power | None (Gather Power was misused in 0.3.14, then removed) | Modifier of a blast (burn cost) | confirmed (groups) | AA-4, after a value model for burn | C |
| Judgments, weapon-bond / arcane / sacred properties, aspects, Wings | None | Toggle, mostly "on for the fight" | strongly supported | AA-2 framework covers the readable ones (buff value); the rest stay as the player set them | C |
| Metamagic rods (`MetamagicRod`) | None (a rod left on is spent by the next eligible spell; hypothesis) | Modifier of a cast | confirmed (`MetamagicRodMechanics`) | AA-4 | C |

### Spells and spell modifiers

| Family | Status now | Correct handling | Evidence | Plan | Priority |
|---|---|---|---|---|---|
| Prepared and custom metamagic spells | OK (seen as separate spells; Empower, Maximize, Quicken, Selective and Reach modelled) | Select | confirmed | Extend and Persistent in the value (duration ×2, two saves) | AA-4 |
| Spontaneous conversions (cure, inflict, summon nature's ally), shadow spells | Unseen (dropped) | Select | confirmed (`AbilityDiscovery`) | AA-1: offer visible conversions, keyed by the slot they spend | A |
| Point-only helpful spells (Summon Monster) | Unseen (no target side) | Select | strongly supported (`EvaluateAbility` target sides) | AA-1: aim them at a point (nearest the fight, within range) | A |
| Hit Dice, creature-type and alignment conditions | Partly (plain Hit Dice only; shared-value Hit Dice and others count both branches) | – | confirmed (Sleep, log 06aebf2d) | AA-1 (= tuning addendum L3a) | A |

### Class and mythic actions

| Family | Status now | Correct handling | Evidence | Plan | Priority |
|---|---|---|---|---|---|
| Smite Evil, Smite Chaos, marks | OK (kept only on the attacked enemy) | Select (coupled) | confirmed | – | – |
| Mark of Justice, Lay on Hands, Mercy | Lay on Hands OK (per-target condition); Mark of Justice [BP?] | Select | partly | AA-0 dump | – |
| Mythic abilities (Demonic Art summons, Light of the Angels, Arcane Reservoir, All According to Plan…) | OK when their components are readable (seen in the logs) | Select | log | AA-0 coverage report lists the unread ones | – |
| Channel energy, hexes, domain powers | OK when readable (damage, heal, buff) | Select | strongly supported | AA-0 | – |

### Out-of-combat routines

| Family | Status now | Correct handling | Evidence | Plan | Priority |
|---|---|---|---|---|---|
| Long buffs after combat, PreBuff button | Partly: class abilities only, ignore the control settings | Select | confirmed | Tuning plan B11; spellbook spells for the button | (tuning) |

### Coverage summary

**Method.** Source and call-graph tracing of the mod; the game's decompiled rules; inventories of the four mods'
sources; eight playtest logs. There was no runtime enumeration yet: AA-0 adds it, and it is the continuing
discovery mechanism.

**Counts.** 32 vanilla families inspected, plus 12 cross-mod gap types:

| Status | Families |
|---|---|
| OK | 9 |
| Partly | 4 |
| Unread | 3 |
| Unseen | 3 |
| None | 10 (all toggles, Fighting Defensively and Mobility included) |
| Engine | 2 |
| Pref | 1 |

**Not represented in tests.** No log yet contains:

- a bard, skald, barbarian, bloodrager or magus;
- a kineticist using infusions;
- an Isekai character using its overpowered abilities;
- a Mystical Mayhem warlock.

Their status is from code only.

---

## D. Research and mechanics: claims against the game

| Claim (source) | Game code | Verdict |
|---|---|---|
| Charge: full round, at least 10 ft, clear path, 2× speed, +2 attack / −2 AC (video, taxonomy) | `AbilityCustomCharge`: straight two-point path, speed ×2, range = speed × 6 s; minimum 10 ft + corpulences (RT), or a step + weapon minimum (TB); navmesh trace clear; other units checked **only at the end spot**; in TB not after moving this turn; Charge buff for 1 round (contents [BP?]); a single attack unless Pounce | Mostly right. "Path must be free of units" is only checked at the end. The real-time wait is not specific to Charge: every command waits for the unit's turn in the initiative cycle. |
| "Always charge round 1 at distant casters" (taxonomy) | – | Rejected as a rule. The engine already compares Charge against Move+Attack, ranged options and risk. |
| Coup de Grace: full round, helpless target, automatic crit, Fort DC 10 + damage or die, provokes (video) | Helpless = `UnitState.IsHelpless`: unconscious, sleeping, or paralyzed / petrified. **Stunned, dazed, prone and entangled are not helpless.** Vanilla Coup de Grace probably does not provoke; TTT adds the provoke as a fix | The video is right about helpless. The taxonomy's "Stunned" is wrong. The provoke depends on TTT. |
| Treat Affliction: Lore (Religion) vs the affliction DC, once per day per recipient (video) | A skill ability (`LoreReligionUseAbility`); the mod profiles it as a removal | The once-per-day limit is plausible (marker buff) but [BP?]. |
| Demoralize: standard, DC 10 + HD + Wis, Shaken 1 + 1 per 5, repeats extend, 30 ft, no SR (video) | `Demoralize.cs`: DC 10 + **character level** + Wis **bonus**; duration 1 + (roll − DC)/5; repeating increases the duration; Frightening Thug and Hellknight upgrades to Frightened | Confirmed apart from range and SR, which are [BP?] (ability blueprint). |
| Demoralize as a swift action with Cornugon Smash / Dazzling Display (taxonomy) | Dazzling Display has its own abilities (an action, move, standard and swift version exist). Cornugon Smash and Dreadful Carnage are features | The taxonomy is wrong as written; the prompt's warning is right. |
| Fighting Defensively: +2 dodge (+3 with 3 Mobility ranks), −4 attack (video) | `FightingDefensivelyACBonusProperty`: 3 with Mobility ranks ≥ 3, else 2, plus feature ranks. The attack penalty is 4 − feature ranks, halved by some facts. Whether the AC applies at once or only after the first attack depends on the toggle's `ActivationType` [BP?] | Numbers confirmed; timing to be read at runtime. |
| Acrobatics / Mobility: half speed, Mobility check to avoid attacks of opportunity (video) | `AcrobaticMovement`: speed ×0.5. On disengage, Mobility vs **the attacker's CMD**, once per attack of opportunity | Confirmed. |
| Mobility gives "two concentration checks" when casting (video) | A failed defensive cast provokes; each of those attacks can be avoided by Mobility only with an extra fact | **Wrong in general.** |
| Inspire Rage "accept effect": off ⇒ no rage from a skald; rage stops spellcasting (video) | The toggle exists (saved). The rage buffs use `ForbidSpellCasting` (spells and spell-likes) with an ignore-feature; items are still allowed | Confirmed in principle; the default [BP?] (web: on). |
| Power Attack: −1 per threshold, +2 (one-handed or ranged) / +3 (two-handed) (taxonomy) | Penalty scales 1 + BAB/4; damage scaled ×1.5 two-handed (×2 Greater Power Attack, ×5/3 mythic), ½ for secondary attacks; applies to every attack while on | Confirmed with scaling detail. |
| Power Attack: on if hit chance ≥ 50 %, off below 35 % (taxonomy) | The game's own non-party AI uses an expected-damage comparison (`PowerAttackWatcher`) | The threshold rule is rejected; use expected value (AA-3). |
| Cleave: standard, bonus attack on an adjacent enemy if it hits, −2 AC (taxonomy) | `AbilityCustomCleave`: next enemies in reach by distance to the primary, stop at the first miss; Cleave caps at 2, Great Cleave unlimited; no adjacency rule and no AC penalty in code | Partly wrong. |
| Selective: "always on for area control in melee" (taxonomy) | The game's `CheckSelective`; TTT keeps lasting areas affecting allies | Handled by the game's own rule (tuning addendum L5). |
| Smite Evil: swift, Cha to hit, level to damage, ignore DR, deflection AC | Logs: swift (followed by Charge in the same turn); buff components seen: attack and damage bonus against the target, ignore DR | Consistent. |
| "Six abilities every character has" (video lists more) | – | The video's own count is inconsistent; not used. |

---

## E. Design decision record

### E1. Where toggle decisions belong

**Options considered:**

1. **Every toggle as a candidate in `Decide`** (switch on / switch off).
   - Simple.
   - But it mixes state decisions with actions every tick, and attack-mode toggles cost no action, so the quick-first
     rule would let them preempt everything. There is a risk of thrashing.
2. **A separate toggle controller that runs on its own schedule.**
   - Stable.
   - But it would value toggles outside the engine: two scoring systems, and no competition with the actions they
     cost.
3. **Hybrid, split by the toggle's own semantics (chosen).**
   - **(a) Toggles whose activation is a game command** (`ActivateWithUnitCommand`: performances; Rage when it has an
     action type) become *candidates* in `Decide`.
     - Their slot is `UnitActivateAbility.GetCommandType`, the game's own rule, including Quicken Performance.
     - Their value is the existing buff value of `Blueprint.Buff` on the recipients: self for Rage; allies in the
       area for performances.
     - The continuing cost per round is the resource.
     - The penalty for what it forbids comes from the buff's `ForbidSpellCasting` against the recipient's spell
       options.
     - They compete with attacks and spells for the same action, which is the real trade-off.
   - **(b) Attack-mode toggles** (Power Attack, Deadly Aim, Piranha Strike, Fighting Defensively, Combat Expertise)
     cost no action, change every attack while on, and are cheap to switch.
     - They are decided *with the attack*: the weapon candidate carries the best mode set.
     - `Execute` sets `IsOn` the way the UI does just before issuing the attack.
     - There is no separate candidate, so the toggle cannot preempt anything.
   - **(c) Movement modifiers** (Mobility) are decided with the move whose attacks of opportunity they change, and
     switched back off when the speed matters more.
   - **(d) Preferences** (accept effects) are never changed.
   - **(e) Everything else** stays as the player set it, and is reported in the coverage report.
4. **Native AI hooks** (`PowerAttackWatcher`-style).
   - The game's watcher skips party units, uses a crude damage guess (4.5 + Str), and would fight the mod's
     decisions. Rejected as the mechanism, though its expected-value idea is the same.

**Why the hybrid:** each class of toggle is decided at the point where its cost and benefit are actually known, and
through the existing value scale, so there is no second scoring system.

### E2. Valuing a toggle without switching it on to look

- The game's attack, AC and weapon rules include active buffs, but nothing can be computed "as if on" without
  applying the buff.
- Switching a buff on and off to measure it would fire buff events (UI, other mods, `CastOutcome`), so it is rejected.
- The plan reads the buff's components instead:
  - The vanilla Power Attack components (`WeaponParametersAttackBonus`, `WeaponParametersDamageBonus` with
    `PowerAttackScaling`) have public fields and simple formulas.
  - The fighting-defensively properties have the same.
  - `EstimateWeaponAttack` takes an extra attack and damage delta.
- **When a mode's components are not the known types** (a mod or TTT replacement): the mode is "not modelled". The AI
  leaves it as it is and the coverage report says so.
- **Why reading formulas is acceptable here:** the guideline "never copy tables" protects rule functions that other
  mods patch. These formulas live in the very components a patch would replace, and a replacement is detected by
  type, not assumed.

### E3. State and anti-thrashing

The game's live state is the only source of truth: `IsOn`, `IsStarted`, resources and `IsAvailable`. No toggle
counters are kept.

**Hysteresis:**

- An attack-mode toggle changes only when the best mode set beats the current one by a margin. The margin is 10 % of
  the attack's value, and at least 0.5.
- A command toggle is switched off only when its continuing cost exceeds its value. In practice:
  - Rage ends by the game's rules or at fight end;
  - performances end when nothing is left to buff, or when an enemy-free state lasts a round.

**Memory and logging:**

- Switches are remembered per unit and round in `CombatMemory`, so a toggle is not switched twice in a round.
- They are logged: `[Toggle] Seelah: Power Attack on (expected +2.1 per attack vs Corrupted Mongrel)`.

**Lifecycle:**

- Nothing persists beyond `CombatMemory`'s existing resets.
- Toggles the AI switched on are not switched off at fight end by the mod; the game's `DeactivateIfCombatEnded` does
  that.

**Respecting player intent:**

- A toggle the player switched on or off *during* the fight while the AI controls the unit is the AI's to change, as
  for any action.
- Acceptance preferences are never touched.

### E4. Unknown and unread abilities

- An ability whose effect is unread keeps the once-per-combat fallback at 1.0. It is a known, bounded behaviour and it
  is logged.
- A toggle whose effect is unread is never switched.
- The coverage report (AA-0) lists both, per character, once per fight, so new content is visible without code
  changes.
- Per-target "reached nothing" detection (tuning L3b) stops repeating an unread or condition-blocked ability against
  an enemy it cannot affect.

### E5. Compatibility

**Tabletop Tweaks.** No dependency on any optional mod. Rules are read from the live game:

- `CheckSelective` for Selective.
- Coup de Grace's added provoke action, seen in the profile.
- Mobility's `DeactivateIfCombatEnded`, read from the blueprint.
- Shatter Defenses: the game's flat-footed rule, already used by the estimates.

**Isekai, Tome of the Firebird and Mystical Mayhem** (section F below):

- Their abilities go through the same discovery and profile.
- Custom components are either understood through the standard actions they contain, or fall to the logged fallback.
- Custom resources are checked by the game's `IsAvailable`.

**Settings.** No new setting is needed for AA-0 to AA-2. AA-3 adds one: "AI may switch combat modes (Power Attack,
Fighting Defensively, Mobility)", default on. Players who manage these by hand can keep them. **Decided (user): default on.**

---

## F. Content from other mods (Isekai, Tome of the Firebird, Mystical Mayhem, Tabletop Tweaks)

Inventory from source and decompiles:

- **Isekai:** about 200 active abilities and about 80 toggles (4097 new blueprints).
- **Tome of the Firebird 1.4.18:** about 22 spells plus stances and claws.
- **Mystical Mayhem 0.7.0h:** 60 abilities and 5 toggles.
- **Tabletop Tweaks:** 118 new abilities and 19 activatables, plus rule patches.

Labels as before. Test cases with GUIDs are listed in the audit report (26 entries, e.g. Isekai `InstakillAbility`
55e06f68…, MM `WarlockCorruptingRayAbility` c312b0a1…, TTT `MagesDisjunctionAbility` 85f616bb…).

### What already works with them (confirmed)

- **Spells built from vanilla components** are read like vanilla spells. That is most of Firebird, most of Isekai's
  91 run-action abilities, and MM's Mighty Strength and Heart of the Mammoth.
- **Wrapper actions that hold an action list** are followed (Isekai `ContextActionOnNearbyPoint` summons).
- **Every patch to the game getters the AI calls applies automatically:**
  - action type: MM Invoker blasts become move actions; Spell Synthesis;
  - full-round metamagic;
  - availability and resources: Isekai's resource logic and its no-restore patch;
  - `CanTarget` restrictions: Isekai's HP-threshold targeting;
  - stacking (`IsStackable`, patched by TTT);
  - `CheckSelective`;
  - TTT's melee touch reach.
- **Isekai's auto-metamagic toggles** reach the damage estimates through `HasMetamagic`.

### Gaps, and the shared fix for each (the fixes are generic; nothing names a mod)

| Gap (examples) | Label | Generic fix | Step |
|---|---|---|---|
| Custom damage actions are invisible: TTT `ContextActionDealDamageTTT` (Oracle, Lich, mythic fixes), MM `ContextActionCorruptingRayDamage` (the warlock's main attack), MM `ContextActionDrainHP` | confirmed | The game's own preview interface `IDealDamageProvider.GetDamageRule(caster, target)`, which the game's attack tooltip uses (`TacticalCombatAttackTooltipVM`). The rule is built, not triggered; MM's ray and TTT's action implement it. Estimate its damage bundle with the existing reduction path (`AfterTargetReduction`). The same for heals via `IHealDamageProvider`. Only for actions the profile does not already read, so vanilla estimates do not change. | AA-1 |
| Kills are invisible: Isekai Instakill, Devour Prey, Beelzebub; TTT Mage's Disjunction; vanilla `ContextActionKill` | confirmed | Model `ContextActionKill` as damage equal to the target's HP, through the save branch it sits in (Conditional / saved guards already evaluated per target) | AA-1 |
| Resurrection (Isekai True Resurrection, Reality Rewrite) | confirmed | Dead allies are not targets today. Value a raise like the revive branch of healing, for dead party members and companions only (the 0.3.11 rule) | AA-4 |
| The "Enchant" type-name rule misfires (MM Enchant Item → enchant fallback; TTT Mage's Disjunction counted as an enchant before its dispel) | confirmed (`AbilityProfile.cs:522`) | Narrow it to the vanilla enchant actions and item-enchantment types; check dispel before enchant in the fallback order | AA-1 |
| Non-combat abilities can get the "utility, once per combat" fallback (Isekai Planar Coliseum Return, Cosmic Store, boss summons) | strongly supported | Utility has never been chosen in 7 logs, so it is low value and has a real risk. Keep the fallback only where the profile saw an effect it cannot verify (an unread buff, an area); abilities with no recognised element at all get 0 and are listed in the coverage report | AA-1 |
| Conversions that are not variants (TTT Quick Channel, Quicken Blessing, Meta-Rage, Quick Study; vanilla spontaneous cure) | confirmed | The same as the conversions item in C: offer visible conversions | AA-1 |
| TTT pseudo-toggles (`AbilityEffectToggleBuff`: Warrior Spirit, Black Blade attunements): read as utility, and a cast may switch the buff *off* | confirmed (structure) | Recognise `AbilityEffectToggleBuff` as an ability toggle (the existing `IsToggle` path, which only switches on when worth it) and read its buff | AA-1 |
| Custom conditions (Isekai caster-level, HP-percent, death-immune and living conditions; Super Buff tiers counted at every level) | confirmed | AA-0 lists every unevaluated condition type. AA-1 adds evaluation by running the game's own `Condition.Check()` inside the ability context, the way the safe list already does. This is safe for conditions that only read state; the safe list grows by type, and mod condition types are evaluated when they derive from known read-only bases | AA-1 / AA-4 |
| Buff components with other names (TTT `*TTT` versions, Isekai `AddStackingStatBonus`, Firebird weapon-dice bonuses, MM Spell Turning) | confirmed | AA-0 lists them per buff. Map them where their fields match the vanilla component (stat, value, descriptor); otherwise they keep the 0.5 "unknown" quality | AA-4 |
| Chain targets (Firebird Chains of Fire, MM Eldritch Chain) | confirmed | Count the extra targets of `AbilityDeliverChain` (count, radius) like area targets | AA-4 |
| TTT's Empower rule (half of the unmaximized roll when combined with Maximize) | strongly supported | The AI over-estimates Empower + Maximize slightly. Fold it into the estimate when both apply (no TTT check: the combined case is rare and the over-estimate small) | AA-4, low |
| Isekai Chrono-Surge (refunds actions), Unlimited Power (restores slots) | confirmed (utility) | Out of scope for now. Valuing action refunds needs a turn-plan model; logged in the coverage report | – |

### Respecting Tabletop Tweaks without depending on it

Every TTT-sensitive point reads the live game:

- Coup de Grace's provoke, from the profile.
- Selective, from `CheckSelective`.
- Mobility's combat-end behaviour, from the blueprint flag.
- Shatter Defenses and flat-footed, from the game rule.
- Stacking, from `IsStackable`.
- Reach, from `GetApproachDistance`.

No TTT type is referenced by name, so the same build works with and without it.

---

## G. Verification plan (acceptance matrix)

Each step ships as a test build. A step passes when its scenarios behave as listed, the regression dashboard
(tuning plan §7) does not get worse, and the logs show the reasons.

| Scenario | Expected | Step |
|---|---|---|
| Sleep against a target above its Hit Dice limit | not chosen; an eligible target in the same area still counts | AA-1 |
| Color Spray (plain Hit Dice branches) | unchanged values | AA-1 |
| A mod spell with an unreadable condition that reaches nothing twice | not chosen against that enemy for the rest of the fight | AA-1 |
| Cleric with a prepared slot and spontaneous cure | the cure conversion is offered and competes; Progressive counts the slot level | AA-1 |
| Summon Monster | cast at a point near the fight when worth it; never at a point outside range or line of sight | AA-1 |
| Class touch power at character level 1 | no longer blacklisted as a "cantrip" | AA-1 |
| Bard or skald at fight start, allies in range | song started when its value beats the best alternative action; not restarted while running; stopped when nothing is left to buff | AA-2 |
| Caster party member with Rage available (Bloodrager with spells) | rage when melee is the better plan; no rage when its best option is a spell the rage forbids | AA-2 |
| Rage resource exhausted | no candidate (game's `IsAvailable`) | AA-2 |
| Power Attack against low AC / high AC targets | on / off by expected damage; no switching every attack (hysteresis) | AA-3 |
| Fighting Defensively, tank threatened by several strong enemies vs a weak one | on when the expected damage avoided beats the lost damage; off otherwise; correct per the toggle's activation type | AA-3 |
| Mobility: caster leaving melee past two enemies | on for the move when the avoided attacks are worth the lost speed; off again when speed matters | AA-3 |
| Inspire Rage accept | never changed by the AI | all |
| Turn-based and real time | the same choices where the rules agree; real-time activation through the game's command and its cooldown | all |
| Tabletop Tweaks on / off | Coup de Grace priced with and without the provoke; Selective areas as in the tuning addendum L5 | AA-1 |
| Regression | weapons, spells, Progressive, healing, movement and area escape unchanged where not intended (decision-line comparison on the same fights) | all |

---

## H. Ranked steps

### AA-0: runtime inventory and coverage report (diagnostics only; ships with phase 1 of the tuning plan)

**Per character, once per fight:**

- **Every ability the action bar offers**, with its status: offered / not available / no target side / blacklisted
  (reason) / unread effect (component types) / conditions not evaluated.
- **Every toggle**, with its blueprint facts:
  - `ActivationType` and command type (the game's `GetCommandType`);
  - group and weight;
  - `DeactivateIfCombatEnded` / `DeactivateImmediately` / `IsOnByDefault`;
  - resource logic (spend type, resource left);
  - buff component types;
  - current `IsOn` / `IsStarted`.

**What this gives:**

- It answers every [BP?] item from the user's own game and installed mods: Charge buff contents, Fighting Defensively
  activation type, the accept toggle's default, rage restrictions, Treat Affliction limits.
- It is the continuing discovery mechanism for new content.
- **Size:** one block per character per fight, about 30–80 lines.

### AA-1: foundations (high confidence, broad effect)

**Status: implemented in 0.3.18-test5** (`docs/TACTICAL_AI.md` §28). Items 1 (shared-value Hit Dice) and 5 shipped in
0.3.17. Item 1's next predicates are the read-only game conditions listed there.

1. **Conditions.**
   - Hit Dice with shared values: dice count and bonus from the ability's context, no rolling. Yes / no / probability.
   - Add the next most common predicates once AA-0 shows which conditions appear: creature type, alignment, has
     fact or buff, HP percent.
2. **Point-target helpful abilities** (summons): aim at a point within range and line of sight, nearest the
   party's front.
3. **Conversions.**
   - Offer the conversions the game shows the player, under the slot they spend.
   - Progressive counts the slot level by `GetSpellLevel` of the converted data.
4. **Blacklist corrections.**
   - The level-0 touch rule uses the spell list level, not `SpellLevel`'s caster-level fallback, and reads damage in
     the sticky delivery.
   - Remove the dead Daze rule.
5. **Per-target "reached nothing" safety net** (tuning L3b).
6. **Coup de Grace provoke** when the profile shows it (TTT).
7. **Custom damage and heal actions** through the game's `IDealDamageProvider` / `IHealDamageProvider`; **kill
   actions** valued through their save branch (F).
8. **"Enchant" name rule narrowed; dispel checked before enchant; utility fallback only for unverifiable recognised
   effects** (F).
9. **TTT-style pseudo-toggles** (`AbilityEffectToggleBuff`) on the existing ability-toggle path (F).

### AA-2: command-activated toggles (performances, Rage family)

**Status: implemented in 0.3.18-test6** (`docs/TACTICAL_AI.md` §29, `Tactics/TacticalEngine.Toggles.cs`).

- Candidates of kind `Toggle`, slot from `GetCommandType`, value from the buff on the recipients, resource per round
  as a cost.
- Penalty for what the buff forbids against the recipient's best forbidden option.
- Stop candidates when the value is gone.
- Issued with the game's own activation (`IsOn = true` → `UnitActivateAbility`) through the dispatcher.

### AA-3: attack-mode toggles and Mobility (modifiers)

**Status: implemented in 0.3.18-test7** (`docs/TACTICAL_AI.md` §30, `Tactics/TacticalEngine.Modes.cs`). Style stances
stay in AA-4.

**Attack modes:**

- Read the mode effects from their components (E2).
- `EstimateWeaponAttack` gets attack and damage deltas.
- The weapon candidate picks the best mode set; hysteresis as in E3.
- The setting described in E5.

**Fighting Defensively:**

- Value = the expected incoming damage avoided, from `PriceAttacks` with AC +2 or +3, minus the outgoing damage lost.
- It respects the activation type: if the AC only applies after an attack, it is decided with the attack.

**Mobility:**

- The move's attack-of-opportunity cost is computed with the Mobility success chance (vs each attacker's CMD), and the
  move range is halved.
- It is switched on when that is cheaper, and off when the speed matters more.

### AA-4: long tail, each only with evidence from AA-0 and logs

**Status: implemented in 0.3.18-test8** (`docs/TACTICAL_AI.md` §31, `Tactics/TacticalEngine.Martial.cs`):
- done: Demoralize and Dazzling Display, combat maneuvers, Vital Strike, Cleave, Pounce, Extend and Persistent, chains;
- still waiting for evidence: metamagic rods, kineticist infusions, Spell Combat, style stances, resurrection and mod
  buff components.

Vital Strike, Cleave, Demoralize and Dazzling Display, combat maneuvers, metamagic rods, Extend and Persistent in the
value, kineticist infusions, Spell Combat, and specific mod abilities whose components AA-0 shows as unread.

### Not planned

- AI control of acceptance preferences.
- Building metamagic versions in the player's spellbook: it changes the player's build, and the AI uses the versions
  that exist.
- Switching unread toggles.
