# Magic Fusion: the full-coverage programme

17 November 2026, Claude. The author asked for one plan covering the whole programme, so that the engine does more than
recognise which fused spells a Magic Deceiver has: it should use them well. Base: development `0019856`
(`claude/dreamy-pasteur-b9h3rn`), after PRs #81–#91. The first slice (C1, below) is implemented on the same branch as this
plan; see [FUSION_MAIN_TARGET_DOT.md](FUSION_MAIN_TARGET_DOT.md).

Evidence levels:
- **Verified:** read in the supplied game assembly, the author's blueprint exports, the stock BubblePrints 2.7.0x data,
  or the current source, or counted by the audit tool.
- **Proposal:** design to be built. No benefit is claimed before it is measured and played.

## 1. What "full coverage" means

A fused spell is covered when three things hold:
1. **Offered.** AI Control does not withhold it (`TacticalEngine.FusionWithheld`).
2. **Valued by the joint model.** Its worth comes from the shared-save model, which keeps each save outcome apart through
   the kill and control maths (`FusionOutcomeModel`, `TryFusionValues`), not from the simpler forecast that scales
   everything by the fail chance.
3. **Every effect it has is priced.** No payload is dropped as "omitted". Harm to allies is counted the same way as harm
   to enemies.

Once a fusion meets all three, the engine's ordinary competition does the rest. It chooses the target, the aim (unit or
ground), the moment, and whether to spend the slot or do something else, and it does this for fusions exactly as it
does for every other option. There is no separate fusion planner, and none is proposed.

Some configurations can never be fully covered. Their native behaviour is random in a way a forecast cannot price
without rolling dice: a recast on a random enemy, a summon, or a cast the native UI forbids. Those stay offered on the
simpler value, or withheld, with a named reason (section 6).

## 2. Where it stands

**Verified.** The coverage audit (`ProfileAudit --fusion-coverage`, 60 source spells, 3,540 ordered pairs) was run on the
#91 build. It lists every configuration the native fusion UI can make, with its delivery, save and school.

| | Configurations |
| --- | --- |
| All templates | 15,494 |
| Offered | 8,709 |
| Offered and valued by the joint model | 4,521 |
| Offered on the simpler value | 4,188 |
| Withheld | 6,785 |

### Withheld, by sole remaining reason

| Reason | Count | Main source spells |
| --- | --- | --- |
| Shared Hit Dice selection | 1,034 | Circle of Death 512, Sleep 274, Rainbow Pattern 268 |
| Nested shared-save branch | 1,031 | Banshee Blast 612, Phantasmal Killer 435 |
| Line delivery geometry | 1,016 | Acidic Spray 350, Lightning Bolt 338, Fire Snake 338 |
| Support/reveal/fact-grant/form/faction/poison carrier (joint value) | 672 | Reduce Person 190, **Acid Arrow 186**, Fear 112, Ray of Exhaustion 88 |
| Unread guard "is the main target" (joint value) | 512 | **Molten Orb 512** |
| Additional area delivery | 454 | Obsidian Flow 278, Haunting Mists 180 |
| Secondary spell payload | 426 | **Hideous Laughter 426** |
| Unread guard "compare target HP" (joint value) | 418 | Disintegrate 418 |
| Clicked-target secondary payload | 336 | Obsidian Flow 336 |
| Healing or caster-side payload | 310 | Vampiric Touch 310 |
| Composite or non-HP damage (joint value) | 280 | Enervation 192, Phantasmal Putrefaction 72 |
| Source spell without a native RunAction | 209 | Haunting Mists 209 |
| Dispel/enchantment/extension, dynamic removal, guard fact (joint value) | 87 | Fear, Dispel Magic, Baleful Polymorph |

### Offered on the simpler value

| Reason the joint model does not apply | Count | Main source spells |
| --- | --- | --- |
| Support/reveal/fact-grant/form/faction/poison carrier | 2,434 | Caustic Eruption, Serenity, Excruciating Deformation, Touch of Slime, Peer Through *, Curse of Magic Negation |
| Composite or non-HP damage | 736 | Phantasmal Putrefaction 374, Wracking Ray 350 |
| Dispel/enchantment/extension dependency | 660 | Dispel Magic, Greater Dispel Magic, Arcana Theft |
| Dynamic removal dependency | 350 | Baleful Polymorph |
| Earlier application changes a guard fact | 8 | Blindness/Color Spray |

These are offered, but their value is less precise: for example, a passed-save effect is lost or a debuff is read as
control.

### Source spells with no configuration offered

Banshee Blast, Circle of Death, Disintegrate, Haunting Mists, Hideous Laughter, **Molten Orb**, Obsidian Flow, Phantasmal
Killer and Vampiric Touch. **Acid Arrow**, Reduce Person and Enervation are offered only where no root save is chosen,
and only on the simpler value.

### An effect omitted today: damage over time

**Verified.** A buff that only deals damage each round is a periodic damage carrier. Examples are Incendiary Runes'
burning, which a save ends, Molten Orb's burning (1d6 fire a round for 1d3 rounds) and Acid Arrow's 2d4 acid a round.
Today such a buff is admitted, but it is worth nothing (`TryFusionControlRounds`: "omitted DOT benefit"). Only one
narrow layout of it, burning with a save, is recognised at all.

Ordinary (unfused) spells also leave these ticks unpriced: `BuffParts` reads an `AddFactContextActions` buff only for its
child buffs and activated stats. Acid Arrow cast on its own is valued at its first 2d4.

## 3. arcan's bar

arcan is the author's Magic Deceiver. The author swaps fusions in and out to test others; a Magic Deceiver holds up to
ten. **Verified** against the coverage report.

| Fusion | Offered | Joint value | What is missing |
| --- | --- | --- | --- |
| Incendiary Runes / Frigid Touch | 8 of 8 | yes | the burning's damage over time (C1) |
| Snowball / Sleep | 16 of 16 | yes | — |
| Snowball / Shocking Grasp | 4 of 4 | yes | — |
| Explosive Runes / Blindness | 8 of 8 | yes | — |
| Explosive Runes / Frigid Touch | 8 of 8 | yes | — |
| Explosive Runes / Hideous Laughter | **0 of 8** | — | withheld: the Best Jokes recast (C2). **Offered 8 of 8 with the joint value since C2.** |
| Explosive Runes / Sleep | 4 of 4 | yes | — |
| Explosive Runes / Color Spray | 8 of 8 | yes | — |

## 4. The slices

Each slice is one branch and one pull request into development, with:
- unit tests;
- a `ProfileAudit` section on the actual blueprints and native routes;
- the coverage delta, with every other report line unchanged;
- a package and a replay.

The order follows the author's request and bar first, then configurations gained × how often the spell is useful ×
risk.

### C1. Main target and damage over time (Molten Orb, Acid Arrow, burning) — **implemented**

- **Native contracts (verified).**
  - `ContextConditionIsMainTarget` compares the cast's main target with the recipient. The main target is the clicked
    target (`AbilityExecutionContext.MainTarget => ClickedTarget`), fixed for the cast. A ground aim is the main target of
    no unit, unless the unit stands exactly on that point with facing 0 (`TargetWrapper.Equals` compares the unit, else
    the point and the facing).
  - A buff's round ticks run in a context whose root is the fused cast. The ticks therefore get its spellbook's dice cap
    (`MagicHackSpellbookComponent.GetMaxDamageDicesForAction`), its DC and its metamagic. A save in a tick rolls on its
    own: `Context.SavingThrow` is unset in a tick.
  - The first tick comes one tick interval after the buff is attached (`Buff.OnAttach`; turn-based counts from the turn's
    start).
  - `IgnoreUnitModifiers` only skips flat damage bonuses (`BaseDamage.AddModifier`). Resistance and immunity still apply.
- **Change.**
  - "Is the main target" becomes a stable fusion guard. It is evaluated against the scored aim, as it already is for
    ordinary bombs.
  - A buff whose whole effect is hit-point damage each round, possibly ended by a save, is a periodic damage carrier. Its
    expected ticks within the horizon join the joint value on the save outcome that applies it. The damage counts for
    HP and progress; the kill bonus stays with the immediate damage.
  - The same ticks are added to the friendly-fire damage cost of the allies the cast catches.
- **Expected coverage (verified by the audit run, see the slice page).** Molten Orb and Acid Arrow configurations
  offered with the joint value. The burning of arcan's Incendiary Runes fusions is priced.

### C2. Hideous Laughter's Best Jokes recast (426; arcan's Explosive Runes / Hideous Laughter) — **implemented**

**Done (20 November 2026, [COVERAGE2_BUILD.md](COVERAGE2_BUILD.md)):**
- The proposal below was simplified: whether or not the caster has the fact, the recast gets no credit and no ally cost,
  so no caster check is needed.
- Only a selector whose every action is a spell cast, and that keeps only the caster's enemies, qualifies
  (`AbilityProfile.IsEnemyOnlyRecast`). The fused profile does not read the recast.
- Offered 9,209 → 9,587; joint value 5,039 → 5,321.


- **Native contract (verified).** On a failed save Hideous Laughter applies its laughter buff. If the caster has the
  fact `ec739ff2` (Best Jokes), it also casts Hideous Laughter itself on one random enemy around that is not already
  laughing (`ContextActionOnRandomTargetsAround` → `ContextActionCastSpell`). The recast picks enemies only.
- **Proposal.**
  - If the caster lacks the fact, the branch cannot run, and it is not a secondary payload. Read it through the existing
    caster-fact guard.
  - If the caster has the fact, give the recast no credit at first: it is a random enemy and it rolls its own save. It
    costs allies nothing, because it targets enemies only. Later, credit the expected laughter on the best eligible
    enemy, weighted by `1 / eligible enemies`.
- **Acceptance.** Explosive Runes / Hideous Laughter offered, with the joint value, for a caster without the fact.

### C3. Disintegrate's "below this HP" marker (418) — **waits for the author**

The marker (`DisintegrateBuff`) kills a creature below 0 HP at its next round tick. On an enemy that is only a kill that
the damage already prices, except where regeneration would otherwise only knock it out. On an ally caught by a fused
area, it turns a knockout into a permanent death. How heavily to weigh that is a policy decision for the author.


- **Native contract (verified).** After each damage action, `Conditional(ContextConditionCompareTargetHP Less)` applies a
  permanent buff (`f7a6a7d2`, the dust). The guard reads the HP left after the damage.
- **Proposal.** Treat it as a kill dependency, like the pooled "is unconscious" test of Fusion F7: the joint value
  already carries the kill chance, and the marker adds no value of its own. The guard becomes stable only for an
  application whose buff has no effect on a living unit. Check this on the dust buff's components.

### C4. Line delivery (1,016)

- **Proposal.** A native line footprint (`AbilityDeliverProjectile` of type Line: its length and width, obstacles and
  Selective) shared by fusions and ordinary lines. Aim it like the cone adapter (`TacticalEngine.ConeAim.cs`), with a
  bounded set of turns past allies. This needs the native line query read from the assembly first.

### C5. Non-HP damage in the joint value (280 withheld, 736 on the simpler value) — **implemented**

**Done (20 November 2026):**
- Ability damage and negative levels join the shared-save outcomes as their HP equivalent (`DrainEquivalent`); a provider
  or a kill stays out.
- Joint value 5,321 → 5,651, mostly Wracking Ray.
- Enervation stays withheld: its undead branch grants temporary HP.


- **Proposal.** Carry drain and ability damage through `FusionDamageRead` per outcome, using the existing
  `DrainEquivalent`. Enervation's undead branch heals: it already reads per recipient. The value of a passed save keeps
  the same rules as for HP.

### C6. Support, reveal, form and fact carriers (672 withheld, 2,434 on the simpler value)

- **Proposal.** Split by mechanism, not into one bucket:
  - penalties through `DebuffShareOf` (Peer Through *, Curse of Magic Negation, Excruciating Deformation);
  - reveal utility through the existing hidden-enemy value;
  - size change (Reduce Person) through the existing form reader;
  - fear and shaken through control.

  Each mechanism is its own sub-slice, with fixtures of the actual buffs.

### C7. Dispel, enchantment, extension and dynamic removal (87 withheld, 1,010 on the simpler value)

- **Proposal.** Dispel Magic and Arcana Theft get the dispel worth per outcome. Baleful Polymorph's removal reads the
  buffs it actually removes, already listed by `BuffsRemovedFrom`.

### C8. Shared Hit Dice beyond the supported pools (1,034)

- **Proposal.** Circle of Death keeps its Hit Dice pool in the shared value `Damage`, read with `SubHD`. It has a
  Fortitude save, and a kill or damage branch by Hit Dice. Extend `SharedHdControl` to the kill pool. Sleep and Rainbow
  Pattern under the deliveries not yet read follow the same reader.

### C9. Nested saves (1,031)

- **Proposal.** Fusion F6 of [MAGIC_FUSION_PLAN.md](MAGIC_FUSION_PLAN.md): an explicit independent save node, starting
  with Phantasmal Killer and Banshee Blast.

### C10. Secondary areas and clicked-target payloads (999)

- **Proposal.** Fusion F8: Obsidian Flow and Haunting Mists, with their own footprints, durations and future-path danger.

### C11. Caster-side and healing payloads (310)

- **Proposal.** Fusion F4S: Vampiric Touch's temporary HP on the caster through the existing buff appraisal, never more
  than its marginal worth.

### Cross-cutting: damage over time on ordinary spells — **implemented** ([PERIODIC_DAMAGE.md](PERIODIC_DAMAGE.md))

- **Proposal.** Use C1's periodic carrier pricing for ordinary spells too (Acid Arrow, Molten Orb and the like cast
  unfused). It changes ordinary spell values broadly, so it is its own slice, with a before/after table of affected
  spells.

## 5. Measuring progress

Each slice reports these from the same inputs as the slice before it:
- the offered count, the joint-value count and every withheld reason;
- arcan's bar, row by row;
- every other `ProfileAudit` line, unchanged.

Gameplay acceptance comes from replays: in each mode, the fusion is cast at a sensible target, and its value line
(`[FusionValue]`) shows the outcome split. The author's `Log.txt` and `combatLog.txt` with the startup MVID are the
evidence.

## 6. Limits that stay

- **Random selection is not rolled.** This covers a recast on a random enemy, a summon, and a spawned area whose position
  is random. Such parts are either given no credit or priced at their expectation, and the plan names which one each
  time.
- **Native UI limits.** Configurations the native UI forbids (cantrips, already-fused sources) are not in the
  denominator.
- **Player-made choices.** The save, school and delivery are fixed when the fusion is created. AI Control chooses when
  and where to cast it, never how it is made.
- **Not played.** Offline checks prove readers and routes on real blueprints, not decisions in a fight.
