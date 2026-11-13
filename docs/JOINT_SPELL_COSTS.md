# Spell boosts: check the activation costs of the proposed sequence

The author approved this follow-up to Focused Shot. Refreshed development is `44b224f`; this branch starts at
PR #50 `4745033`, including #47/#48 and Focused Shot. Claude's concurrent summon-control PR #49 is separate.
Review the prerequisites first. No movement, target commitment, retreat, pathfinder or stuck policy changes.

## Confirmed gap and native rules

`CoupleSpellBoosts` previously chose an individually usable main spell and second standard action, then credited
Quicken with that second action's value. It did not establish that setup + main + second could all be paid.
Two individually available spells can require the last slot in the same spontaneous book/level. Two conversions
can spend the same prepared source slot. A setup and its main cast can share the same activation resource.
These are code/native-rule counterexamples, not a claimed observed failure in the supplied playtest logs.

Native `AbilityData.Spend` consumes material/item, spellbook, then `AbilityCastRateUtils.SpendResources` costs.
`SpellSlotCost` is one plus recursive conversion/extra costs. `SpendOneSpellCharge` follows `ConvertedFrom` to its
actual source. `Spellbook.SpendInternal` uses the effective source level; level zero costs no slot. A spontaneous
book decrements that level's pool; a prepared book selects an available matching `SpellShell` in reverse slot
order, using `AbilityData.Equals(object)` (not spell names). Repeated cost entries need distinct slots.

Resource spending pays an ability fact's per-day resource first, then its resource logic (or override/source).
The inspected base `AbilityResourceLogic` checks its RequiredResource and can pay an OverrideRequiredResource;
its native ResourceCost includes current increasing/decreasing facts. No literal one-point or class-name price
is substituted. Native custom/derived Spend implementations are not established by that interface alone.
[Assembly/fixture identities and contracts](competence/JOINT_SPELL_COSTS_NATIVE.json).

## Implementation

A decision-local reader snapshots only the proposed abilities' **known activation** costs. It memoizes by
AbilityData reference identity, not the game's equality operator or an ability name. Prepared eligibility uses
the native equality/order rule; the budget uses actual slot identities so aliases cannot create another slot.
Spellbook identity + effective level separates spontaneous pools. Resources retain their blueprint identities.

The pure `JointCostBudget` walks the proposed payments in native order. A nonconsuming resource requirement is
different from its subsequent debit. It reuses scratch balances/slot sets across alternative checks, resetting
on every attempt; neither descriptors nor native balances are mutated. Null means unknown, empty means known
free. Overflow/contradictory snapshots are withheld. Work is bounded to 64 charge records and 256 prepared slots;
conversion/source walks stop at 16 before calling the recursive native getter. No rule, context or cast preview,
inventory/spellbook mutation, refund, reservation or extra scene/target search is added.

An unaffordable/unknown setup + selected main cannot finance a boost. Ordinary main actions remain eligible.
Only a qualifying free Quicken boost lazily searches the already-scored, eligible standard-action alternatives,
in descending value, for a jointly affordable setup + main + second. It may choose a cheaper alternative instead
of crediting the unaffordable best one or discarding every alternative. Existing Quicken action/movement/native
metamagic gates and all DC/CL/Persistent formulas remain. The second action is prospective value, not a queued
command: native availability and current costs are read again after each actual issued action.

Unknown pair costs name the failed stage/provider in a verbose note, at most once per setup/main blueprint pair
per fight through the existing 256-entry note ledger. No extra log or native cost query is made per target for
an ability whose price was already read in this coupling pass.

## Limits and boundaries

This is not a simulation of every future event or all possible spending. Quotes use current initialized state;
they do not invent future refunds, probabilistic slot saving or unlimited-spending overrides. Current
SaveSpellbookSlot/Spell Fool skip flags are respected; uninspected custom resource calculators/providers,
items/material payments, held-charge deliveries and toggle activation have no complete joint quote, so their
coupled benefit is withheld. They remain ordinary independently eligible actions. A weapon follow-up has no
spell/ability activation cost in this budget; ammo, recurring/on-hit costs remain in their existing native/value
paths. Arbitrary prospective buff/fact mutations or mod spend overrides require separate contracts; current
prices are not a guarantee of unchanged future prices. No native safety or final spending check is bypassed.

The plan still starts with the existing selected main; it does not run a new all-pairs spell search or retune
resource prices, action values, progressive policy or fight duration. Finite-state budget tests and native
metadata/getter/compiled-route checks do not initialize a caster or establish gameplay timing.

## Focused replay

In both TB and RT, test a free Quicken setup with two useful spells sharing a spontaneous level: one slot left
must not earn a two-cast benefit; two slots can. With a higher-valued second cast that conflicts, provide a
cheaper cast in another level/book or an in-place weapon attack and check that it remains a possible follow-up.
Repeat with prepared conversion aliases, two separate prepared copies, extra slot costs, same/different books,
setup and cast sharing a daily resource, and plentiful resources. A genuinely useful affordable DC/CL/Persistent
boost must still work. Check no-point/cantrip cases, unsupported provider notes, no repeated setup, ordinary
spells/items/weapon attacks, both spell policies, Pure Mage and blocklists. Rest/spawn interventions must be
recorded separately. Keep the startup MVID in logs. Gameplay and matched Unity performance remain pending.

Manifest stays 0.3.30; Claude reviews/integrates. No main/development push, merge or release.
