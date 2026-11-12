# C07: native melee Magus actions

The caster1 captures classify Spell Combat and Spellstrike as unsupported and leave them off (TB1 190–191;
TB2 204/206), confirming the author's automation report. The [broader review](CASTER1_PLAYTEST_REVIEW.md)
covers all 13 fights, 1,043 decision summaries and other decisions/performance; its three independent repairs
are in prerequisite PR #34. This branch starts at its packaging commit `02d2c2a`, over development `1f58150`.
Development was fetched again before completion and remains `1f58150`; PR #33 and the C06 chain are inherited.

## Native rules and observed limits

Melee Spellstrike delivers a **held melee touch** through a successful eligible melee weapon hit. Scorching Ray
and Snowball in these captures do not leave that charge. Spell Combat can pair a native eligible spell with
weapon attacks and is a separate mechanic. Eldritch Archer has its own ranged-spell contract, outside this change's
automatic planning. These logs contain no selected melee touch establishing a successful Spellstrike combination.

The supplied game's `UnitPartMagus`, `MagusController`, `UnitUseAbility.CreateCastCommand`, `AbilityEffectStickyTouch`,
`AbilityDeliverTouch` and attack-count rules establish the executable contracts. [Native identities](competence/C07_MAGUS_NATIVE.json)
distinguish these from the supplementary stock hidden buffs read with the uploaded BubblePrints engine and its
local 2.7.0x database. That database is not installed mod state. Runtime planning reads actual
`BlueprintRoot.SystemMechanics` references and native predicates, including installed patches.

## Resulting behaviour

* Free, non-targeted modes with exactly the native `SetMagusFeatureActive` component compete as preparations
  for useful in-place actions. Identity uses the native component/enum, without localized names or class guesses.
  Existing player ownership, global/character lists, automatic-toggle settings, once-per-round switching and
  combat-end restoration apply. Paid/structurally different modes remain manual. A switch must improve the
  best current main action by more than 5%, with a minimum 0.5-value gain.
* Cast forecasts include native equipment/action requirements, typed Spell Combat penalties and the applicable
  Spellstrike extra attack. A new Standard cast opens its opportunity and needs no previous cast's RT window.
  The actual command stays Standard; the forecast reserves the combined full budget. The native controller
  owns actual buffs, attacks and cooldown spending.
* A held touch contributes once across the eligible routine, using the first successful hit and that hit's
  confirmed critical. Its native Magus spell critical is x2, rather than a weapon's x3/x4. Spell and weapon share
  overkill/kill valuation. Casting over a charge loses its marginal payload value; its already-paid spell slot
  is not discounted again.
* Enemy held-touch delivery uses its priced weapon candidate. Ability issue uses the game's command factory;
  an unexpected weapon command is refused for repricing. Helpful touches retain ally delivery. Enemy weapon
  candidates that would deliver a blocked/helpful charge are removed before attack-mode repricing and checked
  again before issue. Missed weapon attempts do not become silent source-cast failures or spend a second slot.
* Native `PreparedSpellCombat` / `PreparedSpellStrike` permit valid in-place follow-ups after the Standard is
  spent. Commands carry the finite native deadline and explicit full/single flags. Ordinary attack candidates
  cannot turn a Spellstrike-only free single into a free full routine, including with extra actions. Native
  availability, range and held-charge identity are rechecked before issue.

Normal Ranged martials retain attacks. Pure Mage excludes physical Magus combinations, including sticky sources
that would autoqueue a weapon hit. Reach plus melee Spellstrike adds a second delivery roll and requires a separately
priced native switch-off before automatic casting. Fired rays retain native behaviour. No localization or shared
blueprint is rewritten; the blank tooltip's origin remains unresolved.

## Bounded work and compatibility

Only melee Magi with native unlocked mechanics enter. Planning reprices at most four useful in-place casts per
state, reserving one place for the best touch, and at most two future one-toggle states. Duplicate mod toggles
cannot expand this budget. Native range/sight rejects distant pairs before attack calculations. Existing same-frame
routines are reused within each state, held payloads stay decision-local, and first-hit aggregation is a scalar struct.

Previews change only the inspected feature booleans/current grip field and restore them in `finally`. Attack,
parameter, context, condition, resistance and buff-appraisal caches are invalidated at both boundaries. No preview
runs a toggle, applies a buff, initializes an actor, queues a command or raises equipment events. Actual switching
uses the existing executor and native events. Hidden-buff metadata has depth/node/cycle bounds, checks replacement
stacking, native simple typed penalties and mutually exclusive fact branches, and withholds extra unknown effects.
Projected attacks use native `AddExtraAttacks` on a read-only calculation result and native attack/limb enumeration.

Area damage, non-touch hard-control pairings, Fusion/EA combinations and unmodelled caster-side held payloads are
outside the combined forecast; existing permitted ability/native behaviour remains. The existing rule against
pointless buffs with only helpless enemies left applies before pairing. Four casts can miss a lower standalone-value
combination; this deliberately bounds work. The aggregate Magus slow-decision entry includes heap/context work.

This affects the shared tactical engine in TB and RT used by these captures. Legacy RT is not expanded/rerouted.
General movement, commitment, retreat, pathfinding, hazard membership and stuck cancellation are unchanged.
The broader review's movement findings remain proposals requiring explicit approval. Offline fixtures do not
establish a Unity performance improvement or native scene execution.

## Verification and gameplay handover

The final receipt/test identity records unit/native suites, actual-reference Release, strict compatible ProfileAudit,
the separate original-corpus failure and added signatures. Private runnable copies neutralize engine-bound dependencies;
none is deployed. No fixture initializes an actor or executes combat commands/mod initializers.

Replay the identified **magus1** UMM test in TB and shared-engine RT; retain its startup MVID with logs:

1. Suitable one-handed melee weapon/free hand, Shocking Grasp, both modes off, useful adjacent enemy and full
   budget: compare preparation/cast/attack with an ordinary full attack. Look for `[Magus]` notes and
   `[native Spell Combat]` / `[native Spellstrike]` choices.
2. Spell Combat off/Spellstrike on: the native free follow-up must be a **single** weapon attack. Both on/full
   eligibility: verify penalty/extra attack in combat. Expired windows must not grant attacks. Test after movement,
   staggered and with shields/two-handed equipment, including native grip changes.
3. Force a miss, then attack on a later turn: charge retained after miss, discharged once on a successful hit,
   no repeated source-slot spend or payload on every iterative. Check x3/x4 weapon versus x2 spell critical,
   resistance/SR and actual damage.
4. Block the held ability: retain it for the player. Hold a helpful touch beside an enemy: no weapon delivery to
   that enemy, ally delivery still available. Test both global and character lists.
5. Click each mode during combat with Respect Player Toggles on, disable automatic switching, and end combat.
   Manual choices persist and AI-switched modes restore. Pure Mage must forbid physical combinations; ordinary
   Ranged archers retain attacks. Fired Scorching Ray must not become a held melee charge. Reach requires the
   supported off-state. Fusion/EA are outside automatic-combination acceptance here.
6. Replay prerequisite projectile counts, critical ally heal, Orvath burn, bombs, reveal and swarm scenarios.
   Compare matched fights/parties/spells for latency; net heap is not exact allocation. Report the blank tooltip
   with its exact ability/UI context separately from combat delivery.

Gameplay and matched Unity timing remain pending. Manifest 0.3.30; Claude reviews/integrates; no development/main
push, merge or release is performed.
