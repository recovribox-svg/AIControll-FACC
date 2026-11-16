# Death from Above: Grabber-aware cached mode pricing

15 November 2026, Codex. Continue #69 `2e86342`, itself on #68 -> #67 -> #66 -> #65, development `6b9be42`.
Branch `codex/leap-grab-modes`. [Five-gap assessment](GRAPPLE_GAP_ASSESSMENT.md). New behavior is unplayed;
no movement/commitment/retreat/pathfinding/stuck-cancellation policy, main/development push or release change.

## Confirmed code gap

G2 deliberately fixed a leap's Grabber state. There was a further inconsistency: the original native leap quote
already contained G1's expected routine truncation/hold, but `GriffonModeQuote` repriced every cached line without
the grapple and returned none of the hold fields. Ordinary mode bonuses could therefore be compared as if all
attacks remained available, while the candidate's initial value already priced a grab cutting them short.

The older supplied fusion2g1b log contains no issued leap, so this is a source/native-contract finding, not a
reproduced bad leap. Preserve that distinction when reviewing the new package's gameplay.

## Change and bounds

- Extract the existing ordinary grappling line/summary calculation into `PriceGrabLine` / `FinishGrabQuote` without
  changing its arithmetic. Both ordinary and leap forecasts use `ValueModel.GrappleRun`, weapon-trigger filtering,
  expected attacks, initiation probability and the same free-natural-damage summary. No-grab paths bypass the helper.
- After validating the **exact cached routine identity and frame**, reprice that leap with its current assumed grab.
  Reuse `GrappleOpeningOn`'s existing pair/frame/trigger cache. A newly considered grab can need the same CMB/CMD
  calculations as an ordinary Grabber alternative, but never another native own-weapon quote or landing query.
- Let Grabber participate in the existing bounded mode chooser rather than fixing it on leaps. Preserve ownership,
  blocklists, availability, once-per-round switches, hysteresis, groups and the **32-set** limit. A group omitted at
  that limit keeps its current state, as before; this does not promise exhaustive mode optimization.
- Keep the charge-adjusted landing defence baseline, captured concealment, native critical/DR/damage data and existing
  offense factor. No synthetic charge proc, touch payload or extra attack is introduced.
- Preserve **four native own-weapon quotes / eight landing attempts**, native landing/range/LoS/body/swarm safety,
  target commitment and final validation/dispatcher. A stale cached quote is unavailable, never permission to repeat
  the native query outside budget. Mode application still follows accepted dispatch as on ordinary attacks.

The unchanged G1 model still estimates the holder's future loss with generic worth and does not price the full
immobility opportunity/safety cost. Its hold model and future-mode assumptions are not a universal simulation of
all class/mod effects. Those limits also apply to the leap; exposing its mode choice does not resolve them.
Weapon/maneuver queries retain current live-state inputs; landing-dependent procs or positional changes are not
simulated by moving the actor or rewriting a context.

## Validation

Actor-free cached-routine fixtures cover original damage/DR/crit/defence cases and the added grapple outcomes:

- Three 50%-hit lines (claw, claw, other), certain maneuver: **8.75** damage / **1.75** expected attacks / **75%** grab,
  versus **15** damage and no hold with Grabber off.
- A four-point accuracy increase changes both damage and early stop: **9.73** damage / **1.39** expected attacks /
  **91%** grab, instead of crediting all attacks at the increased hit chance.
- Zero maneuver success restores the full routine. Captured concealment feeds both damage and the start chance;
  the hold/free-damage metadata remains available to shared `DamageValue`.

Items are uninitialized, blueprint-backed fixture objects with no item constructor, actor, inventory, attack,
transform, mode switch, path or flight. Compiled checks connect both callers to the same kernel, validate the cache
before reading a grab, retain ownership/defence/dispatch guards, and ensure the fixed-mode predicate no longer excludes
Grabber specially on leaps. Native source inspection, compilation and fixtures are separate from live execution.

Checked source **`351b709179ab3f9caba3083c07b1337e334760d2`** passes **855 unit tests**, 27 inventory, 3 audit-script
and 11 ledger tests. Real-reference Release: **0 warnings/errors**; SigDump: **0 new external references** relative
to #69. Compatible ProfileAudit: 44,639 blueprints / 4,807 abilities without load/reader failure or prerequisite skip;
**GRIFFON-MODES 29** (9 new), VALUE-SELECTION 24 and inherited checks pass. Supplementary Magus 44 and providers
98/14/10 pass. Other audit rows and the ability report match #69, apart from the changed mode-check count and
optional coverage/output-path/separator rows. Original raw Achaekek enum-load failure remains separate.

[Receipt](competence/LEAP_GRAB_CHECKS.json), [compiler/reference/source provenance](competence/LEAP_GRAB_BUILD_PROVENANCE.json).
Manifest stays 0.3.52. The Frigid template
coverage is reused from #69 because the reader/joint-outcome model is unchanged; it is not a new gameplay claim.

[Cumulative gaps1 test ZIP](../test-builds/AI-Control-FACC-v0.3.52-dev-gaps1-351b709-UMM-test.zip),
[identity](../test-builds/AI-Control-FACC-v0.3.52-dev-gaps1-351b709-UMM-test.identity.json),
[checksum](../test-builds/AI-Control-FACC-v0.3.52-dev-gaps1-351b709-UMM-test.sha256).
Startup MVID **`9aea0261-f5a3-4c3e-a025-502de27a716d`**. It includes #65-69 and the leap repair, built with
SDK 8.0.426 / Roslyn 4.11 against UMM 0.33.2.0. The exact checked DLL is preserved across later receipt/docs commits.
It is unplayed and is not a stable release; all earlier packages and receipts remain intact.

## Replay

Play both turn-based and real time, with an actual major Griffon form and **Grabber left to the engine**:

1. Offer a legal Death from Above against a dangerous caster and against a weak summon. A profitable toggle change
   should appear in its mode label; the chosen leap should compete using the expected truncated routine and hold.
   Do not require a particular switch when the current state already wins or the native landing is refused.
2. Repeat with Pounce/full attack and with a restricted single attack. Compare actual attacks before the grab and
   the native Interrupt/end after it; hold value is an estimate, not a guarantee that the maneuver succeeds.
3. Swarm/Freedom of Movement target: no hold reward or routine truncation; an unnecessary toggle flip is not required.
4. Manual toggle ownership, blocklisted Grabber, automatic toggles off and a previously switched toggle this round:
   no unauthorized override. After combat, retain the existing toggle-restoration behavior.
5. Other attack/AC modes, crowded/unsafe landings, moving targets, mode transitions: native safety and final validation
   remain. Retain the form/Fury, G2 release and Frigid/fusion2 checks from the preceding packages.

Supply paired AI/combat logs and startup MVID, noting manual orders, ToyBox spawning and rests. No additional DLL or
blueprint export is needed for this slice. Separate monster-victim/holder-safety cases remain useful for later work.
