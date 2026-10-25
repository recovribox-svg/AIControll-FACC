# Verification tools (not needed for normal builds)

Normal builds reference the real DLLs from your game install (see the top-level README). These small
.NET 8 console tools were used to compile-check and binary-check the mod on a machine without the game.

## RefStubGen
Generates **compile-only** reference stubs for game assemblies that are not available (Owlcat.*, AstarPathfindingProject,
the game's Newtonsoft.Json, ...). It reconstructs them from the TypeRef/MemberRef metadata of `Assembly-CSharp.dll`,
so a stub contains only types and members the game itself uses, with the same signatures and value-type/class
encoding. A few facts that metadata references cannot express are hard-coded and commented in `Program.cs`
(enum values for `Newtonsoft.Json.Formatting` and `Pathfinding.PathCompleteState`, and `ABPath : Path`).

```
dotnet run -c Release --project tools/RefStubGen -- <Assembly-CSharp.dll> <outDir> AstarPathfindingProject Newtonsoft.Json Owlcat.Runtime.Core ...
```
Never ship stubs or deploy them to the game.

## SigDump
Prints every external TypeRef/MemberRef signature that an assembly uses. To prove that a binary built against stubs
binds exactly like one built against the real game:

```
dotnet run -c Release --project tools/SigDump -- old_release.dll | sort > a.txt
dotnet run -c Release --project tools/SigDump -- bin/Release/CompanionAI_Pathfinder.dll | sort > b.txt
dotnet run -c Release --project tools/SigDump -- <game>/Wrath_Data/Managed/Assembly-CSharp.dll | sort > game.txt
comm -13 a.txt b.txt   # every new non-Assembly-CSharp line must also appear verbatim in game.txt
```

## ProfileAudit and RunnableStubs
`ProfileAudit` runs the mod's ability readers on blueprint dumps outside the game and reports, per ability, whether the
AI values it, guesses, values nothing or never offers it (docs/COVERAGE_AUDIT.md, "Verification with the real
readers"). `RunnableStubs` is its helper: a Mono.Cecil rewriter that gives the game's engine-bound methods (Unity
internal calls, `LocalizedString` lookups, `ScriptableObject` construction) empty bodies, so the game's blueprint
loader runs on plain .NET. The rewritten copies go only into the harness's work folder, never into the game.
```
tools/ProfileAudit/run.sh <Wrath_Data/Managed> <mod dll> <work dir> <out.txt> <dump folders...>
```

ProfileAudit is strict by default: a failed/empty load returns 1 and replaces any stale report with INCOMPLETE.
For a known small number of unrelated failures, pass `--allow-failures N` among the dump arguments (for example,
`--kinetic-checks --allow-failures 1 <dump folders...>`). Every failure is listed; an allowed run returns 0 and prefixes
the report PARTIAL with loaded/failed counts. An empty or predominantly failed load remains INCOMPLETE even if N is
large, preserving the guard against a missing dependency appearing to be a zero-ability success. Invalid options
return 2. A partial report is not complete coverage. Unresolved blueprint GUIDs are a separate completeness limit.


For kineticist data, add `--kinetic-checks` among ProfileAudit's dump arguments. This checks infusion/carrier saves,
ongoing effects, area cadence, modifier descriptors and native preview-hook installation. It also confirms that
native cost is unchanged outside a scope and detached dispel preparation stops before dice. It requires the new
mod DLL and Harmony's native helper to work on the host. These are offline integration checks, not in-game actor,
command, restoration, performance or third-party-mod tests. Never deploy the harness or its rewritten dependencies.

The kinetic checks also inject a late optional-patch failure and verify rollback/readiness, check that these hooks
cannot be found by mandatory PatchAll, exercise inactive hot-path exits, and verify per-decision native-query caching.

The playtest follow-up also checks native parameter reuse with isolated ability/aim/modifier states, cloned results,
mutable-ambient-context bypass, early spent-slot pruning and virtual dispatch through a synthetic native calculator
subclass. Counting seams replace game-singleton-dependent calls in the offline process; they are not a latency
benchmark. See `docs/KINETICIST_PLAYTEST_STUTTER.md` for the supplied logs and required gameplay replay.

The condition-context follow-up checks cast/aim/source/recipient isolation, native main-target parity, scoped
guard-hit isolation, shared-value restoration on normal/early/exception paths, movement-only skips and retained
deferred scoring. The offline cache checks also require an isolated fixed `Time.frameCount` getter; live Unity
supplies it in the game. Guard-hit fixtures use seeded results because fully initialized actor misses need the
entity registry. See `docs/DECISION_CONTEXT_PLAYTEST.md`; no harness rewrites belong in a deployed mod.
