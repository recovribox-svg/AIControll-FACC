# Coverage7 playtest review and corrections

22 November 2026. Continued from PR #102, `claude/coverage7-review` at `e96b52b24fb9f1936256a4ba0f4aa50f904fc2bd`.
Corrections are on `codex/coverage7-review`; its draft PR targets that review branch. No merge, development/main push,
tag or release. This replaces the partial review; the original observations were checked rather than assumed true.

## Inputs and scope

All three private upload hashes match [the input receipt](competence/COVERAGE7_PLAYTEST_INPUTS.json).
The played DLL is coverage7, module `0aa40527-d88f-4dfb-977b-3c6b101daaee`, UMM 0.3.60. Ten fights: five TB, five RT;
1,207 decision rows, 928 issues, 924 terminals, 14 issue refusals, no observed AI Control exception. Missing terminals
are not automatically failures. The author confirms rests **between** fights and no manual control of dominated Aeiluvetar.

Reviewed all five TB fights (306 non-END choices), the named RT anomalies/losses, native rules, stock graphs and the
save's spellbooks. The save was inspected as ZIP/JSON data, without game/type deserialization or execution: 21 members,
13,584,021 declared uncompressed bytes, no traversal paths. It is an end-state snapshot after rest, not a combat replay.
Raw logs/save, game references, blueprint dumps and decompiles remain private. `Log.txt` references below use its 15,240
logical lines; combat references use event text and fight because the raw file has lone CRs.

## Findings ranked by evidence and impact

| Priority | Finding | Evidence and disposition |
| --- | --- | --- |
| 1 | Fictitious Point dispel/freeing value | Native contract + three wasted casts; fixed. |
| 2 | Offensive disease/HP-kill overvaluation | Actual Schir graph + observed zero-HP hits; fixed conservatively. |
| 3 | Helpful caster buff stripped at full HP | Four casts + exact caster-removal graph; fixed, deactivation heal deferred. |
| 4 | Real dispel marked no-reach | Native rule + confirmed removal; fixed before it can create a false block. |
| 5 | Stat/level-drain deaths and obstructed routes | Death/route evidence is strong; mortality needs a separate design, and route-policy changes need movement approval. |
| 6 | Lingering hostility, short cancellations | Anomalies observed; native state/cause not established, no guessed fix. The HP100% discrepancy is closed: the author played with an infinite-health cheat. |
| 7 | Expensive low-value touch planning | Ordering and slow timings confirmed; search-specific gain unmeasured, pruning deferred. |

## Confirmed defects fixed in both modes

### 1. Point dispels earned fictitious unit value — high confidence, wasted standard actions

Babau used Dispel Magic (Point) three times for the old flat 2-point fallback (6430, 7413, 14432). It also earned
28.3 freeing value on Bandit Alchemist (5508), despite the native action's point branch enumerating area effects,
not unit buffs. `ContextActionDispelMagic.RunAction`/`TryDispelAreaEffect` and the actual Point blueprint confirm this.

The fallback, freeing and relief routes now distinguish point-only delivery from unit delivery. Point-only dispels earn
no unit-buff value. Greater Dispel Magic (Area) retains its `AbilityTargetsAround` unit branch; its additional clicked-point
area action does not turn it into a point-only spell. Accurate area utility is deferred: it needs native area eligibility,
order, filters, remaining danger, own-area loss and dispel chance. No flat substitute is invented.

### 2. Caster-only removal looked like an unfiltered cleanse — high confidence, four full-HP casts

Vermlek's Abandon Flesh (9718, 10250, 12481, 13103) removes its own Flesh Armor; it cannot remove an arbitrary harmful
buff from another recipient. `CanRemove` now preserves the named caster filter and the actual recipient, including a
real named self-cleanse. Removing an active helpful caster buff costs its existing damage-equivalent output for active
rounds within the short relief horizon. Suppression, expiry, reapplication and already-priced held states are respected.

The stock Flesh Armor supplies +3 armor AC and a deactivation heal of 2d6+3. That heal is **not modelled** here: this
prevents unsupported full-HP stripping but does not establish good low-HP Abandon Flesh use. Flat caster-removal metadata
does not retain every conditional occurrence; its loss estimate can be conservative. No new actor cache is introduced.

### 3. A real dispel could become a silent no-reach strike — high confidence

At 5515 the observer said Babau's targeted dispel did nothing, while combat confirms it removed Dominate Person.
`CastOutcome` now subscribes to `RuleDispelMagic`. A successful **or failed** native check is evidence that the action
reached that buff's owner, so it cannot become a false no-reach block. Matching uses `Reason.Context`, the dispelling
action's context; `RuleDispelMagic.Context` belongs to the removed buff/area. Targetless area checks do not claim to
reach the scoring anchor. Inert native-layout fixtures exercise the actual observer and finish path; no live cast ran.

### 4. Schir on-hit disease inflated swarm attacks — high confidence, new TB finding

At 3775/3785 both Schirs prefer a 53.5-point full attack on Locust Swarm over Dire Bear (~40) and Venomhodag (~39).
Combat records Bardiche/Gore hits for zero physical HP, but also native Disease saves. This does **not** prove the weapon
immunity probe is broken: its fake native damage bundle already carries the weapon, and valid on-hit riders can fire
on zero-HP hits.

The actual Gray Pox graph is daily, but the old reader projected its NewRound actions as combat-round ticks. The successful
save branch also has conditional Strength damage; choosing the larger branch invented that damage without establishing
its target conditions. The corrected positive on-hit reader withholds unread conditions/trigger restrictions and respects
native tick frequency. Weapon-/event-filtered or contextual own riders are withheld when the aggregate routine
cannot prove them (including the actual Shadow feature); the inspected inner direct-damage action remains readable,
which does not mean the whole kit is supported. Incoming threat retains its conservative reader. The utility of on-hit stat damage/control/later
effects is also separate from immediate weapon HP: it cannot earn an HP kill or HP-progress bonus, and is discounted
when actual HP damage removes the victim. Direct HP in separate on-hit action riders is also retained as utility,
not an immediate kill distribution; ordinary weapon-bundle HP/energy damage keeps its existing native quote. That
conservative limitation can under-credit a lethal rider. Stat/level-zero death still has no exact mortality distribution.

## Domination: timeline and conclusion

The proposed `ShouldControlUnit` bypass is **not established**. No redundant or buff-name-based control guard was added.

1. Fight 8: Succubus starts Dominate Person at 9631; Aeiluvetar fails Will. The first turned-ally row is 10125.
2. During that interval, native party AoOs hit her and her native shot hits Lann for 6. There are no AI Control orders
   to her between the prior completion at 10080 and the next issue at 10637.
3. She then succeeds Will against Dominate Person. The stock buff's NewRound branch grants that re-save and removes
   itself on success. No second failed domination or later re-domination is recorded in fights 8–9.
4. Native `FactionSwitcher.Stop`, `ChangeFaction.OnTurnOff` and faction setters restore faction/player authority,
   update attack-faction state and interrupt commands. Her later AI swarm moves and enemy Focused Shots follow the
   successful re-save. Existing `IsPlayerFaction`/`IsDirectlyControllable` checks enforce that native authority.
5. Later party AoOs in fight 8 still treat her as hostile. Native `IsEnemy` also reads group relations, separately from
   direct-control authority; this is a real unresolved hostility transition, not proof that domination persisted.
   A second shot at Lann just after the save may have already launched; that is not proven either.
6. Fight 9 contains no domination save, party AoO on her or allied shot. Final save faction/attack-faction metadata
   matches the other party actors, but that later snapshot cannot reconstruct fight 8.

A focused native/mod replay should record faction/original faction, group, attack-faction state, controllability and
active ChangeFaction immediately before/after the re-save. Existing logs cannot settle the source of the lingering AoOs.

The original “all 141 friendly HP are AoOs” claim was too broad: **135 HP are native AoOs**, plus **6 HP from dominated
Aeiluvetar's ordinary native shot**. No AI-issued friendly-damage event was established. Similarly, fight 5's successful
dispel and later AoOs do not by themselves prove a second domination or a failed removal.

## Remaining review: losses and interruptions

- **Fight 8:** three Quasits die after repeated Tick Swarm Constitution damage (13, 10 and 16 points respectively).
  Prolonged exposure matters; movement/crowding/identity ambiguity prevents blaming an exact same-named Quasit's choice
  without its ID. This motivates remaining-stat/level mortality and feasible escape work, not an arbitrary HP-health fix.
- **Fight 9:** two Vermleks die, correcting the partial review's count of one. Their fatal sequences include Spectre
  attacks for zero HP with 2-level drain. Bandit Alchemist suffers two 2-level drains and ordinary damage, but receives
  no AI decision/order in that fight (prone refusal at 12647). Exact life-state cause is not logged; no friendly damage
  caused these deaths. A statistical/death-state forecast remains a worthwhile separate design slice.
- **Drow Magus, fight 7 (closed, not a defect):** combat reports 541 HP damage while decisions stay at HP100%. The reader
  directly uses native HPLeft/MaxHP/nonlethal, with no local health cache. The author has since confirmed that this playtest
  ran with an infinite-health cheat: companions took damage without losing HP, so HP100% was the true state. No health
  snapshot is needed. In consequence this playtest exercised no low-HP behaviour (healing, drop risk, retreat, low-HP
  Abandon Flesh). Deaths from stat or level drain are not HP deaths and stand.
- **All six arcan Snowball/Shocking Grasp unacted casts** align with target deaths, once duplicate Mournful Shadows are
  separated by ID. No fusion-delivery defect follows from those interruptions.
- **Deva's two Heal approaches** (11262, 11633) end via the explicit six-second no-progress guard, ~1 m short after
  5.4/2.1 m movement. They spend no healing cast. This is a native/crowded touch-arrival limitation and stays a movement proposal.
- **Four short Aeiluvetar swarm moves** and one Knight Commander Demoralize interruption lack a native cancellation
  reason. Their short durations do not match the stuck guard; movement has no target, so do not call all target-death
  cancellations. Other named Vermlek/Knight Commander interruptions correlate with actor or target deaths.
- Four TB “unacted” advances actually move ~9 m for 3 s. The one rejected five-foot step immediately falls back to
  Torrent. No additional TB-wide stall is established. Positive grapple releases and Griffon Shape are exercised.
- The apparent attack on a commanded Ghoul is a false lead: the commanded `39D16` and attacked `39C3D` are different
  same-name creatures. No stale-target/friendly-fire correction is warranted.

## C6/C8 and the saved spell lists

All twenty saved custom fusions appear in coverage. Three high-level combinations retain disclosed non-joint limits
(Phantasmal Killer variants and Baleful Polymorph/Phantasmal Putrefaction); discovery is not complete joint modelling.

The Excruciating Deformation fusion row at 449 is a forecast, not a cast: it loses to another option. No new broad C6
carrier is issued. Rainbow Pattern/Fireball likewise has a pool forecast but is not cast. Fight 8 ordinary Sleep does
reach two Dretches and both fail native saves; the two earlier fight 6 attempts are interrupted. Hypnotism's conditional
pool stays explicitly withheld.

Only Knight Commander knows ordinary Circle of Death, with five sixth-level slots in the snapshot; no saved fusion
uses it. The log says it is natively unavailable (1354). Its stock requirement is ten Diamond Dust; the saved inventory
has four, consistent with that refusal, without proving the earlier quantity or external cost overrides. Absence of a
cast therefore does not prove missing admission. **New C6 execution and Circle of Death's C8 kill/fused paths remain unplayed.**

## Performance and movement proposals

See [bounded follow-up proposals](COVERAGE7_FOLLOWUP_PROPOSALS.md). No touch-pruning or movement behavior changed.

There are 69 slow rows. All six over one second include the memory-clean-up marker. Light/Guidance/touch-heal timings
include the whole ability and decision-local approach-cache misses, not just spot search. Heap deltas use
`GC.GetTotalMemory(false)`: they are occupancy changes, not measured allocation bytes. There is no valid Unity/native-world
before/after replay here, so no speed-up is claimed and the requested pruning is deferred pending that measurement.

The endpoint/native-route mismatch in fight 7 is confirmed, but the claim that no refusal is remembered was wrong:
dispatch refusals already have round memory plus a 12-second/1-metre exact-key guard. Preflight area failures have only
invocation-local feedback. Same-name actors/targets further limit the repeat inference. The proposal reuses bounded
native-route feedback and existing refusal memory while retaining the hazardous-route gate; it needs author approval.

## Validation and next play

Checked source **`fd9cdda7e60262d4327955daf2ee5be22d3b7a02`**: **1,110 unit tests**, **27/21/3 script tests**, real-reference Release **0 warnings/errors**. All strict/full-stock ProfileAudit sections pass, including **RELIEF 32, FREEING 13, CAST-OUTCOME 12, OFFENSIVE-AFFLICTION 18**. ToggleCosts/TomeBurn/ToyBoxSpend **98/14/10**, supplementary Magus **44**; **4** added foreign signatures resolve exactly. The fresh baseline is PR #102's head; its gameplay source equals played coverage7, but its rebuilt DLL is not claimed byte-identical.

Every audit report line was compared on identical inputs. Only the new CAST-OUTCOME/OFFENSIVE-AFFLICTION sections and
FREEING 12 → 13 / RELIEF 24 → 32 counts (plus separators) differ; every other report line is identical. The receipt records the comparison. Both 15,494-configuration Fusion JSON matrices are unchanged (**14,789 admitted / 13,387 joint**), as is summon-kit detail. Strict scope loads **44,814** records / **4,808** abilities without load/reader failures; full-stock scope also passes. The separately tested known raw-export enum mismatch remains an expected failing input control, not a hidden allowance.

[Check receipt](competence/COVERAGE7_REVIEW_CHECKS.json) · [compiler/reference provenance](competence/COVERAGE7_REVIEW_BUILD_PROVENANCE.json). Offline scalar/layout fixtures and static routes are distinguished from live actor execution; no Unity benchmark or gameplay of this corrected DLL was run.

[Complete 0.3.60 UMM test package](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage7fix1-fd9cdda-UMM-test.zip?raw=true), module **`c61d3aec-6677-4035-b7fa-4614b894f909`**; ZIP SHA-256 `402eda0ef52d211e953c2bf6b73bc5d2b5b83c2c5bae491e917c8afff6f9d1cc`. Identity/checksum sidecars are beside it. **Unplayed**; preserve the checked DLL across documentation commits.

Replay both modes with summon control enabled:

1. Verify Point dispels no longer earn unit cleanse/freeing value; Target and Greater Area still do. Check successful and
   resisted target dispels for absence of false “did nothing”/silent blocks.
2. Vermlek at full HP should preserve Flesh Armor; do not expect optimal low-HP Abandon Flesh healing yet.
3. Schir versus Locust Swarm and an ordinary enemy: zero physical HP cannot be promoted into an HP kill by disease utility;
   daily disease ticks and unproved success-branch damage should not dominate its choices. Keep native rider results.
4. Reproduce domination/re-save and retain both logs plus exact actor IDs and native faction/group snapshots if possible.
5. Explicitly exercise a new C6 carrier and an available ordinary Circle of Death; create a Circle of Death fusion to test
   that fused path. Keep sufficient native materials/resources and a valid target set.
6. Retain swarm/level-drain deaths and touch-route interruptions. Say whether any cheat is on; play without infinite
   health where HP-dependent choices matter. Movement proposals and
   performance pruning require their separate approval/measurement; this package does not claim to fix those.
