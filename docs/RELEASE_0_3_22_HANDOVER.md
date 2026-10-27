# v0.3.22 release preparation and development integration

## Author authorization and scope

On 27 October 2026, MonsieurAndroid explicitly asked Codex to prepare the current
version as **0.3.22**, make UMM display that version, put its ZIP in the development
repository, and merge the relevant pull requests into development. The author
will create the GitHub release personally. This supersedes the normal
Claude-only integration convention for this specific integration.

No GitHub release is published and no tag or main-branch update is performed by
this task. When publishing, the author must select the prepared development
revision rather than the repository's older default main branch.

## Integrated code

PR #11 at `47a62bac41e56e33e98ddf9a068b737a3c0a66cc` contains the complete final
scoped PR #6–#10 chain. It was merged with history preserved into
`claude/dreamy-pasteur-b9h3rn` as
`5152ec75a88c80d3a9d3a4490fe9d07ec3954a0d`. GitHub marks **all PRs #6–#11 merged**.
Their earlier task reports saying draft/unmerged describe their historical state.

Release preparation starts from that integration on `codex/release-0.3.22`.
The only new runtime-source change is the startup identity line: it reads the
actual UMM manifest version instead of printing the old test label/0.3.21.
`Info.json` is set to 0.3.22. Decision algorithms, budgets, dispatch, toggles,
movement and game-speed behavior are unchanged from runtime `25434a2`.

This is the scoped work after the rollback, not the withdrawn broad kineticist
implementation. The [postmortem](V0_3_22_POSTMORTEM.md) and the older PR #4 handover
remain historical references. Pull current development before further changes.

## Package and publication

The intended artifact is
[`release/CompanionAI_Pathfinder_v0.3.22.zip`](../release/CompanionAI_Pathfinder_v0.3.22.zip),
with exactly the production mod DLL and Info.json at its root. No game DLL,
verification shim, harness, raw playtest log or settings file is shipped.
The versioned identity sidecar and SHA256SUMS record its exact DLL/source/module.
Earlier release and test ZIPs remain unchanged.

Release preparation is [PR #12](https://github.com/recovribox-svg/AIControll-FACC/pull/12).
The immutable package commit is `2c626c1c1fc8ac4c94a7f7fadb63ad18ef113401`;
that PR's timeline records the final development merge revision. Use that prepared
merge revision as the release target, attach this ZIP, and use
[CHANGELOG_v0.3.22.md](../release/CHANGELOG_v0.3.22.md) for the release notes.
Creating the GitHub release/tag remains the author's action.

## Verification and gameplay status

- All **164 unit tests pass**, zero failed/skipped. Release builds against the
  supplied game/UMM references with **zero warnings/errors**.
- Compiled concentration/Selective/HD diagnostic, activation-adoption and
  censored-resistance guards pass on the release DLL. Native cone/range bindings
  and the retained memo/Grease/activation metadata checks pass, including 486
  native burn/quiver vectors.
- The partial reader audit loads 4,097 exports with the same one known unrelated
  failed export, produces 1,220 ability rows with zero reader errors, and matches
  all normalized Test[5] verdict/detail rows.
- Only Info.json and Main's version identity line differ from the integrated
  Test[5] runtime source. Packaged contents, checksums and public download are
  verified before handover. Exact identity is in `release/README.md` and its
  versioned sidecar. No fresh in-game or live third-party handler test is claimed.

The author reports Test[5] smoothness 8/10 and definite improvement over Test[4].
Those supplied gameplay logs precede the final concentration-classification fix
and bounded aim/energy diagnostics from `25434a2`. They do not independently
identify a startup MVID. The author now authorizes public release of the current
version; no additional gameplay run is claimed by Codex.

Open leads remain explicit: initial energy-resistance optimism, the unproven
Quasit-versus-human-cluster counterfactual, and a separate unacted Aid Another
outcome-ordering case. Preserve existing group Sleep behavior, meaningful native
miss/save outcomes and partial revive progress. Continue using AI logs plus
native CombatLog for diagnosis. See [Test[5] review](TEST5_REVIEW.md) and
[release notes](../release/CHANGELOG_v0.3.22.md) for limits and regression cases.
