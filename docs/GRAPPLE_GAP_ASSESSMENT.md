# Grapple gaps and Frigid Touch selection

15 November 2026, Codex. Start from development `6b9be42`, retaining the #65 -> #66 -> #67 -> #68 stack,
latest dependency `342dc27`. Work branch `codex/grapple-gap-review`. No main/development push, merge or release.
This task assesses the five reported limits; it does not assume that every apparent missing feature is a defect.

## Assessment

| Gap | Decision and integration | Difficulty / remaining evidence |
| --- | --- | --- |
| Frigid Touch stagger | Implement the common buff and fixed minimum duration of a bounded native value selector. Preserve native delivery, save/immunity, marginal-buff, friendly-fire and resource paths. | Small/moderate. Inputs are available. The longer critical outcome remains unpriced. |
| Leap Grabber choice | Implemented separately in [the cached leap-mode follow-up](LEAP_GRAB_MODES.md): share ordinary grapple truncation/hold data, then compare Grabber in the existing bounded mode chooser. | Moderate. Four native quotes/eight landings and geometry are preserved. A leap replay is still needed. |
| Holder's actual best alternative | Defer the model replacement. `DamageValue -> GrappleHold` already participates in candidate pricing; feeding the best candidate straight back creates circular value. A held unit's ordinary candidate list also excludes actions that might become available after release. | High. Needs a bounded, read-only counterfactual permission/alternative view, with no part removal or second full Decide. Keep opening and release valuation consistent. |
| Holder safety | Defer a general penalty. Price the **marginal** cost of staying immobilized, after accounting for the victim's suppressed attacks, native holder penalties, existing exposure/defence costs and free release. | High. Subtracting all incoming damage double-counts costs. Test an actual G2 hold in a hazard and a hold worth keeping; do not introduce a blanket danger veto. |
| Party victim of a monster | Native movement/pin restrictions already enter UnitStatus. Missing targeted help, concentration risk and freeing the victim deserve their own slice. | Moderate/high. Bind the actual target-buff handler; monster variants differ. The supplied log has no AI victim header. Test a spellcaster held and then pinned in both modes. |

The inspected `ShifterGrabTargetBuff.OnEventAboutToTrigger(RuleCastSpell)` calculates the holder's Grapple CMB
and the spell's parameters, then rolls concentration at **10 + CMB + spell level**, setting ForceFail on failure.
It is not a CMD-based check. UnitPartGrappleTarget forbids movement; pinning adds the standard-action restriction.
UnitGrappleController already attempts a victim's break-free each round without an AI action. Do not invent a paid
escape action, clear conditions to preview release, or assume a release restores an action spent earlier that turn.
Any future cast-risk model must integrate healing/buffs and costs too, and account for the actual handler/patches.

## Supplied playtest

The newly supplied files are byte-identical to the fusion2g1b inputs reviewed for #67:

- AI log SHA-256 `fb2033f84c4bc55abeae54fb3e31c3deb42055a2f3d6f9f70578343930d9d363`, 6,029 rows.
- Combat log SHA-256 `4aa2fc9652021ab58ae9ea93e74aca84e2f23c45341cd9b5ba682f05415454b9`.
- Startup module **`e4f231af-1782-4e64-9697-3a3b2455e1a2`**, source `981299e`, before G2 and #68.

Rows 501 and 521-522 expose the Frigid gap: ordinary Frigid Touch is listed as damage; Explosive Runes/Frigid Touch
is read with a zero control envelope. Row 4938 again lists that fusion as damage only. There is no issued Frigid
Touch or leap in this log, and no AI decision header as a grapple victim. Thus it establishes recognition/forecast
omissions, not a numerical counterfactual or gameplay acceptance of the new code. Wenduag's actual holding decision
at row 2425 ends her turn under native restrictions; it does not establish that releasing would have been better.
The earlier maneuver-immunity findings belong to #67; do not attribute this old playtest to the newer package.

## Proposal checked against native evidence

The attached proposal's useful idea is to value a verified common baseline. Its described structure is inaccurate:

- The existing reader already traverses Conditional, saving-throw and direct action-list children.
- Frigid Touch uses **ContextActionSelectByValue**, whose private array elements contain a ContextValue and ActionList.
  The old general walk does not descend into this arbitrary wrapper array.
- Native RunAction selects one greatest calculated value, using the **last** alternative on a tie. It never runs
  both. Frigid Touch's damage writes a critical indicator into DamageBonus; the selector compares that with zero.
- Both alternatives apply the **same Staggered buff**, for one minute or one round, rather than two buff blueprints.
  The root spell descriptor is Cold. A descriptor-only fallback would neither prove the effect nor reliably find it.
- The three exported assets containing the selector are FrigidTouch, SiroccoArea and ElementalAssessor. The latter
  two select different/empty effects based on damage; they are not three versions of this critical stagger.

No fixed 5-15% critical estimate, generic CC injection or execution of the native selector is used.

## Implemented common-buff reader

Qualify an exact native Greatest selector with 2-8 variants, Simple/Shared inputs, and one direct application of
the same harmful buff in **every** alternative. Subject, application flags and Extend behavior must agree. Durations
must be fixed positive Rounds/Minutes with no dice, inheritance or permanence. The profile walks only the actual
shortest-duration application, retaining its enclosing guards. Unknown/different/extra/empty alternatives remain
unread. This is common application metadata, not a promise that a recipient passes immunity or loses an action.

For Frigid Touch it restores the one-round stagger in ordinary, sticky-touch and compatible no-save Fusion profiles.
The usual native attack/delivery, resistance, condition immunity, existing-copy and resource paths remain. Longer
critical control is unpriced. Qualified Fusion damage/control still share their outcomes; no second touch roll is
created for an area-delivered fusion.

Enemy credit for the minimum must not certify ally safety. For a possibly caught ally, retain the longer duration
within the existing horizon and a possible refresh even when the buff is already present. Whole-buff immunity and
proven direct-condition immunity can establish safety; an unfamiliar extra component cannot. The direct-condition
carrier may include the native harmless RemoveWhenCombatEnded marker. Avoid Friendly Fire remains a hard veto.

Exposing this previously hidden side effect also exposes the existing F0 boundary: **168** declared-list saved
configurations involving FrigidTouchCast are now withheld for a buff outside the shared failed-save branch.
The joint model currently represents failed-save control, not arbitrary unconditional control on both save outcomes.
Do not silently enable those profiles with an incorrect shared-save discount. The played no-save Runes combination
remains supported and gains the actual stagger. This is more complete effect reading, not an increase in admission.
General per-outcome unconditional control is a later Fusion slice.

Metadata/reflection is read once per profile/selector. Selection dictionaries/lists are allocated only for profiles
that use a selector. The ordinary safety path exits before iteration and allocates zero bytes in the actor-free warm
microcheck. There is no new actor/context cache, rule/crit roll, action execution, target/world/path search or setting.
One configuration-level diagnostic states the minimum/longer-outcome boundary. Unity timing remains unmeasured.

## Verification and next replay

Checked source **`fad03c4c9a68d5b438b0719827cc6c8ecaab3241`**, MVID **`b0da0523-bbc9-4044-8228-18ed4c118bfa`**:
855 unit, 27 inventory, 3 audit-script and 11 ledger tests pass. Real-reference Release has **0 warnings/errors**;
two added native flag references resolve. ProfileAudit: 44,639 loaded / 4,807 abilities without load/reader failures
or prerequisite skips; **VALUE-SELECTION 24** and inherited checks pass. Supplementary Magus 44 and native providers
98/14/10 pass. The only ability-verdict changes are FrigidTouch and FrigidTouchCast gaining Soft control.

[Receipt](competence/FRIGID_SELECTION_CHECKS.json), [compiler/reference/source provenance](competence/FRIGID_SELECTION_BUILD_PROVENANCE.json),
[input hashes](competence/FRIGID_SELECTION_INPUTS.json), [coverage summary](competence/FRIGID_SELECTION_COVERAGE.json).
The native-UI matrix has 15,494 templates: 7,306 admitted, 8,188 withheld, 1,166 admitted cone components resolved,
3,118 metadata-qualified joint profiles. Against #68, only Frigid configurations change admission/qualification.
This reports metadata, not live casting success. No intermediate ZIP is produced; a cumulative test package follows
with the separate leap-mode repair. Raw logs, game assemblies and graphs stay private. Original raw Achaekek
enum-load failure remains separate. Manifest stays 0.3.52; new behavior is unplayed.

The [leap follow-up](LEAP_GRAB_MODES.md) now supplies the cumulative gaps1 test package (checked source `351b709`,
MVID `9aea0261-f5a3-4c3e-a025-502de27a716d`), including this reader and #65-68. Its independent receipt and replay
are linked there. No newer gameplay is claimed for the supplied older logs.

In TB and RT, include ordinary Frigid Touch, its Spellstrike/held-charge route if available, and Explosive Runes/Frigid
Touch. Check that fresh targets gain a one-round stagger forecast, immunity/already-present copies are handled,
and a damage-immune target can still have a useful non-immune stagger. A real critical hit may last a minute without
the forecast claiming that bonus. Include allies at the area edge, already staggered allies, native condition-immune
allies, and Avoid Friendly Fire on/off. Saved configurations outside the model must give the explicit refusal.

Leave Grabber to the engine for G2 and form replays. Further holder/safety/monster work needs targeted play evidence
from that newer behavior, not another copy of the old log. No additional files are needed for this common-buff slice.
