# Eldritch Archer tactical support

12 November 2026. `codex/eldritch-archer-support` starts at cumulative review2 / PR #40 `8985e40`, over
fetched development `1f58150`. The author explicitly requested Eldritch Archer and Shifter support; this
first focused change covers the former. Shifter and the reported Ulbrig form loop are a separate task.

## Native contract and behavior

The exit1 logs identify Drow Magus as Eldritch Archer, previously outside C07's melee-only planner. The
supplied native `UnitPartMagus`, `UnitUseAbility`, `MagusController`, `RuleAttackWithWeapon` and
`AbilityDeliverProjectile` establish different ranged rules; no guide's tabletop assumptions substitute for them.

`IsSuitableForEldritchArcherSpellStrike` requires a ray and the native Magus spell-list/book predicate. A
Standard/Swift source with active ranged Spellstrike uses the current ranged weapon. The native command arms
the spell and queues an attack, rather than casting the payload immediately. Without a ranged weapon its
native action falls back to an ordinary spell. The forecast retains that distinction.

The **first arrow, even a miss**, invokes the payload. The arrow's attack roll is passed into its execution
context and reused by all simple spell projectiles. Native `MagusController` spends/clears the armed spell
when that cast occurs. Later iteratives and Manyshot cannot retry it. Spell criticals are x2; physical weapon
criticals retain their own multiplier. This is different from C07's first-successful-melee-hit aggregation.

The existing bounded Magus planner now handles ranged equipment as well as melee:

- Standard in-place spells can be priced with native Ranged Spell Combat. Suitable ranged Spellstrike sources
  use correlated first-arrow probability and the native extra attack/typed combat penalty. Source spell and
  weapon share overkill valuation. A compulsory ranged delivery replaces its standalone quote even if its
  value is lower; otherwise the planner would choose a ray the game will not execute independently.
- Actual native `PreparedSpellCombat`/`PreparedSpellStrike`, finite deadlines and full/single command flags
  govern follow-ups. Current armed payloads contribute once, on the first arrow. Casting over one pays its
  marginal opportunity value; another ray is not rewarded for re-arming over it.
- Read-only delivery guards require actual weapon availability, hostility and current in-place range/LOS.
  Unsupported paired payloads, helpful rays and unsafe bow delivery require separately evaluated Spellstrike
  off. A stale native armed field awaits native cleanup before another AI cast/attack can consume it.
- Arming is not treated as a completed damaging spell with no effect. Outcome watching moves to its weapon
  delivery attempt, without blacklisting the source for a missed first arrow.
- The same ownership, global/character lists, automatic-toggle setting, once-per-round throttle and restoration
  apply. No localized class-name/GUID checks, new settings or native actor/equipment/buff mutation in previews.

Ordinary ranged characters keep their existing attacks. Pure Mage retains its existing exclusion of automatic
physical Magus preparations/follow-ups and its permitted intrinsic native spell delivery. Existing manual
native states remain; melee behavior and independent natural-hit held-touch protections remain.

## Bounds and limits

At most four useful in-place sources and two one-toggle future states are repriced, as in C07. One scalar quote
is scoped to the exact AbilityData/recipient; it never leaks into another spell/decision. Existing native
attack/count/rank/resistance caches are reused and invalidated at preview boundaries. There is no new world
scan, actor simulation, path query or native command execution while scoring. Only actual execution switches
a mode and lets the native game queue/spend/deliver.

The paired contract covers ordinary simple single-target rays without controlled projectiles, hard control,
caster-side payloads or Fusion. Ordinary supported spells remain available with the appropriate off state.
Automatic combinations quote Standard sources; native Swift delivery is guarded and uses the correct weapon
roll, without speculative credit for a new full routine. Native target movement after issue can change its
queued execution. No pursuit/target commitment/retreat/pathfinder/stuck policy is retuned. The shared engine
handles TB/RT; legacy RT is not expanded. Offline checks do not establish matched Unity speed or live command
stability with every installed patch.

## Verification and replay

25 native checks cover the exported unlocks and ray/non-ray boundaries, first-arrow misses, spell x2 critical,
scope restoration and compiled native eligibility/queue/cleanup/issue routes. Inherited Magus, caster, Fusion,
projectile, treatment, area and provider checks remain. Final receipt records full unit/inventory tests, actual-
reference Release and exact added signatures. Compatible/supplementary corpus passes remain distinct from the
original full corpus's named one-file enum mismatch, which yields no ability verdicts.

1. Drow Magus/bow: Standard Snowball or Scorching Ray, all four mode combinations, full budget versus Move spent.
   Inspect `[Magus]`, paired labels, actual arrow counts/penalties, first-arrow roll and payload spend.
2. Force a first-arrow miss followed by hits: no payload retry. Check multiple rays and Manyshot; one source
   slot spent on its native cast, spell x2 separate from a bow's x3/x4. Check SR/immunity and weapon-damage immunity.
3. After arming: movement, expiry, mode off and weapon swap must follow native clearing. Re-evaluate pending
   delivery without casting the source again. Different targets/abilities must not share a scalar quote.
4. Lists/manual ownership/automatic toggles off/Pure Mage/ordinary Ranged; empty ammo, helpful or unsupported
   rays require safe native delivery or an evaluated off state. Native melee C07 must retain its prior behavior.
5. Repeat TB and shared RT, retaining startup MVID and combat logs. Match party/settings/warm-up for performance.

## PR #38 trade-offs reviewed

That optimization adds a short numerical-part scan before debuff appraisal, so dynamic-only graphs gain no
context saving and pay that scan. It benefits fixed/simple amounts, not the whole expensive ability stage.
Its predicate must remain aligned with future `PartAmount` context reads; otherwise a new reader could need
data omitted by the optimization. Current rank/shared/dice/pool/caster-level paths retain source contexts;
recipient stats and same-caster fact filters remain live. Actual static scalar tests pass, but matched Unity
performance and installed-mod gameplay are not established by those tests. No new correctness defect was
found in the current supported static path, and this task does not change PR #38's implementation.

Checked source `5ddcbca`, [receipt](competence/ELDRITCH_ARCHER_CHECKS.json) and [archer1 test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-archer1-5ddcbca-UMM-test.zip), startup MVID `8e6a8470-b08a-41d6-9771-0b995ae18465`. 629 unit + 11 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 5/5 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
