# Summon abilities and combat consumables: final plan

22 November 2026. Investigation and plan by Claude; **Codex implements**, Claude reviews. The author's decisions are
recorded at the end. No code, manifest or package change in this document's commits. Base: development `6c4e96c`
(v0.3.60). Evidence fingerprints: [competence/SUMMONS_CONSUMABLES_EVIDENCE.json](competence/SUMMONS_CONSUMABLES_EVIDENCE.json).

The author's two requests:
1. Summons should use the spells and abilities they have, appropriately.
2. Companions should use scrolls and potions far more widely. "Most potions and scrolls are not for healing. They have
   other utilities in battle; a better system is needed."

## Summary

- **Summons already use the abilities the readers understand.** In the coverage7 log the driven summons cast their own
  spells 54 times, and the Deva chose Hold Monster 6 times out of 17 decisions. What holds them back is narrower:
  - a set of effects the readers only guess, in the bigger demons the author summons;
  - teleports and leaps, which have no value at all;
  - no log line saying why an ability was never chosen.
- **Potions and scrolls: the author is right, and the cause is one rule.**
  - The readers understand 95 of 114 potions, 570 of 650 scrolls and 212 of 229 wands.
  - Any potion without a heal is worth 0 unless the fight's difficulty is at least 0.8, and half value even then.
    Difficulty reached 0.8 in 1.4 %, 6.3 % and 9.6 % of the decisions in the last three logs.
  - Buff, cleanse and protection potions in quick slots are therefore almost never even considered. Scrolls get a flat
    0.7 factor, unrelated to what they hold or what they cost.
- **The game lets a potion be given to an adjacent ally** (`AbilityData.PotionForOther`, built by the action bar).
  The engine never builds it, so companions only ever drink potions themselves.
- **Plan**, both modes:
  - **Slice 0, first:** a bounded "why not used" witness and an offline item audit.
  - **Consumables:**
    - **C1:** potions valued like the spell they hold;
    - **C3:** one consumable price in place of the 0.7 / 0.85 / 0.5 / 0 factors, slots before items;
    - **C2:** a potion handed to an ally;
    - **C4:** fixes to the readers' item misreads.
  - **Summons:**
    - **S1:** read the guessed effects in the author's demon roster, by mechanism;
    - **S2:** teleports and leaps (movement, approved by the author).
  - Each slice reuses the existing appraisals. None adds a path search or a cache that outlives a decision.

## Evidence: summons

**Inputs:** the coverage7 `Log.txt` (module `0aa40527`, summon control on), the S1 summon-kit inventory
([SUMMON_KIT_INVENTORY.md](SUMMON_KIT_INVENTORY.md), `competence/SUMMON_KITS_DETAIL.txt`) and the code.

**How a summon's kit reaches the engine.** A driven summon gathers its abilities like a companion
(`AbilityDiscovery.Gather`: its facts and spellbooks, variants expanded). `SummonControl.Allowed` then removes only:
- summoning spells;
- anything that spends an item or a material component.

The `[Coverage]` rows confirm the live bars: every kit ability of the seven driven kinds is listed as understood.

**What they chose** (coverage7; turn-based and real-time decisions):

| Summon | Decisions | Own abilities offered / chosen | Not offered at all |
| --- | --- | --- | --- |
| Quasit | 93 | Invisibility 38/13, Cause Fear 15/11 | — |
| Abrikandilu | 52 | Sunder Armor 23/9, Cause Fear 6/6 | — |
| Babau | 70 | Dispel Magic (Target) 8/1, (Point) 6/3 | — |
| Incubus | 54 | Crushing Despair 6/3, Hideous Laughter 1/1 | — |
| Summoned Monadic Deva | 17 (real time) | Hold Monster 8/6, Heal 6/3, Holy Aura 4/0 | Holy Smite |
| Vermlek | 143 | Abandon Flesh 4/4 | Mass Inflict Light Wounds |
| Schir | 123 | Expeditious Retreat 7/2 | Protection from Good |

"Offered" means the ability was among the five best options printed for that decision. The log shows only the top five
(`TacticalEngine.Decide`, `usable.Take(5)`), so "not offered" does not say why. The three that never appear are
plausibly correct:
- Holy Smite would hurt the party's own demons.
- Mass Inflict may have had no burst without allies in it.
- Protection from Good does nothing against non-good enemies.

The log cannot confirm any of these. This is what slice 0 fixes.

**What the readers only guess.** The S1 inventory lists 28 guessed or unread kit entries across the author's 29 Demonology
creatures. Grouped by mechanism:

| Mechanism | Examples (creature) | Reading now |
| --- | --- | --- |
| Teleport, leap, overrun | Greater Teleport (Vrock), Dimension Door (Derakni), Liquid Leap (Omox), Overrun (Vavakia) | no value: never used |
| Self or area effects with a condition or delayed delivery | Unholy Nimbus (Nalfeshnee), Stunning Screech (Vrock), Derakni Drone, Song of Discord (Gallu) | old quality guess |
| Debuffs on rolls and target marks | Touch of Chaos (Nalfeshnee), Slayer's Study Target (Painajai) | guessed debuff |
| Shadow spells | Shadow Conjuration / Evocation (Shadow Demon, Coloxus) | parent unread; variants may expand at run time |
| Guessed parts of read spells | Enervation's temporary HP, the Contagion variants' stat damage, Enslave Soul | main effect read, a part guessed |

**Checked and ruled out.** All 28 guessed or unread abilities are at will; none carries an `AbilityResourceLogic`. So the
rule that values a guessed limited-use effect at 0 does not hold them back. Their guesses are simply low. No summon resource
or spell-policy change is needed.

**Other observations, not plan items.**
- About one turn-based decision in two for a summon is the end of a turn (96 of 208): an ordinary turn close.
- Crowded approaches ("no free side", 14 rows for one Quasit) are the shared approach topic, not a summon defect.
- Four Quasits cast Cause Fear at one Half-Fiend in a row, and three of the four rolled no save. That is consistent with
  spell resistance, so it needs a replay before it is called a coordination defect.

**Cost today.** A summon decision has a median of 4–19 ms. A summon's first decision costs 77–218 ms: its kit's
profiles are built then, once per blueprint. The slowest summon decision was about 1 s, inside a memory clean-up.

## Evidence: potions and scrolls

**What is on the bars.** The game offers only quick-slot items in combat. The coverage7 save's quick slots held:
- healing potions;
- Shield of Faith, Bear's Endurance, Feather Step, Death Ward and Lesser Restoration potions;
- scrolls of Magic Missile (3), Mage Armor (2), Death Ward, True Seeing and Ear-Piercing Scream;
- a wand of Greater Invisibility;
- two Quicken metamagic rods.

**What was used.** Across the three logs I hold, the only items used were:
- healing potions;
- the Bandit Alchemist's bombs;
- one Enlarge Person potion, in a hard fight;
- a Summon Monster VIII scroll, twice.

None of the buff or cleanse potions above, and none of those scrolls, was ever among a decision's five best options.

**What the readers understand** (exported item blueprints joined to the audit's verdict on each item's ability):

| Type | Items | Read | Guessed | Unread |
| --- | --- | --- | --- | --- |
| Potion | 114 | 95 | 18 | 1 |
| Scroll | 650 | 570 | 68 | 12 |
| Wand | 229 | 212 | 15 | 2 |

Reading is not the limit. The readers' item misreads (C4) are a short list:
- Freedom of Movement read as a debuff;
- True Seeing guessed as a debuff;
- Spell Resistance and Protection from Spells guessed.

The mental-stat potions are "guessed" only in the offline audit, which has no recipient. In play they are appraised per
recipient.

**What the engine does with an item** (`TacticalEngine.ConsumableFactor` and the item bar):

| Rule | Effect |
| --- | --- |
| Potion with a heal | factor 1; the heal curve decides; the restoring part already has the easy-fight restraint (`ValueModel.SpentHealValue`) |
| **Any other potion** | **0 below difficulty 0.8, 0.5 above**: buffs, cleanses, protections, Enlarge, Haste alike |
| Scroll or wand | 0.7 × the game's Use Magic Device chance (`AbilityData.UseMagicDeviceDC`) |
| Other usable (flasks) | 0.85 × that chance |
| Item spent as a material (Alchemical Weapon's flask) | 0.7 (scroll, wand) or 0.85, no Use Magic Device |
| Every spent item | must still be worth 5 points after its risks (`MinSpentItemValue`) |
| A guessed buff from an item | 0 (an item needs a clear effect; this is correct and stays) |
| Easy-fight restraint of offensive spell slots and daily bombs | not applied to items (`SourceItem == null` test) |
| Potion for an ally | never built: `PotionForOther` is false, so the game anchors the potion on its owner |

Difficulty (`enemy threat / party power`) had a median of 0.19, 0.34 and 0.38 in the last three logs.

## Native contracts the slices rely on

Read from the decompiled game. The implementer re-checks each against the supplied assembly before building on it.

**Drinking a potion** (`AbilityData.GetDefaultActionType`):
- a move action, or a swift action with Fast Potion while the swift action is free;
- the engine reads this already (`TacticalEngine.SlotOf` uses `RuntimeActionType`), so a potion competes in the move slot.

**A potion for another** (`AbilityData.PotionForOther`):
- **Built by the action bar** as `new AbilityData(item.Ability) { PotionForOther = true }`
  (`MechanicActionBarSlotItem.GetConvertedAbilityData`, `ActionBarSlot`).
- **Action:** the item's `CustomItemAbilityActionType`, otherwise a standard action. The drinker's move-action rule does not
  apply.
- **Target:** `TargetAnchor` no longer forces the owner. A potion ability with Personal range still anchors on the owner,
  so only potions whose ability can target friends are worth the conversion.
- **Range:** `Caster.GetWeaponRange()` with no weapon: the native minimum weapon range (2 ft in the checked assembly) plus the giver's reach. The approach distance adds both bodies.
- **Delivery:** `UnitUseAbility` switches to the sticky-touch delivery ability when there is one, and uses the touch
  animation when the target is not the giver.
- **Attacks of opportunity:** `UnitUseAbility.OnTick` skips the defensive-casting check for all potions, including a
  handoff. Moving into reach still uses the ordinary walking risk.
- **Identity:** native `Equals` and `GetHashCode` include the flag. The mod's option key (`AbilityDiscovery.Key`) does not
  yet.

**Use Magic Device:** `CombatMath.UseMagicDeviceChance` reads `AbilityData.UseMagicDeviceDC`. Unchanged by this plan.

**Teleports** (`AbilityCustomTeleportation`, `AbilityCustomDimensionDoor`):
- **Landing:** aimed at a unit, the caster lands in front of it, in its facing direction, at both bodies plus the native minimum weapon range (2 ft in the checked assembly).
  Aimed at a point, at the point, relaxed among the bodies that travel.
- **Restriction** (`IsTargetRestrictionPassed`, checked by `AbilityData.CanTarget`): the point is on the navmesh, not in
  the fog of war and not on a disabled flying isle. A `DimensionDoorRestrictionIgnorance` component lifts the check for `AbilityCustomDimensionDoor`. `AbilityCustomTeleportation` still runs its own restriction.
- **Dimension Door moves the units its settings name** (its `Radius`), not only the caster. Read who travels; do not
  assume a solo jump.
- `AlongPath` teleports land along the caster's path, at most a round's combat speed times their multiplier.

## Plan

Both combat modes unless marked. Each slice keeps the existing value units, the native rule queries and the existing
safety checks (friendly fire, permanent death, blocklists, Pure Mage, consumable settings, no items on summons).

### Slice 0: why an ability or item was not used (diagnostics, both tracks)

- **Change:** keep, per AI-driven unit and fight, for each kit ability and quick-slot item:
  - the best value it reached;
  - how often it was chosen;
  - the last rule that zeroed or excluded it.
- **Reasons** are a small fixed set, recorded where scoring already returns:
  - the gates in `AddAbilityCandidates` (not available, not affordable, no score needed);
  - the early exits of `EvaluateAbility` (blocklist, consumable factor, Pure Mage, fusion withheld, Coup de Grace);
  - the per-target skips (no side to aim at, out of range with no approach, friendly-fire veto, no item on a summon);
  - the item bar or quick bar;
  - effect not understood;
  - removed by the summon policy (`SummonControl.Allowed`).
- **Output:** at the fight's end, one `[Unused]` row per unit, listing only what was never chosen. Summons are keyed by
  kind, so a pack reports once. For example:

  ```text
  [Unused] Vermlek: Mass Inflict Light Wounds best 0 (friendly fire on Schir, Orvath)
  [Unused] arcan: Item:Scroll of Mage Armor best 3.1 (below item bar 5)
  ```

- **Bounds:**
  - one small record per ability per decision, reused across decisions (no allocation once warm);
  - strings built only at the fight's end;
  - at most 32 entries a unit and 16 units a fight;
  - no new rule, path or world query.
- **Offline:** a ProfileAudit section `ITEMS` makes the item table above reproducible. It joins every usable item
  (`BlueprintItemEquipmentUsable.m_Ability`) to its ability's verdict and the consumable rule that would apply to it.
- **Why first:** the never-offered summon abilities and every item fix are judged against these rows.
- **Acceptance:** the next log names a reason for every kit ability and quick-slot item never chosen.

### C1: potions valued like the spell they hold

- **Change:** remove the potion difficulty gate in `ConsumableFactor`. A potion's effect goes through the same
  appraisal as the spell:
  - buff parts (`AppraiseBuff`, including per-recipient stat links);
  - relief and cleanse (`TacticalEngine.Relief`);
  - typed energy protection;
  - concealment reveal;
  - heals, unchanged.
- **Cleanses:**
  - A cleanse is worth something only when the drinker carries the effect it ends (poison, disease, blindness, a drain,
    a curse).
  - Then it is not held back by an easy fight: the harm is present now.
  - Death Ward and Protection from Evil are worth what they prevent from the enemies actually present, as their spells
    are.
- **Restraint (author decision 1):** a buff potion in an easy fight takes the spell slots' restraint,
  `ValueModel.LimitedUseDiscount(difficulty)`, on its value. That replaces the binary gate. C3 then makes this the price of
  every item.
- **Kept:** the item rule in `BuffQuality` (a guessed buff from an item is worth 0) and the item bar.
- **Bounds:** no new reader or query. The potion's ability already has a profile.
- **Acceptance:**
  - a poisoned companion with Neutralize Poison drinks it in an ordinary fight;
  - Shield of Faith or Bear's Endurance is drunk early in a fight where melee is coming, when it beats the turn's other
    options;
  - no potion is drunk at full value against a lone sleeping enemy.

### C3: one consumable price, slots first

- **Today:** fixed factors (0.7, 0.85, 0.5) and a gate. None of them knows what the item does or whether the unit has a
  renewable way to do the same.
- **Change:** an item's worth is its effect minus the price of spending it. One pure function in `ValueModel`, used by
  `ConsumableFactor` for every usable item and every item spent as a material:
  - **Restraint:** `LimitedUseDiscount(difficulty)` on the effect's value, the same as spell slots and daily bombs, so
    consumables and slots compete on one footing.
  - **No double restraint:**
    - a healing item's restoring part keeps `SpentHealValue`, which already applies it;
    - a cleanse of a harm present now takes none (C1).
  - **Use Magic Device:** the game's chance, unchanged, for scrolls and wands not on the unit's list. Materials take none:
    the class ability uses the item, the unit does not activate it.
  - **Item bar:** unchanged (5 points after risks).
- **Slots first (author decision 2):**
  - An item copy of a spell (scroll, wand or potion) is not offered while the unit can cast the same spell blueprint from
    its own spellbook now.
  - "Can cast now" is the native availability of that spellbook option (slot or spontaneous slot left), and the option
    must pass the blocklist and Pure Mage rules.
  - The item is the backup once the slots are spent. A rest restores slots; an item is gold.
  - The witness reason is "slot copy available".
- **No scarcity pricing (author decision 3):** item counts, charges and gold are not read.
- **Bounds:** pure formulas in `ValueModel` with unit tests. The slot-copy test reads the unit's own allowed abilities,
  which the decision has already gathered.
- **Acceptance:**
  - in an easy fight a Magic Missile scroll and the same spell from a slot are held back by the same restraint;
  - in a hard fight neither is held back;
  - with a Magic Missile slot left, the scroll is not used;
  - once the slots are spent, it is.

### C2: a potion handed to an adjacent ally

- **Change:**
  - `AbilityDiscovery` adds the native conversion for each potion in a quick slot whose ability can target friends,
    exactly as the action bar does.
  - `AbilityDiscovery.Key` gains the `PotionForOther` flag, so drinking and handing over are separate options.
  - The engine values it on allies through the existing heal, buff and relief code. Its action is the native one
    (standard, unless the item says otherwise), read through `SlotOf`.
  - It walks into the native approach distance through the existing touch-approach rules, the same walk a touch
    spell makes, so no new movement policy.
- **Kept:**
  - no item on a summon (the existing rule);
  - the giver itself is served by the drinking option, not this one;
  - the consumable settings, the C3 price and the provoke pricing.
- **Bounds:** one extra option per such potion; allies already scored for touch heals; no extra path query beyond the
  existing touch preflight.
- **Acceptance:**
  - a companion with a healing potion feeds it to a badly hurt melee ally it can reach, when that beats its own turn;
  - a Personal-range potion is never offered to an ally.

### C4: the readers' item misreads

- **Change:** read the item effects the audit flags as misread or guessed, by mechanism:
  - **Freedom of Movement:** immunity to grapple, paralysis, slow and entangle, valued against the enemies who can apply
    them, as typed protection is.
  - **True Seeing:** concealment reveal, through the existing reveal value.
  - **Spell Resistance and Protection from Spells:** worth the hostile spells present, through the existing resistance
    rule.
  - **Magical Vestment**, already read for potions in v0.3.25: check that the scroll and potion forms agree.
- **Acceptance:** each is a check in the `ITEMS` audit section on the actual blueprint.

### S1: the demons' guessed effects, read by mechanism

- **Change:** extend the existing readers to the mechanisms in the table above, roster first:
  - **Area effects with a save:** Value the condition on Stunning Screech, Derakni Drone and Song of Discord through
    the existing area and control readers. Check each one's native target filters, save, immunity and timing.
  - **Unholy Nimbus:** the cast applies a one-round self buff. Its `AddFactContextActions.Deactivated` callback casts
    the separate self-centred blast. Price the callback's actual save and target filters at expected delivery, with
    uncertainty for who will still be in range. A current recipient list alone does not establish a reliable forecast.
    Withhold positive credit if the native callback or a safe delivery forecast is unreadable.
  - **Roll debuffs and target marks:** Touch of Chaos (`ModifyD20`), Slayer's Study Target and the smite marks
    (`AttackBonusAgainstTarget`, `DamageBonusAgainstTarget`, `IgnoreTargetDR`). Value them through the caster-side mark
    reader (`TacticalEngine.Marks`), which already couples a mark to the unit's own attacks on that target.
  - **Shadow spells:** confirm in play, through slice 0, that the variants reach the engine. Only if they do not, read
    the parent's variants as the action bar does.
  - **Guessed parts of read spells:** Enervation's temporary HP for undead, and the Contagion variants' stat damage
    through the existing drain reader (`CombatMath.Drain`).
- **Rule:** each mechanism is read from its native component's semantics, with a ProfileAudit check on the actual
  blueprint. A mechanism that cannot be read exactly stays withheld, not guessed higher.
- **Bounds:** reading happens when a profile is built, once per blueprint, which is the summon's first decision.
  Nothing per decision.
- **Acceptance:**
  - `SUMMON-KITS` shows these Demonology rows read;
  - in play, Vrock and Nalfeshnee use their screech and nimbus when enemies are in range and allies are not.

### S2: teleports and leaps (movement, approved by the author)

- **Change:** Greater Teleport, Dimension Door, Liquid Leap and the like become another way to make an approach the
  engine already plans:
  - the same destination, with the walk's attacks of opportunity and area costs removed, and the cast's action added;
  - the landing is where the native component puts the caster (see the contracts above), and it is priced like any
    end spot: areas, swarms, reach of the enemies there;
  - any ally that travels with a Dimension Door is priced at its landing spot too.
- **Kept:** Overrun is a maneuver and stays separate.
- **Native checks decide:** landing validity and range are the component's restriction and the ability's range, through
  `AbilityData.CanTarget`. The engine adds no landing rule of its own.
- **Bounds:** only when an approach candidate already exists; at most the four free-side spots the approach already
  prices.
- **Applies to:** summons first; companions with the same abilities get it too.
- **Acceptance:** a Vrock teleports next to a caster behind the enemy line instead of walking through it, and does not
  teleport into an area.

### Not planned, with reasons

- **Summon resource or spell-policy change:** the guessed demon abilities are at will (checked above).
- **Scarcity pricing by count or gold:** not yet (author decision 3). Slice 0 will show whether items are wasted.
- **Coordinating several summons on one target:** the one case seen is consistent with spell resistance; replay first.
- **Crowded summon approaches:** the shared approach topic, already tracked.

## Order and verification

1. **Slice 0** and the `ITEMS` audit, together with **C1**: the biggest change for the smallest code, judged by slice 0's
   rows.
2. **C3**, then **C2** and **C4**.
3. **S1**, roster first.
4. **S2**.
5. **Summon control default** (author decision 5): judged after the S1/S2 playtest. If that log shows no summon-control
   defect and no other major summon gap, the default is switched on in its own commit, with the evidence. Otherwise it
   stays off, and the remaining gaps are listed for the author.

Each slice:
- unit tests for its pure rules;
- a ProfileAudit section on the actual blueprints, with every other report line unchanged on the same inputs;
- the native harnesses unchanged;
- a checks receipt and build provenance under `docs/competence/`, a section in `docs/TACTICAL_AI.md`, and a handover entry
  in `CLAUDE.md`;
- a test package under `test-builds/` and a short replay for the author (turn-based and real time);
- no `main`, tag or release; `Info.json` stays 0.3.60 until a release is authorized.

## Decisions (author, 22 November 2026)

1. **Buff potions in easy fights:** yes, with the spell slots' restraint.
2. **Slot or item of the same spell:** slots first.
3. **Scarcity pricing:** not yet.
4. **S2 teleports and leaps:** approved as movement.
5. **Summon control on by default:** only if summon control is polished enough and no other major summon expansion is
   needed. Judged after S1 and S2 (order step 5).
