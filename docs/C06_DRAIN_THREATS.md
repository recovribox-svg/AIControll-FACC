# C06 playtest: stat/level-drain threat and native death limits

Task branch `codex/c06-drain-threats`, starting at played C06 `ab810b3` (PR #22; development `1f58150` / runtime `3443d94`). Depends on PR #22; version stays 0.3.30.

The supplied logs identify high beyond-HP harm on Shadows/Ghouls, but normalized threat is capped at x2 before target-removal valuation. Native combat records Succubus drain with existing negative-level penalties; the old effect reader ignored that accumulated count when predicting the next drain. Native `UnitLifeController.ForceTickOnUnit` / `UnitPartNegativeLevels.Count` establish the total-count death rule.

The next drain now uses the remaining native limit. Target removal/control also keeps at least its already-read beyond-HP harm, with the existing disabled discount and urgency. Ordinary/incoming-HP threat calibration remains unchanged. No class-name/GUID preference, forced target, new search cap, movement/commitment/retreat/pathfinding/cancellation algorithm or native command change is introduced. Shared consequence accuracy can change existing defensive values.

Two meaningful unit cases add to the 547-test C06 suite: accumulated drain/remaining life limit, and the logged Ghoul non-HP harm through ordinary/disabled/weak/zero harm and damage-progress valuation. Native compilation and SigDump verify the existing-part Count API; no actor is initialized by those offline checks. Exact committed-source build/test/strict compatible-scope receipts accompany the branch.

Replay both combat modes with mixed draining and normal threats, already-drained versus fresh victims, immunity/Death Ward, disabled sources, and difficult-to-hit targets. Check native consequences and competing values. Gameplay and matched Unity timing/allocation acceptance remain pending; the full supplied corpus's one separately fingerprinted enum spelling failure is not hidden by the strict compatible-scope result.
