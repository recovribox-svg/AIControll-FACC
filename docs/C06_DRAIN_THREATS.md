# C06 playtest: stat/level-drain threat and native death limits

Task branch `codex/c06-drain-threats`, starting at played C06 `ab810b3` (PR #22; development `1f58150` / runtime `3443d94`). Depends on PR #22; version stays 0.3.30.

The supplied logs identify high beyond-HP harm on Shadows/Ghouls, but normalized threat is capped at x2 before target-removal valuation. Native combat records Succubus drain with existing negative-level penalties; the old effect reader ignored that accumulated count when predicting the next drain. Native `UnitLifeController.ForceTickOnUnit` / `UnitPartNegativeLevels.Count` establish the total-count death rule.

The next drain now uses the remaining native limit. Target removal/control also keeps at least its already-read beyond-HP harm, with the existing disabled discount and urgency. Ordinary/incoming-HP threat calibration remains unchanged. No class-name/GUID preference, forced target, new search cap, movement/commitment/retreat/pathfinding/cancellation algorithm or native command change is introduced. Shared consequence accuracy can change existing defensive values.

Two meaningful unit cases add to the 547-test C06 suite: accumulated drain/remaining life limit, and the logged Ghoul non-HP harm through ordinary/disabled/weak/zero harm and damage-progress valuation. Native compilation and SigDump verify the existing-part Count API; no actor is initialized by those offline checks. Exact committed-source build/test/strict compatible-scope receipts accompany the branch.

Replay both combat modes with mixed draining and normal threats, already-drained versus fresh victims, immunity/Death Ward, disabled sources, and difficult-to-hit targets. Check native consequences and competing values. Gameplay and matched Unity timing/allocation acceptance remain pending; the full supplied corpus's one separately fingerprinted enum spelling failure is not hidden by the strict compatible-scope result.

Final verification receipt: [competence/C06_DRAIN_THREAT_CHECKS.json](competence/C06_DRAIN_THREAT_CHECKS.json), source `8cecd92`, MVID `262b5ad5-b372-4e53-a61c-d3ee423f600f`. Unit tests 549/0 failed; Release build 0 warnings; strict explicit compatible scope 44,637 unique loaded / 0 failed and 4,807 ability rows / 0 reader errors; 90 toggle checks / 0 failed. This does not turn the original full-corpus INCOMPLETE result into a pass. Gameplay remains pending.
