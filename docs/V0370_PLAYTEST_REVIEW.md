# v0.3.70 playtest review (26 November 2026, Claude)

The author played the v0.3.70 test build (startup module `38994eec-a452-421b-a096-424a31dd2fb1`) in two saves and asked
for bad or broken decisions and missing functions. Three questions came with it:

- Did summons and companions with teleports (Dimension Door, Greater Teleport, "Shadow Walk") use them?
- A 0.3.60 user reports that the AI sometimes casts plain Blindness when a Blindness fusion is available. Bug or justified?
- Is the proposed drain-aware triage worth adding?

The logs are private and not committed; their SHA-256 hashes are in the
[receipt](competence/V0370_PLAYTEST_REVIEW_CHECKS.json).

## The logs

| Save | Fights | Modes | Commands | Party at combat start |
| --- | --- | --- | --- | --- |
| Mastermind | 8 | 3 turn-based, 4 real-time, 1 both | 1,221 | 16–24 units (9 companions plus summons) against 8–13 enemies |
| Overlord | 12 | turn-based (one real-time order each) | 388 | 7–14 units, low level |

No AI Control exception in either log. The only errors are other mods' (WrathPatches blueprint warnings,
VisualAdjustments2).

## Findings

### 1. No teleport was ever offered (bug, fixed)

In the mastermind save the teleport-capable units were three Vrocks (Greater Teleport), the Derakni (Dimension Door,
the "Hell" version) and the Knight Commander (the Evading Steps item, Dimension Door, Mass). None of them teleported.
Three causes, in order of weight:

1. **The crowd bound.** S2 offered a teleport only when the fight had at most 24 combatants (party, summons and enemies
   together). Every mastermind fight had 25–37, so the option was withheld in every decision ("teleport route
   withheld" in the `[Unused]` rows). The bound protected a few cheap safety loops; the costly step is the single native
   landing relaxation, which stays bounded. **Fixed:** the bound is now 96.
2. **The spell ban under Inspired Rage** (finding 2). Greater Teleport is spell-like and the Derakni's Dimension Door is
   a spell. While Camellia's Inspired Rage forbade the summons' spells, both were unavailable ("unavailable" in the rows):
   45 of 125 Vrock decisions and 12 of 36 Derakni decisions.
3. **Use beyond approaches.** S2 uses a teleport only to replace a walk to a weapon attack. The Knight Commander is a
   Pure Mage, makes no weapon approaches, and its teleport is an item, which S2 leaves out. Teleporting to escape melee or
   to reach a casting spot is not built (proposal P1 below).

No unit in either save has Shadow Walk. The Shadow Demon's spell-likes are Shadow Conjuration and Shadow Evocation.

### 2. Inspired Rage switched off most summons' best attacks (bug, fixed)

With Tome of the Firebird or Mystical Mayhem installed, Inspired Rage applies `InspiredRageEffectBuffBeforeMasterSkald`
at run time. That buff forbids spells for every recipient but the skald. The exported blueprints show only the stock
buff, which forbids nothing, so the AI read no ban. The ban appeared in no fight before Camellia started the song, and
in every fight after it except the last, where Camellia died at once.

The rage cost the summons their spell-like attacks. Average value of each decision's choice with the main action
available:

| Summon | Without the ban | Under the ban | Lost |
| --- | --- | --- | --- |
| Shadow Demon | 35.2 (Dragon's Breath) | 8.8 (Aid Another) | breath weapons |
| Nabasu | 48.4 (Enervation) | 7.8 | Enervation, Hold Person, Mass |
| Brimorak | 21.4 (Fireball, breath) | 7.0 | Fireball |
| Derakni | 41.3 (Enervation) | 23.6 | Enervation, Dimension Door |
| Succubus | 23.1 (Dominate Person) | 12.2 | Dominate Person |
| Vrock | 18.7 | 24.5 | gains: its main attacks are weapons and Stunning Screech |

Two gaps in the pricing caused this:

- the ban was not read at all, so the song counted as a pure gain;
- even when a ban is read, a recipient counted as a caster only with a spellbook. Spell-like abilities were never
  counted, and a spellbook caster's loss was a flat 0.

**Fixed, both combat modes:**

- Each unit's decisions record its **spell edge**: its best own spell or spell-like option minus its best other main
  action, as a running average (`SpellEdgeMemory`, unit-tested). Decisions made under a ban are not recorded. A new
  summon of the same kind starts from its kind's average.
- A buff that forbids spells now costs each recipient its spell edge for every round it lasts. A unit with no
  measurement keeps the old rule.
- When a unit is seen under the ban, the engine follows the banning buff's context chain back to the toggle buff whose
  area applied it, and remembers it. From then on that toggle's recipients are priced for the spells they lose. The log
  says so once: `[Toggle] <unit>: <ban buff> forbids its spells, applied through <toggle buff>`.
- The song's `[Buff]` row now also lists the recipients it costs, beyond the first three.

Camellia now starts the rage only when the melee recipients' gain beats the spell-users' loss. She still cannot choose
who accepts it (proposal P2).

### 3. Blindness and Blindness fusions (justified, with one real bug, fixed)

arcan casts Blindness and four fusions with it in the mastermind save. Plain Blindness was **never chosen**. Whenever it
was among the five best options, a fusion with it scored more:

- Blindness/Excruciating Deformation 39.3 against plain Blindness 21.3;
- 55.9 against 32.6;
- 42.1 against 25.8;
- 64.3 against 40.1.

Blindness is valued as soft control (0.4 of the target's worth a round). Both spells cost the same level-2 slot: the
game's fused spell level is the higher of the two parts.

Reasons the AI can still legitimately pick plain Blindness:

- **Immunity.** The game gives a fused spell the descriptors of both spells (`AbilityData.SpellDescriptor` is
  `Spell1 | Spell2`). Excruciating Deformation adds Evil and Polymorph, so a target immune to either is immune to the
  whole fusion. The log shows such targets: a Corrupted Scythe Tree, and swarms immune to both.
- **Shape.** The Runes fusions are delayed bursts aimed at the ground. Near allies they are vetoed or cost friendly fire,
  while plain Blindness hits one target.
- **Damage.** A fusion with damage wakes a sleeping or held enemy, and that is priced.
- **Ties.** When the second part adds nothing on that target, both score the same.

**The bug:** the game gives every fusion the same holder blueprint (one for touch fusions, one for the rest). The cast
watcher's failure key used only the blueprint. So a fusion that "did nothing" twice on a target blocked every fusion on
that target for the fight, and plain Blindness was left as the best. In this log the Knight Commander's
Phantasmal Web/Boneshatter reached 1 of 2 on a Greater Shadow Demon, so it did not trigger here. **Fixed:** failure keys
and area-miss keys carry the fusion's identity.

### 4. Other findings, not changed

- **Real-time commands ended without acting: 190 in the mastermind save.** Most are the AI's own deliberate
  cancellations: stuck walks (36), an enemy engaging on the way or joining the fight (29), arrival in reach (19), arrival
  in a harmful area (15), HP lost before the start (4). 73 have no logged reason; they look like the player's or the
  game's own interrupts. No change.
- **Stunning Screech "did nothing" twice** (a Vrock, value 6.3, then blocked for the fight). It affects only non-demon
  enemies within its radius, and the decision after the Vrock's advance probably counted enemies out of reach. Minor.
- **Deaths.** Fight 7: a CR12 Skeletal Champion miniboss's 23-damage critical hits dropped Camellia, Wenduag (already
  helpless and prone) and Seelah. Fight 8: drain deaths (next section). The party fought CR8–12 enemies with
  low-level companions (Camellia is a level-2 Skald).
- **Lesser Restoration scrolls read "Pure Mage"** in arcan's rows. It is a touch spell, and a Pure Mage walks only for
  critical heals. Lesser Restoration removes no negative levels.

## Drain-aware triage: assessment

**What these logs show.** In mastermind fight 8 a Corpulent Spectre touched Orvath, the player character (level 10),
five times for 2 levels each (2, 4, 6, 8, 10), and he died at his character level. From the second touch on he was
paralyzed by a Nabasu's Hold Person, Mass, so he could not act. Camellia (level 2) died from a single 2-level touch early in
the fight. The Gibrileth, a summon, was drained to 10 levels over fight 5.

**What the engine already did.** Enemy threat is margin-aware: `EnergyDrainEquivalent` uses the levels left before
death and counts a lethal drain as a kill. The `[Threat]` row rated the Spectre ×1.8 for "4 negative levels" on Orvath.
From round 4 the Knight Commander, the Retriever, Wenduag and the Shadow Demon all attacked it. It simply took several
rounds to die.

**Why the proposal's first slice would not have helped here.** Nobody in either party has Death Ward or a Restoration
that removes negative levels; arcan's Lesser Restoration scrolls remove none. Slice 1 prices those two spells, so it
would have changed no decision in these logs.

**Verdict: worth adding, but re-scoped, and not before the next playtest.**

1. **Higher value first:** count a lethal drain hit as a drop in the ally-side model: the drop chance, exposure at a
   destination, and the target's urgency for the party. Today that model uses HP only. Two allies with the same HP
   then differ when one is two levels from death. A level-2 companion would not stand in a Spectre's reach, and the
   party would value freeing a paralyzed ally who is being drained. This needs no new spell readers.
2. **Then the proposal's Death Ward and Restoration slice**, which matters only for parties with those spells (Daeran,
   or companions built as clerics, druids or inquisitors). Its native contracts and bounds are sound as written.
3. **Then the stat-damage slice** (Tick Swarm Constitution), as the proposal already says.

## Proposals (need the author's approval)

- **P1, teleport to escape and to cast (movement).** Offer a point-target teleport (Dimension Door, the Evading Steps
  item) in place of a withdrawal, an area exit or a step out of reach whose walk provokes or crosses danger. Also offer
  it in place of a "walks to its casting spot first". Same landing safety, one relaxation, the cast's own provoke priced.
  This would let the Knight Commander and casters with Dimension Door use it.
- **P2, a summon declines a rage it loses by.** For a summon AI Control drives, switch off "Inspire Rage — Accept
  Effect" when its measured spell edge beats what the rage gives it. The rage then still helps the melee units. This
  changes a toggle the player can also set, so it needs approval.
- **P3, item teleports.** Allow items like Evading Steps through the consumable policy and the item price (C3).

## Replay

Same saves, the new package, **Control Summoned Creatures** on, both modes. Send `Log.txt` and `combatLog.txt`.

1. **Teleports:** a Vrock or the Derakni against an enemy caster behind a crowded front line, before Camellia sings. Look
   for "teleport approach" in the decisions and `Greater Teleport` in the `[Unused]` rows (no more "route withheld").
2. **Inspired Rage:** look for the one `[Toggle] … forbids its spells, applied through InspiredRageBuff` line. Then see
   whether Camellia's `[Buff] … Start Inspired Rage` row lists summons with "stops its spells (-N/rd, measured)" and
   whether she still sings when most of the party relies on spell-likes.
3. **Fusions:** arcan's choices between Blindness and its fusions, especially against undead, swarms and plants.
4. **Drain:** any `[Threat]` row with "negative levels", and who attacks the drainer.
