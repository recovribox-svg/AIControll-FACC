# v0.3.50: review and integration of PRs #46–#51

The author explicitly authorized Codex to review and merge all open PRs and requested the next build be
**v0.3.50** (13 November 2026). This is the task-specific exception to the usual Claude-only development
integration rule. The target is `claude/dreamy-pasteur-b9h3rn`; this is a development test build.

## Reviewed inputs and merge structure

Development was refreshed at `44b224f5af76dc5f5ae6086d6abbfbdeac790627`. Six PRs were open:

| PR | Reviewed head | Scope |
|---|---|---|
| [#46](https://github.com/recovribox-svg/AIControll-FACC/pull/46) | `692eb82` | Plan owned-summon control using native turn-based AI entry |
| [#47](https://github.com/recovribox-svg/AIControll-FACC/pull/47) | `cee7d48` | Pair Shifter form forecasts with the current assessed target |
| [#48](https://github.com/recovribox-svg/AIControll-FACC/pull/48) | `4c0ef7d` | Avoid retrying refused recovery moves within the same decision |
| [#49](https://github.com/recovribox-svg/AIControll-FACC/pull/49) | `0a04adb` | Summon control (opt-in): AI Control drives the party's summoned creatures |
| [#50](https://github.com/recovribox-svg/AIControll-FACC/pull/50) | `4745033` | Support Focused Shot using the native single-shot structure |
| [#51](https://github.com/recovribox-svg/AIControll-FACC/pull/51) | `e0f8304` | Require jointly affordable activation costs for spell boosts |

#49 already includes the #46 plan. #51 contains #47, #48 and #50. The integration preserves all original
head commits as ancestors rather than squashing or copying changes; one reviewed integration PR can merge
the whole stack and let GitHub mark the included PRs merged. Recheck the original heads before merging.
No public release, tag or main update is part of this task. Manifest Id, entry point and settings identity
remain unchanged; only the requested Version changes from 0.3.30 to 0.3.50.

## Review findings and corrections

- **Final summon authority:** #49's command guard reused the frame-cached control answer. Switches, owner/faction
  state or active facts can change between checks in one frame. CommandDispatcher now uses an explicit fresh
  authority read; routine brain/UI checks retain their existing frame memo. No new movement policy.
- **Swarm eligibility lifetime:** #49 cached its first swarm result for the creature's whole lifetime, although
  the shared classifier reads active facts. Reuse the shared frame/decision snapshot and refresh it at final
  issue. A changed snapshot invalidates dependent swarm geometry/DC reads. Unchanged snapshots allocate nothing.
- **Unique summon cohort size:** release/re-adoption reset AdoptionLogged and incremented the per-fight count
  again. AdoptionFight now determines whether the counter increments; ownership-transition logs remain useful.
  Actor-free tests exercise first/repeated/re-adopted/new-fight/different-creature transitions.
- **Gate cost:** excluded mode, faction and unusable actors return before swarm/owner scans. Enabled feature
  still uses the same pure refusal ordering and native ownership predicates; no owner row is created.
- **Integration:** preserve every ProfileAudit registration and both new test-source includes. Keep all original
  handovers; the summon implementation is section 156, so Shifter/recovery/Focused Shot/cost sections 152–155
  remain distinct. This review is section 157. Original source/package identities remain historical evidence.

The source review also checked native Prepare/ForceTick/Acting/ForceToEnd ordering against the supplied
assembly, shared party/summon completion, no-action/command wait handling, default-off and hook-failure gates,
non-persisting neutral settings, owner blocklists, pet/nested/swarm/item/upkeep exclusions and RT pacing cleanup.
The existing shared completion's empty rider/mount command check matches native CanEndTurnAndNoActing.
The bounded recovery fix preserves final path safety; form quotes retain their actual reference; Focused Shot
admits only its inspected structure; joint costs use native source/order and current known prices.

## Validation and limits

The final receipt records the committed combined source, Release build against supplied actual game/Unity/UMM
assemblies, all unit/inventory tests, strict compatible and supplementary ProfileAudit scopes, optional TTT/Psionics/
Tome/ToyBox regressions, exact foreign member signatures and the final ZIP identity/checksums.

These are automated/offline checks. They do not initialize actors, run native turns/casts, apply Harmony patches
or establish Unity performance. The original raw export's known SawtoothSaber/SawtoothSabre enum mismatch remains
separately incomplete; original inputs are not rewritten. Source assembly identity/provenance and that limit
remain in the earlier receipts. Custom/future cost changes and existing weapon-delta approximations retain the
limits documented in [joint costs](JOINT_SPELL_COSTS.md) and [Focused Shot](FOCUSED_SHOT.md).

## First v0.3.50 playtest

1. Start with default settings: companion/pet behavior and ordinary TB/RT turn completion, with summon control off.
2. Enable experimental summon control: one Leopard, then several melee summons; one caster (Bralani) and a
   Demonology ritual summon. Every TB turn must start, act and finish. Check no-option/blocked paths and waiting
   projectiles; stop and report a stuck turn. Compare matched off/on timings, cold/later decisions and gate cost.
3. Toggle control/mode/owner AI during combat; owner unconsciousness/despawn; inherited blocklists; no summon
   settings rows, item/material use or summon-created summons. A swarm body stays with native AI. Re-adopting
   the same creature does not inflate the unique cohort count. Pets/manual party commands remain intact.
4. Shifter form/Fury and paired ordinary/immune targets; rejected plain advances; Focused Shot versus single/full
   attacks on a ranged non-caster; Quicken with one/two shared slots and a cheaper affordable alternative.
5. Keep the startup MVID and label ToyBox spawn/rest interventions. All combined gameplay remains unplayed until
   this replay; the v0.3.50 version number does not turn test evidence into a stable-release claim.

Checked source `ebfac70`, [receipt](competence/V0350_INTEGRATION_CHECKS.json) and [integration1 test ZIP](../test-builds/AI-Control-FACC-v0.3.50-dev-integration1-ebfac70-UMM-test.zip), startup MVID `0b54fad9-fc28-483c-a87a-3f1e9f0efd62`. 705 unit + 27 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 27/27 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
