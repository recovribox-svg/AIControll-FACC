# Repository instructions for coding agents

## Read before editing

Read [CONTRIBUTING.md](CONTRIBUTING.md) for branch ownership, review, testing, and release rules.
Read [CLAUDE.md](CLAUDE.md) for the shared architecture and game-specific constraints; its technical guidance applies to Codex too.
Consult the relevant design and investigation documents linked there before changing a subsystem.
Keep shared process rules in CONTRIBUTING.md and technical architecture in CLAUDE.md rather than duplicating them here.

## Codex workflow

- Work on one `codex/<task>` branch per assigned task, based on the current remote `claude/dreamy-pasteur-b9h3rn`.
- Inspect the checkout, current remote development state, and open pull requests before editing. Do not assume a preloaded checkout is current.
- Submit a pull request targeting `claude/dreamy-pasteur-b9h3rn`; `main` is reserved for released code.
- Claude reviews and integrates into development. Do not merge, push to development/main, or publish a release unless the author explicitly delegates that action.
- Preserve others' work. Coordinate overlapping changes through the pull request and author; use separate worktrees/checkouts if sharing a workspace.
- End each task with a concrete handover: behavior changed, commit/branch, checks and results, unresolved limits, and focused in-game test steps when relevant.

## Implementation and validation

- Preserve the command-dispatch and game-rule constraints in CLAUDE.md, including both combat modes.
- Use the existing game-independent test project for relevant logic changes; a full mod build additionally needs the game's assemblies.
- Check whether referenced game files and decompiled sources actually exist in the current environment. Do not infer availability from another assistant's Windows paths.
- Distinguish static inspection, automated tests, full compilation, and author-confirmed gameplay testing. Never claim an unavailable check passed.
- Keep game DLLs, decompiled sources, local paths containing secrets, and verification stubs out of release packages and commits as applicable; follow the existing README and tools guidance.
- Version changes and stable release packaging belong to the coordinated release process, not routine documentation or task setup.
