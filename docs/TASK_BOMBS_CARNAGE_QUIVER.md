# Alchemist bombs, Dazzling Display / Dreadful Carnage, Kinetic Sharpshooter quiver (31 October 2026, Claude)

This is the task's evidence and plan document, written before any code changed. Its handover is in the last section.

**Task.** Three player reports against released v0.3.22, each to be checked on development.

**Inputs:**
- the author's task prompt and two class guides (Kingmaker / tabletop background, not authoritative);
- the vanilla blueprint exports;
- the decompiled game (`Assembly-CSharp`);
- the Tabletop Tweaks Base 2.6.18 source (supplied by the author during the task);
- the DarkCodex 1.9.6 source.

**What is not available.** No combat log or save with a party Alchemist, a Dreadful Carnage owner or a Kinetic
Sharpshooter exists among the supplied logs. The only bombs logged are an enemy Cambion Alchemist's. Nothing below is
confirmed in play.

**Baseline.**
- Development `00110eb` (UMM 0.3.23, runtime `da4e0a5`).
- Task branch `claude/alchemist-carnage-quiver`.
- No open pull requests.

**How the evidence was gathered.**
- Each mechanism was checked in the decompiled game code and in the blueprints.
- The mod's readers were run on the exports with `tools/ProfileAudit`: classes, buffs, items, spells, feats, mythic.
- Partial exports hide referenced buffs. A first run without the spell dumps showed Blinding Bomb as "damage" only,
  because `BlindnessBuff` was missing.

## 1. Alchemist bombs and Grenadier Alchemical Weapon

### What the game does

| Ability | Native mechanism | Spends |
| --- | --- | --- |
| Class bombs (`BombStandart`, Acid, Blinding, Choking, Force, Frost, Holy, Shock, Tanglefoot, Cursed, Dispelling, Explosive) | `AbilityResourceLogic` → `AlchemistBombsResource` (class level + Int bonus per day). The thrown attack uses `AbilityDeliverProjectile` with `BombItem` and a plain `RuleAttackRoll`, never a `RuleAttackWithWeapon`. The main target takes the dice; the others in `AbilityTargetsAround` (8 ft) take the splash on a Reflex save. A miss gives the main target the splash (`AbilityEffectMiss`). | 1 bomb per throw |
| Precise Bombs | The bomb's `AbilityTargetsAround` filter: "caster lacks `PreciseBomb` OR target is not an ally". | - |
| Fast Bombs (toggle, on by default) | Native `FastBombs`: when a listed bomb's command acts, the game builds a full-attack routine with `WeaponForBombEmulate` (a fake item in the hand, with equipment events). It then queues follow-up throws of the same bomb on the same target with the cooldown ignored, while the bomb stays available. | 1 bomb per throw, several per order |
| Grenadier Alchemical Weapon (`GrenadierAlchemistFire/AcidFlask/HolyWater` `Move/Swift/Free`) | Personal self-buff, 1 minute, `Stacking=Replace`; it removes the other two flask buffs. The buff adds `AdditionalDiceOnAttack OnlyWeaponAttacks` 1d6 + Int (Holy Water 2d4 + Int against evil or negative-energy creatures; Fire also burns). It removes itself on the owner's next weapon hit (`AddInitiatorAttackWithWeaponTrigger OnlyHit` → `ContextActionRemoveSelf`). | One Alchemist's Fire / Acid Flask / Holy Water from the inventory per use |

**How the flask is spent.** The material component (`MaterialComponent`, Count 1) comes out of the inventory on every
use: `AbilityData.Spend` → `SpendMaterialComponent` while `RequireMaterialComponent`, that is, while the ability has no
source item. The game refuses the ability when none is left (`IsAvailableForCast`).

**`OnlyWeaponAttacks` excludes bombs.** `AdditionalDiceOnAttack.CheckCondition` needs a `RuleAttackWithWeapon` parent,
and a bomb has none. So the rider never adds to a bomb, and a bomb never uses it up. The guide's "add it to a weapon
attack or a bomb" is wrong for Wrath.

**Tabletop Tweaks Base 2.6.18.** It changes none of these: its Alchemist fixes are Dispelling Bombs, mutagens and some
archetype features.

### What the AI does now (code, development `00110eb`)

**Alchemical Weapon: confirmed waste, and outside the player's item settings.**
- The flask is invisible to the engine:
  - `CostsNothing` is true: no spellbook, no source item, no ability resource;
  - `ConsumableFactor` is 1 and the spent-item bar (`MinSpentItemValue` 5) is 0, because both look only at `SourceItem`;
  - `ConsumablePolicy.IsAllowed` is always true, so with "use thrown items" off the AI still burns flasks.
- The buff is valued as one weapon hit's extra dice: hit chance × (1d6 + Int), halved for a recipient that also casts.
  That is about 2-4 for a typical Grenadier, which clears the swift bar of 2 whenever the buff is not on. One rider per
  hit is the right reading.
- It is recast after every weapon hit: one flask per turn that hits.
- When the turn's action is a bomb, the rider is spent for nothing until a later weapon hit. A bomb-throwing Grenadier
  therefore spends a flask a fight with no effect.
- Nothing ties the setup to a weapon attack this turn. The mark coupling (`CoupleOwnAttackMarks`) does this for marks
  only.

**Class bombs: read correctly, with one gap.**
- With the full exports the readers see:

  | Bomb | Read as |
  | --- | --- |
  | Choking | hard control |
  | Blinding, Frost, Holy, Tanglefoot | soft control |
  | Shock | minor control |
  | Cursed bombs | debuffs by their penalties |
  | Acid, Explosive | damage plus their buff |
  | Dispelling | dispel |
  | Standard, Directed Blast | damage |

- The direct hit and the splash are separate per target: `ContextConditionIsMainTarget` is evaluated against the aimed
  unit (`AbilityProfile.AimedAt`), and the splash save is read per recipient.
- Precise Bombs is the game's own filter, evaluated per unit (`AreaReaches`; both conditions are in the safe list).
  - An ally the game spares is not counted, and no friendly-fire veto follows.
  - An ally it would hit still is: it vetoes the throw with "Avoid friendly fire" on, or is priced with it off.
- **Gap: Force Bomb's knockdown (`ContextActionKnockdownTarget`, on a failed Reflex save, main target only) is not read
  at all.**
  - Force Bomb is valued as d4 force damage and so looks strictly worse than the standard d6 fire bomb, except against
    fire resistance.
  - The same unread shape is in Battering Blast, Clashing Rocks, Cave Fangs and Rift of Ruin.

**Bomb resources: no opportunity cost.**
- A bomb competes at full value against the crossbow, however easy the fight.
- Levelled spells get a difficulty-scaled discount on their damage and control in easy fights (the Spell Use setting).
  Bombs get none, because the discount checks `Spellbook != null`.
- With Fast Bombs one order spends several bombs, and the AI values and counts it as one throw.

**Not confirmed (no log).** Which bombs a given Alchemist threw, how often, and in what fights.

### Smallest supported corrections

**1a. A spent material item is a spent item.**
- When the game will take a usable item from the inventory (`ability.RequireMaterialComponent` and the item is a
  `BlueprintItemEquipmentUsable`):
  - the player's consumable settings apply (`ConsumablePolicy.IsAllowed`: "use consumables", and the item type's switch -
    thrown items for flasks);
  - the ability no longer counts as costless;
  - the spent-item bar (5) and the item-type conservation factor apply.
- Spell material components such as diamond dust are not usable items and are untouched (spell policy owns them).

**1b. A weapon-hit rider needs a weapon attack this turn.**
- Scope: a self buff that removes itself on its owner's next weapon hit (`BuffsSpentByWeaponHit`: every buff it applies
  carries an `AddInitiatorAttackWithWeaponTrigger` that removes it). This covers the Grenadier flasks, Stunning Fist's
  owner buffs and Elemental Fist.
- It is offered only when the decision's best main action makes a weapon attack:
  - a weapon attack or Move+Attack;
  - Charge, Vital Strike, Cleave or a melee-attack ability;
  - Coup de Grace;
  - an archer blast, which is a `RuleAttackWithWeapon` with the bow.
- A Move-action rider is also not offered before a full attack, which needs that Move.
- This is the existing one-shot mark coupling, applied to self riders. No new value is added.

**1c. Read `ContextActionKnockdownTarget`.**
- It becomes a one-round prone (the existing Trip strength, 0.4 of the target's worth).
- Its chance is the effect's own: spell resistance × attack roll × the failed save when it sits behind one
  (`CombatMath.EffectChance`), instead of CMB against CMD.
- It is nothing on a target that cannot be knocked down (`CanBeKnockedOff`, the native check) or is already prone.

**1d. Bombs share the spells' easy-fight restraint.**
- Scope: an ability with the native `AbilityIsBomb` marker that spends an ability resource.
- Its damage and control are discounted like a levelled spell's: share = 1 - clamp(0.55 + 0.45 × difficulty, 0.55, 1).
  That is 41 % at the easiest fights, 22.5 % at difficulty 0.5, and 0 from difficulty 1 (enemy threat ≥ party power).
- The formula moves to `ValueModel.LimitedUseDiscount`, unit-tested, with the spell values pinned, so spells keep
  exactly their present discount.
- Policy assumption: saving daily bombs for harder fights.
  - **Counterexample:** a single dangerous enemy in an otherwise easy fight still pays the discount. Its threat-weighted
    value usually wins anyway.
  - No quotas, no counts and no "save the last N": a later throw in the same fight is judged the same way.

**1e. Fast Bombs.**
- No valuation change. The native routine needs a fake item in the hand, with equipment events, which is not a
  side-effect-free preview.
- A bounded diagnostic says once per fight that the AI values a bomb order as one throw while Fast Bombs is on.

**Performance.**
- 1a: one property read per item-less ability.
- 1b: one pass over the already sorted usable list, only when it holds a rider.
- 1c: profile analysis, built once per blueprint.
- 1d: one multiplication.

## 2. Dazzling Display and Dreadful Carnage

### What the game does
- **Dreadful Carnage** (`fc37b70e…`) has an `AddOutgoingDamageTrigger` with:
  - no weapon, ability, descriptor or energy checks;
  - `NotZeroDamage`, `ApplyToAreaEffectDamage` and `TargetKilledByThisDamage`.
- After any damage the owner deals that takes a living target to 0 HP or below (and not a regenerating one), it runs
  `ContextActionOnContextCaster` → `ContextActionCastSpell DazzlingDisplayAction`. The cast:
  - comes from the caster;
  - costs no action (no full-round or standard flag);
  - checks only `CanTarget`, so the Weapon Focus weapon requirement does not apply;
  - is a `Demoralize` on every enemy within 30 ft of the caster, with the owner's own Intimidate check.
- **Dazzling Display** (`DazzlingDisplayAction`) is a full-round action (`RequireFullRoundAction`; `SlotOf` gives
  `FullRound`).
  - The mythic versions (`DazzlingDisplaySwift/Move/StandardAction`) take that action instead; the swift one is at −20.
  - On an already Shaken target a successful check lengthens the Shaken (`Buff.IncreaseDuration`).
- **Tabletop Tweaks Base 2.6.18** changes neither feat (it only adds Mounted Maniac, a copy of the Display).

### What the AI does now
- **Manual Display is valued per enemy:** Intimidate odds (`CombatMath.IntimidateOdds`, the game's DC and duration) ×
  the enemy's worth × Shaken's share × rounds, plus Thug / Hellknight / Shatter Confidence / Trickster upgrades.
  - Enemies already Shaken, immune or out of the 30 ft are skipped.
  - After two uses the "repeat" factor of 0.3 applies.
- **Confirmed gap:** no attack option counts the free Display that a kill fires for a Carnage owner. With several
  enemies close by, a full-round Display can outscore a full attack that is likely to kill and would give the same
  Display for nothing. That is the comparison the player describes.
- **Not confirmed (no log):** how often Display was chosen, or with what values.

### Smallest supported correction
- **Recognise the trigger by its shape, not its name.** A feature with an `AddOutgoingDamageTrigger` that has
  `TargetKilledByThisDamage`, none of the ability, descriptor, energy or parent checks, and a caster-side
  `ContextActionCastSpell` of an ability with a `Demoralize` and an `AbilityTargetsAround`. The shape is cached per
  blueprint.
- **Credit each weapon-attack candidate** (full attack, single attack, Move+Attack) with:

  kill chance on its target × the free Display's worth on the other enemies within the burst radius of where the
  attack is made.

  - The kill chance is the one the attack's value already uses (`ValueModel.KillChance` from the estimate's land
    chance and damage).
  - It is 0 against a regenerating target, as the native check.
  - It is credited once per candidate, never per swing.
  - The killed target is excluded.
- **The free Display's worth per enemy** is the manual Display's own terms, computed once per decision: Intimidate
  odds, worth, Shaken's share, rounds, upgrades. Enemies already Shaken, immune to it or outside the radius count 0.
  There is no "repeat" factor, since a proc is not a repeated manual use.
- **Not credited:** spells, bombs and other ability damage. They trigger it too, but only weapon attacks compete with
  Display in the report. A downed target's quirk (its HP is already at 0) is not modelled either.
- **Manual Display stays** wherever its immediate value is higher: no kill likely, enemies out of the attacker's burst,
  or the mythic action versions.
- **Performance:** only for units with the recognised feature. Per decision, one pass over the enemies, then a sum per
  weapon candidate. No extra native forecasts.

## 3. Kinetic Sharpshooter quiver and full attacks

### What the game does
- **Ordinary weapon attacks add blast damage only through a kinetic blade.** `KineticistController` adds a blast to a
  weapon attack only for a `WeaponKineticBlade` weapon (`TryHandleKineticBladeAttack` on `RuleAttackWithWeapon`).
- **The quiver is a burn store.** The Kinetic Sharpshooter's quiver (`KineticSharpshooterObtainQuiverBuff`,
  `UnitPartKineticist.QuiverBuff`) is read only by `AbilityKineticist.CalculateCost` / `Spend`: its ranks pay a
  kinetic ability's burn while a ranged weapon is in hand. No component adds blast damage to a bow attack.
- **An archer blast is one bow attack.** It is one `RuleAttackWithWeapon` with the bow, `SkipMainDamage`
  (`AbilityDeliverProjectile`, `IsArcherBlast`), as a standard action.
- **Exploding Arrows still shoots once.** It also carries an `AbilityDeliverAttackWithWeapon`, but both delivery
  components are layer 0, and `AbilityExecutionProcess` runs only the first one (the projectile). No routine of several
  blast arrows exists.
- **Cluster Arrows adds no arrows.** It is a substance infusion (+3 burn) that puts a stacking −1 AC on the blast's
  target.
- **No mod changes this:** neither Tabletop Tweaks Base 2.6.18 nor DarkCodex 1.9.6 changes any of the above.
  KineticistElementsExpanded was not available to check.

### What the AI does now
- Archer blasts are valued with the bow's damage modifiers, the game's hit chance, Overflow and swarm declines (§37,
  §46, §47).
- The quiver is created when burn blocks every blast (§38).
- Full attacks are valued by the game's attack routine.

### Conclusion
**The report's premise ("a full attack with a quiver adds the blast damage to the attack") does not match the game's
code.** A full attack that out-values a single blast through the bow is the engine comparing two real options.

**The early return in `AddKineticCandidates`** (no quiver while any blast is affordable) skips a quiver that would only
save burn. That is not the reported problem, and changing it is not supported by evidence.

**No change. Needed to go further:**
- the archetype and mod list;
- the ability on the bar;
- a native combat log of a turn where the player expected the blast.

## 4. Acceptance cases and regression guards

Synthetic tests cover the pure rules only. Everything else needs a replay.

**Bombs and Alchemical Weapon**
- A swift flask with "use thrown items" off: never offered.
- With it on: offered only at 5 or more, and only before a weapon attack this turn. Never before a bomb, spell or
  advance.
- Not recast while its buff is on (existing check).
- Out of flasks: not available (native).
- Force Bomb on a target that can fall, against a fire bomb on one resistant to fire: Force Bomb gains its knockdown.
  On a flying or immune target, or one already prone, it gains nothing.
- Precise Bombs: an ally in the splash is spared and no veto follows; without Precise Bombs the ally is protected.
- An easy fight: a bomb 10-20 % better than the crossbow loses. A big group or a dangerous target still wins.
- A hard fight: no discount, so the same choices as before.
- A second throw later in the same fight is judged the same way: no counter.
- Zero bombs left: not offered (native).
- Fast Bombs: the diagnostic line appears once per fight.

**Carnage**
- A character without Carnage: no change.
- A likely kill with Shaken-able enemies within 30 ft: the attack gains the credit.
- An unlikely kill: a small credit.
- No survivors in range, or all already Shaken or immune: none.
- A regenerating target: none.
- Manual Display still wins when nothing can be killed.

**Quiver**
- Unchanged.
- Ordinary kineticist, kinetic blade and infusion selection: unchanged.

**Integration guards**
- AI disabled: unchanged.
- Scoring spends nothing: it reads the inventory, resources and buffs only.
- Unrelated companions: one extra property read per item-less ability.
- Sleep and wake, revive, Selective, TTT and the cast observers: untouched.

## 5. Handover (implemented)

**Commits on `claude/alchemist-carnage-quiver`** (base development `00110eb`):

| Commit | What |
| --- | --- |
| `5bf8c1a` | This document (sections 1-4), before any code. |
| `39ce04e` | Slice 1: flasks priced as items, weapon riders before weapon attacks, Force Bomb knockdown, daily bomb restraint, Fast Bombs note. |
| `cabdb2e` | Slice 1 review: the spent material is read once per ability evaluation. |
| `6c52bfa` | Slice 2: Dreadful Carnage credit on weapon attacks. `ControlShare` is one helper. ProfileAudit lists recognised kill triggers. |
| `dc411f1` | Slice 3: no valuation change; the archer-blast explanation line and per-fight log bookkeeping (`CombatMemory.FirstThisFight`, also used by the Fast Bombs line). |

**Package:** `test-builds/AI-Control-FACC-v0.3.23-dev-bombs-carnage-cabdb2e-UMM-test.zip`.
- Source `cabdb2e`, MVID `89ffb864-46e5-4840-ba2e-15318074028e`.
- ZIP SHA-256 `26072f85…4b82`; identity file next to it.
- UMM stays 0.3.23.

### Assumptions corrected

| Claim | Where from | What the game does |
| --- | --- | --- |
| Alchemical Weapon adds to a bomb | Guide | It does not: a bomb is a plain attack roll, not a weapon attack. |
| Fast Bombs is "a standard action ... full BAB of bombs after a full move" | Guide | The game builds the follow-up throws from its own full-attack routine at the moment the first throw acts. This build does not model it. |
| A quiver adds blast damage to a full attack | Player report | Only a kinetic blade's hits add a blast; the quiver pays burn. |
| Carnage needs a melee kill | Tabletop rule | In Wrath it fires on any damage that takes a living, non-regenerating target to 0 HP, and costs no action. |
| TTT changes Carnage, bombs or Alchemical Weapon | Possible | Tabletop Tweaks Base 2.6.18 changes none of these mechanisms. |

### Performance
- **Work added per decision:**
  - one material-component read per ability without a source item;
  - one pass over the usable list when it holds a weapon-hit rider;
  - for a Carnage or Public Execution owner only: one feature scan (one cached lookup per feature) and one Intimidate
    estimate per enemy, then a sum per weapon option.
- **Kept constant:** no extra native forecasts, no extra `Decide` pass, nothing spent or rolled.
- **Profiles:** the knockdown reader runs once per blueprint, when its profile is built.
- **Not measured in game:** no runtime timing is claimed.

### Limits
- **Fast Bombs:** valued as one throw.
- **Carnage:** only weapon attacks are credited. The credit does not follow attack-mode changes to the kill chance, and
  the duration a repeat adds to an existing Shaken is not counted.
- **Bomb restraint** is fight-level (difficulty). A single dangerous enemy in an easy fight still pays it, though its
  threat-weighted value usually wins.
- **Knockdown chance:** for an ability with a save it always includes the failed save. That under-counts an ability
  whose knockdown does not depend on that save, which errs on the side of restraint.

### Not verified, and what would settle it
- **All three reports:** no log, save or combat log of the reported characters was available.
- **Replay steps:** in [test-builds/README.md](../test-builds/README.md) ("What to play").
- **Kinetic Sharpshooter:** we need:
  - the exact archetype and mod list (KineticistElementsExpanded was not available to check);
  - the ability on the bar;
  - the game's combat log of a turn where blast damage seemed to ride on arrows.
