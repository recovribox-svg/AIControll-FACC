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

ProfileAudit requires every selected blueprint file to deserialize. If any file fails to load, or no blueprints load,
it exits with status 1 and replaces the report with an `INCOMPLETE` diagnostic. Missing Unity dependency DLLs can
otherwise look like a successful run with zero abilities. An incomplete run is not coverage evidence; fix the loader
error and rerun it. Unresolved blueprint GUIDs in a successfully deserialized dump are a separate data-completeness
limitation and still need investigation for the feature being audited.
