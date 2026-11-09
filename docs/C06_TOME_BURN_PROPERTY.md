# C06 playtest: inspected Tome nonlethal burn property

Task branch `codex/c06-tome-burn-property`, starting at played C06 `ab810b3` (PR #22; development `1f58150` / runtime `3443d94`). Depends on PR #22; version stays 0.3.30.

The three logs identify the same active composite on Orvath's ordinary nonlethal property. Installed Tome Burn Resistance constructs that two-term TTT Sum before its feat-exposure setting: `floor(characterLevel * acceptedBurn) + floor(-2 * acceptedBurn)` only when the feat is owned; otherwise its second term is zero. The patch is not DarkCodex's separate Psychokineticist property.

The runtime reader checks exact supplied module identities, component/field shapes, absent settings, term scales and actual native feature ownership. It reads metadata and values without calling a mod method/setup. Unknown/missing ownership or uninspected shapes keep the legacy fallback. Native rank/caps, current-modifier anchoring and daily resource restraint remain. No movement, targeting commitment, dispatch or version change.

Validation: 14 compiled supplied-provider cases on the real Dark Elementalist graph, with restored scratch objects and no actor initialization; two pure arithmetic regressions including float rounding. For reproduction, use `tests/TomeBurn.Native` with the same native references, compiled mod, supplied Core/Tome DLL paths and recorded export scope. Its first two arguments are Core/Tome, followed by ordinary ProfileAudit roots/output. Keep only its three startup files in a prepared scratch harness; never deploy rewritten dependencies or provider files.

Replay the shape recognition, owned/unowned feature state, native nonlethal delta and accepted-burn transitions in both modes. Full actor/mod initialization, successful game outcomes and matched Unity performance remain pending. Exact committed-source check receipts accompany the completed branch.
