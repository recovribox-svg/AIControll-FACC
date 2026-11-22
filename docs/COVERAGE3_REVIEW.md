# coverage3 playtest: review and fixes

21 November 2026, Claude. This page reviews the author's coverage3 logs and describes the fixes. The fixes are in the
coverage4 test package. Both modes are covered. Not played.

**Package:** [coverage4](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage4-361776c-UMM-test.zip)
- startup MVID `248aa48c-27d9-45d0-86cd-303978c0fe79`;
- UMM 0.3.60;
- checks: [COVERAGE4_CHECKS.json](competence/COVERAGE4_CHECKS.json);
- compiler and references: [COVERAGE4_BUILD_PROVENANCE.json](competence/COVERAGE4_BUILD_PROVENANCE.json).

It is coverage3 plus these fixes, on `claude/coverage3-review`. That branch is stacked on `claude/coverage2-review`
(draft PR #95). Nothing is merged.

## The log

- **Build:** startup MVID `60673199`, which is coverage3.
- **Fights:** 15 in total, 6 turn-based and 9 real-time.
- **Commands:**
  - 833 issued and 832 ended. The one without an end row is Orvath's out-of-combat buff, 4 lines before the log stops.
  - No AI Control exception.
- **Raw logs:** not committed. Their hashes are in the receipt.

### What coverage3 was meant to show, and what it showed

| Item | Result |
| --- | --- |
| Real-time area spells with a cast time (coverage2 finding 1) | Fixed. arcan's bursts hurt no ally in 15 fights; in coverage2 they caught allies in 4 of 5 casts (76 HP). Three of his casts were cancelled before they went off, for an ally walking in. One of those cancels was wrong (finding 4). |
| The touch-heal chase (coverage2 finding 2) | Fixed. No unit walked twice to the same heal recipient in a row. |
| R4 timing | Holds. First melee swing 0.64–0.79 s per real-time fight (forecast 0.70 s). |
| Flank forecast | 120 melee attacks compared at the roll. 112 were forecast right, 1 was forecast flanked but was not, and 7 were flanked but not forecast (finding 5). |

## Findings and fixes

### 1. An ally under an enemy's control was attacked until she died (fixed)

**Evidence (fight 15, real time).**
- An enemy's Dominate put Seelah on its side.
- Four units then made weapon attacks on her:
  - Tripper (an approach, then a single attack);
  - Wenduag (a full attack of five, "flanks with Tripper");
  - Bandit (an approach, then a full attack of three);
  - Cambion Bard (a full attack of three).
- Wenduag's and Bandit's last four hits did 74 HP, and Seelah died (combat log).
- Two units used control on her instead, which was right: Bandit Alchemist's Stun Bomb and arcan's Sleep.

**Cause.**
- Native `ChangeFaction` switches the unit's faction for as long as the effect lasts (`UnitDescriptor.FactionSwitcher`).
  From then on she was an enemy to every rule, and her damage counted as a gain.
- A temporary switch keeps the unit's own side: `SwitchFactions(…, permanently: false)` stores `FactionOriginal` before
  the switch. A permanent switch overwrites it.

**Change** (`TacticalEngine.TurnedAllies.cs`, `IsTurnedAlly`).
- A unit whose original faction is the player's and whose current one is not is an ally under an enemy's control. It is
  to be stopped, not killed:
  - It stays an enemy for its danger and for control (Sleep, a stun, a trip, Hold).
  - No weapon attack, walk to attack it, leap or Coup de Grace is aimed at it (`SparesTurnedAlly`).
  - Damage to it is priced as friendly fire: twice the damage it takes, plus the ally's worth if the damage could knock it
    out. This is not under Avoid Friendly Fire's veto, because it is fighting the party.
  - A cast that could kill it for good is not used (finding 2).
- A `[Tactics] … is under an enemy's control (…)` line is logged once per fight.
- The burst re-check counts such a unit as an ally.

### 2. C3: Disintegrate and the permanent-death policy (author's policy)

**The policy.** "A disintegrate that would permakill an ally [should] be weighed in such a manner that it will not be
used. If an ally is at a low enough health where there is genuinely high risk of it killing them, it should be avoided
entirely no matter the circumstances."

**Native.**
- Disintegrate deals its damage and then tests the HP left (`ContextConditionCompareTargetHP`, Less 0).
- If the HP left is below 0, it applies `DisintegrateBuff` (`f7a6a7d2`). That buff kills on activation
  (`ContextActionKill` marks the unit for death). It does not wait for a later round.
- Both outcomes of the Fortitude save carry the test: 2d6 per caster level on a failed save, 5d6 on a passed one.
- With no save rolled, `ContextActionConditionalSaved` runs neither branch.

**Change** (`CombatMath.PermanentDeath.cs`, `PermanentDeathRules`).
- **The scan.** Each ability's action tree is scanned once and cached. The scan finds:
  - dust gates (an HP test that applies a buff killing on activation);
  - direct kills, and buffs that kill on activation outside a gate;
  - the hit-point damage before them, by save outcome.

  Effects on the caster and Hideous Laughter's recast on random enemies are skipped. A fused cast combines both payloads.
  An unreadable tree counts as able to kill.
- **The risk.** For each ally the cast may catch:
  - the chance it is reached (spell resistance, immunity, the attack roll);
  - times, for each save outcome, the exact chance the damage takes its HP left below the gate. This is a convolution of
    the real dice, with Maximize, Empower and Half. Evasion is left out, so the risk is never lower than the game's.
- **The rule** (both modes):
  - at 1 % risk or more, the cast is not used, whatever the settings and its value. It logs "… not used - it could kill
    X for good (P%)" once per fight, ability and ally.
  - below 1 %, the chance costs a hundred times the ally's worth.
  - Summons are exempt: they vanish anyway.
- **Who is at risk:**
  - allies under an enemy's control the cast is aimed at;
  - for an area or a chain that can reach allies and is not Selective, every ally and downed ally inside. A downed ally
    is below 0 HP already, so any damage dusts it.
- **The burst re-check** cancels a cast for an ally who may now be killed for good, past the cancel limit.
- **Fusion coverage.** The dust gate is now a stable fusion guard, applied only to the dust buff.
  - 344 configurations are newly offered, all with the joint value. Examples: Fireball, Burning Hands, Molten Orb,
    Incendiary Runes and Phantasmal Web fused with Disintegrate.
  - Offered 9,655 → 9,999; with the joint value 5,651 → 5,995. None lost.
- **Census.** In the exports, 2 abilities carry a dust gate (Disintegrate and a cutscene copy). 39 carry a direct kill or
  a buff that kills on activation, for example Quivering Palm, Assassinate, Power Word Kill and Word of God. The same
  rule covers them.

### 3. Real-time bursts went off later than forecast (fixed)

**Evidence.**
- The `[Burst] fight summary` lines show arcan's casts going off 0.8, 2.2 and 4.2 s after their forecast.
- In the 4.2 s case the anchor stood 4.7 m from the point when the burst landed, and nobody was hit.

**Cause.**
- Native `UnitCommandController` starts a command only once its unit may act and its action has no cooldown
  (`CanActInCombat && !HasCooldownForCommand`).
- A surprised unit also waits for its initiative.
- The forecast counted the walk and the cast, but not this wait.

**Change** (`TacticalEngine.ReadySeconds`).
- Real time, the wait is read from the unit's cooldown for the ability's action type and its initiative wait.
- The walk runs meanwhile, so the cast starts at the later of the two (`DisruptRules.OrderedStartSeconds`, which the
  disruption timing already used).
- The burst plan, its lead point, its re-check and the cast-disruption timing all count this wait.
- The plan line shows "ready in X" when the wait is the longer.

### 4. The caster's own walk cancelled its burst (fixed)

**Evidence (fight 13).** arcan's Explosive Runes / Hideous Laughter was cancelled before casting: "arcan walks into it",
with 5 enemies inside.

**Cause.** The re-check predicted every unit's position with an uncertainty that grows with time, and that included the
caster. But the caster's walk is the cast's own approach. It stops at the cast's range from the point
(`UnitCommand.IsUnitCloseEnough`).

**Change.** While the caster's cast has not started, the caster counts as inside only if that stop is, or the spot where
it stands.

### 5. Flank misses: allies arriving during the attack (fixed)

**Evidence.**
- Each of the 7 "flanked but not forecast" attacks had no other engager at the decision.
- At the roll, allies whose command targeted the enemy were engaging it. Example: Bandit → Minotaur, where Seelah and
  Tripper were "its command targets it".

**Change** (`CombatMath.ArrivingEngagers`).
- Real time, when no other unit engages the target, the forecast counts a party member or pet that is ordered to make a
  melee attack on the target and reaches it before this attack lands. It is read from the ally's own command; there is no
  path query.
- Such an ally's own gain is not credited (it is its own decision's).
- A missed flank now also names the engagers with no command on the target at the decision (evidence).

### Found, not changed

- **Freeing an ally under an enemy's control.**
  - Dispel Magic or Protection from Evil on such an ally is not valued for what it gives back.
  - Finding 1 stops the killing. Valuing the release needs a reader for which abilities can end a `ChangeFaction`
    effect. Proposed as a follow-up.
- **Nauseated inside two swarms (fight 10, Seelah).**
  - Her attack was dropped: a nauseated unit can only walk.
  - The exit search found "no free spot out of its space within a step or a short move", and she did nothing more that
    fight.
  - A longer search would be a movement change, so it is proposed, not done.
- **Alchemist's Fire splash (fight 13).**
  - Bandit Alchemist threw it at a Nightmare, and the splash hit Wenduag for 8 HP. She had walked up in the meantime.
  - The splash has no re-check of its own. Too small to justify one now.
- **Performance:**
  - 14 decisions took longer than 500 ms (552–989 ms), and 13 of them ran a memory clean-up.
  - Orvath had 5 of them.
  - Unchanged from earlier builds. No speed-up is claimed.

## Checks

On `361776c`:
- Release build: 0 warnings.
- Tests: 1,065 unit (6 new) + 27/21/3 script tests.
- ProfileAudit exit 0:
  - new `PERMADEATH` 24;
  - `BURST-AIM` 78, `EVIDENCE` 15, `SNEAK` 36;
  - `FUSION-OUTCOME` 32 (its Disintegrate gate moved to `PERMADEATH`).
  - Every other line, and the summon detail, are identical to coverage3 on the same inputs. The Fusion coverage gains
    the 344 Disintegrate configurations above and loses none.
- Native harnesses: 98/14/10.
- 7 added game references, all resolved: faction, initiative, surprise and the HP test's value. None removed.

## Replay (coverage4)

Record the startup MVID `248aa48c`. Send `Log.txt` and `combatLog.txt`, and say where you spawned enemies, rested, or
gave manual orders.

1. **An ally under an enemy's control** (an enemy with Dominate Person or a charm, both modes).
   - The log shows "… is under an enemy's control …".
   - The party controls the ally (Sleep, a stun, a trip) or ignores it. It makes no weapon attack on it.
2. **Disintegrate, fused or on its own** (a caster with Disintegrate in the spellbook).
   - Aimed at enemies it is cast as usual. A fused one shows its `[FusionValue]` line.
   - With a low-HP or downed ally inside a fused area, it is not cast: "… could kill X for good".
3. **Real-time bursts.**
   - The plan line may show "ready in X".
   - `[Burst] fight summary`: the casts should go off close to the forecast.
4. **`[Flank] fight summary`.** Fewer "flanked but not forecast".

## Not in this build

- R6, R7 and the death-trigger proposal remain set aside.
