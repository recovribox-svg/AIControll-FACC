# C06: resource-aware toggle lifetimes

Candidate branch `codex/class-competence-toggle-costs` ([PR #22](https://github.com/recovribox-svg/AIControll-FACC/pull/22)), originally from `fdc5e5e`. Self-review uses the author-requested **`3443d94`** base and reconciles current development packaging `1f58150`, preserving original PR head `6d4e993`. Manifest **0.3.30**. Work started after the mapping [PR #21](https://github.com/recovribox-svg/AIControll-FACC/pull/21) opened; this is a separate runtime PR. Claude retains C07 Magus dispatch/Spell Combat/Spellstrike and C08 forms. No movement, version, merge or release change.

## Problem and resulting behavior

`ToggleValue` treated `ActivatableAbility.ResourceCount` as rounds. For a readable paid minute with one use remaining, that capped the buff's appraisal at one round; for a two-unit round drain it overstated the available rounds. `StopIfPointless` also treated any remaining quantity as a continuing expense.

[TacticalEngine.Toggles.cs](../../Tactics/TacticalEngine.Toggles.cs) now reads a bounded spending snapshot, with pure scheduling in [ValueModel.Toggles.cs](../../Tactics/ValueModel.Toggles.cs). It separates activation, existing paid time and upcoming payments. Both the modeled buff window and the legacy/harmful fallback's window use that result; their existing six-/three-round caps remain. Unknown contracts retain the previous valuation/discount/stop fallback and identify the reason.

| Shape | Bounded contract |
| --- | --- |
| `NewRound` | Current owned increasing/decreasing facts determine units per callback, clamped at zero. Pool quantity divided by that cost bounds the window; an active toggle keeps its already-paid time until the next callback |
| `Start` / `TurnOn` | Pay once on start, with no continuing drain. An active paid toggle is not charged again or stopped to save a nonexistent renewal |
| Minute / ten minutes / hour | Pay on start; use the actual existing counter and `TimeToNextRound` for renewal. `TryStart` sets the first callback due now, so that callback also decrements the newly reset counter. No timer data is created by appraisal |
| `Judgment` | Its resource component gates availability; the buff's shared watcher performs the actual one-point spend. Read the real guarded activation graph, its paid/free facts and Ignore stacking. Healing ticks and Smiting's resource-inert rank branches do not become a second cost. Its proven persistent `JudgmentSwitch` watcher also waives native positive-pool availability, so the first/last paid Judgment retains its window at zero. Unknown unequal resource branches remain fallback |
| Installed TTT | Recognize the inspected Core module and its registered prefix/postfix, rather than infer activation from assembly presence. Honor its native one-shot post-spend deactivation and packed critical amount/free-fact rule. Unknown cost/callback patches stay fallback |

Native semantics were inspected in the supplied `ActivatableAbilityResourceLogic` (`OnStart`, `OnNewRound`, `CalcResourceCost`, `SpendResource`), `ActivatableAbility` (`TryStart`, `OnAttack`/`OnHit`/`OnCrit`), controller ticking and `ContextSpendResource.RunAction`. The latter spends **one** when `ContextValueSpendure=false`; its serialized `Value` can be zero. TTT evidence is the supplied Core 0.7.15, module `cdfed43f-53a0-4186-a181-75496eecec8f`, and Base 2.6.18/settings. No decompiled source or supplied binaries are committed.

Base's `ActivatableResourceFreeResourceLogic` was also inspected. The supplied `DisableAfterCombatDeactivationOfUnlimitedAbilities` flag is true: its gated patches can keep single-FreeBlueprint abilities on after combat and skip their ordinary new-round callback while free and available. The quote reads that current free fact as zero cost; after it is lost, actual timer data is read again or remains fallback. Existing game/mod lifecycle and AI restoration ownership are preserved; this is not a live initialization check.

## Self-review corrections (8 November 2026)

| Finding | Evidence and final change |
| --- | --- |
| Last Judgment fell back despite its paid watcher | The original caller's depleted-pool guard also caught the first Judgment when one use remained. Native `IsAvailable` requires zero points while `JudgmentSwitch` is held; the actual shared watcher grants that mechanic. The activation reader now reports a certain permanent/same-duration grant, or reads the actor's existing mechanic. The caller waives only that pool requirement. All fifteen native graphs check cost plus lifetime; a separately timed watcher does not certify the waiver |
| Patch inspection repeated for each toggle | Inspected Harmony 2.2.2 `GetPatchInfo` deserializes stored patch bytes per query. C06 made five such queries and repeated method lookup per toggle/appraisal. Native method identities are now resolved once; a weak context-keyed snapshot shares only known/TTT patch-contract booleans within a decision. `Decide` constructs a fresh context each time, in both modes; new contexts inspect again. Facts, quantity, counter and time are always read afresh. Metadata transition/reuse checks cover this scope; no Unity performance result is claimed |
| Original package lacked Claude's later development fixes | Reconciled `cfd8f0a` and `3443d94` without editing their reveal, large-party decision, unique-buff settle, cast-time bonus or burn-diagnostic runtime files. The new candidate package below supersedes the older `a861a2b` C06 ZIP |

Patch-snapshot fixtures use the pinned offline Harmony provider's serialized metadata records. The host cannot run its native detour helper, so no method wrapper is installed or invoked; the original bytes are restored after the test. That checks production metadata inspection and context invalidation, not live patch application. Actual TTT initialization and Harmony runtime outcomes remain gameplay work.

## Preserved policy and explicit limits

| Boundary | Disposition |
| --- | --- |
| Daily-resource restraint | Keep the existing `clamp(.55 + .45 × difficulty, .55, 1)` whenever a newly valued start/renewal spends. A genuinely paid/free interval has no second resource charge. Unknown contracts keep the previous discount; no new scarcity formula |
| Stopping | Keep the existing beneficiary/performer rule. Stop only for a known affordable recurring payment within one callback, or under the existing unknown-contract fallback. Do not throw away a paid minute/hour to save zero units |
| Empty-pool event lifetime | Native `DeactivateImmediately` can end a depleted toggle on an attack/hit/crit callback even before its paid timer expires. Without a triggering-action forecast, retain the prior fallback at this boundary when availability still requires a positive pool. The Judgment watcher waiver is read from actual mechanics or a certain persistent activation path, not assumed from a name or spend-type label. The pure timer result is not a certificate of that event-dependent lifetime |
| Other fallback shapes | Multiple/custom spend components, source-item charges, first-round expiry, missing/invalid renewal state and unsupported activation actions/guards remain explicit. This does not certify every resource toggle |
| Permissions and execution | Existing `AiManagesToggles`, native availability, hidden/group/command exclusions, acceptance, player ownership, blocklists, switch throttling, execution and post-fight restoration remain in their existing paths. No attack-triggered toggle is newly admitted |
| Mod/actor validation | Exact installed-code inspection and fixtures are separate from active game patching, initialized actor ownership, actual decisions and Unity cost. Those remain pending |

The class/mythic census is 266 resource components: 151 round, 44 minute, 27 TurnOn, 15 Judgment, 12 ten-minute, eight hour, five Start and four attack-triggered. The production profile has no structural exclusion for **232** of them (107 non-round); this still says nothing about actual ownership, settings, hidden UI, affordability or final selection. All four attack-triggered cases are excluded before this candidate path; their existing mode/dispatch valuation is unchanged.

## Verification and integration

[C06_CHECKS.json](C06_CHECKS.json) records the final DLL identity and exact results. The Release build uses the supplied actual native/UMM references and the repository's existing Harmony compile dependency, with no warning suppression added. SigDump is compared against the existing competence3b DLL (`3443d94`, MVID `740e7ec5-27f8-4575-9187-258b0f61f426`); the added game members resolve against the supplied native assembly and Harmony members against the pinned compile provider. The game's runtime Harmony provider is not independently certified by that comparison.

- Unit tests reproduce the updated **531-test** development baseline, **546** at C06 intake, and **547** after self-review: activation versus renewal, cost modifiers, paid time, fractional time to expiry, zero cost, daily restraint and TTT's one-shot last-use boundary.
- Strict ProfileAudit remains **INCOMPLETE**, 44,637 loaded / one Unity-bound Achaekek failure, no verdicts. A separately fingerprinted allowance of that exact failure produces **PARTIAL**, 4,807 rows / zero reader errors. It also retains the existing burn/save/area/on-hit/affliction/swarm/stat-link checks.
- [Native fixtures](../../tests/ToggleCosts.Native/README.md) use full exported progression roots, archetype add/remove levels, AddFacts grants and explicit feats/hex/states. The **90 checks** exercise the compiled cost reader, all fifteen Judgment graphs through the cost/lifetime boundary, native timers, negative boundaries and decision-scoped patch metadata; they do not create actors or issue commands.

Production callers inspected: turn-based `TurnBasedController` → `TacticalEngine.Decide/Execute`, and real time `RealTimeTactics` in `TacticalDriver.cs` → the same engine. `Decide` retains `AddToggleCandidates`; `Execute` retains the final blocklist check and `ExecuteToggle`. `CombatMemory` retains player ownership and restoration, including its no-resource-spending restoration rule. Those caller/dispatch files are unchanged.

The uniquely named UMM ZIP and source commit/MVID/SHA-256 receipt are recorded in `C06_CHECKS.json` and beside the ZIP in `test-builds/`. It contains only this mod's DLL and unchanged `Info.json`, with package bytes checked against the Release output. This is an unplayed development candidate.

## Paired in-game replay (pending)

Run each relevant case in both turn-based and real time; retain startup MVID, AI log, native combat log and the actual owned facts/settings.

| Case | Observe |
| --- | --- |
| Native Bard and Chelish Diva with the actual increasing-cost state | Cost/window follows one versus two units per callback; unaffordable starts decline; compare the chosen toggle with competing attacks/spells |
| Aura of Purity or another confirmed persistent paid interval | An already-paid interval is kept while renewal is distant. If it has no other beneficiaries, an affordable renewal is stopped near its callback, with no second start charge |
| Depleted Sacred Armor / Shifter aspect with `DeactivateImmediately` | Label the event-lifetime fallback; do not infer a full minute from the pure timer. Native event-driven expiry remains observable separately |
| First/last, additional and free Judgment | With exactly one resource point, the first Judgment spends once and retains its paid window through its native watcher. Additional already-paid choices do not spend again; capacity/exclusivity and free-feature guards hold |
| TTT enabled / absent / unknown patch set | Match the actual registered provider, verify one-shot last-use deactivation; unknown contracts show fallback. New decisions recheck the patch contract; Perfect Strike/Critical and attack-activated domain modes keep their existing admission/ownership |
| Policy transitions | `AiManagesToggles` off, manual click with respect enabled, blocked ability, acceptance off/on, game-started rage, fight end and next fight: no takeover or restoration that spends a resource |

A declined toggle is acceptable when another feasible action wins. Report source/target, cost, predicted duration and competing value; selection/issuance alone is not a successful native outcome. Gameplay stability and matched Unity CPU/allocation/latency measurements remain pending.
