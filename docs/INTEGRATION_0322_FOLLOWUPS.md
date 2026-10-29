# Integration of the v0.3.22 follow-ups (PR #13–#15) and main update

**Current handover, 29 October 2026 (Claude).** Read this before resuming work. Earlier handovers
(`RELEASE_0_3_22_HANDOVER.md`, `RELEASE_0322_*_REVIEW.md`, `INTEGRATION_HANDOVER.md`, `KINETICIST_*`,
`DECISION_CONTEXT_PLAYTEST.md`) describe their own time; where they say "pending", "unmerged" or "do not integrate",
this document supersedes them.

## Authorization and scope

The author (MonsieurAndroid) authorized Claude to review and merge Codex's pending PR chain into
`claude/dreamy-pasteur-b9h3rn` and then bring `main` to the resulting reviewed revision. The authorization excludes
publishing a GitHub Release, creating or moving tags, and replacing any published release asset: the author handles
GitHub Releases. No tag was created or moved, and `release/` is unchanged.

Out of scope and **not** resumed: movement, target commitment, retreat/back-off, pathfinding and stuck-cancellation
changes (these need a discussion with the author first); the reach/Grease and Seelah wall cases stay
**diagnostics only**; Ardub/body-blocking integration stays on the backburner; the broad kineticist/performance
experiment withdrawn in `V0_3_22_POSTMORTEM.md` stays withdrawn.

## What is integrated

Starting point: development `0a8b8f2` (the commit tagged `v0.3.22`, the published release). Each PR was merged with
an ordinary merge commit, in dependency order:

| PR | Head | Merge commit | Runtime change |
| --- | --- | --- | --- |
| [#13](https://github.com/recovribox-svg/AIControll-FACC/pull/13) | `8bb6a3c` | `f0a44df` | `ModdedDamageReduction`: when the vanilla `UnitPartDamageReduction` is absent, ask an already installed TabletopTweaks-Core `TTUnitPartDamageReduction` for its own immunity/estimate (lazy reflection on exact public signatures, native `Get<T>` only, no copied tables). Fallback unchanged when TTT is absent or its API differs. `[Reach]` rows: diagnostics only. |
| [#14](https://github.com/recovribox-svg/AIControll-FACC/pull/14) | `9301356` | `1d49979` | `CastOutcome.Settle`: an ended, never-executed, unacted command is discarded even if an unrelated effect (an old Grease save) set broad Evidence. `DamageObserver.NoteEstimate`: a packet whose native attack roll missed (Holy Water splash) is not a direct-hit calibration sample; defense observations of every packet are unchanged. |
| [#15](https://github.com/recovribox-svg/AIControll-FACC/pull/15) | `9847561` | `ecabeb4` | `CombatMath.ArcaneCastingChance`: `RuleCalculateArcaneSpellFailureChance` (the spellbook UI query), gated by `AbilityData.IsAffectedByArcaneSpellFailure`, applied once to expected spell benefit before provoke/move costs, and once to infusion delivery and Progressive viability. `[Walk]` / stuck navigation rows: diagnostics only. |

The source tree of `ecabeb4` is identical to PR #15's head. Everything after it on development is documentation and the
test package below. `main` was fast-forwarded to the same commit as development (history preserved, no force-push).

## What Claude actually checked (29 October)

- **Code review:** the incremental runtime diff of each PR read in full against the scope rules above. No toggle,
  burn, infusion, healing, dispatcher-behaviour, Selective-Grease, movement or budget code is changed; the dispatcher and
  real-time controller changes are log lines only. TTT absence returns the old path; mismatched TTT API logs once and
  returns the old path. Legitimate failed saves, misses, concentration and arcane failures stay legitimate outcomes.
- **Unit tests:** `dotnet test tests/CompanionAI.Tests` — 164 passed, 0 failed, 0 skipped (on PR #15's head and on
  the merged revision).
- **Release build** against the game's `Wrath_Data/Managed` and UnityModManager references (TabletopTweaks not
  available): 0 warnings, 0 errors.
- **External references** (`tools/SigDump`, new build vs the published v0.3.22 DLL): five new game members, all
  resolved by the real-reference build. The two private `UnitMovementAgent` fields read by reflection
  (`m_NextPointIndex`, `m_Destination`) exist in the decompiled game source.
- **Blueprint readers:** the repository's `tools/ProfileAudit` **does not compile against the current source**. It
  still mirrors the withdrawn kineticist engine (`KineticModifierNote`, `ForKineticInfusion`,
  `BuffPartKind.RemovesConcealment`, the `--kinetic-checks` fixtures); this predates these PRs. Claude ran the
  v0.3.21-era harness (`tools/` at `2c04723`) on the author's blueprint dumps for both the v0.3.22 build and the
  merged build: 44,644 blueprints loaded, 1 known failure (`AchaekekSacredWeaponFeature`), 4,801 rows, 0 reader
  errors, **0 differing rows**. This is a regression comparison of the ordinary readers only. That harness does not
  mirror the scoped infusion selection, so its toggle verdicts for substance infusions are outdated.
- **Package:** ZIP contents, SHA-256 and module MVID verified after packing (below).

**Inherited from Codex, not reproduced by Claude** (those harnesses exist only in Codex's workspace): the 39 arcane-chance
guards, 64 Settle vectors / 406 assertions, 20 missed-sample assertions, the TabletopTweaks-Core 0.7.15 binding /
absent / mismatched-shape checks (they need the TTT DLL), the kinetic/native ProfileAudit checks and the 1,220-row
partial reader audit. Recommendation A (below, done the same day) moved the rules those harnesses checked into the
repository's unit tests; the real TTT 0.7.15 binding still needs the TTT DLL or a game session.

## Current package

[`test-builds/AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.zip`](../test-builds/AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.zip?raw=true)
— direct link:
<https://github.com/recovribox-svg/AIControll-FACC/raw/claude/dreamy-pasteur-b9h3rn/test-builds/AI-Control-FACC-dev-integrated-ecabeb4-UMM-test.zip>

| Field | Value |
| --- | --- |
| Runtime source | `ecabeb40141ede99c2f118d99f7ffa0667d746e4` |
| UMM version | 0.3.22 (unchanged; the same number as the published release) |
| Module MVID | `2cc6eb92-af79-4a1c-a65d-aad66d8ef315` |
| Startup marker | `[Build] v0.3.22 module=2cc6eb92-af79-4a1c-a65d-aad66d8ef315` |
| ZIP SHA-256 | `34618d191de35a16aa2a3006bad73bd5e6742c9fcc5e92d2d57c1da813126423` |
| DLL SHA-256 | `06a274047b16a68841db613360848e31cfe4983fd6b9d33d5b5e5d73f21c70a0` |
| Contents | `CompanionAI_Pathfinder.dll`, `Info.json` only |

The code is the same as Codex's be0da2c test, rebuilt from the merged revision, so the bytes and MVID differ from
Codex's ZIP. The version was not changed: no release is being made, and the startup MVID identifies the build.

## Historical assets (unchanged)

- `release/CompanionAI_Pathfinder_v0.3.22.zip` (tag `v0.3.22`, MVID `52bb4a21-9f8f-459f-93db-5c942e531fa2`) is the
  published release. It does **not** contain PR #13–#15. Older `release/` ZIPs and checksums are untouched.
- All earlier `test-builds/` ZIPs (infusion-value, test1–test5, ea20a79, 649ed23, be0da2c), their identity files and
  checksums are kept byte-for-byte. Tags `v0.3.22-test*` and `*-scoped` are historical references only. Some point
  at commits that did not produce the DLL a tester installed (see `V0_3_22_POSTMORTEM.md`).

## What `main` now carries beyond the published release

Fast-forwarding `main` brought these onto the public default branch, as the author authorized:

1. **PR #13–#15 runtime code awaiting gameplay replay**, in particular the arcane-failure valuation (PR #15) and the
   TTT preview's live behaviour beyond the logs already reviewed.
2. **Temporary diagnostics:** `[Reach]` and the extended `[Walk]` / stuck-navigation rows. They collect evidence for
   the movement discussion and should be reviewed (kept, trimmed or removed) before the next release.
3. **Verification tooling:** `tools/ProfileAudit` targeted the withdrawn engine and did not build; repaired by
   Recommendation A (below) on development. `main` still carries the stale copy until the author next moves it.
4. **Test archives** under `test-builds/` and historical documents of withdrawn work. CONTRIBUTING.md's "main: no test
   builds" convention is superseded for this update by the author's instruction; published releases are identified
   by their tags and `release/` ZIPs, not by `main`.

## Awaiting gameplay (both combat modes)

Use the current ZIP and confirm the startup marker. Send complete AI logs, including startup, plus the native
CombatLog.

1. **Arcane failure:** `[CastChance]` notes and changed values for affected spells (a 20% failure gives factor 0.8).
   Unaffected sources (items, held touch, divine casters per the game's own gate) keep factor 1. Random failures still
   occur and must not blacklist a spell.
2. **Observers:** an approaching spell interrupted before it casts, with an old Grease ticking, is "not counted" with no
   (1/2) strike. A missed Holy Water splash produces no direct-hit `[DmgCheck]`. Genuine silent failures and
   concentration failures keep their handling.
3. **TTT:** with the DR rework active, `TTT estimate` / `TTT immunity` routes. With TTT absent or its rework off, the
   old routes.
4. **Regressions:** automatic Burning infusion (DC, burn, off/restoration), toggle management off and player-owned
   toggles, group Sleep, healing and revives, Selective Grease safety.
5. **Diagnostics:** the reach/Grease scene and Seelah's combat20 cloud escape, with the new `[Reach]`, `[Walk]` and stuck
   snapshot rows. Movement is unchanged; these rows only collect evidence.
6. **Smoothness:** the author's last rating was 7.5/10 on a budget laptop, unchanged from the preceding build. No speedup
   or regression is claimed from unmatched encounter averages.

## Recommendation A — done (29 October, development only)

The author asked for Recommendation A. It is on development; `main` was **not** moved (no new authorization), and no tag
or release was touched.

**ProfileAudit builds again against the current source.**
- The withdrawn engine's checks are gone: `KineticistChecks`, `DecisionReadChecks`, `ConditionContextChecks`, the
  `GatherAbilities` and kinetic-talent special cases, `BuffPartKind.RemovesConcealment` and the `Time.frameCount` stub.
  `--kinetic-checks` now fails and explains why.
- Substance infusions are now read through the engine's own scoped reader (`TacticalEngine.DescribeInfusion`, by
  reflection). A renamed reader or field makes the audit fail; it never silently reports everything as supported.
- Full run on the author's dumps: 44,644 loaded, 1 known failure, 4,801 rows, 0 reader errors.
  - 18 infusions supported. Two withheld: Chain Arrows ("delivery-changing infusion has no supported read-only
    recipient query") and Unraveling ("dispel has no read-only native success query"). This matches Codex's "18
    recognized / two withheld".
  - Compared with the v0.3.21-era harness on the ecabeb4 build, only those 20 infusion rows differ. They were
    "toggle not managed: a modifier of another action (AA-4)"; every other row is identical.

**The workspace-only rules are now pure helpers with unit tests** (`dotnet test tests/CompanionAI.Tests`: 218 passed,
was 164):

| Rule (PR) | Helper | Tests |
| --- | --- | --- |
| Settle discards an uncast order before reading Evidence (#14) | `Tactics/ObserverRules.Settle` → Wait / NotCast / Worked / Inspect | `ObserverRulesTests` |
| A missed native attack roll is no calibration sample (#14) | `ObserverRules.SkipCalibrationSample` | `ObserverRulesTests` |
| The arcane chance discounts once: spell value, infusion delivery, Progressive viability (#15) | `ValueModel.ArcaneCastingChance`, `DeliveredBenefit`, `InfusionDeliveryFactor`, `DeliveredSuccessChance` (`Tactics/ValueModel.Delivery.cs`) | `DeliveryChanceTests` |
| Optional-provider binding: absent / matching / mismatched API (#13) | `Tactics/OptionalApiBinding` (used by `ModdedDamageReduction.Bind`) | `OptionalApiBindingTests`: providers emitted at test time, no TTT DLL |

- **Behaviour.** The engine, `CastOutcome`, `DamageObserver`, `CombatMath` and `ModdedDamageReduction` call these
  helpers instead of inline code, with the same order of operations. All inputs are probabilities already in [0, 1];
  the helpers clamp them, and a NaN now counts as 0 instead of propagating.
- **One real gap found and closed.** The binding's "exact signature" lookup used `Type.GetMethod`, whose default binder
  accepts widened primitives. A TTT build whose `EstimateDamage` took `double` would have passed the check.
  - In the game this was harmless: compiling the delegate would then throw inside `Bind`, which logs once and keeps the
    old fallback.
  - The check now compares the parameter types exactly, and the fixture covers it.
- **Builds.** Real-reference Release build: 0 warnings, 0 errors. `tools/SigDump` against the ecabeb4 build: no
  external reference added or removed.
- **No new test package.** The current ZIP (ecabeb4) remains the one to play; the changes after it are tooling and
  behaviour-preserving extractions.

## Recommended next work (prioritized)

1. **A — Reproducible verification. Done; see the section above.**
   - Restore a `tools/ProfileAudit` that builds against the current source: remove the withdrawn-engine checks and
     mirror the scoped infusion selection.
   - Move the workspace-only checks into the repository where they need no proprietary files: Settle's discard gate,
     the missed-roll calibration predicate and the "delivery factor applied once" rule as pure helpers with unit tests,
     and a synthetic-assembly fixture for the optional-provider binding (absent / matching / mismatched API).
   - *Benefit:* every later claim becomes checkable by either assistant. *Risk:* low (tooling and small pure
     extractions). *Evidence needed:* none beyond the current dumps and game references.
2. **E — Evidence-led movement (passive, in parallel).** The new diagnostics are already in the current ZIP. The author
   replays the reach/Grease and combat20 scenes. Any correction is proposed to the author before coding.
   - *Benefit:* settles the root cause. *Risk:* none until a change is proposed. *Evidence needed:* those replays.
3. **D — Observer attribution.** `CastOutcome.Mark` from `RuleSavingThrow` still has no source correlation (PR #14 fixed
   only the uncast case). Correlate evidence by the rule's ability/context, and separate successful-hit secondary
   splash, using native CombatLog cases.
   - *Benefit:* avoids wrong failure learning on executed casts. *Risk:* medium (genuine silent-failure protection must
     stay). *Evidence needed:* CombatLog cases with overlapping effects.
4. **B — Controlled performance.** Same save, settings, mod list and confirmed MVID.
   - Split cold profile construction (the first decision built ~61 profiles, ~0.9 s), warm ability/weapon appraisal and
     allocation per stage before touching budgets.
   - Collection-count overlap does not identify the allocator.
   - *Benefit:* targets the remaining occasional stalls. *Risk:* low for measurement, behaviour changes only after
     evidence. *Evidence needed:* matched sessions.
5. **C — Kineticist acceptance matrix.** Only Burning is gameplay-validated. Build a supported / untested / unsupported
   table per infusion family, element, native cost, immunity, replacement and player ownership from the blueprint dumps
   and the native predicates, then targeted replays. No combinatorial search.
   - *Benefit:* honest coverage. *Risk:* low. *Evidence needed:* a kineticist save with more infusions.
