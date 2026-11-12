# Selected-path validation for step-and-full-attack plans

The author approved the two movement proposals on 12 November 2026. This first task starts at magus1 / PR #35
packaging `aaf9b20`, including fetched development `1f58150`. It covers five-foot-step full-attack feasibility;
reachable area exits are separate. No development/main push, merge, release or version bump is authorized.

The caster1 [review](CASTER1_PLAYTEST_REVIEW.md) records 25 TB path/reach/sight refusals, including repeated `+5ft`
attacks 0.5–1.4 m short (TB2 3313–3556). Those rows include several causes. The confirmed gap here is that
`AddWeaponCandidates` prices a step with a full routine but sets neither `NeedsMove` nor `MoveFirst`.
`PreflightMainCandidates` only checked `WalksBeforeActing`, so these plans skipped the selected-path check
before mark/boost coupling and reached the dispatcher's rejection instead.

Full-attack candidates now carry explicit step intent. Only selected-path classification consumes that flag;
it is not a normal Move-action requirement. Native affordability, full/single flags, step limits and attack/area/AoO
prices retain their rules. In-place routines add no navigation work. The existing weak-weapon fallback predicate
is shared between preflight and final selection, covering low-value step/full routines too.

`SelectedApproachFilter` still checks at most **three selected plans** per decision. A failed plan exposes the
next already-scored action before boosts; unchecked step/act plans are withheld when that budget is spent.
Plain moves retain their existing exception. No all-enemy path scan, global unreachable claim, persistent cache
or retained path is added. New decisions read current geometry; actual issue revalidates. `CanPrepare` releases
temporary native path claims in its existing `finally`.

Native evidence: supplied `Assembly-CSharp.dll`, SHA-256
`2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`, MVID
`90a9869c-2792-4c7b-bfb7-5a8b33da7c82`. `AiAction.ScorePath` checks the actual path endpoint with
`UnitCommand.IsUnitCloseEnough`; `UnitAttack.OnStart` allows explicit `ForceFullAttack`. The existing dispatcher
therefore enforces actual endpoint range/sight and remaining step distance for forced full routines. This repair
routes missing candidates through that inspected contract and introduces no replacement game rule.

Offline checks exercise actual candidate fields, bounded fallback scheduling, fresh-decision retry and the
compiled flag producer without actor initialization, native paths or command issue. The allocation check is a
narrow predicate microcheck, not Unity timing. Final receipt/package identity records unit/native regressions,
real-reference Release and compatible ProfileAudit; the original full export's enum failure stays separate.

Replay TB with a valid step/full attack, an ally-blocked endpoint, a corner that exceeds step distance, missing
sight, a harmful area on the route, and a weak weapon versus resistance. Rejected plans should log
`[Approach] ... withheld before issue/boost` and expose an available alternative. Confirm valid full attacks
retain attacks/action budget and no boost resource buys a rejected follow-up. Regress in-place attacks,
ranged units, Pure Mage, native Magus follow-ups and shared RT (which has no ordinary five-foot-step candidates).
Use matched party/fight timings to judge performance.

Commitment, retreat, route finding, hazard geometry and stuck cancellation remain outside this first repair.
The second approved proposal is separate. Manifest 0.3.30; Claude reviews/integrates; gameplay pending.

Checked source `ee24df1`, [receipt](competence/MOVEMENT_STEP_CHECKS.json) and [step1 test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-step1-ee24df1-UMM-test.zip), startup MVID `9523fbe4-edb2-46b5-9fbe-e430636c9634`. 607 unit + 11 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 0/0 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
