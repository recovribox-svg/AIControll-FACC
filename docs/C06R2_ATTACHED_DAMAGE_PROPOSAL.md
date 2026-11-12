# Proposed follow-up: damage already attached to a recipient

**Approved by the author on 10 November 2026; implemented separately in C06R4.** Read [the implementation, evidence, limits and replay](C06_ATTACHED_DAMAGE.md). The original proposal below was not part of C06R3. Existing movement/retreat/commitment/path/cancellation policies remain in place.

The native TickSwarmClingBuff has target-owned NewRound actions: a Reflex save removes it; failure deals 4d6 physical damage, checks secondary effects and deals 1d4 Constitution damage. Fire, cold, electricity or acid damage can remove it via its native AddEnergyDamageTrigger/ContextActionRemoveSelf components. The native combat log explicitly attributes three damage packets to Cling. These actions do not consult the swarm's current position; separation alone does not remove them. Existing cast-space clearance prices avoidance of future casts, while existing attached harm needs its own position-independent stream. The logged short exits lack accompanying enemy walk/position data, so pursuit versus a changing collateral anchor is not attributed.

Concrete scope for a separate change:

1. Read supported active recipient-owned periodic effects from their actual buff context, remaining duration, saves, immunities and constant/rank damage. Cache per recipient/decision. Reject unread spatial/conditional contracts instead of inferring from a buff name.
2. Keep this background harm at every hypothetical spot, including after the original caster dies when the native buff persists. Price avoidable future cast harm separately and avoid double counting the same damage in observed offence calibration. Verify healing/cleansing and movement risk use the same native consequence.
3. Read the native removal triggers and their actual prerequisites before proposing a cleanse or deliberate damaging action. Do not equate arbitrary energy damage with removal without checking the exact trigger and source.
4. Preserve existing candidate competition, movement permission, paths, commitment, retreat and cancellation. A change to exit distance/timing/pursuit policy needs its own concrete evidence and approval; increasing every exit is not established as a fix by these logs.

Acceptance: TB/RT inside/outside the cast space, Cling active/removed, successful/failed save, native energy trigger with/without resistance, caster alive/dead, overlapping independent damage sources, healing/cleansing alternatives and no-legal-move cases. Compare native damage reasons and actual completed command endpoints. This proposal is not included as an implemented fix in C06R3.
