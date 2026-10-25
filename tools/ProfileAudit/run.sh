#!/bin/bash
# Offline check of the AI's ability readers on blueprint dumps (docs/COVERAGE_AUDIT.md, "Verification with the real readers").
#   tools/ProfileAudit/run.sh <ManagedDir> <ModDll> <outDir> <out.txt> <blueprint dump folders...>
# ManagedDir: the game's Wrath_Data/Managed (plus UnityModManager/UnityModManager.dll); ModDll: a Release build of the mod.
set -euo pipefail
if [ "$#" -lt 5 ]; then
    echo "Usage: $0 <ManagedDir> <ModDll> <outDir> <report.txt> [--kinetic-checks] <dump folders...>" >&2
    exit 2
fi
M="$1"; MOD="$2"; OUT="$3"; REPORT="$4"; shift 4
HERE="$(cd "$(dirname "$0")" && pwd)"
dotnet build "$HERE/ProfileAudit.csproj" -c Release -p:ManagedPath="$M" -p:ModDll="$MOD" -o "$OUT" | grep -E " error |Build succeeded" | sort -u
dotnet build "$HERE/../RunnableStubs/RunnableStubs.csproj" -c Release -o "$OUT/stubs" > /dev/null
stub() { dotnet "$OUT/stubs/RunnableStubs.dll" "$@"; }
# reference stubs and engine-bound methods: runnable copies next to the harness only
for f in "$M"/UnityEngine.*Module.dll; do n=$(basename "$f"); [ "$n" = "UnityEngine.CoreModule.dll" ] && continue; stub "$f" "$OUT/$n" > /dev/null; done
stub "$M/Assembly-CSharp.dll" "$OUT/Assembly-CSharp.dll" Kingmaker.Localization.LocalizedString::op_Implicit Kingmaker.Localization.LocalizedString::ToString > /dev/null
# ★ v0.3.22: guard-cache fixtures need a stable frame counter outside Unity. This rewrites the harness copy only.
core_stubs=$(stub "$M/UnityEngine.CoreModule.dll" "$OUT/UnityEngine.CoreModule.dll" UnityEngine.ScriptableObject::.ctor UnityEngine.Object::Finalize \
   UnityEngine.AnimationCurve::.ctor UnityEngine.AnimationCurve::Finalize UnityEngine.Gradient::.ctor UnityEngine.Gradient::Finalize \
   UnityEngine.Time::get_frameCount)
# Some Unity versions omit Object.Finalize; the frame getter is required for these checks on every version.
if [[ "$core_stubs" != *"  UnityEngine.Time::get_frameCount"* ]]; then
    echo "ProfileAudit: required UnityEngine.Time::get_frameCount stand-in was not created." >&2
    exit 1
fi
[ -d "$M/UnityModManager" ] && cp "$M"/UnityModManager/*.dll "$OUT/"
(cd "$OUT" && dotnet ProfileAudit.dll "$@" "$REPORT")
