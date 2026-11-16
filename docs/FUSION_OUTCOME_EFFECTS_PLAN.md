# Next feature plan: fused spells whose effects land on every save outcome (Fusion F4)

16 November 2026, Claude. **Status: slices A and B are implemented** on `claude/fusion-outcome-effects`, see
[FUSION_OUTCOME_EFFECTS.md](FUSION_OUTCOME_EFFECTS.md); the author's gaps3 log answered missing input 1 there.
This document itself is the plan: no gameplay code, movement policy, build, package, merge, `main` push or
release is part of it. Base checked: development `980cf26` (`claude/dreamy-pasteur-b9h3rn`).
Evidence receipt: [FUSION_F4_PLAN_EVIDENCE.json](competence/FUSION_F4_PLAN_EVIDENCE.json).

Evidence levels used below:

- **Verified:** read in the supplied game assembly, the author's blueprint exports, the current source, or counted
  by the audit tool.
- **Inference:** the likely consequence of verified facts; not seen in play.
- **Proposal:** design to be built; no measured benefit is claimed.

## 1. Decision

The project I recommend planning and building next is **Magic Fusion F4: value and admit fused spells that apply
an effect when the target passes the shared save, or on both outcomes.** Today AI Control refuses to cast every such
spell. It is not the tactical-mobility project the research ranks first, and section 3 gives the reasons.

In one paragraph: a single refusal rule keeps **2,874 of the 15,494** fused-spell configurations a Magic Deceiver can
build off the table (7,306 are admitted today). A private what-if run shows that the existing shared-save value model
can already price **638** of them once it tracks control per save outcome. The change is confined to three Fusion
files, adds no rule query, path search or movement, and needs no movement approval. The author plays a Magic Deceiver
in every recent playtest.

What would change this decision is in section 3.3.

## 2. What was checked first

- **Development and later work.** Development is `980cf26`. Two PRs are open, both from Codex and both based on it:
  - [PR #74](https://github.com/recovribox-svg/AIControll-FACC/pull/74), the research. Documentation only.
  - [PR #75](https://github.com/recovribox-svg/AIControll-FACC/pull/75), "Fix Coup de Grace reach to match the
    delivering melee weapon", head `4110c70`. It appeared while this plan was being prepared and already implements
    the Coup correction the research proposed (section 9). It does not touch the Fusion files.
- **Inputs.** The author's 14 export folders, the supplied `Assembly-CSharp.dll`, the 607 stock fixtures rebuilt
  from the BubblePrints 2.7.0x database, and the integrated source. The raw playtest logs were lost with the earlier
  container; play evidence below comes from the committed reviews.

## 3. The candidates compared

### 3.1 What verification changed in the research's ranking

| Research claim | What I found |
| --- | --- |
| Mobility is "shared by companions and summons" | **Verified, narrower.** The author's summons are the 29 Demonology creatures. Three of them have a position ability, and none is a self-only teleport: Vrock (enemy-anchored, carries group members within 5 ft), Derakni (carries group members within 10 ft), Omox (Liquid Leap, not inspected). The Demonology Babau is `CR6_BabauStandard`; the Babau with Demon Teleport is a different unit. A self-only first slice gives the played summons nothing. |
| Start with "one verified self-only native teleport" such as Demon Teleport | **Improved.** Demon Teleport is one Demon-path spell. The broader self-only family is `AbilityCustomDimensionDoor` with radius 0 and a point target: 15 player abilities share that one delivery (section 3.4). |
| No played owner is named | **Verified gap.** No committed review shows a party member owning any of those abilities. |
| Destructive Shockwave is a native miss-damage example | **Verified.** A melee miss applies a 1-second buff that deals the attacker's Strength bonus as physical damage, not multiplied on a critical. It is a mythic feat; no owner is known in the party. |
| The Fusion gap is "168 saved Frigid templates" | **Larger.** 168 was the Frigid Touch part exposed by PR #69. The same refusal is present in 3,616 configurations and is the only blocker in 2,874. |
| Screech and Drone labels do not prove a runtime gap | **Verified on the audit side** (section 10). |
| Coup de Grace range can differ from the delivering weapon | **Verified** (section 9). |

### 3.2 Benefit, risk and cost

| | Fusion F4 (chosen) | Tactical mobility | Miss damage | Unholy Nimbus |
| --- | --- | --- | --- | --- |
| Who gains | Magic Deceivers. The author plays one (arcan) | Owners of a self-only teleport. None verified in the party or among the played summons | Owners of one mythic feat. None verified | Nalfeshnee summons. Not seen driven in a log |
| What is wrong today | **Verified:** the spell is never cast; the log says "cast withheld" | **Verified:** the ability is unread and never used; the unit walks | **Verified:** a small value is missing from attack forecasts | **Verified:** valued by the old quality guess |
| Size, measured | 2,874 configurations blocked only by this rule; 638 priced by the first slice | 15 player abilities, 0 known owners | About one Strength bonus per missed melee attack | 1 ability, 1 creature |
| New mechanisms | None. Extends the existing outcome model | New candidate kind, destinations, landing checks, a two-step turn, dispatch | One new trigger route | A delayed-payload contract |
| Movement approval | Not needed | Needed | Not needed | Not needed |
| Main risk | A newly admitted spell is mispriced; contained by the existing gate | A unit teleports into danger or away from its group; real-time allies walking into a group radius | Low | Friendly fire at an unknown later position |
| Cost | Small to medium for the first slice | High | Small | Medium to high |
| Earliest useful package | First slice alone | Needs all of the above before any use | First slice | After the contract |

### 3.3 Why not mobility first, and what would change that

Mobility is the larger new capability, and I expect it to be worth building. Two things put it second:

1. **No played owner is verified.** The benefit to the author's party and summons cannot be shown, and a replay would
   need a character built for the test.
2. **It costs several times more and needs approval for a movement change,** so it should start from a confirmed
   case.

It becomes the better choice when one of these is true:

- a party member owns one of the abilities in section 3.4;
- the author wants the played demon summons (Vrock, Derakni) to teleport. That is a group or unit-anchored contract,
  which the request keeps separate from a self-only slice.

### 3.4 Findings to carry into a later mobility plan

Not planned here; recorded so the work is not repeated. All **verified** from the exports and the assembly.

- Self-only point teleports through `AbilityCustomDimensionDoor`, radius 0:

  | Action | Abilities |
  | --- | --- |
  | Move | Arcanist Dimensional Slide; Travel domain Dimensional Hop (Cleric and Separatist); Warpriest Travel blessing; Wizard Conjuration Dimensional Steps; Monk and Drunken Master Abundant Step; Arcane Rider Dimensional Ride; Hermit Recluse's Stride; Walk Through Space |
  | Swift | Spell Dancer Dimension Door; Witch of the Veil Shrouded Step; Cloak of Morta |
  | Standard | Dimension Door (caster only); Scaled Fist Abundant Step |

- Through `AbilityCustomTeleportation`: Demon Teleport (Move) and Aeon Uncertainty Principle (Swift).
- Native delivery: the game moves the unit at once, suppresses attacks of opportunity for the next frame and adds a
  short system buff. The destination must be on the navmesh and out of the fog of war. Dimension Door blueprints
  ignore line of sight; Demon Teleport does not.
- With a radius above 0 the game also moves every member of the caster's group inside the radius, and their mounts.
- **Inference:** a Move-action self-only teleport has the same action cost as a walk. It could therefore be offered
  as another way to reach the spot an existing walk-first option already priced, without a new destination search.

## 4. Player benefit, failing examples and non-goals

### 4.1 Benefit

A Magic Deceiver's fused spell is cast when it is the best action, including when it carries an effect that:

- lands when the target **passes** the shared save (Fear leaves the target shaken for a round; Boneshatter leaves
  it fatigued), or
- lands **whatever** the save result (Frigid Touch staggers for a round).

### 4.2 Current failing examples

**Verified** by the audit on the integrated source (`--fusion-coverage`, 60 sources, 15,494 configurations):

- Every such configuration is withheld with
  `[Fusion] <name>: buff outside the shared failed-save branch; unsupported forecast, cast withheld`.
- Nine source spells cause all 2,874 sole-blocker cases: Ray of Exhaustion, Molten Orb, Fear, Disintegrate,
  Boneshatter, Reduce Person, Acid Arrow, Enervation and Frigid Touch.
- Examples the first slice admits (each counted in the what-if run): Fireball/Fear, Cone of Cold/Fear,
  Hold Person/Boneshatter, Slow/Frigid Touch, Incendiary Runes with Fear, Boneshatter or Frigid Touch, and
  Explosive Runes with Fear or Boneshatter.

**Verified from committed reviews:** arcan built seven Runes fusions in the fusion2g1 session. Frigid Touch, alone or
fused, was offered and not issued in gaps1 and gaps2.

**Not verified:** that arcan's present bar holds a withheld fusion. His seven played fusions used the no-save Runes
form, which is already admitted. Template counts are not player configurations.

### 4.3 Non-goals

- No change to ordinary (unfused) spells, including their pass-branch effects.
- No second save, attack roll, target or action; no guessed critical rate for Frigid Touch's longer stagger.
- No support for independent nested saves, shared Hit Dice pools, lines, secondary areas, healing or caster-side
  payloads. Those are Fusion plan slices F5 to F8.
- No paid next-spell boost planning for fusions.
- No movement, target-commitment, retreat, pathfinding or stuck-cancellation change.
- No NPC casting quotas, random casting, flat focus bonuses, spell-level buff tiers, Rogue Trader action-point rules
  or model calls in combat.

## 5. Native contracts

All **verified** by decompiling the supplied assembly.

1. `AbilityEffectRunAction.Apply`, fused cast: one root save of type `MagicHackData.SavingThrowType` (rolled again
   for Persistent), then both source spells' action lists run in order under that single result.
2. `ContextActionConditionalSaved.RunAction` reads that result: passed runs `Succeed`, failed runs `Failed`. With no
   save in the context it logs an error and runs neither.
3. `ContextActionSavingThrow.RunAction`, fused cast with a root save and without `MakeSaveAlsoForMagicHack`: no new
   roll; its actions run under the root result.
4. `ContextActionApplyBuff` has no save test. Outside a saved branch it applies on both outcomes.

So each buff application has exactly one of three paths: **Failed**, **Passed** or **Both**. The game rolls nothing
further.

## 6. Current code to reuse

| Where | What it does today | Change |
| --- | --- | --- |
| `AbilityProfile.Fusion.cs`, `InspectFusionPaths` | Labels every buff and damage action with `FusionSavePath`, then refuses a target buff that is not on `Failed` when a root save exists | Record the requirement instead of refusing |
| `TacticalEngine.Fusion.cs`, `FusionOutcomeModel` | Metadata gate for the shared-save value model, cached per profile | Reused as the admission gate for these profiles |
| `TacticalEngine.Fusion.cs`, `TryFusionControlRounds` | One control envelope; skips `Passed` occurrences | Two envelopes filled in the same pass |
| `ValueModel.Fusion.cs`, `FusionValue` | Control counted on failure only | Adds the passed-outcome term |
| `CombatMath.Fusion.cs`, `FusionDamageFactor` | Damage already split by Failed, Passed and Both | Unchanged |
| `TacticalEngine.cs`, `EvaluateAbility` | Withholds on `FusionUnsupported`; uses the old forecast when the gate fails | One added test after the existing one |
| `TacticalEngine.Fusion.cs`, `FusionControlCatches` | Decides whether a caught ally is harmed | Counts either outcome |

Already present and **not** to be added again: per-application applicability, immunity and already-present checks,
overlap capping (`ControlEnvelope`, 32 entries), duration memo, kill-before-control ordering, the six-angle cone
re-aim, friendly-fire veto, configuration identity in every memory key.

## 7. Slices

The "unlocks" column is the first remaining refusal reason of the existing gate for each of the 2,874 configurations,
from the what-if run. A later slice must be measured again after the earlier one.

| Slice | Work | Unlocks | Depends on | Size |
| --- | --- | --- | --- | --- |
| **A. Control per save outcome** | Sections 7.1 to 7.3 | 638 | none | Small to medium |
| **B. Exclusive branches** (Ray of Exhaustion) | The guard-order check ignores an earlier application in the opposite branch of the same `Conditional`. An application in the other source spell still refuses | up to 410 | A | Small |
| **C. Main-target guard** (Molten Orb) | Read `ContextConditionIsMainTarget` against the candidate's clicked target; burning stays an unpriced benefit and a counted ally harm | up to 496 | A | Medium |
| **D. Death marker** (Disintegrate) | Recognise the exact graph: target at 0 hit points after the damage, then a permanent buff whose only action is a kill. No extra enemy value; a caught ally that drops counts as dead | up to 416 | A | Medium |
| **E. Carriers that are not control** | Acid Arrow's periodic damage through the attached-damage reader; Reduce Person and Enervation on enemies valued by sign, or refused | up to 844 | A, and each on its own evidence | Medium to high |

**Earliest useful package: slice A alone.** B may ride in the same package if its audit check passes. C, D and E each
need a stated reason from the author's actual fusions (section 13) and are separate PRs.

### 7.1 Slice A, profile (`AbilityProfile.Fusion.cs`)

- Replace the `LimitFusion("buff outside the shared failed-save branch", …)` call with a recorded flag and its first
  location, for example `FusionNeedsOutcomeModel`.
- Keep every other refusal, including "kill outside the shared failed-save branch", "nested shared-save branch" and
  "shared apply action on different save branches".
- `FusionLimitations` keeps listing the location, so the audit can still name it.

### 7.2 Slice A, admission (`TacticalEngine.cs`, `EvaluateAbility`)

- After the existing `FusionUnsupported` test: when the profile needs the outcome model and `FusionOutcomeModel`
  returns false, withhold the cast and log once per configuration:
  `[Fusion] <name>: effect on a passed or either save outcome; joint value not qualified (<reason>); cast withheld`.
- Such a profile never uses the old forecast. That forecast multiplies control by the fail chance and cannot
  represent a pass-branch or unconditional effect.
- Put the rule in one predicate used by the engine and by the audit, so `profileAccepted` in the coverage report
  means "the engine would offer it".
- A shadow copy of such a fusion stays withheld (`FusionOutcomeModel` already refuses shadow copies).

### 7.3 Slice A, value (`TacticalEngine.Fusion.cs`, `ValueModel.Fusion.cs`)

- `TryFusionControlRounds` returns two numbers. Each priced occurrence is added to the failed envelope when its path
  is Failed or Both, and to the passed envelope when its path is Passed or Both. Share, duration, immunity and
  already-present checks are computed once per occurrence, as now.
- `FusionValue` gains `controlRoundsOnPass`:

  ```
  control = delivery × controlWorth ×
            ( fail × (1 − failedKill) × roundsOnFailure
            + (1 − fail) × (1 − passedKill) × roundsOnPass )
  ```

  With `roundsOnPass = 0` this is today's formula, so every currently admitted fusion keeps its value.
- With no root save only the Both path exists and the fail chance is already 1; unchanged.
- `FusionControlCatches` treats an ally as caught when either envelope is above zero.
- When one enemy cannot be priced (unknown guard or duration), the existing branch keeps its damage and credits no
  control for that enemy. An ally in the same state counts as caught, as now.
- The `[FusionValue]` row prints both envelopes.

## 8. Accounting, consistency and safety

- **Action and resources.** One native cast and one slot at the fused spell's native level. Conservative, Aggressive
  and Pure Mage policies, consumable policy and the joint cost check are untouched.
- **Recipients.** The recipients are those the selected delivery reaches (the F1 resolver). One shared save per
  recipient; each application is tested on that recipient with its live guards.
- **Uncertainty.** Unknown means no credit for an enemy and "caught" for an ally. Frigid Touch's longer critical
  stagger and burning's periodic damage stay unpriced. Allies keep the longer duration (PR #69).
- **Failure recovery.** Unchanged: `CastOutcome`, the command trace and the per-target "reached nothing" safety net,
  all keyed by the configuration identity.
- **Player ownership and blocklists.** `UserAbilityPolicy` runs before appraisal and again at `Execute` and in the
  dispatcher. The implementation adds a check that a blocked fused spell stays blocked after admission. Manual casts
  are not touched.
- **Both modes.** `EvaluateAbility` is shared by turn-based and real time. A Magic Deceiver already uses the shared
  engine when the old real-time engine is selected.
- **Other mods.** Profiles read the live graphs. A source spell another mod has patched with an unread guard fails
  the gate and stays withheld, as the Runes fusions did before PR #66.

## 9. Coup de Grace (separate, already in PR #75)

- **Verified independently:** `AbilityData.GetApproachDistance` takes Weapon range from `GetFirstWeapon`;
  `ContextActionMeleeAttack.RunAction` attacks with `GetThreatHandMelee`. A ranged first weapon with a usable melee
  hand or limb can therefore approve a distance the delivering weapon does not have.
- **Not claimed:** a live reproduction. Gaps2 contains no issued Coup de Grace.
- **Correction:** the one I would propose is what PR #75 describes: select the delivering hand with the game's own
  selector, use it for the estimate and for the reach, cap only AI Control's own command radius, recheck at dispatch,
  no global patch. I have not reviewed its code; that is the normal review step before a merge.
- **Test:** its replay list is the right one. Two cases decide it: a bow user with a natural or off-hand melee attack
  and a sleeper beyond melee reach (must approach or do something else), and a reach weapon on a large body (legal
  reach must stay usable).

## 10. Summon evidence (separate, small)

- **Verified:** `tools/ProfileAudit/Audit.cs`, `Classify`, personal-area branch: the enemy side requires
  `HarmfulEffects`, damage, a maneuver or a weapon attack. Control strength is not in the test. The inventory's
  "buff by old quality" label for Vrock Stunning Screech and Derakni Drone therefore does not show what the engine
  does with them.
- **Not verified here:** the engine's per-recipient value for those two; no log has a driven cast of either.
- **Proposal, tooling only:** align that branch with the engine's side selection and add a `SUMMON-KITS` check on the
  two blueprints. Decide on Unholy Nimbus after a driven Nalfeshnee appears in a log.

## 11. Bounds, caches and performance

- **Work per decision.** No new rule trigger, context, path, world or target query. Per recipient: the existing
  single pass over at most 32 applications, and one more scalar envelope.
- **Allocation.** One more `ControlEnvelope` scratch per decision context, created on first use.
- **Cache keys and invalidation, unchanged:**
  - profile: the `MagicHackData` instance, checked against holder, both sources, delivery, additional area, level,
    school, save, shape and touch;
  - gate result: once per profile;
  - duration: ability, action and recipient within the resistance scope and its version;
  - memory keys: `AbilityDiscovery.Key` with `FusionIdentity`.
- **Performance comparison.**
  - A newly admitted spell is scored where it was skipped before. That is the feature, and no admitted spell or
    target may be dropped to recover time.
  - Baseline: arcan's decisions with only already-admitted fusions, before and after. The per-ability stage time and
    `[FusionValue]` numbers must match within noise.
  - Then add one, three and five newly admitted fusions. The expectation to test: each costs about what one admitted
    fusion of the same shape costs today.
  - Offline: a warm, actor-free microcheck of the two-envelope path, 10,000 calls, 0 bytes.
  - Heap deltas in the slow-decision line are not allocation totals.

## 12. Tests, checks and replay

### 12.1 Automated

- **Unit tests (`ValueModel`).**
  - `roundsOnPass = 0` reproduces every existing `FusionValue` expectation.
  - Pass-only control is worth `delivery × (1 − fail) × (1 − passedKill) × worth × rounds`.
  - Control on both outcomes does not depend on the fail chance when neither outcome kills.
  - A passed-outcome kill removes the passed-outcome control.
  - Fear's pair: a long failed-branch effect and a one-round passed-branch effect never add into one envelope.
- **ProfileAudit, `FUSION-VALUE` on the actual graphs.**
  - Path labels of Fear, Boneshatter and Frigid Touch under each root save.
  - The admission predicate, and the compiled route from `EvaluateAbility` to it.
  - Negatives that must stay refused: kill outside the failed branch, one apply action on two branches, a helpful
    carrier, an independent nested save, a saved branch without a root save.
  - The `VALUE-SELECTION` check "newly exposed saved-cast side effect retains the existing unsupported boundary"
    is replaced by "saved Frigid configurations are admitted only through the outcome model".
- **Fusion coverage, expected after slice A.**
  - Admitted 7,306 to 7,944; gate-qualified 3,118 to 3,756.
  - The other 2,236 are withheld under the new reason, split as in the receipt.
  - No previously admitted configuration changes its gate result (0 in the what-if run).
  - Reader errors 0; two runs identical.
- **Build.** Real-reference Release, 0 warnings and errors. Expected added game references: 0.
- **Unchanged.** Unit suite, inventory, supplement and ledger tests; native harnesses 98/14/10; the full ProfileAudit
  with every other section's line identical.

### 12.2 Replay, turn-based and real time

Record the startup MVID and keep the paired AI and combat logs.

1. Have arcan create two or three saved fusions from section 4.2, with different root saves, for example
   Fireball/Fear with Reflex and with Will.
2. Expect one `[Fusion] … shared save …` row and one `[FusionValue] … control fail/pass …` row per configuration,
   and the spell among the candidates.
3. When it is cast, the combat log should show, on a passed save, Shaken for one round or Fatigued, and on a failed
   save, Frightened or Exhausted. Frigid Touch should stagger on both.
4. A target that passes the save and receives the pass effect must not produce a "no effect" verdict.
5. Put an ally in a Fear cone with Avoid Friendly Fire on: expect a turned aim or no cast.
6. Block one of the new fusions in the catalogue: it must not be cast.
7. **Mixed pack.** Repeat with four to eight driven summons in melee with the targets. Summons do not cast fusions;
   the pack tests caught allies in cones and bursts and the decision time with many bodies.
8. Regression: the existing Runes fusions, ordinary Fear and Boneshatter, and a fused spell that is still withheld
   (for example one with Molten Orb) behave as before.

### 12.3 Package rules, if a build is produced

- A development test ZIP named `AI-Control-FACC-v0.3.52-dev-outcome1-<checked commit>-UMM-test.zip`, with its
  SHA-256 file and identity JSON (DLL hash, MVID, checked source).
- A build provenance record: SDK and compiler, references, effective options, Source Link revision.
- The checked DLL stays frozen across later receipt and documentation commits.
- Manifest stays 0.3.52. No `main` push, tag or release.

### 12.4 Go and no-go

- **Go to build slice A:** the author agrees with the choice. No movement approval is involved.
- **Go to package:** the coverage numbers match section 12.1 or each difference is explained; all tests pass; the
  already-admitted fusions' values are unchanged.
- **No-go in play, any one of:**
  - a newly admitted fusion is cast repeatedly with "no effect" verdicts;
  - an ally receives a pass-branch or unconditional effect that the log did not count;
  - arcan's slow-decision rows grow by more than the added spells explain.
- **Later slices:** B with A when its check passes. C, D and E only for sources the author actually fuses.

## 13. Missing inputs

1. **arcan's present fusion list:** the `[Fusion] <name>: <spell 1> + <spell 2>; delivery …, shared save …` and
   "cast withheld" rows from one `Log.txt` of the gaps3 build or later. It shows whether a played fusion is withheld
   and which later slice matters.
2. **Which sources the author wants to fuse** among Fear, Boneshatter, Frigid Touch, Ray of Exhaustion, Molten Orb,
   Disintegrate, Acid Arrow, Reduce Person and Enervation.
3. **A performance baseline:** the same log's slow-decision rows for arcan.
4. **For a later mobility plan:** which party members own an ability from section 3.4, and whether the demon
   summons' group teleports are wanted.

## 14. Does it warrant its cost

- **For:** the refusal is verified and counted; the native rule is simple and fully read; the model, caches and
  safety checks exist; the first slice is small and can be measured exactly before any play.
- **Against:** the benefit reaches only Magic Deceivers. arcan's seven played fusions are not blocked today, so the
  gain for the author depends on fusing a saved spell with one of the nine sources. Input 1 settles that in one log.
- **Risk if built:** the engine starts casting spells it has never cast, so any latent mispricing in the existing
  outcome model for these sources becomes visible. The first-use `[FusionValue]` row and the replay are the guard.
- **If input 1 shows no interest in saved fusions,** the cost is still small, but the mobility project with a
  confirmed owner would then be the better next step.
