# Contributing

## Branches and ownership

| Branch | Purpose | Who updates it |
| --- | --- | --- |
| `main` | Public, latest released version; no test builds | Release maintainer, currently Claude, when MonsieurAndroid authorizes a release |
| `claude/dreamy-pasteur-b9h3rn` | Shared development and integration; may contain unfinished work and test builds | Claude; reviewed Codex work is integrated here |
| `codex/<task>` | One focused Codex task | Codex |

Normal flow: Codex task branch -> pull request into development -> review and integration -> in-game testing of the combined build -> author-authorized release to main.

MonsieurAndroid sets priorities, supplies gameplay feedback and logs, and decides what is released. Claude continues working on its existing development branch and coordinates integration and releases. Codex implements assigned tasks on separate branches. No permanent Codex integration branch is needed.

These are working conventions, not GitHub branch-protection rules.

Note (29 October 2026): at the author's instruction, `main` was brought to the reviewed development revision, which
includes test archives under `test-builds/` and changes still awaiting gameplay. Published releases are identified by
their tags and `release/` ZIPs, not by `main`. Later the same day, again at the author's instruction, `main` was moved to
the Recommendation A revision (repaired verification tooling and behaviour-preserving, unit-tested helpers). See
`docs/INTEGRATION_0322_FOLLOWUPS.md`.

## Starting and coordinating a task

1. Fetch current remote state and read this file, `AGENTS.md`, and `CLAUDE.md`. Check open pull requests for overlapping work.
2. Create `codex/<short-task-name>` from the current remote `claude/dreamy-pasteur-b9h3rn` commit. Record the starting commit in the pull request.
3. State the task scope and affected systems in the task chat or draft pull request. If Claude has unpushed overlapping work, the author should relay that information before either assistant integrates conflicting changes.
4. Use a separate checkout or worktree when assistants share a machine. Never switch branches beneath another assistant's active checkout.
5. Keep changes focused. Do not reset, force-push, or overwrite another contributor's branch.

Assistants do not automatically share chat history. Use pull requests and repository documentation as the durable handover. The author relays context from private chats when needed.

## Review and integration

- Target Codex pull requests at `claude/dreamy-pasteur-b9h3rn`, even though GitHub defaults to `main`.
- Explain the resulting behavior, affected systems, starting commit, checks performed, remaining limitations, and in-game test steps. Use the pull request template.
- Claude reviews and integrates Codex work into development. Opening a pull request does not authorize Codex to merge it or publish a release.
- If development has advanced, reconcile with its latest state without discarding others' work. Recheck the affected behavior after conflict resolution.
- Keep the task branch until its work is integrated; delete it only when no longer needed.

## Verification and gameplay handover

Use checks appropriate to the change and report actual results separately:
- **Code review / static inspection:** what was examined.
- **Automated tests:** commands, results, or why they could not run.
- **Full mod build:** whether it compiled against the real game assemblies.
- **In-game test:** exact tested commit, build/package, scenarios, results, and logs supplied by the author.

Game-independent tests can run with .NET 8 without game DLLs:

```sh
dotnet test tests/CompanionAI.Tests
```

For a full mod build, follow the current README and `Build.ps1` instructions. Game DLLs and decompiled sources may exist only on the author's machine. Do not commit them. See `tools/README.md` for offline verification tools; stubs are not deployable game dependencies and do not establish in-game correctness.

For gameplay changes, supply a focused checklist covering the changed behavior and relevant turn-based and real-time regressions. Record the tested commit and package identity (ideally a SHA-256 checksum), since test builds may share a version number. Compilation or offline tests alone do not establish gameplay stability. Documentation-only changes do not need a game build or gameplay test.

## Releasing

1. MonsieurAndroid authorizes a release after the relevant in-game tests pass.
2. Claude checks the combined development revision and records the tested commit/package. Further behavior changes require relevant retesting.
3. Update version metadata and release notes as appropriate; ordinary task branches should not independently choose release versions.
4. Bring `main` to the approved release revision, preserving history. Create the release tag and publish the package from that revision.
5. Keep development consistent with the released revision so future task branches include it.

Do not move unfinished development to `main`, publish test packages as stable releases, or alter an existing release tag to substitute different code.
