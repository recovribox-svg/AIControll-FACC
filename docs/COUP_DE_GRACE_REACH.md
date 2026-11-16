# Coup de Grace: use the delivering weapon

Author-authorized correction on `codex/coup-de-grace-reach`, based on development
`980cf26827e23e5b0c78696fe530edecd7156162`. Applies to AI Control's companions and driven summons in both modes.
Manifest remains 0.3.52. This is a development candidate, awaiting gameplay.

## Evidence and scope

The gaps2 logs contain no issued Coup de Grace. They cannot establish that the reported distant summon attack
occurred in that session. The preceding [research PR #74](https://github.com/recovribox-svg/AIControll-FACC/pull/74)
identified an independent, source-verifiable inconsistency:

- Stock 2.7.0x ability `32280b137ca642c45be17e2d92898758` has Weapon range, a Standard/full-round action,
  native melee-item and helpless/sleeping/unconscious target requirements. Its first effect action is
  `ContextActionMeleeAttack`: explicit target, automatic hit/critical confirmation, extra single attack.
- `AbilityData.GetApproachDistance` derives Weapon range from `UnitEntityData.GetFirstWeapon`. `UnitUseAbility.Init`
  installs that distance as its approach radius.
- `ContextActionMeleeAttack.RunAction` selects `GetThreatHandMelee`. With `SelectNewTarget == false`, its target
  selection returns the explicit target without a distance check. The range provider and attack provider can differ
  when a ranged primary coexists with a usable melee hand or natural limb.
- The supplied TTT Base Coup fix removes the native effect component and installs TTT Core's
  `TabletopTweaks.Core.NewComponents.OwlcatReplacements.CoupDeGraceTTT`, retaining Weapon range. Its delivery uses
  `GetThreatHand`, also without a distance check. This native selector can select a ranged weapon with Snap Shot.
- `UnitAttack.GetApproachRadius(weapon, caster, target)` returns that weapon's actual attack range plus both views'
  corpulence. Native melee selection handles primary/secondary hands, two-handed exclusion and additional limbs;
  native threat eligibility handles Improved Unarmed Strike and Snap Shot. Those rules are called, not reimplemented.
- AI Control's estimate used only the primary weapon, and sleeper-preservation used the primary weapon plus a
  fixed 2 m reach. Neither necessarily matched the delivered attack.

Evidence was inspected from the supplied real `Assembly-CSharp.dll` (SHA-256
`2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`), TTT Core/Base binaries and the separate stock
2.7.0x BubblePrints graph. A stock graph does not prove a particular modded actor's equipped weapon or in-game outcome.
Private DLLs, graphs, decompiles and playtest logs are not committed.

## Implemented contract

`CoupDeGraceDelivery` recognizes the existing Coup GUID and two verified delivery shapes: the stock first-action
melee attack, or the exact TTT Core replacement type and assembly. Multiple effect/delivery providers, changed native
attack flags/range, missing providers and unknown replacements are withheld by AI Control with a bounded explanation.
There is no hard TTT reference or global patch.

For a supported route it calls the actual native hand selector. The selected weapon must be melee. In particular,
TTT plus Snap Shot is refused when TTT would select the ranged weapon: selecting another hand for the estimate would
still let TTT deliver the original ranged weapon. Unarmed/natural attacks are admitted when the native selector admits
them. No manually maintained equipment/feat/size table is involved.

The usable radius is the smaller of the native ability radius and the delivering weapon's native attack radius.
Both already include body sizes; no second addition, fixed 2 m cap, extra tolerance or extension of a stricter ability
radius is introduced. Invalid/nonfinite radii refuse the option. Appraisal uses this radius before its existing
range, LoS, affordability and approach logic. Coup damage/kill appraisal receives that same selected weapon. The
public estimate wrapper now uses the stock native melee selector; the engine passes its verified provider weapon
explicitly. The existing damage/kill valuation policy otherwise stays unchanged.

The shared dispatcher re-reads the current route, hand and native target legality after command initialization,
before existing path preparation. It caps the initialized command radius, preserving a stricter command radius.
Both preflight and final issue use it; appraisal is not an authority to execute later with changed equipment or a
newly ineligible target. This changes only commands AI Control sends through its dispatcher. Native/manual/other
mods' commands are not patched. Full-round cost, native target checks, line of sight, height measurement, native
pathfinding, blocked-path refusal and action economy remain authoritative.

Sleeper preservation also requires a supported delivering hand and uses that hand's native radius plus the existing
walk allowance. A ranged/no-usable-melee ally no longer buys a hypothetical distant melee execution. Existing danger
checks and per-decision target memoization remain.

An accepted command emits `[CoupReach]` once per fight/caster/weapon through the existing bounded lazy reporter:
provider, weapon blueprint, native ability radius and capped command radius. It says **issued**, not delivered;
use the normal `CommandTrace` terminal and combat log to verify the outcome. Preview calculations do not emit an
issue note. The change adds no path search pass, world scan, rule/context forecast or persistent actor/context cache.
A corrected radius can require the existing approach and path preparation where the old code treated the target
as in range.
Route inspection is a small component walk only for Coup; final native targeting is rechecked only for Coup.
The hot identity guard compares native blueprint GUID values directly: it no longer formats a GUID string for
every unrelated ability checked. The audit separately measures its warmed, actor-free allocation count.

## Checks and replay

Automated results and exact package/compiler identities live in
[COUP_REACH_CHECKS.json](competence/COUP_REACH_CHECKS.json) and
[COUP_REACH_BUILD_PROVENANCE.json](competence/COUP_REACH_BUILD_PROVENANCE.json).
`COUP-REACH` checks the actual stock graph, malformed/ambiguous routes, the real optional TTT provider and compiled
native/mod call paths. The unit regressions cover mixed-weapon radii, reach/size inputs, stricter native/command
limits, invalid distances, repeated preparation and provider identity. Offline metadata/IL checks do not execute
live actor equipment, movement, animation or a native attack.

For a repeatable audit, supply the usual compatible folders and missing-only stock fixtures, plus the stock Coup
graph if absent from those exports. Extract that GUID using `tools/BubblePrintsData/Reader`, wrap the raw object as
`{"AssetId":"32280b137ca642c45be17e2d92898758","Data":...}` and normalize only its top-level `PrototypeLink` to the
loader's string form, as the existing stock fixtures do. Keep it private. Pass `--optional-provider <TTT-Core.dll>`
to exercise the real replacement too; without it the stock/compiled checks run and the report explicitly identifies
the provider check as unavailable. Do not treat that as verification of installed TTT code.

Play the new coup1 package, record its startup MVID, and retain paired AI/combat logs. In **both TB and RT**:

1. Put a helpless/sleeping enemy beyond the delivering melee weapon's reach. Use an AI-driven companion and an
   ordinary AI-driven summon where possible. With a ranged primary plus a usable melee limb/offhand, require a
   native legal approach or another action; there must be no distant melee execution. In TB respect the existing
   full-round restrictions; this change does not grant an extra move-and-full-round action.
2. Repeat within actual melee reach. Confirm an issued Coup has a terminal/action and combat damage from the
   logged delivering weapon. Sleeping-target, weapon and full-round requirements must still hold.
3. Repeat with a reach weapon, a larger body, and a natural/unarmed attacker with native eligibility. Their legal
   native reach must remain usable. A pure ranged/no-selected-melee attacker must not receive an AI Coup.
4. With TTT's fix enabled, repeat mixed equipment; if Snap Shot makes the actual selected hand ranged, expect
   withholding rather than a fabricated melee substitution. With the fix disabled, verify the stock route instead.
5. Wake/remove helplessness or change weapon before a fresh dispatch. The final target/hand check must reject
   ineligible dispatches. Compare ordinary attacks/spells, manual Coup orders, a wall/ledge, blocked approaches
   and allied sleep preservation. Existing native LoS/height/path/action rules must still apply.

## Limits

This repairs a verified consistency defect, not a proven explanation of the unrecorded reported episode. A weapon,
form or provider could still be changed after acceptance and before the native effect executes; the game's effect
reselects its hand at delivery. No intrusive delivery-time Harmony patch, equipment lock or interruption policy is
added. Such a separate problem needs a played command trace. Unsupported replacements are conservatively withheld
only by AI Control; support for another delivery provider needs its own evidence. Critical/death immunity and damage
forecast completeness, general summon-kit competence and new tactical/movement policies are outside this correction.
