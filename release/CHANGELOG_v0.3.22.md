# AI Control - Fully Autonomous Combat Companions v0.3.22

## Highlights

- **Automatic kineticist substance infusions.** Supported learned infusions now
  compete with ordinary actions according to the actual blast, target, effect
  and native burn cost. The AI can activate, replace or stop a useful infusion
  and restore the original state after combat. Player ownership and the
  **AI manages toggles** setting remain respected.
- **More accurate spell choices.** Sleep forecasts its shared Hit Dice pool
  across recipients; cones use the game's target geometry; energy protection
  considers matching damage types. Spell boosts are paired with reachable
  follow-up actions.
- **Safer allied control areas.** With Avoid friendly fire enabled, a new lasting
  control circle can be withheld when an ally's accepted melee approach clearly
  ends inside it. Actual Selective protection is respected, including the game's
  rules as changed by other mods.
- **Outcome and touch-cast fixes.** Reaching a touch spell's casting range no
  longer blocks the subsequent cast when the separate approach ends. A visible
  failed concentration check no longer teaches a false silent/no-reach failure.
- **Performance improvements and clearer diagnostics.** Repeated local forecast
  reads are reused, and slow decisions show stage timings. The author reports
  8/10 smoothness and improvement in the latest playtest; this is not a controlled
  hardware benchmark. Bounded explanations help investigate Sleep aims and
  energy-damage estimates. Combat and animation speed are unchanged.

## Compatibility and installation

Fully exit the game, then install `CompanionAI_Pathfinder_v0.3.22.zip` through
Unity Mod Manager. UMM displays **0.3.22**. The mod ID and settings location remain
`CompanionAI_Pathfinder`, so existing settings are retained.

## Known limitations

- Initial Fire Blast/Burning estimates can remain optimistic against energy
  resistance. Additional diagnostics are included; a complete correction is not
  claimed. Critical blasts can sometimes penetrate that resistance.
- Unraveling and Chain Arrows are withheld from automatic infusion selection.
  Deep Slumber retains its spreading-delivery restriction. This release does not
  add a new Gather Power, metakinesis or form-configuration optimizer.
- The new control-area guard covers clear whole-arrival containment, not every
  route, slope or partial overlap. It adds no new pathfinding, target lock or
  emergency-cancellation policy.
- Native/third-party handlers and every supported infusion family are not all
  gameplay-verified. Latest concentration handling and diagnostic changes have
  automated/static verification and still need live regression feedback.

See [the development handover](../docs/RELEASE_0_3_22_HANDOVER.md) for source,
package identity, checks and remaining investigation leads. Earlier withdrawn
v0.3.22 experiments are not part of this release; this builds on the restored
v0.3.21 base and the subsequent scoped work.
