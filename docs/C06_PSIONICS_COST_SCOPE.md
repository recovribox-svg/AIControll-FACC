# C06 playtest: confined Psionics resource-cost changes

Task branch `codex/c06-psionics-cost-scope`, starting at played C06 `ab810b3` (PR #22; development `1f58150` / runtime `3443d94`). Depends on PR #22; manifest stays 0.3.30.

Evidence: all captured paid performance quotes use the original generic fallback. Installed Psionics globally patches `CalcResourceCost` but writes a result only for GUIDs in its live Mind Blade cost dictionary. The runtime reader recognizes the inspected module/member and checks that dictionary before accepting an otherwise native price. It never calls the patch or mod setup. Mapped abilities and unknown providers remain fallback.

Eight added native fixtures bring the existing 90 checks to 98: actual provider metadata binding, unmapped native cost, mapped positive and zero costs, map changes within one snapshot, old-context reuse, wrong-callback rejection and restoration. These are metadata/data-holder checks, not live Harmony patch application.

Replay in both modes with the same installed mods, capture the detailed cost/fallback labels, native resource quantities, starts/stops and manual/restoration transitions. A newly named unknown patch needs its actual module before another contract can be recognized. Scope/ownership/acceptance/blocklist/native command paths remain unchanged.

The strict audit receipt names its exact native-compatible input scope; the original expanded corpus's separately fingerprinted enum spelling error remains visible. No source input is rewritten. Gameplay and matched Unity latency/allocation measurements remain pending.
