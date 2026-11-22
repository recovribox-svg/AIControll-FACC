# Summon abilities and combat consumables: investigation and plan

22 November 2026, Claude. Plan only: no code, manifest or package change. Base: development `6c4e96c` (v0.3.60).
Evidence fingerprints: [competence/SUMMONS_CONSUMABLES_EVIDENCE.json](competence/SUMMONS_CONSUMABLES_EVIDENCE.json).

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
    - **C2:** a potion handed to an ally;
    - **C3:** one consumable price in place of the 0.7 / 0.85 / 0.5 / 0 factors;
    - **C4:** fixes to the readers' item misreads.
  - **Summons:**
    - **S1:** read the guessed effects in the author's demon roster, by mechanism;
    - **S2:** teleports and leaps. This is movement, so it needs approval.
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
| Self or area effects with a condition or combat trigger | Unholy Nimbus (Nalfeshnee), Stunning Screech (Vrock), Derakni Drone, Song of Discord (Gallu) | old quality guess |
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
| Potion with a heal | factor 1; the heal curve decides |
| **Any other potion** | **0 below difficulty 0.8, 0.5 above**: buffs, cleanses, protections, Enlarge, Haste alike |
| Scroll or wand | 0.7 × the game's Use Magic Device chance (`AbilityData.UseMagicDeviceDC`) |
| Other usable (bombs, flasks) | 0.85 × that chance |
| Every spent item | must still be worth 5 points after its risks (`MinSpentItemValue`) |
| A guessed buff from an item | 0 (an item needs a clear effect; this is correct) |
| Potion for an ally | never built: `PotionForOther` is false, so the game anchors the potion on its owner |

Difficulty (`enemy threat / party power`) had a median of 0.19, 0.34 and 0.38 in the last three logs.

## Plan

Both combat modes unless marked. Each slice keeps the existing value units, the native rule queries and the existing
safety checks.

### Slice 0: why an ability or item was not used (diagnostics, both tracks)

- **Change:** keep, per AI-driven unit and fight, the best value each kit ability and quick-slot item reached, and the
  last rule that zeroed it. Examples: consumable factor, item bar, friendly-fire veto, no side to aim at, not available,
  effect not understood, summon policy, Use Magic Device chance.
- **Output:** at the fight's end, one `[Unused]` row per unit; summons are keyed by kind, so a pack reports once. For
  example:

  ```text
  [Unused] Vermlek: Mass Inflict Light Wounds best 0 (friendly fire on Schir, Orvath)
  [Unused] arcan: Item:Scroll of Mage Armor best 3.1 (below item bar 5)
  ```

- **Bounds:**
  - one small record per ability per decision, written where scoring already returns;
  - strings only at the fight's end;
  - at most 32 entries a unit and 16 units a fight;
  - no new query.
- **Offline:** a ProfileAudit section `ITEMS` makes the item table above reproducible. It joins every usable item to its
  ability's verdict and the rule that would apply to it.
- **Why first:** the never-offered summon abilities and every item fix are judged against these rows.
- **Acceptance:** the next log names a reason for every kit ability and quick-slot item never chosen.

### C1: potions valued like the spell they hold

- **Change:** remove the potion difficulty gate. A potion's effect goes through the same appraisal as the spell:
  - buff parts (`AppraiseBuff`, including per-recipient stat links);
  - relief and cleanse (`TacticalEngine.Relief`);
  - typed energy protection;
  - concealment reveal;
  - heals, unchanged.
- **Cleanses:**
  - A cleanse is worth something only when the drinker carries the effect it ends (poison, disease, blindness, a drain,
    a curse). Then it is not held back by an easy fight: the harm is present now.
  - Death Ward and Protection from Evil are worth what they prevent from the enemies actually present, as their spells
    are.
- **Restraint:** what C3 sets. Until C3 lands, buff potions use the spell slots' easy-fight restraint
  (`ValueModel.LimitedUseDiscount`) instead of the binary gate.
- **Bounds:** no new reader or query. The potion's ability already has a profile.
- **Acceptance:**
  - a poisoned companion with Neutralize Poison drinks it in an ordinary fight;
  - Shield of Faith or Bear's Endurance is drunk early in a fight where melee is coming, when it beats the turn's other
    options;
  - no potion is drunk at full value against a lone sleeping enemy.

### C2: a potion handed to an adjacent ally

- **Native contract:** `MechanicActionBarSlotItem.GetConvertedAbilityData` builds `new AbilityData(itemAbility)
  { PotionForOther = true }`. `AbilityData.TargetAnchor` then uses the potion ability's own anchor (a touch) instead of
  the owner. `UnitUseAbility` delivers it by touch, with the touch animation, and uses the sticky-touch delivery ability
  when there is one.
- **Change:**
  - `AbilityDiscovery` adds this conversion for each potion in a quick slot, exactly as the action bar does.
  - The engine values it on allies through the existing heal, buff and relief code, and walks to touch range through
    the existing touch-approach rules. This is the same walk a touch spell makes, so no new movement policy.
- **Kept:**
  - no item on a summon (the existing rule);
  - the consumable settings;
  - the existing unit-key separation, so the self and other versions are separate options.
- **Bounds:** one extra option per potion; allies already scored for touch heals; no extra path query beyond the
  existing touch preflight.
- **Acceptance:** a companion with a healing potion feeds it to a badly hurt melee ally it can reach, when that beats its
  own turn.

### C3: one consumable price

- **Today:** three fixed factors (0.7, 0.85, 0.5) and a gate. None of them knows what the item does, how many are left,
  or whether the unit has a renewable way to do the same.
- **Change:** an item's worth is its effect minus the price of spending it:
  - **Restraint:** the share of the effect an easy fight takes off. This is `ValueModel.LimitedUseDiscount`, already
    used for spell slots and daily bombs, so consumables and slots compete on one footing.
  - **Use Magic Device:** the game's chance, unchanged, for scrolls and wands not on the user's list.
  - **Item bar:** unchanged (5 points after risks).
- **Default for slot or scroll of the same spell:** spend the renewable slot first, the scroll when slots are out. A rest
  restores slots; a scroll is gold. This is close to today's behaviour, made explicit. It is an author choice (see
  below).
- **Bounds:** pure formulas in `ValueModel` with unit tests. Item counts and charges come from the item
  (`ItemEntity.Count`, `Charges`) only if the author wants scarcity (below).
- **Acceptance:** in an easy fight a Magic Missile scroll and the same spell from a slot compare by the same restraint;
  in a hard fight neither is held back.

### C4: the readers' item misreads

- **Change:** read the item effects the audit flags as misread or guessed, by mechanism:
  - Freedom of Movement: immunity to grapple, paralysis, slow and entangle, valued against the enemies who can apply
    them, as typed protection is.
  - True Seeing: concealment reveal, through the existing reveal value.
  - Spell Resistance and Protection from Spells: worth the hostile spells present, through the existing resistance rule.
  - Magical Vestment, already read for potions in v0.3.25: check that the scroll and potion forms agree.
- **Acceptance:** each is a check in the `ITEMS` audit section on the actual blueprint.

### S1: the demons' guessed effects, read by mechanism

- **Change:** extend the existing readers to the mechanisms in the table above, roster first:
  - **Area effects with a save:** Stunning Screech, Unholy Nimbus, Derakni Drone and Song of Discord are an area cast
    whose buff applies a condition after a save. Value them through the existing area and control readers
    (`AddCondition` → control strength; `CombatStateTrigger` read as the native trigger, not guessed).
  - **Roll debuffs and target marks:** Touch of Chaos (`ModifyD20`), Slayer's Study Target and the smite marks
    (`AttackBonusAgainstTarget`, `DamageBonusAgainstTarget`, `IgnoreTargetDR`). Value them through the caster-side mark
    reader (`TacticalEngine.Marks`), which already couples a mark to the unit's own attacks on that target.
  - **Shadow spells:** confirm in play, through slice 0, that the variants reach the engine. Only if they do not, read
    the parent's variants as the action bar does.
  - **Guessed parts of read spells:** Enervation's temporary HP for undead, and the Contagion variants' stat damage
    through the existing drain reader.
- **Rule:** each mechanism is read from its native component's semantics, with a ProfileAudit check on the actual
  blueprint. A mechanism that cannot be read exactly stays withheld, not guessed higher.
- **Bounds:** reading happens when a profile is built, once per blueprint, which is the summon's first decision.
  Nothing per decision.
- **Acceptance:** `SUMMON-KITS` shows these Demonology rows read. In play, Vrock and Nalfeshnee use their screech and
  nimbus when enemies are in range and allies are not.

### S2: teleports and leaps (movement: needs the author's approval)

- **Change:** Greater Teleport, Dimension Door, Liquid Leap and the like become another way to make an approach the engine
  already plans: the same destination, with the walk's attacks of opportunity and area costs removed, and the cast's
  action added. Overrun is a maneuver and stays separate.
- **Native contract:** to be read before building: the custom teleport components, landing validity and range. Their
  native checks decide; the engine adds no landing rule of its own.
- **Bounds:** only when an approach candidate already exists; at most the four free-side spots the approach already
  prices.
- **Applies to:** summons first; companions with the same abilities get it too.
- **Acceptance:** a Vrock teleports next to a caster behind the enemy line instead of walking through it, and does not
  teleport into an area.

### Not planned, with reasons

- **Summon resource or spell-policy change:** the guessed demon abilities are at will (checked above).
- **Scarcity pricing by count or gold:** no evidence yet that items are wasted; slice 0 will show it. Offered as an author
  choice below.
- **Coordinating several summons on one target:** the one case seen is consistent with spell resistance; replay first.
- **Crowded summon approaches:** the shared approach topic, already tracked.

## Order and verification

1. **Slice 0** and the `ITEMS` audit, together with **C1**: the biggest change for the smallest code, judged by slice 0's
   rows.
2. **C3**, then **C2** and **C4**.
3. **S1**, roster first.
4. **S2**, after the author's decision.

Each slice:
- unit tests for its pure rules;
- a ProfileAudit section on the actual blueprints, with every other report line unchanged on the same inputs;
- the native harnesses unchanged;
- a test package and a short replay;
- no `main`, tag or release.

## Decisions for the author

1. **Buff potions in easy fights:** use the spell slots' restraint (my recommendation), or keep buff potions for hard
   fights only?
2. **Slot or scroll of the same spell:** slots first (my recommendation), or scrolls first?
3. **Scarcity:** should the last potion of a stack be kept for higher stakes than the tenth? I recommend not yet, until
   slice 0 shows waste.
4. **S2 teleports and leaps:** approve as movement?
5. **Summon control:** it stays experimental and off by default. Should it become on by default once S1 is played?
