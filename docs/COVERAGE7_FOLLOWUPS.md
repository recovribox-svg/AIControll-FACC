# Coverage7 follow-ups (coverage8)

22 November 2026, Claude. Codex finished the coverage7 review and its corrections in draft PR #103
(`codex/coverage7-review`). The author asked me to take over, and approved anything not yet implemented that needs
approval, movement included, if it is of acceptable quality and worth adding. Both modes unless marked. Not played.

**Package:** [coverage8](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage8-17e3e1e-UMM-test.zip)
- startup MVID `828dc676-d296-4f0a-9e9a-70b6f6487c09`;
- UMM 0.3.60;
- checks: [COVERAGE8_CHECKS.json](competence/COVERAGE8_CHECKS.json);
- compiler and references: [COVERAGE8_BUILD_PROVENANCE.json](competence/COVERAGE8_BUILD_PROVENANCE.json).

It contains Codex's coverage7fix1 corrections. It is on `claude/coverage7-followups`, stacked on `codex/coverage7-review`
(draft PR #103) → `claude/coverage7-review` (#102) → `claude/fusion-coverage-c6-c8` (#101) → `codex/review-92-99` (#100).
Nothing is merged.

## Review of PR #103

I read Codex's diff against the decompiled game and agree with it. On its head `8e1dd27` I reproduced:
- Release build: 0 warnings;
- 1,110 unit tests;
- ProfileAudit exit 0. Against coverage7 only CAST-OUTCOME (new), OFFENSIVE-AFFLICTION (new), FREEING 12 → 13 and RELIEF
  24 → 32 differ;
- the Fusion matrix and summon detail are byte-identical.

One consequence needed work (change 1 below). With no weapon identity per hit, Codex withheld every weapon-filtered on-hit
rider. That fixed the Schir double count, but it also dropped riders whose filter matches the whole routine: a Shadow's
Strength drain only fires on its touch, and its touch is its only weapon.

## Changes

### 1. On-hit riders credited per attack (both modes)

**Native contract** (`AbstractWeaponTrigger.IsSuitable`):
- An item enchantment's rider fires only for its own weapon.
- Then the weapon filters apply: the blueprint, or the type, category, range type, fighter group and natural/unarmed type.
- OnlyOnFirstAttack fires on the first attack; OnlyOnFirstHit fires on the first hit only.

**Change:**
- The weapon estimate keeps each attack line's hit chance (`WeaponAttackEstimate.LineHits`); the leap re-quote does too.
- Each rider is fired by the lines whose weapon passes its filters (`CombatMath.RiderSuitsWeapon`).
- The count of firings is pure arithmetic (`OnHitRules.FiringHits`, 8 unit tests): the sum of those lines' hit chances,
  or the chance of at least one hit for a first-hit rider. A grapple that ends the routine scales it.
- Effect on the playtest's cases:
  - A Schir's bardiche disease counts on bardiche hits only, not on its gore.
  - A Shadow's drain counts on every touch.
  - A weapon enchantment's poison counts on its own weapon only. Codex's rule had removed it; the old aggregate count had
    credited it on every weapon.
- A rider's actions are read once a frame per attacker and victim.
- The positive reader now also reads a "target has fact" gate (the Shadow's) through the game's own condition checker, as
  Codex's reader already did for "target has buff".

**Still withheld:** flags that depend on the roll or the moment (criticals, sneak attack, natural 20, distance, damage
form, charge, extra or opportunity attacks, full attack only, dueling weapons, auto-hits). The threat reader for incoming
harm is unchanged.

### 2. Abandon Flesh at low HP (both modes)

Codex stopped Vermlek from dropping its Flesh Armor at full HP, and left the heal that removal gives unread.
- The heal from a removed caster buff's `Deactivated` actions is now valued as any heal of the caster
  (`CasterRemovalHeal`, `DeactivationHeal`). Only plain heals with fixed dice are read; Flesh Armor's is 2d6+3.
- Ordinary heals and this heal share one formula (`HealGain`, extracted from the heal loop unchanged).
- So Abandon Flesh is worth nothing at full HP and pays when the Vermlek is badly hurt. The game's own AI uses it in the
  same range: only when seriously injured (HP 20 % to 10 %).

### 3. Point dispels end areas (both modes)

Codex made Dispel Magic (Point) worth nothing on a unit, and left its real use unread.

**Native contract** (`ContextActionDispelMagic.RunAction`, `TryDispelAreaEffect`):
- With no target unit, the dispel tries every area effect in the game's list, in that order, whose view contains the
  point. All-area effects are skipped.
- Each area needs a caster and a source ability, and must match the dispel's schools and descriptor (and be an enemy's,
  with OnlyEnemyAreaEffects).
- It rolls against 11 + the area's caster level, and stops after its count (one for Dispel Magic).

**Change** (`PointDispelValue`):
- Each area is worth what it does to the units inside over the rounds it still lasts, up to the relief horizon of three.
  It is a relief for allies and a loss for enemies, whoever cast it.
- Order and count use the same expected-value code as unit dispels.
- A point dispel is also aimed where an ally stands inside an area.
- In practice: dispelling an enemy's Stinking Cloud or Web over the party pays. Dispelling the party's own Grease on
  enemies costs.

### 4. Advances through an area the real path crosses (real time; author-approved movement)

**Evidence** (Codex's review, fight 7): Babau and Schir had 13 advances refused at issue ("the path walks into Corrosive
Bomb") and stood idle.
- Each endpoint lay clear of the area, but the game's own path to it crossed the area.
- Walks to attack through the same area were priced and issued.

Codex was right that refusals are already remembered. The gap was that the advance had no priced form.

**Change:**
- When an advance's real path is refused for entering an area, the dispatcher records the path segment that enters it.
- The next advance build on that target then also offers the advance priced as passing through that area, and issues it
  without the area check (`AddThroughAreaAdvance`).
  - The price is `AreaStayCost`, passing through, as an attack walk into an area is priced.
  - An area that can stop the walk takes its share of the advance's worth.
- It is chosen only if it still pays.
- Bounds: one crossing per unit and target, for 12 s while the unit stays within 1 m. No extra path query.
- Turn-based is unchanged.

### 5. Measurement and diagnostics (no behaviour change)

- **Approach planning** (Codex's performance proposal, step 1): the slow-decision row now reports approaches planned
  (cache misses), their time, how many were reused, and how many spot searches ran and their time. This tests whether
  Light's ~100 ms really is the free-side search before any pruning. No pruning is added: a score where the caster stands
  now is not an upper bound once it walks, as Codex showed.
- **Faction rows:** a `[Faction]` row when an ally turns (an enemy's control) or comes back, as AI Control sees it. It
  gives faction and original faction, player faction, direct control and the switching effect, with short unit IDs; at
  most 16 a fight. A row "turned (AI Control decides for it)" would show the control bypass Codex could not establish.

## Checks

On `17e3e1e`:
- Release build: 0 warnings.
- Tests: 1,118 unit (Codex's 1,110 + 8) + 27/21/3 script tests.
- ProfileAudit exit 0. Against Codex's head on the same inputs:
  - new `COVERAGE7-FOLLOWUPS` 20 checks: the actual Flesh Armor heal and Abandon Flesh graph, the native area-dispel
    contract, the actual Dispel Magic (Point), and the routes of changes 2–5;
  - `OFFENSIVE-AFFLICTION` 18 → 27: the actual Schir and Shadow triggers suit their own weapons; target fact gates;
    roll-dependent flags stay withheld; both quotes record line hits;
  - `APPROACH-SPOT`'s route check reads `PlanApproachMiss` (same 28 checks);
  - every other line is identical; the Fusion matrix and summon detail are byte-identical.
- Native harnesses: 98/14/10.
- MAGUS: 32 checks in this scope. The 44-check run with stock fixtures was not repeated; no Magus code changed.
- 3 game references added, all compiled against the supplied assembly: `AbstractWeaponTrigger.Group`,
  `ContextActionDispelMagic.OnlyEnemyAreaEffects`, `SimpleBlueprint`'s implicit bool.

## Replay (coverage8)

Record the startup MVID `828dc676`. Send `Log.txt` and `combatLog.txt`, and say where you spawned enemies, rested, or
gave manual orders.

1. **Summons with on-hit riders** (Schir, Quasit, a Shadow if one is summoned): their full attacks against ordinary
   enemies should not be dominated by a disease. With a filtered rider, compare the decision value with the hits on the
   matching weapon.
2. **Vermlek:** keeps Flesh Armor at full HP; when badly hurt, a row may show Abandon Flesh with "its removal heals ~10".
3. **Dispel Magic (Point)** (Babau, Knight Commander, arcan): when an enemy area holds party members, look for
   "Dispel Magic (Point)->ally(ends areas +N (k there))". It should not be cast at an enemy's feet with no area there.
4. **Real time, an area between a summon and the enemies:** after one refusal, an "Advance->X [through Corrosive Bomb]"
   option should appear and, when it pays, be issued instead of waiting.
5. **Slow decisions:** look for "approaches planned N (… ms, M reused), spot searches K (… ms)".
6. **Domination:** keep any `[Faction]` rows.
7. Codex's replay (in [the review](COVERAGE7_PLAYTEST_REVIEW.md)) still applies.

## Limits

- Riders whose effect depends on the roll or the moment (criticals, sneak attack, charge) stay withheld. A rider's direct
  HP damage counts as utility, not toward a kill (Codex's rule).
- A removal's heal reads only plain fixed-dice heals.
- Caster removals inside conditionals are flat metadata (Codex's limit): the loss and the heal can apply when a branch
  would not remove the buff.
- Area worth uses the danger analyzer's levels, not a full damage forecast.
- A point dispel aimed at an ally's position ends areas there; one that could end several areas at once is still aimed
  one ally at a time.
- The through-area advance uses the recorded entry segment. A different route to the same goal may cross elsewhere.
- No pruning of touch planning yet; the new timings are for deciding it.
- Codex's open items stand: mortality from stat or level drain, lingering hostility after a re-save, and the Drow Magus
  HP reading.
