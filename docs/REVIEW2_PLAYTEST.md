# Review2 playtest: findings and corrections

17 November 2026, Codex. Played **v0.3.60 review2-f0b6097**, module
`0c600255-e6b8-4788-a5bc-4e0a9ad5f7e9`. The author used ToyBox spawns and rested **between** fights, left Grabber to the
engine, and reported Wenduag's successful Death from Above. This review checks the wider session, not just that ability.
[Input census](competence/REVIEW2_PLAYTEST_INPUTS.json) records file hashes and evidence boundaries; raw inputs stay private.

**Result:** the leap and two walk-in burst cancellations are confirmed. There is a real, consequential support gap for
enemy death explosions. The implemented corrections remove unnecessary buff-preview work and repair the offline
ledger's initial-fight count. **No combat weights, invocation cap, movement/commitment policy or death-risk strategy is
silently changed.** Native death-risk handling needs the separate proposal below; it is not claimed fixed by this build.

## Source and next package

Development remains `ad63ee8`. Task branch `codex/review2-playtest` starts at PR #90 head
`29cfc4ef6cda16d803fc31e6869d8c75834abfcb`; its C#/project/manifest equal played `f0b6097`.
All ten prior stack PRs (#81–90) were open when inspected. No merge, development/main update, tag or release.
The new draft correction PR follows **#81 → … → #90** and targets `claude/dreamy-pasteur-b9h3rn`.

Checked new source **`d669ec1bb0af2b0a7d5df872e19db9c1cbe6ae93`**, startup MVID **`e1aa77fe-fef8-4496-aae9-9c751d2974ec`**; UMM stays **0.3.60**.
[review2fix1 ZIP](../test-builds/AI-Control-FACC-v0.3.60-dev-review2fix1-d669ec1-UMM-test.zip?raw=true), [identity](../test-builds/AI-Control-FACC-v0.3.60-dev-review2fix1-d669ec1-UMM-test.identity.json),
[checksum](../test-builds/AI-Control-FACC-v0.3.60-dev-review2fix1-d669ec1-UMM-test.sha256). SHA-256 **`e2a3d3922c62c0dcc3415a0254facc3bbc15a661957b852f064326824216905e`**.
The checked DLL is frozen across later documentation commits.
[Checks](competence/REVIEW2_PLAYTEST_CHECKS.json) / [compiler/reference provenance](competence/REVIEW2_PLAYTEST_BUILD_PROVENANCE.json).

## Session coverage

- **13 fights**, five TB and eight RT; **643 TB / 669 RT** ordinary decision rows. The first fight began before the log's
  start marker was available; the later twelve explicit starts do not mean there were only twelve fights.
- **No AI Control exception** is logged. Eight blueprint-owner warnings come from `WrathPatches` and name cloned
  Isekai/other-mod components. That identifies a separate native/mod-data warning, not an AI Control exception or proof
  those abilities are unusable. No external blueprint is mutated to silence it.
- Native command results include **three acted failures** and many interrupts. An acted interrupt is not proof an attack
  failed: TB attacks commonly end that way after acting; despawning/dead targets and combat end can interrupt other
  orders. The two recorded burst cancellations have explicit reasons. This review does not turn every interrupt into a bug.
- Diverse driven summons, ordinary/fused spells, swarm and mixed encounters, healing, invocation transfers, forms,
  seven grapple releases, four actual Coup abilities and two Coup approaches are present. The Drow Magus uses a bow;
  this is not the missing melee Spellstrike replay. No suppressed-cleanse action is selected, so F3's gameplay edge
  remains unexercised.

## Highest-impact finding: unpriced death explosions (proposal)

Fight 13's Risen Followers are not simply high sustained-damage melee enemies. Combat rows **43160–43302** show one dying
and then dealing **339** party damage through **Zombie Rib Bomb** and **Zombie Electric Explosion**; the second deals
**146** at **44935–45060**. Together that is **485 of the fight's 501** enemy damage to the party. Bandit and Tripper
die after the first burst. These values are completed native outcomes, not a damage forecast or an inferred drain.

The supplied unit/buff/ability graphs and native IL confirm `DeathActions → ContextActionCastSpell`:
slashing **10d6**, electricity **10d6 + 6d6 unholy**, **Reflex DC 30**, half on save, enemy recipients within **10 ft**.
`ContextSetAbilityParams` supplies the fixed parameters. Actual active facts matter: one extra buff is difficulty-gated
in the unit graph, and a static unit alone must not be used to assume every instance has every death effect.
The second variant uses Meat Bomb and Unholy Dispel; the latter also contains a caster-level dispel of one spell buff.
It must not be treated as merely another identical electrical blast.

`TacticalEngine.DamageValue` prices damage and removal of an enemy; `EnemyThreat` predicts recurring attacks/casts and
observes dealt damage. Neither prices this terminal harm when choosing to kill the unit. Raising its continuous threat
or lowering global danger calibration would not supply that missing decision model. Likewise, blindly subtracting all
future death harm from every attack can make the last enemy unkillable by the planner.

**Not implemented.** [DEATH_TRIGGER_SAFETY_PROPOSAL.md](DEATH_TRIGGER_SAFETY_PROPOSAL.md) gives native facts, a bounded
read/valuation design, progress requirements, mode/caching tests and the approval boundary for any movement response.
The risk is real in these logs; the best complete strategy is a design question. The proposal does not assume that
holding, sleeping or teleporting these undead avoids the trigger.

## Implemented: remove unused constant-stat preview contexts

### Evidence and scope

There are **149** slow-decision rows: median **346 ms**, maximum **1,799 ms**. Ability work dominates many rows. Examples:
Lann's rows **4149/4156** report approximately **4.9/5.0 MB** positive ability-stage heap changes; arcan's **4719** reports
about **19.6 MB**. These are sampled heap deltas, **not exact allocations**. Fourteen slow rows have a stage heap drop
over 10 MB, consistent with garbage collection; that does not prove any one ability caused the entire pause.

Source inspection found that `AppraiseBuff` always created a full native ability context even for a completely read,
permanent, fixed stat bonus. The same is repeated when pricing a unique holder or removed sibling. For all twelve
Profane Invocation variants, the existing numerical reader consumes the fixed **+2**, and duration consumes the native
**Permanent** flag. Neither uses the context or its caster level.

### Correction

`CanSkipBuffContext` admits only completely read **Strength/Dexterity/Constitution/mental-stat** parts with constant
amounts, exact inspected `ContextActionApplyBuff` applications and permanent duration. It excludes ranked/dice/pooled/
caster-level/granted/caster-fact/weapon-specific parts, nonstat effects, unknown applications/action subclasses and
equipment carriers. Everything else keeps the original context path.

The same appraisal kernel still reads recipient stats, native stacking, offence/defence, useful rounds, unique-holder
and sibling losses, settling and summon/role scaling. No actor/result cache, special invocation identity rule, numerical
retuning or extra rule/world query is added. The metadata helper's boxed LINQ enumeration is also replaced by the
concrete list enumeration. The offline warm eligibility check changed from **40 bytes/call** in the first implementation
to **zero**, and avoids the unused context for every admitted appraisal.

**39 new ProfileAudit checks** exercise actual twelve-variant eligibility, production **+2** amounts and the existing
**100-round permanent-buff forecast convention**,
context-dependent/timed/equipment/unknown rejection, compiled production routing and a 10,000-call warm predicate.
The scalar check uses neither caster nor target nor a native cast. This proves the narrow eligibility and unchanged
numbers, not an in-game speed-up. The other heavy caster/fusion/path work remains; this does not claim every pause fixed.

## Implemented: initial-fight accounting in the offline ledger

The ledger reported twelve starts while retaining the first fight's casts under scope **0**. It now counts an observed
opening combat fragment, explicitly labels its missing start marker, and retains the existing scope ids and transfer
evidence. Startup/out-of-combat text or an unmatched completion cannot invent a fight or cast. Three new script tests
pass; the ledger suite is **21**. All cast rows, classifications and sequences for these logs are otherwise identical.

### Profane Invocation assessment

**62 issues / 61 acted successes**, classified as **15 first placements, 27 settled moves, 16 holder-unseen, three
settling overrides and one unapplied cast**. Eleven low-holder-loss and five sibling flags warrant context, not an
automatic spam verdict. There is no same-stat flag. The one cross-tier alternative found is **not equivalent**:
tier-2 Dexterity straight onto Drow Magus would remove its tier-2 Intelligence (rows **2661/2811**).

There are short chains and returns. Three moves cross the existing settling override on logged net gains **6.0, 9.3
and 10.2**, rather than bypassing that gate. Several holder-unseen cases follow an ally being down/absent; the ledger's
last HP alone does not prove its state at the later cast or that the buff still exists. Between-fight rests also make
old holder evidence incomplete. All invocation completions record **0 m** movement. Seven RT commands have nonzero
lifetimes (1.2–5.4 s); command lifetime does not establish cast time or wasted work because native start/animation waits
are included.

These logs do **not** justify a per-fight cap, a stronger arbitrary lock or a joint tier assignment implementation.
The performance correction preserves all of that decision-making. Native CMB/hold value and future recovery of an absent
holder are still limits of the broader stat-buff model; no unsupported benefit is added to call a transfer wrong.

## Confirmed outcomes and remaining evidence limits

### Death from Above / grapples / forms

- Wenduag's **one issued leap** at log **9250–9253** selected Grabber, moved **8.9 m**, acted successfully in **2.4 s**.
  Combat **43406/43429/43440** shows the claw hit for **19** and its grapple maneuver, with the native **+2 Death from
  Above** modifier. This is actual execution evidence, beyond an admitted candidate. It does not measure every forecast
  hold round or prove all refusals correct. Hazard/body/range refusals remain bounded at the existing 8/4 work limits.
- Seven Release Grapple issues and kept holds are present; weak holds can release while strong elemental holds remain.
  Their exact alternatives/safety costs still have the previously documented limitations.
- The review2 RT form case is readable: **4656** gives form value **91.1**, then a swift Aspect is selected. At **4807**
  the form still exists at **85.3**; after losing 93 HP while a native toggle waits, the **98.6** heal wins. Later the
  shorter horizon/changed attacks produce **10.2**, then **−4** (**5082/5130**). This case is not evidence that the Aspect
  removed the form option, nor retrospective proof of the older unexplained fusion2g1b case.
- A later **2.0 `Griffon Shape(dispel)`** candidate (**5131**) is a generic removal fallback, not a positive form
  appraisal. The existing predicate requires a harmful buff this action can remove; the log does not identify the
  particular buff. Do not suppress the fallback or assert it beneficial from this label alone.

### Burst/fusion safety

Six watches, **two explicit walk-in cancellations**: Tripper at **4084**, Bandit Alchemist at **5414**. The summaries
report **16 re-checks**, **52 native-path / seven straight-route** reads, at most **one** watch. Native path reading and
the existing cancellation route were exercised. Watch saturation/TB metadata eviction was **not** exercised.

Ordinary and mixed fusion spells, including Incendiary Runes/Frigid Touch and Sleep combinations, acted. Native
Frigid-touch minimum/longer-outcome and shared-save distinctions stay in the reader; a successful cast does not establish
every component's accuracy. The logs do not isolate a damage-immune, control-vulnerable walk-in ally, changing immunity,
or an ally created after selection. Sleep's component ally protection remains separate from mixed-fusion damage.

### Disruption, Coup, summons and failures

Thirteen cast-threat witnesses and eight decision/command rows containing a break tag occur. Aeiluvetar chooses a
Vermlek break attack at **5581–5582**, and it acts (**5662**). There is no `more, after K allied hits` tag in this session,
so this is **not** a complete marginal ordered-hit/sequence/readiness replay. TB supplies no disruption bonus.

Four actual Coup abilities act; two RT choices first issue approach moves. Incubus's **5117** and Cambion's **5634** are
movement commands, not remote executions. Bandit's actual **5642–5751** command includes **5.6 m** approach. No new
weapon/range mismatch is established, although logs without positions cannot prove every native reach boundary.
The broad summon competence inventory and the prior isolated safety/timing limits remain, not a claim of universal kits.

Three acted ability failures (Command—Approach and two Cure Light Wounds) have no matching AI exception; short/native
failures alone do not prove poor forecasting. Seelah's native 10% arcane-failure diagnostic is priced already, but this
log does not independently establish the cause of each failure. No blind retry or blanket spell exclusion is added.

## Validation and replay

New checked source **`d669ec1`**, MVID **`e1aa77fe-fef8-4496-aae9-9c751d2974ec`**:

- Real-reference Release **0 warnings/errors**, SDK **8.0.426 / Roslyn 4.11**; full compiler/reference/Source Link hashes.
- **1,044 unit**, **27 inventory / 21 ledger / three supplement scripts**, native **98 ToggleCosts with Psionics /
  14 TomeBurn / 10 ToyBoxSpend**, supplementary **Magus 44** pass.
- ProfileAudit strict **44,640 / 4,808** and full-stock **45,248 / 4,849** (graphs/abilities), zero reader errors or
  prerequisite skips. **BUFF-CONTEXT 39**; inherited **A61/B48/C35/D34** unchanged. The only added report block is
  BUFF-CONTEXT; every previous report row, both full Fusion matrices (**8,709/15,494**) and summon-kit detail are
  byte-identical to the frozen played review2 build on identical inputs. **0 added/removed native signatures**.
- The unmodified raw Achaekek enum mismatch remains an explicit negative input test (exit 1); the compatible scope
  excludes it. No raw-export pass or exact historical corpus is invented.
- Offline native/scalar/layout checks do not execute live actors, casts, RNG, navigation, Harmony combat or Unity timing.
  The new package is **unplayed**, with no measured speed-up claim. No game/mod dependencies or shims ship in the ZIP.

**Next play:** install **`AI-Control-FACC-v0.3.60-dev-review2fix1-d669ec1-UMM-test.zip`**, UMM **0.3.60**, startup **module `e1aa77fe`**.
Use one ordinary TB and one RT mixed encounter with Lann's invocations and the same broad party. Keep the first
placement and later transfer decisions; compare the existing context-count/heap/slow rows without treating heap deltas
as allocations. Numeric buff valuations should follow the same rules, with fewer preview contexts for the eligible
parts. Also retain a timed stat spell and Haste/equipment buff regression: those retain contexts.

Replay the burst cancellations, leap/Grabber/release, and a short-cast caster if convenient. **Death explosions remain
unpriced in this package**; do not expect review2fix1 to solve that proposal. Send **Log.txt + combatLog.txt**, the startup
module, and spawn/rest/manual-order context. The earlier [review](REVIEW_86_89.md) still owns its R1–R7 proposals and
targeted unexercised scenarios. No merge is implied by this review or by offline green checks.
