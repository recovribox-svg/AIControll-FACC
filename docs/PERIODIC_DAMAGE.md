# Damage over time on ordinary spells

20 November 2026, Claude. This is the cross-cutting slice of the [fusion coverage programme](FUSION_COVERAGE_PROGRAMME.md).
It uses C1's periodic carrier pricing ([FUSION_MAIN_TARGET_DOT.md](FUSION_MAIN_TARGET_DOT.md)) for spells that are cast on
their own, not fused. Both modes, valuation only. Not played.

## The problem

A buff whose whole effect is hit-point damage each round is a periodic damage carrier. Acid Arrow's acid, Molten Orb's
burning and Alchemist's Fire are examples. On an ordinary spell such a buff was not priced as damage at all. Instead, the
control section guessed it was a debuff worth 0.25 of the target's worth, and the audit reported "guessed debuff".

## The change

- **Which carriers.** `BuffApplication.PeriodicDamageOnly` is now set for every profile, fused or not.
  `BuffApplication.UnderSave` records whether the application sits in a branch of a saving throw.
- **Pricing.** `TacticalEngine.TryPeriodicDamage` prices an ordinary spell's carriers that apply whatever the save. It
  uses the same tick rule as C1:
  - the native schedule: the first tick comes one interval after the buff lands;
  - the recipient's resistance and immunity;
  - a save in a tick ends the later ticks with its chance;
  - no credit for a carrier the target already has.
- **Value.** The ticks count, times the cast's chance to land, for HP and progress towards a kill, capped at the HP left.
  The chance to kill now stays with the immediate damage (`DamageValue(..., laterDamage)`). Without ticks the value is
  unchanged.
- **No debuff guess.** A cast whose every target buff is a priced carrier skips the control section (`CarriersOnly`).
- **Allies.** An ally the cast catches pays its expected ticks in the friendly-fire cost, and in the burst re-check.
- **Carriers behind a save** keep the older rule. Example: Incendiary Runes cast on its own burns only on a failed save.
  The save path is read only for fused casts.

## Abilities affected (author's exports)

- **Priced now (6):** Acid Arrow, Molten Orb, Alchemist's Fire, Bleeding Touch (Death domain, and its Separatist copy),
  and Vrock Spores. Vrock Spores is the Demonology Vrock summon's ability.
- **Carrier behind a save, older rule kept (5):** Incendiary Runes, Arcanist Burning Flame exploit, Final Wyrmshifter
  green breath, Deskari breath and Nature's Agony.

## Checks

ProfileAudit `PERIODIC` has 8 checks:
- the actual Acid Arrow and Molten Orb carriers are priced;
- Incendiary Runes cast on its own keeps the older rule;
- the routes are checked;
- the full list of affected abilities is printed.

The audit's own classification of the six priced abilities changes from "guessed debuff" to "damage over time". Every
other line is unchanged. See [COVERAGE2_CHECKS.json](competence/COVERAGE2_CHECKS.json).

## Limits

- A carrier behind a save on an ordinary spell is not priced. Incendiary Runes' burning on its own is an example.
- A carrier the target already has gets no refresh credit.
- Carriers whose dice come from ranks stay on the older rule.
