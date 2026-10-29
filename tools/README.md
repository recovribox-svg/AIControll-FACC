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
`--allow-failures 1 <dump folders...>`). Every failure is listed; an allowed run returns 0 and prefixes
the report PARTIAL with loaded/failed counts. An empty or predominantly failed load remains INCOMPLETE even if N is
large, preserving the guard against a missing dependency appearing to be a zero-ability success. Invalid options
return 2. A partial report is not complete coverage. Unresolved blueprint GUIDs are a separate completeness limit.

Substance infusions (activatable group `SubstanceInfusion`) are reported through the mod's own scoped infusion reader
(`TacticalEngine.DescribeInfusion`, read by reflection): verdict `infusion selection: supported` or
`infusion selection: withheld (<the engine's reason>)`, with the buff, whether it has a damage graph, the trigger's
action types and any spell-resistance / damage-reduction bypass. On the author's dumps (29 October 2026): 18 supported,
Chain Arrows and Unraveling withheld. If the reader or one of its fields is renamed, the audit fails instead of
silently reporting everything as supported.

The withdrawn kineticist engine's harness options (`--kinetic-checks`, the decision-read and condition-context
fixtures, the `Time.frameCount` stub) were removed with that engine (`docs/V0_3_22_POSTMORTEM.md`);
`--kinetic-checks` now fails with that explanation. Documents describing those runs (`docs/DECISION_CONTEXT_PLAYTEST.md`,
`docs/KINETICIST_*`, `docs/INTEGRATION_HANDOVER.md`) describe their own time.
