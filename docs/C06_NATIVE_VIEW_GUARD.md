# C06 playtest: native view query guard

Starting development: `1f58150` (runtime `3443d94`); task branch `codex/c06-native-query-guards` starts from played C06 / PR #22 `ab810b3`. Depends on PR #22. Manifest remains 0.3.30.

Evidence: `Log 3 RT+ TB.txt` lines 91–110, provided 9 November 2026. Native `UnitAttack.GetApproachRadius` dereferences both views; target corpulence at IL `002a` matches the error after reinforcement spawning. The engine catches the exception at the candidate-stage boundary, which discards remaining candidates in that stage.

`CombatMath.ReadRoutine` now declines an incomplete actor pair before querying native reach. It continues using the game's range, body sizes and attack rules once both views exist. Six compiled native fixture cases exercise the guard without actor setup or commands. The old reader fails all six cases with NullReferenceException; the corrected reader passes all six. The scratch-only harness rewrites the Unity frame-count bridge to zero so .NET can JIT the method; no clock or game dependency is shipped. No movement, target commitment, retreat, pathfinding or cancellation policy is changed.

The test receipt records the committed source, real-reference build, all unit tests and strict compatible-data ProfileAudit scope. The original expanded corpus remains separately reported: one exported category is `SawtoothSaber`, while the supplied native enum is `SawtoothSabre`. Native enum deserialization tries to log that parse error, and offline Unity logger initialization fails. No input is rewritten or silently accepted; that input's exact fingerprint remains excluded only from the explicitly named compatible scope.

Replay both TB and RT with newly spawned and already visible enemies. Expect valid attacks to remain candidates while incomplete targets wait for their native view. Source-level guard/fixtures do not establish successful gameplay or measured Unity performance.
