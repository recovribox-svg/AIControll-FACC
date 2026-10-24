#!/bin/bash
# Offline check of the AI's ability readers on blueprint dumps (docs/COVERAGE_AUDIT.md, "Verification with the real readers").
#   tools/ProfileAudit/run.sh <ManagedDir> <ModDll> <outDir> <out.txt> <blueprint dump folders...>
# ManagedDir: the game's Wrath_Data/Managed (plus UnityModManager/UnityModManager.dll); ModDll: a Release build of the mod.
set -e
M="$1"; MOD="$2"; OUT="$3"; REPORT="$4"; shift 4
HERE="$(cd "$(dirname "$0")" && pwd)"
dotnet build "$HERE/ProfileAudit.csproj" -c Release -p:ManagedPath="$M" -p:ModDll="$MOD" -o "$OUT" | grep -E " error |Build succeeded" | sort -u
dotnet build "$HERE/../RunnableStubs/RunnableStubs.csproj" -c Release -o "$OUT/stubs" > /dev/null
F="dotnet $OUT/stubs/RunnableStubs.dll"
# reference stubs and engine-bound methods: runnable copies next to the harness only
for f in "$M"/UnityEngine.*Module.dll; do n=$(basename "$f"); [ "$n" = "UnityEngine.CoreModule.dll" ] && continue; $F "$f" "$OUT/$n" > /dev/null; done
$F "$M/Assembly-CSharp.dll" "$OUT/Assembly-CSharp.dll" Kingmaker.Localization.LocalizedString::op_Implicit Kingmaker.Localization.LocalizedString::ToString > /dev/null
$F "$M/UnityEngine.CoreModule.dll" "$OUT/UnityEngine.CoreModule.dll" UnityEngine.ScriptableObject::.ctor UnityEngine.Object::Finalize \
   UnityEngine.AnimationCurve::.ctor UnityEngine.AnimationCurve::Finalize UnityEngine.Gradient::.ctor UnityEngine.Gradient::Finalize > /dev/null
[ -d "$M/UnityModManager" ] && cp "$M"/UnityModManager/*.dll "$OUT/"
(cd "$OUT" && dotnet ProfileAudit.dll "$@" "$REPORT")
