# Appendix to AI_TUNING_AUDIT.md: audit reports (read-only, 0.3.17 cycle)

These are the four audit reports behind `docs/AI_TUNING_AUDIT.md`. Line numbers refer to the code at commit 82ca4e5 (0.3.17-test1). Paths into a scratchpad folder refer to the session's working files and are not in the repository.

## A. Parameter inventory


On the coordinator's resume message: I had already read all of TacticalEngine.cs (lines 1-2156) and every other scope file. This is the complete report.

Paths are under `/home/user/CombatAIPWOTR/`. TE = Tactics/TacticalEngine.cs, CM = Tactics/CombatMath.cs, RM = Tactics/RiskMath.cs, ADA = Analysis/AoeDangerAnalyzer.cs. A value applies to both turn-based and real-time unless marked **TB** or **RT**.

### 0. Selection, thresholds, context

- **TE:104 `MinMainValue = 0.5f`** and **TE:105 `MinQuickValue = 2f`**.
  - Used in Decide (164-165), in ApplyProgressive's GateItem.Threshold (274), in EvaluateAbility's bestViable test (1318), in `anyAttackNow` (1772, `c.Value >= MinMainValue`), in the step-out gate (1818, `followUp >= MinQuickValue`), in withdraw (1855) and in leave area (1967).
- **Decide, TE:145-165 (verbatim):**
  ```
  var usable = candidates.Where(c => c.Value > 0f && Affordable(ctx, c) && !memory.Failed.Contains(c.Key) && !SilentlyFailing(ctx, c))
      .OrderByDescending(c => c.Value).ToList();
  ... ApplyProgressive (151-155) ... CoupleOwnAttackMarks (157) ...
  var quick = usable.FirstOrDefault(c => c.Slot == ActionSlot.Free || c.Slot == ActionSlot.Swift);
  var main = usable.FirstOrDefault(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift);
  if (quick != null && quick.Value >= MinQuickValue) chosen = quick;
  else if (main != null && main.Value >= MinMainValue) chosen = main;
  ```
  A quick action of 2 or more always wins over any main action, whatever the main action's value.
- **PriceHeldCharge, TE:197-212.** When a touch charge is held, every non-item spell or spell-like candidate gets `c.Value -= held`, where `held` is the maximum value of the StickyTouch candidates.
- **CoupleOwnAttackMarks, TE:233-258.** A mark for the caster's own attacks is kept only if it targets the best main action's enemy (re-aimed through EvaluateAbility), otherwise it is dropped. This is selection logic, not a constant.
- **BuildContext:**
  - **ExpectedRounds, TE:417-422 (verbatim):**
    ```
    float enemyHp = ctx.Enemies.Sum(e => (float)e.HPLeft);
    float enemyThreat = ctx.Enemies.Sum(CombatMath.Threat);
    float partyPower = ctx.Allies.Sum(a => 4f + 3f * Math.Max(1, a...CharacterLevel ?? 1));
    float partyLevels = ctx.Allies.Sum(a => (float)Math.Max(1, ...CharacterLevel ?? 1));
    ctx.ExpectedRounds = Mathf.Clamp(1f + enemyHp / Math.Max(10f, partyLevels * 4f), 1f, 6f);
    ctx.Difficulty = Mathf.Clamp(enemyThreat / Math.Max(1f, partyPower), 0.1f, 2f);
    ```
  - **MoveRange:**
    - TB with a turn controller: game-provided remaining movement (352).
    - **RT** (375): `CurrentSpeedMps * 6f`. Comment: "the horizon is one round of it".
    - Otherwise (379): `CurrentSpeedMps * 3f`.
  - TE:366: `FiveFootRange < 0.1f` sets NoMovementLeft (**TB**).
  - TE:397: downed allies are listed only within `< 30f` m.
- **Affordable, TE:283-303.** Eligibility only, no constants.

### 1. Shared helpers (verbatim)

- **CombatMath.Threat, CM:517-528:**
  ```
  float cr = 1f; try { cr = Math.Max(1, enemy.Blueprint?.CR ?? 1); } catch { }
  float threat = 4f + 3f * cr;
  if (IsDisabled(enemy)) threat *= 0.25f;
  try { if (enemy.CombatState.EngagedUnits.Count > 0) threat *= 1.2f; } catch { }
  ```
- **DamageValue, TE:869-878:**
  ```
  if (expectedDamage <= 0f) return 0f;
  float hp = Math.Max(1, enemy.HPLeft); float threat = CombatMath.Threat(enemy);
  float value = Math.Min(expectedDamage, hp);
  if (expectedDamage >= hp) value += threat;              // likely kill: its future damage is prevented
  else value += threat * 0.25f * (expectedDamage / hp);   // progress towards a kill, weighted by threat
  ```
- **WakeFactor, TE:437-442:** `asleep && expectedDamage < enemy.HPLeft ? 0.5f : 1f`. Comment: "wakes it ... throws away the control already paid for: worth half".
- **RoleWeight, TE:880-891:**
  ```
  Support: damage 0.85, heal 1.6, buff 1.5, else 1
  Tank:    damage 1.0, buff 0.9, else 1
  DPS:     damage 1.1, buff 0.7, heal 0.9, else 1
  ```
  Categories actually passed in: "damage", "heal" (also used for cleanse) and "buff" (also used for the enchant fallback). Control, mark, summon, dispel, utility and burn get no role weight.
- **PreferenceWeight, TE:1006-1014:** Ranged preference gives ranged 1.1 / melee 0.7. Melee preference gives ranged 0.8 / melee 1.1. Mixed (the default, ModSettings:236) gives 1.
- **AllyWorth, TE:836-843:**
  ```
  float worth = (4f + 3f * level) * Math.Min(Math.Max(1f, ctx.ExpectedRounds), 3f);
  if (IsSummon(ally)) worth *= 0.5f;
  ```
- **RiskTolerance, TE:608:** Tank 0.6, other roles 1. Comment: "pays 60 % of the damage and exposure".
- **CautionBelow, TE:614:** Tank 0.35, other roles 0.5. Comment: "half its HP, a third for a tank".
- **IncomingDamage, TE:475-496.**
  - Reach zone: `ranged ? 20f : CurrentSpeedMps*3f + 2f + Corpulence`, with fallback `11f` (489).
  - Each enemy adds `Threat / sharedBy`, where sharedBy is the number of allies within that enemy's zone.
- **RiskMath (RM):**
  - `ChanceAtLeast = Clamp01((1.5*perHit - hp)/perHit)` (20). Damage is modelled as uniform between 0.5 and 1.5 times the average.
  - AttackDropChance: lines 27-34.
  - `RoundDropChance = exp(-max(1,hp)/danger)` (48).
- **PriceAttacks, TE:621-639.** Fallbacks when there is no estimate:
  - hit chance `0.5f` (629)
  - expected damage `Threat*0.35f` (630)
  - crit chance `0.05f*hit` (632)
  - crit multiplier `2f` (633)
  - per-hit floor divisor `0.05f` (631)

### 2. Damage term

**Weapon attacks (AddWeaponCandidates, TE:914-1004).**
- **RT** (919): candidates are listed even while the action is on cooldown.
- Five-foot-step reach (934-935): `dist <= reach + FiveFootRange * 0.85f` (**TB**; RT has no five-foot step).
- Verbatim, 937-943:
  ```
  float weight = RoleWeight(ctx, "damage") * PreferenceWeight(ctx, single.IsRanged);
  if (Tank && enemy engaged by another ally) weight *= 1.25f;
  aooPenalty = single.IsRanged && Engagers.Count>0 ? ctx.Engagers.Sum(CombatMath.Threat) * 0.35f : 0
  ```
- Full attack (957-958): `DamageValue(enemy, full.ExpectedDamage) * weight * (fiveFootReach ? 0.95f : 1f) * WakeFactor(...) - aooPenalty`. Requires `full.Attacks > 1`.
- Single attack (975): `DamageValue(enemy, single.ExpectedDamage) * weight * WakeFactor(...) - aooPenalty`.
- Move+Attack (980-990):
  - Eligible when `dist - reach <= MoveRange * 0.95f`.
  - Value: `DamageValue(...) * weight * 0.9f * WakeFactor(...)`, then `value*(1-risk.StopChance) - risk.Cost`.
  - It does **not** subtract aooPenalty.

**Weapon estimate (CM:105-164).**
- `HitProbability = Clamp((21+AB-AC)/20, 0.05, 0.95)` (54).
- Crit extra: `threatChance*p*(mult-1)*perHit`.
- Sneak attack: `3.5f*sneakDice` when flanked or the target is flat-footed (153).
- First crit chance: `threatChance*p*p` (147).

**Charge (EvaluateCharge, TE:1347-1377), full-round.**
- Only offered when `dist - reach > MoveRange*0.95f` (1357).
- Value (1359-1360): `DamageValue(enemy, single.ExpectedDamage * 1.1f) * RoleWeight("damage") * PreferenceWeight(false) * 0.95f`, then MoveRisk.

**Coup de Grace (TE:456-465).**
- `kill*(max(1,HP)+Threat) + (1-kill)*DamageValue(target,damage)`, times `RoleWeight("damage")`, minus `ΣThreat(other engagers)*0.35f`, floored at 0.
- Underlying math (CM:173-208):
  - damage = `perHit*max(2,critMult)`, plus `3.5*sneakDice`.
  - kill = 1 if damage ≥ HP, else `Clamp((10+dmg - fort - 1)/20, 0.05, 0.95)`.

**Ability damage (ScoreAbility, TE:1437-1464), verbatim:**
```
dmgValue += DamageValue(enemy, dmg * p) * WakeFactor(enemy, dmg * p);
... float allyCost = affectedAllies.Sum(a => Math.Min(dmg, a.HPLeft)) * 2f;
if (ctx.Settings.AvoidFriendlyFire && allyCost > 0f) { why = "friendly fire"; return 0f; }
dmgValue -= allyCost;
if (dmgValue > 0f) value += dmgValue * RoleWeight(ctx, "damage");
```
- Comment on the friendly-fire term: "hurting an ally costs twice".
- AvoidFriendlyFire defaults to true (ModSettings:243), which turns this into a veto.
- Inputs:
  - `EffectChance` (CM:412-422): SR pass × attack-roll hit (clamped 0.05-0.95, fallback 0.6, CM:474-478) × save. For damage the save term is `fail + (1-fail)*(HalfOnSave?0.5:0)`.
  - `SaveFailChance` = `Clamp((dc-bonus-1)/20, 0.05, 0.95)`, fallback 0.5, DC fallback 10 (CM:430-451).
  - EstimateAbilityDamage evaluates dice with caster level capped at `Math.Min(cl, 10)` (CM:291).
  - `dmg.Half` halves the damage (292).
  - Empower ×1.5 (244).

### 3. Heal term (TE:1468-1534)

Revive branch (1493-1507), verbatim:
```
float needed = Math.Max(1f, 1f - ally.HPLeft + ally.DamageNonLethal);
float share = Math.Min(1f, heal / needed);
float upChance = share >= 1f ? 1f : 0.6f * share;
if (share >= 1f) upChance *= ReviveStaysUp(ctx, ally, Math.Max(1f, heal - needed + 1f));
healValue += upChance * AllyWorth(ctx, ally);
```
- Comment: "x0.6: the ally is still down until the next heal lands".
- ReviveStaysUp (TE:648-665): round drop uses `IncomingDamage*1.25f` (652), times the stand-up attacks of opportunity.

Normal branch (1510-1526), verbatim:
```
if (hp >= threshold) continue;
float urgency = hp <= 0.25f ? 3f : hp < 0.5f ? 2f : 1f;
healValue += heal * 0.6f * urgency * (threatened ? 1.25f : 1f);
float before = Mathf.Exp(-Math.Max(1f, ally.HPLeft) / danger);
float after  = Mathf.Exp(-(Math.Max(1f, ally.HPLeft) + heal) / danger);
healValue += (before - after) * AllyWorth(ctx, ally);
```
- Then (1530): `value += healValue * RoleWeight("heal")`.
- **HealThreshold (CM:74-80):**
  - Own potion (item on self): `PotionHealThresholdPercent/100`, default 50 (ModSettings:275).
  - Otherwise: `Max(0.35f, HealAtHPPercent/100 + 0.2f)`, with HealAtHPPercent defaulting to 30 (ModSettings:240), so the default threshold is 0.5.
- EstimateAbilityHeal caps caster level at `Math.Min(cl, 5)` (CM:372).

### 4. Control / debuff / mark term (TE:1539-1626)

Strength (1550-1557), verbatim:
```
float strength = Hard ? 1f : Soft ? 0.4f : isMark ? (oneShotMark ? 0.1f : 0.15f) : 0.25f;
if (profile.ControlStrength == ControlStrength.None && CombatMemory.TimesUsed(unit, ability) >= 2) strength *= 0.3f;
float effectRounds = profile.IsOneShot ? 1f : Math.Min(ctx.ExpectedRounds, 3f);
```
- Comment on the repeat factor: "'spam' pattern: diminish them".

Allies caught in a harmful area (1576-1577): `(4f + 3f*level) * strength * effectRounds` per ally. With AvoidFriendlyFire this is a veto (1575).

Allies at the edge or heading into a spawned area (1593-1595):
- Counted if within `AoERadius + 2.5f + Corpulence`, or if their nearest enemy is inside the area.
- Cost: `0.5f * (4f + 3f*level) * strength * effectRounds`. Comment: "Half the cost of an ally caught inside".

Per enemy (1608-1618), verbatim:
```
float rounds = effectRounds; float own = profile.EffectRoundsOn(ability, enemy);
if (own > 0f) rounds = Math.Min(rounds, Math.Max(1f, own));
float enemyValue = p * CombatMath.Threat(enemy) * strength * rounds;
if (CombatMath.IsDisabled(enemy)) enemyValue *= 0.1f;
if (oneShotMark && !(enemy engaged by another ally)) enemyValue *= 0.5f;
```
- The result is added with no RoleWeight (1623).
- Duration inputs (AbilityProfile.cs:671-694):
  - permanent = 100 rounds
  - seconds/6, at least 1
  - rate multiplier: minutes 10, ten minutes 100, otherwise 600
  - non-simple duration in non-round units = 10
  - day-long marker: hours with bonus ≥ 20 (649)
  - one round = seconds ≤ 6.01 (723)

### 5. Buff term (TE:1629-1682; duplicate public BuffWorth at TE:1048-1070)

Verbatim, 1668-1679:
```
float perRound = 1f + level * 1.5f;
float rounds = BuffsLastOneRound ? 1f : BuffsSpentByAttack ? Math.Min(ctx.ExpectedRounds, 2f)
             : HasLongBuff ? ctx.ExpectedRounds : Math.Min(ctx.ExpectedRounds, 3f);
buffValue += perRound * rounds * quality * (fighting ? 1f : 0.6f);
...
float timing = ctx.Round <= 2 ? 1f : 0.6f;
value += buffValue * timing * RoleWeight(ctx, "buff") * Math.Min(1.2f, 0.4f + ctx.Difficulty);
```
- At most 5 recipients (`recipients.Take(5)`, 1643).
- A mode switch that removes a buff is skipped if `quality <= lost + 0.05f`, otherwise `quality -= lost` (1663-1664).
- **BuffQuality (TE:1093-1097):**
  - analyzer CombatValue when recognized
  - Enchants: 0.8
  - only unrecognized components: 0.5, or 0 for items
  - no components at all: 0.05
  - items with q < 0.5 are set to 0
- **BuffWorth** has the same formula but no timing, RoleWeight or Difficulty factor, and `expectedRounds = 3f` by default. Its only caller is Scoring/Scorers/BuffScorer.cs:485. TB ScoreAbility does not call it.
- **RT:** `ctx.Round` counts six-second spans (comment TE:308), and that is what feeds the `timing` gate.

### 6. Cleanse, burn, summon, enchant, dispel, utility

- **Cleanse (1691-1695):** `(2f + 2f*level) * Min(ExpectedRounds,3f) * (fighting?1:0.6f)`, times `RoleWeight("heal")`.
- **Burn relief (1130-1131):** `(burn>0 ? 4f : 0f) + 1.5f*Min(ExpectedRounds,3f)`. It requires souls whose total HD reaches the caster's level (1118-1127).
- **Once-per-combat fallback for unverifiable effects (1704-1746).** Runs only when `value <= 0`, the effect is not verifiable, and the ability has not been used on this target:
  - Summon (1713): `(3f + 3f*level) * Min(ExpectedRounds,4f) * 0.5f * (Enemies.Count>=2 ? 1f : 0.6f)`
  - Enchant (1722): `(1f + level*1.5f) * ExpectedRounds * 0.5f * RoleWeight("buff")`. ExpectedRounds is uncapped here (up to 6). Returns 0 if the enchantment is already active.
  - Item: 0
  - Dispel / RemovesBuffs: `+2f`, only if HasSomethingToDispel
  - Utility (1742): `+1f`, only if `TimesUsed == 0`. Comment: "only when nothing better exists"
- **Spell-slot difficulty multiplier (1749-1750).** Applies to the whole value (damage, heal, buff and the rest) for level > 0 spellbook spells that are not items: `value *= Clamp(0.55f + 0.45f*Difficulty, 0.55f, 1f)`.

### 7. Post-score ability modifiers (EvaluateAbility, TE:1270-1297)

- **ConsumableFactor (1170-1186):**
  - potion: 1 if it heals, else `Difficulty >= 0.8f ? 0.5f : 0f`
  - scroll or wand: `0.7f * UMD chance`
  - other items: `0.85f * UMD chance`
  - UMD chance = `Clamp((21+skill-dc)/20, 0.05, 1)`, or 0 without the skill (CM:483-497)
- Needs a move: `*0.9f` (1273). Eligible only if Standard slot and `dist - range <= MoveRange*0.95f` (1262).
- Sticky touch out of reach by height is skipped when the flat distance is ≤ `range*0.9f` (1260).
- CastProvokeRisk (1283-1289): `value*(1-StopChance) - Cost`.
- `*CastingConcentrationChance` (1292), where the chance is `Clamp01((21-(15+SL[+SL]-conc))/20)` (CM:397-399).
- Archer walking up to touch an enemy: `*0.5f` (1295-1296).
- MoveRisk (1297), called with `alwaysExposure: true` (1266).
- Progressive policy: bestViable requires `SuccessChance >= SpellTierPolicy.MinSuccessChance (0.25f)` (SpellTierPolicy.cs:51) and `value >= slot threshold` (1318-1319).
  - `ApplyProgressive` passes mode "TB" and `enemiesComplete: true` in both modes (TE:279).
  - Boss bypass defaults on (ModSettings:280).

### 8. Move risk, attack-of-opportunity cost, area cost

- **MoveRisk, area part (TE:538-544), verbatim:**
  ```
  float perRound = AllyWorth(ctx, unit) / Math.Min(Math.Max(1f, ctx.ExpectedRounds), 3f);
  float rounds = endsInside ? Mathf.Clamp(Math.Min(roundsLeft, ctx.ExpectedRounds), 1f, 3f) : 1f;
  risk.Cost += danger * perRound * rounds * (endsInside ? 2f : 1f);
  ```
  Comment on the ×2: "standing up provokes ... they hit it at +4".
- **MoveRisk, exposure part (552-562):**
  - Gated by `alwaysExposure || HpPct < CautionBelow` and `exposure > 0.02f`.
  - Cost: `exposure * AllyWorth * RiskTolerance`.
- **DisengageRisk (576-601):**
  - Skipped if the move fits a five-foot step: `dist <= FiveFootRange*0.9f` (580).
  - Cost (599): `dropNow*worth + ((1-dropNow)*max(0,after-before)*worth + damage*0.3f) * RiskTolerance`.
  - The path is sampled every `0.75f` m (740).
  - **RT** (821-822): an enemy also attacks on leaving when it engages ≤1 unit or this unit was its last target.
- **CastProvokeRisk (770-801):**
  - Check DC `15 + 2*SL`; `fail = Clamp01((dc - conc - 1)/20f)`, fallback 0.5.
  - `StopChance = fail*(dropNow + (hitChance-dropNow)*0.5f)`
  - `Cost = fail*(dropNow*worth + damage*0.3f*RiskTolerance)`
- **ShouldStandUp (TB, 711-720):**
  - `stayDrop = RoundDropChance(hp, danger*1.25f)`
  - `stand = -dropNow*worth + (1-dropNow)*(act - afterDrop*worth)`
  - `stay = -stayDrop*worth`
- **ApproachPoint (832):** stops at `range * 0.9f` from the target, minimum `0.1f`.
- **ADA danger levels (EvaluateEffectDanger, ADA:444-479):**
  - additive: damaging +0.6, movement-impairing +0.4, Death +0.8, Poison +0.3, Stun +0.5, Paralysis +0.6, Fear +0.3
  - Hard control: `max(d,0.3)+0.5`; Soft control: `max(d,0.4)`
  - total = `Clamp01(d) * AffectChance`
  - on an exception the level is `0.3f` (490)
- **AffectChance (ADA:597-640):**
  - save fail `Clamp((dc-bonus-1)/20, 0.05, 0.95)`; damage that halves on a save counts `0.5+0.5*fail`
  - times `Clamp01(secondsLeft/6)`
  - fallback 1
- **RoundsLeft:** 10 when the end is unknown (ADA:658).
- **InsideDanger:** `worst + 0.25f*rest` (ADA:260). This can exceed 1.
- `DANGER_THRESHOLD_ESCAPE 0.5f` (ADA:30) is used only by ShouldEscape (legacy and real-time paths). `DANGER_THRESHOLD_AVOID 0.3f` (ADA:31) is defined but not referenced.

### 9. Move candidates (AddMovementCandidates, TE:1760-1980)

- **Step or back off out of melee (1790-1834):**
  - `followUp` is the best main action plus its CastRiskCost, or for an archer the best ranged `DamageValue*RoleWeight("damage")`.
  - Gate: `followUp >= MinQuickValue`.
  - Distance: **RT** `4f`, **TB** `FiveFootRange*0.9f`.
  - Value: `Min(ΣThreat(engagers)*0.4f, followUp)`; **RT** also subtracts MoveRisk.Cost.
  - Slot: **TB** Free, **RT** Move.
  - The kept target must be within `shotRange*0.95f`.
  - `anyAttackNow` (1772-1774): **RT** ignores Affordable.
- **Withdraw (1841-1866):**
  - Gate: `HpPct < CautionBelow`.
  - Search distance: `MoveRange`, or `speed*3f`.
  - Verbatim: `aoo = ΣThreat(engagers)*0.5f; dangerThere = IncomingDamage(point)+aoo; value = (exp(-hp/dangerHere) - exp(-hp/dangerThere)) * AllyWorth`
  - Offered only if `>= MinMainValue`.
- **Advance (1871-1909):**
  - Target choice: `DamageValue/Max(3f, dist)`.
  - Radius: ranged `Reach*0.85f`; casters at least `CasterApproachRadius = 8f` (TE:107).
  - Value: `Max(0.6f, DamageValue*0.35f) - risk.Cost`.
- **Leave area (1921-1980):**
  - Maximum distance `Min(9f, MoveRange or speed*3f)`; aborts if `< 1f`.
  - Value: `inside.Danger * 0.5f * AllyWorth`, then `*(1-StopChance) - Cost`.
  - Requires `>= MinMainValue`; the kept target must be within `Reach*0.95f`.
- **FindRetreatPoint (1983-2014):**
  - 12 directions × 30°.
  - Score: `nearestEnemy - 0.15f*dist(allyCenter)`; must beat the current spot by `+0.5f`.
  - **RT:** navmesh validity check.
- **FindExit (ADA:515-528):**
  - Rings 1.5/3/4.5/6/9 m, 16 directions × 22.5°.
  - Score: `(keeps?10:0) - ring + 0.1*min(10, away)`.
- **IssueStandUp:** rings 1.5 and 2.5 m, at most 4 attempts (TE:2041-2051).

### 10. Logged-only thresholds (ThreatAssessment.cs:61-65)

`CR >= partyLevel + 3` and `Difficulty >= 1.25f` produce "would bypass" log lines only; they never change a decision.

---

### (a) Same concept valued on different scales

1. **Worth of a unit.** All use `4+3×level/CR` as the per-round base, multiplied differently:
   - AllyWorth: ×min(rounds,3), ×0.5 for summons (TE:839)
   - Threat: ×1 per round, ×1.2 engaged, ×0.25 disabled (CM:522-526)
   - allyControlCost: base × strength × effectRounds, no summon halving and not via AllyWorth (TE:1576, 1595)
   - partyPower: base only (TE:419)
   - Hostile control is valued as Threat×strength×rounds, while protecting an ally (withdraw, heal, leave area) uses AllyWorth.
2. **Attacks-of-opportunity / engager cost.**
   - Flat `Threat×0.35` for ranged weapon attacks (TE:943) and Coup de Grace (463); no StopChance.
   - `×0.4` for step-out value (1821); `×0.5` added to danger in withdraw (1850).
   - Modelled through PriceAttacks/RiskMath for Move+Attack, Charge, Advance and casting (CastProvokeRisk).
3. **Damage taken.** `×0.3` HP cost in DisengageRisk/CastProvokeRisk (599, 799). Friendly-fire damage costs `×2` (1454). Control on an ally costs 1:1 with the enemy-equivalent (1576).
4. **Drop chance.** RiskMath.RoundDropChance (MoveRisk, DisengageRisk, ShouldStandUp) and inline `Mathf.Exp` in heal (1523-1524) and withdraw (1852-1853). Withdraw folds attacks of opportunity into danger instead of using PriceAttacks.
5. **Round horizons.**
   - Min(ER,3): AllyWorth, MoveRisk perRound/rounds, control, normal buffs, cleanse, burn
   - Min(ER,2): spent-by-attack buffs
   - Min(ER,4): summon
   - uncapped ER (up to 6): long buffs (1673), enchant fallback (1722)
   - BuffWorth default 3 (1048)
6. **Role weights.** Applied to damage, heal, cleanse, buff and enchant; not applied to control, mark, summon, dispel, utility, burn or movement.
7. **Moving before acting.**
   - Move+Attack ×0.9 (982); ability needs move ×0.9 (1273); five-foot full attack ×0.95 (957)
   - Charge ×0.95 and damage ×1.1 (1359-1360)
   - Move+Attack has no aooPenalty, the in-place ranged attack does
8. **Heal thresholds.** Own potion: setting, no margin (CM:78). Other heals: setting + 0.2 with a 0.35 floor (CM:79).
9. **Five-foot / range fractions.**
   - FiveFootRange×0.85 (TE:935) vs ×0.9 (580, 1817)
   - MoveRange×0.95 (980, 1262, 1357)
   - approach ×0.9 (832, 1260); shot/reach ×0.95 (1802, 1947); ranged advance ×0.85 (1882); TB touch walk ×0.8 (2112)
10. **Real-time movement horizon.** RT MoveRange = speed×6 (375); fallbacks use speed×3 (379, 1847, 1931).

### (b) Duplicated constants

- `4f + 3f*level/CR`: TE:419, 839, 1576, 1595; CM:522.
- `Math.Min(ctx.ExpectedRounds, 3f)`: TE:538, 839, 1062, 1130, 1557, 1673, 1691; clamp 1-3 at 539.
- `1f + level*1.5f` (buff per round): TE:1059, 1668, 1722.
- Not-fighting factor `0.6f`: TE:1064, 1674, 1691.
- Buff `rounds` ternary: TE:1060-1062 and 1671-1673 (BuffWorth duplicates ScoreAbility).
- `Threat*0.35f`: TE:463, 630, 943.
- `damage*0.3f`: TE:599, 799.
- `*1.25f` danger while lying: TE:652, 715. (Also 1.25 Tank weight at 939 and threatened heal at 1516, different meanings.)
- Save-fail `Clamp((dc-bonus-1)/20, 0.05, 0.95)`: CM:447, CM:199 (Coup de Grace), ADA:623. Unclamped variant at TE:784.
- `MoveRange*0.95f`: TE:980, 1262, 1357.
- `speed*3f` fallback: TE:379, 1847, 1931.
- 12×30° direction rings: TE:1993-1995, 2043-2044.

### (c) Hard thresholds (cliff edges)

- MinQuickValue 2 / MinMainValue 0.5 in Decide (164-165). A quick action ≥2 pre-empts any main action. The same values gate withdraw (1855), leave area (1967), step-out (1818) and anyAttackNow (1772).
- DamageValue kill jump: below HP the bonus is `0.25×threat×ratio`, at or above HP it is the full threat (875-876).
- WakeFactor 0.5 vs 1 at `expectedDamage < HPLeft` (441).
- Heal urgency 3/2/1 at `hp <= 0.25` / `< 0.5` (1514). Heal skipped at `hp >= HealThreshold` (1512).
- Revive `share >= 1` gives 1×staysUp, otherwise 0.6×share (1497).
- CautionBelow 0.5 / Tank 0.35 gates exposure (552) and withdraw (1841). Exposure counts only when `> 0.02` (557).
- Buff timing ×1 vs ×0.6 at `Round <= 2` (1678). In RT the round is a 6-second span.
- Potion factor 0.5 vs 0 at `Difficulty >= 0.8` (1179).
- Debuff/mark strength ×0.3 at `TimesUsed >= 2` (1554). Utility only at `TimesUsed == 0` (1739).
- Summon ×1 vs ×0.6 at `Enemies.Count >= 2` (1713).
- Disabled enemy: Threat ×0.25 (CM:524), control ×0.1 (1614).
- Tank weapon weight ×1.25 when an ally engages the target (938-939).
- Item buff quality below 0.5 becomes 0 (1097). Mode switch skipped at `quality <= lost+0.05` (1663).
- Progressive `SuccessChance >= 0.25` (SpellTierPolicy:51).
- Charge only beyond `MoveRange*0.95` (1357). Five-foot-step move: no attack-of-opportunity check at ≤ `FiveFootRange*0.9` (580).
- Downed allies counted only within `< 30` m (397). Leave area requires a max distance ≥ 1 m (1932).
- AoE: `ShouldEscape` at TotalDanger ≥ 0.5 (ADA:48). Any danger > 0 rejects a retreat or exit point (TE:1999, ADA:523).
- Engaged enemy Threat ×1.2 (CM:526) switches on at `EngagedUnits.Count > 0`.


## B. Log decision statistics

**Log decision statistics (7 logs: 5 turn-based, 2 real-time)**

**Script:** `/tmp/claude-0/-home-user/e5dcce11-ec8d-5243-a475-f41c2ff178bc/scratchpad/logstats/logstats.py`. Run it with `python3 -I logstats.py <uploads dir> <file>...`. The full output is in `/tmp/claude-0/-home-user/e5dcce11-ec8d-5243-a475-f41c2ff178bc/scratchpad/logstats/out.txt`. Three drill-downs are in the same folder: `extra.py`, `extra2.py` and `extra3.py`. Nothing in the repo was modified.

**Parsing notes:**
- 4229 decision lines were parsed. I left out 1238 verbose `[V]` RT "=> wait" lines that list no options, which leaves 2991.
- `9c5c6df2` contains both TB (R) and RT (RT) lines. I split them by the round tag.
- Each command outcome is the first `ENDED` line for the same unit after the decision and before that unit's next decision.
- **Interrupt is not a failure marker on its own.** A TB `Attack` or `FullAttack` normally ends with `result=Interrupt acted=True`.

---

#### 1. Chosen category (n per character and mode)

| char/mode | n | weapon | dmg | heal | buff | cc/deb | mark | summon | move | end/wait |
|---|---|---|---|---|---|---|---|---|---|---|
| Aeiluvetar TB | 321 | 159 | 0 | 0 | 0 | 0 | 2 | 0 | 24 | 136 |
| Aeiluvetar RT | 218 | 133 | 0 | 4 | 0 | 0 | 8 | 0 | 73 | 0 |
| Camellia TB | 286 | 48 | 100 | 14 | 21 | 27 | 0 | 0 | 19 | 57 |
| Camellia RT | 186 | 69 | 7 | 37 | 19 | 15 | 5 | 0 | 9 | 25 |
| Lann TB | 293 | 162 | 0 | 0 | 4 | 0 | 0 | 0 | 3 | 124 |
| Lann RT | 175 | 124 | 0 | 15 | 2 | 0 | 1 | 0 | 33 | 0 |
| Orvath TB | 463 | 122 | 86 | 2 | 43 | 31 | 1 | 33 | 21 | 124 |
| Orvath RT | 689 | 49 | 194 | 12 | 177 | 1 | 0 | 26 | 72 | 158 |
| Seelah TB | 248 | 116 | 0 | 24 | 8 | 2 | 0 | 0 | 18 | 80 |
| Seelah RT | 112 | 68 | 0 | 23 | 5 | 1 | 0 | 0 | 15 | 0 |

- **Per log:** weapon is the largest category in every log. For example, `648247bb` has 207 weapon and 106 dmg choices out of 580, and `87aa4bc7` has 368 weapon and 159 buff out of 1143.
- **No utility choices.** Utility was never chosen. "Commune with Constellations" is listed 496 times at a flat 1.0 and never wins.
- **Burn never chosen.** "Soul Power (burn)" is listed 102 times (5.6–8.5) and never chosen.
- **Orvath RT buff is mostly Gather Power.** 149 of Orvath's 177 RT buff choices are Gather Power; it is chosen 170 times overall.
- **Choices outside the ranking.** In 218 decisions the chosen action does not appear in the listed options at all (all Orvath except 9). In 173 of them swift (W) was available. The 15 most common such actions were checked against the next decision's flags: Mind Spike, Demonic Art summons and All According to Plan use up the swift action. Gather Power, Arcane Reservoir and Perfect Strike use up no listed action. Another 73 choices are listed but are not the top value.

#### 2. END TURN / wait with Standard available

- **All 18 cases are TB; there are none in RT.** Seelah has 16, Camellia 1, Aeiluvetar 1.
- **13 of the 18 had no options listed at all.** Seelah had `[SMWF5] HP100% :: => END TURN` in 648247bb R1–R6.
- **5 of the 18 had an option above 0, all tiny (max 0.4):**
  - 648247bb:1913, Camellia: Guidance 0.3 / Virtue 0.2.
  - 25a1e132:643, Seelah: `Move+Attack->Cultist Champion [into Grease]=0.1`.
  - 25a1e132:1534 and 1615, Seelah while entangled: Advance `[into Grease]` 0.3 / 0.4.
  - e62751a4:170, Seelah: `Advance [into Entangle]=0.3`.

#### 3. Margin between chosen and runner-up

These figures cover the 1804 decisions where the chosen action was the top listed option and had at least two options.

- **Typical gap:** the median relative gap is 28%.
- **Distribution:**
  - exact tie: 184 (108 are identical labels, i.e. two enemies with the same name)
  - up to 5%: 163
  - 5–20%: 416
  - 20–50%: 498
  - over 50%: 543
- **Near-ties:** 347 decisions (19%) are within 5%, and 297 of those are between options of the same category.

| near-tie pair | n |
|---|---|
| weapon/weapon | 131 |
| heal/heal | 72 |
| dmg/dmg | 71 |
| dmg/weapon | 29 (almost all Orvath Attack vs Fire Blast) |
| mark/mark | 12 |
| summon/summon | 10 |
| cc/weapon | 4 |
| buff/weapon | 3 |
| cc/dmg, buff/cc, buff/dmg, dmg/heal | 2 each |

#### 4. Value scale of chosen options (min / median / max)

| char | weapon | dmg | heal | buff | cc/debuff | mark | summon | move |
|---|---|---|---|---|---|---|---|---|
| Aeiluvetar | 0.7/6.8/24.4 | – | 11.3/14.8/31.3 | – | – | 0.5/1.9/1.9 | – | 0.6/4.8/21.6 |
| Camellia | 2.1/4.7/24.4 | 1.2/2.6/29.5 | 5.0/18.2/35.5 | 0.7/4.8/7.1 | 1.8/4.8/7.2 | 1.9/1.9/2.1 | – | 0.6/2.1/13.0 |
| Lann | 0.6/8.1/26.9 | – | 9.7/13.8/21.2 | 2.7 | – | 0.9 | – | 0.9/3.5/13.0 |
| Orvath | 8.3/14.3/28.1 | 3.1/9.8/33.0 | 7.6/17.1/24.3 | 0.5/1.9/33.0 | 8.8/25.6/62.4 | 10.3 | 4.7/9.0/11.9 | 0.5/4.8/15.4 |
| Seelah | 1.3/8.0/31.9 | – | 6.6/20.7/43.5 | 3.9/5.2/7.3 | 7.0/7.5/9.0 | – | – | 0.6/2.3/15.0 |

- **Heal is the highest category.** Its median chosen value is 18–21, against a weapon median of 7–8. Orvath, at 100% HP, heals others at 7.6–24.3.
- **Orvath's cc is highest of all.** The median is 25.6 and Sleep reaches 62.4, against his weapon median of 14.3.
- **Mark is about 1–2, a tenth of the attacks.** The exception is a single 10.3 for Orvath.
- **Orvath's buffs span 0.5 to 33.0.** Gather Power and Arcane Reservoir sit at 0.5–3. Prayer and Light of the Angels sit at 15–33.
- **The same target gets values 10x apart.** Against Cultist Champion, all five characters are within roughly ±30% of these medians in all three logs (e62751a4, 25a1e132, 9c5c6df2):
  - Lann's FullAttack: 1.3–1.5
  - Aeiluvetar's Attack: 0.7–0.8
  - Camellia: about 2.5
  - Seelah: 4.4–7.7
  - Orvath's Attack/Fire Blast: 7.5–9.4

#### 5. Repetition: same action on the same target in 3+ consecutive rounds

| log | char | action | rounds | first line |
|---|---|---|---|---|
| 9c5c6df2 RT | Orvath | Gather Power | 10 (RT7–16) | 3776 |
| 87aa4bc7 RT | Orvath | Gather Power | 9 / 8 / 7 / 7 / 6 (several fights) | 242, 14588, 10003, 16717, 4438 |
| 648247bb TB | Camellia | Daze->Werewolf | 7 (R1–7) | 3604 |
| 87aa4bc7 RT | Camellia | Item: Cure Moderate Wounds->self | 6 | 14813 |
| e62751a4 TB | Orvath | Color Spray->Cultist Champion | 5 | 4099 |
| 87aa4bc7 RT | Camellia | Daze->Goblin Stirrer | 5 | 394 |
| 9c5c6df2 RT | Seelah / Camellia | Item: Cure Moderate Wounds->self | 5 / 5 | 4841 / 4805 |
| 4de5be87 TB | Orvath | Sleep->Werewolf | 3 (R5–7) | 656 |
| 4de5be87 TB | Orvath | Sleep->Neophyte Sharpshooter | 3 | 417 |

- **Every one of these casts ended Success acted=True.** The Daze and Sleep values stayed flat from round to round (Daze 6.0–7.2; Sleep 11.4 / 14.1 / 11.6).
- **Weapon repeats** on a single target are as long as 13 rounds (Aeiluvetar Attack->Quasit, 87aa4bc7:406).

#### 6. Movement outcomes

| kind / mode | n | Success | Interrupt | acted=False | acted=True but moved <0.5m |
|---|---|---|---|---|---|
| Move+Attack TB | 71 | 4 | 63 | 6 | 8 |
| Move+Attack RT | 113 | 57 | 56 | 56 (26 of them moved <0.5m) | 7 |
| Advance TB | 39 | 26 | 13 | 13 | 0 |
| Advance RT | 51 | 39 | 12 | 12 (7 of them moved <0.5m) | 2 |
| Charge TB | 18 | 17 | 1 | 0 | 0 |

- **RT Move+Attack with acted=False, by character:** Seelah 28, Camellia 17, Lann 5, Aeiluvetar 3, Orvath 3.
- **TB Move+Attack:** 4 had no ENDED line because they were rejected.

#### 7. Choices carrying a risk tag

- **`[AoO N]`:** chosen 9 times (listed 34). Six were weapon and three movement. Results: 5 Success, 4 Interrupt, 1 acted=False.
- **`[into X]`:** chosen 20 times (listed 88). 13 were weapon, 5 movement and 2 cc. Results: 6 Success, 14 Interrupt, 10 acted=False. Most of these are Seelah moving into Grease or Entangle.
- **`[exposed]` and `[casting provokes]`:** these tags do not appear in any of the logs.

#### 8. Safety limit and timeouts

- **Decision safety limit:** reached 3 times, all Camellia in 648247bb (lines 453, 602 and 744).
  - Each time the same decision was taken 4–5 times with full flags `[SMWF5]`: `Item:Inflict Light Wounds->Cultist Champion(dmg)=2.6`.
  - Each was carried out as `UnitMoveTo 'Approach to touch: …' result=Success acted=True moved=0.0m after 0.0s`.
- **Wait timeout:** 0.
- **"not retrying this turn":** 54 lines. Most are RT "ended without acting"; 10 are Orvath RT "moved nowhere"; 4 are TB "force-finished before it started".
- **REJECTED:** 14 lines.
  - 8 are Lann: "full attack needs 2.4–5.2m of movement (more than a 5-foot step)".
  - 5 are "a 5-foot step was taken this turn".
  - 1 is an RT "command not accepted by UnitCommands.Run".

---

#### 10 examples that point to possible calibration problems

1. **648247bb:137, 254, 1295, 1399, 1496, 1593, 1694.** Seelah at HP100% with `[SMWF5]` and 2–6 enemies has an empty option list and ends her turn in every round from R1 to R6.
2. **648247bb:430–453, also 584–602 and 726–744.** Camellia chooses the touch item 5 times with "Approach" moving 0.0m, until the safety limit ends the turn. The outranked options were Aid Another 1.7 and Divine Zap 1.3.
3. **Same target, values about 10x apart** (e62751a4, 9c5c6df2:5 and 50). Against Cultist Champion, Lann's FullAttack is 1.3–1.5 and Aeiluvetar's Attack is 0.7–0.8, while Orvath's Attack or Fire Blast is about 9.
4. **648247bb:4838.** Orvath `[SMWF5]` lists `Attack->Werewolf=25.4` but chooses Mind Spike, his 5th-listed option at 3.1, which uses the swift action. This pattern occurs 23 times in TB and 12 in RT. 648247bb:4643 is similar: Attack 23.0 listed, Mind Spike 5.2 chosen.
5. **87aa4bc7:60.** Orvath `[SM-F-]` has no swift action, and the list shows `Entangle(debuff)=29.7` on top. He chooses an unlisted "Arcane Reservoir — Increase Caster Level" buff, then chooses Entangle at line 78. 4de5be87:255 is similar: Grease 36.7 listed, unlisted Searing Flesh buff chosen.
6. **648247bb:7114.** Orvath `[SMWF5]` chooses "5ft step out of melee", listed 4th at 9.1, over Fire Blast 24.9 and Attack 19.4.
7. **Buffs outranking attacks.** At 9c5c6df2:598, `Item:Prayer->Orvath(buff)=33.0` beats Move+Attack 17.4. At e62751a4:3965, Prayer 19.0 beats Color Spray 18.7 and Attack 14.1. At 648247bb:5152, Light of the Angels 15.6 beats Move+Attack 13.0. At e62751a4:163, Seelah's Item:Bull's Strength 4.6 beats Move+Attack 4.4.
8. **Gather Power chosen round after round.** It is chosen 170 times at values of 0.5–3.2, or unlisted, including 10 consecutive rounds at 9c5c6df2:3776. Orvath RT also has 158 "wait" lines while options are listed, e.g. 9c5c6df2:1865 with Commune 1.0 and Gather Power 0.4.
9. **Repeated control on the same target with flat values.** Camellia casts Daze->Werewolf 7 rounds in a row (648247bb:3604, 3749, 3881, 4002, 4133 …), each at 6.0–7.2 with a Success result. Orvath casts Sleep->Werewolf 3 rounds in a row (4de5be87:656, 725, 793).
10. **Heals for summons and full-HP casters.** Camellia at HP100% heals a summoned Dretch over attacking: 648247bb:4247 has Cure Light Wounds->Dretch 5.7 against Daze 5.1 and Attack 3.2, and 25a1e132:2383 is similar. Heal is the highest-valued category overall, with medians of 18–21 against a weapon median of 7–8.

**Additional examples:**
- **RT Move+Attack that never attacks.** At 9c5c6df2:3685, Seelah's command ended at line 4142 as Interrupt acted=False after 23.5 s and 2.7 m. At 9c5c6df2:3579, it ended after 8.1 s and 0.2 m.
- **Seelah ends her turn rather than step into Grease.** At 25a1e132:643 the only option was `Move+Attack [into Grease]=0.1`, while she had `[SMWF5]`.


## C. Live vs legacy vs dead code


Short paths are relative to `/home/user/CombatAIPWOTR/`. RTC = `GameInterface/RealTimeController.cs`, TE = `Tactics/TacticalEngine.cs`, UDE = `Core/DecisionEngine/UnifiedDecisionEngine.cs`. The csproj compiles every folder except bin/obj/dist/References/tools/release/tests (`CompanionAI_Pathfinder.csproj:75`), so nothing is excluded from the build. The search covered Harmony attributes, `AccessTools` strings, EventBus interfaces, UI calls and comment-only mentions. Comment-only mentions were not counted as call sites.

### 1. Entry points and what each one reaches

**Registered in `Main.Load` (Main.cs:33-92)**
- `PatchAll` picks up the attribute patches:
  - `GameInterface/TrapAvoidancePatch.UnitCommands_Run_Prefix` (prefix on `UnitCommands.Run`)
  - `GameInterface/PetItemsPatch.Transpiler` (on the `AbilityData.CanBeCastByCaster` getter)
- Manual patches, using method names as strings:
  - `CustomBrainPatch.TickBrain_Prefix` on `AiBrainController.TickBrain` (Main.cs:105-141)
  - `TurnBasedPatches.StartTurn_Postfix` on `CombatController.StartTurn` (Main.cs:170-184)
  - `TurnBasedPatches.TurnControllerTick_Postfix` on `TurnController.Tick` (Main.cs:225-240)
- EventBus subscribers:
  - `CombatEventHandler` (`IInCombatHandler`, `IPartyCombatHandler`; CustomBrainPatch.cs:441)
  - `CommandTrace` (`IUnitCommandEndHandler`)
  - `MountPathHandler` (`IUnitRunCommandHandler`)
  - `Tactics.DamageObserver`, `Tactics.SaveObserver` and `Tactics.CastOutcome` (rulebook, ability-execution and buff handlers)
- `PreBuffController.Initialize` also creates the `PreBuffUpdater` MonoBehaviour, whose `Update` calls `PreBuffController.Tick`.
- `OnToggle` (Main.cs:258) only subscribes and unsubscribes the EventBus handlers. It never unpatches Harmony. `TrapAvoidancePatch` (TrapAvoidancePatch.cs:55), `PetItemsPatch` and `PreBuffUpdater.Update` do not check `Main.Enabled`, so they keep running while the mod is toggled off.

#### (a) Turn-based combat
- `TurnBasedPatches.StartTurn_Postfix` calls `CustomBrainPatch.ShouldControlUnit`. It only logs.
- `TurnBasedPatches.TurnControllerTick_Postfix` (CustomBrainPatch.cs:284) runs `ShouldControlUnit`, then `TurnBasedController.ProcessTurn` (Core/TurnBased/TurnBasedController.cs:47). That uses:
  - `CombatMemory.Sync/ForTurn`
  - `UnitStatus.CanStandUpOnCommand`, then `StandUp`, which calls `TacticalEngine.ShouldStandUp/IssueStandUp`
  - `GameAPI.IsReadyForNextAction` and `GameAPI.HasRunningCommand`
  - `TacticalDriver.LearnFromLastCommand/RecordIssued/RecordRejected`
  - `TacticalEngine.Decide` and `TacticalEngine.Execute`, which issue commands through `CommandDispatcher.Run`
- What `TacticalEngine` uses:
  - Tactics: `AbilityDiscovery.Gather`, `AbilityProfile`, `CombatMath`, `ConsumablePolicy.IsAllowed`, `ProgressiveGate` and `SpellTierPolicy`, `ThreatAssessment`, `RiskMath`, `ActionRules` and `UnitStatus.Permissions`, `CastOutcome`, `CombatMemory`, `SelectiveRule`
  - Analysis:
    - `AbilityClassifier.IsBlacklistedForCombat/HasCCImmunity/IsUndeadOrConstruct/AnalyzeCCTypeFromDescriptor`
    - `CombatRulesAnalyzer.AnalyzeResistance`
    - `HitChanceCalculator.CalculateDamageReduction/CalculateSpellResistance/CalculateAbilityHitChance/IsTargetFlatFooted` (all through `CombatMath`)
    - `BuffEffectAnalyzer.Analyze/GetCombatValue`
    - `BuffStackingAnalyzer.Analyze`
    - `AoeDangerAnalyzer.EnteringDanger/InsideDanger/FindExit/IsPositionValid`
  - `CommandDispatcher.Run` uses `BattlefieldGrid.CellAt` (CommandDispatcher.cs:301) and `CommandTrace.Track/LogRejected`.
- `MountPathHandler.HandleUnitRunCommand` calls `CommandDispatcher.AttachPathIfNeeded` (turn-based only).
- `TickBrain_Prefix` is not reached in turn-based combat (the guard at CustomBrainPatch.cs:73).

#### (b) Real-time combat, default settings
- `CustomBrainPatch.TickBrain_Prefix` calls `PreBuffController.Instance.Tick` once per frame, then `ShouldControlUnit`, checks `NextCommandTime` and whether an ability is in progress, then calls `RealTimeController.ProcessUnit` (RTC:119). `ProcessUnit` runs:
  - `CheckAndUpdateTimeBudget` / `ProcessPendingUnitsQueue`
  - `CombatDataCache.RefreshIfNeeded` (RTC:136)
  - `UpdateTeamBlackboardCombatState` (RTC:1770). This calls `TeamBlackboard.InitializeCombat/UpdateTeamAssessment/Clear` and `MovementPlanner.ClearMoveHistory`.
  - `ProcessPendingBuffs` (RTC:2004)
  - `IsNextCommandTimeReady`, `IsMountBeingRidden`, `GetCannotActReason`, `DropUnstartableWhileNauseated`
  - The AoE escape: `AoeDangerAnalyzer.AnalyzeUnit`, then `ExecuteMoveAction` (RTC:211-223). `ExecuteMoveAction` uses `TrapAwarenessController.CheckMovementSafety`, `BattlefieldGrid.ValidateTargetPosition`, `CommandDispatcher.Run` and `SetNextCommandTime`.
  - `RealTimeTactics.ShouldReplanAfterDamage`
  - Stuck-command detection: `IsCommandStuck`, `WaitReason`, `FailedAbilityTracker.RecordFailure` (RTC:241-320)
  - `Tactics.RealTimeTactics.Act` (TacticalDriver.cs:154), which goes into the same `TacticalEngine.Decide/Execute` as turn-based.
- `TrapAvoidancePatch` also redirects moves the player issues (`CreatedByPlayer`, not turn-based).

#### (c) Only with `RealTimeLegacyEngine = true`
- RTC:326 calls `ExecuteAIDecision` (RTC:560), which runs `UnifiedDecisionEngine.DecideAction` and then `ExecuteActionCandidate/ExecuteAbilityAction/ExecuteBasicAttack/ExecuteMoveAction` (RTC:587-724). Along the way:
  - `RecordAbilityUse` and `PendingActionTracker.RegisterPendingBuff`
  - `FailedAbilityTracker.RecordCommandAndCheckRepeat`
- UDE reaches:
  - `SituationAnalyzer.Analyze`, and through it `Situation`, `TargetScorer`, `TargetAnalyzer`, `BattlefieldInfluenceMap`, `LineOfSightChecker`, `MovementAPI`, most of `GameAPI`, `CombatDataCache` data, `TeamBlackboard.RegisterUnitSituation`
  - `CombatPhaseDetector.DetectPhase`
  - All of `Scoring/*` (`UtilityScorer`, the four Scorers, `ResponseCurves`, `Consideration`, `ScoreNormalizer`, `TargetNormalizer`, `ScoringWeights`, `ActionCandidate`)
  - `MovementPlanner.PlanMove/PlanRetreat/ShouldRetreat`
  - `CrowdControlAnalyzer`, `TargetImmunityChecker`, `CombatRulesAnalyzer.IsImmuneTo`
  - `AbilityClassifier.Classify/EvaluateEffectiveness`
  - `PendingActionTracker.IsBuffPending`, `FailedAbilityTracker.IsBlacklisted`
  - `TurnOrchestrator.Instance.IsAbilityFailedThisTurn` (UDE:341)

#### (d) Out of combat
- When the party combat state changes, `CombatEventHandler.HandlePartyCombatStateChanged` calls `CombatPatches.OnPartyCombatStart/End` (CustomBrainPatch.cs:361-410). These reset `CombatMemory` (via `TurnBasedController.ResetAll`), `FailedAbilityTracker`, `CombatPhaseDetector`, `PendingActionTracker`, `DamageObserver`, `CastOutcome` and `SaveObserver`. At combat end, `RealTimeController.ApplyPermanentBuffsToParty` then runs `ApplyPermanentBuffsOutOfCombat` (RTC:2058). That function uses `AbilityClassifier.GetUnappliedPermanentBuffs` (classifier → `ClassifyAllAbilities` → `Classify`), then `IsWorthKeepingUp` (`AbilityProfile` + `BuffEffectAnalyzer`), then `CommandDispatcher.Run`.
- `HandleObjectLeaveCombat` (CustomBrainPatch.cs:484) calls `InterruptAll` and then `ApplyPermanentBuffsOutOfCombat(queueOnBusy)`. The queue is drained only by `RTC.ProcessUnit` (RTC:142), so only when `TickBrain` runs.
- `UI/MainUI.cs:131` (the button) calls `PreBuffController.TriggerPartyBuff`. That goes through `PreBuffAnalyzer.AnalyzeAllPreBuffs/GenerateBuffRequests` (`AbilityClassifier.ClassifyAllAbilities/GetBuffEffects`, `BuffEffectAnalyzer`, `BuffStackingAnalyzer.Analyze`). It then ticks through `PreBuffUpdater.Update`, `ProcessNextBuff` and `ExecuteBuff`, which calls **`request.Caster.Commands.Run(command)` directly (PreBuffController.cs:341), bypassing `CommandDispatcher`**.
- `TrapAvoidancePatch` handles player moves, and `PetItemsPatch` changes the rule at all times.
- Neither out-of-combat routine checks `ShouldControlUnit`. `ApplyPermanentBuffsToParty` (RTC:2167) and `PreBuffController.GetPartyMembers` (PreBuffController.cs:379) take every `IsPlayerFaction` unit, ignoring `ControlMainCharacter`, `ControlCompanions` and `EnableCustomAI`.

### 2. Dead or legacy-only code

#### Wholly dead (no runtime caller, even with the legacy setting)

| Class / member | Why unreachable | Lines |
|---|---|---|
| `Planning/TurnPlanner` | `TurnPlanner.Instance` is used only in its own file; other mentions are comments | 153 |
| `Planning/Plans/BasePlan`, `DPSPlan`, `TankPlan`, `SupportPlan` | Referenced only by TurnPlanner | 532+135+100+145 |
| `Analysis/SequenceOptimizer`, `Analysis/ActionSequence` | Used only by DPSPlan | 517+342 |
| `Execution/ActionExecutor` | Only instantiated in the `TurnOrchestrator` constructor (TurnOrchestrator.cs:57); its methods are called only from `TurnOrchestrator.ProcessTurn`, which has no caller | 407 |
| `Core/TurnOrchestrator` | `ProcessTurn` (line 70) has no caller. UDE:341 calls `IsAbilityFailedThisTurn`, but `_turnStates` is filled only by `ProcessTurn`, so it always returns false | 435 |
| `Core/TurnPlan`, `Core/PlannedAction` | Produced and consumed only by Planning, TurnOrchestrator and ActionExecutor; `TeamBlackboard.RegisterUnitPlan` has no caller | 215+253 |
| `Abstraction/IGameUnit`, `IAbilityData`, `PathfinderUnitAdapter`, `PathfinderAbilityAdapter` | Only built by `PathfinderCombatStateAdapter.CurrentTurnUnit`, which nobody reads | 44+57+92+140 |
| `CustomBrainPatch.CombatModePatches` | `CheckModeChange` (CustomBrainPatch.cs:423) has no caller | ~20 |
| RTC `#region Legacy Role-based Logic` through `#region Movement` | `ExecuteDPSLogic/ExecuteTankLogic/ExecuteSupportLogic` (776/838/887) have no callers, and every other method there (`SelectBestTarget`, `FindMostWoundedAlly`, `TryUseHeal/Buff/DebuffAbility`, `ExecuteRangeBasedAction`, `TryRangedAttack`, `TryAbilityAttack`, `ScoreAbility`, `TryAttackEnemy`, `TryMove*`, `IsRangedAbility`) is called only from inside that block | RTC:768-1654 (~887) |
| RTC `HasNonInterruptibleCommand` (1679), `ClearCache` (1747), `IsAbilityOnCooldown` / `GetAbilityCooldown` / `ShouldSkipAbility` (1891-1980; reached only from the dead block) | No live caller | ~150 |
| `TrapAvoidancePatch.FindNearestSafePosition` (126); `TrapAwarenessController.GetActiveTraps/ClearCache` | No caller | ~40 |
| `AoeDangerAnalyzer.IsPositionDangerous` (127), `GetAllDangerousEffects` (272) | No caller | ~55 |
| `AbilityClassifier`: `AreAllBuffsApplied`, `GetDebuffAbilities`, `RecommendAbility`, `ClearCache`, `ClearCCRemovalCache`; `GetBest*ForTarget`, `GetBuffAbilities`, `GetHealingAbilities`, `GetAttackAbilities`, `GetCCAbilities` (internal chain from `RecommendAbility`) | No external caller | ~200 |
| `HitChanceCalculator`: `CalculateOverallDamageEfficiency`, `CalculateOverallSpellSuccess`, `GetIterativeAttackCount`, `InvalidateCache` | No caller | ~110 |
| `CombatRulesAnalyzer`: `ClearCache`, `IsThreatened`, `IsSafeToUse`, `GetAoOPenalty`, `GetResistancePenalty`, `IsEnergyImmune`, `GetSpellResistance` | No caller | ~100 |
| `BuffStackingAnalyzer`: `ClearCache`, `ProvidesArmorBonus`, `ProvidesShieldBonus`; `BuffEffectAnalyzer.ClearCache` | No caller (so these caches are never cleared) | ~50 |
| `BattlefieldGrid`: `FindEmptyPositionNear`, `GetDebugInfo`, `GetNearestWalkablePosition`, `IsPathClear` | No caller | — |
| `GameAPI`: `CanUseAbilityOn`, `GetCurrentTurnController`, `GetCurrentTurnUnit`, `GetTurnStatus`, `HasUnfinishedCommand`, `IsAbilityAvailable`, `IsCommandQueueEmpty` | No caller | — |
| `PendingActionTracker.IsBuffPendingByOther/RemovePending`; `CombatPhaseDetector.GetPhaseDescription`; `ConsumablePolicy.IsConsumable` | No caller (`RiskMath.AnyDrops` is used only by tests) | — |
| `SituationAnalyzer` instance field in RTC (RTC:96, 107) | Constructed and never used | — |

#### Reachable only with the legacy setting

| Class | Notes | Lines |
|---|---|---|
| `Core/DecisionEngine/UnifiedDecisionEngine` | Only from RTC:567 | 1095 |
| All of `Scoring/` | Only through UDE | 5153 |
| `Analysis/SituationAnalyzer`, `Situation` | Only UDE, TurnPlanner and TurnOrchestrator call `Analyze` | 1286+281 |
| `Analysis/TargetScorer`, `TargetAnalyzer`, `TargetImmunityChecker`, `CrowdControlAnalyzer`, `LineOfSightChecker` | Legacy chain only | 1028+703+239+540+95 |
| `Analysis/BattlefieldInfluenceMap` | Legacy, except `InvalidateCache` from `TeamBlackboard.Clear`, which is a no-op in the default path | 712 |
| `GameInterface/MovementAPI` | Callers: SituationAnalyzer, UDE, MovementPlanner, SequenceOptimizer | 677 |
| `Planning/MovementPlanner` | Legacy, except `ClearMoveHistory` (RTC:1807) | 656 |
| `Core/TurnState` | Only the legacy SituationAnalyzer/UDE signature (always null in RT) | 387 |
| `GameAPI`, apart from `IsReadyForNextAction` and `HasRunningCommand` | Remaining methods serve only SituationAnalyzer and the Planning classes | ~500 of 595 |
| RTC `ExecuteAIDecision`, `ExecuteActionCandidate`, `ExecuteAbilityAction`, `ExecuteBasicAttack` | RTC:554-724 | ~170 |
| `AbilityClassifier.Classify` / `EvaluateEffectiveness` | Legacy, except that `Classify` is also used out of combat through `ClassifyAllAbilities` | — |

#### Live calls that do nothing in the default path

| Call (default RT) | Why it has no effect | Lines |
|---|---|---|
| `TeamBlackboard.UpdateTeamAssessment` (RTC:1786) | Returns at once because `_unitSituations` is filled only by the legacy `SituationAnalyzer.Analyze` (SituationAnalyzer.cs:102) | 742 |
| `CombatDataCache.RefreshIfNeeded` (RTC:136) | Rebuilds lists every frame; only `SituationAnalyzer` reads them (lines 338-859) | 227 |
| `FailedAbilityTracker.RecordFailure` (RTC:293) | The blacklist is read only by `IsBlacklisted` (SituationAnalyzer, UDE:302); `TacticalEngine` never reads it, so the stuck-command blacklist does not affect the engine | 251 |
| `CombatPhaseDetector.ResetCombatTracking` | The tracking it resets feeds only `DetectPhase` (UDE) | 327 |
| `PendingActionTracker.Clear` | Entries are added only in legacy `ExecuteAbilityAction` | 187 |

All told, roughly **19-20k lines** are dead or legacy-only, out of about 37k in the mod (the 39,468 total includes tests and tools).

### 3. Duplicated policy that is live with default settings

1. **AoE escape: two policies, both live in RT.** RTC:211-223 escapes before the engine is asked, whenever `AoeDangerAnalyzer.DangerAnalysis.ShouldEscape` holds (`TotalDangerLevel >= 0.5`, the plain sum of area danger levels; AoeDangerAnalyzer.cs:30, 48). It uses `FindExit` with `ESCAPE_MAX_DISTANCE = 9` (line 497), no `keepsTarget`, no attack-of-opportunity pricing, and no comparison with acting. TE `AddLeaveAreaCandidate` (around TE:1918-1960) does the same job differently. It uses `InsideDanger` (worst area + 0.25 × the rest), a distance of `min(9, move range)`, a reach-keeping exit for melee, `MoveRisk`, and a value of `danger × 0.5 × AllyWorth` weighed against other options.
   - They share `InsideDanger` and `FindExit` (AoeDangerAnalyzer.cs:85, 97), but the trigger and the weighing differ.
   - In RT the RTC rule decides first and overrides the engine's choice. The escape move also passes through `TrapAwarenessController` and `BattlefieldGrid.ValidateTargetPosition`; the engine's moves (`CommandDispatcher`) do neither.
2. **Can-act and stuck gates.** In RT, `RTC.GetCannotActReason` (RTC:537) refuses Dazed, Cowering, Frightened and Prone before the engine runs. The engine's own status rules (`UnitStatus.Permissions`, `ActionRules.Refusal`) and turn-based `StandUp` do not apply then. Stuck-command handling exists twice: `RTC.IsCommandStuck` with `FailedAbilityTracker` (written, never read by the engine, see §2), and `TacticalDriver.LearnFromLastCommand` with `CombatMemory.Failed` plus `CastOutcome`, which the engine actually uses. `NextCommandTime` is checked in `TickBrain_Prefix` (CustomBrainPatch.cs:88) and again in `RTC.IsNextCommandTimeReady` (RTC:1715). Engine-issued commands never set it; only the escape and the legacy or out-of-combat paths do (`SetNextCommandTime`).
3. **Hit chance: two calculators feed the live engine.**
   - Weapon attacks use `CombatMath.EstimateWeaponAttack`, which runs the game's own rules (`RuleCalculateAttackBonus`, `RuleCalculateAC`, `RuleCalculateWeaponStats`; CombatMath.cs:125-133) and `CombatMath.HitProbability` (line 52).
   - Attack-roll spells use `CombatMath.SpellAttackHitChance` → `HitChanceCalculator.CalculateAbilityHitChance` (CombatMath.cs:474). That is hand-rolled: BAB + STR/DEX + `AdditionalAttackBonus` against `Stats.AC.Touch` (HitChanceCalculator.cs:1347-1360), with its own `CalculateHitProbability` (line 1208) and a 2-second cache.
   - Its own `requiresAttackRoll` test (line 1332) can return 1.0 even when `AbilityProfile.NeedsAttackRoll` (which gates the call in `CombatMath.EffectChance`, line 415) says the spell needs a roll. Two classifiers decide "needs attack roll".
4. **Spell resistance and immunity: two analyzers run on the same candidate.** For damage (TE:1442) and control (TE:1605), `CombatRulesAnalyzer.AnalyzeResistance` is a hard filter. It has its own SR formula (CombatRulesAnalyzer.cs:780-826: `GetCasterLevel` with no spell-penetration bonus and `Blueprint.SpellResistance`), energy immunity and resistance through `HitChanceCalculator.CalculateEnergyResistance`, and descriptor immunities. Right after it, `CombatMath.EffectChance`, then `SpellResistancePass`, then `HitChanceCalculator.CalculateSpellResistance` computes SR again a different way: deliver blueprint, penetration feats, `UnitPartSpellResistanceCheckReplace` (HitChanceCalculator.cs:457-520). Energy immunity is computed a third time in `CombatMath.AfterTargetReduction`, through `DamageObserver`, `AddEnergyDamageImmunity` and `UnitPartDamageReduction.EstimateDamage` (CombatMath.cs:322-345). The control branch also calls `AbilityClassifier.HasCCImmunity` (TE:1604), which partly overlaps the descriptor immunity in `CombatRulesAnalyzer` (lines 903-990).
5. **Undead/construct test.** `AbilityClassifier.IsUndeadOrConstruct` (AbilityClassifier.cs:1716) guesses from Sleep + Stun immunity. It is live in `CombatMath.EstimateCoupDeGrace` (CombatMath.cs:189) and TE:1481 (whether an ally can be affected).
6. **Ability blacklist versus value.** `AbilityClassifier.IsBlacklistedForCombat` (AbilityClassifier.cs:2154) filters before `AbilityProfile` scores anything (TE:1027). It rejects Demoralize, fatigue-only touch spells, non-damaging touch cantrips and Daze cantrips, whatever their computed value. Its `HasVariants` check duplicates `AbilityDiscovery.Gather`, which already swaps variant parents for their variants (AbilityDiscovery.cs:46).
7. **Buff worth: three live policies.**
   - The engine: `BuffStackingAnalyzer.Analyze(...).IsEffective` (descriptor rule) plus `BuffEffectAnalyzer.Analyze` (TE:1086-1087).
   - Buffs after combat: `AbilityClassifier.Classify` `Timing == PermanentBuff`, then `IsWorthKeepingUp` (`AbilityProfile` + `BuffEffectAnalyzer.CombatValue >= 0.5`, `HasFact`, no stacking check; RTC:2139).
   - The PreBuff button: `PreBuffAnalyzer`, using `AbilityClassifier.GetBuffEffects`, `BuffEffectAnalyzer`, `BuffStackingAnalyzer.Analyze` (PreBuffAnalyzer.cs:685), Role/RangePreference affinity, and `AbilityClassifier.IsBuffAlreadyApplied`. It has no `ConsumablePolicy` and no item or toggle exclusion.
   - Both out-of-combat routines list abilities with `ClassifyAllAbilities`, which reads `unit.Abilities.Visible` only (AbilityClassifier.cs:1519). `AbilityDiscovery.Gather`, used in combat, treats spellbook spells as a separate source. So spellbook spells appear not to be seen out of combat.
8. **Heal and buff logic in RTC.** It is all in the dead block (RTC:1047-1175), so in default settings heal/buff logic is not duplicated.

**Single source in the live engine (no duplication):** save chance (`CombatMath.SaveFailChance` with `SaveObserver`; `HitChanceCalculator.CalculateSaveDC` is legacy-only), weapon damage reduction (`HitChanceCalculator.CalculateDamageReduction`), consumables (`ConsumablePolicy`), Progressive policy (`ProgressiveGate`).

### 4. Settings (`Settings/ModSettings.cs`)

| Property | Live TB | Live RT (default) | Legacy | UI | Notes |
|---|---|---|---|---|---|
| CharacterSettings.CharacterId / CharacterName | – | – | – | – | Written only, never read (JSON only) |
| EnableCustomAI | ✓ CustomBrainPatch.cs:182 | ✓ | ✓ | ✓ | Not checked by the out-of-combat buff routines |
| RangePreference | ✓ TE:1008, 1775, 1885 | ✓ | ✓ scorers | ✓ | Also PreBuffAnalyzer:524/606 |
| Role | ✓ TE:608, 614, 882, 938, 1776 | ✓ | ✓ | ✓ | Also PreBuffAnalyzer |
| HealAtHPPercent | ✓ `CombatMath.HealThreshold` (CombatMath.cs:79) | ✓ | ✓ | **No UI** | JSON only |
| SpellPolicy | ✓ TE:151, 1214; ProgressiveGate:39 | ✓ | ✓ UDE:244 | ✓ | |
| AvoidFriendlyFire | ✓ TE:1455, 1575 | ✓ | ✓ UDE:211 | **No UI** | JSON only |
| EnableDebugLogging | ✓ Main.Verbose | ✓ | ✓ | ✓ | |
| UILanguage | – | – | – | ✓ only | |
| ControlMainCharacter / ControlCompanions | ✓ CustomBrainPatch.cs:170/175 | ✓ | ✓ | ✓ | Ignored by the out-of-combat buff routines |
| EnableInTurnBased | ✓ CustomBrainPatch.cs:312 | – | – | ✓ | |
| EnableInRealTime | – | ✓ CustomBrainPatch.cs:84 | ✓ | ✓ | |
| RealTimeLegacyEngine | – | ✓ (the switch) RTC:226, 326 | – | ✓ | |
| RealTimeDecisionInterval | – | ✓ RTC:35 | ✓ | ✓ | |
| UseConsumables / UsePotions / UseScrollsAndWands / UseThrownItems | ✓ ConsumablePolicy.cs:21-27 | ✓ | via ConsumablePolicy | ✓ | Not applied by PreBuff |
| PotionHealThresholdPercent | ✓ CombatMath.cs:78 | ✓ | ✓ | ✓ | |
| PetsUseItems | ✓ PetItemsPatch.cs:30 (global game rule) | ✓ | ✓ | ✓ | Applies even when the mod is disabled |
| ProgressiveBossBypass | ✓ TE:1216, ProgressiveGate:131 | ✓ | ✓ | ✓ | |

No setting turns `TrapAwarenessController.Enabled`, `TrapAvoidancePatch.PatchEnabled` or `ApplyOutOfCombat` off; they are hard-coded to true.

### Notes on certainty
- I did not have the decompiled game source, so whether the game calls `AiBrainController.TickBrain` for party units out of combat is not confirmed. That decides whether `RTC.ProcessUnit`'s "not in combat" branch (RTC:233) and `ProcessPendingBuffs` ever run outside a fight.
- The CLAUDE.md pipeline puts `TurnControllerTick_Postfix` on `CustomBrainPatch`; it is actually `TurnBasedPatches` (in CustomBrainPatch.cs).


## D. Hot-path cost


All paths are under /home/user/CombatAIPWOTR. This was a read-only audit and nothing was profiled. The costs of the game's own functions (Rulebook triggers, `CalculateParams`, `UnitAttack.Init`, line-of-sight checks) are inferred from how the code uses them; the decompile is not in this container.

#### 1. How often Decide runs

**Turn-based** (`Core/TurnBased/TurnBasedController.cs`)
- Decide runs only once the unit's previous command has finished (`IsReadyForNextAction`, :65). That is a few decisions per turn, capped at `MAX_DECISIONS_PER_TURN = 16` (:42).
- When the dispatcher rejects a candidate, Decide is rerun from scratch, up to `MAX_ATTEMPTS_PER_TICK = 3` times in the same frame (:45, loop :83-101, `TacticalDriver.MaxAttemptsPerDecision = 3` at `Tactics/TacticalDriver.cs:17`). Each rerun rebuilds the whole context, runs `Gather` again and re-scores every candidate, only to drop the failed key.
- For a prone unit, `ShouldStandUp` (:208) runs a full preview Decide (`TacticalEngine.cs:699`). After the stand-up order, another full Decide follows.

**Real time** (`GameInterface/RealTimeController.cs`)
- Throttle: `DecisionInterval` = setting `RealTimeDecisionInterval`, default 0.5 s, clamped to 0.2–2 s (:34-35; default at `Settings/ModSettings.cs:268`).
- A per-unit offset is added to that interval rather than shifting its phase: `GetUnitPhaseOffset` (:343-361) gives 0–0.405 s (`MAX_PHASE_OFFSET 0.45`, `PHASE_DIVISIONS 10`). The effective interval is 0.5–0.9 s per unit.
- The check is at :152-155 and the timestamp is written at :330 even when Act returns "wait".
- The frame budget `MAX_AI_TIME_MS = 3` (:42, :367-383) only defers later units. It never splits a decision.
- `RealTimeTactics.Act` (`TacticalDriver.cs:154-186`) also retries up to 3 times.
- Real time lists weapon candidates during the action cooldown (`TacticalEngine.cs:919`). So all the work runs even when the result is "wait".
- Real-time `MoveRange = speed × 6 s` (:375), twice turn-based's 3 s (:379). That means more Move+Attack and approach candidates and longer paths for `PathAttackers`.
- With 6 party units this is about 7–12 full decisions per second.
- Before Decide, `AoeDangerAnalyzer.AnalyzeUnit` (:214) runs `InsideDanger` and maybe `FindExit`. Decide repeats both in `AddLeaveAreaCandidate` (`TacticalEngine.cs:1924, 1950`).

#### 2. Ranked hot spots

Assumed decision: 6 enemies (E), 6 allies (A), about 30 of 40 abilities available, giving roughly 150 (ability, target) pairs.

**#1 `CastProvokeRisk` per (ability, target) — `TacticalEngine.cs:1283-1290`, body :770-801**
- It runs for every standard-action spell that provokes and has value > 0, on every target.
- Each call loops over all enemies with `Threatens` (:803-809: `CanAct`, `GetThreatHand`, `IsReach`, then `UnitPartConcealment.Calculate`) and `AttackOfOpportunity(simulate:true)`.
- If anyone threatens, it calls `ability.CalculateParams()` (:782) and `PriceAttacks` → `EstimateWeaponAttack(enemy, unit)` per attacker (:628).
- Example, engaged by 2 enemies with about 15 provoking spells × 5 targets = 75 calls:
  - about 450 `Threatens`
  - about 150 concealment calculations and 150 simulated attacks of opportunity
  - about 150 `EstimateWeaponAttack`, which is about 150 `UnitAttack.Init` plus about 450 rule triggers
  - 75 `CalculateParams`
- **Reusable:** yes. When `castAt == unit.Position` (not `needsMove`), the attacker set and `PriceAttacks` results are the same for every ability and target. Only `fail` (spell level / concentration) depends on the ability. Compute the attackers and price once per decision; compute `fail` once per ability.

**#2 `CalculateParams` and context creation, repeated per target and helper**
- `CalculateParams` (`RuleCalculateAbilityParams`) runs once per target in each of:
  - `EstimateAbilityDamage` (`CombatMath.cs:284`)
  - `EstimateAbilityHeal` (:366)
  - `SaveFailChance` (:430)
  - `CastingConcentrationChance` (:396, only while entangled)
  - `HitChanceCalculator.CalculateSpellResistance` (`Analysis/HitChanceCalculator.cs:497`, only when SR > 0)
  - `CastProvokeRisk` (:782)
- `CreateContext` (`CreateExecutionContext` + `RecalculateRanks`, `CombatMath.cs:219-232`) runs once per damage or heal estimate, and on the first check of each condition per frame (`AbilityProfile.cs:839`).
- For an area spell this multiplies by the number of enemies inside.
- Estimate: about 200–500 `CalculateParams` per decision.
- **Reusable:** `CalculateParams()` takes no target, so it can be cached per AbilityData per decision. Context creation could be cached per (ability, target).

**#3 Spell-resistance path scans every fact on the caster — `HitChanceCalculator.cs:746-763` (`IgnoresSpellResistance`), :689-743 (`GetSpellPenetrationBonus`)**
- `EffectChance` (`CombatMath.cs:412`) → `SpellResistancePass` → `CalculateSpellResistance` (:457).
- For every SR-allowing spell × enemy evaluation, it walks `caster.Facts.List` and every component of each fact (`OfType<>` LINQ). That list includes features, buffs and abilities, likely 150–400 facts.
- `GetSpellPenetrationBonus` repeats the scan and allocates a Dictionary, but only when the target's SR is above 0.
- `EffectChance` is called from `ScoreAbility` damage (:1444) and control (:1606), and again from `ProgressiveGate.SuccessChance` (`TacticalEngine.cs:1317` → `ProgressiveGate.cs:116/119`).
- Estimate: 60–200 calls, each scanning hundreds of facts.
- **Reusable:** yes. Both results depend only on (caster, ability) and can be cached per decision. `SuccessChance` duplicates `EffectChance` work already done in `ScoreAbility` for the same (ability, target).

**#4 `EstimateWeaponAttack` computed several times for the same pair — `CombatMath.cs:105-164`**
- Each call does `UnitAttack.Init` (OnlyForInspect, :94-96), then per attack 3 `Rulebook.Trigger` calls (`RuleCalculateAttackBonus` :125, `RuleCalculateAC` :129, `RuleCalculateWeaponStats` :133), plus `CalculateDamageReduction` (`HitChanceCalculator.cs:769`, which loops DR sources).
- Unit vs enemy, single attack, recomputed up to 5× per enemy: `TacticalEngine.cs:923` (weapon candidates), :1354 (charge), :1798 (ranged step-out), :1875 (advance), :1942 (leave area).
- Unit vs enemy, full attack: :947 and again at :1812.
- Enemy vs unit (`PriceAttacks` :628): recomputed in every `DisengageRisk`, every `CastProvokeRisk` (#1), `ReviveStaysUp` (:657) and `ShouldStandUp` (:712).
- Estimate: the weapon loop alone is about 12 estimates and 70+ rule triggers. Repeats add 2–5× more, and the risk paths far more (#1, #5).
- **Reusable:** yes. Key by (attacker, target, full) for the duration of one Decide. This cache must not cross `ShouldStandUp`'s temporary `Prone.Active = false` (:696-708), because prone changes AC and attack.

**#5 `MoveRisk` → `PathAttackers` / `IncomingDamage` / `EnteringDanger` — `TacticalEngine.cs:525-565, 576-601, 734-760`**
- `PathAttackers` samples the path every 0.75 m (:740). That is about 12 steps for 9 m in turn-based and about 24 in real time, each step running `Threatens` on all 6 enemies: 72–144 calls per move.
- `AttacksOnLeaving` (:817-824) runs `ShouldAttackOnDisengage(simulate)` and `AttackOfOpportunity(simulate)`; in real time it also runs `EngagedUnits.Count()` LINQ.
- `attackers.Contains` is a List lookup (:746).
- `IncomingDamage` (:475-496) is E × A work (36 distance checks, `Spellbooks.Any()`, a closure allocation per enemy, `Threat`). It runs up to 3 times per `MoveRisk`:
  - :591, at the destination
  - :555, at the destination again — an identical repeat
  - :556, at the unit's own position — identical for every `MoveRisk` in the decision
- `EnteringDanger` (`AoeDangerAnalyzer.cs:173-208`) does areas × ceil(len/1 m) shape tests. It is cheap when there are no areas.
- Where `MoveRisk` is called:
  - :985 — one per out-of-reach enemy
  - :1266 — per (ability, target) needing a move, with `alwaysExposure` (so 2 extra `IncomingDamage`). It runs before `ScoreAbility` (:1270), so abilities that then score 0 still pay for it.
  - :1363 (charge), :1825, :1896, :1960
- `ApproachPoint` (:827) gives the same destination for every ability with the same range on the same target, so `MoveRisk` is recomputed per ability.
- **Reusable:** yes, all of it per decision:
  - `IncomingDamage(unit, unit.Position)`
  - per-enemy concealment, `GetThreatHand`, `CanAct`
  - the attack-on-leaving result for each (enemy, unit)
  - `MoveRisk` keyed by (rounded destination, alwaysExposure)
- Moving the :1266 risk after `ScoreAbility`'s `value > 0` check skips work for options worth nothing.

**#6 Per-(ability, target) setup before scoring — `TacticalEngine.cs:1241-1268`**
- ~150 pairs. Each does:
  - `AbilityProfile.TargetOn` → `ability.CanTarget` once or twice, with a new `TargetWrapper` each time (`AbilityProfile.cs:961-972`)
  - `GetApproachDistance`
  - `new UnitUseAbility(ability, wrapper).NeedLoS` (:1252) — a command object built only to read one flag
  - `InRangeNow` → `UnitCommand.IsUnitCloseEnough` with a line-of-sight check (:900-903)
- **Reusable:** `NeedLoS` is presumably per (ability, unit-or-point wrapper) and can be cached per ability. Line of sight from the unit's eye to a target position can be cached per target per decision.

**#7 Rest of ScoreAbility's per-enemy work — `TacticalEngine.cs:1379-1754`**
- `CombatRulesAnalyzer.AnalyzeResistance` (`Analysis/CombatRulesAnalyzer.cs:734-775`) allocates a result with lists and a dictionary, plus `GetAbilityEnergyTypes`. It runs for damage (:1442) and again for control (:1605) on the same enemy within one call.
- `AfterTargetReduction` (`CombatMath.cs:321-347`), per damage part:
  - `DamageObserver.Lookup` (string concatenation)
  - `UnitDescriptionHelper.VisitComponents` over all the target's facts (:354)
  - `part.AllSources.Count()`
  - `CreateDamage` allocation
  - `EstimateDamage`
- `SumAlternatives` (:302) does an `OrderBy` per call.
- Control: `InOwnSideArea` iterates all area effects per enemy (`AbilityProfile.cs:228-250`).
- Area spells: the `headsInto` lambda orders all enemies per ally (`TacticalEngine.cs:1586-1590`).
- Buffs: `BuffStackingAnalyzer.Analyze` (`GetExistingBonus`, string building on conflicts) and `BeneficialBuffsRemoved` → `target.Buffs.Enumerable.ToList()` (`AbilityProfile.cs:890`).
- Heals: `IncomingDamage(ctx, ally)` per heal ability × ally (:1520). That is about 30 × 36 distance checks — pure per ally and cacheable.
- **Reusable:** yes per (ability, enemy) for `AnalyzeResistance` and damage estimates, and per enemy for energy immunity.

**#8 Condition-checker cache key allocation — `AbilityProfile.cs:814-866`**
- Every `AppliesTo` / `BuffAppliesTo` with guards builds `$"{caster?.UniqueId}|{target.UniqueId}"` (:818), even on a cache hit.
- They are called many times per `ScoreAbility`: `Heals.Any`, `AnyTargetEffectApplies`, `Damage.All`, per damage part, `TargetBuffs.Where`, `EffectRoundsOn`, `AreaReaches` (which also allocates a guard array, :944).
- That is likely thousands of small strings per decision. The result itself is cached correctly per frame.

**#9 `CombatMath.Threat` and `Engagers.Sum(Threat)` recomputed constantly**
- `Threat` (`CombatMath.cs:517-528`) reads CR, runs `IsDisabled` (6 `HasCondition` checks) and `EngagedUnits.Count`.
- It is called from `DamageValue` per candidate, `IncomingDamage` per enemy, and `ctx.Engagers.Sum(CombatMath.Threat)` inside the per-enemy weapon loop (:943), plus :463, :1821, :1850.
- Each call is cheap, but there are hundreds per decision. Cache per enemy per decision.

**#10 Area exit and retreat sampling (only near areas or when retreating)**
- `FindExit` (`AoeDangerAnalyzer.cs:505-544`): up to 5 rings × 16 = 80 points.
- Each point runs `IsOutside`, `EnteringDanger` (all areas × path samples) and `IsPositionValid` (navmesh `IsPointInsideNavMesh` / `GetNearestNode` / `TraceAlongNavmesh`, :550-581).
- It stops at the first ring with a hit, but with a `prefer` predicate it can scan all 80. `prefer` also runs line-of-sight `IsUnitCloseEnough` per enemy in reach (`TacticalEngine.cs:1947`).
- In real time it runs twice per interval (`AnalyzeUnit`, then `AddLeaveAreaCandidate`).
- `FindRetreatPoint` (`TacticalEngine.cs:1983-2014`): 12 points × (`keepsTarget`: up to 6 line-of-sight checks, :1802) + `EnteringDanger` + a navmesh trace in real time (:2002). Called up to twice per decision (:1818, :1847).
- `AffectChance` and `RoundsLeft` use reflection `FieldInfo.GetValue` twice each, boxing a `TimeSpan` (`AoeDangerAnalyzer.cs:628-633, 647-651`). The FieldInfo itself is cached, and this only runs for areas being entered or stood in.

**#11 `AbilityDiscovery.Gather` once per Decide, including every retry — `Tactics/AbilityDiscovery.cs:26-112`**
- Per call:
  - `KineticBladeInternals` walks the activatable abilities
  - `Key(a)` string (two `AssetGuid.ToString()` plus metamagic tag) per ability
  - `GetConversions().ToList()` and `IsVisible()` per variant
  - `GetAllKnownSpells`, `GetAllCustomSpells` and 11 × `GetSpecialSpells` per spellbook
- The `ReportOnce` message at :108-110 (`string.Join` + `Distinct`) is built on every call before the dedupe check.
- Then `IsAvailable` and `IsBlacklistedForCombat` run per ability (`TacticalEngine.cs:1026-1027`); the blacklist check is uncached and does `GetComponent` calls.
- **Reusable:** within one decision and its retry loop.

#### 3. Repeated identical work within one decision

- `EstimateWeaponAttack`: same (unit, enemy, single) up to 5×; (unit, enemy, full) 2×; (enemy, unit) in every `PriceAttacks` call (#4).
- `CastProvokeRisk` at the unit's own position: same result for every ability and target, apart from `fail` (#1).
- `IncomingDamage(unit, unit.Position)` in every `MoveRisk` (:556); the destination value twice per `MoveRisk` (:555 and :591).
- `MoveRisk` to the same `ApproachPoint` across abilities that share a range (#5).
- `EffectChance` in `ScoreAbility` and again in `SuccessChance` (:1317); `AnalyzeResistance` twice per enemy in one `ScoreAbility` (:1442 / :1605).
- `CalculateParams` per target and per helper (#2).
- `InsideDanger` / `FindExit` twice per real-time interval.
- The whole Decide, `Gather` included, rerun per rejected candidate (up to 3 per tick) and for the `ShouldStandUp` preview.
- `CoupleOwnAttackMarks` reruns `EvaluateAbility` for a re-aimed mark (:253). This is small: one target per mark.

#### 4. Caching present

| Cache | Key | Lifetime / invalidation | Growth |
|---|---|---|---|
| `AbilityProfile.Cache` (`AbilityProfile.cs:262`) | BlueprintAbility | forever (`ClearCache` at :289 has no caller) | bounded by number of blueprints |
| `AbilityProfile.CheckerCache` (:767) | checker → "casterId\|targetId" | entry valid only for the same `Time.frameCount` (:816-821); dictionary cleared on `CombatMemory.ResetCombat` (`CombatMemory.cs:69`) | bounded per combat; stale entries overwritten, not removed |
| `HitChanceCalculator._cache` (`HitChanceCalculator.cs:254`) | string, e.g. `A_{att}_{tgt}_{guid}` | all cleared every 2 s (:1465-1475) | bounded |
| `CombatRulesAnalyzer._baseImmunityCache` / `_aoOImmunityCache` (`CombatRulesAnalyzer.cs:190-195`) | unit UniqueId | **never cleared** (`ClearCache` at :198 has no caller) | **grows with every unit ever analysed in the session, and goes stale**: immunities gained from buffs later are never seen. This is a correctness issue as well as a memory one. |
| `_threatCache` (:186) | unit id, per frame | overwritten | grows by unit id, never cleared |
| `ThreatAssessment.BossBlueprints` | BlueprintUnit | forever | bounded |
| `BuffEffectAnalyzer._cache` / `BuffStackingAnalyzer._buffBonusCache` | buff guid | forever | bounded |
| `AbilityClassifier._classificationCache` | — | capped at 4096 (`AbilityClassifier.cs:808`) | bounded |
| `AbilityDiscovery.Reported` | — | cleared at 1024 entries (`AbilityDiscovery.cs:159`), so messages re-log afterwards | bounded |
| `ProgressiveGate.LastLog` / `ProgressiveLogged` | — | capped at 512 / 4096 | bounded |

Nothing caches `EstimateWeaponAttack`, `CalculateParams`, `AnalyzeResistance`, `MoveRisk`, `IncomingDamage`, `Threat` or attack-of-opportunity results per decision.

#### 5. Logging cost

- `Main.Verbose` (`Main.cs:328-334`) checks `EnableDebugLogging` (default false, `ModSettings.cs:256`) only inside the method. Every interpolated string is built first at the call site. If settings are null, the `?? true` default means it logs.
- Verbose calls on the hot path whose strings are built for nothing:
  - `HitChanceCalculator.cs:853` — each DR calculation against a target with DR, inside `EstimateWeaponAttack`, per attack
  - :512 and :486 — each SR calculation
  - :741 — spell penetration
  - `CombatRulesAnalyzer.cs:764` — each `AnalyzeResistance` with SR or a penalty
  - `AbilityClassifier.cs:2168, 2240` — per ability per decision
- `AbilityClassifier.IsBlacklistedForCombat` calls `Main.Log` unconditionally (:2206, :2213, :2232). A unit with Touch of Fatigue or an enemy-targeting touch cantrip without damage writes real log lines on every decision.
- The revive `ReportOnce` (`TacticalEngine.cs:1532`) builds its key and message on every heal evaluation.
- `LogDecision` (:305-321) always builds a StringBuilder. In turn-based it is always logged, which is intended. In real time with no choice (:319) the string is built and then discarded by Verbose, once per interval per unit. Small; a flag check first would fix it.

#### 6. Clearly not worth optimising

- `AbilityProfile.Get` (dictionary hit), `CastOutcome.Settle` / `IsBlocked` / `WouldReplaceHeldCharge`, `PriceHeldCharge`, `Affordable`, `IsOffensive`, `CoupleOwnAttackMarks`.
- `ProgressiveGate.HeldBack` / `SpellTierPolicy` (linear over about 20 options; `SuccessChance` is pre-filled from the candidate), `ThreatAssessment.IsBoss`.
- The `BuildContext` loop over `State.Units`, which runs once per decision. `UnitStatus.Permissions` / `Describe`.
- `LogDecision` in turn-based, and the small LINQ chains over 6-element lists. Individually these are trivial; together they only matter as GC garbage.
- Analysis-time reflection in `AbilityProfile` (:327, :1099-1156). It runs once per blueprint and is cached.

#### Suggested order

1. A per-decision memo object on `TacticalContext`:
   - per-enemy threat, concealment, threat hand and attack-of-opportunity/disengage results
   - `EstimateWeaponAttack` keyed by (attacker, target, full)
   - `IncomingDamage` at the unit's position and per ally
   - `CalculateParams` per ability
   - the SR-ignore and spell-penetration fact scans per ability
   - `MoveRisk` per destination
2. Hoist `CastProvokeRisk`'s attacker pricing out of the per-target loop.
3. Run `ScoreAbility` before `MoveRisk` / `CastProvokeRisk`.
4. Reuse the candidate list across retries instead of rerunning Decide.
5. Guard the Verbose and Log calls on the hot path.
6. Clear `CombatRulesAnalyzer`'s caches at combat end.

All of these values are fixed for the duration of one decision, as long as the cache does not cross `ShouldStandUp`'s prone toggle.
