# Verification tools (not needed for normal builds)

Normal builds reference the real DLLs from your game install (see the top-level README). The .NET 8 console tools
below compile-check, binary-check and inspect readers outside the game. The standalone Python inventory needs no game DLLs.

## BlueprintInventory
[BlueprintInventory](BlueprintInventory/README.md) reads local JBP exports and emits derived class/archetype/progression
metadata, prerequisite links, input identities and reference-closure counts. It helps request targeted mechanism graphs
without uploading the whole game. It never executes native code or changes exports; its inventory is not gameplay validation.

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

The audit also exercises the shipped ricochet delivery-layer reader: exact equality/Not/operand-order cases,
loaded reference plain-blast graphs (layer0 full versus nonzero-layer half), and the damage-calibration gate for
explicitly half packets. No actor or cast is created. Missing reference graphs are explicitly marked SKIPPED;
failures return1. These checks verify reader behavior, not live combat. The report lists their counts separately.

★ v0.3.28 test 6: it also checks the rank-increment reader (`TacticalEngine.RankTalents`): its reflected game members are
bound, Enveloping Winds' miss chance reads its effect feature's rank once with an evaluable progression, and both Shroud of
Water effect features name the upgrade feature (`RANK-INCREMENT n checks, failures m`; missing blueprints are SKIPPED).

★ v0.3.28 test 7: and the link the quiver preparation relies on - Create Quiver's profile puts on its caster the very buff
the kineticist part names as the quiver (`QUIVER-BINDING ok` or `FAILED`; SKIPPED without the Kinetic Sharpshooter dumps).

Substance infusions (activatable group `SubstanceInfusion`) are reported through the mod's own scoped infusion reader
(`TacticalEngine.DescribeInfusion`, read by reflection): verdict `infusion selection: supported` or
`infusion selection: withheld (<the engine's reason>)`, with the buff, whether it has a damage graph, the trigger's
action types and any spell-resistance / damage-reduction bypass. On the author's dumps (29 October 2026): 18 supported,
Chain Arrows and Unraveling withheld. If the reader or one of its fields is renamed, the audit fails instead of
silently reporting everything as supported.

`--optional-provider <TabletopTweaks-Core.dll>` checks the mod's own TabletopTweaks-Core damage-reduction binding
(`Tactics/ModdedDamageReduction`) against a real TTT DLL. It can run alone (no dump folders) or with them, and its lines
head the report. It loads the DLL into the harness, lets the mod bind lazily as in the game, then calls the bound
`TryInitialize` and `EstimateDamage` on a fresh, empty TTT part (10 fire damage must stay 10). `Get<T>` and `IsImmune`
need a real unit, so they stay for a game session. Absent and mismatched providers are covered by the unit tests
(`OptionalApiBindingTests`). The TTT DLL is a third-party mod: pass its path, never commit it.
```
tools/ProfileAudit/run.sh <Wrath_Data/Managed> <mod dll> <work dir> <out.txt> --optional-provider <TTT folder>/TabletopTweaks-Core.dll
```

`--blocklist-checks` runs the mod's "Abilities AI must not use" matching (`UserAbilityPolicy.SourceOf`) on the loaded
blueprints:
- every variant is blocked by its parent's entry and by its own, but not by a sibling's;
- conversions that are not variants (Cure from a Bless slot) do not inherit the source's ban;
- empty lists block nothing and keep the same list.

It also prints the time and allocations of one check. Its lines head the report, and a failed check returns 1
(docs/TASK_USER_ABILITY_BLACKLIST.md §2.5).
```
tools/ProfileAudit/run.sh <Wrath_Data/Managed> <mod dll> <work dir> <out.txt> --blocklist-checks <dump folders...>
```

The withdrawn kineticist engine's harness options (`--kinetic-checks`, the decision-read and condition-context
fixtures, the `Time.frameCount` stub) were removed with that engine (`docs/V0_3_22_POSTMORTEM.md`);
`--kinetic-checks` now fails with that explanation. Documents describing those runs (`docs/DECISION_CONTEXT_PLAYTEST.md`,
`docs/KINETICIST_*`, `docs/INTEGRATION_HANDOVER.md`) describe their own time.
