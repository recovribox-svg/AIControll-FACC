# Verification tools (not needed for normal builds)

Normal builds reference the real DLLs from your game install (see the top-level README). These two small
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
