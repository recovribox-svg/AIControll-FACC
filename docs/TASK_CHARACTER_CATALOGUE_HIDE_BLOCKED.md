# Character catalogue: Hide blocked

Implemented on `codex/character-catalogue-hide-blocked`, starting at remote development commit
`d4ac1939ac5fdda8aef0a9d83c79cf37cf531057` (5 November 2026).

## Evidence and integration

The supplied prompt described the older blocklist task. Its UI map still matches the current development tree:
`UI/AbilityBlocklistUI.cs` contains the character catalogue and a separate shared-list control;
`Settings/ModSettings.cs` contains its EN/KR localization dictionary. Existing blocklist GUID matching,
shared/character ownership, variant/conversion and held-touch rules remain in `UserAbilityPolicy` and
`UserAbilityRules`; this addition does not modify either file or any combat/buffing route.

- **Hide blocked** is beside **Show blocked only**, using the same checkbox-style label button.
- Enabling either clears the other. Turning Hide off restores the existing search-filtered catalogue with its
  entries still blocked; enabling Show blocked only also makes hidden blocked rows accessible again.
- Both visibility flags are transient character-panel state, following the existing filter's lifetime. No serialized
  setting, blocklist entry or schema field was added. The edit in ModSettings is one localization entry only:
  English `Hide blocked`; Korean `차단된 항목 숨기기`.
- View construction uses the existing exact/shared/character/family row status. Hide filters the already-built rows
  before the existing page count and page clamp. Search and both filter states reset the page on a view rebuild;
  list edits still invalidate it through the existing policy revision.
- Filtering remains deferred to Layout events. Refresh still rebuilds the catalogue on the next Layout, and toggling
  a visibility filter does not rescan blueprints or rediscover abilities.
- Existing Unblock buttons and missing-entry recovery rows remain in place. The shared-list widget is separate, so it
  receives no new control. Its behavior and the policy check path are unchanged; there is no added allocation on that path.

## Verification

- Before and after: `dotnet test tests/CompanionAI.Tests` — 359 passed, 0 failed, 0 skipped.
- Release build against the supplied real game DLLs — 0 warnings, 0 errors.
- `git diff --check` — passes.
- SigDump compared with the original build of the same development base — 0 external signatures added or removed.
- Independent source review covered mutual exclusion, off-state equivalence, Layout/Repaint consistency, search,
  Refresh, list revisions, page clamping and recovery routes; no blocker found.
- No new GUI test was added: BuildView constructs an impure panel cache, and no new pure filter function was extracted.
  Existing policy/serialization tests remain green. These results do not establish live GUI interaction.
- Built DLL SHA-256: `1f0e6fa9e712c74bb0ef78257e7ae59ef1ed4d4893f94feb56f8340a3ee97421`.
- In-game UI replay is pending. No version metadata or existing release/test package was changed. This task is
  independent of the unmerged Kinetic Sharpshooter PR #18 and does not bundle its changes.

## Focused in-game check

1. Open a character's existing catalogue. With both filters off, compare names/order/search and page size with the
   development build. Verify the English and Korean labels.
2. Block an ability, a toggle and a variant family, including a shared entry. Enable Hide: all matching blocked rows
   disappear while their blocklist state remains. Search for one of those names: the blocked match stays hidden.
3. Enable Show blocked only: Hide must turn off and blocked rows must appear with their existing Unblock buttons.
   Enable Hide again: Show blocked only must turn off. Toggle Hide off: all matching rows return, still blocked.
4. Begin on a later page and enable Hide or change search. Expect the first valid filtered page, or the existing
   no-rows message when nothing matches; no empty page caused by hidden rows. Block/unblock entries and verify
   revision changes refresh and clamp the view. Remove a shared block in the separate shared panel and verify
   a matching row returns under Hide.
5. Refresh after an equipment/ability change, switch/reopen character panels, and verify both filters retain the same
   transient lifetime as the old filter. Missing-entry removal and shared-list removal must still work.
