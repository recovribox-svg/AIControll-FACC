# Proposal: anticipate an enemy's death effects without stopping combat progress

17 November 2026, Codex. **Proposed, not implemented or approved as movement policy.**
Evidence: [review2 playtest](REVIEW2_PLAYTEST.md), [input hashes](competence/REVIEW2_PLAYTEST_INPUTS.json).
The supplied inputs are sufficient for this initial native investigation; no new upload is currently required.

## Why this is worth doing

In the final review2 fight, two Risen Followers dealt **485 of 501** enemy damage to the party **after dying**. Bandit and
Tripper died in the first explosion sequence. Current targeting prices their removal but has no terminal-effect risk
term. Treating those packets as repeated melee/cast danger cannot anticipate the first blast or choose its timing.
This is a substantial demonstrated gap, not a request to make AI Control compatible with another mod.

The difficulty is **moderate for a narrow reader, high for a complete decision policy**. A simple kill penalty can
create a refusal to finish the last enemy. A complete implementation must choose useful preparation or safe finishing
actions and preserve progress, with any new movement response approved first.

## Confirmed native evidence

| Input | Fact |
| --- | --- |
| `ArchpriestBoss_ZombieStandardVar3`, `61cabd2ab6bf4699905407cf9e13a6eb` | Adds the electrical death buff; its additional slashing buff is difficulty-gated in the exported unit. Inspect the live active facts rather than assuming every static grant is active. |
| `ZombieDeathEffectElectroBuff`, `7aa80c279fba44be840b46e06f079e4f` | `DeathActions` invokes `ContextActionCastSpell` for the electrical explosion. |
| `ZombieElectricExplosion`, `6326841848db417d87a4564d06925b4a` | 10d6 electricity and 6d6 unholy, Reflex half, fixed DC 30/CL 20; 10-ft enemy-only around selection. |
| `ZombieDeathEffectSlashingBuff`, `912375890d1543ddbce40176d06bb85d` | Casts `ZombieSlashingExplosion`, `f6b63adab8b645c8beb9cab170dac9d3`: 10d6 physical slashing, Reflex half, DC 30/CL 20, 10 ft. |
| `ZombieDeathEffectDispelBuff`, `31b0aa7101cc4009a02f563a2c8c0af2` | Casts `ZombieDispelExplosion`, `bcd9277193ef41dfb8d3b292ee33c828`: one spell-buff dispel by caster-level check, then 6d6 unholy, Reflex half. This must not be silently classified as damage-only. |
| Combat rows 43160–43302 | Var3 instance `37FD8` dies, then rib/electrical/unholy packets deal 339 to four party units. |
| Combat rows 44935–45060 | Var1 instance `37FAB` dies, then Meat Bomb and Unholy Dispel packets deal 146 to the remaining nearby party units. The variants have different payloads. |

These numbers are actual outcomes, not expected rolls. The unit graph's difficulty gates do not independently prove
which facts a ToyBox-created/live modded instance has. The combat log establishes the triggered payloads in this test.

Native `Kingmaker.UnitLogic.Mechanics.Components.DeathActions`:

- Chooses its unit-death or life-state callback through `m_DeathTrigger`; the latter can also accept unconsciousness
  through `m_WithUnconsciousLifeState`. Do not treat every instance as “on reaching zero HP”.
- `HandleEvent` requires the event unit to be the component owner, checks `OnlyOnParty`, checks/spends a resource when
  configured, and honors `OnlyNaturalDeath`/`ForceKill`.
- Runs the fact's `IFactContextOwner.RunActionInContext`. An active buff's context/ownership is relevant, not just the
  feature name on its original unit blueprint.

Native `ContextActionCastSpell.RunAction` constructs ability data from the context caster or target as configured,
handles child/override spellbook/DC/level options, validates its target and triggers `RuleCastSpell`. This is a fact
event, **not a Standard command every round**, and the triggered ability can have its own delivery delay. Forecasting
must never call this method, spend its resource, roll the spell, kill a preview actor or mutate position/state.

The existing native/profile readers already know these inner abilities' damage/save/filter structure. The missing
connection is the event's activation/ownership and its terminal consequences. The review receipt hashes the private
native IL used here; game decompiles/dumps are not committed.

## Working design to assess before implementation

### 1. Read current activation, then classify a deliberately narrow effect graph

- Inspect the current active fact/component set on a relevant living enemy once per tactical evaluation. Cache only
  blueprint/action metadata across decisions; fact activation, suppression, remaining resource and source context are
  live inputs. Release actor/context references with the evaluation.
- Begin with exact native `DeathActions` and direct `ContextActionCastSpell` chains whose owner/caster, event and
  filters can be established. Read both trigger variants explicitly; unsupported subtypes/conditions/overrides retain
  an explicit unknown result. A known zero is not the same as unread.
- Reuse the actual `AbilityProfile`, native parameter/recipient rules and the existing no-roll damage/save/resistance
  estimators. Fixed parameters remain fixed; do not substitute unit level for `ContextSetAbilityParams` or mistake
  multiple packets for independent full save events. Physical reduction, energy immunity/resistance, summons, friendly
  NPCs and changing faction/context caster need their existing native-aware routes.
- Initially report non-damage terminal outcomes such as the dispel as incomplete unless they are also modeled. Avoid
  presenting a damage-only quote as a complete safety certificate. Never infer a radius/trigger by localized names.

### 2. Connect the quote to actual terminal outcomes

`TacticalEngine.DamageValue` already uses `ValueModel.KillChance`/`RiskMath.ChanceAtLeast`. Assess whether that existing
approximation can carry an **incremental** terminal-harm term without falsely replacing natural death by every
knockdown, grapple, Sleep, coup save or scripted kill. Keep actual damage, kill benefit and death harm separate for
debugging and avoid counting one explosion several times for a multi-packet attack.

Evaluate relevant affected allies from the existing battlefield snapshot, using the event's native center, radius,
recipient filters and source caster. Determine future position/latency assumptions explicitly. Do not add a world scan,
path search, rule roll or repeated fact scan for every attack or spell target. Compute a supported victim quote once per
source/event/evaluation, invalidate on a real state/preview change, and reuse it across current candidates.

Already-issued damaging attacks/spells can make the death inevitable before the proposed action. A future model may
need marginal attribution (as F2 does for cast disruption); a command reservation or target commitment change is not
implied. Do not invent a coordinated future plan and claim all companions will follow it.

### 3. Preserve a way to finish the encounter

This is the unresolved design decision, not a detail to fix after shipping a penalty:

- Prefer a genuinely safer useful action when one exists: another target, protection, a heal, or an already permitted
  ranged attack while recipients are outside the blast. Reuse existing candidate/dispatch rules.
- A terminal penalty must not make `Wait` dominate forever when the last hostile must eventually die. Specify the
  policy for unavoidable harm, projected future exposure and the earliest useful preparation. Test it directly.
- New separation/escape planning, anticipated corpse hazard movement, retreat or commitment/cancellation rules require
  the author's approval. A proposal should give exact state transitions, ownership, expiry and worst-case cost before
  changing those systems. Do not silently move all melee allies away, change area ownership or fake an enemy as alive.
- Native creature/summon deaths, expiration/despawn, revival and resource-gated repeat triggers need distinct handling.
  A generic one-time-per-blueprint mark is not a correct lifetime contract.

### 4. Performance and diagnostics

Use an offline/static inventory first to rank the exact supported/unsupported families. Runtime reads should be lazy
for relevant actors and shared within an evaluation, following existing candidate/actor budgets. Report the actual
numbers of fact reads, source/victim quotes and reused quotes. Choose any new bound from an explicit fail/unknown policy
and profiling, not by silently omitting excess dangerous effects or by an unexplained new score constant.

One bounded diagnostic should identify the active trigger/payload and the predicted party harm when it changes a
decision. Unknown effects and overflow are reported once per relevant identity/fight. Do not log the whole fact graph
or every candidate, and do not assert measured Unity performance from a headless timing.

## Acceptance and replay

Before accepting a gameplay implementation:

1. Unit/native-layout tests distinguish death, unconscious life-state, `ForceKill`, resource-gated/party-only cases,
   suppression, multiple active death effects, context caster versus owner, unknown subtype and resource exhaustion.
2. ProfileAudit uses the actual Risen Follower buffs and four inner payload families. Verify native parameter overrides,
   10-ft targeting, Reflex half, physical/energy packets, and explicitly classified dispel support/unknown. Add a fixture
   for an enemy without the difficulty-gated buff; metadata presence alone must not enable it.
3. Pure valuation/progress tests cover nonlethal chip, multiattack/multiple packets, pending lethal hits, a last enemy
   with unavoidable blast harm, a safe finishing attack, two sources, immunity, changing active facts and summons/NPCs.
   Test the outcome policy, not a duplicate of the implementation's formula.
4. All tests/scripts and real-reference zero-warning Release pass; ProfileAudit/Fusion/summon detail differences are
   explained against the current baseline, native harnesses and SigDump run, and receipt/compiler identity/handover are
   updated. No raw inputs or native binaries are committed; keep UMM 0.3.60 unless separately authorized.
5. TB and RT gameplay includes one Risen Follower at range, one with a melee ally inside, two death effects, a damage-
   immune ally, a resource/conditional case and an encounter's last explosive enemy. Confirm it completes without idle
   loops and that ordinary enemies keep their behavior. Identify ToyBox spawns/rests/force kills separately.

Start any implementation on a new task branch after the current stack, with a draft PR into development. Do not merge,
push main/development, tag or publish a release. This proposal does not grant movement authorization.
