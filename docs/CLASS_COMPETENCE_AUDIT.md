# Class competence audit - Stage A (current state), 0.3.19-test12

Task: `Class_Competence_Coverage_Audit_Master_Prompt_Expanded_Buff_Framework.md`. This document is the Stage A
deliverable (baseline, pipeline, evidence, matrix, ranked findings, plan). Evidence labels as the prompt asks:
**[code]** traced in this checkout; **[game]** traced in the decompiled Assembly-CSharp or a blueprint dump the user
supplied; **[log]** seen in a play-test log; **[untested]** plausible from source, no runtime case; **[historical]**;
**[unknown]** with what is missing.

## 1. Baseline

| Item | State |
|---|---|
| Revision | branch `claude/dreamy-pasteur-b9h3rn`, commit `36fdf7d` (0.3.19-test12), clean tree; `Info.json` 0.3.19. The supplied archive (test10, `560c376`) is two test builds behind. |
| Build | `dotnet build CompanionAI_Pathfinder.csproj -c Release -p:ManagedPath=<Managed>`: 0 errors |
| Tests | `dotnet test tests/CompanionAI.Tests`: 62 pass (ValueModel, ActionRules, ResistanceRule, RiskMath, SpellTierPolicy) |
| Game code | decompiled Assembly-CSharp (user-supplied DLL) |
| Blueprints available | Spells (1 912), Buffs (623), Mythic (2 116), MythicAbilities (129), Kineticist (800), ArcanistReservoir (10) |
| Blueprints missing | **class features** of Skald, Bard, Cleric, Warpriest, Bloodrager, Witch, Shaman, Barbarian and the other classes (performances' areas, Inspired Rage, spirits, hexes, domains, Fervor, Bloodrage, blessings) |
| Mod sources | Tabletop Tweaks Base/Core (decompiled), Tome of the Firebird, Mystical Mayhem, IsekaiMod, Buff It 2 The Limit 1.22.0 |
| Runtime logs | about 60 play-test logs, 0.3.16 to 0.3.19-test11, TB and RT |

**Runtime evidence covers one party only**: Orvath (main character: Kineticist 2 with Arcanist reservoir, Demonology summons,
mythic, Fire Blast, Grease/Sleep/Color Spray), Seelah (Paladin), Camellia (Shaman; also has an Inspired Rage toggle), Lann
(Monk/archer; Inspired Rage and Battle Song toggles), Aeiluvetar (modded ranged companion), occasionally Albedo and
Wenduag. **No Skald, Bard, Cleric, Warpriest, Bloodrager or Witch was ever under AI control in a supplied log.** For those
six, everything below is source-level ([code]/[untested]) until a log exists.

## 2. Pipeline (current)

```
TB: TurnController.Tick postfix -> TurnBasedController -> TacticalEngine.Decide -> Execute -> CommandDispatcher.Run
RT: AiBrainController.TickBrain prefix -> RealTimeController -> RealTimeTactics.Act -> the same Decide / Execute
    (legacy UnifiedDecisionEngine only with "Real time: use the old decision engine"; not covered by this audit's fixes)
Decide: AbilityDiscovery.Gather (action bar, spellbooks, variants, conversions) -> AbilityProfile (effect graph, per-target
    branches) -> candidates (abilities, weapon attacks, martial, toggles, modes, kinetic) -> ScoreAbility / ToggleValue ->
    quick (free/swift) first if it clears its bar, else best main; 15 % restraint for Standard/full-round buffs (test11)
Execute: CommandDispatcher (abilities, attacks, moves); toggles by IsOn (the game queues UnitActivateAbility)
Feedback: CastOutcome (silent failures), CombatMemory (per round/fight), TacticalDriver.LearnFromLastCommand
```

Ownership (an unused feature is not a defect when one of these holds) [code]:
- `ControlCompanions` / `ControlMainCharacter` / per-character `EnableCustomAI`: the AI does not act for that unit.
- `AiManagesToggles` (off: the AI switches no toggle at all - player or Buff It 2 The Limit own them).
- `RespectPlayerToggles` (`CombatMemory.PlayerOwnsToggle`): a toggle the player clicked in this fight is left alone.
- `AiRecastsSelfBuffs`: out-of-combat recasting of long self buffs.
- Per-character `SpellPolicy` (Progressive ladder), `Role`, `HealAtHPPercent`, `AvoidFriendlyFire`.
- Acceptance toggles (`Inspire Rage - Accept Effect`): **never switched by the AI** - ProfileOf excludes them ("not
  activated by a command"). [code] [log: every coverage report]

## 3. Version reconciliation

| Old claim | Where | Current code | Status |
|---|---|---|---|
| "Activatable toggles are unsupported" | `docs/audit/ACTIVE_ABILITY_AUDIT_REPORTS.md` (0.3.17) | `TacticalEngine.Toggles.cs` (AA-2, 0.3.18), `Modes.cs` (AA-3) | outdated; but see F1/F2: area toggles that apply their buff by actions are still unread |
| `TurnOrchestrator` architecture | `ARCHITECTURE.md` (v0.2.57) | removed in 0.3.17 (`CLAUDE.md`) | outdated document |
| "Kineticist only uses standard attacks" | historical complaint | `TacticalEngine.Kinetic.cs` (0.3.19-test6/7): burn, blocking infusions, quiver, blade internals | Orvath's Fire Blast chosen whenever it scores best (test7 TB: 12 of 37 offers; test11: 4 of 45); it lost to the crossbow (Kineticist 2, 1d6+1 vs heavy crossbow) or to Sleep/Grease [log]. No evidence of a remaining defect at this level; high-level Kineticist untested |
| "Cleric does not heal" | historical complaint | heal curve, downed allies, `PartyCanRevive`, conversions (AbilityDiscovery), Cure/Inflict branches per target | no Cleric in any log; the shared heal path works for Seelah's Lay On Hands and Camellia's cures [log] - Cleric itself [untested] |
| Short-buff unknowns: immunities, auras, ally turn order, haste stacking | `SHORT_BUFFS_AUDIT.md` §6 | unchanged | open (auras are F1/F2 here) |

## 4. Capability matrix

Status codes: **NA** not present on the build; **PO** player-owned; **UN** discovered, unavailable now; **UR** discovered,
not analysed; **UV** analysed, undervalued/filtered; **LB** lost to a better action (fine); **SF** selected, failed;
**OK** competent in observed cases; **NV** not runtime verified.

### 4.1 Priority classes

| Class | Feature family | Path | Status | Evidence |
|---|---|---|---|---|
| Skald | Inspired Rage (performance, area, acceptance) | Toggles: `ProfileOf` reads only `AbilityAreaEffectBuff` | **UR** - "its area applies nothing the AI can read" | [log] Camellia, Lann (40 + 26 coverage reports); resource 0 in every log, so it could not have started anyway [unknown: granted without the class levels that size the resource?] |
| Skald | Rage powers, Song of Strength / Dirge of Doom, spells | rage powers via the Inspired Rage effect buff (AddFacts); spells generic | **NV** | no Skald blueprint, no Skald log |
| Skald | Court Poet (replacement performances) | same toggle path | **NV** | needs blueprints |
| Bard | Inspire Courage (performance, `AbilityAreaEffectBuff`) | Toggles | analysed ("may start it when worth it"); **UN** (0 rounds in every log) | [log] Seelah (DLC3 copy); [game] DLC3 InspireCourageArea uses AbilityAreaEffectBuff with conditions |
| Bard | value of the performance | `ToggleValue`: legacy `BuffPerRound(level/2) x quality`, not the test11 appraisal; area conditions (`AbilityAreaEffectBuff.Condition`) not evaluated per ally | **UV** (F2, F3) | [code] |
| Bard | archetype performances (Dirge, Thundercaller) | toggle / ability paths | **NV** | needs blueprints |
| Cleric | Channel Energy (heal/harm, selective) | generic ability: `AbilityProfile` per-target branches (living/undead), `SelectiveRule` | **NV** | [code]; Isekai's ChannelPositiveHarm blueprint warning in the test11 log is the mod's, not ours |
| Cleric | spontaneous Cure/Inflict | `AbilityDiscovery` conversions (+ TTT Quick Channel) | **NV** | [code] |
| Cleric | domain powers (Touch of Good, Bit of Luck, Guarded Hearth) | generic buff path (test11 appraisal); one-roll / next-attack buffs | **NV** | needs Classes/Cleric blueprints |
| Cleric | emergency triage, revive | heal curve, `ReviveWorth`, `PartyCanRevive` | **OK** for the shared path (Seelah, Camellia) / Cleric **NV** | [log] |
| Warpriest | Fervor (swift self spell, heal) | unknown how the game exposes it (ability variants? AutoMetamagic?) | **NV / unknown** | needs Classes/Warpriest |
| Warpriest | Sacred Weapon / Armor, blessings | enchant rule (`EnchantmentActive`), generic buffs | **NV** | |
| Kineticist | blasts, burn, Gather Power, infusions, quiver, blade | `TacticalEngine.Kinetic.cs`, `CoverageReport.ReportKineticist` | **OK** at level 2 (Orvath) / higher levels **NV** | [log] coverage "kineticist: level 2, burn 0/3 ..."; blasts chosen when best |
| Kineticist | blocking substance infusion | switched off when it blocks every blast (AI-owned toggles only) | **NV** (Burning Infusion on, never blocking at level 2) | [log] |
| Bloodrager | Bloodrage (self toggle, ForbidSpellCasting with IgnoreFeature) | Toggles self-buff path, forbids-casting penalty | **UV** (F2: legacy value, not the recipient's own attacks) / **NV** | [code] |
| Bloodrager | bloodline powers, spells while raging | generic | **NV** | needs Classes/Bloodrager |
| Witch | hexes (Slumber, Evil Eye, Misfortune...) | generic ability path; legality by the game's `CanTarget` (hex cooldown facts); control duration per target; observed saves | **NV** | [code]; Camellia (Shaman) has no hex on her bar |
| Witch | Cackle / Chant (extend hexes) | generic: an extension of an active buff is not a recognised effect -> "effect not understood", never cast | **UR** probably / **NV** | [code] inference; needs Classes/Witch |

### 4.2 Shaman (Camellia: the only spirit/hex-family class with logs)

| Feature | Status | Evidence |
|---|---|---|
| Battle Spirit (Standard action, 4 rounds a fight) | **UR** - "its area applies nothing the AI can read": never offered in any fight | [log] 40 coverage reports, test6-test11 |
| Spirit Weapon Enchantment | **OK** (cast once per buff duration, swift) | [log] |
| Cures, Doom, Divine Zap, Enlarge | **OK / LB** | [log] |

### 4.3 Broad pass (all classes)

The runtime party gives evidence for: Paladin (Smite Evil coupled to the attack target, Lay On Hands self/others,
Vital Strike, Charge, Bull Rush: **OK** [log]), Monk/archer (full attacks, Perfect Strike as a free next-attack buff:
**OK** [log]), Arcanist (reservoir boosters, test12 branch fix: **OK** [log]), Kineticist (above), Shaman (above).
Every other class (Alchemist, Barbarian, Cavalier, Druid, Fighter, Hunter, Inquisitor, Magus, Oracle, Ranger, Rogue,
Slayer, Sorcerer, Wizard; prestige classes) is **NV**: its shared paths (spells, weapon attacks, maneuvers, toggles,
summons, conversions) exist [code] but no log shows the class. Known-risky families from the code: Magus Spell Combat /
Spellstrike (no dedicated path - the game's own Spell Combat full-round ability would be a generic ability), Alchemist
bombs (generic projectile damage with splash), Cavalier Challenge / Inquisitor Judgment (judgments are toggles; a
judgment's buff may be read as a self buff), Hunter/Druid pets (pet AI is the game's unless the pet is controlled).

## 5. Findings (ranked)

**F1 (P1) - area toggles that apply their buff by actions are invisible.** [code] [log]
`ProfileOf` (TacticalEngine.Toggles.cs) reads an area's `AbilityAreaEffectBuff` only. Areas that give their buff through
`AbilityAreaEffectRunAction` (UnitEnter / Round -> [Conditional ->] `ContextActionApplyBuff`) get "its area applies
nothing the AI can read" and are never candidates. Seen: Shaman Battle Spirit (Camellia, available with 4 rounds in every
fight, never considered), Inspired Rage. In the supplied Mythic blueprints the same pattern is used by 13 Aeon gazes,
Angel Halo and Dark Rites [game]. Root cause is shared (class-independent); one reader fixes all of them.

**F2 (P1) - toggles are valued by the pre-test11 formula.** [code]
`ToggleValue` uses `BuffPerRound(level/2) x BuffEffectAnalyzer quality`: a performance's +1 counts as much as a +5, a
rage is worth the same to a two-handed fighter and to an archer, and morale bonuses that do not stack with an existing one
are only checked as "effective/not". The test11 recipient appraisal (`AppraiseBuff`: the recipient's own attacks, what
comes at it, stacking increment, rank-based amounts in the caster's context) exists but toggles do not use it, so the
short-buff policy and toggles disagree; the 15 % restraint for Standard-action buffs does not apply to a Standard-action
performance either.

**F3 (P1, safety) - area conditions not evaluated per recipient.** [code] [game]
`AbilityAreaEffectBuff.Condition` (DLC3 Inspire Courage: IsAlly + CasterHasFact mythic/non-mythic) is ignored, and once
F1 reads action-applied buffs, their `Conditional`s (Inspired Rage's acceptance) must be evaluated, or an ally who
declined Inspired Rage (a caster) would be counted as a recipient. The prompt's rule - reason from the accepted state,
never switch it - requires this.

**F4 (P2) - harmful area toggles valued by threat only.** `0.25 x Threat x rounds` for any harmful buff on enemies in
range (dirges, gazes). Kept as it is until a log shows such a toggle; noted.

**F5 (P2, unknown) - Witch Cackle / Chant and similar "extend an active effect" abilities.** Most likely read as "effect
not understood" (never cast), which is safe; needs the blueprints to confirm and design.

**F6 (P2, unknown) - Warpriest Fervor, Cleric domain powers, Bloodrager bloodlines, Skald rage powers, Court Poet.**
Need the class blueprints to see which path they take.

No P0 was found: no stall, no forced acceptance switch, no harmful channel/area on allies in the logs (the friendly-fire
veto and Selective rule apply to channels as to any area) [log].

## 6. Plan

**Stage B1 (implemented in 0.3.19-test13, `docs/TACTICAL_AI.md` §44): F1 + F2 + F3 in one bounded change to `TacticalEngine.Toggles.cs`, reusing existing parts.**
- Invariant: an area toggle offers what its area really gives to each recipient - buffs from `AbilityAreaEffectBuff` and
  from UnitEnter/Round actions (through `Conditional`s, not UnitExit, not caster-side containers) - and each recipient
  counts only when the grant's conditions hold for it (game's own condition check, the same safe-condition list
  `AbilityProfile` uses; an unevaluable condition counts at half).
- Value: the test11 appraisal for each recipient (parts, its own attacks / incoming damage, stacking increment), over the
  rounds the resource lasts and the fight is expected to last; unread effects keep the old formula at 60 % on a main
  action, as for spells. Spell prohibition keeps the toggle's own rule (`ForbidSpellCasting` with its ignore feature).
- Discipline: a helpful-only toggle is a buff for the 15 % restraint rule.
- Not touched: acceptance toggles, attack modes, infusions, Gather Power, `AiManagesToggles` / `RespectPlayerToggles`,
  the stop rule's semantics, the legacy RT engine.
- Diagnostics: the coverage line names what the area gives (and under which condition); the existing "[Toggle] ... on"
  line carries the value; a chosen helpful toggle gets the "[Buff]" line.
- Risk / rollback: toggles that were never offered (Battle Spirit) can now be started; the restraint rule and the
  appraisal bound it. One file plus a shared helper; reverting the commit restores 0.3.19-test12 behaviour.
- Test: unit tests for the pure parts (useful rounds of a resource toggle); in game: Camellia's Battle Spirit should
  appear in the coverage report as read and either start in a fight with several engaged allies, or lose with a "[Buff]
  ... not cast" line.

**Stage B2+ (after the blueprints and logs below):** Witch hex extension (F5), Cleric domain powers / channel checks,
Warpriest Fervor, Bloodrage and Skald rage powers through the B1 path (F6), each as its own bounded change with a
positive and a negative scenario.

Open point of B1: where Inspired Rage checks acceptance (a Conditional in its area - read - or inside the effect buff -
not read) is not confirmed without the Skald blueprints; the coverage line now prints the conditions it found.

## 7. Evidence needed (cheapest next step)

1. Blueprint dumps of the class folders (same format as the Spells/Buffs zips): `Classes/Skald`, `Classes/Bard`,
   `Classes/Cleric`, `Classes/Warpriest`, `Classes/Bloodrager`, `Classes/Witch`, `Classes/Shaman`, `Classes/Barbarian`
   (or the whole `Classes` folder).
2. Play-test logs (TB and RT) with AI-controlled characters of the six classes that have none yet - for example Sosiel
   (Cleric), Ember (Witch), and mercenaries or ToyBox-built Skald / Bard / Warpriest / Bloodrager with their class
   resources available - including one fight with several melee allies near the performer.
3. Optional: a Kineticist of level 6+ (infusions, Gather Power levels, Elemental Overflow) for the higher-level kinetic
   paths.

## 8. Addendum: verified against the Classes blueprints (user-supplied, 12 070 files)

Every claim below is **[game]** (blueprint plus decompiled code) unless marked.

| Class / family | What the game does | AI path now | Status |
|---|---|---|---|
| Skald - Inspired Rage | `InspiredRageAbility` (Standard command, RagingSongResource); `InspiredRageArea` UnitEnter: `Conditional(HasFact InspiredRageAllyToggleSwitchBuff + IsAlly)` -> (mythic branch) -> `InspiredRageEffectBuff`: `AttackTypeAttackBonus` / `WeaponAttackTypeDamageBonus` (melee, rage, rank by skald level), Will save, rage powers from the caster (`AddFactsFromCaster`); casters get `ForbidSpellCasting` | B1 reads the grant and evaluates acceptance with the game's check (the acceptance is in the area, confirmed); **B2** reads the melee attack/damage components (were unread) and counts lost spells for an accepting caster | analysed; **NV** in game |
| Skald - other songs (Song of Strength, Song of Inspiration (Court Poet), Insightful Contemplation, Dirge of Doom, Song of the Fallen) | area toggles, grants by `AbilityAreaEffectBuff` or actions | B1 path | analysed; rage powers inside the effect (`AddFactsFromCaster`) not valued |
| Barbarian Rage, Bloodrage | `StandartRageActivateableAbility` / `BloodragerStandartRageActivateableAbility`: ActivationType **Immediately**, DeactivateIfCombatEnded, IsOnByDefault - the game starts them at combat start while on (`ActivatableAbility.HandleUnitJoinCombat`) | not a command: left to the game and the player's on/off choice (coverage now says so) | **PO / game-automated**, not a defect |
| Bard performances | Inspire Courage/Competence/Greatness/Heroics, Dirge, Fascinate, Frightening Tune, archetype songs: area toggles | B1 path; Inspire Courage's save bonus against fear/charm now read (B2, conditional) | analysed |
| Shaman - Battle Spirit | `ShamanBattleSpiritAbility` area -> `ShamanBattleSpiritEffectBuff` (two `AddContextStatBonus`) | B1: now offered (was "applies nothing") | analysed; **NV** (expected in the next log: Camellia) |
| Cleric - Channel Energy | `AbilityTargetsAround` 30 ft; `Conditional`s on CasterHasFact (positive/negative), IsAlly, HasFact (undead) -> heal or damage per unit | per-target branches (`AbilityProfile.AppliesTo`, all conditions safe) | analysed; **NV** |
| Cleric - Touch of Good | Standard touch, 1 round: sacred +½ level attack and saves | test11 appraisal, one round, 15 % restraint | analysed (rarely worth a Standard action - by design) |
| Cleric - Bit of Luck / Witch Fortune / Lion's Call / Knowledge, Law, Chaos domains | `ModifyD20` TakeBest (roll twice) | **B2**: read as about +3 on attacks / saves (conditional variants half) | analysed |
| Warpriest - Fervor | heal: swift self (`WarpriestFervorPositiveAbilitySelf`) / sticky touch; **quickened self spells**: `AddMechanicsFeature FervorQuickenedSpell` -> `AbilityData.GetConversions` adds each Warpriest spell quickened, `ForceTargetOwner`, paid with Fervor | heal: generic; quickened spells: **were dropped by `AbilityDiscovery`** (a conversion with the same blueprint was skipped) - **B2 fix**: same-blueprint conversions with different metamagic are kept (Fervor, Combat Healer); self-only by `TargetAnchor.Owner` | **fixed (B2)**; **NV** |
| Warpriest - Sacred Weapon / Armor | swift toggles, `ContextActionWeaponEnchantPool` | toggle self path; enchant pool not valued (unread, legacy formula) | partially analysed |
| Witch - Slumber, Misfortune, Evil Eye | 1-day cooldown buff on the target first (`AbilityTargetHasFact` inverted), then the save | the game's `CanTarget` keeps a hexed target out; control duration per target; observed saves | analysed (no retry spam possible) |
| Witch - Cackle / Chant | Move action, 30 ft: `Conditional(HasBuffFromCaster hex)` -> `ContextActionReduceBuffDuration` Increase +1 round | **was "no recognised effect" (never cast)** - **B2**: `AbilityProfile.Extensions`; value = per hexed unit within reach, one more round of its share of that enemy's worth (control scale), only while the fight outlasts the hex | **fixed (B2)**; **NV** |
| Inquisitor - Judgments, Bane | self toggles (commands) | toggle self path; judgments' `AddFactContextActions` buffs read, `AddMechanicsFeature` parts not | partially analysed |

Not covered by B2 (logged as unread, legacy value): polymorph forms (Wild Shape, Beast Shape, dragon forms),
condition and descriptor immunities, enchantment pools, rage powers granted from the caster, on-hit triggers.

### Stage B2 (0.3.19-test14)

1. `AbilityDiscovery`: same-blueprint conversions with other metamagic (Fervor, Combat Healer) are options.
2. `BuffParts`: `AttackTypeAttackBonus`, `WeaponAttackTypeDamageBonus` (melee/ranged), `ACBonusAgainstAttacks`,
   `TemporaryHitPointsPerLevel` (per recipient level), `ModifyD20` take-best (+3 attack / saves), saving throw bonuses
   against descriptors (conditional).
3. Toggles: an accepting recipient's lost spells count (`ForbidSpellCasting` in the effect); the performer's keep the
   toggle's own rule.
4. `AbilityProfile.Extensions` + `TacticalEngine.ExtensionValue`: Cackle / Chant.
5. Coverage wording for game-started rages.
