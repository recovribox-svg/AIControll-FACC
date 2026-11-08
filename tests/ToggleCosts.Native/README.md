# Native toggle cost fixtures

This optional .NET 8 executable links the existing ProfileAudit loader/checks without changing that tool. It runs the **compiled mod's** private cost readers on externally supplied JBP objects, with progression-shaped fact sets for Bard, Chelish Diva, Warpriest, Shaman, Bladebound and Inquisitor. Actual archetype level replacements and explicit common feats/hex/state transitions are included. Selection alternatives are not all granted; these are not initialized native actors.

Checks cover native fact modifiers (including duplicate entries), zero/free/list costs, all fifteen Judgment graphs before/after payment, the Ignore watcher, packed TTT critical pricing, an unknown unequal-cost branch, unchanged event-toggle exclusion, and the native runtime timer's read-without-creation API. Fixture GUIDs identify evidence only; production recognition uses component/action shapes.

Use the same actual references, mod DLL and fourteen export roots as the current stage's [check receipt](../../docs/competence/C06_CHECKS.json). Keep all outputs outside the repository:

1. Prepare a scratch harness with `tools/ProfileAudit/run.sh <managed> <mod.dll> <harness> <strict.txt> <export roots...>`. The frozen supplied corpus has one known Unity-bound deserialization failure; the strict run returns 1 and generates no verdicts. Check the failure's exact fingerprint before any allowance.
2. Build this project separately:

   ```sh
   dotnet build tests/ToggleCosts.Native -c Release -p:ManagedPath=/path/to/managed \
     -p:ModDll=/path/to/CompanionAI_Pathfinder.dll -o /scratch/toggle-tests
   ```

3. Copy **only** `ToggleCosts.Native.dll`, `ToggleCosts.Native.deps.json` and `ToggleCosts.Native.runtimeconfig.json` from that output into the prepared harness. Keep its rewritten native dependencies; do not overwrite them with original engine DLLs. Ensure its mod DLL still matches the build/package receipt.
4. In the harness, run `dotnet ToggleCosts.Native.dll --allow-failures 1 <the same export roots...> <diagnostic.txt>`. This is explicitly **PARTIAL**, retains the failed input, runs the ordinary reader checks and appends the toggle checks. Missing mandatory fixtures or failed checks return nonzero; no positive check is silently skipped.

No installed-mod initializer, native actor, resource spend, toggle switch or command is executed. TTT's packed-cost arithmetic is a fixture of the inspected installed-code contract, not a live provider initialization test. The game build and runtime patch set remain separate validation. Never deploy the harness or its rewritten dependencies.
